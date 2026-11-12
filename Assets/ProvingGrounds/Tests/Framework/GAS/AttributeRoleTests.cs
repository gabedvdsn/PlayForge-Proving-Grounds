using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-1, the user's question of 2026-11-11: several effects give Damage Amplification, and an attack's scaler reads
    /// it; how does it add up in a way that makes sense? An attribute's role says (Decision 46): a Bonus Share's bonuses
    /// add, and a number reads it as a multiplier; a Resistance's stack with diminishing returns, and a number reads what's
    /// left. An effect's impact can add up its own way, and a number can read it its own way.
    /// </summary>
    public class AttributeRoleTests : ProvingGroundsTest
    {
        private ForgeAttribute health;

        private ForgeAttribute Role(string name, EAttributeRole role)
        {
            var attribute = Make.Attribute(name);
            attribute.Role = role;
            return attribute;
        }

        private GameplayEffect Bonus(ForgeAttribute attribute, float amount, EAddBehaviour add = EAddBehaviour.AttributeRole)
        {
            var fx = Make.Effect("Bonus", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = attribute, Magnitude = amount });
            fx.ImpactSpecification.AddBehaviour = add;
            return fx;
        }

        // A blow of 100, times the attribute as a number reads it
        private GameplayEffect Blow(ForgeAttribute attribute, ESourceTarget from, EAttributeRead read = EAttributeRead.ByRole) =>
            Make.Effect("Blow", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -100f,
                MagnitudeScaler = new AttributeBackedScaler { CaptureAttribute = attribute, CaptureFrom = from, ReadAs = read },
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler,
            });

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private async Cysharp.Threading.Tasks.UniTask<(IGameplayAbilitySystem attacker, IGameplayAbilitySystem target)> Pair(Hub hub, ForgeAttribute attribute)
        {
            health = Make.Attribute("Health");
            var attacker = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f), Make.Element(attribute, 0f, 0f)), "Attacker");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f), Make.Element(attribute, 0f, 0f)), "Target");
            await Frames.Step();
            return (attacker, target);
        }

        /// <summary>
        /// Damage Amplification as a Bonus Share: +20% and +30% add to 0.5, and the attack reads it as a multiplier, so a
        /// blow of 100 deals 150; the number says so.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_1_A_bonus_share_adds_up_and_reads_as_a_multiplier([Values] Hub hub) => Live(async () =>
        {
            var amplification = Role("Damage Amplification", EAttributeRole.BonusShare);
            var (attacker, target) = await Pair(hub, amplification);
            Assert.IsTrue(World.Apply(attacker, attacker, Bonus(amplification, 0.2f)), "+20%");
            Assert.IsTrue(World.Apply(attacker, attacker, Bonus(amplification, 0.3f)), "+30%");
            await Frames.Step();
            Assert.AreEqual(0.5f, Value(attacker, amplification), 0.001f, "they add");

            Assert.IsTrue(World.Apply(attacker, target, Blow(amplification, ESourceTarget.Source)));
            await Frames.Step();
            Assert.AreEqual(1000f - 150f, Value(target, health), 0.01f, "a blow of 100, times 1 + 0.5");
            Assert.That(NumberText.Describe(new AttributeBackedScaler { CaptureAttribute = amplification, CaptureFrom = ESourceTarget.Source }),
                Does.StartWith("(1 + "), "and the number says it reads 1 + x");
        });

        /// <summary>
        /// Damage Resistance as a Resistance: two 30% make 51%, each on what's left, and a blow reads what's left, so 100
        /// deals 49.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_1_A_resistance_stacks_with_diminishing_returns([Values] Hub hub) => Live(async () =>
        {
            var resistance = Role("Damage Resistance", EAttributeRole.Resistance);
            var (attacker, target) = await Pair(hub, resistance);
            Assert.IsTrue(World.Apply(target, target, Bonus(resistance, 0.3f)), "30%");
            Assert.IsTrue(World.Apply(target, target, Bonus(resistance, 0.3f)), "another 30%");
            await Frames.Step();
            Assert.AreEqual(0.51f, Value(target, resistance), 0.001f, "1 − 0.7 × 0.7");

            Assert.IsTrue(World.Apply(attacker, target, Blow(resistance, ESourceTarget.Target)));
            await Frames.Step();
            Assert.AreEqual(1000f - 49f, Value(target, health), 0.01f, "a blow of 100 takes what's left, 49");
        });

        /// <summary>An effect's impact can add its own way: a plain sum on a Resistance, two 30% making 60%.</summary>
        [UnityTest]
        public IEnumerator TW_1_An_effect_can_add_up_its_own_way([Values] Hub hub) => Live(async () =>
        {
            var resistance = Role("Damage Resistance", EAttributeRole.Resistance);
            var (_, target) = await Pair(hub, resistance);
            Assert.IsTrue(World.Apply(target, target, Bonus(resistance, 0.3f, EAddBehaviour.Sum)));
            Assert.IsTrue(World.Apply(target, target, Bonus(resistance, 0.3f, EAddBehaviour.Sum)));
            await Frames.Step();
            Assert.AreEqual(0.6f, Value(target, resistance), 0.001f, "summed, as each says");
        });

        /// <summary>A number can read its own way: a Bonus Share read as it is gives the share, 0.5, so the blow deals 50.</summary>
        [UnityTest]
        public IEnumerator TW_1_A_number_can_read_its_own_way([Values] Hub hub) => Live(async () =>
        {
            var amplification = Role("Damage Amplification", EAttributeRole.BonusShare);
            var (attacker, target) = await Pair(hub, amplification);
            Assert.IsTrue(World.Apply(attacker, attacker, Bonus(amplification, 0.5f)));
            await Frames.Step();
            Assert.IsTrue(World.Apply(attacker, target, Blow(amplification, ESourceTarget.Source, EAttributeRead.AsIs)));
            await Frames.Step();
            Assert.AreEqual(1000f - 50f, Value(target, health), 0.01f, "100 × 0.5");
        });

        /// <summary>A Value, every attribute made before, adds and reads as it did.</summary>
        [UnityTest]
        public IEnumerator TW_1_A_value_adds_and_reads_as_it_did([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            Assert.AreEqual(EAttributeRole.Value, strength.Role, "an attribute is a Value unless it says");
            var (attacker, target) = await Pair(hub, strength);
            Assert.IsTrue(World.Apply(attacker, attacker, Bonus(strength, 0.3f)));
            Assert.IsTrue(World.Apply(attacker, attacker, Bonus(strength, 0.3f)));
            await Frames.Step();
            Assert.AreEqual(0.6f, Value(attacker, strength), 0.001f, "they sum");
            Assert.IsTrue(World.Apply(attacker, target, Blow(strength, ESourceTarget.Source)));
            await Frames.Step();
            Assert.AreEqual(1000f - 60f, Value(target, health), 0.01f, "and a number reads it as it is");
        });
    }
}
