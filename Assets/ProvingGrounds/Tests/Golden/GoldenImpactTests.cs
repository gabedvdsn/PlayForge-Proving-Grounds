using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>The impact model's golden scenarios, S1 to S12, from IMPACT_MODEL_REFACTOR.md.</summary>
    public class GoldenImpactTests : GoldenTest
    {
        /// <summary>
        /// THE motivating bug shape, authored correctly as continuous modifiers: two ×1.2 buffs (distinct
        /// assets) on a 50 attribute compound to 72; removing the FIRST must land on 60 (recompute), never
        /// 62 (delta rollback).
        /// </summary>
        [UnityTest]
        public IEnumerator S1_ContinuousMultiply_TwoBuffs_Multiplicative([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Damage");
            var gas = Entity(hub, Make.Element(attr, 50f, 50f));

            var buffA = Make.Effect("BuffA", new EffectConfig { Target = attr, Op = ECalculationOperation.Multiply, Magnitude = 1.2f });
            var buffB = Make.Effect("BuffB", new EffectConfig { Target = attr, Op = ECalculationOperation.Multiply, Magnitude = 1.2f });

            AssertValue(gas, attr, "initial", 50f, 50f);

            Assert.That(Apply(gas, buffA), "apply A");
            await ValueHolds(gas, attr, "after A", 60f);

            Assert.That(Apply(gas, buffB), "apply B");
            await ValueHolds(gas, attr, "after B", 72f);

            await Remove(gas, buffA);
            await ValueHolds(gas, attr, "after removing A", 60f);

            await Remove(gas, buffB);
            await ValueHolds(gas, attr, "after removing B", 50f);
        });

        /// <summary>AdditiveBonus multiply pooling: two +20% pool to +40%, and reverse cleanly.</summary>
        [UnityTest]
        public IEnumerator S2_ContinuousMultiply_TwoBuffs_AdditiveBonus([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Damage");
            var gas = Entity(hub, Make.Element(attr, 50f, 50f));

            var buffA = Make.Effect("BuffA", new EffectConfig { Target = attr, Op = ECalculationOperation.Multiply, Magnitude = 1.2f, MultiplyBehaviour = EMultiplyBehaviour.AdditiveBonus });
            var buffB = Make.Effect("BuffB", new EffectConfig { Target = attr, Op = ECalculationOperation.Multiply, Magnitude = 1.2f, MultiplyBehaviour = EMultiplyBehaviour.AdditiveBonus });

            Apply(gas, buffA);
            await ValueHolds(gas, attr, "after A", 60f);

            Apply(gas, buffB);
            await ValueHolds(gas, attr, "after B (pooled +40%)", 70f);

            await Remove(gas, buffA);
            await ValueHolds(gas, attr, "after removing A", 60f);

            await Remove(gas, buffB);
            await ValueHolds(gas, attr, "after removing B", 50f);
        });

        /// <summary>Item-style infinite +50 flat modifier: registered while present, exact reversal.</summary>
        [UnityTest]
        public IEnumerator S3_ContinuousAdd_ItemStyle_RegisterRemove([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Health");
            var gas = Entity(hub, Make.Element(attr, 100f, 100f));

            var item = Make.Effect("ItemHealth", new EffectConfig
            {
                Target = attr, Op = ECalculationOperation.Add, Slot = EAttributeTarget.CurrentAndBase, Magnitude = 50f,
            });

            Apply(gas, item);
            await ValueHolds(gas, attr, "equipped", 150f, 150f);

            await Remove(gas, item);
            await ValueHolds(gas, attr, "unequipped", 100f, 100f);
        });

        /// <summary>
        /// "5 HP per second for 5 seconds": periodic commits are PERMANENT, so the value stays after the
        /// effect expires naturally. Deterministic time via a UseScaler delta-time override (1 s per frame).
        /// </summary>
        [UnityTest]
        public IEnumerator S4_HoT_TicksArePermanentAfterExpiry([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Health");
            var gas = Entity(hub, Make.Element(attr, 50f, 100f, EAttributeOverflowPolicy.ZeroToBase));

            var hot = Make.Effect("Regen", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational,
                Target = attr, Op = ECalculationOperation.Add, Magnitude = 5f,
                Duration = 5f, PeriodicTicks = true, Ticks = 5, FixedDeltaTime = 1f,
            });

            Apply(gas, hot);
            AssertValue(gas, attr, "on apply (no TickOnApplication)", 50f);

            await Frames.Step(5); // 5 s → 5 ticks → +25, duration exhausted
            AssertValue(gas, attr, "after 5 ticks", 75f);

            await Frames.Step(2); // expiry cleanup
            AssertValue(gas, attr, "after natural expiry, ticks are permanent", 75f, 100f);
        });

        /// <summary>
        /// Instant commits: clamped damage and heal history must survive unrelated recomputes (a modifier
        /// added to and removed from the same attribute) without resetting or drifting.
        /// </summary>
        [UnityTest]
        public IEnumerator S5_InstantDamage_ClampAndHistorySurvival([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Health");
            var gas = Entity(hub, Make.Element(attr, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));

            var damage30 = Make.Effect("Damage30", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = attr, Op = ECalculationOperation.Add, Magnitude = -30f,
            });
            var maxHp20 = Make.Effect("MaxHp20", new EffectConfig
            {
                Target = attr, Op = ECalculationOperation.Add, Slot = EAttributeTarget.Base, Magnitude = 20f,
            });
            var heal50 = Make.Effect("Heal50", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = attr, Op = ECalculationOperation.Add, Magnitude = 50f,
            });

            Apply(gas, damage30);
            await ValueHolds(gas, attr, "after 30 damage", 70f, 100f);

            Apply(gas, maxHp20);
            await ValueHolds(gas, attr, "max-HP buff must not touch damage history", 70f, 120f);

            await Remove(gas, maxHp20);
            await ValueHolds(gas, attr, "buff removed, damage still applied", 70f, 100f);

            Apply(gas, heal50);
            await ValueHolds(gas, attr, "overheal clamps to Base", 100f, 100f);

            Apply(gas, damage30);
            await ValueHolds(gas, attr, "post-clamp history stays consistent", 70f, 100f);
        });

        /// <summary>
        /// Committed history lands where it was measured, after the % modifiers, so they don't scale it
        /// (Decision 4, GAS-15): 50 +(instant ×1.2 → +10 committed) → 60; ×1.5 modifier → 50×1.5 + 10 = 85.
        /// It joined the baseline before, and the aura made it 90.
        /// </summary>
        [UnityTest]
        public IEnumerator S6_CommittedHistoryLandsAfterModifiers([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Damage");
            var gas = Entity(hub, Make.Element(attr, 50f, 50f));

            var tome = Make.Effect("PermanentTome", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = attr, Op = ECalculationOperation.Multiply, Magnitude = 1.2f,
            });
            var aura = Make.Effect("Aura", new EffectConfig
            {
                Target = attr, Op = ECalculationOperation.Multiply, Magnitude = 1.5f,
            });

            Apply(gas, tome);
            await ValueHolds(gas, attr, "permanent ×1.2 commit", 60f);

            Apply(gas, aura);
            await ValueHolds(gas, attr, "the aura leaves committed history alone: 50×1.5 + 10", 85f);

            await Remove(gas, aura);
            await ValueHolds(gas, attr, "aura removed", 60f);
        });

        /// <summary>Stack count feeds modifier magnitude (+10 per stack) and reverses fully.</summary>
        [UnityTest]
        public IEnumerator S7_Stacking_MagnitudePerStack([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Armor");
            var gas = Entity(hub, Make.Element(attr, 50f, 50f));

            var stacking = Make.Effect("StackingBuff", new EffectConfig
            {
                Target = attr, Op = ECalculationOperation.Add,
                Magnitude = 10f, MagnitudeScaler = new StackCountScaler(), MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = EEffectStackPolicy.StacksShareOneDuration,
            });

            Apply(gas, stacking);
            await ValueHolds(gas, attr, "1 stack", 60f);

            Apply(gas, stacking);
            await ValueHolds(gas, attr, "2 stacks", 70f);

            await Remove(gas, stacking);
            await ValueHolds(gas, attr, "all stacks removed", 50f);
        });

        /// <summary>Test-only scaler: magnitude follows the container's stack count.</summary>
        private class StackCountScaler : AbstractScaler
        {
            public override void Initialize(IAttributeImpactCarrier deriv) { }
            public override float Evaluate(IAttributeImpactCarrier deriv) => 1f;
            public override float Evaluate(AbstractStackingEffectContainer container) => container.Stacks;
        }

        /// <summary>
        /// Regulation graph: B is blueprint-derived as 2 × A (cached attribute scaler, Current linked to
        /// Base). Modifying A recomputes B automatically; removal reverts.
        /// </summary>
        [UnityTest]
        public IEnumerator S8_Regulation_DerivedAttributeFollowsSource([Values] Hub hub) => Live(async () =>
        {
            var attrA = Make.Attribute("Strength");
            var attrB = Make.Attribute("AttackPower");

            var elementA = Make.Element(attrA, 10f, 10f);
            var elementB = Make.Element(attrB, 0f, 0f,
                baseSpecOverride: new AttributeMagnitudeSpec
                {
                    Magnitude = 2f,
                    RealMagnitude = EMagnitudeOperation.MultiplyWithScaler,
                    Scaling = new CachedAttributeScaler { CaptureAttribute = attrA },
                },
                linkCurrentToBase: true);

            var gas = Entity(hub, elementA, elementB);
            AssertValue(gas, attrB, "derived initial (2×10)", 20f, 20f);

            var strBuff = Make.Effect("StrBuff", new EffectConfig
            {
                Target = attrA, Op = ECalculationOperation.Add, Slot = EAttributeTarget.CurrentAndBase, Magnitude = 5f,
            });

            Apply(gas, strBuff);
            await ValueHolds(gas, attrA, "source buffed", 15f, 15f);
            await ValueHolds(gas, attrB, "derived follows source (2×15)", 30f, 30f);

            await Remove(gas, strBuff);
            await ValueHolds(gas, attrB, "derived reverts", 20f, 20f);
        });

        /// <summary>Durational effect with NO attribute target (pure tag grant): apply and remove without exceptions.</summary>
        [UnityTest]
        public IEnumerator S9_NullTargetEffect_TagGrantOnly([Values] Hub hub) => Live(async () =>
        {
            var gas = Entity(hub, Make.Element(Make.Attribute("Unused"), 1f, 1f));

            var grantedTag = Make.NewTag("Test.Status.Shielded");
            var tagFx = Make.Effect("ShieldTag", new EffectConfig
            {
                Target = null, GrantedTags = new List<Tag> { grantedTag },
            });

            Assert.That(Apply(gas, tagFx), "apply tag-only effect");
            Assert.That(gas.GetTagWeight(grantedTag), Is.GreaterThan(0), "tag granted while active");

            await Frames.Step();
            Assert.That(gas.GetTagWeight(grantedTag), Is.GreaterThan(0), "tag still granted a frame later");

            await Remove(gas, tagFx);
            // TagCache.GetWeight returns -1 for an absent tag (0 would be a present tag at zero weight),
            // so "gone" means weight <= 0
            Assert.That(gas.GetTagWeight(grantedTag), Is.LessThanOrEqualTo(0), "tag gone after removal");
        });

        /// <summary>ReplaceOldContainer re-application is idempotent for an identical spec: no double-retain, clean removal.</summary>
        [UnityTest]
        public IEnumerator S10_ReplaceOldContainer_Idempotent([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Speed");
            var gas = Entity(hub, Make.Element(attr, 50f, 50f));

            var buff = Make.Effect("SpeedBuff", new EffectConfig
            {
                Target = attr, Op = ECalculationOperation.Multiply, Magnitude = 1.2f,
                Reapply = EEffectReApplicationPolicy.ReplaceOldContainer,
            });

            Apply(gas, buff);
            await ValueHolds(gas, attr, "first apply", 60f);

            Apply(gas, buff);
            await ValueHolds(gas, attr, "re-apply replaces, never compounds", 60f);

            await Remove(gas, buff);
            await ValueHolds(gas, attr, "removed", 50f);
        });

        /// <summary>
        /// Per-group attribution. One health pool split into base (attribute-set root) and bonus
        /// (item-style modifier) retention groups. Slices must sum to the total and answer "how much of
        /// this attribute comes from group X" for UI.
        /// </summary>
        [UnityTest]
        public IEnumerator S11_GroupAttribution_BaseVsBonus([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = Entity(hub, Make.Element(health, 100f, 100f));

            var bonusGroup = Make.NewTag("Test.Group.Bonus");

            var itemHp = Make.Effect("ItemHealth", new EffectConfig
            {
                Target = health, Op = ECalculationOperation.Add, Slot = EAttributeTarget.CurrentAndBase, Magnitude = 50f,
            });
            itemHp.Definition.RetentionGroup = bonusGroup;

            Apply(gas, itemHp);
            await ValueHolds(gas, health, "total pool", 150f, 150f);

            var attrSystem = gas.GetAttributeSystem();
            Assert.That(attrSystem.TryGetGroupValue(health, bonusGroup, out var bonus), "bonus slice query");
            Assert.That(bonus.CurrentValue, Is.EqualTo(50f).Within(0.001f), "bonus slice (Current)");
            Assert.That(bonus.BaseValue, Is.EqualTo(50f).Within(0.001f), "bonus slice (Base)");

            Assert.That(attrSystem.TryGetGroupValue(health, Tags.DEFAULT, out var baseSlice), "base slice query");
            Assert.That(baseSlice.CurrentValue, Is.EqualTo(100f).Within(0.001f), "base slice (Current)");

            await Remove(gas, itemHp);
            attrSystem.TryGetGroupValue(health, bonusGroup, out var bonusAfter);
            Assert.That(bonusAfter.CurrentValue, Is.EqualTo(0f).Within(0.001f), "bonus slice after removal");
        });

        /// <summary>
        /// Ability introspection: a HoT ability ("X HP per tick, 5 ticks over N seconds") must decipher to
        /// per-level series, 20/30/40/50 total healing over 3/4/5/6 seconds across 4 levels, with the
        /// right role and target scope. It reads an ability asset and runs nothing, so it needs no frames.
        /// </summary>
        [Test]
        public void S12_AbilityInspector_PerLevelSeries()
        {
            var attr = Make.Attribute("Health");

            var hot = Make.Effect("Regen", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational,
                Target = attr, Op = ECalculationOperation.Add,
                Magnitude = 0f, MagnitudeScaler = LevelScaler(4f, 6f, 8f, 10f), MagnitudeOp = EMagnitudeOperation.UseScaler,
                Duration = 0f, PeriodicTicks = true, Ticks = 5,
            });
            hot.DurationSpecification.DurationOperation = new ScalableFloat
            {
                Magnitude = 0f,
                Scaler = LevelScaler(3f, 4f, 5f, 6f),
                RealMagnitude = EMagnitudeOperation.UseScaler,
            };

            var ability = Make.Ability("Regen Aura", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { hot } }));
            ability.StartingLevel = 1;
            ability.MaxLevel = 4;

            var breakdown = AbilityInspector.Build(ability);

            Assert.That(breakdown.GameplayUsages.Count(), Is.EqualTo(1), "one gameplay usage");
            var usage = breakdown.GameplayUsages.First();
            Assert.That(usage.Role, Is.EqualTo(EAbilityEffectRole.StageApplication));
            Assert.That(usage.Scope, Is.EqualTo(EEffectTargetScope.Caster), "implicit self target");

            var series = usage.Series;
            Assert.That(series, Is.Not.Null, "series built");
            Assert.That(series.FormatDuration(), Is.EqualTo("3/4/5/6"));
            Assert.That(series.Impacts, Has.Count.EqualTo(1), "one impact");

            var impactSeries = series.Impacts[0];
            Assert.That(impactSeries.Delivery, Is.EqualTo(EImpactDelivery.Periodic));
            Assert.That(impactSeries.IsDynamic, Is.False, "not flagged dynamic");
            Assert.That(impactSeries.FormatMagnitude(), Is.EqualTo("4/6/8/10"), "per-tick magnitudes");
            Assert.That(impactSeries.FormatTotal(), Is.EqualTo("20/30/40/50"), "totals");

            Assert.That(breakdown.Describe(2).Any(line => line.Contains("30") && line.Contains("4")), "the tooltip mentions healing 30 over 4 s");
        }

        private static SimpleScaler LevelScaler(params float[] values)
        {
            var scaler = new SimpleScaler
            {
                LevelValues = values,
                MaxLevel = values.Length,
                Configuration = ELevelConfig.LockToLevelProvider,
                Interpolation = EScalerInterpolation.Linear,
            };
            scaler.RegenerateCurve();
            return scaler;
        }
    }
}
