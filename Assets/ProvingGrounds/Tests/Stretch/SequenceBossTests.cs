using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The review's sequence-driven boss, a stretch scenario: one long TaskSequence whose stages are the fight's phases,
    /// branching on the arena's state, with injections from the arena. The boss slams until it's worn below 200, then
    /// rains meteors while the arena's pillars stand, or stands exposed once they're down, then ends in a fury of slams;
    /// the arena's alarm calls the fury early. Its blows carry cues: a slam, and each meteor's tick, shake the camera
    /// through the game's shaker (FEAT-1).
    /// </summary>
    [Category("Stretch")]
    public class SequenceBossTests : ProvingGroundsTest
    {
        private const float SecondsPerFrame = 0.5f, BossHealth = 300f, Worn = 200f;

        private sealed class FightClock : IForgeClock
        {
            public float DeltaTime => SecondsPerFrame;
            public float UnscaledDeltaTime => SecondsPerFrame;
            public float FixedDeltaTime => SecondsPerFrame;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        private sealed class CountingShaker : ICameraShaker
        {
            public int Shakes;
            public void Shake(float strength, float duration) => Shakes++;
        }

        // The fight: the boss, the hero, the arena's pillars, and the sequence that runs the boss
        private sealed class Fight : IDisposable
        {
            public readonly IGameplayAbilitySystem Boss, Hero;
            public readonly Attribute Health;
            public readonly Tag Exposed;
            public readonly TaskSequence Sequence;
            public readonly List<string> Phases = new();
            public readonly CountingShaker Camera = new();
            public int Pillars = 2;
            public int FuryStage { get; }

            private readonly IForgeClock previousClock;
            private readonly ICameraShaker previousShaker;
            private readonly World world;
            private readonly GameplayEffect slam, rain, exposed, strike;

            public Fight(World world, ForgeAssets make, Hub hub)
            {
                this.world = world;
                previousClock = ForgeTime.Use(new FightClock());
                previousShaker = ForgeCues.Shaker;
                ForgeCues.Shaker = Camera;

                Health = make.Attribute("Health");
                Boss = world.Entity(hub, make.Set(make.Element(Health, BossHealth, BossHealth)), "Boss");
                Hero = world.Entity(hub, make.Set(make.Element(Health, 1000f, 1000f)), "Hero");
                Exposed = make.NewTag("Boss.Exposed");

                slam = make.Effect("Slam", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -20f });
                slam.Cues.Add(new CameraShakeCue { Strength = 1f, Duration = 0.2f });
                rain = make.Effect("Meteor Rain", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 2f, Target = Health, Magnitude = -5f, PeriodicTicks = true, Ticks = 4,
                });
                rain.Cues.Add(new CameraShakeCue { Strength = 0.5f, Duration = 0.1f, OnEachTick = true });
                exposed = make.Effect("Exposed", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { Exposed },
                });
                strike = make.Effect("Strike", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -30f });

                // The opening slams each second, until the boss is worn; the branch rains meteors while a pillar stands, or
                // exposes the boss; the fury is two slams
                FuryStage = 2;
                var fight = TaskSequenceBuilder.Create("Boss")
                    .Stage(stage => stage.WithName("Opening").WithRepeat(true)
                        .Task(_ => Phase("Opening"))
                        .Task(_ => Hit(Hero, slam))
                        .Delay(1f))
                    .InjectWhen(_ => Value(Boss) < Worn, BreakStageRepeatInjection.Instance, fireOnce: true)
                    .Branch(branch => branch
                        .If(_ => Pillars > 0, stage => stage.WithName("Meteor Rain")
                            .Task(_ => Phase("Meteor Rain"))
                            .Task(_ => Hit(Hero, rain))
                            .Delay(2.5f))
                        .Default(stage => stage.WithName("Exposed")
                            .Task(_ => Phase("Exposed"))
                            .Task(_ => Hit(Boss, exposed))
                            .Delay(1f)))
                    .Stage(stage => stage.WithName("Fury")
                        .Task(_ => Phase("Fury"))
                        .Task(_ => Hit(Hero, slam))
                        .Delay(0.5f)
                        .Task(_ => Hit(Hero, slam)));
                Sequence = fight.BuildSequence();
            }

            private void Phase(string name)
            {
                if (Phases.Count == 0 || Phases[Phases.Count - 1] != name) Phases.Add(name);
            }

            private void Hit(IGameplayAbilitySystem target, GameplayEffect effect) => world.Apply(Boss, target, effect);

            public ProcessRelay Begin() => Sequence.RegisterAndRun(SequenceDataPacket.SceneRoot());

            /// <summary>The hero strikes the boss.</summary>
            public void Strike() => world.Apply(Hero, Boss, strike);

            public float Value(IGameplayAbilitySystem gas) => gas.TryGetAttributeValue(Health, out var value) ? value.CurrentValue : float.NaN;

            public void Dispose()
            {
                ForgeTime.Use(previousClock);
                ForgeCues.Shaker = previousShaker;
            }
        }

        // The hero strikes the boss once a second until the fight's sequence ends
        private async Cysharp.Threading.Tasks.UniTask StrikeUntilOver(Fight fight, ProcessRelay relay, int strikes = 4)
        {
            for (int frame = 0; frame < 80 && World.Control.IsRegistered(relay.CacheIndex); frame++)
            {
                if (frame % 2 == 0 && strikes-- > 0) fight.Strike();
                await Frames.Step();
            }
            Assert.IsFalse(World.Control.IsRegistered(relay.CacheIndex), "the fight's sequence runs to its end");
        }

        /// <summary>
        /// The boss fights in phases: it slams until it's worn, rains meteors while the pillars stand, then ends in a fury.
        /// Each slam shakes the camera through the game's shaker, and the meteor rain as it comes and on each tick (FEAT-1).
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_The_boss_fights_in_phases([Values] Hub hub) => Live(async () =>
        {
            using var fight = new Fight(World, Make, hub);
            var relay = fight.Begin();
            Assert.IsNotNull(relay, "the fight begins");
            await StrikeUntilOver(fight, relay);

            CollectionAssert.AreEqual(new[] { "Opening", "Meteor Rain", "Fury" }, fight.Phases, "the fight's phases, in order");
            Assert.That(fight.Value(fight.Boss), Is.LessThan(Worn), "the boss was worn down");
            int slams = (int)Math.Round((1000f - fight.Value(fight.Hero) - 4 * 5f) / 20f);
            Assert.That(slams, Is.GreaterThanOrEqualTo(3), "it slammed in its opening and its fury");
            Assert.AreEqual(slams + 1 + 4, fight.Camera.Shakes, "each slam shook the camera, and the meteors as they came and on each of their four ticks");
        });

        /// <summary>With the pillars down, the branch exposes the boss in place of the meteor rain.</summary>
        [UnityTest]
        public IEnumerator Stretch_With_the_pillars_down_the_boss_is_exposed([Values] Hub hub) => Live(async () =>
        {
            using var fight = new Fight(World, Make, hub);
            fight.Pillars = 0;
            var relay = fight.Begin();
            await StrikeUntilOver(fight, relay);

            CollectionAssert.AreEqual(new[] { "Opening", "Exposed", "Fury" }, fight.Phases, "the branch takes the exposed way");
            Assert.That(fight.Boss.GetTagCache().GetWeight(fight.Exposed), Is.GreaterThan(0), "and the boss is exposed");
        });

        /// <summary>The arena's alarm calls the fury early: its injection jumps the fight to its last stage, mid-opening.</summary>
        [UnityTest]
        public IEnumerator Stretch_The_arena_calls_the_fury_early([Values] Hub hub) => Live(async () =>
        {
            using var fight = new Fight(World, Make, hub);
            var relay = fight.Begin();
            await Frames.Step(3);
            Assert.IsTrue(fight.Sequence.Inject(new JumpToStageInjection(fight.FuryStage)), "the alarm sounds");
            await StrikeUntilOver(fight, relay, strikes: 0);

            CollectionAssert.AreEqual(new[] { "Opening", "Fury" }, fight.Phases, "and the fury comes at once");
            Assert.AreEqual(BossHealth, fight.Value(fight.Boss), "with the boss unworn");
        });
    }
}
