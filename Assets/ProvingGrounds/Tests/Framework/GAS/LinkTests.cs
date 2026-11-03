using NUnit.Framework;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class LinkTests : ProvingGroundsTest
    {
        /// <summary>An identity's UnlinkAllChildren linked its starting abilities again instead of unlinking them.</summary>
        [Test]
        public void GAS_30_An_identity_unlinks_its_abilities()
        {
            var ability = Make.Ability("Starting");
            var identity = Make.Identity("Linked", Make.UnusedSet());
            identity.StartingAbilities.Add(ability);
            ability.LinkToProvider(identity);
            Assert.That(ability.IsLinkedTo(identity), "linked first");

            identity.UnlinkAllChildren();

            Assert.That(ability.IsLinkedTo(identity), Is.False);
        }

        /// <summary>An ability unlinked its cooldown only if its cost was linked, and threw if it had no cost.</summary>
        [Test]
        public void GAS_30_An_ability_unlinks_its_cooldown()
        {
            var ability = Make.Ability("Cooling Down");
            var cooldown = Make.Effect("Cooldown", new EffectConfig());
            ability.Cooldown = cooldown;
            cooldown.LinkToProvider(ability);
            Assert.That(cooldown.IsLinkedTo(ability), "linked first");

            Assert.DoesNotThrow(() => ability.UnlinkAllChildren(), "with no cost");

            Assert.That(cooldown.IsLinkedTo(ability), Is.False);
        }
    }
}
