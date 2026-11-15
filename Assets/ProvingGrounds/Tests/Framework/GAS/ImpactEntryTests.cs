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

        /// <summary>
        /// An additional impact's change runs its own attribute's workers. The attribute system found a change's workers
        /// by its carrier's attribute, and an effect's spec, which carries every one of its impacts, gave its first
        /// impact's: a drain's mana ran health's workers and none of mana's, and a floor on mana let it fall (GAS-78,
        /// seen mapping the architecture).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_78_An_additional_impact_runs_its_own_attributes_workers([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var mana = Make.Attribute("Mana");
            var set = Make.Set(Make.Element(health, 100f, 100f), Make.Element(mana, 50f, 100f));
            set.WorkerGroup.AttributeWorkers.Add(new CantFallBelowAttributeWorker { TargetAttribute = mana, Floor = 45f });
            var gas = World.Entity(hub, set);
            await Frames.Step();
            gas.GetAttributeSystem().ProvideWorker(new ProbeAttributeWorker("Health watcher", health, Faults));
            gas.GetAttributeSystem().ProvideWorker(new ProbeAttributeWorker("Mana watcher", mana, Faults));
            var drain = Make.Effect("Drain", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = -30f,
            });
            drain.ImpactSpecification.AdditionalImpacts.Add(Impact(mana, -10f, EImpactDelivery.OnApply));

            World.Apply(gas, gas, drain);
            await Frames.Step(2);

            Assert.That(Faults.Hits("Health watcher.Intercept"), Is.EqualTo(1), "health's worker saw health's change alone");
            Assert.That(Faults.Hits("Mana watcher.Intercept"), Is.EqualTo(1), "mana's saw mana's");
            Assert.That(Current(gas, mana), Is.EqualTo(45f), "and mana's floor held it");
            Assert.That(Current(gas, health), Is.EqualTo(70f), "as the first impact took 30 health");
        });

        /// <summary>
        /// An additional impact that raises an attribute's Base rescales that attribute's Current, where it scales with
        /// Base: the rescale was queued on the carrier's attribute, the effect's first impact's, so a blessing that doubled
        /// mana's Base raised health instead (GAS-78).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_78_An_additional_impact_rescales_its_own_attribute([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var mana = Make.Attribute("Mana");
            var manaElement = Make.Element(mana, 50f, 100f);
            manaElement.Constraints.AutoScaleWithBase = true;
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f), manaElement));
            await Frames.Step();
            var blessing = Make.Effect("Blessing", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = -10f,
            });
            var raise = Impact(mana, 100f, EImpactDelivery.OnApply);
            raise.TargetSlot = EAttributeTarget.Base;
            blessing.ImpactSpecification.AdditionalImpacts.Add(raise);

            World.Apply(gas, gas, blessing);
            await Frames.Step(3);

            gas.TryGetAttributeValue(mana, out var manaValue);
            Assert.That(manaValue.BaseValue, Is.EqualTo(200f), "mana's Base doubled");
            Assert.That(manaValue.CurrentValue, Is.EqualTo(100f), "and its Current with it, half of its Base as it was");
            Assert.That(Current(gas, health), Is.EqualTo(90f), "while health lost only the first impact's 10");
        });

        /// <summary>
        /// A lasting modifier's end is no hit. Its start is its effect's impact, landing, but its coming off ran a hit's
        /// relay too: its effect's workers heard a second impact, and its dealer's impact workers ran on the change back
        /// (GAS-85, Decision 84, seen mapping the architecture). A change of nothing, as a hit a barrier soaks whole, is
        /// still a hit, as it was.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_85_A_lasting_modifiers_end_is_no_hit([Values] Hub hub) => Live(async () =>
        {
            var armor = Make.Attribute("Armor");
            var dealer = World.Entity(hub, Make.Set(Make.Element(armor, 10f, 10f)));
            var target = World.Entity(hub, Make.Set(Make.Element(armor, 10f, 10f)));
            await Frames.Step();
            dealer.GetAbilitySystem().ProvideWorker(new ProbeImpactWorker("Dealt", armor, Faults));
            var sunder = Make.Effect("Sunder", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 0.5f, Workers = { new ProbeEffectWorker("Sunder", Faults) },
            });
            sunder.ImpactSpecification.AdditionalImpacts.Add(Impact(armor, -4f, EImpactDelivery.Continuous));

            World.Apply(dealer, target, sunder);
            await Frames.Step(2);
            Assert.That(Current(target, armor), Is.EqualTo(6f), "the sunder lowers armor while it lasts");
            int impacts = Faults.Hits("Sunder.Impact"), dealt = Faults.Hits("Dealt.Inline");
            Assert.That(impacts, Is.GreaterThan(0), "its start is its impact");
            Assert.That(dealt, Is.GreaterThan(0), "and a hit its dealer's impact workers hear");

            await Frames.Seconds(0.8f);
            Assert.That(Current(target, armor), Is.EqualTo(10f), "its end gives the armor back");
            Assert.That(Faults.Hits("Sunder.Impact"), Is.EqualTo(impacts), "and is no second impact");
            Assert.That(Faults.Hits("Dealt.Inline"), Is.EqualTo(dealt), "nor a hit its dealer's workers hear");

            var nothing = Make.Effect("Nothing", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = armor, Magnitude = 0f, Workers = { new ProbeEffectWorker("Nothing", Faults) },
            });
            World.Apply(dealer, target, nothing);
            await Frames.Step(2);
            Assert.That(Faults.Hits("Nothing.Impact"), Is.EqualTo(1), "a change of nothing is still a hit");
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

        /// <summary>
        /// An additional impact authored to land as a ticking effect does was dropped: Tick On Application was the only
        /// durational path that committed OnApply entries, so an effect that didn't tick on application dealt its ticks
        /// alone (GAS-71, found building the Dota lab's Ice Blast). Its OnApply impact lands once, as it does, and its
        /// ticks follow.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_71_An_impact_lands_as_a_ticking_effect_does([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)));
            var blast = Make.Effect("Blast", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 2f, PeriodicTicks = true, Ticks = 2,
                Target = health, Op = ECalculationOperation.Add, Magnitude = -10f,
            });
            blast.ImpactSpecification.AdditionalImpacts.Add(Impact(health, -325f, EImpactDelivery.OnApply));

            World.Apply(gas, gas, blast);
            await Frames.Step(2);
            Assert.That(Current(gas, health), Is.EqualTo(675f), "325 lands as the effect does, before any tick");

            await Frames.Seconds(2.5f);
            Assert.That(Current(gas, health), Is.EqualTo(655f), "and its two ticks of 10 follow");
        });

        /// <summary>
        /// An effect that ticks on application commits its OnApply impacts with that tick, once, as it did before GAS-71:
        /// the commit as an effect lands isn't made a second time.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_71_An_effect_that_ticks_on_application_lands_its_impact_once([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)));
            var pulse = Make.Effect("Pulse", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 2f, TickOnApplication = true,
                Target = health, Op = ECalculationOperation.Add, Magnitude = -10f,
            });
            pulse.ImpactSpecification.AdditionalImpacts.Add(Impact(health, -325f, EImpactDelivery.OnApply));

            World.Apply(gas, gas, pulse);
            await Frames.Step(2);
            Assert.That(Current(gas, health), Is.EqualTo(665f), "its pulse of 10 and the 325, each once");
        });
    }
}
