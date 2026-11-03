using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class MaintainedStageTests : ProvingGroundsTest
    {
        public enum Stopper
        {
            Last,
            All
        }

        /// <summary>
        /// Maintaining a stage moved the sequence on, and moving on cleaned the stage it left, whose tasks were
        /// still running. Clean belongs at the end of a stage, wherever that comes.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_J_A_maintained_stage_is_cleaned_when_it_ends() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Maintaining")
                .Task(new ProbeSequenceTask(World, "Maintained", frames: 8))
                .Task(new ProbeSequenceTask(World, "Next", frames: 2))
                .Build());
            var relay = Run(sequence);
            await Frames.Step(2);

            Assert.That(sequence.Inject(SkipAndMaintainInjection.Instance), "the stage is maintained");
            await Frames.Step();

            Assert.That(Ledger.Prepares("Next"), Is.EqualTo(1), "the next stage has started");
            Assert.That(Ledger.Cleans("Maintained"), Is.EqualTo(0), "and the maintained one is still working");

            await Frames.Until(() => relay.State == EProcessState.Terminated, 12, "the sequence finishes");

            Assert.That(Ledger.Cleans("Maintained"), Is.EqualTo(1), "cleaned once, at its end");
            Assert.That(Ledger.Violations(), Is.Empty);
        });

        /// <summary>
        /// Interrupted, the sequence cleaned all it had at once, a maintained stage's tasks with the rest while
        /// they were still unwinding. It waits for them now, and each stage cleans its own.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_J_Interrupting_cleans_a_maintained_stage_once_it_stops() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Interrupted While Maintaining")
                .Task(new ProbeSequenceTask(World, "Maintained", frames: 1000))
                .Task(new ProbeSequenceTask(World, "Current", frames: 1000))
                .Build());
            var relay = Run(sequence);
            await Frames.Step(2);

            sequence.Inject(SkipAndMaintainInjection.Instance);
            await Frames.Step(2);

            sequence.Interrupt();
            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "the sequence ends");

            Assert.That(Ledger.Cleans("Maintained"), Is.EqualTo(1));
            Assert.That(Ledger.Cleans("Current"), Is.EqualTo(1));
            Assert.That(Ledger.Violations(), Is.Empty, "nothing cleaned while it ran");
        });

        /// <summary>
        /// Past the last stage there's nothing to maintain, and the sequence counted one all the same, so it
        /// waited for ever on a stage that wasn't running.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_J_Only_a_running_stage_can_be_maintained() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Maintained Last")
                .Task(new ProbeSequenceTask(World, "Last", frames: 5))
                .Build());
            var relay = Run(sequence);
            await Frames.Step(2);

            Assert.That(sequence.Inject(SkipAndMaintainInjection.Instance), "the last stage is maintained");
            Assert.That(sequence.Inject(SkipAndMaintainInjection.Instance), Is.False, "and then there's nothing to maintain");

            await Frames.Until(() => relay.State == EProcessState.Terminated, 8, "the sequence ends with its stage");
        });

        /// <summary>Jumping back to a stage that's still maintained ran its tasks a second time while they ran.</summary>
        [UnityTest]
        public IEnumerator TS_J_A_maintained_stage_cant_be_jumped_to() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Jumped Back")
                .Task(new ProbeSequenceTask(World, "Maintained", frames: 1000))
                .Task(new ProbeSequenceTask(World, "Current", frames: 30))
                .Build());
            Run(sequence);
            await Frames.Step(2);

            sequence.Inject(SkipAndMaintainInjection.Instance);
            await Frames.Step();

            Assert.That(sequence.Inject(new JumpToStageInjection(0)), Is.False, "the jump is refused");
            await Frames.Step(2);

            Assert.That(Ledger.Prepares("Maintained"), Is.EqualTo(1), "the stage runs once");
            Assert.That(Ledger.Cleans("Current"), Is.EqualTo(0), "and the current one goes on");
        });

        /// <summary>
        /// A stage left the active list only from the top, so one maintained and ending under a later stage
        /// stayed on it, and a critical one held the critical section for the rest of the run.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_J_A_maintained_critical_stage_lets_go_when_it_ends() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Critical Then Maintained")
                .Stage(stage =>
                {
                    stage.WithCriticalAllowedInjections(typeof(SkipAndMaintainInjection));
                    stage.Task(new ProbeSequenceTask(World, "Critical", frames: 3));
                })
                .Task(new ProbeSequenceTask(World, "After", frames: 30))
                .Build());
            Run(sequence);
            await Frames.Step();

            Assert.That(sequence.Inject(SkipAndMaintainInjection.Instance), "the critical stage is maintained");
            await Frames.Until(() => Ledger.Cleans("Critical") == 1, 6, "and ends");

            Assert.That(sequence.IsCriticalSection, Is.False, "the critical section is over");
        });

        /// <summary>A timer applied its injection to whatever stage was current, not the stage it belonged to.</summary>
        [UnityTest]
        public IEnumerator TS_K_A_maintained_stages_timeout_stops_that_stage() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Timed Out While Maintained")
                .Stage(stage =>
                {
                    stage.Task(new ProbeSequenceTask(World, "Maintained", frames: 1000));
                    stage.WithTimeout(0.1f);
                })
                .Task(new ProbeSequenceTask(World, "Current", frames: 30))
                .Build());
            Run(sequence);
            await Frames.Step(2);

            sequence.Inject(SkipAndMaintainInjection.Instance);
            await Frames.Seconds(0.2f);

            Assert.That(Ledger.Cleans("Maintained"), Is.EqualTo(1), "its own timeout ended it");
            Assert.That(Ledger.Cleans("Current"), Is.EqualTo(0), "and the current stage goes on");
        });

        /// <summary>A maintained stage's own condition skipped whatever stage was current, as its timers did.</summary>
        [UnityTest]
        public IEnumerator TS_K_A_maintained_stages_condition_skips_that_stage() => Live(async () =>
        {
            bool skip = false;
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Skipped While Maintained")
                .Stage(stage =>
                {
                    stage.Task(new ProbeSequenceTask(World, "Maintained", frames: 1000));
                    stage.SkipWhen(_ => skip);
                })
                .Task(new ProbeSequenceTask(World, "Current", frames: 30))
                .Build());
            Run(sequence);
            await Frames.Step(2);

            sequence.Inject(SkipAndMaintainInjection.Instance);
            await Frames.Step();
            skip = true;
            await Frames.Step(3);

            Assert.That(Ledger.Cleans("Maintained"), Is.EqualTo(1), "its own condition ended it");
            Assert.That(Ledger.Cleans("Current"), Is.EqualTo(0), "and the current stage goes on");
        });

        /// <summary>
        /// A timer waited on a source that was disposed and never cancelled, so it outlived what it timed and
        /// fired into whatever came next: here, the next round of a repeating sequence.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_AD_A_timer_ends_with_what_it_timed([Values] bool stageTimer) => Live(async () =>
        {
            int timeouts = 0;
            var builder = TaskSequenceBuilder.Create("Timed Rounds").WithRepeat(true);
            if (stageTimer)
            {
                builder.Stage(stage => stage.WithTimeout(0.3f).OnTimeout(() => timeouts++).Delay(0.2f));
            }
            else
            {
                builder.WithTimeout(0.3f).OnTimeout((_, _) => timeouts++).Delay(0.2f);
            }

            Run(new TaskSequence(builder.Build()));
            await Frames.Seconds(0.7f);

            Assert.That(timeouts, Is.EqualTo(0), "no round lasts long enough to time out");
        });

        /// <summary>The stage being stopped was picked from all the running stages, the current one among them.</summary>
        [UnityTest]
        public IEnumerator TS_L_Stopping_maintained_stages_leaves_the_current_one([Values] Stopper stopper) => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Nothing Maintained")
                .Task(new ProbeSequenceTask(World, "Current", frames: 30))
                .Task(new ProbeSequenceTask(World, "Next"))
                .Build());
            Run(sequence);
            await Frames.Step(2);

            ISequenceInjection stop = stopper == Stopper.Last ? StopMaintainedLastInjection.Instance : StopMaintainedAllInjection.Instance;
            Assert.That(sequence.Inject(stop), Is.False, "there's nothing to stop");
            await Frames.Step(3);

            Assert.That(Ledger.Cleans("Current"), Is.EqualTo(0), "the current stage goes on");
        });

        /// <summary>An index was checked against how many stages were running, not which.</summary>
        [UnityTest]
        public IEnumerator TS_L_A_late_stage_can_be_stopped_by_its_index() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Stopped By Index")
                .Do(_ => { })
                .Do(_ => { })
                .Task(new ProbeSequenceTask(World, "Maintained", frames: 1000))
                .Task(new ProbeSequenceTask(World, "Current", frames: 30))
                .Build());
            Run(sequence);
            await Frames.Step(3);

            Assert.That(sequence.Inject(SkipAndMaintainInjection.Instance), "the stage is maintained");
            await Frames.Step();

            Assert.That(sequence.Inject(new StopMaintainedAtIndexInjection(2)), "the stop is accepted");

            await Frames.Until(() => Ledger.Cleans("Maintained") == 1, 3, "stage 2 stops");
            Assert.That(Ledger.Cleans("Current"), Is.EqualTo(0), "and the current stage goes on");
        });

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            return relay;
        }
    }
}
