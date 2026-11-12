using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-12, Decision 52: barriers. An attribute with the Barrier role absorbs for the one it names: each workable loss to
    /// that one comes off it first, as much as it holds, and two hits in a frame soak no more than it holds. It decays at
    /// its rate, a number or a share of its base, and what a lasting effect gives it can fade to 0 by the effect's end.
    /// </summary>
    public class BarrierTests : ProvingGroundsTest
    {
        private ForgeAttribute health, barrier;

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

        private IForgeClock previousClock;
        private bool clockSet;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            barrier = Make.Attribute("Barrier");
            barrier.Role = EAttributeRole.Barrier;
            barrier.AbsorbsFor = health;
        }

        [TearDown]
        public void PutBackTime()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            clockSet = false;
        }

        private void Stepping(float seconds)
        {
            var was = ForgeTime.Use(new SteppingClock { Step = seconds });
            if (!clockSet) previousClock = was;
            clockSet = true;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name, float shield) => World.Entity(hub, Make.Set(
            Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
            Make.Element(barrier, shield, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        private GameplayEffect Blow(float amount) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>A loss to Health comes off the barrier first, as much as it holds; the rest goes on to Health.</summary>
        [UnityTest]
        public IEnumerator TW_12_A_barrier_takes_a_loss_first([Values] Hub hub) => Live(async () =>
        {
            var hero = Fighter(hub, "Hero", 30f);
            var foe = Fighter(hub, "Foe", 0f);
            await Frames.Step();

            Assert.IsTrue(World.Apply(foe, hero, Blow(20f)));
            await Frames.Step(2);
            Assert.AreEqual(100f, Value(hero, health), 0.01f, "the barrier takes the whole of a 20 it can hold");
            Assert.AreEqual(10f, Value(hero, barrier), 0.01f, "and has 10 left");

            Assert.IsTrue(World.Apply(foe, hero, Blow(25f)));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(hero, barrier), 0.01f, "it takes what it holds");
            Assert.AreEqual(85f, Value(hero, health), 0.01f, "and the rest goes on to Health");
        });

        /// <summary>Two hits in one frame soak no more than the barrier holds.</summary>
        [UnityTest]
        public IEnumerator TW_12_Two_hits_in_a_frame_soak_no_more_than_it_holds([Values] Hub hub) => Live(async () =>
        {
            var hero = Fighter(hub, "Hero", 30f);
            var foe = Fighter(hub, "Foe", 0f);
            await Frames.Step();

            Assert.IsTrue(World.Apply(foe, hero, Blow(20f)));
            Assert.IsTrue(World.Apply(foe, hero, Blow(20f)));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(hero, barrier), 0.01f, "the barrier's 30 soaked, 20 and then 10");
            Assert.AreEqual(90f, Value(hero, health), 0.01f, "and the second's other 10 went on to Health");
        });

        /// <summary>A barrier decays at its rate, by its holder's time: a share of its base, or a number.</summary>
        [UnityTest]
        public IEnumerator TW_12_A_barrier_decays_at_its_rate([Values] Hub hub) => Live(async () =>
        {
            barrier.DecayPerSecond = 0.1f;
            barrier.DecayAsShareOfBase = true;
            Stepping(0.1f);
            var hero = Fighter(hub, "Hero", 30f);
            await Frames.Step(10);
            Assert.AreEqual(20f, Value(hero, barrier), 1.5f, "a tenth of its base of 100 a second: 10 off in a second");
            await Frames.Step(30);
            Assert.AreEqual(0f, Value(hero, barrier), 0.01f, "and no further than 0");
        });

        /// <summary>What a lasting effect gives a barrier that fades with its effect fades to 0 by the effect's end.</summary>
        [UnityTest]
        public IEnumerator TW_12_A_lasting_effects_barrier_fades_by_its_end([Values] Hub hub) => Live(async () =>
        {
            barrier.FadesWithEffect = true;
            Stepping(0.1f);
            var hero = Fighter(hub, "Hero", 0f);
            var shield = Make.Effect("Shield", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 2f, Target = barrier, Magnitude = 40f,
            });
            await Frames.Step();
            Assert.IsTrue(World.Apply(hero, hero, shield), "the shield's on");
            await Frames.Step(2);
            Assert.That(Value(hero, barrier), Is.InRange(34f, 40f), "about all of its 40 at first");
            await Frames.Step(9);
            Assert.That(Value(hero, barrier), Is.InRange(14f, 26f), "about half of it a second in");
            await Frames.Step(12);
            Assert.AreEqual(0f, Value(hero, barrier), 0.01f, "and none by its end");
        });
    }
}
