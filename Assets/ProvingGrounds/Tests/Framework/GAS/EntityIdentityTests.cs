using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EntityIdentityTests : ProvingGroundsTest
    {
        /// <summary>A runtime copy of an identity lost what it starts with, so the entity came up with nothing.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_09_A_runtime_copy_starts_with_what_the_identity_does() => Live(async () =>
        {
            var identity = Filled(out var sword);

            var gas = new RuntimeGameplayAbilitySystem(new RuntimeEntityIdentity(identity));
            World.Register(gas);
            await Frames.Step();

            Assert.That(gas.GetAbilitySystem().AbilityCount, Is.EqualTo(1), "its starting ability");
            Assert.That(gas.GetItemSystem().HasItem(sword), "its starting item");
        });

        /// <summary>Every field the two share, so the next one added can't be left out of the copy either.</summary>
        [Test, Category("Red")]
        public void GAS_09_A_runtime_copy_carries_every_field_the_identity_has()
        {
            var identity = Filled(out _);
            var copy = new RuntimeEntityIdentity(identity);

            foreach (var field in typeof(RuntimeEntityIdentity).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var original = typeof(EntityIdentity).GetField(field.Name, BindingFlags.Public | BindingFlags.Instance);
                if (original != null) Assert.That(field.GetValue(copy), Is.EqualTo(original.GetValue(identity)), field.Name);
            }
        }

        private EntityIdentity Filled(out Item sword)
        {
            sword = Make.Item("Starting Sword");

            var identity = Make.Identity("Hero", Make.UnusedSet());
            identity.StartingAbilities.Add(Make.Ability("Starting Ability"));
            identity.StartingItems.Add(new StartingItemContainer { Item = sword });
            identity.AllowDuplicateAbilities = true;
            identity.AllowDuplicateItems = true;
            identity.AllowDuplicateEquippedItems = true;
            return identity;
        }
    }
}
