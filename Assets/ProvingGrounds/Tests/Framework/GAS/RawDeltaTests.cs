using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A raw delta is a change handed to the GAS directly, with no effect behind it. Workers see it as they see an
    /// effect's change, with no impact type, while the changes the system makes to itself reach none (Decision 9).
    /// </summary>
    public class RawDeltaTests : ProvingGroundsTest
    {
        public enum RawDelta
        {
            GenerateSimple,
            SourceDerivation
        }

        /// <summary>A raw delta was marked unworkable and typed DisallowImpact, so no impact worker ever saw one.</summary>
        [UnityTest]
        public IEnumerator GAS_25_A_raw_delta_reaches_impact_workers([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            set.WorkerGroup.ImpactWorkers.Add(new ProbeImpactWorker("Watcher", health, Faults));
            var gas = World.Entity(hub, set);
            await Frames.Step();
            Assert.That(Faults.Hits("Watcher.Inline"), Is.EqualTo(0), "the pulse each attribute starts with reaches no worker");

            gas.TryModifyAttribute(health, SourcedModifiedAttributeValue.GenerateSimple(gas, health, -10f, 0f));
            await Frames.Step();

            Assert.That(Faults.Hits("Watcher.Inline"), Is.EqualTo(1), "a raw delta reaches it");
        });

        /// <summary>
        /// An attribute worker that takes any impact type dropped a raw delta on its DisallowImpact type, so armor that
        /// halves losses let a raw one through whole.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_25_Armor_halves_a_raw_loss([Values] Hub hub, [Values] RawDelta made) => Live(async () =>
        {
            var (gas, health) = Armored(hub, anyImpactType: true);
            await Frames.Step();

            gas.TryModifyAttribute(health, Loss(gas, health, made));
            await Frames.Step();

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(85f).Within(0.001f), "a raw 30 lands as 15");
        });

        /// <summary>The control: armor that only takes certain impact types lets an untyped raw delta through whole.</summary>
        [UnityTest]
        public IEnumerator Armor_for_one_impact_type_leaves_a_raw_loss_alone([Values] Hub hub, [Values] RawDelta made) => Live(async () =>
        {
            var (gas, health) = Armored(hub, anyImpactType: false);
            await Frames.Step();

            gas.TryModifyAttribute(health, Loss(gas, health, made));
            await Frames.Step();

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(70f).Within(0.001f), "a raw 30 lands whole");
        });

        // A loss of 30, made the two ways a caller would make one
        private static SourcedModifiedAttributeValue Loss(IGameplayAbilitySystem gas, Attribute health, RawDelta made)
        {
            return made == RawDelta.GenerateSimple
                ? SourcedModifiedAttributeValue.GenerateSimple(gas, health, -30f, 0f)
                : new SourcedModifiedAttributeValue(IAttributeImpactCarrier.GenerateSourceDerivation(gas, health), -30f, 0f);
        }

        // Golden S15's armor: a pre-change worker that halves every loss to Health
        private (IGameplayAbilitySystem, Attribute) Armored(Hub hub, bool anyImpactType)
        {
            var health = Make.Attribute("Health");
            var armor = Make.Attribute("Armor");
            var gas = World.Entity(hub, Make.Set(
                Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(armor, 0.5f, 0.5f)));

            gas.GetAttributeSystem().ProvideWorker(new MathAttributeWorker
            {
                TimingConfig = AbstractAttributeWorker.EChangeEventTiming.PreChange,
                TargetAttribute = health,
                AllowSelfModification = true,
                TargetModification = EAttributeTargetExpanded.Current,
                SignPolicy = ESignPolicyExtended.Negative,
                From = EFromSelfSource.FromSelf,
                RelativeTo = armor,
                Operation = ECalculationOperation.Multiply,
                OperationTarget = EAttributeTarget.Current,
                OperationPolicy = EMathApplicationPolicy.OneMinus,
                AnyImpactType = anyImpactType,
                ImpactTypes = anyImpactType ? new List<Tag>() : new List<Tag> { Make.NewTag("Test.Impact.Fire") },
            });

            return (gas, health);
        }
    }
}
