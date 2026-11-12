using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-12, Decision 52: block and evade. A Block or Evade stage in a hit's pipeline, or one an incoming modifier brings,
    /// rolls once as the effect lands, and on a roll that comes up nothing of the effect lands; the one hit hears Blocked or
    /// Evaded, and the dealer Hit Blocked or Hit Evaded. A lasting effect's ticks aren't rolled again.
    /// </summary>
    public class BlockTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag marked;

        private sealed class ScriptedRandom : IForgeRandom
        {
            private readonly float[] rolls;
            public ScriptedRandom(params float[] rolls) => this.rolls = rolls;
            public int Drawn { get; private set; }
            public float Value => rolls[Drawn++ % rolls.Length];
            public Vector2 InsideUnitCircle => Vector2.zero;
            public Vector3 InsideUnitSphere => Vector3.zero;
        }

        private IForgeRandom previousRandom;
        private bool randomSet;
        private readonly List<Object> made = new();

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            marked = Make.NewTag("Test.TW12.Marked");
        }

        [TearDown]
        public void PutBack()
        {
            if (randomSet) ForgeRandom.Use(previousRandom);
            randomSet = false;
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private ScriptedRandom Rolls(params float[] rolls)
        {
            var scripted = new ScriptedRandom(rolls);
            var was = ForgeRandom.Use(scripted);
            if (!randomSet) previousRandom = was;
            randomSet = true;
            return scripted;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) =>
            World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // An effect whose impact's pipeline holds the stages given
        private GameplayEffect Piped(GameplayEffect effect, params AbstractValueStage[] stages)
        {
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            made.Add(pipeline);
            pipeline.Stages = new List<AbstractValueStage>(stages);
            effect.ImpactSpecification.Pipeline = pipeline;
            return effect;
        }

        private GameplayEffect Marking(float damage) => Make.Effect("Strike", new EffectConfig
        {
            Policy = EEffectDurationPolicy.Durational, Duration = 5f, Target = health, Magnitude = -damage,
            GrantedTags = new List<Tag> { marked },
        });

        // Who heard which of the four, on either side
        private List<Tag> Hearing(IGameplayAbilitySystem gas, params Tag[] tags)
        {
            var heard = new List<Tag>();
            var listening = Make.Ability($"Ears {gas.GetHashCode()}", Make.Stage());
            listening.Definition.Triggers = new List<AbilityTrigger>();
            foreach (var tag in tags) listening.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.GameplayEvent, Tag = tag });
            Assert.That(gas.GetAbilitySystem().GiveAbility(listening, 1, out _), "it listens");
            gas.GetAbilitySystem().Callbacks.OnGameplayEvent += (tag, _) => heard.Add(tag);
            return heard;
        }

        /// <summary>A blocked effect lands nothing, its damage nor its tags, and each side hears it.</summary>
        [UnityTest]
        public IEnumerator TW_12_A_blocked_effect_lands_nothing([Values] Hub hub) => Live(async () =>
        {
            var hero = Fighter(hub, "Hero");
            var foe = Fighter(hub, "Foe");
            await Frames.Step();
            var heroHeard = Hearing(hero, Tags.BLOCKED, Tags.HIT_BLOCKED);
            var foeHeard = Hearing(foe, Tags.BLOCKED, Tags.HIT_BLOCKED);

            var strike = Piped(Marking(30f), new BlockStage { Chance = 1f });
            Assert.IsFalse(World.Apply(foe, hero, strike), "blocked, it doesn't land");
            await Frames.Step(3);
            Assert.AreEqual(100f, Value(hero, health), 0.01f, "no damage");
            Assert.AreEqual(-1, hero.GetTagCache().GetWeight(marked), "nor its tags");
            Assert.That(heroHeard, Is.EqualTo(new List<Tag> { Tags.BLOCKED }), "the one hit hears Blocked");
            Assert.That(foeHeard, Is.EqualTo(new List<Tag> { Tags.HIT_BLOCKED }), "the dealer, Hit Blocked");
        });

        /// <summary>An evade rolls its chance: a miss lets the effect land, and one that comes up stops it.</summary>
        [UnityTest]
        public IEnumerator TW_12_An_evade_rolls_its_chance([Values] Hub hub) => Live(async () =>
        {
            var hero = Fighter(hub, "Hero");
            var foe = Fighter(hub, "Foe");
            await Frames.Step();
            var heroHeard = Hearing(hero, Tags.EVADED);
            var foeHeard = Hearing(foe, Tags.HIT_EVADED);

            var strike = Piped(Marking(30f), new EvadeStage { Chance = 0.5f });
            Rolls(0.9f);
            Assert.IsTrue(World.Apply(foe, hero, strike), "a 0.9 misses its half: it lands");
            await Frames.Step(2);
            Assert.AreEqual(70f, Value(hero, health), 0.01f, "its damage");

            Rolls(0.1f);
            Assert.IsFalse(World.Apply(foe, hero, Piped(Marking(30f), new EvadeStage { Chance = 0.5f })), "a 0.1 comes up: evaded");
            await Frames.Step(3);
            Assert.AreEqual(70f, Value(hero, health), 0.01f, "nothing more");
            Assert.That(heroHeard, Is.EqualTo(new List<Tag> { Tags.EVADED }), "the one hit hears Evaded");
            Assert.That(foeHeard, Is.EqualTo(new List<Tag> { Tags.HIT_EVADED }), "the dealer, Hit Evaded");
        });

        /// <summary>An incoming modifier's Block stage blocks the hits landing on its holder, as Tougher Times does.</summary>
        [UnityTest]
        public IEnumerator TW_12_An_incoming_modifiers_block_blocks_its_holders_hits([Values] Hub hub) => Live(async () =>
        {
            var hero = Fighter(hub, "Hero");
            var foe = Fighter(hub, "Foe");
            var tougher = Make.Item("Tougher Times");
            tougher.Modifiers.Add(new EffectModifierEntry
            {
                Rule = new EffectModifierRule
                {
                    Direction = EModifierDirection.Incoming,
                    Changes = new List<AbstractEffectChange> { new ChangeImpactChange { Attribute = health, Stage = new BlockStage { Chance = 1f } } },
                },
            });
            await Frames.Step();
            Assert.That(hero.GetItemSystem().GiveAndEquipItem(tougher, out _), "it's equipped");

            Assert.IsFalse(World.Apply(foe, hero, Marking(30f)), "a hit on its holder is blocked");
            await Frames.Step(2);
            Assert.AreEqual(100f, Value(hero, health), 0.01f, "and lands nothing");
            Assert.IsTrue(World.Apply(hero, foe, Marking(30f)), "its holder's own hits aren't");
        });

        /// <summary>A lasting effect rolls once as it lands, and its ticks aren't rolled again.</summary>
        [UnityTest]
        public IEnumerator TW_12_A_lasting_effect_rolls_once([Values] Hub hub) => Live(async () =>
        {
            var hero = Fighter(hub, "Hero");
            var foe = Fighter(hub, "Foe");
            await Frames.Step();
            var burn = Piped(Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 1f, Target = health, Magnitude = -5f,
                PeriodicTicks = true, Ticks = 5, TickOnApplication = true,
            }), new EvadeStage { Chance = 0.5f });

            var rolls = Rolls(0.9f);
            Assert.IsTrue(World.Apply(foe, hero, burn), "a 0.9 misses: it lands");
            await Frames.Step(90);
            Assert.That(Value(hero, health), Is.LessThan(100f), "and its ticks burn");
            Assert.AreEqual(1, rolls.Drawn, "rolled once, as it landed, its ticks not again");
        });
    }
}
