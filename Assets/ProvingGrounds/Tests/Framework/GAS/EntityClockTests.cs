using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A clock for each entity (FEAT-7, Decision 30): the game's time, scaled by the entity's time scale, which its effects,
    /// cooldowns and abilities' waits run by. And TS-6: what ran on unscaled time by default runs on scaled time.
    /// </summary>
    public class EntityClockTests : ProvingGroundsTest
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

        private GameplayEffect Timed() =>
            Make.Effect("Timed", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = null });

        private static float Left(IGameplayAbilitySystem gas, GameplayEffect effect)
        {
            Assert.IsTrue(gas.TryGetEffectContainer(effect, out var container), $"{gas.GetName()} still has {effect.GetName()}");
            return container.DurationRemaining;
        }

        /// <summary>
        /// Every entity ran on Unity's one clock, so nothing could slow one entity and not the rest (FEAT-7). An entity's
        /// effects run by its own clock, its time scale times the game's.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_7_An_entitys_time_scale_slows_its_effects([Values] Hub hub) => Live(async () =>
        {
            var normal = World.Entity(hub, name: "Normal");
            var slowed = World.Entity(hub, name: "Slowed");
            slowed.GetClock().TimeScale = 0.5f;
            var timed = Timed();
            var previous = ForgeTime.Use(new SteppingClock { Step = 0.5f });
            try
            {
                Assert.IsTrue(World.Apply(normal, normal, timed) && World.Apply(slowed, slowed, timed), "the effect lands on both");
                await Frames.Step(4);
                float normalLeft = Left(normal, timed), slowedLeft = Left(slowed, timed);
                Assert.That(normalLeft, Is.EqualTo(8f).Within(0.6f), "four frames of half a second each");
                Assert.That(10f - slowedLeft, Is.EqualTo((10f - normalLeft) / 2f).Within(0.01f), "and the slowed entity's effect took half as long");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>An entity's ability waits by its clock, so a slowed caster's wait lasts longer (FEAT-7).</summary>
        [UnityTest]
        public IEnumerator FEAT_7_An_abilitys_wait_runs_on_its_casters_clock([Values] Hub hub) => Live(async () =>
        {
            var wait = Make.Ability("Wait", Make.Stage(new DelayTask { DelayMilliseconds = 1000 }));
            var normal = World.Entity(hub, name: "Normal").GetAbilitySystem();
            var slowedGas = World.Entity(hub, name: "Slowed");
            slowedGas.GetClock().TimeScale = 0.5f;
            var slowed = slowedGas.GetAbilitySystem();
            Assert.IsTrue(normal.GiveAbility(wait, 1, out int normalIndex), "the normal entity has the ability");
            Assert.IsTrue(slowed.GiveAbility(wait, 1, out int slowedIndex), "and the slowed one");
            await Frames.Step();

            var previous = ForgeTime.Use(new SteppingClock { Step = 0.25f });
            try
            {
                Assert.IsTrue(normal.TryActivateAbility(normal.CreateActivationRequest(normalIndex)), "the normal entity waits a second");
                Assert.IsTrue(slowed.TryActivateAbility(slowed.CreateActivationRequest(slowedIndex)), "and the slowed one");
                await Frames.Step(6);
                bool normalRuns = normal.GetAbilityContainers().Single(each => each.Index == normalIndex).IsActive;
                bool slowedRuns = slowed.GetAbilityContainers().Single(each => each.Index == slowedIndex).IsActive;
                Assert.IsFalse(normalRuns, "a second of quarter-second frames has passed for the normal entity");
                Assert.IsTrue(slowedRuns, "and half that for the slowed one, whose wait goes on");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>A time scale of 0 holds an entity's time still, and no one else's: it isn't a pause (FEAT-7).</summary>
        [UnityTest]
        public IEnumerator FEAT_7_A_time_scale_of_0_holds_one_entity_still([Values] Hub hub) => Live(async () =>
        {
            var frozen = World.Entity(hub, name: "Frozen");
            var other = World.Entity(hub, name: "Other");
            frozen.GetClock().TimeScale = 0f;
            var timed = Timed();
            Assert.IsTrue(World.Apply(frozen, frozen, timed) && World.Apply(other, other, timed), "the effect lands on both");
            float frozenAt = Left(frozen, timed);
            await Frames.Step(4);
            Assert.AreEqual(frozenAt, Left(frozen, timed), "the frozen entity's effect holds still");
            Assert.That(Left(other, timed), Is.LessThan(10f), "while the other's runs");
            Assert.AreEqual(0f, frozen.GetClock().Time, "and the frozen entity's time stays where it started");
        });

        /// <summary>RunProcessTask ran its processes on unscaled time by default, apart from the game's time scale (TS-6).</summary>
        [Test]
        public void TS_6_RunProcessTask_runs_on_scaled_time_by_default()
        {
            Assert.IsFalse(new RunProcessTask().UseUnscaledDeltaTime, "a new RunProcessTask runs on scaled time");
        }

        /// <summary>A relay's remaining runtime read unscaled time by default (TS-6).</summary>
        [Test]
        public void TS_6_A_relays_remaining_runtime_reads_scaled_time_by_default()
        {
            var overloads = typeof(ProcessRelay).GetMethods().Where(method => method.Name == nameof(ProcessRelay.RemainingRuntime)).ToList();
            Assert.That(overloads, Is.Not.Empty);
            foreach (var method in overloads)
            {
                var unscaled = method.GetParameters().Single(each => each.Name == "unscaled");
                Assert.AreEqual(false, unscaled.DefaultValue, $"{method} reads scaled time unless asked");
            }
        }
    }
}
