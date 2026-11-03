using System.Collections.Generic;
using NUnit.Framework;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EffectDefinitionTests : ProvingGroundsTest
    {
        /// <summary>A copied impact dropped its affiliation, and one with no contained effects couldn't be copied.</summary>
        [Test, Category("Red")]
        public void ED_14_A_copied_impact_keeps_its_affiliation_and_needs_no_packets()
        {
            var impact = new GameplayEffectImpact
            {
                AffiliationPolicy = EAffiliationPolicy.UseAffiliationList,
                AffiliationComparison = EAnyAllPolicy.All,
                Affiliations = new List<Tag> { Make.NewTag("Test.Team") },
                Packets = null
            };

            GameplayEffectImpact copy = null;
            Assert.DoesNotThrow(() => copy = new GameplayEffectImpact(impact), "an impact with no packets copies");
            Assert.That(copy.AffiliationComparison, Is.EqualTo(EAnyAllPolicy.All), "with its comparison");
            Assert.That(copy.Affiliations, Is.EqualTo(impact.Affiliations), "and its affiliations");
            Assert.That(copy.Affiliations, Is.Not.SameAs(impact.Affiliations), "in a list of its own");
        }
    }
}
