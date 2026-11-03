using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class LifecycleEdgeTests : ProvingGroundsTest
    {
        /// <summary>
        /// Unity sends no OnDestroy to an object that was never active, so a process registered on one outlived it
        /// in the hierarchy, and the next process registered on an object of its own read the destroyed root and threw.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_26_A_destroyed_root_doesnt_break_the_next_registration() => Live(async () =>
        {
            var host = new GameObject("Never Active");
            host.SetActive(false);
            World.Register(host.AddComponent<ProbeMonoProcess>());
            Object.Destroy(host);
            await Frames.Step();

            ProbeMonoProcess next = null;
            Assert.DoesNotThrow(() => next = World.MonoProbe("Next"), "it registers");
            await Frames.Step(2);

            Assert.That(next.Recorder[ProbeHook.Update], Is.GreaterThan(0), "and steps");
        });

        /// <summary>
        /// A block left its step groups by its process's timing as it was then, not as it was when the block joined
        /// them, so changing a running process's timing left it stepping where it had been, paused or not.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_26_A_timing_changed_mid_run_takes_effect_cleanly() => Live(async () =>
        {
            var probe = World.MonoProbe("Retimed", EProcessLifecycle.Synchronous);
            await Frames.Step();

            probe.ProcessTiming = EProcessStepTiming.LateUpdate;
            probe.ProcessRelay.Pause();
            int updates = probe.Recorder[ProbeHook.Update];
            await Frames.Step(2);
            Assert.That(probe.Recorder[ProbeHook.Update], Is.EqualTo(updates), "paused, it doesn't step");

            probe.ProcessRelay.Unpause();
            int lateUpdates = probe.Recorder[ProbeHook.LateUpdate];
            await Frames.Step(2);

            Assert.That(probe.Recorder[ProbeHook.Update], Is.EqualTo(updates), "resumed, it steps in Update no more");
            Assert.That(probe.Recorder[ProbeHook.LateUpdate] - lateUpdates, Is.EqualTo(2), "but in LateUpdate, once a frame");
        });

        /// <summary>
        /// Unity sends no OnDestroy to an object that was never active, so a process registered on one went on after the
        /// object was destroyed: its block kept stepping with nothing behind it.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_30_A_process_on_an_object_that_never_woke_ends_with_it() => Live(async () =>
        {
            var host = new GameObject("Never Active");
            host.SetActive(false);
            var relay = World.Register(host.AddComponent<ProbeMonoProcess>());
            await Frames.Step();
            Assert.That(World.Control.IsRegistered(relay.CacheIndex), "registered");

            Object.Destroy(host);
            await Frames.Step(2);

            Assert.That(World.Control.IsRegistered(relay.CacheIndex), Is.False, "and gone with its object");
        });
    }
}
