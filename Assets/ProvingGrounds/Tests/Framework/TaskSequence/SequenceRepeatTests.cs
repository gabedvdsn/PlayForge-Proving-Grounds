using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class SequenceRepeatTests : ProvingGroundsTest
    {
        public enum RepeatKind
        {
            StageFlag,
            Policy
        }

        // Stops a loop that never yields. Without it, these tests would freeze the run rather than fail
        private const int Fuse = 1000;

        /// <summary>
        /// A task can finish without yielding, and nothing in the repeat loop yielded either, so the stage
        /// repeated for ever inside one frame. That's the freeze the Swarm Defender sample yields to avoid.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_M_An_instant_stage_repeats_once_a_frame([Values] RepeatKind kind) => Live(async () =>
        {
            int iterations = 0;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Instant Repeat")
                .Stage(stage =>
                {
                    Repeats(stage, kind);
                    stage.Do(data => { if (++iterations >= Fuse) data.BreakStageRepeat(); });
                })
                .Build()));

            await Frames.Step(3);

            Assert.That(iterations, Is.LessThanOrEqualTo(5), "a frame apiece");
            Assert.That(relay.IsRunning, "and it goes on repeating");
        });

        /// <summary>
        /// SyncDelay resets in Prepare, and a repeat never prepared again, so from the second iteration the
        /// delay was over before it began, and the stage spun as the test above does.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_M_A_repeating_SyncDelay_waits_every_time([Values] RepeatKind kind) => Live(async () =>
        {
            int iterations = 0;
            Run(new TaskSequence(TaskSequenceBuilder.Create("Delayed Repeat")
                .Stage(stage =>
                {
                    Repeats(stage, kind);
                    stage.SyncDelay(0.1f);
                    stage.Do(data => { if (++iterations >= Fuse) data.BreakStageRepeat(); });
                })
                .Build()));

            await Frames.Seconds(0.35f);

            Assert.That(iterations, Is.InRange(3, 5), "a tenth of a second apiece");
        });

        /// <summary>An iteration is a run of the stage's tasks, so each one gets its Prepare and its Clean.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_M_Every_iteration_prepares_and_cleans_its_tasks([Values] RepeatKind kind) => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Repeated", frames: 2);
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Prepared Repeat")
                .Stage(stage =>
                {
                    Repeats(stage, kind);
                    stage.Task(task);
                })
                .Build());
            var relay = Run(sequence);

            await Frames.Step(10);

            Assert.That(sequence.Inject(BreakStageRepeatInjection.Instance), "the break is accepted");
            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "the repeat ends");

            Assert.That(Ledger.Prepares("Repeated"), Is.GreaterThanOrEqualTo(3), "several iterations");
            Assert.That(Ledger.Cleans("Repeated"), Is.EqualTo(Ledger.Prepares("Repeated")));
            Assert.That(Ledger.Violations(), Is.Empty);
        });

        /// <summary>The synced runner prepared each iteration, but cleaned only the last.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_M_A_synced_repeat_cleans_before_it_prepares_again() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Stepped", frames: 2);
            var runner = TaskSequenceBuilder.Create("Synced Repeat")
                .Stage(stage => stage.WithRepeat(true).Task(task))
                .BuildSyncRunner();

            var data = SequenceDataPacket.SceneRoot();
            World.Register(new TaskSequenceProcess(runner), data);

            await Frames.Step(8);

            Assert.That(Ledger.Prepares("Stepped"), Is.GreaterThanOrEqualTo(3), "several iterations");
            Assert.That(Ledger.Violations(), Is.Empty);

            runner.Interrupt(data);
            await Frames.Step(2);

            Assert.That(Ledger.Cleans("Stepped"), Is.EqualTo(Ledger.Prepares("Stepped")));
        });

        /// <summary>The whole-sequence repeat waits on its maintained stages, which has always taken it a frame.</summary>
        [UnityTest]
        public IEnumerator An_instant_sequence_repeats_once_a_frame() => Live(async () =>
        {
            int runs = 0;
            Run(new TaskSequence(TaskSequenceBuilder.Create("Instant Sequence")
                .WithRepeat(true)
                .Do(data => { if (++runs >= Fuse) data.Interrupt(); })
                .Build()));

            await Frames.Step(3);

            Assert.That(runs, Is.LessThanOrEqualTo(5), "a frame apiece");
        });

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            return relay;
        }

        private static void Repeats(StageBuilder stage, RepeatKind kind)
        {
            if (kind == RepeatKind.StageFlag) stage.WithRepeat(true);
            else stage.WithRepeatAndDelay();
        }
    }
}
