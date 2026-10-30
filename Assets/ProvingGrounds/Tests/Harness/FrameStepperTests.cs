using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class FrameStepperTests : ProvingGroundsTest
    {
        [UnityTest]
        public IEnumerator A_frame_runs_one_update_one_late_update_and_one_fixed_update() => Live(async () =>
        {
            int updates = Frames.Updates, lates = Frames.LateUpdates, fixeds = Frames.FixedUpdates;

            await Frames.Step(10);

            Assert.That(Frames.Frame, Is.EqualTo(10));
            Assert.That(Frames.Updates - updates, Is.EqualTo(10), "Update");
            Assert.That(Frames.LateUpdates - lates, Is.EqualTo(10), "LateUpdate");
            Assert.That(Frames.FixedUpdates - fixeds, Is.EqualTo(10), "FixedUpdate");
        });

        [UnityTest]
        public IEnumerator Time_advances_exactly_one_delta_per_frame() => Live(async () =>
        {
            float start = Time.time, fixedStart = Time.fixedTime;

            await Frames.Step(60);

            Assert.That(Time.time - start, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Time.fixedTime - fixedStart, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Time.deltaTime, Is.EqualTo(Frames.DeltaTime).Within(0.00001f));
        });

        [UnityTest]
        public IEnumerator Seconds_become_frames() => Live(async () =>
        {
            await Frames.Seconds(0.5f);

            Assert.That(Frames.Frame, Is.EqualTo(30));
        });

        [UnityTest]
        public IEnumerator Until_reports_the_frames_taken() => Live(async () =>
        {
            int ready = Frames.Frame + 4;

            int taken = await Frames.Until(() => Frames.Frame >= ready, 10, "the counter reaches it");

            Assert.That(taken, Is.EqualTo(4));
        });

        [UnityTest]
        public IEnumerator Until_fails_when_the_condition_never_holds() => Live(async () =>
        {
            await AssertFails("never: not true within 5 frames", Frames.Until(() => false, 5, "never").AsUniTask());
            Assert.That(Frames.Frame, Is.EqualTo(5), "it gave up at its bound, not before");
        });

        [UnityTest]
        public IEnumerator ExactlyIn_fails_when_early() => Live(async () =>
        {
            int ready = Frames.Frame + 2;

            await AssertFails("early: expected in exactly 3 frames, took 2",
                Frames.ExactlyIn(3, () => Frames.Frame >= ready, "early"));
        });

        [UnityTest]
        public IEnumerator ExactlyIn_fails_when_late() => Live(async () =>
        {
            int ready = Frames.Frame + 4;

            await AssertFails("late: not true within 3 frames",
                Frames.ExactlyIn(3, () => Frames.Frame >= ready, "late"));
        });

        [UnityTest]
        public IEnumerator ExactlyIn_passes_on_the_frame() => Live(async () =>
        {
            int ready = Frames.Frame + 3;

            await Frames.ExactlyIn(3, () => Frames.Frame >= ready, "on time");
        });

        [UnityTest]
        public IEnumerator Holds_fails_on_the_first_frame_it_breaks() => Live(async () =>
        {
            int last = Frames.Frame + 2;

            await AssertFails("steady: stopped holding after 3 of 5 frames",
                Frames.Holds(5, () => Frames.Frame <= last, "steady"));
        });

        private static async UniTask AssertFails(string message, UniTask running)
        {
            try
            {
                await running;
            }
            catch (AssertionException failure)
            {
                Assert.That(failure.Message, Does.Contain(message));
                return;
            }

            Assert.Fail($"Expected a failure: {message}");
        }
    }
}
