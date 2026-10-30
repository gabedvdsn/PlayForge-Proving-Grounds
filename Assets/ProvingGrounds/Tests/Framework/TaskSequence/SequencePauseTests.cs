using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A paused sequence holds where it is (Decision 5). No stage starts or ends, its timers stop, and what it
    /// runs times itself through the library so that stops too. It used to say it had paused and keep going.
    /// </summary>
    public class SequencePauseTests : ProvingGroundsTest
    {
        public enum Pauser
        {
            Relay,
            Control
        }

        public enum Timer
        {
            StageTimeout,
            StageMaxDuration,
            SequenceTimeout,
            SequenceMaxDuration
        }

        public enum TimedAbilityTask
        {
            Delay,
            Channel
        }

        [UnityTest]
        public IEnumerator TS_Y_A_paused_delay_stops_counting([Values] Pauser pauser) => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Next Stage");
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Delayed").Delay(0.5f).Task(next).Build()));

            await Frames.Seconds(0.25f);
            Pause(relay, pauser);
            await Frames.Seconds(1f);
            Assert.That(Ledger.Prepares("Next Stage"), Is.EqualTo(0), "the delay held");

            Resume(relay, pauser);

            int taken = await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 30, "the delay runs out");
            Assert.That(taken, Is.GreaterThan(10), "with the quarter second it had left");
        });

        /// <summary>A task that doesn't time itself finishes when it finishes, but its stage waits for the resume.</summary>
        [UnityTest]
        public IEnumerator TS_Y_No_stage_ends_or_starts_during_a_pause([Values] Pauser pauser) => Live(async () =>
        {
            var first = new ProbeSequenceTask(World, "First Stage", frames: 3);
            var next = new ProbeSequenceTask(World, "Next Stage");
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Staged").Task(first).Task(next).Build()));

            await Frames.Step();
            Pause(relay, pauser);
            await Frames.Step(10);

            Assert.That(Ledger.Cleans("First Stage"), Is.EqualTo(0), "its stage hasn't ended");
            Assert.That(Ledger.Prepares("Next Stage"), Is.EqualTo(0), "and the next hasn't started");

            Resume(relay, pauser);

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 5, "the sequence goes on");
        });

        [UnityTest]
        public IEnumerator TS_Y_A_timer_doesnt_run_out_during_a_pause([Values] Timer timer) => Live(async () =>
        {
            const float Seconds = 0.25f;
            var next = new ProbeSequenceTask(World, "Next Stage");

            var builder = TaskSequenceBuilder.Create("Timed");
            if (timer == Timer.SequenceTimeout) builder.WithTimeout(Seconds, SkipStageInjection.Instance);
            if (timer == Timer.SequenceMaxDuration) builder.WithMaxDuration(Seconds, SkipStageInjection.Instance);

            builder.Stage(stage =>
            {
                stage.Delay(100f);
                if (timer == Timer.StageTimeout) stage.WithTimeout(Seconds);
                if (timer == Timer.StageMaxDuration) stage.WithMaxDuration(Seconds, SkipStageInjection.Instance);
            });

            var relay = Run(new TaskSequence(builder.Task(next).Build()));

            await Frames.Seconds(0.1f);
            relay.Pause();
            await Frames.Seconds(1f);
            Assert.That(Ledger.Prepares("Next Stage"), Is.EqualTo(0), "the timer held");

            relay.Unpause();

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 20, "and then runs out");
        });

        /// <summary>The library's helpers get only a token, and that token is enough to hold them.</summary>
        [UnityTest]
        public IEnumerator TS_Y_A_library_helper_holds_with_its_sequence([Values] Pauser pauser) => Live(async () =>
        {
            float value = 0f;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Lerped")
                .Task(async (CancellationToken token) => await SequenceTaskLibrary.LerpFloat(0f, 1f, 0.5f, v => value = v, token))
                .Build()));

            await Frames.Seconds(0.25f);
            Pause(relay, pauser);
            float held = value;

            await Frames.Seconds(1f);
            Assert.That(value, Is.EqualTo(held), "it held");

            Resume(relay, pauser);

            await Frames.Until(() => relay.State == EProcessState.Terminated, 30, "it finishes");
            Assert.That(value, Is.EqualTo(1f));
        });

        [UnityTest]
        public IEnumerator TS_Y_A_paused_ability_holds_its_timed_tasks([Values] Hub hub, [Values] TimedAbilityTask timed) => Live(async () =>
        {
            AbstractAbilityTask task = timed == TimedAbilityTask.Delay
                ? new DelayTask { DelayMilliseconds = 500 }
                : new ChannelAbilityTask { ChannelDuration = 0.5f };

            var ability = Make.Ability("Timed", Make.Stage(task), Make.Stage(new ProbeAbilityTask(World, "Next Stage")));
            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Seconds(0.25f);
            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Seconds(1f);
            Assert.That(Ledger.Prepares("Next Stage"), Is.EqualTo(0), "the task held");

            World.Control.SetControlState(EProcessControlState.Ready);

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 30, "the ability goes on");
        });

        [UnityTest]
        public IEnumerator TS_Y_A_paused_chain_holds_between_sequences() => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Next Sequence");
            var chain = new TaskSequence(TaskSequenceBuilder.Create("First").Delay(0.1f).Build())
                .Then(new TaskSequence(TaskSequenceBuilder.Create("Second").Task(next).Build()));

            ProcessControl.Register(chain, out var relay);
            World.Own(relay);

            await Frames.Seconds(0.05f);
            relay.Pause();
            await Frames.Seconds(0.5f);
            Assert.That(Ledger.Prepares("Next Sequence"), Is.EqualTo(0), "the chain held");

            relay.Unpause();

            await Frames.Until(() => Ledger.Cleans("Next Sequence") == 1, 20, "the chain goes on");
        });

        [UnityTest]
        public IEnumerator A_sequence_can_be_paused_more_than_once() => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Next Stage");
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Twice").Delay(0.5f).Task(next).Build()));

            await Frames.Seconds(0.1f);

            for (int i = 0; i < 2; i++)
            {
                relay.Pause();
                await Frames.Seconds(0.5f);
                relay.Unpause();
                await Frames.Seconds(0.1f);
            }

            Assert.That(Ledger.Prepares("Next Stage"), Is.EqualTo(0), "0.3 s of its 0.5 s have gone");

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 20, "the rest runs out");
        });

        /// <summary>A Terminate is an interrupt to a sequence, which a pause in the same frame doesn't undo.</summary>
        [UnityTest]
        public IEnumerator A_terminate_on_a_sequence_survives_a_control_pause() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Ending", frames: 1000);
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Ending").Task(task).Build()));
            await Frames.Step(2);

            relay.Terminate();
            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step(3);

            World.Control.SetControlState(EProcessControlState.Ready);

            await Frames.Until(() => relay.State == EProcessState.Terminated, 3, "it ends");
            Assert.That(Ledger.Prepares("Ending"), Is.EqualTo(1), "without running again");
        });

        /// <summary>An interrupt during a pause ends the ability, and nothing is left waiting on the held sequence.</summary>
        [UnityTest]
        public IEnumerator An_ability_interrupted_during_a_pause_can_run_again([Values] Hub hub) => Live(async () =>
        {
            var task = new ProbeAbilityTask(World, "Long", frames: 20);
            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Interrupted", Make.Stage(task)), 1, out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(2);

            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step();

            abilities.Inject(index, InterruptSequenceInjection.Instance);
            await Frames.Step(3);

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step(3);

            Assert.That(Ledger.Cleans("Long"), Is.EqualTo(1), "its task is cleaned up");
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "and it activates again");

            await Frames.Until(() => Ledger.Cleans("Long") == 2, 30, "and runs to its end");
        });

        /// <summary>An ability activated during a pause waits with everything else.</summary>
        [UnityTest]
        public IEnumerator An_ability_activated_during_a_pause_starts_when_it_ends([Values] Hub hub) => Live(async () =>
        {
            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Late", Make.Stage(new ProbeAbilityTask(World, "Late Task"))), 1, out int index);

            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step();

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(5);
            Assert.That(Ledger.Prepares("Late Task"), Is.EqualTo(0), "it waits");

            World.Control.SetControlState(EProcessControlState.Ready);

            await Frames.Until(() => Ledger.Cleans("Late Task") == 1, 10, "and then runs");
        });

        /// <summary>Skipping is asked for from outside, so it lands during a pause, but what follows it waits.</summary>
        [UnityTest]
        public IEnumerator A_stage_skipped_during_a_pause_holds_the_next_one() => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Next Stage");
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Skipped").Delay(100f).Task(next).Build());
            var relay = Run(sequence);
            await Frames.Step(2);

            relay.Pause();
            Assert.That(sequence.SkipCurrentStage(), "the skip is accepted");
            await Frames.Step(5);

            Assert.That(Ledger.Prepares("Next Stage"), Is.EqualTo(0), "the next stage waits for the resume");

            relay.Unpause();

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 5, "and then runs");
        });

        /// <summary>The same a level up: a sequence skipped in a paused chain doesn't start the next one.</summary>
        [UnityTest]
        public IEnumerator A_sequence_skipped_during_a_chains_pause_holds_the_next_one() => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Next Sequence");
            var chain = new TaskSequence(TaskSequenceBuilder.Create("First").Delay(100f).Build())
                .Then(new TaskSequence(TaskSequenceBuilder.Create("Second").Task(next).Build()));

            ProcessControl.Register(chain, out var relay);
            World.Own(relay);
            await Frames.Step(2);

            relay.Pause();
            relay.TryGetProcess<TaskSequenceProcess>(out var process);
            Assert.That(process.SkipCurrentSequence(), "the skip is accepted");
            await Frames.Step(5);

            Assert.That(Ledger.Prepares("Next Sequence"), Is.EqualTo(0), "the next sequence waits for the resume");

            relay.Unpause();

            await Frames.Until(() => Ledger.Cleans("Next Sequence") == 1, 5, "and then runs");
        });

        /// <summary>A chain ended while paused doesn't carry the pause into its next run.</summary>
        [UnityTest]
        public IEnumerator A_chain_ended_while_paused_runs_again() => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Chained");
            var chain = new TaskSequence(TaskSequenceBuilder.Create("Only").Delay(0.1f).Build())
                .Then(new TaskSequence(TaskSequenceBuilder.Create("Then").Task(next).Build()));

            ProcessControl.Register(chain, out var first);
            World.Own(first);
            await Frames.Step(2);

            first.Pause();
            first.TerminateImmediate();
            await Frames.Step(2);

            ProcessControl.Register(chain, out var second);
            World.Own(second);

            await Frames.Until(() => Ledger.Cleans("Chained") == 1, 20, "the new run goes through");
        });

        /// <summary>A held sequence is waiting for a resume that won't come, so ending it has to end its run.</summary>
        [UnityTest]
        public IEnumerator TS_Y_Terminating_a_paused_sequence_ends_it([Values] Pauser pauser) => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Held", frames: 1000);
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Ended While Paused").Task(task).Build()));

            await Frames.Step(2);
            Pause(relay, pauser);
            await Frames.Step(2);

            relay.Terminate();

            await Frames.Until(() => Ledger.Cleans("Held") == 1, 5, "its task is cleaned up");
            Assert.That(Ledger.Violations(), Is.Empty);

            if (pauser == Pauser.Control) World.Control.SetControlState(EProcessControlState.Ready);
        });

        /// <summary>Interrupted during a pause, a sequence is over, so the resume ends it and doesn't start it again.</summary>
        [UnityTest]
        public IEnumerator TS_Y_A_sequence_interrupted_during_a_pause_ends_on_resume() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Interrupted", frames: 1000);
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Interrupted While Paused").Task(task).Build()));

            await Frames.Step(2);
            relay.Pause();
            await Frames.Step();

            relay.TryGetProcess<TaskSequenceProcess>(out var process);
            Assert.That(process.Interrupt(), "the interrupt is accepted");
            await Frames.Step(3);

            Assert.That(relay.IsWaiting, "it stays paused until it's resumed");

            relay.Unpause();

            await Frames.Until(() => relay.State == EProcessState.Terminated, 3, "it ends");
            Assert.That(Ledger.Prepares("Interrupted"), Is.EqualTo(1), "without running again");
        });

        /// <summary>A synchronous sequence has no run: it holds by not being stepped, and always has.</summary>
        [UnityTest]
        public IEnumerator A_synchronous_sequence_holds_by_not_stepping([Values] Pauser pauser) => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Stepped", frames: 8);
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Synchronous")
                .WithLifecycle(EProcessLifecycle.Synchronous, EProcessStepTiming.Update)
                .Task(task)
                .Build()));

            await Frames.Step(3);
            Pause(relay, pauser);
            await Frames.Step();

            int steps = Faults.Hits(task.Site(TaskHook.Step));
            await Frames.Step(3);
            Assert.That(Faults.Hits(task.Site(TaskHook.Step)), Is.EqualTo(steps), "it held");

            Resume(relay, pauser);

            await Frames.Until(() => relay.State == EProcessState.Terminated, 10, "it carries on and finishes");
        });

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            return relay;
        }

        private void Pause(ProcessRelay relay, Pauser pauser)
        {
            if (pauser == Pauser.Relay) Assert.That(relay.Pause(), "the pause is accepted");
            else World.Control.SetControlState(EProcessControlState.Waiting);
        }

        private void Resume(ProcessRelay relay, Pauser pauser)
        {
            if (pauser == Pauser.Relay) Assert.That(relay.Unpause(), "the resume is accepted");
            else World.Control.SetControlState(EProcessControlState.Ready);
        }
    }
}
