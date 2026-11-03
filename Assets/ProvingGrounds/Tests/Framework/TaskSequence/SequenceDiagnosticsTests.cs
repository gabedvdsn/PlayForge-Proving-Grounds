using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class SequenceDiagnosticsTests : ProvingGroundsTest
    {
        public enum Timer
        {
            None,
            FiredTimeout
        }

        /// <summary>
        /// A task's error was rethrown with <c>throw e</c> at every level it passed through, which starts its stack
        /// trace again from there, so the error a sequence reported no longer said where it happened.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_R_A_tasks_error_keeps_its_stack_trace([Values] bool subStage) => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Broken", frames: 1);
            Faults.Arm(task.Site(TaskHook.Execute), loggedAs: null);

            Exception reported = null;
            var builder = TaskSequenceBuilder.Create("Reporting").OnException((_, ex) =>
            {
                reported = ex;
                return true;
            });
            if (subStage) builder.Stage(stage => stage.SubStage(sub => sub.Task(task)));
            else builder.Task(task);

            ProcessControl.Register(new TaskSequence(builder.Build()), out var relay);
            World.Own(relay);
            await Frames.Until(() => reported != null, 5, "the error is reported");

            Assert.That(reported.StackTrace, Does.Contain(nameof(FaultInjector.Trip)), "from where it was thrown");
        });

        /// <summary>
        /// Elapsed time was kept by the sequence's timers, so one without a timer read 0 throughout, and one whose
        /// timer had fired stopped where it fired.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_W_A_run_knows_how_long_it_has_run([Values] Timer timer) => Live(async () =>
        {
            float elapsed = -1f;
            var builder = TaskSequenceBuilder.Create("Timed");
            if (timer == Timer.FiredTimeout) builder.WithTimeout(0.1f, new DelegateSequenceInjection(_ => true));
            builder.Delay(0.3f).Do(data => elapsed = data.ElapsedTime);

            ProcessControl.Register(new TaskSequence(builder.Build()), out var relay);
            World.Own(relay);
            await Frames.Until(() => relay.State == EProcessState.Terminated, 30, "it runs");

            Assert.That(elapsed, Is.EqualTo(0.3f).Within(0.05f));
        });
    }
}
