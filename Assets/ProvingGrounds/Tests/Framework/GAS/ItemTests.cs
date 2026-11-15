using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ItemTests : ProvingGroundsTest
    {
        /// <summary>
        /// An item's tag comes with it and goes with it. Every held item's asset tag is its holder's, and giving or taking
        /// one asked for no compile of the tags, so a given item's tag waited for the next unrelated one and a removed
        /// item's stayed, an equipped one's too, as unequipping compiled while it was still held (GAS-82, seen mapping
        /// the architecture).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_82_An_items_tag_comes_and_goes_with_it([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var charm = Make.Item("Charm");
            var ring = Make.Item("Ring");
            await Frames.Step();
            var items = gas.GetItemSystem();

            Assert.IsTrue(items.GiveItem(charm, out _), "the charm is given");
            Assert.That(gas.GetTagWeight(charm.Tags.AssetTag), Is.GreaterThan(0), "and its tag is its holder's at once");
            Assert.IsTrue(items.RemoveItem(charm), "it's taken back");
            Assert.That(gas.GetTagWeight(charm.Tags.AssetTag), Is.LessThanOrEqualTo(0), "and its tag goes with it");

            Assert.IsTrue(items.GiveAndEquipItem(ring, out _), "a ring goes on");
            Assert.That(gas.GetTagWeight(ring.Tags.AssetTag), Is.GreaterThan(0), "its tag with it");
            Assert.IsTrue(items.RemoveItem(ring), "it's taken off and away");
            Assert.That(gas.GetTagWeight(ring.Tags.AssetTag), Is.LessThanOrEqualTo(0), "and its tag goes too");
        });

        // An entity on either hub that may hold and wear two of one item
        private IGameplayAbilitySystem Collector(Hub hub, AttributeSet set)
        {
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity("Collector", set);
                identity.AllowDuplicateItems = true;
                identity.AllowDuplicateEquippedItems = true;
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, "Collector");
            mono.EntityData.AllowDuplicateItems = true;
            mono.EntityData.AllowDuplicateEquippedItems = true;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        /// <summary>
        /// Two copies of an item share its workers, one instance of each. Worn together, the attribute, tag and analysis
        /// caches kept each once and dropped it as the first copy came off, the second still worn, and the impact cache
        /// ran it once for each copy. Each cache counts the copies that give it a worker, runs it once, and keeps it until
        /// the last goes (GAS-83, Decision 85, seen mapping the architecture).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_83_A_worker_two_copies_share_stays_until_both_go([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var holder = Collector(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var foe = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)));
            var amulet = Make.Item("Amulet");
            amulet.WorkerGroup = new StandardWorkerGroup();
            amulet.WorkerGroup.AttributeWorkers.Add(new CantFallBelowAttributeWorker { TargetAttribute = health, Floor = 50f });
            amulet.WorkerGroup.ImpactWorkers.Add(new ProbeImpactWorker("Amulet", health, Faults));
            await Frames.Step();
            var items = holder.GetItemSystem();
            float Health() => holder.TryGetAttributeValue(health, out var value) ? value.CurrentValue : float.NaN;

            Assert.IsTrue(items.GiveAndEquipItem(amulet, out var first), "one amulet goes on");
            Assert.IsTrue(items.GiveAndEquipItem(amulet, out var second), "and a second, a copy");
            World.Apply(holder, foe, Make.Effect("Poke", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f }));
            await Frames.Step(2);
            Assert.That(Faults.Hits("Amulet.Inline"), Is.EqualTo(1), "their impact worker runs once for a hit, not once a copy");

            Assert.IsTrue(items.UnequipItem(first), "the first comes off");
            World.Apply(foe, holder, Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -80f }));
            await Frames.Step(2);
            Assert.That(Health(), Is.EqualTo(50f), "and the second's floor still holds");

            Assert.IsTrue(items.UnequipItem(second), "the second comes off");
            World.Apply(foe, holder, Make.Effect("Cut", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f }));
            await Frames.Step(2);
            Assert.That(Health(), Is.EqualTo(20f), "and with both off, the floor goes");
        });

        /// <summary>
        /// An item linked to a level provider takes its max level from the provider, as its Max Level says it does: its
        /// spec took the item's own (GAS-82).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_82_A_linked_item_takes_its_providers_max_level([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var provider = Make.Ability("Provider");
            provider.MaxLevel = 10;
            var charm = Make.Item("Linked Charm");
            charm.MaxLevel = 4;
            Assert.IsTrue(charm.LinkToProvider(provider), "the charm follows its provider's levels");
            await Frames.Step();

            Assert.IsTrue(gas.GetItemSystem().GiveItem(charm, out var index), "the charm is given");
            Assert.IsTrue(gas.GetItemSystem().TryGetItemContainer(index, out var held), "and held");
            Assert.AreEqual(10, held.Spec.GetLevel().MaxValue, "with its provider's max level, not its own");
        });

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

        /// <summary>
        /// Unequipping removed the item's Active Ability whether or not equipping had granted it. An entity that had the
        /// ability already, where duplicates aren't allowed, lost its own when the item came off.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_48_Unequipping_removes_only_the_ability_the_item_granted([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var abilities = gas.GetAbilitySystem();
            var slash = Make.Ability("Slash");
            Assert.That(abilities.GiveAbility(slash, 1, out _), "the entity knows it already");

            var sword = Make.Item("Sword");
            sword.ActiveAbility = slash;
            var items = gas.GetItemSystem();
            Assert.That(items.GiveAndEquipItem(sword, out int index), "the sword goes on, though it can't grant a second Slash");
            items.UnequipItem(index);

            Assert.That(abilities.HasAbility(slash), "the entity keeps its own");
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
