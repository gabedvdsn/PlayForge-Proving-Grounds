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

        [UnityTest, Category("Red")]
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
        [UnityTest, Category("Red")]
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

        [UnityTest, Category("Red")]
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
        [UnityTest, Category("Red")]
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

        [UnityTest, Category("Red")]
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

        [UnityTest, Category("Red")]
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

        /// <summary>A held sequence is waiting for a resume that won't come, so ending it has to end its run.</summary>
        [UnityTest, Category("Red")]
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
        [UnityTest, Category("Red")]
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
