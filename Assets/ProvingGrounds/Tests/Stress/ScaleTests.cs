using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The stress suite's scale runs (the review's Module 3): PlayForge with as many entities, effects and targets as a
    /// busy game has, under the live ProcessControl the other suites run under. A cost is compared per entity with the
    /// same run at a tenth of the size, as the cost tests compare, so the machine's speed doesn't decide it.
    /// </summary>
    [Category("Stress")]
    public class ScaleTests : ProvingGroundsTest
    {
        private const int Entities = 500, EffectsEach = 20, Targets = 200;

        /// <summary>
        /// 500 entities, each with 20 effects that tick every quarter second, run 2.1 seconds: each effect ticks 8 times on
        /// each entity, and a frame among them, ticks and all, allocates nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_500_entities_with_20_effects_each_tick_on_time_and_allocate_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var entities = Burning(hub, health, Burns(health), Entities);

            // A tick every quarter second for 2.1 s: 8 of each burn, which take 1 to 20 a tick
            await Frames.Seconds(2.1f);
            const float left = 10000f - 8 * 210f;
            int wrong = entities.Count(gas => HealthOf(gas, health) != left);
            Assert.That(wrong, Is.EqualTo(0), $"entities whose burns didn't each tick 8 times; the first has {HealthOf(entities[0], health)}");

            using var probe = new AllocationProbe();
            var frames = await probe.MeasureFrames(Frames, 60);
            Assert.That(frames.Total, Is.EqualTo(0), frames.ToString());
        });

        /// <summary>
        /// An entity's frame, its 20 effects counting down and ticking, costs about as much among 500 as among 50: within
        /// three times, the bound the cost tests hold an application to. A cost that grew with the count would be ten
        /// times as much at 500.
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_An_entitys_frame_costs_as_much_among_500_as_among_50([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var burns = Burns(health);
            const int few = Entities / 10;

            Burning(hub, health, burns, few);
            await Frames.Step(5);
            double perFew = await StressMeasure.StepNanoseconds(Frames, 30) / few;

            Burning(hub, health, burns, Entities - few);
            await Frames.Step(5);
            double perMany = await StressMeasure.StepNanoseconds(Frames, 30) / Entities;

            TestContext.WriteLine($"µs an entity a frame, {few} → {Entities}: {perFew / 1000:F2} → {perMany / 1000:F2}");
            Assert.That(perMany, Is.LessThan(3 * perFew), "an entity's frame, in PlayForge's step loops");
        });

        /// <summary>
        /// An area ability, every match within 10 m and then Apply Effects, cast once among 200 entities, strikes each of
        /// them once. It struck one, the farthest (GAS-62).
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_An_area_ability_strikes_200_targets_at_once([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            var targets = new List<GameplayAbilitySystem>();
            for (int i = 0; i < Targets; i++)
            {
                // A disc of them around the caster, 9 m out at most, as a sunflower's seeds sit
                float radius = 9f * Mathf.Sqrt((i + 0.5f) / Targets), turn = i * 2.39996f;
                var target = World.MonoEntity(set, $"Target {i}");
                target.transform.position = new Vector3(radius * Mathf.Cos(turn), 0f, radius * Mathf.Sin(turn));
                target.gameObject.AddComponent<SphereCollider>();
                targets.Add(target);
            }
            Physics.SyncTransforms();

            var hurt = Make.Effect("Hurt", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = -30f,
            });
            var nova = Make.Ability("Nova", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { hurt } }));
            nova.Behaviour.Targeting = new AutoTargetTask
            {
                Range = 10f, MaxTargets = 0, Mode = EAutoTargetMode.Nearest,
                Requirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
            };
            var abilities = World.Entity(hub, name: "Caster").GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(nova, 1, out int index), "the caster knows Nova");

            var watch = Stopwatch.StartNew();
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "Nova is cast");
            await Frames.Until(() => targets.All(target => HealthOf(target, health) < 100f), 10, "Nova strikes them all");
            watch.Stop();

            Assert.That(targets.Count(target => HealthOf(target, health) == 70f), Is.EqualTo(Targets), "each of them once");
            TestContext.WriteLine($"Nova over {Targets}: {watch.Elapsed.TotalMilliseconds:F2} ms, its frames included");
        });

        // Twenty burns, 1 to 20 a tick, each ticking every quarter second for 10 s
        private List<GameplayEffect> Burns(ForgeAttribute health) => Enumerable.Range(1, EffectsEach).Select(k => Make.Effect($"Burn {k}", new EffectConfig
        {
            Policy = EEffectDurationPolicy.Durational, Target = health, Op = ECalculationOperation.Add, Magnitude = -k,
            Duration = 10f, PeriodicTicks = true, Ticks = 40,
        })).ToList();

        // `count` entities with 10,000 health, each with every burn on it. Unobserved: the invariants' listening would cost
        // and allocate what these tests measure
        private List<IGameplayAbilitySystem> Burning(Hub hub, ForgeAttribute health, List<GameplayEffect> burns, int count)
        {
            var set = Make.Set(Make.Element(health, 10000f, 10000f));
            var entities = new List<IGameplayAbilitySystem>(count);
            for (int i = 0; i < count; i++)
            {
                var gas = World.Entity(hub, set, $"Burning {i}", observed: false);
                foreach (var burn in burns)
                {
                    if (!World.Apply(gas, gas, burn)) Assert.Fail($"{burn.name} didn't land on entity {i}");
                }
                entities.Add(gas);
            }
            return entities;
        }

        private static float HealthOf(IGameplayAbilitySystem gas, ForgeAttribute health)
        {
            gas.TryGetAttributeValue(health, out var value);
            return value.CurrentValue;
        }
    }
}
