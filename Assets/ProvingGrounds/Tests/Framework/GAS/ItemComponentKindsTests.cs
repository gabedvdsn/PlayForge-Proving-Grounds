using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FN-14, Decision 77: an item's Uses and Set Bonus components. Each use of an item's ability, one that paid what it
    /// costs, is a use of the item, which its components hear; a Uses component counts them, one of the item going as they
    /// run out. A Set Bonus gives its holder an effect while enough of its set's pieces are equipped, once.
    /// </summary>
    public class ItemComponentKindsTests : ProvingGroundsTest
    {
        private Tag dragon, scales;

        [SetUp]
        public void Tags()
        {
            dragon = Make.NewTag("Test.FN14.Set.Dragon");
            scales = Make.NewTag("Test.FN14.Set.Scales");
        }

        // A potion: its ability a quick sip, its uses counted
        private Item Potion(int uses, int stackSize = 1)
        {
            var sip = Make.Ability("Sip", Make.Stage(new ProbeAbilityTask(World, "Sip")));
            var potion = Make.Item("Potion");
            potion.ActiveAbility = sip;
            potion.Definition.MaxStackSize = stackSize;
            potion.Components.Add(new UsesItemComponent { Uses = uses });
            return potion;
        }

        private async Cysharp.Threading.Tasks.UniTask Use(IGameplayAbilitySystem gas, Ability ability, int done)
        {
            Assert.IsTrue(gas.GetAbilitySystem().TryActivateAbility(ability, out var refusal, null, EAbilityActivationPolicyExtended.AlwaysActivate),
                $"it's used: {refusal}");
            await Frames.Until(() => Ledger.Cleans("Sip") == done, 20, "the use ends");
            await Frames.Step(2);
        }

        /// <summary>Its Uses count each use of its ability, and with its last, the item goes.</summary>
        [UnityTest]
        public IEnumerator FN_14_An_items_uses_run_out_and_it_goes([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, Make.Set(), "Holder");
            var potion = Potion(2);
            await Frames.Step();
            Assert.IsTrue(gas.GetItemSystem().GiveAndEquipItem(potion, out _), "the potion's given and equipped");

            await Use(gas, potion.ActiveAbility, 1);
            Assert.IsTrue(gas.GetItemSystem().TryGetItemContainer(potion, out var held), "one use, and it's still held");
            Assert.AreEqual(1, ((UsesItemComponent)potion.Components[0]).Left(held), "with one use left");

            await Use(gas, potion.ActiveAbility, 2);
            Assert.IsFalse(gas.GetItemSystem().TryGetItemContainer(potion, out _), "its last use, and it goes");
            Assert.IsFalse(gas.GetAbilitySystem().TryGetAbilityContainer(potion.ActiveAbility, out _), "with its ability");
        });

        /// <summary>A stack of an item spends its uses a stack at a time, each stack's uses whole for the next.</summary>
        [UnityTest]
        public IEnumerator FN_14_A_stacks_uses_run_out_a_stack_at_a_time([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, Make.Set(), "Holder");
            var potion = Potion(1, stackSize: 3);
            await Frames.Step();
            Assert.IsTrue(gas.GetItemSystem().GiveAndEquipItem(potion, out _), "the potion's given and equipped");
            Assert.AreEqual(1, gas.GetItemSystem().GiveItem(potion, 1), "and another onto its stack");
            Assert.IsTrue(gas.GetItemSystem().TryGetItemContainer(potion, out var held));
            Assert.AreEqual(2, held.ItemStacks, "two of it");

            await Use(gas, potion.ActiveAbility, 1);
            Assert.AreEqual(1, held.ItemStacks, "its one use spent, a stack goes");
            await Use(gas, potion.ActiveAbility, 2);
            Assert.IsFalse(gas.GetItemSystem().TryGetItemContainer(potion, out _), "and with the last, the item");
        });

        // A piece of the Dragon set, whose bonus, at two pieces, is Scales
        private (Item Piece, SetBonusItemComponent Bonus) Piece(string name, GameplayEffect bonus)
        {
            var piece = Make.Item(name);
            var component = new SetBonusItemComponent { Set = dragon, Pieces = 2, Bonus = bonus };
            piece.Components.Add(component);
            return (piece, component);
        }

        private static int Bonuses(IGameplayAbilitySystem gas, GameplayEffect bonus) =>
            gas.GetEffectSystem().Shelf.Count(container => container.Spec.Base == bonus);

        /// <summary>
        /// A set's bonus holds while its holder has as many of its pieces equipped as it needs, once however many carry it,
        /// and through a piece's leaving while enough stay; with too few, it comes off.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_set_bonus_holds_while_enough_pieces_are_equipped([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, Make.Set(), "Holder");
            var bonus = Make.Effect("Scales", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { scales } });
            var (helm, _) = Piece("Helm", bonus);
            var (plate, _) = Piece("Plate", bonus);
            var (boots, _) = Piece("Boots", bonus);
            var items = gas.GetItemSystem();
            await Frames.Step();

            Assert.IsTrue(items.GiveAndEquipItem(helm, out _), "the helm");
            await Frames.Step();
            Assert.AreEqual(0, Bonuses(gas, bonus), "one piece, no bonus");

            Assert.IsTrue(items.GiveAndEquipItem(plate, out _), "and the plate");
            await Frames.Step();
            Assert.AreEqual(1, Bonuses(gas, bonus), "two pieces, the bonus");
            Assert.IsTrue(gas.GetTagCache().GetWeight(scales) > 0, "which grants its tag");

            Assert.IsTrue(items.GiveAndEquipItem(boots, out _), "and the boots");
            await Frames.Step();
            Assert.AreEqual(1, Bonuses(gas, bonus), "three, the bonus once");

            Assert.IsTrue(items.UnequipItem(helm), "the helm off");
            await Frames.Step();
            Assert.AreEqual(1, Bonuses(gas, bonus), "two stay, and so does the bonus, whichever piece it came from");

            Assert.IsTrue(items.UnequipItem(plate), "the plate off");
            await Frames.Step();
            Assert.AreEqual(0, Bonuses(gas, bonus), "one left, the bonus comes off");
            Assert.IsFalse(gas.GetTagCache().GetWeight(scales) > 0, "with its tag");
        });

        /// <summary>
        /// A load puts a set's bonus back once, as the save had it, its pieces worn again weighed with it, and the bonus it
        /// put back comes off as the pieces fall short.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_load_puts_a_set_bonus_back_once([Values] Hub hub) => Live(async () =>
        {
            var bonus = Make.Effect("Scales", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { scales } });
            var (helm, _) = Piece("Helm", bonus);
            var (plate, _) = Piece("Plate", bonus);
            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            Assert.IsTrue(catalog.Add("scales", bonus) && catalog.Add("helm", helm) && catalog.Add("plate", plate), "the catalog names each");
            var hero = World.Entity(hub, Make.Set(), "Hero");
            await Frames.Step();
            Assert.IsTrue(hero.GetItemSystem().GiveAndEquipItem(helm, out _) && hero.GetItemSystem().GiveAndEquipItem(plate, out _), "two pieces worn");
            await Frames.Step();
            Assert.AreEqual(1, Bonuses(hero, bonus), "the bonus once");
            var snapshot = ForgeSave.Capture(hero, catalog);
            Assert.IsNotNull(snapshot, "the hero can be saved");

            var twin = World.Entity(hub, Make.Set(), "Twin");
            await Frames.Step();
            Assert.IsTrue(ForgeSave.Restore(twin, snapshot, catalog), "the save loads onto a fresh entity");
            await Frames.Step();
            Assert.AreEqual(1, Bonuses(twin, bonus), "the bonus once, not again as its pieces are worn again");

            Assert.IsTrue(twin.GetItemSystem().UnequipItem(helm), "a piece off");
            await Frames.Step();
            Assert.AreEqual(0, Bonuses(twin, bonus), "and the bonus the load put back comes off, the pieces short");
        });
    }
}
