using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>What running a sequence costs.</summary>
    public class SequenceCostTests : ProvingGroundsTest
    {
        /// <summary>
        /// Every stage made a token source for its policy, linked to the stage's own, though only a policy that stops
        /// the tasks still running needs one, as WhenAny and WhenN do. With the stage's own, a three-stage ability made
        /// about 14. A stage that waits for all its tasks now hands its policy the stage's own source, so it makes one
        /// fewer than a stage under a policy the runtime doesn't know, which still gets one of its own.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_10_A_stage_that_waits_for_all_its_tasks_makes_no_second_token_source() => Live(async () =>
        {
            var builtIn = await PerFrame(WhenAllStagePolicy.Instance);
            var unknown = await PerFrame(new WaitsForAll());
            TestContext.WriteLine($"a stage that waits for all its tasks: {builtIn}; under a policy the runtime doesn't " +
                                  $"know: {unknown}");

            Assert.That(unknown.PerFrame - builtIn.PerFrame, Is.GreaterThanOrEqualTo(64f),
                "bytes a frame less, at least a token source's worth");
        });

        // What a frame allocates for a sequence that runs one stage of one instant task under `policy`, once a frame
        private async UniTask<FrameAllocation> PerFrame(ISequenceStagePolicy policy)
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Pulsing")
                .WithRepeat(true)
                .Stage(stage => stage.WithPolicy(policy).Do(_ => { }))
                .Build());
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Step(30);

            FrameAllocation frames;
            using (var probe = new AllocationProbe())
            {
                frames = await probe.MeasureFrames(Frames, 300);
            }

            sequence.Interrupt();
            await Frames.Until(() => relay.State == EProcessState.Terminated, 10, "the sequence stops");
            return frames;
        }

        // A policy the runtime doesn't know, which waits for all its tasks as the built-in one does
        private sealed class WaitsForAll : ISequenceStagePolicy
        {
            public UniTask AwaitCompletion(TaskSequenceRuntime runtime, SequenceStage stage, UniTask[] taskUnits,
                CancellationTokenSource stageCts) => WhenAllStagePolicy.Instance.AwaitCompletion(runtime, stage, taskUnits, stageCts);
        }
    }
}
