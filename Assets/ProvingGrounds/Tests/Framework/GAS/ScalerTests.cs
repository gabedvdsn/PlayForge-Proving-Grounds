using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Scalers in Decision 28's model (ED-33): one can be a shared Scaler asset that fields reference, and its levels map
    /// exactly unless it stretches over a level range.
    /// </summary>
    public class ScalerTests : ProvingGroundsTest
    {
        // A magnitude read at a level of a 1 to 5 range, through the scaler pipeline the runtime uses
        private static float At(ScalableFloat magnitude, int level) =>
            AbilityInspector.EvaluateAtLevel(magnitude, level, 1, 5, out _);

        private static ScalableFloat Reading(AbstractScaler scaler, float magnitude = 0f,
            EMagnitudeOperation real = EMagnitudeOperation.UseScaler) =>
            new() { Magnitude = magnitude, RealMagnitude = real, Scaler = scaler };

        /// <summary>
        /// A scaler wanted in two places was copied into each, and an edit had to be propagated to every copy: the Scaler
        /// Template's link code had no callers. Two fields that reference one Scaler asset read the same values, one of
        /// them times 1.5 through its own magnitude, and an edit to the asset reaches both.
        /// </summary>
        [Test]
        public void ED_33_A_shared_scaler_reads_its_asset()
        {
            var fireDuration = ScriptableObject.CreateInstance<ScalerAsset>();
            try
            {
                fireDuration.Scaler = new SimpleScaler { LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = false };
                var plain = Reading(new SharedScaler { Asset = fireDuration });
                var longer = Reading(new SharedScaler { Asset = fireDuration }, 1.5f, EMagnitudeOperation.MultiplyWithScaler);

                Assert.AreEqual(20f, At(plain, 2), "a field reads the asset's value");
                Assert.AreEqual(30f, At(longer, 2), "and another reads it times its own 1.5");

                fireDuration.Scaler.LevelValues[1] = 40f;
                Assert.AreEqual(40f, At(plain, 2), "an edit to the asset reaches the first field");
                Assert.AreEqual(60f, At(longer, 2), "and the second, with nothing to propagate");

                Assert.AreEqual(0f, At(Reading(new SharedScaler()), 2), "a field whose asset is gone reads as no scaler");
            }
            finally
            {
                Object.DestroyImmediate(fireDuration);
            }
        }

        /// <summary>
        /// Every scaler spread its values over a level range, so a table of three on an ability of five levels read 15 at
        /// level 2. Levels map exactly now: level 2 reads the 2nd value, and a level past the end reads the last (Decision
        /// 28). A scaler that stretches reads as before, and the scalers saved before the option existed stretch.
        /// </summary>
        [Test]
        public void ED_33_Levels_map_exactly_unless_the_scaler_stretches()
        {
            var exact = new SimpleScaler { LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = false };
            Assert.AreEqual(10f, At(Reading(exact), 1), "level 1 reads the first value");
            Assert.AreEqual(20f, At(Reading(exact), 2), "level 2 the second");
            Assert.AreEqual(30f, At(Reading(exact), 5), "and a level past the end the last");

            var stretched = new SimpleScaler
            {
                LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = true,
                Configuration = ELevelConfig.LockToLevelProvider, Interpolation = EScalerInterpolation.Linear,
            };
            stretched.RegenerateCurve();
            Assert.AreEqual(15f, At(Reading(stretched), 2), 0.001f, "a stretched scaler reads between its values");
            Assert.AreEqual(30f, At(Reading(stretched), 5), 0.001f, "and its last at the range's end");

            Assert.IsTrue(new SimpleScaler().StretchOverLevels, "a scaler saved before the option keeps stretching");
        }

        /// <summary>
        /// A scaler spread over its provider's levels "up to a level" ended its range at the lower of its Max Level and the
        /// level being read, so a level always sat at the range's end and every level above the first read the last value
        /// (GAS-64, the user's call). The range ends at the lower of its Max Level and the provider's last level: three
        /// values spread up to level 3 of 5 read 10, 20 and 30, and 30 past it; up to level 9, the provider's last, 5, ends
        /// the range.
        /// </summary>
        [Test]
        public void GAS_64_A_scaler_spread_up_to_a_level_spreads_to_it()
        {
            var upTo = new SimpleScaler
            {
                LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = true,
                Configuration = ELevelConfig.Clamped, MaxLevel = 3, Interpolation = EScalerInterpolation.Linear,
            };
            upTo.RegenerateCurve();
            var levels = new[] { 1, 2, 3, 4, 5 };
            Assert.That(levels.Select(level => At(Reading(upTo), level)), Is.EqualTo(new[] { 10f, 20f, 30f, 30f, 30f }).Within(0.001f),
                "spread to level 3, and its last value past it");

            upTo.MaxLevel = 9;
            Assert.That(levels.Select(level => At(Reading(upTo), level)), Is.EqualTo(new[] { 10f, 15f, 20f, 25f, 30f }).Within(0.001f),
                "up to a level past the provider's, spread over the provider's five");
        }

        /// <summary>
        /// An attribute set's element shares a Scaler asset through the cached Shared kind: Power's Base reads the asset's
        /// scaler, twice Strength, and follows Strength as the entity's values change (ED-33).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_An_attribute_set_element_shares_a_scaler([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var power = Make.Attribute("Power");
            var twice = ScriptableObject.CreateInstance<ScalerAsset>();
            twice.Scaler = new CachedAttributeScaler { CaptureAttribute = strength };
            try
            {
                var set = Make.Set(Make.Element(strength, 10f, 10f), Make.Element(power, 0f, 0f,
                    baseSpecOverride: new AttributeMagnitudeSpec
                    {
                        Magnitude = 2f,
                        RealMagnitude = EMagnitudeOperation.MultiplyWithScaler,
                        Scaling = new SharedCachedScaler { Asset = twice },
                    },
                    linkCurrentToBase: true));
                var gas = World.Entity(hub, set);
                await Frames.Step();

                gas.TryGetAttributeValue(power, out var value);
                Assert.That(value.BaseValue, Is.EqualTo(20f).Within(0.001f), "Power's Base reads the shared scaler: twice Strength");
            }
            finally
            {
                Object.DestroyImmediate(twice);
            }
        });

        /// <summary>
        /// A Conditional scaler's Target Attribute Threshold read the attribute on the effect's source, as Source Attribute
        /// Threshold does, so "more against a target below 50 Health" read the caster's Health. It reads the target's.
        /// Found building the demo (GAS-69). Its condition is an Attribute condition, on the target or the caster (FEAT-29).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_69_A_target_threshold_reads_the_target([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 30f, 100f)), "Target");
            await Frames.Step();

            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f });
            var spec = target.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster), blow);
            Assert.AreEqual(2f, Below50(NumberSide.Target).Evaluate(spec), "the target, at 30 Health, is below 50");
            Assert.AreEqual(1f, Below50(NumberSide.Caster).Evaluate(spec), "and the caster, at 100, isn't");

            ConditionalScaler Below50(NumberSide side) => new()
            {
                When = new AttributeCondition { On = side, Attribute = health, Comparison = EComparisonOperator.LessThan, Value = 50f },
                TrueValue = 2f, FalseValue = 1f,
            };
        });

        /// <summary>
        /// A Clamped scaler clamped its own table alone, so a value another scaler worked out, as damage by a target's
        /// missing health, couldn't be capped. Given an inner scaler, it clamps that; with none, its table, as before.
        /// Found building the demo, whose Eviscerate caps its damage (FEAT-27). What it clamps is its Value, a number of
        /// any kind, by level where it's made (FEAT-29).
        /// </summary>
        [Test]
        public void FEAT_27_A_clamped_scaler_clamps_an_inner_scaler()
        {
            var capped = new ClampedScaler
            {
                MinValue = 0f, MaxValue = 400f,
                Value = Reading(new SimpleScaler { LevelValues = new[] { 100f, 300f, 500f }, StretchOverLevels = false }),
            };
            Assert.AreEqual(100f, At(Reading(capped), 1), "an inner value inside the bounds goes through");
            Assert.AreEqual(400f, At(Reading(capped), 3), "and one past the most is capped");

            var byLevel = new ClampedScaler { MinValue = 0f, MaxValue = 25f };
            var table = (SimpleScaler)byLevel.Value.Scaler;
            table.LevelValues = new[] { 10f, 30f };
            table.StretchOverLevels = false;
            Assert.AreEqual(10f, At(Reading(byLevel), 1), "made with nothing else, it clamps a value by level");
            Assert.AreEqual(25f, At(Reading(byLevel), 2), "as it clamped its own table");
        }
    }
}
