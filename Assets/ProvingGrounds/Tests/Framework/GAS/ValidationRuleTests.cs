using System.Collections;
using NUnit.Framework;
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
    }
}
