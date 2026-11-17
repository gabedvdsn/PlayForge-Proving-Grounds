using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Pooled Prefab cues (EP-51): a Prefab cue that pools takes its objects from a pool and gives them back as its cue
    /// ends, in place of making and destroying one each time, as Unreal recycles its cue actors; its pool makes some
    /// ahead; an object's own reset runs as it's taken and given back; and a cue raised at a point places its prefab
    /// there.
    /// </summary>
    public class PooledCueTests : ProvingGroundsTest
    {
        private readonly List<Object> made = new();

        // A pooled object's reset, counting each time its pool hands it out and takes it back
        private sealed class Probe : MonoBehaviour, IPooledCue
        {
            public static int Takes, Returns;
            public static Probe Last;

            public void Taken()
            {
                Takes++;
                Last = this;
            }

            public void Returned() => Returns++;
        }

        [SetUp]
        public void Counts()
        {
            Probe.Takes = 0;
            Probe.Returns = 0;
            Probe.Last = null;
        }

        [TearDown]
        public void PutBack()
        {
            ForgeCuePool.Clear();
            // What a cue kept out, as a burst with no Lifetime, goes too
            foreach (var probe in Object.FindObjectsByType<Probe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.Destroy(probe.gameObject);
            foreach (var each in made) if (each) Object.Destroy(each);
            made.Clear();
        }

        // An inactive prefab, its probe on it
        private GameObject Prefab(string name)
        {
            var prefab = new GameObject(name);
            prefab.SetActive(false);
            prefab.AddComponent<Probe>();
            made.Add(prefab);
            return prefab;
        }

        /// <summary>
        /// A pooled lasting cue gives its object back as its effect goes, and takes the same one again as it lands again,
        /// the object's own reset running each time (EP-51).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_51_A_pooled_cue_gives_its_object_back_and_takes_it_again([Values] Hub hub) => Live(async () =>
        {
            var glow = Prefab("EP-51 Glow");
            var gas = World.Entity(hub);
            var aura = Make.Effect("Aura", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            aura.Cues.Add(new PrefabCue { Prefab = glow, WhileActive = true, Pooled = true });

            Assert.IsTrue(World.Apply(gas, gas, aura), "an aura lands");
            await Frames.Step();
            Assert.AreEqual(1, Probe.Takes, "its glow is taken from the pool");
            Assert.AreEqual(0, ForgeCuePool.Idle(glow), "and none waits there");

            gas.RemoveGameplayEffect(aura);
            await Frames.Step(2);
            Assert.AreEqual(1, Probe.Returns, "the aura goes, and its glow goes back");
            Assert.AreEqual(1, ForgeCuePool.Idle(glow), "to wait in the pool");

            Assert.IsTrue(World.Apply(gas, gas, aura), "the aura lands again");
            await Frames.Step();
            Assert.AreEqual(2, Probe.Takes, "and takes it again");
            Assert.AreEqual(0, ForgeCuePool.Idle(glow), "the one that waited");
            gas.RemoveGameplayEffect(aura);
            await Frames.Step(2);
        });

        /// <summary>A pooled cue's pool makes its Preallocate objects ahead as it first plays (EP-51).</summary>
        [UnityTest]
        public IEnumerator EP_51_A_pooled_cue_makes_its_objects_ahead([Values] Hub hub) => Live(async () =>
        {
            var spark = Prefab("EP-51 Spark");
            var gas = World.Entity(hub);
            var cue = new PrefabCue { Prefab = spark, Pooled = true, Preallocate = 3, Lifetime = 0f };
            await Frames.Step();

            ForgeCues.Burst(cue, new CueContext(gas, ECueMoment.Raised));
            Assert.AreEqual(1, Probe.Takes, "one is taken as it first plays");
            Assert.AreEqual(2, ForgeCuePool.Idle(spark), "and two more wait, of the three it made ahead");
        });

        /// <summary>A pooled burst gives its object back after its Lifetime (EP-51).</summary>
        [UnityTest]
        public IEnumerator EP_51_A_pooled_burst_gives_its_object_back_after_its_lifetime() => Live(async () =>
        {
            var spark = Prefab("EP-51 Spark");
            var gas = World.Entity(Hub.Runtime);
            var cue = new PrefabCue { Prefab = spark, Pooled = true, Lifetime = 0.1f };
            await Frames.Step();

            ForgeCues.Burst(cue, new CueContext(gas, ECueMoment.Raised));
            Assert.AreEqual(0, ForgeCuePool.Idle(spark), "its spark is out");
            await Frames.Seconds(0.3f);
            Assert.AreEqual(1, Probe.Returns, "and after its lifetime, it goes back");
            Assert.AreEqual(1, ForgeCuePool.Idle(spark), "to wait in the pool");
        });

        /// <summary>A cue raised at a point places its prefab there, on an entity with no place of its own (EP-51).</summary>
        [UnityTest]
        public IEnumerator EP_51_A_prefab_cue_raised_at_a_point_is_placed_there() => Live(async () =>
        {
            var spark = Prefab("EP-51 Spark");
            var gas = World.Entity(Hub.Runtime);
            var cue = new PrefabCue { Prefab = spark, Pooled = true, Lifetime = 0f };
            await Frames.Step();

            ForgeCues.Burst(cue, new CueContext(gas, ECueMoment.Raised, point: new Vector3(4f, 0f, 2f)));
            Assert.IsNotNull(Probe.Last, "a spark is out");
            Assert.AreEqual(new Vector3(4f, 0f, 2f), Probe.Last.transform.position, "at the point");
        });
    }
}
