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
    /// The cue raise and its sink (NET-2): each raise of a list of cues names what the list belongs to, and its world's
    /// sink is asked once, before any of them plays, whatever mutes it; its default plays everything. An effect's raise
    /// carries the hit its applier gave: where, which way and how hard.
    /// </summary>
    public class CueSinkTests : ProvingGroundsTest
    {
        private ForgeCueSink previousSink;

        [SetUp]
        public void Keep() => previousSink = ForgeCues.Sink;

        [TearDown]
        public void Restore()
        {
            ForgeCues.Sink = previousSink;
            ForgeCues.Enabled = true;
            ForgeCueRoutes.Library = null;
        }

        // A sink that writes down each raise it's asked of, and plays them or not
        private sealed class Recording : ForgeCueSink
        {
            public readonly List<(ECueEvent Event, CueSource Source, CueContext Cue)> Asked = new();
            public bool Plays = true;

            public override bool WillPlay(ECueEvent cueEvent, in CueRaise raise)
            {
                Asked.Add((cueEvent, raise.Source, raise.Cue));
                return Plays;
            }
        }

        // A cue that counts its bursts, and keeps the last one's place and magnitude
        private sealed class Counting : AbstractCueHandler
        {
            public int Bursts;
            public Vector3? Point;
            public Vector3? Normal;
            public float Magnitude;

            public override void Burst(CueContext cue)
            {
                Bursts++;
                Point = cue.Point;
                Normal = cue.Normal;
                Magnitude = cue.Magnitude;
            }
        }

        /// <summary>
        /// A sink is asked once for a raise, before any of its cues plays, and refused, none plays; it's asked even while
        /// cues are off, as a server's that sends them on (NET-2).
        /// </summary>
        [UnityTest]
        public IEnumerator NET_2_A_sink_is_asked_once_per_raise_before_any_cue_plays() => Live(async () =>
        {
            var sink = new Recording();
            ForgeCues.Sink = sink;
            var gas = World.Entity(Hub.Runtime);
            var first = new Counting();
            var second = new Counting();
            var both = new List<AbstractCueHandler> { first, second };

            ForgeCues.Burst(both, new CueContext(gas, ECueMoment.Raised));
            Assert.AreEqual(1, sink.Asked.Count, "asked once for the raise");
            Assert.AreEqual(ECueSource.Code, sink.Asked[0].Source.Kind, "raised by code");
            Assert.AreEqual((1, 1), (first.Bursts, second.Bursts), "and both played");

            sink.Plays = false;
            ForgeCues.Burst(both, new CueContext(gas, ECueMoment.Raised));
            Assert.AreEqual((1, 1), (first.Bursts, second.Bursts), "refused, neither plays");

            ForgeCues.Enabled = false;
            ForgeCues.Burst(both, new CueContext(gas, ECueMoment.Raised));
            Assert.AreEqual(3, sink.Asked.Count, "asked while cues are off too");
            await Frames.Step();
        });

        /// <summary>
        /// An effect's raise names the effect, and its cues play where its applier said it hit, facing as it said, as hard
        /// as it said (NET-2).
        /// </summary>
        [UnityTest]
        public IEnumerator NET_2_An_effects_raise_names_the_effect_and_carries_its_hit([Values] Hub hub) => Live(async () =>
        {
            var sink = new Recording();
            ForgeCues.Sink = sink;
            var gas = World.Entity(hub);
            var spark = Make.Effect("Spark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            var probe = new Counting();
            var cues = new CuesEffectComponent();
            cues.OnApply.Add(probe);
            spark.Components.Add(cues);

            var spec = gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(gas), spark)
                .WithHit(new Vector3(1f, 2f, 3f), Vector3.up, 4f);
            Assert.IsTrue(gas.ApplyGameplayEffect(spec), "a spark lands");
            await Frames.Step();

            var raise = sink.Asked.Single(each => each.Source.Kind == ECueSource.Effect);
            Assert.AreSame(spark, raise.Source.Asset, "its raise names the effect");
            Assert.AreEqual(new Vector3(1f, 2f, 3f), probe.Point, "its cue plays where its applier said it hit");
            Assert.AreEqual(Vector3.up, probe.Normal, "facing as it said");
            Assert.AreEqual(4f, probe.Magnitude, 0.001f, "as hard as it said");
        });

        /// <summary>A world's own sink is asked for its entities' cues, in place of the default (NET-2).</summary>
        [UnityTest]
        public IEnumerator NET_2_A_worlds_own_sink_is_asked_for_its_entities_cues([Values] Hub hub) => Live(async () =>
        {
            var everywhere = new Recording();
            ForgeCues.Sink = everywhere;
            var own = new Recording();
            var gas = World.Entity(hub);
            var world = ProcessWorld.Of(gas);
            Assume.That(world, Is.Not.Null, "the entity is in a world");

            ForgeCues.UseSink(world, own);
            try
            {
                ForgeCues.Burst(new Counting(), new CueContext(gas, ECueMoment.Raised));
                Assert.AreEqual(1, own.Asked.Count, "its world's own sink is asked");
                Assert.AreEqual(0, everywhere.Asked.Count, "and the default isn't");
            }
            finally
            {
                ForgeCues.UseSink(world, null);
            }
            ForgeCues.Burst(new Counting(), new CueContext(gas, ECueMoment.Raised));
            Assert.AreEqual(1, everywhere.Asked.Count, "taken back, the default is asked");
            await Frames.Step();
        });

        /// <summary>
        /// A cue tag raised from code asks its sink once, naming the tag, however far it routes: a Cue Tag in its route
        /// plays within the raise, unasked (NET-2).
        /// </summary>
        [UnityTest]
        public IEnumerator NET_2_A_tag_raised_from_code_asks_once_however_it_routes() => Live(async () =>
        {
            var sink = new Recording();
            ForgeCues.Sink = sink;
            var counting = new Counting();
            var library = ScriptableObject.CreateInstance<CueLibrary>();
            try
            {
                library.Routes.Add(new CueRoute
                {
                    Tag = Tag.Generate("Test.Cue.Net"),
                    Handlers = new List<AbstractCueHandler> { counting, new TagCue { Tag = Tag.Generate("Test.Cue.Net.Inner") } },
                });
                library.Routes.Add(new CueRoute { Tag = Tag.Generate("Test.Cue.Net.Inner"), Handlers = new List<AbstractCueHandler> { counting } });
                ForgeCueRoutes.Library = library;
                var gas = World.Entity(Hub.Runtime);

                ForgeCueRoutes.Burst(new CueContext(gas, ECueMoment.Raised, tag: Tag.Generate("Test.Cue.Net")));

                Assert.AreEqual(1, sink.Asked.Count, "asked once");
                Assert.AreEqual(ECueSource.Tag, sink.Asked[0].Source.Kind, "for a tag");
                Assert.AreEqual("Test.Cue.Net", sink.Asked[0].Source.Tag.Path, "the one raised");
                Assert.AreEqual(2, counting.Bursts, "its route played, and the tag it raises within");
            }
            finally
            {
                Object.DestroyImmediate(library);
            }
            await Frames.Step();
        });
    }
}
