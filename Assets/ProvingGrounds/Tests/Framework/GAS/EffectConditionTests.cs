using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-19, Decision 58: conditions beside an effect's tags in each phase of its requirements. An Apply condition must
    /// hold for it to land; an Ongoing one pauses it while it fails; a Removal one takes it off once it holds, as Attribute
    /// Shift stops at its floor. The Ongoing and Removal ones are checked at each frame's end while it lasts, though no tag
    /// changes, and a phase passes only when its tags pass and its conditions all hold.
    /// </summary>
    public class EffectConditionTests : ProvingGroundsTest
    {
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

        private ForgeAttribute health, mana;
        private Tag marked;
        private IForgeClock previousClock;
        private bool clockSet;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            mana = Make.Attribute("Mana");
            marked = Make.NewTag("Test.TW19.Marked");
        }

        [TearDown]
        public void PutBackTime()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            clockSet = false;
        }

        private void Stepping(float seconds)
        {
            var was = ForgeTime.Use(new SteppingClock { Step = seconds });
            if (!clockSet) previousClock = was;
            clockSet = true;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name, float healthValue = 100f, float manaValue = 100f) => World.Entity(hub, Make.Set(
            Make.Element(health, healthValue, 100f, EAttributeOverflowPolicy.ZeroToBase),
            Make.Element(mana, manaValue, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        private static AttributeCondition TargetHas(ForgeAttribute attribute, EComparisonOperator comparison, float value) =>
            new() { On = NumberSide.Target, Attribute = attribute, Comparison = comparison, Value = value };

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static bool Carries(IGameplayAbilitySystem gas, GameplayEffect effect) => gas.GetTagCache().GetWeight(effect.Tags.AssetTag) > 0;

        /// <summary>An Apply condition must hold for the effect to land: a finisher only below half.</summary>
        [UnityTest]
        public IEnumerator TW_19_An_apply_condition_must_hold_to_land([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var healthy = Fighter(hub, "Healthy");
            var hurt = Fighter(hub, "Hurt", healthValue: 40f);
            var finisher = Make.Effect("Finisher", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 5f, GrantedTags = new List<Tag> { marked },
            });
            finisher.Conditions.Application.Add(TargetHas(health, EComparisonOperator.LessThan, 50f));
            await Frames.Step();

            Assert.IsFalse(World.Apply(caster, healthy, finisher), "a target above half can't take it");
            Assert.IsTrue(World.Apply(caster, hurt, finisher), "one below half can");
        });

        /// <summary>An Ongoing condition pauses the effect while it fails, though no tag changes, and it goes on as it holds again.</summary>
        [UnityTest]
        public IEnumerator TW_19_An_ongoing_condition_pauses_the_effect_while_it_fails([Values] Hub hub) => Live(async () =>
        {
            Stepping(0.1f);
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target", manaValue: 0f);
            var burn = Make.Effect("Mana Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = health, Magnitude = -1f,
                PeriodicTicks = true, Ticks = 100,
            });
            burn.Conditions.Ongoing.Add(TargetHas(mana, EComparisonOperator.GreaterThan, 0f));
            var refill = Make.Effect("Refill", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = mana, Magnitude = 50f });
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, burn), "it lands, its Ongoing condition aside");
            await Frames.Step(10);
            Assert.AreEqual(100f, Value(target, health), 0.01f, "paused while its target has no mana");

            Assert.IsTrue(World.Apply(caster, target, refill));
            await Frames.Step(10);
            Assert.That(Value(target, health), Is.LessThan(100f), "and burns once its condition holds again");
        });

        /// <summary>A Removal condition takes the effect off once it holds, at its frame's end: a drain stops at its floor.</summary>
        [UnityTest]
        public IEnumerator TW_19_A_removal_condition_ends_the_effect_at_its_floor([Values] Hub hub) => Live(async () =>
        {
            Stepping(0.1f);
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            var drain = Make.Effect("Drain", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 30f, Target = mana, Magnitude = -10f,
                PeriodicTicks = true, Ticks = 300,
            });
            drain.Conditions.Removal.Add(TargetHas(mana, EComparisonOperator.LessOrEqual, 30f));
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, drain));
            await Frames.Until(() => !Carries(target, drain), 120, "it comes off once its target's mana is down to 30");
            float floor = Value(target, mana);
            Assert.That(floor, Is.InRange(20f, 30f), "at its floor");
            await Frames.Step(10);
            Assert.AreEqual(floor, Value(target, mana), 0.01f, "and drains no further");
        });

        /// <summary>A phase passes only when its tags pass and its conditions all hold: Removal asks for both.</summary>
        [UnityTest]
        public IEnumerator TW_19_A_phases_tags_and_conditions_must_all_hold([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            var curse = Make.Effect("Curse", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 30f });
            curse.TargetRequirements ??= new EffectTagRequirements();
            curse.TargetRequirements.RemovalRequirements = new AvoidRequireTagGroup { RequireTags = new List<TagQuery> { new() { Tag = marked } } };
            curse.Conditions.Removal.Add(TargetHas(health, EComparisonOperator.LessThan, 50f));
            var mark = Make.Effect("Mark", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 30f, GrantedTags = new List<Tag> { marked },
            });
            var wound = Make.Effect("Wound", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -60f });
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, curse));
            Assert.IsTrue(World.Apply(caster, target, mark));
            await Frames.Step(3);
            Assert.IsTrue(Carries(target, curse), "its tag alone doesn't take it off");

            Assert.IsTrue(World.Apply(caster, target, wound));
            await Frames.Step(3);
            Assert.IsFalse(Carries(target, curse), "its tag and its condition together do");
        });
    }
}
