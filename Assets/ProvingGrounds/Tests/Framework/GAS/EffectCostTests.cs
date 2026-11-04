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
