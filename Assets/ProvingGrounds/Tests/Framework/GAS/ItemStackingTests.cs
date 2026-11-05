using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Item stacking (FEAT-14, Decision 30), which the Roguelite lab needs. A give fills the item's existing stack first,
    /// up to its Max Stack Size, and a full stack opens another only with Allow Duplicate Items on. A stack applies its
    /// effects and abilities once, and a scaler can read its count. <c>GiveItem(item, count)</c> takes what fits and
    /// returns how many it took; <c>RemoveItem(item, count)</c> takes stacks off.
    /// </summary>
    public class ItemStackingTests : ProvingGroundsTest
    {
        // Gives by the count, by reflection until the overload is in
        private static int Give(ItemSystemComponent items, Item item, int count)
        {
            var method = typeof(ItemSystemComponent).GetMethod("GiveItem", new[] { typeof(Item), typeof(int) });
            Assert.IsNotNull(method, "an item can be given by the count");
            return (int)method.Invoke(items, new object[] { item, count });
        }

        // Takes stacks off, by reflection until the overload is in
        private static int Remove(ItemSystemComponent items, Item item, int count)
        {
            var method = typeof(ItemSystemComponent).GetMethod("RemoveItem", new[] { typeof(Item), typeof(int) });
            Assert.IsNotNull(method, "an item's stacks can be taken off by the count");
            return (int)method.Invoke(items, new object[] { item, count });
        }

        private Item Stackable(string name, int max)
        {
            var item = Make.Item(name);
            item.Definition.MaxStackSize = max;
            return item;
        }

        private static int Stacks(ItemSystemComponent items, Item item) =>
            items.TryGetItemContainer(item, out var container) ? container.ItemStacks : 0;

        /// <summary>
        /// An item's Max Stack Size was never read, and its stack count never set (FEAT-14, GAS-51). A give fills the
        /// existing stack first, and without Allow Duplicate Items, what a full stack can't take isn't taken.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_14_A_give_fills_the_stack_first([Values] Hub hub) => Live(async () =>
        {
            var items = World.Entity(hub).GetItemSystem();
            var coin = Stackable("Coin", 5);

            Assert.AreEqual(3, Give(items, coin, 3), "three coins are taken");
            Assert.AreEqual(1, items.ItemCount, "in one stack");
            Assert.AreEqual(3, Stacks(items, coin), "of three");
            Assert.AreEqual(2, Give(items, coin, 4), "of four more, the stack takes the two it has room for");
            Assert.AreEqual(5, Stacks(items, coin), "and is full");
            Assert.AreEqual(1, items.ItemCount, "and no second stack opens without Allow Duplicate Items");
            await Frames.Step();
        });

        /// <summary>An item given one at a time, as <c>GiveItem(item, out index)</c> gives it, stacks too, in the one slot (FEAT-14).</summary>
        [UnityTest]
        public IEnumerator FEAT_14_An_item_given_one_at_a_time_stacks([Values] Hub hub) => Live(async () =>
        {
            var items = World.Entity(hub).GetItemSystem();
            var gem = Stackable("Gem", 3);
            Assert.IsTrue(items.GiveItem(gem, out int first), "a gem is given");
            Assert.IsTrue(items.GiveItem(gem, out int second), "and another");
            Assert.AreEqual(first, second, "into the same slot");
            Assert.AreEqual(2, Stacks(items, gem), "which holds two");
            await Frames.Step();
        });

        /// <summary>With Allow Duplicate Items on, a full stack opens another, and every one asked for is taken (FEAT-14).</summary>
        [UnityTest]
        public IEnumerator FEAT_14_A_full_stack_opens_another_with_duplicates() => Live(async () =>
        {
            var identity = Make.RuntimeIdentity("Hoarder", Make.UnusedSet());
            identity.AllowDuplicateItems = true;
            var gas = new RuntimeGameplayAbilitySystem(identity);
            World.Register(gas);
            var items = gas.GetItemSystem();
            var coin = Stackable("Coin", 5);

            Assert.AreEqual(7, Give(items, coin, 7), "seven coins are taken");
            Assert.AreEqual(2, items.ItemCount, "in two stacks");
            var containers = items.GetItemContainers();
            Assert.AreEqual(5, containers[0].ItemStacks, "a full one");
            Assert.AreEqual(2, containers[1].ItemStacks, "and the rest");
            await Frames.Step();
        });

        /// <summary>
        /// A stack applies its effects once, and a scaler reads its count (FEAT-14): a relic of +10 might a stack grants
        /// +10 with one, and +30 once two more stack on, its effect still applied once.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_14_A_stack_applies_its_effects_once_and_a_scaler_reads_its_count([Values] Hub hub) => Live(async () =>
        {
            var might = Make.Attribute("Might");
            var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f)));
            var items = gas.GetItemSystem();
            var scalerType = typeof(AbstractScaler).Assembly.GetType("FarEmerald.PlayForge.ItemStacksScaler");
            Assert.IsNotNull(scalerType, "a scaler reads an item's stack count");

            var blessing = Make.Effect("Blessing", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = might, Magnitude = 10f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = (AbstractScaler)Activator.CreateInstance(scalerType),
            });
            var relic = Stackable("Relic", 9);
            relic.GrantedEffects = new List<GameplayEffect> { blessing };

            Assert.IsTrue(items.GiveAndEquipItem(relic, out _), "the relic is equipped");
            await Frames.Step();
            Assert.IsTrue(gas.TryGetAttributeValue(might, out var value) && value.CurrentValue == 10f, "a stack grants +10");

            Assert.AreEqual(2, Give(items, relic, 2), "two more stack on");
            await Frames.Step(2);
            gas.TryGetAttributeValue(might, out value);
            Assert.AreEqual(30f, value.CurrentValue, "and the scaler reads three");
            Assert.IsTrue(gas.TryGetEffectContainers(blessing, out var applied) && applied.Length == 1, "from one application of the effect");
        });

        /// <summary>
        /// Taking stacks off leaves the rest, and the last one takes the item with it (FEAT-14): of three coins, two come off,
        /// then the last, and the slot is gone.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_14_Removing_takes_stacks_off([Values] Hub hub) => Live(async () =>
        {
            var items = World.Entity(hub).GetItemSystem();
            var coin = Stackable("Coin", 5);
            Assert.AreEqual(3, Give(items, coin, 3), "three coins are taken");

            Assert.AreEqual(2, Remove(items, coin, 2), "two come off");
            Assert.AreEqual(1, Stacks(items, coin), "leaving one");
            Assert.AreEqual(1, Remove(items, coin, 5), "and of five asked for, the one left comes off");
            Assert.IsFalse(items.HasItem(coin), "and the coin with it");
            await Frames.Step();
        });
    }
}
