using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Dota lab's bot: ten effects from Dota 2, authored as assets under Assets/ProvingGrounds/Dota, each played as
    /// the game plays it. Witch Doctor's Maledict, Monkey King's Jingu Mastery, Phantom Assassin's Coup de Grace, Ancient
    /// Apparition's Ice Blast and Morphling's Attribute Shift, and with them Ursa's Fury Swipes, Slark's Essence Shift,
    /// Anti-Mage's Mana Void, Dazzle's Shallow Grave and Blade Mail. Time runs on a clock of its own, a step a frame, so a
    /// round is exact.
    /// </summary>
    [Category("Lab")]
    public class DotaLabTests : ProvingGroundsTest
    {
        private const string Root = "Assets/ProvingGrounds/Dota";

        // A clock whose frames each take a fixed step
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

        // Rolls that come out as the test says, one after another
        private sealed class ScriptedRandom : IForgeRandom
        {
            private readonly float[] rolls;
            private int next;

            public ScriptedRandom(params float[] rolls) => this.rolls = rolls;

            public float Value => rolls[next++ % rolls.Length];
            public Vector2 InsideUnitCircle => Vector2.zero;
            public Vector3 InsideUnitSphere => Vector3.zero;
        }

        private static T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<T>($"{Root}/{path}.asset");
            Assert.IsNotNull(asset, $"{path} is an asset");
            return asset;
#else
            Assert.Ignore("The Dota effects are read from the asset database, in the editor");
            return null;
#endif
        }

        private static GameplayEffect Effect(string hero, string name) => Load<GameplayEffect>($"Effects/{hero}/{name}");
        private static ForgeAttribute Attribute(string name) => Load<ForgeAttribute>($"Attributes/{name}");

        private ForgeAttribute Health => Attribute("Health");

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static float Base(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.BaseValue : float.NaN;

        private static int Stacks(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.TryGetEffectContainer(effect, out var container) ? (container as AbstractStackingEffectContainer)?.Stacks ?? 1 : 0;

        private static bool Has(IGameplayAbilitySystem gas, GameplayEffect effect) => gas.TryGetEffectContainer(effect, out _);

        // A hero with health, Attack Damage and Healing Received, and whatever else it needs, each current as its base
        // unless it says. A pool, as health, mana or charges, stays between 0 and its base; a stat, as Agility, can be
        // raised past its base by a bonus, as the Dota Hero set has them
        private IGameplayAbilitySystem Hero(Hub hub, string name, float health = 1000f, float healthNow = -1f, float attackDamage = 100f,
            params (string attribute, float current, float @base)[] more)
        {
            var elements = new System.Collections.Generic.List<AttributeSetElement>
            {
                Make.Element(Health, healthNow < 0f ? health : healthNow, health, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(Attribute("Attack Damage"), attackDamage, attackDamage),
                Make.Element(Attribute("Healing Received"), 1f, 1f),
            };
            foreach (var (attribute, current, @base) in more)
            {
                bool pool = attribute == "Mana" || attribute == "Jingu Charges";
                elements.Add(Make.Element(Attribute(attribute), current, @base,
                    pool ? EAttributeOverflowPolicy.ZeroToBase : EAttributeOverflowPolicy.Unlimited));
            }
            return World.Entity(hub, Make.Set(elements.ToArray()), name);
        }

        private GameplayEffect Blow(float amount) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -amount });

        private GameplayEffect Heal(float amount) =>
            Make.Effect("Heal", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = amount });

        private static IForgeClock Clock(float step) => ForgeTime.Use(new SteppingClock { Step = step });

        // ═══════════════════════════════════════════════════════════════════════════
        // The five asked for
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Maledict: 10 damage a second for 12 seconds, and every fourth second a burst of 24% of the health its target
        /// has lost since the curse began. No code: the Attribute Change worker answers the health lost.
        /// </summary>
        [UnityTest]
        public IEnumerator Maledict_bursts_by_the_health_lost_since_it_began([Values] Hub hub) => Live(async () =>
        {
            var witchDoctor = Hero(hub, "Witch Doctor");
            var target = Hero(hub, "Target");
            var maledict = Effect("Witch Doctor", "Maledict");
            await Frames.Step();
            var previous = Clock(1f);
            try
            {
                Assert.IsTrue(World.Apply(witchDoctor, target, maledict), "the curse lands");
                await Frames.Until(() => !Has(target, maledict), 20, "the curse runs out");
            }
            finally
            {
                ForgeTime.Use(previous);
            }

            float left = 1000f;
            for (int tick = 1; tick <= 12; tick++) left -= 10f + (tick % 4 == 0 ? 0.24f * (1000f - left) : 0f);
            Assert.AreEqual(left, Value(target, Health), 0.05f, "twelve ticks of 10, and at the 4th, 8th and 12th 24% of what was lost before");
            Assert.That(Value(target, Health), Is.LessThan(1000f - 120f - 50f), "which is well past 10 a second alone");
        });

        /// <summary>
        /// Jingu Mastery: Monkey King's fourth hit on a target gives him four charged hits, each dealing 80 more and healing
        /// him 30% of what it deals. A charged hit adds no counter, and the hit after the last starts counting again. His
        /// lifesteal is his Lifesteal, which Jingu Mastery's buff raises by 0.3 while he has charges, an effect having no
        /// lifesteal of its own (FN-22).
        /// </summary>
        [UnityTest]
        public IEnumerator Jingu_mastery_charges_monkey_king_on_the_fourth_hit([Values] Hub hub) => Live(async () =>
        {
            // Through the Lifesteal worker its project gives every entity, by the Lifesteal role's attribute, healing the
            // Health role's (FN-15)
            var lifesteal = Attribute("Lifesteal");
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = Health });
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_LIFESTEAL, Attribute = lifesteal });
            var was = ForgeProject.Use(project);
            try
            {
                var monkeyKing = Hero(hub, "Monkey King", health: 1000f, healthNow: 500f, more: new[] { ("Jingu Charges", 0f, 4f), ("Lifesteal", 0f, 0f) });
                var target = Hero(hub, "Target", health: 5000f);
                var attack = Effect("Monkey King", "Monkey King Attack");
                var counter = Effect("Monkey King", "Jingu Mastery Counter");
                var charges = Attribute("Jingu Charges");
                await Frames.Step();

                for (int hit = 1; hit <= 3; hit++)
                {
                    Assert.IsTrue(World.Apply(monkeyKing, target, attack), $"hit {hit} lands");
                    await Frames.Step();
                    Assert.AreEqual(hit, Stacks(target, counter), $"hit {hit} adds a counter");
                }
                Assert.IsTrue(World.Apply(monkeyKing, target, attack), "the fourth hit lands");
                await Frames.Step(2);
                Assert.AreEqual(4f, Value(monkeyKing, charges), "and gives four charged hits");
                Assert.IsFalse(Has(target, counter), "its counter spent");
                Assert.AreEqual(5000f - 4 * 100f, Value(target, Health), "four uncharged hits of 100");
                Assert.AreEqual(500f, Value(monkeyKing, Health), "which heal nothing");
                Assert.AreEqual(0.3f, Value(monkeyKing, lifesteal), 0.001f, "and his charges raise his Lifesteal by 0.3");

                for (int hit = 1; hit <= 4; hit++)
                {
                    Assert.IsTrue(World.Apply(monkeyKing, target, attack), $"charged hit {hit} lands");
                    await Frames.Step();
                    Assert.AreEqual(4f - hit, Value(monkeyKing, charges), $"and spends a charge");
                }
                Assert.AreEqual(5000f - 400f - 4 * 180f, Value(target, Health), 0.01f, "four charged hits of 100 + 80");
                Assert.AreEqual(500f + 4 * 0.3f * 180f, Value(monkeyKing, Health), 0.01f, "each healing 30% of its 180");
                Assert.IsFalse(Has(target, counter), "and none added a counter");
                await Frames.Step();
                Assert.AreEqual(0f, Value(monkeyKing, lifesteal), 0.001f, "his last charge spent, his Lifesteal falls back");

                Assert.IsTrue(World.Apply(monkeyKing, target, attack), "the next hit lands");
                await Frames.Step();
                Assert.AreEqual(1, Stacks(target, counter), "uncharged, and counting again");
                Assert.AreEqual(5000f - 400f - 720f - 100f, Value(target, Health), 0.01f, "for 100");
            }
            finally
            {
                ForgeProject.Use(was);
                Object.DestroyImmediate(project);
            }
        });

        /// <summary>
        /// Coup de Grace: a 15% chance on each of Phantom Assassin's attacks of a critical strike of 325%. A roll under
        /// 0.15 crits; the rolls are the test's own.
        /// </summary>
        [UnityTest]
        public IEnumerator Coup_de_grace_crits_on_its_chance([Values] Hub hub) => Live(async () =>
        {
            var phantomAssassin = Hero(hub, "Phantom Assassin");
            var target = Hero(hub, "Target", health: 5000f);
            var attack = Effect("Phantom Assassin", "Phantom Assassin Attack");
            await Frames.Step();
            var previous = ForgeRandom.Use(new ScriptedRandom(0.1f, 0.9f, 0.149f, 0.151f));
            try
            {
                float before = Value(target, Health);
                float[] dealt = new float[4];
                for (int i = 0; i < 4; i++)
                {
                    Assert.IsTrue(World.Apply(phantomAssassin, target, attack), $"attack {i + 1} lands");
                    await Frames.Step();
                    dealt[i] = before - Value(target, Health);
                    before = Value(target, Health);
                }
                Assert.AreEqual(325f, dealt[0], 0.01f, "a roll of 0.1 crits for 325%");
                Assert.AreEqual(100f, dealt[1], 0.01f, "one of 0.9 doesn't");
                Assert.AreEqual(325f, dealt[2], 0.01f, "nor does 0.149 miss");
                Assert.AreEqual(100f, dealt[3], 0.01f, "and 0.151 is past the chance");
            }
            finally
            {
                ForgeRandom.Use(previous);
            }
        });

        /// <summary>
        /// Ice Blast: 325 as it lands, then frostbite for 10 seconds: 20 a second, no healing, and a blow that leaves its
        /// target below 13% of its maximum health shatters it. Its rules go with it: Healing Received held at 0, which
        /// Health's gains are scaled by, and an incoming Execute stage (TW-15).
        /// </summary>
        [UnityTest]
        public IEnumerator Ice_blast_blocks_healing_and_shatters([Values] Hub hub) => Live(async () =>
        {
            var apparition = Hero(hub, "Ancient Apparition");
            var target = Hero(hub, "Target");
            var other = Hero(hub, "Other");
            var iceBlast = Effect("Ancient Apparition", "Ice Blast");
            await Frames.Step();
            var previous = Clock(1f);
            try
            {
                Assert.IsTrue(World.Apply(apparition, target, iceBlast), "the blast lands");
                await Frames.Step();
                float hit = 1000f - Value(target, Health);
                Assert.That(hit, Is.EqualTo(325f + 20f).Within(0.01f).Or.EqualTo(325f + 40f).Within(0.01f), "325 as it lands, then 20 a second");

                float before = Value(target, Health);
                Assert.IsTrue(World.Apply(other, target, Heal(100f)), "a heal lands");
                await Frames.Step();
                Assert.That(Value(target, Health), Is.LessThanOrEqualTo(before), "and heals nothing");

                // A blow that leaves it under 130 of its 1000 shatters it
                float now = Value(target, Health);
                Assert.IsTrue(World.Apply(other, target, Blow(now - 100f)), "a blow that would leave it 100");
                await Frames.Step();
                Assert.AreEqual(0f, Value(target, Health), "shatters it");

                // Once it's over, healing heals again and a blow is a blow
                Assert.IsTrue(World.Apply(apparition, other, iceBlast), "a blast lands on another");
                await Frames.Until(() => !Has(other, iceBlast), 15, "and runs out");
                float after = Value(other, Health);
                Assert.AreEqual(1000f - 325f - 10 * 20f, after, 0.01f, "325, then ten ticks of 20");
                Assert.IsTrue(World.Apply(apparition, other, Heal(100f)), "a heal lands");
                await Frames.Step();
                Assert.AreEqual(after + 100f, Value(other, Health), 0.01f, "and heals");
                Assert.IsTrue(World.Apply(apparition, other, Blow(Value(other, Health) - 100f)), "a blow that leaves it 100");
                await Frames.Step();
                Assert.AreEqual(100f, Value(other, Health), 0.01f, "leaves it 100");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>
        /// Attribute Shift: a point of Strength into Agility four times a second, for good, until Strength is down to 1;
        /// the shift the other way stops it as it starts. What's shifted stays.
        /// </summary>
        [UnityTest]
        public IEnumerator Attribute_shift_moves_strength_into_agility_and_back([Values] Hub hub) => Live(async () =>
        {
            var strength = Attribute("Strength");
            var agility = Attribute("Agility");
            var morphling = Hero(hub, "Morphling", more: new[] { ("Strength", 20f, 20f), ("Agility", 20f, 20f) });
            var weakling = Hero(hub, "Weakling", more: new[] { ("Strength", 3f, 3f), ("Agility", 20f, 20f) });
            var toAgility = Effect("Morphling", "Attribute Shift (Agility Gain)");
            var toStrength = Effect("Morphling", "Attribute Shift (Strength Gain)");
            await Frames.Step();
            var previous = Clock(0.25f);
            try
            {
                Assert.IsTrue(World.Apply(morphling, morphling, toAgility), "the shift to Agility starts");
                await Frames.Until(() => Value(morphling, strength) <= 16f, 12, "it shifts four points");
                Assert.AreEqual(40f, Value(morphling, strength) + Value(morphling, agility), 0.01f, "point for point");
                Assert.AreEqual(Value(morphling, agility), Base(morphling, agility), 0.01f, "for good");

                Assert.IsTrue(World.Apply(morphling, morphling, toStrength), "the shift back starts");
                await Frames.Step(2);
                Assert.IsFalse(Has(morphling, toAgility), "and stops the shift to Agility");
                float strengthNow = Value(morphling, strength);
                await Frames.Until(() => Value(morphling, strength) >= strengthNow + 3f, 12, "it shifts three back");
                Assert.AreEqual(40f, Value(morphling, strength) + Value(morphling, agility), 0.01f, "point for point");
                Assert.IsTrue(morphling.RemoveGameplayEffect(toStrength.Tags.AssetTag), "the shift is toggled off");
                await Frames.Step(2);
                float held = Value(morphling, strength);
                await Frames.Step(4);
                Assert.AreEqual(held, Value(morphling, strength), 0.01f, "and what's shifted stays");

                Assert.IsTrue(World.Apply(weakling, weakling, toAgility), "a weakling shifts to Agility");
                await Frames.Step(12);
                Assert.AreEqual(1f, Value(weakling, strength), 0.01f, "down to 1 Strength and no further");
                Assert.AreEqual(22f, Value(weakling, agility), 0.01f, "for 2 Agility");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // Five more
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fury Swipes: each of Ursa's attacks deals 20 more for each stack its target holds, then adds one; they last 15
        /// seconds from the last attack. No code: Asset Part reads the stacks on the target.
        /// </summary>
        [UnityTest]
        public IEnumerator Fury_swipes_deal_more_for_each_stack([Values] Hub hub) => Live(async () =>
        {
            var ursa = Hero(hub, "Ursa");
            var target = Hero(hub, "Target", health: 5000f);
            var attack = Effect("Ursa", "Ursa Attack");
            var swipes = Effect("Ursa", "Fury Swipes");
            await Frames.Step();
            var previous = Clock(1f);
            try
            {
                float before = Value(target, Health);
                for (int hit = 0; hit < 3; hit++)
                {
                    Assert.IsTrue(World.Apply(ursa, target, attack), $"attack {hit + 1} lands");
                    await Frames.Step();
                    Assert.AreEqual(100f + 20f * hit, before - Value(target, Health), 0.01f, $"for 100 and 20 for each of its {hit} stacks");
                    before = Value(target, Health);
                }
                Assert.AreEqual(3, Stacks(target, swipes), "three stacks");

                await Frames.Until(() => !Has(target, swipes), 20, "the stacks run out 15 seconds after the last");
                Assert.IsTrue(World.Apply(ursa, target, attack), "the next attack lands");
                await Frames.Step();
                Assert.AreEqual(100f, before - Value(target, Health), 0.01f, "for 100 again");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>
        /// Essence Shift: each of Slark's attacks takes a point of each attribute from its target and gives him a point of
        /// Agility, each for 20 seconds of its own.
        /// </summary>
        [UnityTest]
        public IEnumerator Essence_shift_steals_a_point_a_hit([Values] Hub hub) => Live(async () =>
        {
            var strength = Attribute("Strength");
            var agility = Attribute("Agility");
            var intelligence = Attribute("Intelligence");
            var attributes = new[] { ("Strength", 20f, 20f), ("Agility", 20f, 20f), ("Intelligence", 20f, 20f) };
            var slark = Hero(hub, "Slark", more: attributes);
            var target = Hero(hub, "Target", health: 5000f, more: attributes);
            var attack = Effect("Slark", "Slark Attack");
            await Frames.Step();
            var previous = Clock(1f);
            try
            {
                for (int hit = 0; hit < 3; hit++)
                {
                    Assert.IsTrue(World.Apply(slark, target, attack), $"attack {hit + 1} lands");
                    await Frames.Step();
                }
                await Frames.Step();
                Assert.AreEqual(17f, Value(target, strength), 0.01f, "three points of Strength taken");
                Assert.AreEqual(17f, Value(target, agility), 0.01f, "of Agility");
                Assert.AreEqual(17f, Value(target, intelligence), 0.01f, "and of Intelligence");
                var stolen = Effect("Slark", "Essence Shift (Stolen)");
                Assert.IsTrue(Has(slark, stolen), "Slark holds what he took");
                Assert.AreEqual(3, Stacks(slark, stolen), "in three stacks");
                Assert.AreEqual(23f, Value(slark, agility), 0.01f, "and Slark has three more Agility");

                await Frames.Until(() => Value(target, strength) >= 18f, 25, "the first point comes back");
                Assert.AreEqual(18f, Value(target, strength), 0.01f, "one point, the others on their own time");
                await Frames.Until(() => Value(target, strength) >= 20f && Value(slark, agility) <= 20f, 10, "every point comes back");
                Assert.AreEqual(20f, Value(slark, agility), 0.01f, "and Slark's go");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>Mana Void: 0.8 damage for each point of mana its target is missing.</summary>
        [UnityTest]
        public IEnumerator Mana_void_deals_by_the_mana_missing([Values] Hub hub) => Live(async () =>
        {
            var antiMage = Hero(hub, "Anti-Mage");
            var drained = Hero(hub, "Drained", more: new[] { ("Mana", 100f, 400f) });
            var full = Hero(hub, "Full", more: new[] { ("Mana", 400f, 400f) });
            var manaVoid = Effect("Anti-Mage", "Mana Void");
            await Frames.Step();

            Assert.IsTrue(World.Apply(antiMage, drained, manaVoid), "it lands on a drained hero");
            Assert.IsTrue(World.Apply(antiMage, full, manaVoid), "and on one at full mana");
            await Frames.Step();
            Assert.AreEqual(1000f - 0.8f * 300f, Value(drained, Health), 0.01f, "0.8 for each of the 300 missing");
            Assert.AreEqual(1000f, Value(full, Health), 0.01f, "and nothing where none is");
        });

        /// <summary>Shallow Grave: for 5 seconds its target can't die; no blow takes its health below 1.</summary>
        [UnityTest]
        public IEnumerator Shallow_grave_keeps_its_target_alive([Values] Hub hub) => Live(async () =>
        {
            var dazzle = Hero(hub, "Dazzle");
            var ally = Hero(hub, "Ally", healthNow: 200f);
            var enemy = Hero(hub, "Enemy");
            var grave = Effect("Dazzle", "Shallow Grave");
            await Frames.Step();
            var previous = Clock(1f);
            try
            {
                Assert.IsTrue(World.Apply(dazzle, ally, grave), "the grave lands on an ally");
                Assert.IsTrue(World.Apply(enemy, ally, Blow(500f)), "a blow of 500 lands");
                await Frames.Step();
                Assert.AreEqual(1f, Value(ally, Health), 0.01f, "and leaves it 1");
                Assert.IsTrue(World.Apply(enemy, ally, Blow(500f)), "and another");
                await Frames.Step();
                Assert.AreEqual(1f, Value(ally, Health), 0.01f, "still 1");

                await Frames.Until(() => !Has(ally, grave), 10, "the grave runs out");
                Assert.IsTrue(World.Apply(enemy, ally, Blow(500f)), "a blow lands after it");
                await Frames.Step();
                Assert.AreEqual(0f, Value(ally, Health), 0.01f, "and the ally falls");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>
        /// Blade Mail: for 4.5 seconds, 85% of what its wearer takes goes back to whoever dealt it, and two mails don't
        /// return it to each other forever.
        /// </summary>
        [UnityTest]
        public IEnumerator Blade_mail_returns_the_damage_taken([Values] Hub hub) => Live(async () =>
        {
            var wearer = Hero(hub, "Wearer");
            var attacker = Hero(hub, "Attacker");
            var mail = Effect("Items", "Blade Mail");
            await Frames.Step();

            Assert.IsTrue(World.Apply(wearer, wearer, mail), "the wearer puts it on");
            Assert.IsTrue(World.Apply(attacker, wearer, Blow(200f)), "a blow of 200 lands on the wearer");
            await Frames.Step(2);
            Assert.AreEqual(800f, Value(wearer, Health), 0.01f, "who takes it");
            Assert.AreEqual(1000f - 0.85f * 200f, Value(attacker, Health), 0.01f, "and 85% of it goes back");

            Assert.IsTrue(World.Apply(attacker, attacker, mail), "the attacker puts one on too");
            Assert.IsTrue(World.Apply(attacker, wearer, Blow(100f)), "and strikes for 100");
            await Frames.Step(3);
            Assert.AreEqual(700f, Value(wearer, Health), 0.01f, "the wearer takes 100, and nothing back of what it returned");
            Assert.AreEqual(1000f - 170f - 85f, Value(attacker, Health), 0.01f, "the attacker takes 85 back, once");
        });
    }
}
