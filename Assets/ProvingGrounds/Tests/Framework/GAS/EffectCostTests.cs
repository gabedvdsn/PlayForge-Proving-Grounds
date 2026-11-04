using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>What effects cost: applying and ending them as an entity carries more, and what ticking allocates.</summary>
    public class EffectCostTests : ProvingGroundsTest
    {
        /// <summary>
        /// A commit allocated: a change object, a list of frozen impacters with an impacter for each, and a node kept on its
        /// carrier for every impact the carrier ever made. So a damage-over-time effect allocated on every tick, and kept
        /// what it allocated for as long as it lasted. A frame in which an effect ticks and commits now allocates nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P6_A_frame_in_which_an_effect_commits_allocates_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 1e6f, 1e6f)), name: "Burning", observed: false);
            World.Apply(gas, gas, Make.Effect("Burn", new EffectConfig { Target = health, Magnitude = -1f, PeriodicTicks = true, FixedDeltaTime = 1f }));
            await Frames.Step(20);

            gas.TryGetAttributeValue(health, out var before);
            using var probe = new AllocationProbe();
            var frames = await probe.MeasureFrames(Frames, 600);
            gas.TryGetAttributeValue(health, out var after);

            Assert.That(before.CurrentValue - after.CurrentValue, Is.GreaterThanOrEqualTo(600f), "it committed every frame");
            Assert.That(frames.Total, Is.EqualTo(0), frames.ToString());
        });

        /// <summary>
        /// A commit a worker adjusted, as armor adjusts incoming damage, made a carrier to wrap the adjusted delta in, and
        /// the worker's sign check built an array and summed it through LINQ. A frame in which armor adjusts an effect's
        /// commit now allocates nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P6_A_frame_in_which_armor_adjusts_a_commit_allocates_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var armor = Make.Attribute("Armor");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 1e6f, 1e6f), Make.Element(armor, 0.5f, 0.5f)),
                name: "Armored", observed: false);
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
            });
            World.Apply(gas, gas, Make.Effect("Burn", new EffectConfig { Target = health, Magnitude = -1f, PeriodicTicks = true, FixedDeltaTime = 1f }));
            await Frames.Step(20);

            gas.TryGetAttributeValue(health, out var before);
            await Frames.Step();
            gas.TryGetAttributeValue(health, out var after);
            Assert.That(before.CurrentValue - after.CurrentValue, Is.EqualTo(0.5f).Within(0.001f), "armor halved the tick");

            using var probe = new AllocationProbe();
            var frames = await probe.MeasureFrames(Frames, 600);

            Assert.That(frames.Total, Is.EqualTo(0), frames.ToString());
        });

        /// <summary>
        /// A tick looked up its effect's impacted attributes and their impacters, each lookup building a new list or set,
        /// and walked its contained effects through LINQ: about 530 bytes a tick before anything committed. A tick now
        /// allocates nothing up to its commit. Here the commit finds no such attribute on the entity and returns, which
        /// leaves the tick's own work; what a commit allocates is GAS-P6.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P4_A_tick_allocates_nothing_up_to_its_commit([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, name: "Ticking", observed: false);
            var elsewhere = Make.Attribute("Elsewhere");
            World.Apply(gas, gas, Make.Effect("Pulse", new EffectConfig { Target = elsewhere, Magnitude = -1f, PeriodicTicks = true, FixedDeltaTime = 1f }));
            await Frames.Step();

            var effects = gas.GetEffectSystem();
            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(effects.Tick, runs: 500, warmup: 20);

            Assert.That(bytes, Is.EqualTo(0f), "bytes per tick");
        });

        /// <summary>
        /// Each recompute built a new list for the multipliers that pool their bonus, and every tick that committed
        /// recomputed. A recompute now allocates nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P4_A_recompute_allocates_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), name: "Recomputed", observed: false);
            World.Apply(gas, gas, Make.Effect("Plus", new EffectConfig { Target = health, Magnitude = 5f }));
            World.Apply(gas, gas, Make.Effect("Pooled", new EffectConfig
            {
                Target = health, Op = ECalculationOperation.Multiply, MultiplyBehaviour = EMultiplyBehaviour.AdditiveBonus, Magnitude = 1.1f,
            }));
            World.Apply(gas, gas, Make.Effect("Flat", new EffectConfig { Target = health, Op = ECalculationOperation.FlatBonus, Magnitude = 2f }));
            await Frames.Step();

            var attributes = gas.GetAttributeSystem();
            attributes.TryGetCachedValue(health, out var cached);
            var cache = attributes.GetAttributeCache();
            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() => cached.Recompute(cache), runs: 500, warmup: 20);

            Assert.That(bytes, Is.EqualTo(0f), "bytes per recompute");
        });

        /// <summary>
        /// Re-registering a carrier's modifiers, as a stack change does, removed its old entries through a lambda that
        /// captured the carrier and built new ones, allocating each time. Refreshing now reuses them and allocates nothing,
        /// as does taking a carrier's modifiers away and registering them again.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P4_Refreshing_a_modifier_allocates_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), name: "Refreshed", observed: false);
            var aura = Make.Effect("Aura", new EffectConfig { Target = health, Magnitude = 5f });
            World.Apply(gas, gas, aura);
            await Frames.Step();

            gas.TryGetEffectContainer(aura, out var container);
            var spec = container.Spec;
            var impacters = spec.GetImpactersFor(health, EImpactDelivery.Continuous);
            var attributes = gas.GetAttributeSystem();
            attributes.TryGetCachedValue(health, out var cached);
            var cache = attributes.GetAttributeCache();
            using var probe = new AllocationProbe();
            float refresh = probe.MeasureEach(() => cached.RegisterModifier(spec, impacters, cache), runs: 500, warmup: 20);
            float cycle = probe.MeasureEach(() =>
            {
                cached.UnregisterModifier(spec, cache);
                cached.RegisterModifier(spec, impacters, cache);
            }, runs: 500, warmup: 20);

            Assert.That(refresh, Is.EqualTo(0f), "bytes per refresh");
            Assert.That(cycle, Is.EqualTo(0f), "bytes per removal and registration again");
        });

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
