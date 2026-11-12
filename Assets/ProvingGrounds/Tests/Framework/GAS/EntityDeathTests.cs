using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-4, the user's request of 2026-11-11: an entity that dies and comes back, as a MOBA hero, keeps its entity while
    /// its body goes. Its identity's death says Stay: it stays registered and dead, its buffs ended by tag and its
    /// cooldowns kept, its abilities refused, until it revives, by code or a respawn timer; and its body, a MonoBehaviour
    /// process, goes as it dies and comes back as it revives, where a runtime entity follows it (Decision 46).
    /// </summary>
    public class EntityDeathTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag buffs;

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

        [TearDown]
        public void PutBackTime()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            clockSet = false;
        }

        // A set of 100 health whose death watcher sees it reach 0
        private AttributeSet Mortal()
        {
            health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            return set;
        }

        // An entity on either hub whose identity's death is as given
        private IGameplayAbilitySystem Entity(Hub hub, EntityDeath death, string name = "Hero")
        {
            var set = Mortal();
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Death = death;
                var gas = new RuntimeGameplayAbilitySystem(identity);
                World.Register(gas);
                World.Invariants.Observe(gas);
                return gas;
            }

            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Death = death;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private GameplayEffect Blow(float amount) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });

        // Its health back to 100
        private GameplayEffect Restore() => Make.Effect("Restore", new EffectConfig
        {
            Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Override, Magnitude = 100f,
        });

        private static float Health(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static bool Has(IGameplayAbilitySystem gas, GameplayEffect effect) => gas.TryGetEffectContainer(effect, out _);

        private async Cysharp.Threading.Tasks.UniTask Kill(IGameplayAbilitySystem entity)
        {
            var killer = World.Entity(Hub.Runtime, Make.Set(Make.Element(health, 1f, 1f)), "Killer");
            Assert.IsTrue(World.Apply(killer, entity, Blow(1000f)), "a mortal blow");
            await Frames.Until(() => entity.IsDead, 10, "it dies");
            await Frames.Step(2);
        }

        /// <summary>
        /// A staying entity dies and stays: registered, dead, its buffs ended by their tag, its cooldown going on, its
        /// abilities refused.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_4_A_staying_entity_dies_and_stays([Values] Hub hub) => Live(async () =>
        {
            buffs = Make.NewTag("Test.TW4.Buff");
            var hero = Entity(hub, new EntityDeath { Policy = EDeathPolicy.Stay, EndsEffectsTagged = new List<Tag> { buffs } });
            var buff = Make.Effect("Buff", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { buffs } });
            var cooldown = Make.Effect("Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 60f });
            var ability = Make.Ability("Strike", Make.Stage());
            await Frames.Step();
            Assert.IsTrue(World.Apply(hero, hero, buff) && World.Apply(hero, hero, cooldown), "a buff and a cooldown");
            Assert.That(hero.GetAbilitySystem().GiveAbility(ability, 1, out int index), "and an ability");

            await Kill(hero);

            Assert.IsTrue(hero.IsDead, "dead");
            Assert.That(ForgeEntities.Live, NUnit.Framework.Has.Member(hero), "and still here");
            Assert.IsFalse(Has(hero, buff), "its buff ended, by its tag");
            Assert.IsTrue(Has(hero, cooldown), "its cooldown going on");
            Assert.IsFalse(hero.GetAbilitySystem().TryActivateAbility(hero.GetAbilitySystem().CreateActivationRequest(index)),
                "its abilities refused");
        });

        /// <summary>Revived, it lives again: its revive effects taken, its abilities its own, and it can die again.</summary>
        [UnityTest]
        public IEnumerator TW_4_A_revival_brings_it_back([Values] Hub hub) => Live(async () =>
        {
            var death = new EntityDeath { Policy = EDeathPolicy.Stay };
            var hero = Entity(hub, death);
            death.OnRevive.Add(Restore());
            var ability = Make.Ability("Strike", Make.Stage());
            await Frames.Step();
            Assert.That(hero.GetAbilitySystem().GiveAbility(ability, 1, out int index), "an ability");
            await Kill(hero);

            Assert.IsTrue(hero.Revive(), "revived");
            await Frames.Step(2);
            Assert.IsFalse(hero.IsDead, "alive");
            Assert.AreEqual(100f, Health(hero, health), 0.01f, "its health back, as its revive effect says");
            Assert.IsTrue(hero.GetAbilitySystem().TryActivateAbility(hero.GetAbilitySystem().CreateActivationRequest(index)),
                "its abilities its own again");

            await Kill(hero);
            Assert.IsTrue(hero.IsDead, "and it dies again, its death watcher ready for it");
        });

        /// <summary>A respawn timer, the effect it takes as it dies, revives it as it ends.</summary>
        [UnityTest]
        public IEnumerator TW_4_A_respawn_timer_revives_it([Values] Hub hub) => Live(async () =>
        {
            var previous = ForgeTime.Use(new SteppingClock { Step = 0.5f });
            previousClock = previous;
            clockSet = true;
            var respawn = Make.Effect("Respawn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 3f,
                Workers = new List<AbstractEffectWorker> { new ReviveOnRemovalEffectWorker() },
            });
            var death = new EntityDeath { Policy = EDeathPolicy.Stay, OnDeath = new List<GameplayEffect> { respawn } };
            var hero = Entity(hub, death);
            death.OnRevive.Add(Restore());
            await Frames.Step();
            await Kill(hero);
            Assert.IsTrue(Has(hero, respawn), "it waits out its respawn timer");

            await Frames.Until(() => !hero.IsDead, 12, "and revives as the timer ends");
            await Frames.Step();
            Assert.AreEqual(100f, Health(hero, health), 0.01f, "its health back");
        });

        /// <summary>
        /// A runtime entity's body, a MonoBehaviour process, comes with it, and the entity is where its body is; it goes as
        /// the entity dies, and comes back where the entity revives.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_4_A_runtime_entitys_body_goes_and_comes_back() => Live(async () =>
        {
            var template = World.MonoProbeTemplate("Body", EProcessLifecycle.RequiresControl);
            var death = new EntityDeath { Policy = EDeathPolicy.Stay, Body = template };
            var hero = Entity(Hub.Runtime, death);
            death.OnRevive.Add(Restore());
            await Frames.Step(2);

            var body = hero.GetBody();
            Assert.IsNotNull(body, "its body is spawned");
            body.transform.position = new Vector3(4f, 0f, 2f);
            await Frames.Step(2);
            Assert.AreEqual(new Vector3(4f, 0f, 2f), hero.GetTargetingPacket().position, "and the entity is where it is");

            await Kill(hero);
            await Frames.Step(2);
            Assert.IsNull(hero.GetBody(), "its body goes as it dies");
            Assert.IsFalse(body, "ended");

            Assert.IsTrue(hero.Revive(new Vector3(-3f, 0f, 1f)), "revived at a spawn point");
            await Frames.Step(2);
            Assert.IsNotNull(hero.GetBody(), "a body again");
            Assert.AreEqual(new Vector3(-3f, 0f, 1f), hero.GetBody().transform.position, "where it revived");
        });

        /// <summary>An entity whose death ends, as every one did, ends.</summary>
        [UnityTest]
        public IEnumerator TW_4_An_ending_entity_ends_as_it_did([Values] Hub hub) => Live(async () =>
        {
            var hero = Entity(hub, new EntityDeath());
            await Frames.Step();
            await Kill(hero);
            await Frames.Step(3);
            Assert.That(ForgeEntities.Live, NUnit.Framework.Has.No.Member(hero), "it ended");
            Assert.IsFalse(hero.Revive(), "and nothing revives it");
        });
    }
}
