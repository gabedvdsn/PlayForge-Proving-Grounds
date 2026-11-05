using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Gameplay cues (FEAT-1): an effect changed attributes and tags and nothing else, so what it showed and sounded was
    /// each game's own code. An effect lists cue handlers, as it lists workers: a burst as it lands or ticks, a lasting
    /// cue while it's on, and a camera shake through the shaker the game gives.
    /// </summary>
    public class GameplayCueTests : ProvingGroundsTest
    {
        // The game's camera, which counts the shakes it's asked for
        private sealed class CountingShaker : ICameraShaker
        {
            public readonly List<(float Strength, float Duration)> Shakes = new();
            public void Shake(float strength, float duration) => Shakes.Add((strength, duration));
        }

        // Gives PlayForge the game's shaker, and gives back the one before as the round ends
        private sealed class Shaker : IDisposable
        {
            public readonly CountingShaker Camera = new();
            private readonly ICameraShaker previous = ForgeCues.Shaker;

            public Shaker() => ForgeCues.Shaker = Camera;

            public void Dispose() => ForgeCues.Shaker = previous;
        }

        // A cue that writes down each moment it plays at
        private sealed class RecordingCue : AbstractCueHandler
        {
            public readonly List<string> Moments = new();
            public override void OnApplied(CueContext cue) => Moments.Add("Applied");
            public override void OnTicked(CueContext cue) => Moments.Add($"Ticked {cue.Ticks}");
            public override void OnRemoved(CueContext cue) => Moments.Add("Removed");
            public override void Begin(CueContext cue) => Moments.Add("Begin");
            public override void End(CueContext cue) => Moments.Add("End");
        }

        // A second of game time each frame
        private sealed class SecondClock : IForgeClock
        {
            public float DeltaTime => 1f;
            public float UnscaledDeltaTime => 1f;
            public float FixedDeltaTime => 1f;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        /// <summary>A shake cue goes through the game's shaker as its effect lands, with its strength and its length (FEAT-1).</summary>
        [UnityTest]
        public IEnumerator FEAT_1_A_shake_cue_goes_through_the_games_shaker([Values] Hub hub) => Live(async () =>
        {
            using var shaker = new Shaker();
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var slam = Make.Effect("Slam", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
            slam.Cues.Add(new CameraShakeCue { Strength = 2f, Duration = 0.3f });

            Assert.IsTrue(World.Apply(gas, gas, slam), "a slam lands");
            await Frames.Step();
            CollectionAssert.AreEqual(new[] { (2f, 0.3f) }, shaker.Camera.Shakes, "and shakes the camera once, as hard and as long as its cue says");
        });

        /// <summary>
        /// A lasting cue lasts as its effect does: a prefab placed as the effect lands, and taken away as it goes. A restore
        /// ends and begins it again as the effect leaves and comes back, and plays no burst (FEAT-1, FEAT-9).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_1_A_lasting_cue_lasts_as_its_effect_does([Values] Hub hub) => Live(async () =>
        {
            using var shaker = new Shaker();
            var gas = World.Entity(hub);
            var glow = new GameObject("FEAT-1 Glow");
            glow.SetActive(false);
            try
            {
                var aura = Make.Effect("Aura", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
                aura.Cues.Add(new PrefabCue { Prefab = glow, WhileActive = true });
                aura.Cues.Add(new CameraShakeCue { Strength = 1f, Duration = 0.1f });
                int Glows() => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Count(t => t.name == "FEAT-1 Glow(Clone)");

                Assert.IsTrue(World.Apply(gas, gas, aura), "an aura lands");
                await Frames.Step();
                Assert.AreEqual(1, Glows(), "and its glow is placed");
                Assert.AreEqual(1, shaker.Camera.Shakes.Count, "with its burst");

                var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
                catalog.Add("aura", aura);
                Assert.IsTrue(ForgeSave.Restore(gas, ForgeSave.Capture(gas, catalog), catalog), "a save of it loads");
                await Frames.Step();
                Assert.AreEqual(1, Glows(), "and the glow, ended and begun again, is one");
                Assert.AreEqual(1, shaker.Camera.Shakes.Count, "and no burst played again");

                gas.RemoveGameplayEffect(aura);
                await Frames.Step(2);
                Assert.AreEqual(0, Glows(), "the aura goes, and its glow with it");
            }
            finally
            {
                Object.Destroy(glow);
            }
        });

        /// <summary>
        /// A cue plays at each moment of its effect, in order: its burst and its lasting cue's beginning as it lands, a burst
        /// as each tick executes, and its burst and its lasting cue's end as it comes off (FEAT-1).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_1_A_cue_plays_at_each_moment_of_its_effect([Values] Hub hub) => Live(async () =>
        {
            var previous = ForgeTime.Use(new SecondClock());
            try
            {
                var health = Make.Attribute("Health");
                var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
                var burn = Make.Effect("Burn", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 3f, Target = health, Magnitude = -1f, PeriodicTicks = true, Ticks = 3,
                });
                var cue = new RecordingCue();
                burn.Cues.Add(cue);

                Assert.IsTrue(World.Apply(gas, gas, burn), "a burn lands");
                await Frames.Until(() => !gas.TryGetEffectContainer(burn, out _), 8, "and runs its course");
                CollectionAssert.AreEqual(new[] { "Applied", "Begin", "Ticked 1", "Ticked 1", "Ticked 1", "Removed", "End" }, cue.Moments,
                    "each moment, in order");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });
    }
}
