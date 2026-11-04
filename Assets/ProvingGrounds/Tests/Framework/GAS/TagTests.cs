using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class TagTests : ProvingGroundsTest
    {
        /// <summary>
        /// A shelved effect granted its target the asset tag of whoever applied it, as if the target were its
        /// source. It grants its own asset tag, as an ability or an item does.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_14_An_effect_grants_its_own_asset_tag_not_its_sources([Values] Hub hub) => Live(async () =>
        {
            var source = World.Entity(hub, name: "Source");
            var target = World.Entity(hub, name: "Target");
            var marking = Make.Effect("Marking", new EffectConfig());

            World.Apply(source, target, marking);
            await Frames.Step();

            Assert.That(target.GetTagWeight(source.GetAssetTag()), Is.LessThanOrEqualTo(0), "not its source's asset tag");
            Assert.That(target.GetLongestDurationFor(source.GetAssetTag()).FoundDuration, Is.False, "nor is it found by it");
            Assert.That(target.GetTagWeight(marking.Tags.AssetTag), Is.GreaterThan(0), "but by its own");
        });

        /// <summary>
        /// A query counted a tag's weight for an exact match through the cache and distinct tags otherwise, and the
        /// GAS asked through the list, which has no weights. So "at least two" meant two grants of a tag or two
        /// different tags, depending on how it was asked.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_16_A_query_counts_a_tags_weight([Values] Hub hub, [Values] ETagMatchMode mode) => Live(async () =>
        {
            var gas = World.Entity(hub);
            await Frames.Step();

            string root = Make.Unique("Query");
            var parent = Tag.Generate(root);
            var child = Tag.Generate(root + ".Burn");
            var cousin = Tag.Generate(root + ".Chill");

            var (applied, asked) = mode switch
            {
                ETagMatchMode.IncludeChildren => (child, parent),
                ETagMatchMode.IncludeParents => (parent, child),
                ETagMatchMode.SameRoot => (child, cousin),
                _ => (child, child)
            };

            var tags = gas.GetTagCache();
            tags.AddTag(applied);
            tags.AddTag(applied);

            var query = new TagQuery { Tag = asked, Operator = EComparisonOperator.GreaterOrEqual, Magnitude = 2, MatchMode = mode };
            Assert.That(gas.QueryTags(query), "two grants of one tag count as two, asked through the GAS");
            Assert.That(query.Validate(tags), "and through its cache");
        });

        /// <summary>The control: every overload counts a list of tags alike, each tag once.</summary>
        [Test]
        public void Every_overload_counts_a_list_alike([Values] ETagMatchMode mode)
        {
            string root = Make.Unique("Overloads");
            var tags = new List<Tag> { Tag.Generate(root), Tag.Generate(root + ".Burn"), Tag.Generate(root + ".Chill"), Make.NewTag("Elsewhere") };
            var query = new TagQuery { Tag = Tag.Generate(root + ".Burn"), MatchMode = mode };

            Assert.That(query.CountMatches(new HashSet<Tag>(tags)), Is.EqualTo(query.CountMatches(tags)));
        }

        /// <summary>
        /// The hierarchy helpers, and a name made into a tag, gave deterministic tags, which pad the name with a
        /// hash. So a tag's parent wasn't the tag of that name, and a root query never found its own children.
        /// </summary>
        [Test]
        public void GAS_17_A_tags_relatives_are_the_tags_of_their_names()
        {
            string root = Make.Unique("Hierarchy");
            var status = Tag.Generate(root);
            var debuff = Tag.Generate(root + ".Debuff");
            var burn = Tag.Generate(root + ".Debuff.Burn");

            Assert.That(burn.GetParent(), Is.EqualTo(debuff), "its parent");
            Assert.That(burn.GetRoot(), Is.EqualTo(status), "its root");
            Assert.That(burn.GetAncestors(), Is.EqualTo(new[] { debuff, status }), "its ancestors");
            Assert.That(status.Child("Debuff"), Is.EqualTo(debuff), "a child");
            Assert.That((Tag)(root + ".Debuff.Burn"), Is.EqualTo(burn), "a name made into a tag");
            Assert.That(TagQuery.HasTag(status, ETagMatchMode.SameRoot).Validate(new List<Tag> { burn }), "and a root query finds its children");
        }

        /// <summary>
        /// A same-root query cut each side's root out as a new string, and registered it as a tag, for every tag it
        /// compared. It now compares the roots in place and allocates nothing, and still finds what it found.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P4_A_same_root_query_allocates_nothing() => Live(async () =>
        {
            var gas = World.Entity(Hub.Runtime, observed: false);
            var tags = gas.GetTagCache();
            tags.AddTag(Make.NewTag("Test.Status.Burning"));
            tags.AddTag(Make.NewTag("Test.Status.Wet"));
            var asked = Make.NewTag("Test.Other");
            var stranger = Make.NewTag("Elsewhere.Thing");
            await Frames.Step();

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() => tags.HasTag(asked, ETagMatchMode.SameRoot), runs: 500, warmup: 20);

            Assert.That(bytes, Is.EqualTo(0f), "bytes per query");
            Assert.That(tags.HasTag(asked, ETagMatchMode.SameRoot), "a tag under the same root");
            Assert.That(tags.HasTag(stranger, ETagMatchMode.SameRoot), Is.False, "and none under another");
        });

        /// <summary>
        /// A tag was hashed and compared by its name, a string, on every lookup, and a built-in tag's name is 64
        /// characters, so a lookup cost as much as the name was long. Each tag now has an ID, given as it's made or
        /// loaded, so a lookup costs the same however long the name, for a tag loaded with an asset as for one made in
        /// code.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P7_A_tag_lookup_costs_the_same_however_long_its_name([Values] bool loaded) => Live(async () =>
        {
            var tags = World.Entity(Hub.Runtime, observed: false).GetTagCache();
            var brief = Make.NewTag("B");
            var lengthy = Make.NewTag(new string('L', 2048));
            tags.AddTag(brief);
            tags.AddTag(lengthy);
            await Frames.Step();

            var briefAsked = loaded ? Loaded(brief) : brief;
            var lengthyAsked = loaded ? Loaded(lengthy) : lengthy;
            Assert.That(tags.GetWeight(briefAsked), Is.EqualTo(1), "found");
            Assert.That(tags.GetWeight(lengthyAsked), Is.EqualTo(1), "found");

            double briefMs = FastestLookups(tags, briefAsked), lengthyMs = FastestLookups(tags, lengthyAsked);
            TestContext.WriteLine($"ms per 10,000 lookups, a name of {brief.Name.Length} → {lengthy.Name.Length} " +
                                  $"characters: {briefMs:F4} → {lengthyMs:F4}");

            Assert.That(lengthyMs, Is.LessThan(3 * briefMs), "in milliseconds per 10,000 lookups");
        });

        /// <summary>
        /// The control for the IDs: a tag equals another of the same name whatever string holds it, follows its name when
        /// the name is changed in place, and a tag with no name at all still differs from one named with an empty string.
        /// </summary>
        [UnityTest]
        public IEnumerator A_tag_equals_another_of_its_name() => Live(async () =>
        {
            await Frames.Step();
            var burning = Make.NewTag("Test.Status.Burning");
            var wet = Make.NewTag("Test.Status.Wet");

            var copy = burning;
            copy.Name = new string(burning.Name.ToCharArray());
            Assert.That(copy, Is.EqualTo(burning), "the same name in a string of its own");
            Assert.That(copy.GetHashCode(), Is.EqualTo(burning.GetHashCode()));
            Assert.That(Loaded(burning), Is.EqualTo(burning), "and loaded with an asset");

            var renamed = burning;
            renamed.Name = wet.Name;
            Assert.That(renamed, Is.EqualTo(wet), "renamed in place, it's the tag of its new name");
            Assert.That(renamed, Is.Not.EqualTo(burning));
            Assert.That(renamed.GetHashCode(), Is.EqualTo(wet.GetHashCode()));

            Assert.That(default(Tag), Is.Not.EqualTo(Tag.Generate("")), "no name at all isn't the empty name");
            Assert.That(default(Tag), Is.EqualTo(new Tag()));
        });

        // The tag as an asset holding it loads: the asset made again from its serialized form, with a string of its own
        private Tag Loaded(Tag tag)
        {
            var holder = Make.Effect("Holder", new EffectConfig { Target = null });
            var held = holder.Tags;
            held.AssetTag = tag;
            holder.Tags = held;

            var loaded = UnityEngine.Object.Instantiate(holder);
            var copy = loaded.Tags.AssetTag;
            UnityEngine.Object.Destroy(loaded);

            Assert.That(copy.Name, Is.EqualTo(tag.Name), "the same name");
            Assert.That(ReferenceEquals(copy.Name, tag.Name), Is.False, "in a string of its own");
            return copy;
        }

        // Milliseconds for 10,000 lookups of one tag, the fastest of several rounds
        private static double FastestLookups(TagCache tags, Tag asked)
        {
            const int rounds = 7, lookups = 10_000;
            double fastest = double.MaxValue;
            int found = 0;
            var watch = new System.Diagnostics.Stopwatch();
            for (int round = 0; round < rounds; round++)
            {
                watch.Restart();
                for (int i = 0; i < lookups; i++) found += tags.GetWeight(asked);
                watch.Stop();
                fastest = System.Math.Min(fastest, watch.Elapsed.TotalMilliseconds);
            }

            Assert.That(found, Is.EqualTo(rounds * lookups), "every lookup found it");
            return fastest;
        }
    }
}
