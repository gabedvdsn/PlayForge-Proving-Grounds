using System.Collections;
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
    }
}
