using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// An effect's additional impacts (ED-3). An effect made one change: the attribute, operation and magnitude its
    /// Impact section held, so a drain that took health and mana took two effects. Each additional impact now changes an
    /// attribute of its own, with a delivery of its own.
    /// </summary>
    public class ImpactEntryTests : ProvingGroundsTest
    {
        /// <summary>One effect takes health and mana: its first impact takes the one, an additional impact the other.</summary>
        [UnityTest]
        public IEnumerator ED_3_An_effect_makes_each_of_its_impacts([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var mana = Make.Attribute("Mana");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f), Make.Element(mana, 50f, 100f)));
            var drain = Make.Effect("Drain", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = -30f,
            });
            drain.ImpactSpecification.AdditionalImpacts.Add(Impact(mana, -10f, EImpactDelivery.OnApply));

            World.Apply(gas, gas, drain);
            await Frames.Step(2);

            Assert.That(Current(gas, health), Is.EqualTo(70f), "the first impact took 30 health");
            Assert.That(Current(gas, mana), Is.EqualTo(40f), "and the additional impact took 10 mana");
        });

        /// <summary>
        /// Each impact keeps its own delivery: one effect burns each tick and lowers armor while it lasts, which took a
        /// contained effect. The armor comes back as the effect ends, and the burn's ticks stay.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_3_Each_impact_keeps_its_own_delivery([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var armor = Make.Attribute("Armor");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f), Make.Element(armor, 10f, 10f)));
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 2f, PeriodicTicks = true, Ticks = 2,
                Target = health, Op = ECalculationOperation.Add, Magnitude = -5f,
            });
            burn.ImpactSpecification.AdditionalImpacts.Add(Impact(armor, -4f, EImpactDelivery.Continuous));

            World.Apply(gas, gas, burn);
            await Frames.Step(2);
            Assert.That(Current(gas, armor), Is.EqualTo(6f), "the armor is 4 lower while the burn lasts");
            Assert.That(Current(gas, health), Is.EqualTo(100f), "and the burn hasn't ticked yet");

            await Frames.Seconds(2.5f);
            Assert.That(Current(gas, health), Is.EqualTo(90f), "two ticks of 5 landed");
            Assert.That(Current(gas, armor), Is.EqualTo(10f), "and the armor came back as the burn ended");
        });

        /// <summary>
        /// A cost that spends two attributes is checked for each. The Cost rule read only the first impact, so 0 stamina
        /// paid a cost of 10 mana and 10 stamina.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_3_A_cost_is_checked_for_each_attribute_it_spends(
            [Values] Hub hub, [Values(0f, 20f)] float stamina) => Live(async () =>
        {
            var mana = Make.Attribute("Mana");
            var endurance = Make.Attribute("Stamina");
            var gas = World.Entity(hub, Make.Set(Make.Element(mana, 50f, 100f), Make.Element(endurance, stamina, 100f)));
            var ability = Make.Ability("Costly");
            ability.Cost = Make.Effect("Cost", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = mana, Op = ECalculationOperation.Add, Magnitude = -10f,
            });
            ability.Cost.ImpactSpecification.AdditionalImpacts.Add(Impact(endurance, -10f, EImpactDelivery.OnApply));

            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int index);

            Assert.That(abilities.CanActivateAbility(index), Is.EqualTo(stamina >= 10f),
                $"{stamina} stamina {(stamina >= 10f ? "can" : "can't")} pay 10 mana and 10 stamina");
            await Frames.Step();
        });

        private static AttributeImpactEntry Impact(ForgeAttribute attribute, float magnitude, EImpactDelivery delivery) => new()
        {
            AttributeTarget = attribute,
            Delivery = delivery,
            Operation = ECalculationOperation.Add,
            TargetSlot = EAttributeTarget.Current,
            Magnitude = new ScalableFloat { Magnitude = magnitude, RealMagnitude = EMagnitudeOperation.UseMagnitude },
        };

        private static float Current(IGameplayAbilitySystem gas, ForgeAttribute attribute)
        {
            Assert.That(gas.TryGetAttributeValue(attribute, out var value), $"the entity has {attribute.Name}");
            return value.CurrentValue;
        }
    }
}
