using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-9: number math. A Stacking Curve turns a count into its number, as Risk of Rain's items stack and Dota's armor
    /// reads; a Group divides, and a cached Group too; and an attribute reads as what's missing of it, or its share of its
    /// base, in × Attribute and the Attribute condition.
    /// </summary>
    public class NumberMathTests : ProvingGroundsTest
    {
        private static ScalableFloat Kind(AbstractScaler kind) => new(kind, 1f, EMagnitudeOperation.UseScaler);

        private static float Curve(EStackingCurve curve, float a, float n, float plus = 0f) =>
            Kind(new StackingCurveKind { Curve = curve, Coefficient = a, Count = n, Plus = plus }).Evaluate(NumberContext.For(null, null, 1));

        private static float Current(IGameplayAbilitySystem gas, ForgeAttribute attribute)
        {
            Assert.That(gas.TryGetAttributeValue(attribute, out var value), $"the entity has {attribute.Name}");
            return value.CurrentValue;
        }

        /// <summary>Each curve turns its count into its number: linear, hyperbolic, reciprocal, inverse and exponential, then Plus.</summary>
        [Test]
        public void TW_9_A_stacking_curve_turns_a_count_into_its_number()
        {
            Assert.AreEqual(0.45f, Curve(EStackingCurve.Linear, 0.15f, 3f), 0.0001f, "Soldier's Syringe: 15% a stack, three stacks");
            Assert.AreEqual(0.1304f, Curve(EStackingCurve.Hyperbolic, 0.15f, 1f), 0.0001f, "Tougher Times with one: 13%");
            Assert.AreEqual(0.6f, Curve(EStackingCurve.Hyperbolic, 0.15f, 10f), 0.0001f, "and with ten, 60%");
            Assert.AreEqual(0.625f, Curve(EStackingCurve.Reciprocal, 0.06f, 10f), 0.0001f, "Dota's armor of 10 leaves 62.5% of a physical hit");
            Assert.AreEqual(0.85f, Curve(EStackingCurve.Inverse, 1.7f, 2f), 0.0001f, "a 1.7 s wind-up at an attack speed of 2");
            Assert.AreEqual(0f, Curve(EStackingCurve.Inverse, 1.7f, 0f), 0.0001f, "and at a speed of 0, none");
            Assert.AreEqual(0.81f, Curve(EStackingCurve.Exponential, 0.9f, 2f), 0.0001f, "0.9 to the 2");
            Assert.AreEqual(1.45f, Curve(EStackingCurve.Linear, 0.15f, 3f, plus: 1f), 0.0001f, "Plus 1 makes the share a multiplier");
            StringAssert.Contains("1 − 1 / (1 + 0.15 × 10)",
                new StackingCurveKind { Curve = EStackingCurve.Hyperbolic, Coefficient = 0.15f, Count = 10f }.Describe(), "its line says its curve");
        }

        private static float Join(params (float value, EGroupOperation joins)[] members)
        {
            var group = new ScalerGroup();
            foreach (var (value, joins) in members) group.AddMember(new ConstantScaler { Value = value }, joins);
            return Kind(group).Evaluate(NumberContext.For(null, null, 1));
        }

        /// <summary>A Group's member that divides divides the sum, in any order, before the flat bonuses; one that reads 0 divides nothing.</summary>
        [Test]
        public void TW_9_A_Group_divides()
        {
            Assert.AreEqual(25f, Join((100f, EGroupOperation.Add), (4f, EGroupOperation.Divide)), 0.001f, "100 ÷ 4");
            Assert.AreEqual(50f, Join((4f, EGroupOperation.Divide), (100f, EGroupOperation.Add), (2f, EGroupOperation.Multiply)), 0.001f,
                "the sum multiplied and divided, in any order: 100 × 2 ÷ 4");
            Assert.AreEqual(30f, Join((100f, EGroupOperation.Add), (4f, EGroupOperation.Divide), (5f, EGroupOperation.FlatBonus)), 0.001f,
                "then the flat bonus: 100 ÷ 4 + 5");
            Assert.AreEqual(100f, Join((100f, EGroupOperation.Add), (0f, EGroupOperation.Divide)), 0.001f, "a member that reads 0 divides nothing");
        }

        /// <summary>An attribute set element's cached Group divides as the Group does.</summary>
        [UnityTest]
        public IEnumerator TW_9_A_cached_Group_divides([Values] Hub hub) => Live(async () =>
        {
            CachedMagnitudeModifierGroupMember Member(float value, EGroupOperation joins) => new()
            {
                Calculation = new CachedSimpleScaler { LevelValues = new[] { value }, StretchOverLevels = false },
                RelativeOperation = joins,
            };
            var dividing = new CachedScalerGroup { Calculations = new[] { Member(60f, EGroupOperation.Add), Member(3f, EGroupOperation.Divide) } };
            var stamina = Make.Attribute("Stamina");
            var startsAt = new AttributeMagnitudeSpec { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseScaler, Scaling = dividing };
            var tiring = World.Entity(hub, Make.Set(Make.Element(stamina, 0f, 0f, currentSpecOverride: startsAt)), "Tiring");
            await Frames.Step();
            Assert.AreEqual(20f, Current(tiring, stamina), 0.01f, "60 ÷ 3");
        });

        /// <summary>× Attribute and the Attribute condition read an attribute as what's missing of it, or its share of its base.</summary>
        [UnityTest]
        public IEnumerator TW_9_An_attribute_reads_as_whats_missing_or_its_share([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var hurt = World.Entity(hub, Make.Set(Make.Element(health, 30f, 100f, EAttributeOverflowPolicy.ZeroToBase)), "Hurt");
            var healthy = World.Entity(hub, Make.Set(Make.Element(health, 95f, 100f, EAttributeOverflowPolicy.ZeroToBase)), "Healthy");
            await Frames.Step();
            var context = NumberContext.For(hurt, hurt, 1);

            float Read(EAttributeTargetBinary what) => Kind(new AttributeBackedScaler { CaptureAttribute = health, CaptureWhat = what }).Evaluate(context);
            Assert.AreEqual(30f, Read(EAttributeTargetBinary.Current), 0.01f, "its current");
            Assert.AreEqual(70f, Read(EAttributeTargetBinary.Missing), 0.01f, "what's missing: 100 − 30, as Mana Void reads a target's spent mana");
            Assert.AreEqual(0.3f, Read(EAttributeTargetBinary.Share), 0.001f, "its share: 30 ÷ 100");

            var aboveNinety = new AttributeCondition
            {
                On = NumberSide.Target, Attribute = health, What = EAttributeTargetBinary.Share,
                Comparison = EComparisonOperator.GreaterThan, Value = 0.9f,
            };
            Assert.IsFalse(aboveNinety.Holds(context), "the Crowbar's above 90%: not at 30%");
            Assert.IsTrue(aboveNinety.Holds(NumberContext.For(hurt, healthy, 1)), "and at 95%");
            StringAssert.Contains("(share)", aboveNinety.Describe(), "its line says the part it reads");
        });
    }
}
