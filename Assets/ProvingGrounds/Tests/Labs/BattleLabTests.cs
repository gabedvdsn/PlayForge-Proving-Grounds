using System.Collections;
using System.Globalization;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Battle lab's bot. It lets the battle play out, as the fighters' own minds fight it, and checks it was a battle:
    /// long enough to watch, won by one side, with every kind of blow landing on the way. Its scene plays it to watch.
    /// </summary>
    [Category("Lab")]
    public class BattleLabTests : ProvingGroundsTest
    {
        public const int Seed = 7;
        public const float MostSeconds = 150f;

        /// <summary>
        /// A battle, eight a side, fought to the end. It lasts at least 15 seconds and one side stands; on the way shots fly,
        /// fireballs burst and novas hit over an area, blows knock fighters back, bashes stun, imps are summoned, and clerics
        /// heal and bless. Its seed's battle is the same each time; watched in its scene, each battle is a new one.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_battle() => Live(async () =>
        {
            int seed = LabScene.Watching ? System.Environment.TickCount : Seed;
            using var battle = new BattleLab(World, Make, seed);
            while (!battle.Over && battle.Elapsed < MostSeconds)
            {
                battle.Step(ForgeTime.DeltaTime);
                await Frames.Step();
            }

            UnityEngine.Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[Battle] Seed {12}: {0} after {1:0.0} s, {2} of Blue and {3} of Red standing: {4} shots, {5} area hits, {6} knockbacks, " +
                "{7} stuns, {8} summons, {9} heals, {10} blessings, {11} deaths",
                battle.Winner switch { 0 => "Blue won", 1 => "Red won", _ => "No winner" }, battle.Elapsed, battle.Standing(0), battle.Standing(1),
                battle.Shots, battle.AreaHits, battle.Knockbacks, battle.Stuns, battle.Summons, battle.Heals, battle.Blessings, battle.Deaths, seed));

            Assert.IsTrue(battle.Over, $"one side stands within {MostSeconds} s");
            Assert.That(battle.Elapsed, Is.GreaterThanOrEqualTo(15f), "a battle to watch: at least 15 s");
            Assert.That(battle.Winner, Is.Not.EqualTo(-1), "won by one side");
            Assert.That(battle.Shots, Is.GreaterThanOrEqualTo(30), "shots flew");
            Assert.That(battle.AreaHits, Is.GreaterThanOrEqualTo(6), "fireballs burst and novas hit over an area");
            Assert.That(battle.Knockbacks, Is.GreaterThanOrEqualTo(8), "blows knocked fighters back");
            Assert.That(battle.Stuns, Is.GreaterThanOrEqualTo(3), "bashes stunned");
            Assert.That(battle.Summons, Is.GreaterThanOrEqualTo(2), "imps were summoned");
            Assert.That(battle.Heals, Is.GreaterThanOrEqualTo(3), "clerics healed");
            Assert.That(battle.Blessings, Is.GreaterThanOrEqualTo(2), "and blessed");
            Assert.That(battle.Deaths, Is.GreaterThanOrEqualTo(8), "and a side fell");
        });
    }
}
