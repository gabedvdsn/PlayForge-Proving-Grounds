using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EffectStackingTests : ProvingGroundsTest
    {
        /// <summary>
        /// Two bugs in a row. The base constructor reaches DurationRemaining before the derived constructor has
        /// made its packet list, so applying such an effect throws. Past that, the packets are structs walked
        /// with foreach, so what counts down is a copy and no stack ever expires.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_04_Stacks_with_independent_durations_expire_one_at_a_time([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var effect = Stacking(EEffectStackPolicy.StacksHaveIndependentDurations);

            Assert.DoesNotThrow(() => World.Apply(gas, gas, effect), "the effect applies");
            await Frames.Seconds(0.5f);

            World.Apply(gas, gas, effect);
            Assert.That(Stacks(gas, effect), Is.EqualTo(2), "two stacks");

            await Frames.Seconds(0.5f);
            await Frames.Step(2);
            Assert.That(Stacks(gas, effect), Is.EqualTo(1), "the first stack's second is up");

            await Frames.Seconds(0.5f);
            await Frames.Step(2);
            Assert.That(gas.TryGetEffectContainer(effect, out _), Is.False, "the last stack's second is up, and the effect is gone");
        });

        /// <summary>The containers add one stack whatever amount they're asked for.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_05_Each_application_adds_the_stack_amount(
            [Values(EEffectStackPolicy.StacksShareOneDuration, EEffectStackPolicy.DurationTakenFromOneStack)] EEffectStackPolicy policy) => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(policy, stackAmount: 3);

            World.Apply(gas, gas, effect);
            Assert.That(Stacks(gas, effect), Is.EqualTo(3), "the first application");

            World.Apply(gas, gas, effect);
            Assert.That(Stacks(gas, effect), Is.EqualTo(6), "the second application");

            await Frames.Step();
        });

        /// <summary>The stack amount only consults its configs when it has a scaler, so a plain Max does nothing.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_05_Max_stacks_is_enforced(
            [Values(EEffectStackPolicy.StacksShareOneDuration, EEffectStackPolicy.DurationTakenFromOneStack)] EEffectStackPolicy policy) => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(policy);
            effect.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig { Max = 2 });

            for (int i = 0; i < 4; i++) World.Apply(gas, gas, effect);

            Assert.That(Stacks(gas, effect), Is.EqualTo(2), "held at Max");
            await Frames.Step();
        });

        /// <summary>
        /// Each config picks the container in turn and the last answer stands, so a config with no opinion
        /// overrules a bound that asked for a new container.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_05_A_later_config_doesnt_overrule_the_stack_bound() => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(EEffectStackPolicy.StacksShareOneDuration);
            effect.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig { Max = 1, OnMaxPolicy = EStackBoundsOnMaxPolicy.AppendNewContainer });
            effect.DurationSpecification.StackingConfigs.Add(new InitializationOperation());

            World.Apply(gas, gas, effect);
            World.Apply(gas, gas, effect);

            Assert.That(Containers(gas, effect), Is.EqualTo(2), "the full container stayed full, and a new one started");
            await Frames.Step();
        });

        /// <summary>The control for the test above: the bound alone gets its way.</summary>
        [UnityTest]
        public IEnumerator A_stack_bound_alone_starts_a_new_container_at_Max() => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(EEffectStackPolicy.StacksShareOneDuration);
            effect.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig { Max = 1, OnMaxPolicy = EStackBoundsOnMaxPolicy.AppendNewContainer });

            World.Apply(gas, gas, effect);
            World.Apply(gas, gas, effect);

            Assert.That(Containers(gas, effect), Is.EqualTo(2));
            await Frames.Step();
        });

        private GameplayEffect Stacking(EEffectStackPolicy policy, int stackAmount = 1)
        {
            return Make.Effect("Stacking", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational,
                Duration = 1f,
                Target = null,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = policy,
                StackAmount = stackAmount,
            });
        }

        private static int Stacks(IGameplayAbilitySystem gas, GameplayEffect effect)
        {
            Assert.That(gas.TryGetEffectContainer(effect, out var container), "the effect is on the entity");
            return ((AbstractStackingEffectContainer)container).Stacks;
        }

        private static int Containers(IGameplayAbilitySystem gas, GameplayEffect effect)
        {
            return gas.TryGetEffectContainers(effect, out var containers) ? containers.Length : 0;
        }
    }
}
