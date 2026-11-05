using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Status-effect lab's bot. It plays the lab's rounds as a player would, under a live ProcessControl, and checks
    /// what each round leaves; the harness checks its invariants, dealt against received among them, as each ends. Phase
    /// 5's gate is every lab green.
    /// </summary>
    [Category("Lab")]
    public class StatusEffectLabTests : ProvingGroundsTest
    {
        private StatusEffectLab Lab(Hub hub) => new(World, Make, hub);

        /// <summary>Poison's stacks share one duration, and each tick deals once a stack: three stacks deal 2 × 3 a tick, four times.</summary>
        [UnityTest]
        public IEnumerator Lab_Poison_stacks_share_one_duration([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            for (int i = 0; i < 3; i++) Assert.IsTrue(lab.Cast(World, lab.Poison), "the poison lands");
            Assert.AreEqual(3, lab.Stacks(lab.Poison), "in three stacks");

            await Frames.Until(() => !lab.Has(lab.Poison), 12, "the poison runs out");
            Assert.AreEqual(100f - 2f * 3 * 4, lab.Value(lab.Health), "its four ticks each dealt once a stack");
        });

        /// <summary>Burn's stacks keep their own durations: a stack cast a second later outlasts the first.</summary>
        [UnityTest]
        public IEnumerator Lab_Burn_stacks_keep_their_own_durations([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(World, lab.Burn), "the burn lands");
            await Frames.Step();
            Assert.IsTrue(lab.Cast(World, lab.Burn), "and again a second later");
            Assert.AreEqual(2, lab.Stacks(lab.Burn), "in two stacks");

            await Frames.Until(() => lab.Stacks(lab.Burn) < 2, 6, "the first stack runs out");
            Assert.IsTrue(lab.Has(lab.Burn), "while the second burns on");
            await Frames.Until(() => !lab.Has(lab.Burn), 6, "and then it runs out too");
            Assert.AreEqual(100f - 3f * 2 * 2, lab.Value(lab.Health), "each stack dealt its two ticks");
        });

        /// <summary>Bleed's stacks run one after another, and each tick deals once a stack still to run.</summary>
        [UnityTest]
        public IEnumerator Lab_Bleed_stacks_run_one_after_another([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(World, lab.Bleed) && lab.Cast(World, lab.Bleed), "two bleeds land");
            Assert.AreEqual(2, lab.Stacks(lab.Bleed), "in two stacks");

            await Frames.Until(() => lab.Stacks(lab.Bleed) < 2, 6, "the first stack runs out");
            Assert.IsTrue(lab.Has(lab.Bleed), "and the second runs after it");
            await Frames.Until(() => !lab.Has(lab.Bleed), 6, "until it runs out too");
            Assert.AreEqual(100f - (2f * 2 + 1f * 2), lab.Value(lab.Health), "two ticks of both, then two of the second");
        });

        /// <summary>A cleanse takes off every debuff at once, and the dummy stops losing health (FEAT-8).</summary>
        [UnityTest]
        public IEnumerator Lab_A_cleanse_takes_off_every_debuff([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(World, lab.Poison) && lab.Cast(World, lab.Burn) && lab.Cast(World, lab.Bleed), "the debuffs land");
            await Frames.Step();

            Assert.IsTrue(lab.Cast(World, lab.Cleanse), "the cleanse lands");
            await Frames.Step(2);
            Assert.IsFalse(lab.Has(lab.Poison) || lab.Has(lab.Burn) || lab.Has(lab.Bleed), "it took off every debuff");

            float health = lab.Value(lab.Health);
            await Frames.Step(3);
            Assert.AreEqual(health, lab.Value(lab.Health), "and the dummy loses no more health");
        });

        /// <summary>The antidote makes the dummy immune to poison while it lasts, and to nothing else (FEAT-8).</summary>
        [UnityTest]
        public IEnumerator Lab_The_antidote_makes_the_dummy_immune_to_poison([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(World, lab.Antidote), "the antidote lands");
            Assert.IsFalse(lab.Cast(World, lab.Poison), "the poison doesn't");
            Assert.IsTrue(lab.Cast(World, lab.Burn), "a burn does");

            await Frames.Until(() => !lab.Has(lab.Antidote), 10, "the antidote wears off");
            Assert.IsTrue(lab.Cast(World, lab.Poison), "and then the poison lands");
        });

        /// <summary>The barrier's shield takes damage before health, and what it can't take reaches health.</summary>
        [UnityTest]
        public IEnumerator Lab_The_shield_takes_damage_before_health([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(World, lab.Barrier), "the barrier lands");
            Assert.AreEqual(30f, lab.Value(lab.Shield), "with 30 shield");

            Assert.IsTrue(lab.Cast(World, lab.Strike), "a strike lands");
            await Frames.Step();
            Assert.AreEqual(100f, lab.Value(lab.Health), "the shield takes it all");
            Assert.AreEqual(10f, lab.Value(lab.Shield), "and has 10 left");

            Assert.IsTrue(lab.Cast(World, lab.Strike), "another strike lands");
            await Frames.Step();
            Assert.AreEqual(90f, lab.Value(lab.Health), "what the shield couldn't take reaches health");
            Assert.AreEqual(0f, lab.Value(lab.Shield), "and the shield is spent");
        });

        /// <summary>Two blows in one frame share what the shield has left: it never takes more than it holds.</summary>
        [UnityTest]
        public IEnumerator Lab_Two_blows_in_one_frame_share_the_shield([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(World, lab.Barrier), "the barrier lands");
            Assert.IsTrue(lab.Cast(World, lab.Strike) && lab.Cast(World, lab.Strike), "two strikes land in one frame");
            await Frames.Step();

            Assert.AreEqual(90f, lab.Value(lab.Health), "the shield took 30 of their 40");
            Assert.AreEqual(0f, lab.Value(lab.Shield), "and is spent");
        });

        /// <summary>
        /// A fight: each second the caster casts a debuff or a strike in turn, and the dummy's bot answers as a player would,
        /// with the barrier when its shield is spent, the antidote once poison lands, a cleanse when health runs low and a
        /// potion when it runs lower. The dummy lasts the fight, and every part of the lab plays.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_The_dummy_lasts_a_fight([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            var rotation = new[] { lab.Poison, lab.Burn, lab.Bleed, lab.Strike };
            int landed = 0, refused = 0, cleanses = 0, barriers = 0, potions = 0, lastPotion = -10;
            bool antidoted = false;
            for (int second = 0; second < 40; second++)
            {
                if (lab.Cast(World, rotation[second % rotation.Length])) landed++;
                else refused++;

                if (lab.Value(lab.Shield) <= 0f && second % 5 == 0 && lab.Cast(World, lab.Barrier)) barriers++;
                if (!antidoted && lab.Has(lab.Poison)) antidoted = lab.Cast(World, lab.Antidote);
                if (lab.Value(lab.Health) < 60f && lab.Cast(World, lab.Cleanse)) cleanses++;
                if (lab.Value(lab.Health) < 50f && second - lastPotion >= 6 && lab.Cast(World, lab.Potion))
                {
                    potions++;
                    lastPotion = second;
                }

                await Frames.Step();
                Assert.That(lab.Value(lab.Health), Is.GreaterThan(0f), $"the dummy is standing at {second} s");
                Assert.That(lab.Value(lab.Shield), Is.InRange(0f, 50f), "and its shield holds no more than its base");
            }

            Assert.IsTrue(antidoted, "the dummy drank the antidote");
            Assert.That(refused, Is.GreaterThan(0), "which refused a poison");
            Assert.That(barriers, Is.GreaterThan(1), "it raised the barrier more than once");
            Assert.That(cleanses, Is.GreaterThan(0), "and cleansed itself");
            Assert.That(potions, Is.GreaterThan(0), "and drank a potion");
            Assert.That(landed, Is.GreaterThan(30), "while the caster's casts landed");
        });
    }
}
