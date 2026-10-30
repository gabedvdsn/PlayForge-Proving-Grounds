using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ItemTests : ProvingGroundsTest
    {
        /// <summary>
        /// Most items have no ability of their own, and equipping one threw, with its effects already on and
        /// the item marked as equipped.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_32_An_item_with_no_ability_can_be_equipped([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var items = gas.GetItemSystem();
            int abilities = gas.GetAbilitySystem().AbilityCount;

            bool equipped = false;
            Assert.DoesNotThrow(() => equipped = items.GiveAndEquipItem(Make.Item("Ring"), out _));

            Assert.That(equipped, "it's on");
            Assert.That(gas.GetAbilitySystem().AbilityCount, Is.EqualTo(abilities), "and grants nothing it doesn't have");

            Assert.DoesNotThrow(() => items.UnequipAllItems());
            Assert.That(gas.GetAbilitySystem().GiveAbility(null, 1, out _), Is.False, "and there's no giving nothing");
            await Frames.Step();
        });

        /// <summary>Having no ability to grant isn't a failed grant, so a passive item says nothing about one.</summary>
        [UnityTest]
        public IEnumerator A_passive_item_reports_no_ability([Values] Hub hub) => Live(async () =>
        {
            var items = World.Entity(hub).GetItemSystem();

            int reports = 0;
            items.Callbacks.OnItemAbilityGranted += _ => reports++;
            items.Callbacks.OnItemAbilityRevoked += _ => reports++;

            items.GiveAndEquipItem(Make.Item("Ring"), out int index);
            items.UnequipItem(index);

            Assert.That(reports, Is.EqualTo(0));
            await Frames.Step();
        });

        /// <summary>
        /// Removing an item moves the rest down, so counting up skipped every other one, and the Clear after it
        /// dropped those without unequipping them. Their effects stayed on the entity with no item behind them.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_32_Clearing_items_takes_every_one_of_them_off([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var items = gas.GetItemSystem();

            var worn = new List<Tag>();
            for (int i = 0; i < 4; i++)
            {
                var tag = Make.NewTag("Test.Status.Worn");
                worn.Add(tag);
                Assert.That(items.GiveAndEquipItem(Gear($"Gear {i}", tag), out _), "equipped");
            }

            await Frames.Step();

            items.ClearAllItems();
            await Frames.Step(2);

            Assert.That(items.ItemCount, Is.EqualTo(0));
            foreach (var tag in worn) Assert.That(gas.GetTagWeight(tag), Is.LessThanOrEqualTo(0), "its effect went with it");
        });

        /// <summary>Unequipping removed the effect by asset, so it took the same effect from anywhere else too.</summary>
        [UnityTest]
        public IEnumerator GAS_32_Unequipping_leaves_the_same_effect_from_elsewhere([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var ally = World.Entity(hub, name: "Ally");
            var blessed = Make.NewTag("Test.Status.Blessed");
            var charm = Gear("Charm", blessed);
            var blessing = charm.GrantedEffects[0];

            var items = gas.GetItemSystem();
            items.GiveAndEquipItem(charm, out int index);
            World.Apply(ally, gas, blessing);
            await Frames.Step();

            Assert.That(gas.TryGetEffectContainers(blessing, out var both) ? both.Length : 0, Is.EqualTo(2), "one from each");

            items.UnequipItem(index);
            await Frames.Step(2);

            Assert.That(gas.TryGetEffectContainers(blessing, out var left) ? left.Length : 0, Is.EqualTo(1), "the ally's stays");
            Assert.That(left[0].Spec.Origin, Is.Not.InstanceOf<ItemSpec>(), "and the item's doesn't");
        });

        /// <summary>
        /// An item's spec always said it wasn't active, and an effect-scoped action is dropped when its origin
        /// isn't, so every queued action from an item's effect was thrown away.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_32_An_items_spec_is_active_while_it_is_equipped([Values] Hub hub) => Live(async () =>
        {
            var items = World.Entity(hub).GetItemSystem();
            items.GiveItem(Make.Item("Sword"), out int index);
            items.TryGetItemContainer(index, out var container);

            Assert.That(container.Spec.IsActive(), Is.False, "in the bag");

            items.EquipItem(index);
            Assert.That(container.Spec.IsActive(), "equipped");

            items.UnequipItem(index);
            Assert.That(container.Spec.IsActive(), Is.False, "taken off");

            await Frames.Step();
        });

        /// <summary>Tags compiled while the item still counted as equipped, so its tags outlived it.</summary>
        [UnityTest]
        public IEnumerator GAS_32_An_unequipped_item_takes_its_tags_with_it([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var items = gas.GetItemSystem();
            var armed = Make.NewTag("Test.Status.Armed");

            var sword = Make.Item("Sword");
            sword.Tags.PassiveGrantedTags = new List<Tag> { armed };

            items.GiveAndEquipItem(sword, out int index);
            Assert.That(gas.GetTagWeight(armed), Is.GreaterThan(0), "wearing it grants the tag");

            items.UnequipItem(index);
            Assert.That(gas.GetTagWeight(armed), Is.LessThanOrEqualTo(0), "taking it off takes the tag");

            await Frames.Step();
        });

        private Item Gear(string name, Tag grants)
        {
            var item = Make.Item(name);
            item.GrantedEffects.Add(Make.Effect(name, new EffectConfig { Target = null, GrantedTags = new List<Tag> { grants } }));
            return item;
        }
    }
}
