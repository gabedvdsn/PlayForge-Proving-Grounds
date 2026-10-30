using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// The base of every test: a live <see cref="World"/> before it, and the invariants and a full
    /// teardown after it. A test is a PlayMode <c>[UnityTest]</c> whose body runs through <see cref="Live"/>:
    /// <code>
    /// [UnityTest]
    /// public IEnumerator Name() =&gt; Live(async () =&gt;
    /// {
    ///     var probe = World.Probe("Worker", runFrames: 3);
    ///     await Frames.ExactlyIn(3, () =&gt; probe.Relay.State == EProcessState.Terminated, "the run ends");
    /// });
    /// </code>
    /// Never call a process's WhenUpdate or WhenLateUpdate by hand. That's how the golden tests hid GAS-1;
    /// step frames and let ProcessControl do it.
    /// </summary>
    public abstract class ProvingGroundsTest
    {
        protected World World { get; private set; }

        protected FrameStepper Frames => World.Frames;
        protected FaultInjector Faults => World.Faults;
        protected TaskLedger Ledger => World.Ledger;
        protected ForgeAssets Make => World.Make;

        [SetUp]
        public void BuildWorld()
        {
            // In two steps, so teardown still has a world to clear if bootstrapping is what failed
            World = new World();
            World.Bootstrap();
        }

        /// <summary>Runs a test body from the end of a frame, so it starts from a whole frame.</summary>
        protected IEnumerator Live(Func<UniTask> body) => UniTask.ToCoroutine(async () =>
        {
            await Frames.Align();
            await body();
        });

        [UnityTearDown]
        public IEnumerator EndWorld() => UniTask.ToCoroutine(async () =>
        {
            var violations = new List<string>();
            try
            {
                // A test that already failed has said what's wrong; its leftovers would only add noise
                if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed)
                {
                    violations = await World.Audit();
                }
            }
            finally
            {
                await World.Destroy();
                World = null;
            }

            if (violations.Count > 0)
            {
                throw new AssertionException($"Invariants broken:\n  {string.Join("\n  ", violations)}");
            }
        });
    }
}
