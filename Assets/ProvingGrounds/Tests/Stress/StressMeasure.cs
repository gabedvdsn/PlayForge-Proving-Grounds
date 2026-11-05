using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Unity.Profiling;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>What the stress suite times by: a frame's time in PlayForge's step loops, from their markers (FEAT-18).</summary>
    internal static class StressMeasure
    {
        private static readonly string[] StepLoops = { "PlayForge.Update", "PlayForge.LateUpdate", "PlayForge.FixedUpdate" };

        /// <summary>The step loops over a stretch of frames, from as it's made to as it's read.</summary>
        public sealed class StepTimer : IDisposable
        {
            private readonly ProfilerRecorder[] recorders;

            public StepTimer(int frames) =>
                recorders = StepLoops.Select(name => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, frames + 4)).ToArray();

            /// <summary>Nanoseconds a frame spent in the step loops, on average over the frames so far.</summary>
            public double PerFrame
            {
                get
                {
                    double perFrame = 0;
                    foreach (var recorder in recorders)
                    {
                        if (recorder.Count == 0) continue;
                        long total = 0;
                        for (int i = 0; i < recorder.Count; i++) total += recorder.GetSample(i).Value;
                        perFrame += total / (double)recorder.Count;
                    }
                    return perFrame;
                }
            }

            public void Dispose()
            {
                foreach (var recorder in recorders) recorder.Dispose();
            }
        }

        /// <summary>
        /// Nanoseconds a frame spent in PlayForge's step loops, over <paramref name="count"/> frames: the fastest of three
        /// runs, as the slow runs are the ones a collection or the editor interrupted.
        /// </summary>
        public static async UniTask<double> StepNanoseconds(FrameStepper frames, int count)
        {
            double fastest = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                using var timer = new StepTimer(count);
                await frames.Step(count);
                fastest = Math.Min(fastest, timer.PerFrame);
            }
            return fastest;
        }
    }
}
