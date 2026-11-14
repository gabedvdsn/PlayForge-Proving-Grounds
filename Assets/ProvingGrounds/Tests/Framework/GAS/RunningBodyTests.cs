using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FN-18, Decision 78: a running entity is given a body at runtime, one that isn't its identity's Body, through
    /// <c>UseBody</c>: a MonoBehaviour process a runtime entity follows, which ends with the entity as it dies, as its
    /// identity's Body does; a revive spawns the identity's Body, where it has one. Null lets a body go, ending it.
    /// </summary>
    public class RunningBodyTests : ProvingGroundsTest
    {
        private ForgeAttribute health;

        // An entity on either hub that dies at 0 Health and stays
        private IGameplayAbilitySystem Mortal(Hub hub, string name)
        {
            health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            var death = new EntityDeath { Policy = EDeathPolicy.Stay };
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Death = death;
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Death = death;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private async Cysharp.Threading.Tasks.UniTask Kill(IGameplayAbilitySystem entity)
        {
            var killer = World.Entity(Hub.Runtime, Make.Set(Make.Element(health, 1f, 1f)), "Killer");
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1000f });
            Assert.IsTrue(World.Apply(killer, entity, blow), "a mortal blow");
            await Frames.Until(() => entity.IsDead, 10, "it dies");
            await Frames.Step(3);
        }

        /// <summary>A running runtime entity takes a body it's given, which is its body now, and is where its body is.</summary>
        [UnityTest]
        public IEnumerator FN_18_A_running_entity_takes_a_body_and_follows_it() => Live(async () =>
        {
            var wraith = Mortal(Hub.Runtime, "Wraith");
            var body = World.MonoProbe("Wraith Body");
            await Frames.Step();
            Assert.IsNull(wraith.GetBody(), "it starts with none, its identity naming none");

            Assert.IsTrue(wraith.UseBody(body), "it takes the body");
            Assert.AreSame(body, wraith.GetBody(), "which is its body now");
            body.transform.position = new Vector3(3f, 0f, 4f);
            await Frames.Step(2);
            Assert.That(Vector3.Distance(new Vector3(3f, 0f, 4f), wraith.GetTargetingPacket().position), Is.LessThan(0.01f),
                "and it's where its body is");
        });

        /// <summary>
        /// A body given at runtime ends with its entity as it dies, as its identity's Body does; with no Body of the identity's
        /// to spawn, it revives with none.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_18_A_given_body_ends_with_its_entity([Values] Hub hub) => Live(async () =>
        {
            var wraith = Mortal(hub, "Wraith");
            var body = World.MonoProbe("Wraith Body");
            await Frames.Step();
            Assert.IsTrue(wraith.UseBody(body), "it takes the body");

            await Kill(wraith);
            Assert.AreEqual(1, body.Recorder[ProbeHook.Terminate], "the body ends as its entity dies");
            Assert.IsNull(wraith.GetBody(), "and it has none");
            Assert.IsFalse(wraith.UseBody(World.MonoProbe("Another Body")), "a dead entity takes none");

            Assert.IsTrue(wraith.Revive(), "it revives");
            Assert.IsNull(wraith.GetBody(), "with no Body of its identity's to spawn, none");
        });

        /// <summary>Another body takes the place of the one it had, which ends; null lets a body go, ending it.</summary>
        [UnityTest]
        public IEnumerator FN_18_A_new_body_or_none_ends_the_one_it_had([Values] Hub hub) => Live(async () =>
        {
            var wraith = Mortal(hub, "Wraith");
            var first = World.MonoProbe("First Body");
            var second = World.MonoProbe("Second Body");
            await Frames.Step();

            Assert.IsTrue(wraith.UseBody(first));
            Assert.IsTrue(wraith.UseBody(second), "it takes another");
            await Frames.Step(2);
            Assert.AreEqual(1, first.Recorder[ProbeHook.Terminate], "and the first ends");
            Assert.AreSame(second, wraith.GetBody(), "the second its body");

            Assert.IsTrue(wraith.UseBody(null), "it lets go");
            await Frames.Step(2);
            Assert.AreEqual(1, second.Recorder[ProbeHook.Terminate], "and that body ends");
            Assert.IsNull(wraith.GetBody(), "leaving it none");
        });
    }
}
