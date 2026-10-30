using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EffectStackingTests : ProvingGroundsTest
    {
        /// <summary>
        /// Two bugs in a row. The base constructor reached DurationRemaining before the derived constructor had
        /// made its packet list, so applying such an effect threw. Past that, the packets were structs walked
        /// with foreach, so what counted down was a copy and no stack ever expired.
        /// </summary>
        [UnityTest]
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

        /// <summary>Each stack keeps its own tick timer: stacks a half second apart tick a half second apart.</summary>
        [UnityTest]
        public IEnumerator GAS_04_Each_stack_ticks_on_its_own_timer() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 50f, 100f, EAttributeOverflowPolicy.ZeroToBase)));
            var regen = Make.Effect("Regen", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational,
                Target = health, Op = ECalculationOperation.Add, Magnitude = 5f,
                Duration = 2f, PeriodicTicks = true, Ticks = 2,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = EEffectStackPolicy.StacksHaveIndependentDurations,
                StackAmount = 1,
            });

            World.Apply(gas, gas, regen);
            await Frames.Seconds(0.5f);
            World.Apply(gas, gas, regen);

            await Frames.Seconds(0.75f);
            Assert.That(Value(gas, health), Is.EqualTo(55f), "the first stack ticked at 1 s");

            await Frames.Seconds(0.5f);
            Assert.That(Value(gas, health), Is.EqualTo(60f), "the second ticked at 1.5 s");
        });

        /// <summary>A refresh reached a copy too, so the oldest stack expired on its first schedule.</summary>
        [UnityTest]
        public IEnumerator GAS_04_A_refresh_reaches_the_oldest_stack() => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(EEffectStackPolicy.StacksHaveIndependentDurations);
            effect.DurationSpecification.ReApplicationInteraction = EEffectInteractionPolicy.RefreshContainerDuration;

            World.Apply(gas, gas, effect);
            await Frames.Seconds(0.5f);
            World.Apply(gas, gas, effect);

            await Frames.Seconds(0.75f);
            Assert.That(Stacks(gas, effect), Is.EqualTo(2), "the refreshed stack outlives its first second");
        });

        private static float Value(IGameplayAbilitySystem gas, Attribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }

        /// <summary>The containers added one stack whatever amount they were asked for.</summary>
        [UnityTest]
        public IEnumerator GAS_05_Each_application_adds_the_stack_amount([Values] EEffectStackPolicy policy) => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(policy, stackAmount: 3);

            World.Apply(gas, gas, effect);
            Assert.That(Stacks(gas, effect), Is.EqualTo(3), "the first application");

            World.Apply(gas, gas, effect);
            Assert.That(Stacks(gas, effect), Is.EqualTo(6), "the second application");

            await Frames.Step();
        });

        /// <summary>The stack amount only consulted its configs when it had a scaler, so a plain Max did nothing.</summary>
        [UnityTest]
        public IEnumerator GAS_05_Max_stacks_is_enforced([Values] EEffectStackPolicy policy) => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(policy);
            effect.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig { Max = 2 });

            for (int i = 0; i < 4; i++) World.Apply(gas, gas, effect);

            Assert.That(Stacks(gas, effect), Is.EqualTo(2), "held at Max");
            await Frames.Step();
        });

        /// <summary>
        /// Each config picks the container in turn and the last answer stands, so they have to go in priority
        /// order, or a config with no opinion overrules a bound that asked for a new container.
        /// </summary>
        [UnityTest]
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

        /// <summary>The amount goes through the configs in the same order, so a bound caps what the rest ask for.</summary>
        [UnityTest]
        public IEnumerator GAS_05_A_bound_caps_an_amount_set_by_a_later_config() => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(EEffectStackPolicy.StacksShareOneDuration);
            effect.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig { Max = 2 });
            effect.DurationSpecification.StackingConfigs.Add(new TripledAmount());

            World.Apply(gas, gas, effect);

            Assert.That(Stacks(gas, effect), Is.EqualTo(2), "tripled to 3, and held at Max");
            await Frames.Step();
        });

        private sealed class TripledAmount : AbstractStackingConfig
        {
            public override void InitializeContainer(AbstractStackingEffectContainer container) { }

            public override int GetStacks(AbstractStackingEffectContainer container, int amount = 1) => amount * 3;
        }

        /// <summary>
        /// The field defaults to 0 on any asset authored before amounts took effect, and every application
        /// added one regardless, so that's what an unset amount has to go on meaning.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_05_An_unset_stack_amount_is_one_stack([Values] EEffectStackPolicy policy) => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            var effect = Stacking(policy, stackAmount: 0);

            World.Apply(gas, gas, effect);
            World.Apply(gas, gas, effect);

            Assert.That(Stacks(gas, effect), Is.EqualTo(2));
            await Frames.Step();
        });

        /// <summary>The bound alone gets its way, and a new container starts counting from nothing.</summary>
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
