using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>What applying an effect costs, as an entity carries more.</summary>
    public class EffectCostTests : ProvingGroundsTest
    {
        /// <summary>
        /// Each application re-checked the requirements of every effect already on the entity and rebuilt all its tags,
        /// twice over for an instant effect, and a re-application walked the whole shelf to find its own containers. An
        /// application cost as much as the entity carried, so many effects on one entity, or an area effect over
        /// entities that carry many, cost the square. An application now costs the same however much the entity carries.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P3_An_application_costs_the_same_however_much_the_entity_carries([Values] Hub hub) => Live(async () =>
        {
            var pulse = Make.Effect("Pulse", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = null });
            var stacking = Make.Effect("Stacking", new EffectConfig { Target = null, Reapply = EEffectReApplicationPolicy.StackExistingContainers });

            var light = Carrying(hub, "Light", 2, stacking);
            var heavy = Carrying(hub, "Heavy", 1000, stacking);
            await Frames.Step();

            double lightPulse = Fastest(light, pulse), heavyPulse = Fastest(heavy, pulse);
            double lightStack = Fastest(light, stacking), heavyStack = Fastest(heavy, stacking);
            TestContext.WriteLine($"ms each, carrying 2 → 1000: instant {lightPulse:F4} → {heavyPulse:F4}, " +
                                  $"re-application {lightStack:F4} → {heavyStack:F4}");

            Assert.That(heavyPulse, Is.LessThan(3 * lightPulse), "an instant effect, in milliseconds each");
            Assert.That(heavyStack, Is.LessThan(3 * lightStack), "a re-application, in milliseconds each");
        });

        /// <summary>
        /// Ending effects searched the list of finished ones as each was marked, searched it again for every container as
        /// the shelf ticked, and took each off the shelf and out of the index with a linear remove, so a cleanse or a mass
        /// expiry of n effects cost n². Ending one now costs the same however many end with it.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P5_Ending_an_effect_costs_the_same_however_many_end_with_it([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, name: "Cleansed", observed: false);
            await Frames.Step();

            double few = PerEnded(gas, 50), many = PerEnded(gas, 3000);
            TestContext.WriteLine($"ms per ended effect, 50 → 3000 at once: {few:F4} → {many:F4}");

            Assert.That(many, Is.LessThan(3 * few), "in milliseconds each");
        });

        // Milliseconds per container, for `count` of one effect ended at once: marked by one removal, then taken off at the
        // next tick. The fastest of several rounds
        private double PerEnded(IGameplayAbilitySystem gas, int count)
        {
            const int rounds = 5;
            var carried = Make.Effect($"Carried {count}", new EffectConfig { Target = null });
            var effects = gas.GetEffectSystem();
            double fastest = double.MaxValue;
            var watch = new Stopwatch();
            for (int round = 0; round < rounds; round++)
            {
                for (int i = 0; i < count; i++) World.Apply(gas, gas, carried);

                // The requirement pass the applications left, outside the timing
                effects.Tick();

                watch.Restart();
                gas.RemoveGameplayEffect(carried);
                effects.Tick();
                watch.Stop();
                fastest = Math.Min(fastest, watch.Elapsed.TotalMilliseconds / count);
            }
            return fastest;
        }

        // An entity carrying `count` effects, and one more it stacks
        private IGameplayAbilitySystem Carrying(Hub hub, string name, int count, GameplayEffect stacking)
        {
            var gas = World.Entity(hub, name: name, observed: false);
            var carried = Make.Effect($"{name} Carried", new EffectConfig { Target = null });
            for (int i = 0; i < count; i++) World.Apply(gas, gas, carried);
            World.Apply(gas, gas, stacking);
            return gas;
        }

        // Milliseconds per application, the fastest of several rounds: the slow rounds are the ones a collection or
        // another process interrupted
        private double Fastest(IGameplayAbilitySystem gas, GameplayEffect effect)
        {
            const int rounds = 7, applications = 50;
            double fastest = double.MaxValue;
            var watch = new Stopwatch();
            for (int round = 0; round < rounds; round++)
            {
                watch.Restart();
                for (int i = 0; i < applications; i++) World.Apply(gas, gas, effect);
                watch.Stop();
                fastest = Math.Min(fastest, watch.Elapsed.TotalMilliseconds / applications);
            }
            return fastest;
        }
    }
}
