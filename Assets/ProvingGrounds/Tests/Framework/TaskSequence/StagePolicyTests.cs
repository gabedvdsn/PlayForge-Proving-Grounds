using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class StagePolicyTests : ProvingGroundsTest
    {
        public enum StageExit
        {
            Skip,
            Timeout,
            MaxDuration
        }

        // A second is well past the 0.1 s the timed exits take, and over at the same time for all of them
        private const int ExitFrames = 12;

        /// <summary>
        /// WhenAny waits for a task to finish and ignores cancellation, so a stage stopped from outside
        /// cancels every task, none finishes, and the sequence hangs on that stage for good.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_A_A_stopped_WhenAny_stage_lets_the_sequence_go_on([Values] StageExit exit) =>
            StopsAndGoesOn(stage => stage.WhenAny(), exit);

        /// <summary>WhenN has the same wait: a cancelled task doesn't count, so N is never reached.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_A_A_stopped_WhenN_stage_lets_the_sequence_go_on([Values] StageExit exit) =>
            StopsAndGoesOn(stage => stage.WhenN(1), exit);

        /// <summary>The control for the two above, which also shows the frames they allow are enough.</summary>
        [UnityTest]
        public IEnumerator A_stopped_WhenAll_stage_lets_the_sequence_go_on([Values] StageExit exit) =>
            StopsAndGoesOn(stage => stage.WhenAll(), exit);

        private IEnumerator StopsAndGoesOn(Action<StageBuilder> policy, StageExit exit) => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Next Stage");
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Stopped Stage")
                .Stage(stage =>
                {
                    policy(stage);
                    stage.Delay(100f);
                    stage.Delay(100f);

                    if (exit == StageExit.Timeout) stage.WithTimeout(0.1f);
                    if (exit == StageExit.MaxDuration) stage.WithMaxDuration(0.1f, SkipStageInjection.Instance);
                })
                .Stage(stage => stage.Task(next))
                .Build());

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Step(2);

            if (exit == StageExit.Skip) Assert.That(sequence.SkipCurrentStage(), "the skip is accepted");

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, ExitFrames, "the next stage runs");
        });
    }
}
