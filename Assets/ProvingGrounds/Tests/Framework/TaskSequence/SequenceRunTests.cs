using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class SequenceRunTests : ProvingGroundsTest
    {
        /// <summary>
        /// A TaskSequence held one Runtime, and each run overwrote it, so running one twice at once left the
        /// first run unreachable: interrupting it stopped the other one instead.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_I_An_interrupt_reaches_the_run_it_was_sent_to() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Shared Sequence").Delay(100f).Build());

            ProcessControl.Register(sequence, out var first);
            World.Own(first);
            await Frames.Step();

            ProcessControl.Register(sequence, out var second);
            World.Own(second);
            await Frames.Step();

            first.TryGetProcess<TaskSequenceProcess>(out var firstProcess);
            Assert.That(firstProcess.Interrupt(), "the interrupt is accepted");
            await Frames.Step(3);

            Assert.That(second.IsRunning, "the other run goes on");
            Assert.That(first.IsRunning, Is.False, "the interrupted run stops");
        });

        /// <summary>
        /// A run's stage timeout went into the shared definition, so every later run of it inherited a timeout
        /// that one run asked for.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_I_A_run_leaves_the_definition_as_it_found_it() => Live(async () =>
        {
            var definition = TaskSequenceBuilder.Create("Shared Definition").Delay(100f).Build();
            var sequence = new TaskSequence(definition);

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Step(2);

            sequence.Runtime.RequestStartStageTimeout(new SequenceTimeout { Seconds = 100f });

            Assert.That(definition.Stages[0].Timeouts, Is.Empty, "a run's timeout stays with the run");
        });

        /// <summary>
        /// A broken repeat was recorded on the stage, which every run shares, so it broke the repeat of them all.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_I_Breaking_a_repeat_stops_only_that_run() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Repeating")
                .Stage(stage => stage.WithRepeat(true).Task(async (CancellationToken token) =>
                    await UniTask.Yield(PlayerLoopTiming.Update, token)))
                .Build());

            ProcessControl.Register(sequence, out var first);
            World.Own(first);
            ProcessControl.Register(sequence, out var second);
            World.Own(second);
            await Frames.Step(3);

            first.TryGetProcess<TaskSequenceProcess>(out var firstProcess);
            Assert.That(firstProcess.Inject(BreakStageRepeatInjection.Instance), "the break is accepted");
            await Frames.Step(4);

            Assert.That(second.IsRunning, "the other run goes on repeating");
            Assert.That(first.IsRunning, Is.False, "the broken run ends");
        });

        /// <summary>
        /// A condition's fired flag lived on the shared definition, and only the newest run was ever checked,
        /// so a once-only condition fired once between all the runs.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_I_Each_run_fires_a_once_only_condition_once() => Live(async () =>
        {
            var fired = new List<TaskSequenceRuntime>();
            bool armed = false;

            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Conditional")
                .InjectWhen(_ => armed, new DelegateSequenceInjection(run => { fired.Add(run); return true; }), fireOnce: true)
                .Delay(100f)
                .Build());

            ProcessControl.Register(sequence, out var first);
            World.Own(first);
            ProcessControl.Register(sequence, out var second);
            World.Own(second);
            await Frames.Step();

            armed = true;
            await Frames.Step(3);

            Assert.That(fired, Has.Count.EqualTo(2), "each run fires it");
            Assert.That(fired, Is.Unique, "and no run fires it twice");
        });

        /// <summary>
        /// A sequence waited on its stage through AttachExternalCancellation, which throws the moment the
        /// token cancels, so CleanStage ran while the stage's tasks were still unwinding. A task that releases
        /// something in Clean and uses it in Execute's finally saw it gone.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_AA_A_cancelled_sequence_cleans_its_tasks_after_they_stop() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Cancelled", frames: 1000);
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Cancelled Sequence").Task(task).Build());
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Step(2);

            relay.TerminateImmediate();
            await Frames.Step(2);

            Assert.That(Ledger.Violations(), Is.Empty);
        });

        /// <summary>
        /// Everything a stage signals with sits in a finally behind its OnTerminate, so a throw there left the
        /// sequence waiting on a signal that never came.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_E_A_throwing_stage_callback_doesnt_stop_the_sequence() => Live(async () =>
        {
            const string Site = "Stage.OnTerminate";
            Faults.Arm(Site, loggedAs: LogType.Error);

            var next = new ProbeSequenceTask(World, "Next Stage");
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Throwing Stage Callback")
                .Stage(stage => stage.Delay(0.05f).OnTerminate(() => Faults.Trip(Site)))
                .Stage(stage => stage.Task(next))
                .Build());

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 20, "the next stage runs");
        });

        /// <summary>Leaving a critical stage notifies from the same finally, ahead of the signal.</summary>
        [UnityTest]
        public IEnumerator TS_E_A_throwing_critical_exit_on_a_stage_doesnt_stop_the_sequence() => Live(async () =>
        {
            const string Site = "Stage.CriticalExit";
            Faults.Arm(Site, loggedAs: LogType.Error);

            var next = new ProbeSequenceTask(World, "Next Stage");
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Throwing Critical Exit")
                .Stage(stage => stage.WithCriticalFlag(true).Delay(0.05f))
                .Stage(stage => stage.Task(next))
                .Build());
            sequence.OnCriticalSectionExited = () => Faults.Trip(Site);

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, 20, "the next stage runs");
        });

        /// <summary>
        /// A whole-sequence critical section exits in the finally around the run, so a throw there skipped
        /// OnComplete and OnTerminate and left the data packet marked in use.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_E_A_throwing_critical_exit_on_a_sequence_still_completes_it() => Live(async () =>
        {
            const string Site = "Sequence.CriticalExit";
            Faults.Arm(Site, loggedAs: LogType.Error);

            bool terminated = false;
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Throwing Sequence Exit")
                .WithCriticalFlag(true)
                .OnTerminate((_, _) => terminated = true)
                .Delay(0.05f)
                .Build());
            sequence.OnCriticalSectionExited = () => Faults.Trip(Site);

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);

            await Frames.Until(() => terminated, 20, "the sequence's OnTerminate fires");
        });
    }
}
