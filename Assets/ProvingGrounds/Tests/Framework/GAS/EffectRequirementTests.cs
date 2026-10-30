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
        [UnityTest, Category("Red")]
        public IEnumerator GAS_06_A_removal_requirement_doesnt_block_application([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var poison = Poison(out _);
            RemoveWhen(poison, Make.NewTag("Test.Status.Cleansed"));

            Assert.That(World.Apply(gas, gas, poison), "it applies, as nothing is asking for its removal yet");
            await Frames.Step();
        });

        /// <summary>
        /// The removal check is inverted, so an effect leaves while its requirement ISN'T met. It takes a
        /// requirement on both sides to show, because the sides are AND-ed and an empty group never passes.
        /// The requirements go on after the effect lands, to get past the application bug above.
        /// </summary>
        [UnityTest, Category("Red")]
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

        /// <summary>The control for the test above: with one side empty, the AND keeps the effect on today.</summary>
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
        /// The removal check needs the target's AND the source's group to pass, and an empty group never does
        /// (it's inverted too), so a requirement on one side alone can never remove anything.
        /// </summary>
        [UnityTest, Category("Red")]
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
        [UnityTest, Category("Red")]
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
