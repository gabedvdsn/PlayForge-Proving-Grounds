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
        /// WhenAny waited for a task to finish and ignored cancellation, so a stage stopped from outside
        /// cancelled every task, none finished, and the sequence hung on that stage for good.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_A_A_stopped_WhenAny_stage_lets_the_sequence_go_on([Values] StageExit exit) =>
            StopsAndGoesOn(stage => stage.WhenAny(), exit);

        /// <summary>WhenN had the same wait: a cancelled task doesn't count, so N was never reached.</summary>
        [UnityTest]
        public IEnumerator TS_A_A_stopped_WhenN_stage_lets_the_sequence_go_on([Values] StageExit exit) =>
            StopsAndGoesOn(stage => stage.WhenN(1), exit);

        /// <summary>A WhenN that one task has part met is still stopped by the cancellation of the rest.</summary>
        [UnityTest]
        public IEnumerator TS_A_A_part_met_WhenN_stage_can_be_stopped() => Live(async () =>
        {
            var next = new ProbeSequenceTask(World, "Next Stage");
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Part Met")
                .Stage(stage =>
                {
                    stage.WhenN(2);
                    stage.Delay(0.05f);
                    stage.Delay(100f);
                    stage.Delay(100f);
                })
                .Stage(stage => stage.Task(next))
                .Build());

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Seconds(0.1f);

            Assert.That(sequence.SkipCurrentStage(), "the skip is accepted");

            await Frames.Until(() => Ledger.Cleans("Next Stage") == 1, ExitFrames, "the next stage runs");
        });

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
