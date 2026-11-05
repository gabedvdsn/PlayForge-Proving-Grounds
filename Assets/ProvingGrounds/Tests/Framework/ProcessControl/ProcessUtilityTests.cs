using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ProcessUtilityTests : ProvingGroundsTest
    {
        /// <summary>The elapsed time was taken the wrong way round, so it went down every frame and the loop never ended.</summary>
        [UnityTest]
        public IEnumerator PC_25_DoWhile_ends_after_its_duration() => Live(async () =>
        {
            bool done = false;
            var cts = new CancellationTokenSource();
            try
            {
                Loop().Forget();
                await Frames.Until(() => done, 12, "it ends after a tenth of a second");
            }
            finally
            {
                cts.Cancel();
                cts.Dispose();
            }

            async UniTaskVoid Loop()
            {
                try
                {
                    await ProcessTaskUtil.DoWhile(() => UniTask.CompletedTask, 0.1f, cts.Token);
                    done = true;
                }
                catch (System.OperationCanceledException) { }
            }
        });

        /// <summary>The lifetime was cut to whole seconds before it became milliseconds.</summary>
        [UnityTest]
        public IEnumerator PC_25_RemainingRuntime_counts_every_millisecond() => Live(async () =>
        {
            var probe = World.Probe("Timed");
            await Frames.Seconds(1.5f);

            Assert.That(probe.Relay.RemainingRuntime(5000, 1000, unscaled: false), Is.InRange(3400, 3550));
        });

        /// <summary>
        /// A block's usage was the wall-clock time between its steps, much the same for every process whatever its step
        /// cost, which Process Control showed as each process's share of the frame (ED-5). While asked, as the Runtime
        /// Debugger asks while it's open, each block counts its steps and times them; otherwise it does neither.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_5_A_process_times_its_steps_while_asked() => Live(async () =>
        {
            var measure = typeof(ProcessControlBlock).GetProperty("MeasureSteps", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(measure, "ProcessControl can time its processes' steps");
            var relay = World.Register(new ActionProcess("Busy", EProcessStepPriorityMethod.Manual, 0,
                EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating, () =>
                {
                    var watch = Stopwatch.StartNew();
                    while (watch.Elapsed.TotalMilliseconds < 2.0) { }
                }));
            Assert.IsTrue(World.Control.TryGetPCB(relay.CacheIndex, out var pcb));
            int Steps() => (int)typeof(ProcessControlBlock).GetProperty("Steps").GetValue(pcb);
            double Seconds() => (double)typeof(ProcessControlBlock).GetProperty("StepSeconds").GetValue(pcb);

            await Frames.Step(3);
            Assert.AreEqual(0, Steps(), "unasked, a block counts nothing");

            measure.SetValue(null, true);
            try
            {
                await Frames.Step(4);
                Assert.That(Steps(), Is.InRange(3, 5), "asked, it counts each step");
                Assert.That(Seconds() / Steps(), Is.GreaterThan(0.0015), "and times it");
            }
            finally
            {
                measure.SetValue(null, false);
            }
        });
    }
}
