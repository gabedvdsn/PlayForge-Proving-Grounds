using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// FN-14, Decision 77, in the editor: the kinds of component that make no sense twice say so, and the Problems view
    /// says where one never works.
    /// </summary>
    public class ComponentKindsEditorTests
    {
        private static List<string> Messages(Object asset) => ForgeValidator.Check(asset).Select(problem => problem.Message).ToList();

        [Test]
        public void FN_14_The_new_kinds_that_make_no_sense_twice_allow_one()
        {
            Assert.IsFalse(new CastRangeAbilityComponent().AllowsSeveral, "an ability has one Cast Range");
            Assert.IsFalse(new RespawnEntityComponent().AllowsSeveral, "an entity one Respawn");
            Assert.IsTrue(new BountyEntityComponent().AllowsSeveral, "and as many Bounties as it gives");
            Assert.IsTrue(new SpreadEffectComponent().AllowsSeveral, "an effect as many Spreads as it has moments");
            Assert.IsFalse(new UsesItemComponent().AllowsSeveral, "an item one Uses");
            Assert.IsTrue(new SetBonusItemComponent().AllowsSeveral, "and as many Set Bonuses as its set has");
        }

        /// <summary>Uses on an item with no ability to use never count, which the Problems view says.</summary>
        [Test]
        public void FN_14_Problems_say_where_an_items_uses_never_count()
        {
            var wand = ScriptableObject.CreateInstance<Item>();
            var bolt = ScriptableObject.CreateInstance<Ability>();
            try
            {
                wand.Components.Add(new UsesItemComponent());
                Assert.That(Messages(wand), Has.Some.StartsWith("Its Uses never count"), "it lends no ability");
                wand.ActiveAbility = bolt;
                Assert.That(Messages(wand), Has.None.StartsWith("Its Uses never count"), "with one, they count");
            }
            finally
            {
                Object.DestroyImmediate(wand);
                Object.DestroyImmediate(bolt);
            }
        }

        /// <summary>A Respawn on an entity that ends as it dies never revives it, which the Problems view says.</summary>
        [Test]
        public void FN_14_Problems_say_where_a_respawn_never_revives()
        {
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            try
            {
                hero.Components.Add(new RespawnEntityComponent());
                hero.Death = new EntityDeath { Policy = EDeathPolicy.End };
                Assert.That(Messages(hero), Has.Some.StartsWith("Its Respawn never revives it"), "it ends as it dies");
                hero.Death = new EntityDeath { Policy = EDeathPolicy.Stay };
                Assert.That(Messages(hero), Has.None.StartsWith("Its Respawn never revives it"), "it stays, so it revives");
            }
            finally
            {
                Object.DestroyImmediate(hero);
            }
        }
    }
}
