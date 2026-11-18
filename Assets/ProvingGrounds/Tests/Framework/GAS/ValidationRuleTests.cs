using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>The activation rules an ability's caster or target must pass.</summary>
    public class ValidationRuleTests : ProvingGroundsTest
    {
        /// <summary>
        /// An Attribute Validation compared the attribute's value alone, so "usable on a target below half its health" had
        /// to name a number of health, right for one entity and wrong for the next. It can compare the attribute's share of
        /// its base, 0 to 1. Found building the demo, whose Eviscerate wants a target below half (FEAT-26).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_26_An_attribute_rule_reads_a_share_of_the_base([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var hurt = World.Entity(hub, Make.Set(Make.Element(health, 40f, 100f)), "Hurt");
            var hale = World.Entity(hub, Make.Set(Make.Element(health, 80f, 100f)), "Hale");
            await Frames.Step();

            var belowHalf = new AttributeValidation { Attribute = health, Comparison = EComparisonOperator.LessThan, Value = 0.5f, ShareOfBase = true };
            Assert.IsTrue(belowHalf.Validate(null, () => hurt, out _), "40 of 100 is below half");
            Assert.IsFalse(belowHalf.Validate(null, () => hale, out _), "80 of 100 isn't");

            var byValue = new AttributeValidation { Attribute = health, Comparison = EComparisonOperator.LessThan, Value = 50f };
            Assert.IsTrue(byValue.Validate(null, () => hurt, out _), "by value, 40 is below 50, as before");
            Assert.IsFalse(byValue.Validate(null, () => hale, out _), "and 80 isn't");
        });

        /// <summary>
        /// An ability's rules couldn't read a condition, which an effect's requirements, a modifier and an On Hit can
        /// (FEAT-30). A Condition rule holds while its condition does, read with the entity its list hands it as the target,
        /// and says what didn't hold.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_30_A_condition_rule_holds_while_its_condition_does([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var hurt = World.Entity(hub, Make.Set(Make.Element(health, 40f, 100f)), "Hurt");
            var hale = World.Entity(hub, Make.Set(Make.Element(health, 80f, 100f)), "Hale");
            await Frames.Step();

            var rule = new ConditionValidation
            {
                When = new AttributeCondition { On = NumberSide.Target, Attribute = health, Comparison = EComparisonOperator.LessOrEqual, Value = 50f },
            };
            Assert.IsTrue(rule.Validate(null, () => hurt, out _), "40 is at most 50");
            Assert.IsFalse(rule.Validate(null, () => hale, out string why), "80 isn't");
            StringAssert.Contains(health.GetName(), why, "and it says what didn't hold");
            Assert.IsTrue(new ConditionValidation().Validate(null, () => hale, out _), "a rule with no condition holds");
            Assert.AreEqual(EValidationSide.Either, ValidationSideAttribute.Of(typeof(ConditionValidation)), "among either list");
        });

        // ─── The rules' pass and seven new rules (EP-35) ─────────────────────────────────────────────────────────────

        private Tag red, blue;

        [SetUp]
        public void Sides()
        {
            red = Make.NewTag("Test.EP35.Red");
            blue = Make.NewTag("Test.EP35.Blue");
        }

        /// <summary>
        /// Is Alive read the entity it was handed with no check that there was one, so a missing target threw where it
        /// should be refused (EP-35). It's refused, and the rule says why.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_35_Is_Alive_refuses_a_missing_entity_and_says_why([Values] Hub hub) => Live(async () =>
        {
            var rule = new IsAliveValidation();
            bool passes = true;
            string error = null;
            Assert.DoesNotThrow(() => passes = rule.Validate(null, () => null, out error), "a missing entity throws nothing");
            Assert.IsFalse(passes, "and is refused");
            Assert.That(error, Is.Not.Null.And.Not.Empty, "saying why");

            var living = World.Entity(hub);
            await Frames.Step();
            Assert.IsTrue(rule.Validate(null, () => living, out _), "a living entity passes");
        });

        /// <summary>A refused rule left its error empty, or said only that it failed (EP-35); it says why it refused.</summary>
        [UnityTest]
        public IEnumerator EP_35_A_refused_rule_says_why([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var hale = World.Entity(hub, Make.Set(Make.Element(health, 80f, 100f)), "Hale");
            await Frames.Step();

            var below = new AttributeValidation { Attribute = health, Comparison = EComparisonOperator.LessThan, Value = 50f };
            Assert.IsFalse(below.Validate(null, () => hale, out var error), "80 isn't below 50");
            StringAssert.Contains("80", error, "the error says the value it read");
            StringAssert.Contains("50", error, "and what it compared it with");
        });

        /// <summary>
        /// Affiliation takes a target by how it stands to its caster (EP-35): an enemy shares none of its sides, an ally
        /// shares one, the caster among them, and the caster itself is its own case.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_35_Affiliation_takes_enemies_allies_and_the_caster([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var ally = At(hub, "Ally", red, Vector3.right);
            var enemy = At(hub, "Enemy", blue, Vector3.left);
            await Frames.Step();
            var data = CasterData(caster);

            bool Takes(EAffiliationFilter affiliation, IGameplayAbilitySystem target) =>
                new AffiliationValidation { Target = affiliation }.Validate(data, () => target, out _);
            Assert.IsTrue(Takes(EAffiliationFilter.Enemies, enemy), "an enemy is an enemy");
            Assert.IsFalse(Takes(EAffiliationFilter.Enemies, ally), "an ally isn't");
            Assert.IsFalse(Takes(EAffiliationFilter.Enemies, caster), "nor is the caster");
            Assert.IsTrue(Takes(EAffiliationFilter.Allies, ally), "an ally is an ally");
            Assert.IsTrue(Takes(EAffiliationFilter.Allies, caster), "and so is the caster");
            Assert.IsFalse(Takes(EAffiliationFilter.Allies, enemy), "an enemy isn't");
            Assert.IsTrue(Takes(EAffiliationFilter.OtherAllies, ally), "another ally is");
            Assert.IsFalse(Takes(EAffiliationFilter.OtherAllies, caster), "the caster isn't another");
            Assert.IsTrue(Takes(EAffiliationFilter.Self, caster), "the caster is itself");
            Assert.IsFalse(Takes(EAffiliationFilter.Self, ally), "an ally isn't");
            Assert.IsTrue(Takes(EAffiliationFilter.AllButSelf, enemy), "anyone else is not itself");
            Assert.IsFalse(Takes(EAffiliationFilter.AllButSelf, caster), "the caster is");

            Assert.IsFalse(new AffiliationValidation().Validate(data, () => ally, out var error), "an enemy rule refuses an ally");
            Assert.That(error, Is.Not.Null.And.Not.Empty, "saying why");
        });

        /// <summary>
        /// Tag Weight compares a tag's weight on the checked entity with a number, as combo points or stacks (EP-35); an
        /// entity without the tag holds it at 0.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_35_Tag_Weight_compares_a_tags_weight([Values] Hub hub) => Live(async () =>
        {
            var combo = Make.NewTag("Test.EP35.Combo");
            var entity = World.Entity(hub);
            for (int i = 0; i < 3; i++) entity.AddLooseTag(combo);
            var other = World.Entity(hub, name: "Other");
            await Frames.Step();
            Assume.That(entity.GetTagWeight(combo), Is.EqualTo(3), "three grants, a weight of three");

            Assert.IsTrue(new TagWeightValidation { Tag = combo, Weight = 3 }.Validate(null, () => entity, out _), "three is at least three");
            Assert.IsFalse(new TagWeightValidation { Tag = combo, Weight = 4 }.Validate(null, () => entity, out var error), "but not four");
            StringAssert.Contains("3", error, "the error says the weight it read");
            Assert.IsFalse(new TagWeightValidation { Tag = combo }.Validate(null, () => other, out _), "one without the tag holds none");
            Assert.IsTrue(new TagWeightValidation { Tag = combo, Comparison = EComparisonOperator.Equal, Weight = 0 }
                .Validate(null, () => other, out _), "holding it at 0");
        });

        /// <summary>
        /// Has Effect asks whether the checked entity has an effect on, by its asset or by a tag it carries, and Without
        /// asks the other way (EP-35); an effect leaving isn't had.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_35_Has_Effect_finds_an_effect_by_asset_or_by_tag([Values] Hub hub) => Live(async () =>
        {
            var marked = Make.NewTag("Test.EP35.Marked");
            var mark = Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { marked } });
            var entity = World.Entity(hub);
            await Frames.Step();

            var byAsset = new HasEffectValidation { Effect = mark };
            var byTag = new HasEffectValidation { Tagged = marked };
            var without = new HasEffectValidation { Effect = mark, Without = true };
            Assert.IsFalse(byAsset.Validate(null, () => entity, out var error), "unmarked, it hasn't the mark");
            Assert.That(error, Is.Not.Null.And.Not.Empty, "saying why");
            Assert.IsFalse(byTag.Validate(null, () => entity, out _), "nor anything tagged Marked");
            Assert.IsTrue(without.Validate(null, () => entity, out _), "so Without passes");

            Assert.IsTrue(World.Apply(entity, entity, mark), "the mark lands");
            await Frames.Step();
            Assert.IsTrue(byAsset.Validate(null, () => entity, out _), "marked, it has the mark");
            Assert.IsTrue(byTag.Validate(null, () => entity, out _), "and an effect that grants Marked");
            Assert.IsFalse(without.Validate(null, () => entity, out _), "so Without refuses");

            entity.RemoveGameplayEffect(mark.Tags.AssetTag);
            Assert.IsFalse(byAsset.Validate(null, () => entity, out _), "a mark leaving isn't had");
        });

        /// <summary>Holds Item asks whether the checked entity holds an item, or with Equipped wears it (EP-35).</summary>
        [UnityTest]
        public IEnumerator EP_35_Holds_Item_asks_whether_an_item_is_held_or_worn([Values] Hub hub) => Live(async () =>
        {
            var shield = Make.Item("Shield");
            var entity = World.Entity(hub);
            await Frames.Step();

            var holds = new HoldsItemValidation { Item = shield };
            var wears = new HoldsItemValidation { Item = shield, Equipped = true };
            Assert.IsFalse(holds.Validate(null, () => entity, out var error), "it holds no shield");
            Assert.That(error, Is.Not.Null.And.Not.Empty, "saying why");

            var items = entity.GetItemSystem();
            Assert.IsTrue(items.GiveItem(shield, out _), "it's given the shield");
            Assert.IsTrue(holds.Validate(null, () => entity, out _), "and holds it");
            Assert.IsFalse(wears.Validate(null, () => entity, out _), "but doesn't wear it");
            Assert.IsTrue(items.EquipItem(shield), "it puts it on");
            Assert.IsTrue(wears.Validate(null, () => entity, out _), "and wears it");
        });

        /// <summary>Ability Level compares the ability's level with a number, as a form it unlocks at a rank (EP-35).</summary>
        [UnityTest]
        public IEnumerator EP_35_Ability_Level_compares_the_abilitys_level([Values] Hub hub) => Live(async () =>
        {
            var entity = World.Entity(hub);
            await Frames.Step();

            var rank2 = new AbilityLevelValidation { Level = 2 };
            Assert.IsFalse(rank2.Validate(CasterData(entity, 1), () => entity, out var error), "level 1 is under 2");
            StringAssert.Contains("1", error, "the error says its level");
            Assert.IsTrue(rank2.Validate(CasterData(entity, 2), () => entity, out _), "level 2 is at it");
        });

        /// <summary>
        /// Line of Sight is blocked by a collider between the caster and its target, on the layers it names, and not by the
        /// two ends' own colliders (EP-35); measured by the targeting packets either hub has.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_35_Line_of_Sight_is_blocked_by_a_wall_not_by_its_ends([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var target = At(hub, "Target", blue, new Vector3(10f, 0f, 0f));
            // Each end's own collider, which the line passes through and doesn't stop at
            if (caster is Component casterBody) casterBody.gameObject.AddComponent<SphereCollider>().radius = 2f;
            if (target is Component targetBody) targetBody.gameObject.AddComponent<SphereCollider>().radius = 2f;
            var wall = new GameObject("Wall");
            try
            {
                wall.AddComponent<BoxCollider>().size = new Vector3(1f, 4f, 4f);
                wall.transform.position = new Vector3(5f, 1f, 0f);
                Physics.SyncTransforms();
                await Frames.Step();
                var data = CasterData(caster);

                var sight = new LineOfSightValidation();
                Assert.IsFalse(sight.Validate(data, () => target, out var error), "the wall blocks the line");
                StringAssert.Contains("Wall", error, "and the error names it");
                Assert.IsTrue(new LineOfSightValidation { Blocking = 0 }.Validate(data, () => target, out _), "on layers the wall isn't on, it doesn't");

                wall.transform.position = new Vector3(5f, 1f, 20f);
                Physics.SyncTransforms();
                Assert.IsTrue(sight.Validate(data, () => target, out _), "moved aside, nothing blocks it, the ends' own colliders aside");
            }
            finally
            {
                Object.Destroy(wall);
            }
        });

        /// <summary>
        /// Facing takes a target within an angle in front of its caster, or a caster within an angle behind its target, as
        /// a backstab wants (EP-35), each facing its targeting packet's.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_35_Facing_takes_the_casters_front_or_the_targets_back([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var target = At(hub, "Target", blue, new Vector3(0f, 0f, 5f));
            caster.GetTargetingPacket().rotation = Quaternion.identity;
            target.GetTargetingPacket().rotation = Quaternion.identity;
            await Frames.Step();
            var data = CasterData(caster);

            var front = new FacingValidation { Angle = 90f };
            var behind = new FacingValidation { Facing = EFacingCheck.BehindTarget, Angle = 90f };
            Assert.IsTrue(front.Validate(data, () => target, out _), "the target is ahead of the caster");
            Assert.IsTrue(behind.Validate(data, () => target, out _), "and the caster behind the target, which faces away");

            target.GetTargetingPacket().rotation = Quaternion.Euler(0f, 180f, 0f);
            Assert.IsFalse(behind.Validate(data, () => target, out var error), "facing the caster, the target isn't backstabbed");
            Assert.That(error, Is.Not.Null.And.Not.Empty, "saying why");

            caster.GetTargetingPacket().rotation = Quaternion.Euler(0f, 180f, 0f);
            Assert.IsFalse(front.Validate(data, () => target, out _), "with the caster turned away, the target isn't ahead");
            Assert.IsTrue(new FacingValidation { Angle = 360f }.Validate(data, () => target, out _), "a full circle takes it anywhere");
        });

        // An entity of a side at a point, on either hub
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, Vector3 at)
        {
            var set = Make.Set(Make.Element(Make.Attribute("Health"), 100f, 100f));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = at;
            return gas;
        }

        // An activation's packet for an ability the caster knows at a level, as its rules are asked with
        private AbilityDataPacket CasterData(IGameplayAbilitySystem caster, int level = 1)
        {
            var ability = Make.Ability("Checked", Make.Stage());
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(ability, level, out _), "the caster knows the ability");
            Assert.IsTrue(abilities.TryGetAbilityContainer(ability, out var container), "and holds it");
            return AbilityDataPacket.GenerateFrom(container, abilities);
        }
    }
}
