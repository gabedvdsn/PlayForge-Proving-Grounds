using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FEAT-7's fixed-rate tick (Decision 30): with a tick rate set, ProcessControl steps every process, every entity among
    /// them, and every sequence, a fixed number of times a frame, each tick a fixed step of the game's time, so a run
    /// comes out the same whatever its frame rate. Off, as it is by default, they step once a frame.
    /// </summary>
    public class FixedTickTests : ProvingGroundsTest
    {
        // A clock whose frames each take a fixed step, with Unity's time and frame
        private sealed class SteppingClock : IForgeClock
        {
            public float Step;
            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        // An effect that ticks ten times a second for a second, and counts its ticks in an attribute
        private (GameplayEffect Effect, Attribute Counter) Ticker()
        {
            var counter = Make.Attribute("Ticks");
            var effect = Make.Effect("Ticker", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Target = counter, Magnitude = 1f, Duration = 1f,
                PeriodicTicks = true, Ticks = 10,
            });
            return (effect, counter);
        }

        private static float Value(IGameplayAbilitySystem gas, Attribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }

        // Runs a ticking effect for half a second of game time at a frame rate, under a tick rate, and returns its ticks
        // and the time it has left
        private async Cysharp.Threading.Tasks.UniTask<(float Ticks, float Left)> RunAt(Hub hub, float frame, int tickRate)
        {
            var (ticker, counter) = Ticker();
            var gas = World.Entity(hub, Make.Set(Make.Element(counter, 0f, 100f)));
            int rateBefore = World.Control.FixedTickRate;
            World.Control.FixedTickRate = tickRate;
            var previous = ForgeTime.Use(new SteppingClock { Step = frame });
            try
            {
                World.Apply(gas, gas, ticker);
                int frames = UnityEngine.Mathf.RoundToInt(0.5f / frame);
                await Frames.Step(frames);
                gas.TryGetEffectContainer(ticker, out var container);
                return (Value(gas, counter), container != null ? container.DurationRemaining : -1f);
            }
            finally
            {
                ForgeTime.Use(previous);
                World.Control.FixedTickRate = rateBefore;
            }
        }

        /// <summary>
        /// A run's effects stepped once a frame, by the frame's time, so a run at another frame rate came out otherwise,
        /// and no two machines could agree (FEAT-7). With a tick rate set, half a second of game time ticks the same
        /// effect the same number of times at any frame rate.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_7_A_fixed_tick_runs_the_same_at_any_frame_rate([Values] Hub hub) => Live(async () =>
        {
            var slow = await RunAt(hub, 1f / 20f, 60);
            var fast = await RunAt(hub, 1f / 60f, 60);
            Assert.AreEqual(slow.Ticks, fast.Ticks, "the same ticks at 20 and 60 frames a second");
            Assert.That(slow.Left, Is.EqualTo(fast.Left).Within(1e-4f), "and the same time left");
            Assert.That(slow.Ticks, Is.InRange(4f, 6f), "half a second of ten ticks a second");
        });

        /// <summary>A tick rate steps the processes that many times a second of game time, each frame its share.</summary>
        [UnityTest]
        public IEnumerator FEAT_7_A_tick_rate_steps_the_processes_that_often() => Live(async () =>
        {
            int steps = 0;
            var relay = World.Register(new ActionProcess("Counter", EProcessStepPriorityMethod.Manual, 0,
                EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating, () => steps++));
            int rateBefore = World.Control.FixedTickRate;
            World.Control.FixedTickRate = 60;
            var previous = ForgeTime.Use(new SteppingClock { Step = 1f / 20f });
            try
            {
                await Frames.Step();
                int before = steps;
                await Frames.Step(4);
                Assert.That(steps - before, Is.InRange(11, 13), "four frames of a twentieth of a second each, three ticks a frame");
            }
            finally
            {
                ForgeTime.Use(previous);
                World.Control.FixedTickRate = rateBefore;
                relay.TerminateImmediate();
            }
        });

        /// <summary>A sequence waits by the tick, so its wait of half a second takes thirty ticks at sixty a second.</summary>
        [UnityTest]
        public IEnumerator FEAT_7_A_sequence_waits_by_the_tick() => Live(async () =>
        {
            bool done = false;
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Ticked Wait").Delay(0.5f).Do(_ => done = true).Build());
            int rateBefore = World.Control.FixedTickRate;
            World.Control.FixedTickRate = 60;
            var previous = ForgeTime.Use(new SteppingClock { Step = 1f / 20f });
            try
            {
                var relay = ProcessControl.Register(sequence).Run().Relay;
                await Frames.Step(9);
                Assert.IsFalse(done, "nine frames of three ticks are 27 ticks, short of half a second");
                await Frames.Step(2);
                Assert.IsTrue(done, "and eleven frames are past it");
                relay?.TerminateImmediate();
            }
            finally
            {
                ForgeTime.Use(previous);
                World.Control.FixedTickRate = rateBefore;
            }
        });

        /// <summary>Off, as it is by default, every process steps once a frame, as before.</summary>
        [UnityTest]
        public IEnumerator FEAT_7_Off_the_processes_step_once_a_frame() => Live(async () =>
        {
            Assert.AreEqual(0, World.Control.FixedTickRate, "the tick is off by default");
            int steps = 0;
            var relay = World.Register(new ActionProcess("Counter", EProcessStepPriorityMethod.Manual, 0,
                EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating, () => steps++));
            await Frames.Step();
            int before = steps;
            await Frames.Step(4);
            Assert.AreEqual(4, steps - before, "once a frame");
            relay.TerminateImmediate();
        });
    }
}
