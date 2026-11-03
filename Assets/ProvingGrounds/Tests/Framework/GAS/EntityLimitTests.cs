using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EntityLimitTests : ProvingGroundsTest
    {
        /// <summary>
        /// An identity's max abilities and items had no default, so one that didn't set them could hold neither:
        /// a new asset's came out at 0, and a runtime identity's weren't there at all.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_23_An_identity_that_sets_no_limits_can_hold_abilities_and_items([Values] Hub hub) => Live(async () =>
        {
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity("Unlimited", Make.UnusedSet());
                var fresh = new RuntimeEntityIdentity();
                identity.MaxAbilitiesOperation = fresh.MaxAbilitiesOperation;
                identity.MaxItemsOperation = fresh.MaxItemsOperation;
                identity.MaxEquippedItemsOperation = fresh.MaxEquippedItemsOperation;

                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(name: "Unlimited");
                var fresh = ScriptableObject.CreateInstance<EntityIdentity>();
                mono.EntityData.MaxAbilitiesOperation = fresh.MaxAbilitiesOperation;
                mono.EntityData.MaxItemsOperation = fresh.MaxItemsOperation;
                mono.EntityData.MaxEquippedItemsOperation = fresh.MaxEquippedItemsOperation;
                Object.Destroy(fresh);

                World.Register(mono);
                gas = mono;
            }
            await Frames.Step();

            Assert.That(gas.GetAbilitySystem().GiveAbility(Make.Ability("Any"), 1, out _), "an ability");
            Assert.That(gas.GetItemSystem().GiveItem(Make.Item("Any"), out _), "and an item");
        });
    }
}
