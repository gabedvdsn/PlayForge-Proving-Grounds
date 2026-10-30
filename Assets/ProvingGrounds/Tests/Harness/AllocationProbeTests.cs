using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class AllocationProbeTests : ProvingGroundsTest
    {
        private static readonly List<object> Sink = new();

        [TearDown]
        public void EmptySink() => Sink.Clear();

        [UnityTest]
        public IEnumerator An_operation_that_allocates_is_measured() => Live(async () =>
        {
            using var probe = new AllocationProbe();

            float bytes = probe.MeasureEach(() => Sink.Add(new byte[1024]), runs: 100);

            Assert.That(bytes, Is.GreaterThanOrEqualTo(1024f));
            Assert.That(bytes, Is.LessThan(1200f), "the array and its header, little else");
            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator An_operation_that_allocates_nothing_measures_zero() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            int counter = 0;

            float bytes = probe.MeasureEach(() => counter++, runs: 1000);

            Assert.That(bytes, Is.EqualTo(0f));
            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator Idle_frames_measure_exactly_zero() => Live(async () =>
        {
            using var probe = new AllocationProbe();

            var idle = await probe.MeasureFrames(Frames, 1200);

            Assert.That(idle.Total, Is.EqualTo(0), idle.ToString());
        });

        [UnityTest]
        public IEnumerator An_idle_GAS_under_live_stepping_allocates_nothing() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            World.RuntimeEntity(observed: false);
            World.MonoEntity(observed: false);
            await Frames.Step(20);

            var frames = await probe.MeasureFrames(Frames, 1200);

            Assert.That(frames.Total, Is.EqualTo(0), frames.ToString());
        });

        [UnityTest]
        public IEnumerator A_process_allocating_every_frame_is_caught() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            var leaky = World.Probe("Leaky", EProcessLifecycle.Synchronous);
            leaky.Recorder.OnHit = hook =>
            {
                if (hook == ProbeHook.Update) Sink.Add(new byte[2048]);
            };
            await Frames.Step(5);

            var frames = await probe.MeasureFrames(Frames, 30);

            Assert.That(frames.PerFrame, Is.GreaterThanOrEqualTo(2048f), frames.ToString());
            Assert.That(frames.PerFrame, Is.LessThan(2200f), frames.ToString());
            Assert.That(frames.FramesOver, Is.EqualTo(30), frames.ToString());
        });

        [UnityTest]
        public IEnumerator A_few_bytes_a_frame_are_not_lost_in_the_noise() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            var leaky = World.Probe("Slightly Leaky", EProcessLifecycle.Synchronous);
            leaky.Recorder.OnHit = hook =>
            {
                if (hook == ProbeHook.Update) Sink.Add(new object());
            };
            await Frames.Step(5);

            var frames = await probe.MeasureFrames(Frames, 300);

            // Well under the 82 bytes the runner's noise moves by, on every frame
            Assert.That(frames.FramesOver, Is.EqualTo(300), frames.ToString());
            Assert.That(frames.PerFrame, Is.LessThan(82f), frames.ToString());
        });

        [UnityTest]
        public IEnumerator Every_stepping_phase_is_measured() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            var leaky = World.Probe("Leaky Everywhere", EProcessLifecycle.Synchronous, EProcessStepTiming.UpdateFixedAndLate);
            leaky.Recorder.OnHit = hook =>
            {
                if (hook is ProbeHook.Update or ProbeHook.FixedUpdate or ProbeHook.LateUpdate) Sink.Add(new byte[1024]);
            };
            await Frames.Step(5);

            var frames = await probe.MeasureFrames(Frames, 30);

            Assert.That(frames.PerFrame, Is.GreaterThanOrEqualTo(3 * 1024f), frames.ToString());
        });

        [UnityTest]
        public IEnumerator A_UniTask_continuation_is_measured() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            bool running = true;

            async UniTaskVoid Leak()
            {
                while (running)
                {
                    Sink.Add(new byte[512]);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            }

            Leak().Forget();
            await Frames.Step(5);

            var frames = await probe.MeasureFrames(Frames, 30);
            running = false;

            Assert.That(frames.PerFrame, Is.GreaterThanOrEqualTo(512f), frames.ToString());
        });

        [UnityTest]
        public IEnumerator An_occasional_allocation_shows_as_the_frames_it_hit() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            int updates = 0;
            var leaky = World.Probe("Now And Then", EProcessLifecycle.Synchronous);
            leaky.Recorder.OnHit = hook =>
            {
                if (hook == ProbeHook.Update && ++updates % 10 == 0) Sink.Add(new byte[256]);
            };
            await Frames.Step(5);

            var frames = await probe.MeasureFrames(Frames, 100);

            Assert.That(frames.FramesOver, Is.EqualTo(10), frames.ToString());
        });
    }
}
