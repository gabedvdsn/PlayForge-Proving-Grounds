using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A Process cue (EP-63): it runs a MonoBehaviour process where its placement says, on a packet loaded for the cue, a
    /// burst's for its own course and a lasting cue's until the cue ends. Pooled, it takes its objects from the cue pool
    /// through an instantiator its registration gives, which ProcessControl now takes for one registration; and a
    /// process's object whose process ended is no longer initialized, so a pool's can run again (Decision 91).
    /// </summary>
    public class ProcessCueTests : ProvingGroundsTest
    {
        [TearDown]
        public void Clean() => ForgeCuePool.Clear();

        // An instantiator that counts what it makes and takes back
        private sealed class Counting : AbstractMonoProcessInstantiator
        {
            public int Made, Cleaned;

            protected override AbstractMonoProcess PrepareNew(AbstractMonoProcess process, ProcessDataPacket data)
            {
                Made++;
                return Object.Instantiate(process);
            }

            protected override AbstractMonoProcess PrepareExisting(AbstractMonoProcess process, ProcessDataPacket data) => process;

            public override void CleanProcess(AbstractMonoProcess process)
            {
                Cleaned++;
                Object.Destroy(process.gameObject);
            }
        }

        // The probes a cue runs, live in the scene, by their template's name
        private static List<ProbeMonoProcess> Running(string name) =>
            Object.FindObjectsByType<ProbeMonoProcess>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(probe => probe.name.StartsWith(name) && probe.Data != null && probe.Data.ContainsKey(Tags.CUE)).ToList();

        private (IGameplayAbilitySystem Target, GameplayEffect Hit) Struck(Hub hub, AbstractCueHandler cue, Vector3 at)
        {
            var health = Make.Attribute("Health");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), name: "Target");
            if (target is Component placed) placed.transform.position = at;
            var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -5f });
            hit.Components.Add(new CuesEffectComponent { OnApply = { cue } });
            return (target, hit);
        }

        /// <summary>
        /// A Process cue in a burst list runs its process where its placement says, on a packet loaded for the cue with the
        /// cue's own Data, and the process runs its own course (EP-63).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_63_A_Process_cue_runs_its_process_with_the_cues_data([Values] Hub hub) => Live(async () =>
        {
            var word = Make.NewTag("Test.Data.Word");
            var spark = World.MonoProbeTemplate("EP-63 Spark", runFrames: 3);
            var cue = new ProcessCue { Process = spark };
            cue.Data.Add(new DataWrapper { Key = word, Value = new LocalString { Value = "spark" } });
            var (target, hit) = Struck(hub, cue, new Vector3(2f, 0f, 5f));

            Assert.IsTrue(World.Apply(target, target, hit), "the hit lands");
            await Frames.Step();

            var ran = Running("EP-63 Spark");
            Assert.That(ran, Has.Count.EqualTo(1), "its process runs");
            var process = ran[0];
            Assert.AreSame(hit, process.Data.Get<GameplayEffect>(Tags.ASSET, EDataTarget.Primary), "on the cue's packet");
            Assert.AreEqual("spark", process.Data.Get<string>(word, EDataTarget.Primary), "with the cue's own Data");
            if (hub == Hub.Mono)
                Assert.AreEqual(new Vector3(2f, 0f, 5f), process.transform.position, "where its placement says");
            await Frames.Until(() => process == null, 12, "and it runs its own course, as a burst's does");
        });

        /// <summary>
        /// A Process cue in a list of lasting cues runs its process while its cue lasts, following the one it's on, and
        /// ends it as the cue ends (EP-63).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_63_A_lasting_Process_cue_runs_until_its_cue_ends([Values] Hub hub) => Live(async () =>
        {
            var hum = World.MonoProbeTemplate("EP-63 Hum");
            var target = World.Entity(hub, name: "Target");
            var shield = Make.Effect("Shield", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            shield.Components.Add(new CuesEffectComponent { WhileActive = { new ProcessCue { Process = hum } } });

            Assert.IsTrue(World.Apply(target, target, shield), "the shield goes up");
            await Frames.Step(2);

            var ran = Running("EP-63 Hum");
            Assert.That(ran, Has.Count.EqualTo(1), "its process runs while the shield lasts");
            var process = ran[0];
            if (hub == Hub.Mono)
                Assert.AreSame(((Component)target).transform, process.transform.parent, "following the one it's on");

            target.RemoveGameplayEffect(shield);
            await Frames.Until(() => process == null, 12, "and its process ends as its cue does");
        });

        /// <summary>
        /// A pooled Process cue takes its objects from the cue pool, which makes its Preallocate ahead, and gives each back
        /// as its process ends, no longer initialized; the next hit runs the same object again (EP-63).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_63_A_pooled_Process_cue_takes_its_objects_from_the_cue_pool([Values] Hub hub) => Live(async () =>
        {
            var spark = World.MonoProbeTemplate("EP-63 Pooled", runFrames: 4);
            var cue = new ProcessCue { Process = spark, Pooled = true, Preallocate = 2 };
            var (target, hit) = Struck(hub, cue, Vector3.zero);

            Assert.IsTrue(World.Apply(target, target, hit), "the hit lands");
            await Frames.Step();
            Assert.AreEqual(1, ForgeCuePool.Idle(spark.gameObject), "its pool made two ahead, and handed one out");
            var first = Running("EP-63 Pooled").Single();
            await Frames.Until(() => !first.gameObject.activeSelf, 12, "its process runs its course");
            Assert.IsTrue(first != null, "and its object goes back to the pool, not destroyed");
            Assert.AreEqual(2, ForgeCuePool.Idle(spark.gameObject), "where it waits again");
            Assert.IsFalse(first.IsInitialized, "no longer initialized, so it can run again");

            Assert.IsTrue(World.Apply(target, target, hit), "a second hit lands");
            await Frames.Step();
            var second = Running("EP-63 Pooled").Single();
            Assert.AreSame(first, second, "the object given back last runs it");
            Assert.IsTrue(second.IsInitialized, "initialized afresh");
        });

        /// <summary>
        /// A registration takes an instantiator of its own, which makes its process's object and takes it back as the
        /// process ends, ahead of the object's own instantiator (EP-63).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_63_A_registration_takes_an_instantiator_of_its_own() => Live(async () =>
        {
            var own = new Counting();
            var others = new Counting();
            var bolt = World.MonoProbeTemplate("EP-63 Bolt", runFrames: 2);
            bolt.Instantiator = others;

            var registration = ProcessControl.Register(bolt).With(ProcessDataPacket.Default()).InstantiatedBy(own).Run();
            Assert.IsTrue(registration, "the bolt registers");
            World.Own(registration.Relay);
            Assert.AreEqual(1, own.Made, "the registration's instantiator makes its object");
            Assert.AreEqual(0, others.Made, "ahead of the object's own");
            await Frames.Until(() => own.Cleaned == 1, 12, "and takes it back as its process ends");
            Assert.AreEqual(0, others.Cleaned, "which the object's own never sees");
        });
    }
}
