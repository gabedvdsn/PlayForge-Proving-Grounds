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
        [Test, Category("Red")]
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
    }
}
