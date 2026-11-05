using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FEAT-13's two cheap changes (Decision 30), which keep networking possible: one injectable clock and one seedable
    /// random source, which the runtime reads instead of Unity's Time and Random, and IDs scoped to an entity.
    /// </summary>
    public class ForgeTimeTests : ProvingGroundsTest
    {
        // A clock whose frames each take a fixed step, with Unity's time and frame
        private sealed class SteppingClock : IForgeClock
        {
            public float Step;
            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        /// <summary>
        /// An effect's time passed by Unity's Time.deltaTime, so no clock of a game's own, a replay's or a server's, could
        /// step it (FEAT-13). A clock put in steps it.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_13_A_clock_put_in_steps_the_effects([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var slow = Make.Effect("Slow", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = null });
            var previous = ForgeTime.Use(new SteppingClock { Step = 0.5f });
            try
            {
                Assert.IsTrue(World.Apply(gas, gas, slow), "the effect lands");
                await Frames.Step(4);
                Assert.IsTrue(gas.TryGetEffectContainer(slow, out var container), "and lasts");
                Assert.That(container.DurationRemaining, Is.EqualTo(8f).Within(0.6f), "its four frames took half a second each");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        private GameplayEffect Lucky(ForgeAttribute strength) => Make.Effect("Lucky", new EffectConfig
        {
            Policy = EEffectDurationPolicy.Infinite, Target = strength, Op = ECalculationOperation.Add, Magnitude = 1f,
            MagnitudeScaler = new RandomizedScaler { VarianceMode = EVarianceMode.Multiplier, MinMultiplier = 0f, MaxMultiplier = 100f, RollOncePerSpec = true },
            MagnitudeOp = EMagnitudeOperation.UseScaler,
        });

        private static float Current(IGameplayAbilitySystem gas, ForgeAttribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }

        /// <summary>
        /// A roll drew from UnityEngine.Random, so it could be repeated only by seeding the whole game's random numbers, and
        /// anything else that drew from them moved it (FEAT-13). A seeded source repeats its rolls, and leaves Unity's alone.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_13_A_seeded_source_rolls_the_same_again([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var first = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f)), "First");
            var second = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f)), "Second");
            var unity = UnityEngine.Random.state;
            var previous = ForgeRandom.Use(new SeededRandom(7));
            try
            {
                World.Apply(first, first, Lucky(strength));
                await Frames.Step();
                ForgeRandom.Use(new SeededRandom(7));
                UnityEngine.Random.InitState(1234);
                World.Apply(second, second, Lucky(strength));
                await Frames.Step();
            }
            finally
            {
                ForgeRandom.Use(previous);
                UnityEngine.Random.state = unity;
            }
            Assert.That(Current(first, strength), Is.Not.EqualTo(10f), "the first roll moved strength");
            Assert.AreEqual(Current(first, strength), Current(second, strength), "and the same seed rolls the same, whatever Unity's is");
        });

        /// <summary>
        /// An effect's application had no ID, so nothing could tell two of one effect apart, as a looping cue or a replay
        /// needs (FEAT-13). Each application is numbered by the entity it lands on.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_13_Each_entity_numbers_its_own_effect_applications([Values] Hub hub) => Live(async () =>
        {
            var one = World.Entity(hub, name: "One");
            var two = World.Entity(hub, name: "Two");
            var mark = Make.Effect("Mark", new EffectConfig { Target = null });
            World.Apply(one, one, mark);
            World.Apply(one, one, mark);
            World.Apply(two, two, mark);
            await Frames.Step();

            var ones = one.GetEffectSystem().Shelf.Where(container => container.Spec.Base == mark).Select(container => container.Spec.ApplicationId).ToList();
            var twos = two.GetEffectSystem().Shelf.Where(container => container.Spec.Base == mark).Select(container => container.Spec.ApplicationId).ToList();
            Assert.AreEqual(2, ones.Count, "the first entity holds both");
            Assert.That(ones[0], Is.GreaterThan(0), "each application has an ID");
            Assert.AreEqual(ones[0] + 1, ones[1], "counted as they land");
            Assert.AreEqual(ones[0], twos[0], "by the entity they land on");
        });

        // The ID of the activation an ability's container runs now
        private static int RunningHandleId(AbilitySpecContainer container)
        {
            var handles = (IList)typeof(AbilitySpecContainer).GetField("_activeHandles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
            Assert.That(handles.Count, Is.GreaterThan(0), "the ability runs");
            return ((AbilityActivationHandle)handles[0]).HandleId;
        }

        /// <summary>
        /// Activations were numbered by one count for the whole game, in the order of every entity's activations, which two
        /// machines can't share (FEAT-13). Each ability system numbers its own.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_13_Each_ability_system_numbers_its_own_activations([Values] Hub hub) => Live(async () =>
        {
            var channel = Make.Ability("Channel", Make.Stage(new DelayTask { DelayMilliseconds = 600000 }));
            var one = World.Entity(hub, name: "One").GetAbilitySystem();
            var two = World.Entity(hub, name: "Two").GetAbilitySystem();
            Assert.IsTrue(one.GiveAbility(channel, 1, out int oneIndex), "the first has the ability");
            Assert.IsTrue(two.GiveAbility(channel, 1, out int twoIndex), "and the second");
            await Frames.Step();

            Assert.IsTrue(one.TryActivateAbility(one.CreateActivationRequest(oneIndex)), "the first activates it");
            Assert.IsTrue(two.TryActivateAbility(two.CreateActivationRequest(twoIndex)), "and the second");
            await Frames.Step();

            int oneId = RunningHandleId(one.GetAbilityContainers().Single(container => container.Index == oneIndex));
            int twoId = RunningHandleId(two.GetAbilityContainers().Single(container => container.Index == twoIndex));
            Assert.AreEqual(oneId, twoId, "each numbers its activations from the start");
        });
    }
}
