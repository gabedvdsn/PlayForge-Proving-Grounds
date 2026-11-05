using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EffectRequirementTests : ProvingGroundsTest
    {
        /// <summary>
        /// Application refuses an effect unless its removal group passes, so a poison that a cleanse removes
        /// can only land on a target already cleansed.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_06_A_removal_requirement_doesnt_block_application([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out _);
            RemoveWhen(poison, Make.NewTag("Test.Status.Cleansed"));

            Assert.That(World.Apply(gas, gas, poison), "it applies, as nothing is asking for its removal yet");
            await Frames.Step();
        });

        /// <summary>
        /// The removal check was inverted, so an effect left while its requirement WASN'T met. It took a
        /// requirement on both sides to show, because the sides were AND-ed and an empty group never passed.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_06_An_effect_stays_while_its_removal_requirement_isnt_met([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out var poisoned);
            var cleansed = Make.NewTag("Test.Status.Cleansed");
            Assert.That(World.Apply(gas, gas, poison), "it applies");

            RemoveWhen(poison, cleansed);
            poison.SourceRequirements.RemovalRequirements.RequireTags = new List<TagQuery> { TagQuery.HasTag(cleansed) };

            World.Apply(gas, gas, Make.Effect("Unrelated", new EffectConfig { Target = null }));
            await Frames.Step(2);

            Assert.That(gas.GetTagWeight(poisoned), Is.GreaterThan(0), "nothing has cleansed it");
        });

        /// <summary>The same with one side empty, which held even with the bug.</summary>
        [UnityTest]
        public IEnumerator An_effect_stays_while_a_target_requirement_isnt_met([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out var poisoned);
            Assert.That(World.Apply(gas, gas, poison), "it applies");

            RemoveWhen(poison, Make.NewTag("Test.Status.Cleansed"));

            World.Apply(gas, gas, Make.Effect("Unrelated", new EffectConfig { Target = null }));
            await Frames.Step(2);

            Assert.That(gas.GetTagWeight(poisoned), Is.GreaterThan(0), "nothing has cleansed it");
        });

        /// <summary>
        /// The removal check needed the target's AND the source's group to pass, and an empty group never did,
        /// so a requirement on one side alone could never remove anything.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_06_An_effect_leaves_when_its_removal_requirement_is_met([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out var poisoned);
            var cleansed = Make.NewTag("Test.Status.Cleansed");
            Assert.That(World.Apply(gas, gas, poison), "it applies");

            RemoveWhen(poison, cleansed);

            var cleanse = Make.Effect("Cleanse", new EffectConfig { Target = null, GrantedTags = new List<Tag> { cleansed } });
            World.Apply(gas, gas, cleanse);
            await Frames.Step(2);

            Assert.That(gas.GetTagWeight(poisoned), Is.LessThanOrEqualTo(0), "the cleanse removed it");
        });

        /// <summary>A target that already meets the removal requirement would lose the effect as it landed.</summary>
        [UnityTest]
        public IEnumerator GAS_06_An_effect_doesnt_land_when_its_removal_requirement_is_met([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out _);
            var cleansed = Make.NewTag("Test.Status.Cleansed");
            RemoveWhen(poison, cleansed);

            World.Apply(gas, gas, Make.Effect("Cleanse", new EffectConfig { Target = null, GrantedTags = new List<Tag> { cleansed } }));
            await Frames.Step();

            Assert.That(World.Apply(gas, gas, poison), Is.False, "the target is already cleansed");
        });

        /// <summary>
        /// An effect's requirements were checked again only when an effect was applied or removed, so a tag granted with
        /// no effect behind it, as a loose tag is, didn't end an effect its removal names until another effect came or
        /// went (GAS-56).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_56_A_loose_tag_ends_an_effect_that_names_it([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out var poisoned);
            var cleansed = Make.NewTag("Test.Status.Cleansed");
            RemoveWhen(poison, cleansed);
            Assert.That(World.Apply(gas, gas, poison), "it applies");
            await Frames.Step();

            gas.AddLooseTag(cleansed);
            await Frames.Step(2);

            Assert.That(gas.GetTagWeight(poisoned), Is.LessThanOrEqualTo(0), "the loose tag ends it, with no other effect coming or going");
        });

        /// <summary>The same for a tag an ability grants while the entity holds it (GAS-56).</summary>
        [UnityTest]
        public IEnumerator GAS_56_A_tag_an_ability_grants_ends_an_effect_that_names_it([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out var poisoned);
            var cleansed = Make.NewTag("Test.Status.Cleansed");
            RemoveWhen(poison, cleansed);
            Assert.That(World.Apply(gas, gas, poison), "it applies");
            await Frames.Step();

            var ward = Make.Ability("Ward", Make.Stage());
            ward.Tags.PassiveGrantedTags = new List<Tag> { cleansed };
            Assert.That(gas.GetAbilitySystem().GiveAbility(ward, 1, out _), "an ability that grants the tag");
            await Frames.Step(2);

            Assert.That(gas.GetTagWeight(poisoned), Is.LessThanOrEqualTo(0), "its tag ends it");
        });

        private static void RemoveWhen(GameplayEffect effect, Tag tag)
        {
            effect.TargetRequirements.RemovalRequirements.RequireTags = new List<TagQuery> { TagQuery.HasTag(tag) };
        }

        private GameplayEffect Poison(out Tag poisoned)
        {
            poisoned = Make.NewTag("Test.Status.Poisoned");
            return Make.Effect("Poison", new EffectConfig { Target = null, GrantedTags = new List<Tag> { poisoned } });
        }
    }
}
