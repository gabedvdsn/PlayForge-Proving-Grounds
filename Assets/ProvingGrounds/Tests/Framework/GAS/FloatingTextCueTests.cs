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
    /// A Floating Text cue (EP-55): a text that floats where its placement says, as a hit's damage, the gold a kill gave or
    /// a status's name; its number written as asked; rising, fading and going back to its pool; merging, staggered and
    /// capped on one entity. And an effect's On Hit cues, which hear the value an impact changed, for damage numbers.
    /// </summary>
    public class FloatingTextCueTests : ProvingGroundsTest
    {
        // Each text still floating ends, so the next test starts with none, and the pool lets go of them
        [TearDown]
        public void Clean()
        {
            foreach (var text in Object.FindObjectsByType<ForgeFloatingText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (text != null) Object.Destroy(text.gameObject);
            ForgeCuePool.Clear();
        }

        // The texts floating now
        private static List<ForgeFloatingText> Floating() =>
            Object.FindObjectsByType<ForgeFloatingText>(FindObjectsSortMode.None).Where(each => each.Floating).ToList();

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

        // A cue that writes down each burst's moment, the one it's on, and its magnitude
        private sealed class Probe : AbstractCueHandler
        {
            public readonly List<(ECueMoment Moment, IGameplayAbilitySystem On, float Magnitude)> Heard = new();
            public override void Burst(CueContext cue) => Heard.Add((cue.Moment, cue.Target, cue.Magnitude));
        }

        /// <summary>
        /// A floating text says its Text with its number written as its Value says: a sign, shortened, and none for 0
        /// (EP-55).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_55_A_floating_text_says_its_number_as_written() => Live(async () =>
        {
            var gas = World.Entity(Hub.Runtime);
            var gold = new FloatingTextCue { Text = "{value} gold", Value = new FloatingTextValue { Absolute = false, PlusSign = true } };
            var big = new FloatingTextCue { Value = new FloatingTextValue { Abbreviate = true } };
            var none = new FloatingTextCue();

            ForgeCues.Burst(gold, new CueContext(gas, ECueMoment.Raised, magnitude: 50f));
            ForgeCues.Burst(big, new CueContext(gas, ECueMoment.Raised, magnitude: -1234f));
            ForgeCues.Burst(none, new CueContext(gas, ECueMoment.Raised, magnitude: 0f));
            await Frames.Step();

            CollectionAssert.AreEquivalent(new[] { "+50 gold", "1.2k" }, Floating().Select(each => each.Text).ToList(),
                "its number as written, and no text for 0");
        });

        /// <summary>A floating text rises on the game's clock, and goes back to its pool as its life ends (EP-55).</summary>
        [UnityTest]
        public IEnumerator EP_55_A_floating_text_rises_and_goes_back_to_its_pool() => Live(async () =>
        {
            var previous = ForgeTime.Use(new SecondClock());
            try
            {
                var gas = World.Entity(Hub.Runtime);
                var rising = new FloatingTextCue
                {
                    Placement = new CuePlacement(),
                    Motion = new FloatingTextMotion { Lifetime = 3f, Velocity = new Vector3(0f, 1f, 0f), RandomVelocity = Vector3.zero, Drag = 0f },
                };

                ForgeCues.Burst(rising, new CueContext(gas, ECueMoment.Raised, magnitude: 7f));
                var text = Floating().Single();
                Assert.AreEqual(0f, text.transform.position.y, 0.001f, "it appears where it's placed");
                await Frames.Step();
                Assert.That(text.transform.position.y, Is.GreaterThan(0.5f), "and rises");
                await Frames.Step(4);
                Assert.IsFalse(text.Floating, "its life over, it's done");
                Assert.IsFalse(text.gameObject.activeSelf, "and waits in its pool");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>A number close behind the last on one adds to it, which shows the sum (EP-55).</summary>
        [UnityTest]
        public IEnumerator EP_55_Numbers_close_behind_merge_into_one() => Live(async () =>
        {
            var gas = World.Entity(Hub.Runtime);
            var burn = new FloatingTextCue { Stacking = new FloatingTextStacking { MergeWithin = 1f } };

            ForgeCues.Burst(burn, new CueContext(gas, ECueMoment.Raised, magnitude: -10f));
            ForgeCues.Burst(burn, new CueContext(gas, ECueMoment.Raised, magnitude: -15f));
            await Frames.Step();

            var texts = Floating();
            Assert.AreEqual(1, texts.Count, "one text");
            Assert.AreEqual("25", texts[0].Text, "showing the sum");
        });

        /// <summary>
        /// Texts on one are staggered, each in the lowest place free, and past the cap the oldest goes first (EP-55).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_55_Texts_on_one_stagger_and_the_oldest_goes_past_the_cap() => Live(async () =>
        {
            var gas = World.Entity(Hub.Runtime);
            var hits = new FloatingTextCue
            {
                Placement = new CuePlacement(),
                Motion = new FloatingTextMotion { Velocity = Vector3.zero, RandomVelocity = Vector3.zero },
                Stacking = new FloatingTextStacking { Stagger = 0.5f, MaxShown = 2 },
            };

            for (int i = 1; i <= 3; i++) ForgeCues.Burst(hits, new CueContext(gas, ECueMoment.Raised, magnitude: i));

            var texts = Floating().OrderBy(each => each.transform.position.y).ToList();
            CollectionAssert.AreEquivalent(new[] { "2", "3" }, texts.Select(each => each.Text).ToList(), "the oldest went past the cap");
            Assert.AreEqual(0.5f, texts[1].transform.position.y - texts[0].transform.position.y, 0.001f, "and one floats a stagger above the other");
            await Frames.Step();
        });

        /// <summary>
        /// An effect's On Hit cues play as an impact it deals lands, on the one it hit, told the value it changed, for a
        /// damage number (EP-55).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_55_An_effects_on_hit_cue_hears_the_value_dealt([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var strike = Make.Effect("Strike", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -5f });
            var probe = new Probe();
            var cues = new CuesEffectComponent();
            cues.OnHit.Add(probe);
            strike.Components.Add(cues);

            Assert.IsTrue(World.Apply(caster, target, strike), "a strike lands");
            await Frames.Step();

            Assert.AreEqual(1, probe.Heard.Count, "its hit plays once");
            Assert.AreEqual(ECueMoment.Hit, probe.Heard[0].Moment, "as a hit");
            Assert.AreSame(target, probe.Heard[0].On, "on the one it hit");
            Assert.AreEqual(-5f, probe.Heard[0].Magnitude, 0.001f, "told the value it changed");
        });

        /// <summary>A floating text in an effect's On Apply names the effect, as a status does as it lands (EP-55).</summary>
        [UnityTest]
        public IEnumerator EP_55_A_floating_text_names_the_effect_that_raised_it([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var stun = Make.Effect("Stunned", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            var cues = new CuesEffectComponent();
            cues.OnApply.Add(new FloatingTextCue { Text = "{name}" });
            stun.Components.Add(cues);

            Assert.IsTrue(World.Apply(gas, gas, stun), "a stun lands");
            await Frames.Step();

            Assert.That(Floating().Select(each => each.Text), Has.Member("Stunned"), "and its name floats");
        });
    }
}
