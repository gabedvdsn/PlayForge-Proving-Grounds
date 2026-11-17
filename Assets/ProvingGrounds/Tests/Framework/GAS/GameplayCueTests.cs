using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        // Writes down each cue ForgeCues plays, by its event and its moment, until the round ends
        private sealed class Listening : IDisposable
        {
            public readonly List<(ECueEvent Event, ECueMoment Moment, CueContext Cue)> Heard = new();
            public Listening() => ForgeCues.Played += Hear;
            private void Hear(ECueEvent cueEvent, AbstractCueHandler handler, in CueContext cue) => Heard.Add((cueEvent, cue.Moment, cue));
            public void Dispose() => ForgeCues.Played -= Hear;
        }

        // A cue that plays every burst one way, writing down what raised it
        private sealed class BurstingCue : AbstractCueHandler
        {
            public readonly List<ECueMoment> Bursts = new();
            public override void Burst(CueContext cue) => Bursts.Add(cue.Moment);
        }

        // A cue that throws as it plays
        private sealed class BreakingCue : AbstractCueHandler
        {
            public override void Burst(CueContext cue) => throw new InvalidOperationException("the cue broke");
        }

        /// <summary>
        /// One dispatch plays every cue, whatever raised it (EP-46): an effect's cues play through ForgeCues as they did,
        /// each told what raised it and who's behind it, a lasting cue's beginning and its end sharing a key.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_46_An_effects_cues_play_through_one_dispatch([Values] Hub hub) => Live(async () =>
        {
            var previous = ForgeTime.Use(new SecondClock());
            using var listening = new Listening();
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
                    "its cue plays as it did");
                var heard = listening.Heard.Where(each => each.Cue.Spec?.Base == burn).ToList();
                CollectionAssert.AreEqual(new (ECueEvent, ECueMoment)[]
                {
                    (ECueEvent.Burst, ECueMoment.Applied), (ECueEvent.Begin, ECueMoment.Applied), (ECueEvent.Burst, ECueMoment.Ticked),
                    (ECueEvent.Burst, ECueMoment.Ticked), (ECueEvent.Burst, ECueMoment.Ticked), (ECueEvent.Burst, ECueMoment.Removed),
                    (ECueEvent.End, ECueMoment.Removed),
                }, heard.Select(each => (each.Event, each.Moment)).ToArray(), "each heard through ForgeCues, with what raised it");
                Assert.That(heard.Select(each => each.Cue.Source), Has.All.SameAs(gas), "and who's behind it");
                Assert.IsNotNull(heard[1].Cue.Key, "its lasting cue has a key");
                Assert.AreSame(heard[1].Cue.Key, heard[6].Cue.Key, "which its beginning and its end share");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>
        /// A cue can be raised for any moment, and by a game's own code (EP-46): a handler written for effects plays its
        /// OnApplied for a moment that isn't an effect's, one that plays every burst one way hears what raised it, and one
        /// that throws is reported while the rest play on.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_46_A_cue_plays_for_any_moment_and_a_broken_one_stops_none() => Live(async () =>
        {
            var gas = World.Entity(Hub.Runtime);
            await Frames.Step();
            var forEffects = new RecordingCue();
            var bursting = new BurstingCue();

            ForgeCues.Burst(forEffects, new CueContext(gas, ECueMoment.Died));
            ForgeCues.Burst(bursting, new CueContext(gas, ECueMoment.Died, point: new Vector3(1f, 2f, 3f)));
            LogAssert.Expect(LogType.Exception, new Regex("the cue broke"));
            ForgeCues.Burst(new AbstractCueHandler[] { new BreakingCue(), bursting }, new CueContext(gas, ECueMoment.Raised));

            CollectionAssert.AreEqual(new[] { "Applied" }, forEffects.Moments, "a handler written for effects plays its OnApplied");
            CollectionAssert.AreEqual(new[] { ECueMoment.Died, ECueMoment.Raised }, bursting.Bursts,
                "one that plays every burst hears each moment, the second after one that broke");
            Assert.AreEqual(new Vector3(1f, 2f, 3f), new CueContext(gas, ECueMoment.Died, point: new Vector3(1f, 2f, 3f)).Position,
                "and a cue plays where its raiser says");
        });

        /// <summary>
        /// Cues can be muted, all of them, as on a server, or one entity's, as one off screen (EP-46): nothing bursts or
        /// begins while muted, and a lasting cue that began still ends.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_46_Muted_cues_dont_play_and_a_lasting_one_still_ends([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var aura = Make.Effect("Aura", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            var cue = new RecordingCue();
            aura.Cues.Add(cue);
            try
            {
                ForgeCues.Enabled = false;
                Assert.IsTrue(World.Apply(gas, gas, aura), "an aura lands");
                await Frames.Step();
                CollectionAssert.IsEmpty(cue.Moments, "and while cues are off, nothing plays");

                ForgeCues.Enabled = true;
                gas.RemoveGameplayEffect(aura);
                await Frames.Step(2);
                CollectionAssert.AreEqual(new[] { "Removed", "End" }, cue.Moments, "its end plays once they're on");

                cue.Moments.Clear();
                ForgeCues.Mute(gas, true);
                Assert.IsTrue(ForgeCues.IsMuted(gas), "the entity is muted");
                Assert.IsTrue(World.Apply(gas, gas, aura), "the aura lands again");
                await Frames.Step();
                gas.RemoveGameplayEffect(aura);
                await Frames.Step(2);
                CollectionAssert.AreEqual(new[] { "End" }, cue.Moments, "a muted entity plays nothing but a lasting cue's end");
            }
            finally
            {
                ForgeCues.Enabled = true;
                ForgeCues.Mute(gas, false);
            }
        });
    }
}
