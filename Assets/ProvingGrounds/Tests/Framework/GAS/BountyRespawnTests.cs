using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FN-14, Decision 77: an entity's Bounty and Respawn components. A Bounty gives its killer an effect from it as it
    /// dies, and the killer's allies near it where it shares; a Respawn revives an entity that stays as it dies a while
    /// after, at its home or where it died.
    /// </summary>
    public class BountyRespawnTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag red, blue, paid;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            red = Make.NewTag("Test.FN14.Bounty.Red");
            blue = Make.NewTag("Test.FN14.Bounty.Blue");
            paid = Make.NewTag("Test.FN14.Bounty.Paid");
        }

        // A mortal entity of a side, at a point along x, on either hub, which stays as it dies, holding the components given
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, float x, params AbstractEntityComponent[] components)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            // Mended whole as it revives, so it can die again
            var death = new EntityDeath
            {
                Policy = EDeathPolicy.Stay,
                OnRevive = new List<GameplayEffect> { Make.Effect("Mend", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = 1000f }) },
            };
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                identity.Death = death;
                identity.Components = new List<AbstractEntityComponent>(components);
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                mono.EntityData.Death = death;
                mono.EntityData.Components = new List<AbstractEntityComponent>(components);
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = new Vector3(x, 0f, 0f);
            return gas;
        }

        private bool Paid(IGameplayAbilitySystem gas) => gas.GetTagCache().GetWeight(paid) > 0;

        private async Cysharp.Threading.Tasks.UniTask Kill(IGameplayAbilitySystem killer, IGameplayAbilitySystem entity)
        {
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1000f });
            Assert.IsTrue(World.Apply(killer, entity, blow), "a mortal blow");
            await Frames.Until(() => entity.IsDead, 10, "it dies");
        }

        /// <summary>
        /// As it dies, its killer takes its Bounty's effect, and the killer's allies near it where it shares; not the
        /// killer's ally out of its share, nor one of its own side near it.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_bounty_pays_its_killer_and_the_killers_allies_near([Values] Hub hub) => Live(async () =>
        {
            var gold = Make.Effect("Gold", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, GrantedTags = new List<Tag> { paid } });
            var killer = At(hub, "Killer", red, -10f);
            var creep = At(hub, "Creep", blue, 0f, new BountyEntityComponent { Effect = gold, ShareRadius = 5f });
            var helper = At(hub, "Helper", red, 3f);
            var idler = At(hub, "Idler", red, 20f);
            var friend = At(hub, "Friend", blue, 2f);
            await Frames.Step();

            await Kill(killer, creep);
            await Frames.Step(2);
            Assert.IsTrue(Paid(killer), "its killer takes its bounty");
            Assert.IsTrue(Paid(helper), "and the killer's ally near it, its share");
            Assert.IsFalse(Paid(idler), "not the killer's ally out of its share");
            Assert.IsFalse(Paid(friend), "nor one of its own side");
        });

        /// <summary>
        /// A Respawn revives an entity that stays as it dies once its delay passes, at its home, where it stood as it
        /// registered; or where it died.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_respawn_revives_its_entity_after_its_delay([Values] Hub hub, [Values] bool home) => Live(async () =>
        {
            var killer = At(hub, "Killer", red, -10f);
            var hero = At(hub, "Hero", blue, 0f, new RespawnEntityComponent { Delay = 0.3f, At = home ? ERespawnPlace.Home : ERespawnPlace.WhereItDied });
            await Frames.Step();
            var spawn = hero.GetTargetingPacket().position;
            hero.GetTargetingPacket().position = new Vector3(5f, 0f, 2f);
            await Frames.Step();

            await Kill(killer, hero);
            await Frames.Step(2);
            Assert.IsTrue(hero.IsDead, "it stays dead a while");
            await Frames.Until(() => !hero.IsDead, 240, "and revives once its delay passes");
            var at = hero.GetTargetingPacket().position;
            if (home) Assert.That(Vector3.Distance(spawn, at), Is.LessThan(0.01f), $"at its home, {spawn}, not {at}");
            else Assert.That(Vector3.Distance(new Vector3(5f, 0f, 2f), at), Is.LessThan(0.01f), $"where it died, not {at}");
        });

        /// <summary>
        /// A revive before its time, as a buyback, ends the wait: dead again, it waits its own delay, the first wait, which
        /// would have ended by then, gone.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_revive_before_its_time_ends_the_respawns_wait([Values] Hub hub) => Live(async () =>
        {
            var killer = At(hub, "Killer", red, -10f);
            var hero = At(hub, "Hero", blue, 0f, new RespawnEntityComponent { Delay = 1f });
            await Frames.Step();

            await Kill(killer, hero);
            await Frames.Step();
            Assert.IsTrue(hero.Revive(), "bought back at once");
            await Frames.Seconds(0.5f);
            Assert.IsFalse(hero.IsDead, "it stands");

            await Kill(killer, hero);
            await Frames.Seconds(0.8f);
            Assert.IsTrue(hero.IsDead, "dead again, past when its first wait would have ended, it waits its own delay");
            await Frames.Until(() => !hero.IsDead, 240, "then it revives");
        });
    }
}
