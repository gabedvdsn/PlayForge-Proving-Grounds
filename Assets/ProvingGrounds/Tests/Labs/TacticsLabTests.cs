using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Tactics lab's bot. It plays the lab's rounds a turn at a time, under a live ProcessControl and the lab's clock,
    /// which moves only as a turn ends, and checks what each leaves. Between turns it saves a squad and loads it back.
    /// Phase 5's gate is every lab green.
    /// </summary>
    [Category("Lab")]
    public class TacticsLabTests : ProvingGroundsTest
    {
        // The lab's clock for the round, and the game's back after it
        private sealed class LabTime : IDisposable
        {
            private readonly IForgeClock previous;
            public LabTime(IForgeClock clock) => previous = ForgeTime.Use(clock);
            public void Dispose() => ForgeTime.Use(previous);
        }

        // One battle, which every squad that plays it plays alike: who acts in each turn, with what, on whom
        private static readonly (int Turn, string Unit, string Ability, string Target)[] Battle =
        {
            (1, "Archer", "Poison Arrow", "Grub"), (1, "Knight", "Slash", "Grub"), (1, "Grub", "Slash", "Knight"), (1, "Snag", "Slash", "Archer"),
            (2, "Knight", "Guard", "Knight"), (2, "Archer", "Mend", "Knight"),
            (3, "Knight", "Slash", "Snag"), (3, "Archer", "Poison Arrow", "Snag"), (3, "Grub", "Slash", "Archer"), (3, "Snag", "Slash", "Knight"),
            (4, "Archer", "Mend", "Archer"), (4, "Knight", "Guard", "Knight"),
            (5, "Knight", "Slash", "Snag"), (5, "Grub", "Slash", "Knight"), (5, "Snag", "Slash", "Knight"), (5, "Archer", "Poison Arrow", "Grub"),
        };

        private const int LastTurn = 5;

        // A unit acts, and its ability runs to its end, which takes frames but no time
        private async UniTask Act(TacticsLab lab, IGameplayAbilitySystem unit, Ability ability, IGameplayAbilitySystem target)
        {
            Assert.IsTrue(lab.Use(unit, ability, target), $"{unit} uses {ability.GetName()}");
            await Frames.Until(() => !lab.Busy(unit), 20, $"{ability.GetName()} ends");
        }

        // The turn ends: the clock moves a whole turn in one frame, and stands still after it
        private async UniTask EndTurn(TacticsLab lab)
        {
            lab.Clock.Step = TacticsLab.Turn;
            await Frames.Step();
            lab.Clock.Step = 0f;
            await Frames.Step();
        }

        // A squad's actions in a turn of the battle
        private async UniTask Play(TacticsLab lab, Squad squad, int turn)
        {
            foreach (var (when, unit, ability, target) in Battle)
            {
                if (when == turn) await Act(lab, squad.Find(unit), lab.Named(ability), squad.Find(target));
            }
        }

        private static List<float> Healths(TacticsLab lab, Squad squad) => squad.Units.Select(u => lab.Value(u.Unit)).ToList();

        /// <summary>A poison lasts its turns: it bites only as each ends, however long a turn takes, and is gone after three.</summary>
        [UnityTest]
        public IEnumerator Lab_A_poison_lasts_its_turns([Values] Hub hub) => Live(async () =>
        {
            var lab = new TacticsLab(World, Make, hub);
            using var time = new LabTime(lab.Clock);
            var squad = lab.Muster("Squad");

            await Act(lab, squad.Archer, lab.PoisonArrow, squad.Grub);
            Assert.IsTrue(squad.Grub.TryGetEffectContainer(lab.Poison, out _), "the arrow poisons Grub");
            float start = lab.Value(squad.Grub);
            await Frames.Step(30);
            Assert.AreEqual(start, lab.Value(squad.Grub), "and while the turn goes on, however long, nothing bites");

            for (int turn = 1; turn <= TacticsLab.PoisonTurns; turn++)
            {
                await EndTurn(lab);
                Assert.AreEqual(start - turn * TacticsLab.PoisonDamage, lab.Value(squad.Grub), $"turn {turn} ends, and the poison bites");
            }
            Assert.IsFalse(squad.Grub.TryGetEffectContainer(lab.Poison, out _), "then it's over");
        });

        /// <summary>A cooldown counts turns: Slash comes back two turns after it's used, not before.</summary>
        [UnityTest]
        public IEnumerator Lab_A_cooldown_counts_turns([Values] Hub hub) => Live(async () =>
        {
            var lab = new TacticsLab(World, Make, hub);
            using var time = new LabTime(lab.Clock);
            var squad = lab.Muster("Squad");

            await Act(lab, squad.Knight, lab.Slash, squad.Grub);
            await Frames.Step(30);
            Assert.IsFalse(lab.Use(squad.Knight, lab.Slash, squad.Grub), "Slash is on cooldown for the rest of the turn");
            await EndTurn(lab);
            Assert.IsFalse(lab.Use(squad.Knight, lab.Slash, squad.Grub), "and the next");
            await EndTurn(lab);
            await Act(lab, squad.Knight, lab.Slash, squad.Grub);
            Assert.AreEqual(80f - 2 * TacticsLab.SlashDamage, lab.Value(squad.Grub), "two turns on, it's back");
        });

        /// <summary>A guard turns a slash aside for the rest of the turn, and is down as the turn ends.</summary>
        [UnityTest]
        public IEnumerator Lab_A_guard_turns_a_slash_aside_for_the_turn([Values] Hub hub) => Live(async () =>
        {
            var lab = new TacticsLab(World, Make, hub);
            using var time = new LabTime(lab.Clock);
            var squad = lab.Muster("Squad");

            await Act(lab, squad.Knight, lab.Guard, squad.Knight);
            await Act(lab, squad.Grub, lab.Slash, squad.Knight);
            Assert.AreEqual(100f, lab.Value(squad.Knight), "the guard turns Grub's slash aside");
            await EndTurn(lab);
            await Act(lab, squad.Snag, lab.Slash, squad.Knight);
            Assert.AreEqual(100f - TacticsLab.SlashDamage, lab.Value(squad.Knight), "with the turn it's down, and Snag's lands");
        });

        /// <summary>
        /// A load takes the battle back a turn (FEAT-9): every unit's health, the poison with its turns left and by the
        /// archer still, and the cooldowns. Played on from there, the battle goes as it did.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_load_takes_the_battle_back_a_turn([Values] Hub hub) => Live(async () =>
        {
            var lab = new TacticsLab(World, Make, hub);
            using var time = new LabTime(lab.Clock);
            var squad = lab.Muster("Squad");

            await Play(lab, squad, 1);
            await EndTurn(lab);
            var saved = squad.Save(lab.Catalog);
            var afterOne = Healths(lab, squad);
            await Play(lab, squad, 2);
            await EndTurn(lab);
            await Play(lab, squad, 3);
            await EndTurn(lab);
            var afterThree = Healths(lab, squad);
            CollectionAssert.AreNotEqual(afterOne, afterThree, "the battle goes on");

            Assert.IsTrue(squad.Load(saved, lab.Catalog), "the save loads");
            await Frames.Step();
            CollectionAssert.AreEqual(afterOne, Healths(lab, squad), "and every unit is as it was after the first turn");
            Assert.IsTrue(squad.Grub.TryGetEffectContainer(lab.Poison, out var poison), "Grub is poisoned still");
            Assert.AreSame(squad.Archer, poison.Spec.Source, "by the archer");
            Assert.AreEqual((TacticsLab.PoisonTurns - 1) * TacticsLab.Turn, poison.DurationRemaining, 0.001f, "with two turns to go");
            Assert.IsFalse(lab.Use(squad.Knight, lab.Slash, squad.Snag), "and the knight's Slash is on cooldown");

            await Play(lab, squad, 2);
            await EndTurn(lab);
            await Play(lab, squad, 3);
            await EndTurn(lab);
            CollectionAssert.AreEqual(afterThree, Healths(lab, squad), "played on, it goes as it did");
        });

        /// <summary>
        /// A save carries on in a new session: loaded onto a fresh squad, it plays on as the first squad does, turn for
        /// turn, and the poison on the fresh Grub is the fresh archer's (FEAT-9).
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_save_carries_on_in_a_new_session([Values] Hub hub) => Live(async () =>
        {
            var lab = new TacticsLab(World, Make, hub);
            using var time = new LabTime(lab.Clock);
            var first = lab.Muster("First");
            var second = lab.Muster("Second");

            await Play(lab, first, 1);
            await EndTurn(lab);
            await Play(lab, first, 2);
            await EndTurn(lab);

            Assert.IsTrue(second.Load(first.Save(lab.Catalog), lab.Catalog), "the first squad's save loads onto the second");
            await Frames.Step();
            CollectionAssert.AreEqual(Healths(lab, first), Healths(lab, second), "as it was");
            Assert.IsTrue(second.Grub.TryGetEffectContainer(lab.Poison, out var poison), "the second Grub is poisoned");
            Assert.AreSame(second.Archer, poison.Spec.Source, "by the second archer");

            for (int turn = 3; turn <= LastTurn; turn++)
            {
                await Play(lab, first, turn);
                await Play(lab, second, turn);
                await EndTurn(lab);
                CollectionAssert.AreEqual(Healths(lab, first), Healths(lab, second), $"turn {turn} goes as it does for the first");
            }
        });

        /// <summary>
        /// A battle: one squad plays it straight through, and another the same with a save and a load between every turn,
        /// through JSON, as a player who quits and comes back each turn. Each turn goes alike for both.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_battle_saved_and_loaded_every_turn([Values] Hub hub) => Live(async () =>
        {
            var lab = new TacticsLab(World, Make, hub);
            using var time = new LabTime(lab.Clock);
            var straight = lab.Muster("Straight");
            var reloaded = lab.Muster("Reloaded");
            var start = Healths(lab, straight);

            for (int turn = 1; turn <= LastTurn; turn++)
            {
                Assert.IsTrue(reloaded.Load(reloaded.Save(lab.Catalog), lab.Catalog), $"before turn {turn}, the save loads");
                await Play(lab, straight, turn);
                await Play(lab, reloaded, turn);
                await EndTurn(lab);
                CollectionAssert.AreEqual(Healths(lab, straight), Healths(lab, reloaded), $"turn {turn} goes alike");
            }
            CollectionAssert.AreNotEqual(start, Healths(lab, straight), "and the battle took its toll");
        });
    }
}
