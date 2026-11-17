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
    /// Where a cue places what it plays (EP-54): on the one it's on, its source, an ability's target or each of its
    /// targets, or at its point; at a socket, moved by an offset in their space and scattered; following them or not; and
    /// facing their way, the cue's normal, or from or toward its source. A cue placed only at its target, and a sound only
    /// at its target's place, were all a cue could do.
    /// </summary>
    public class CuePlacementTests : ProvingGroundsTest
    {
        private readonly List<Object> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var clone in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (clone != null && clone.name.StartsWith("EP-54 ")) Object.Destroy(clone.gameObject);
            foreach (var each in made)
                if (each) Object.Destroy(each);
            made.Clear();
        }

        // A prefab for a cue to place, inactive so its copies do nothing
        private GameObject Prefab(string name)
        {
            var prefab = new GameObject(name);
            prefab.SetActive(false);
            made.Add(prefab);
            return prefab;
        }

        // The copies of a prefab a cue placed
        private static List<Transform> Clones(string name) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(each => each.name == name + "(Clone)").ToList();

        private static Transform Put(IGameplayAbilitySystem gas, Vector3 position)
        {
            var transform = ((Component)gas).transform;
            transform.position = position;
            return transform;
        }

        /// <summary>A cue placed on its source is at the source's socket, moved by its offset in the socket's space (EP-54).</summary>
        [UnityTest]
        public IEnumerator EP_54_A_cue_is_placed_at_its_sources_socket_and_offset() => Live(async () =>
        {
            var target = World.Entity(Hub.Mono);
            var caster = World.Entity(Hub.Mono);
            Put(target, Vector3.zero);
            var body = Put(caster, new Vector3(10f, 0f, 0f));
            body.rotation = Quaternion.Euler(0f, 90f, 0f);
            var hand = new GameObject("Hand").transform;
            hand.SetParent(body, false);
            hand.localPosition = new Vector3(0f, 1f, 0f);
            var spark = new PrefabCue
            {
                Prefab = Prefab("EP-54 Spark"), Lifetime = 0f,
                Placement = new CuePlacement { At = ECueAt.Source, Socket = "Hand", Offset = new Vector3(0f, 0f, 1f) },
            };

            ForgeCues.Burst(spark, new CueContext(target, ECueMoment.Raised, source: caster));
            await Frames.Step();

            var placed = Clones("EP-54 Spark");
            Assert.AreEqual(1, placed.Count, "one spark");
            var expected = hand.position + hand.rotation * new Vector3(0f, 0f, 1f);
            Assert.That(Vector3.Distance(placed[0].position, expected), Is.LessThan(0.001f),
                $"at the caster's hand, a step ahead of it: {placed[0].position} for {expected}");
            Assert.IsNull(placed[0].parent, "and a burst's stays where it was placed");
        });

        /// <summary>A scattered cue lands within its scatter of where it's placed, and not always at one spot (EP-54).</summary>
        [UnityTest]
        public IEnumerator EP_54_A_scattered_cue_lands_within_its_scatter() => Live(async () =>
        {
            var target = World.Entity(Hub.Mono);
            Put(target, new Vector3(0f, 0f, 5f));
            var dust = new PrefabCue
            {
                Prefab = Prefab("EP-54 Dust"), Lifetime = 0f, Placement = new CuePlacement { Scatter = new Vector3(1f, 0f, 1f) },
            };

            for (int i = 0; i < 20; i++) ForgeCues.Burst(dust, new CueContext(target, ECueMoment.Raised));
            await Frames.Step();

            var positions = Clones("EP-54 Dust").Select(each => each.position).ToList();
            Assert.AreEqual(20, positions.Count, "a cloud of dust");
            Assert.That(positions.Where(p => Mathf.Abs(p.x) > 1.0001f || Mathf.Abs(p.y) > 0.0001f || Mathf.Abs(p.z - 5f) > 1.0001f),
                Is.Empty, "each within its scatter of the target");
            Assert.That(positions.Distinct().Count(), Is.GreaterThan(1), "and scattered");
        });

        /// <summary>
        /// What a cue places follows the one it's on as its placement says: a lasting cue's by default and a burst's not, a
        /// burst's too when Always, and neither when Never (EP-54).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_54_What_a_cue_places_follows_as_its_placement_says() => Live(async () =>
        {
            var target = World.Entity(Hub.Mono);
            var body = Put(target, Vector3.zero);

            Transform Parent(string name, ECueAttach attach, bool lasting)
            {
                var glow = new PrefabCue { Prefab = Prefab(name), Lifetime = 0f, Placement = new CuePlacement { Attach = attach } };
                if (lasting) ForgeCues.Begin(glow, new CueContext(target, ECueMoment.Raised, key: name, lasting: true));
                else ForgeCues.Burst(glow, new CueContext(target, ECueMoment.Raised));
                var placed = Clones(name);
                Assert.AreEqual(1, placed.Count, $"one {name}");
                return placed[0].parent;
            }

            Assert.IsNull(Parent("EP-54 Burst", ECueAttach.WhileLasting, false), "a burst's stays, by default");
            Assert.AreSame(body, Parent("EP-54 Lasting", ECueAttach.WhileLasting, true), "a lasting cue's follows, by default");
            Assert.AreSame(body, Parent("EP-54 Always", ECueAttach.Always, false), "a burst's follows when Always");
            Assert.IsNull(Parent("EP-54 Never", ECueAttach.Never, true), "and a lasting cue's stays when Never");
            await Frames.Step();
        });

        /// <summary>A cue placed on each target places one at each of an ability's targets (EP-54).</summary>
        [UnityTest]
        public IEnumerator EP_54_A_cue_on_each_target_places_one_at_each() => Live(async () =>
        {
            var caster = World.Entity(Hub.Mono);
            var first = World.Entity(Hub.Mono);
            var second = World.Entity(Hub.Mono);
            Put(caster, Vector3.zero);
            Put(first, new Vector3(3f, 0f, 0f));
            Put(second, new Vector3(-3f, 0f, 0f));
            var data = ProcessDataPacket.Default();
            data.AddPayload(Tags.TARGET_REAL, (ITarget)first);
            data.AddPayload(Tags.TARGET_REAL, (ITarget)second);
            var hit = new PrefabCue { Prefab = Prefab("EP-54 Hit"), Lifetime = 0f, Placement = new CuePlacement { At = ECueAt.EachTarget } };

            ForgeCues.Burst(hit, new CueContext(caster, ECueMoment.Stage, source: caster, data: data));
            await Frames.Step();

            var placed = Clones("EP-54 Hit").Select(each => each.position.x).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(new[] { -3f, 3f }, placed, "one at each target");
        });

        /// <summary>A cue facing away from its source turns from the source toward where it's placed (EP-54).</summary>
        [UnityTest]
        public IEnumerator EP_54_A_cue_faces_away_from_its_source() => Live(async () =>
        {
            var target = World.Entity(Hub.Mono);
            var caster = World.Entity(Hub.Mono);
            Put(target, Vector3.zero);
            Put(caster, new Vector3(10f, 0f, 0f));
            var spray = new PrefabCue
            {
                Prefab = Prefab("EP-54 Spray"), Lifetime = 0f, Placement = new CuePlacement { Facing = ECueFacing.AwayFromSource },
            };

            ForgeCues.Burst(spray, new CueContext(target, ECueMoment.Raised, source: caster));
            await Frames.Step();

            var placed = Clones("EP-54 Spray");
            Assert.AreEqual(1, placed.Count, "one spray");
            Assert.That(Vector3.Dot(placed[0].forward, Vector3.left), Is.GreaterThan(0.999f), $"facing away from its source: {placed[0].forward}");
        });

        /// <summary>An attached sound cue plays from a source of its own, under the one it's on, so it follows them (EP-54).</summary>
        [UnityTest]
        public IEnumerator EP_54_An_attached_sound_follows_the_one_its_on() => Live(async () =>
        {
            var target = World.Entity(Hub.Mono);
            var body = Put(target, Vector3.zero);
            var clip = AudioClip.Create("EP-54 Hum", 4410, 1, 44100, false);
            made.Add(clip);
            var hum = new SoundCue { Clip = clip, Placement = new CuePlacement { Attach = ECueAttach.Always } };

            ForgeCues.Burst(hum, new CueContext(target, ECueMoment.Raised));
            await Frames.Step();

            var source = body.Find("EP-54 Hum (Cue)");
            Assert.IsNotNull(source, "a source of its own, under the one it's on");
            Assert.IsNotNull(source.GetComponent<AudioSource>(), "that plays it");
        });
    }
}
