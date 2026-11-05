using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The MOBA hero kit's bot. It plays the lab's rounds as players would, under a live ProcessControl and the lab's
    /// clock of half a second a frame, and checks what each round leaves; the harness checks its invariants as each
    /// ends. Phase 5's gate is every lab green.
    /// </summary>
    [Category("Lab")]
    public class MobaLabTests : ProvingGroundsTest
    {
        private MobaLab Lab(Hub hub) => new(World, Make, hub);

        // The lab's clock for the round, and the game's back after it
        private sealed class LabTime : IDisposable
        {
            private readonly IForgeClock previous = ForgeTime.Use(new MobaClock());
            public void Dispose() => ForgeTime.Use(previous);
        }

        // Frames for a span of the lab's time
        private static int FramesFor(float seconds) => (int)Math.Ceiling(seconds / MobaLab.SecondsPerFrame);

        /// <summary>A cast spends its mana and starts its cooldown, and the ability waits out the cooldown to be cast again.</summary>
        [UnityTest]
        public IEnumerator Lab_A_cast_spends_mana_and_starts_its_cooldown([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Firebolt, lab.Warden), "the Pyromancer casts Firebolt at the Warden");
            await Frames.Step();
            Assert.AreEqual(360f, lab.Value(lab.Pyromancer, lab.Mana), "spending its 40 mana");
            Assert.AreEqual(3f, lab.CooldownOf(lab.Pyromancer, lab.Firebolt), 1e-4f, "and going on cooldown for 3 s");
            Assert.AreEqual(720f, lab.Value(lab.Warden, lab.Health), "the Warden, with no magic resist, takes all 80");

            Assert.IsFalse(lab.Cast(lab.Pyromancer, lab.Firebolt, lab.Warden), "on cooldown, it can't be cast");
            await Frames.Until(() => lab.Ready(lab.Pyromancer, lab.Firebolt), FramesFor(3f) + 2, "until 3 s have passed");
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Firebolt, lab.Warden), "and it casts again");
        });

        /// <summary>A hero casts until its mana runs out: the Duelist's 200 pays for ten Lunges of 20, and no more.</summary>
        [UnityTest]
        public IEnumerator Lab_Mana_runs_out([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            int casts = 0;
            for (int frame = 0; frame < 40; frame++)
            {
                if (lab.Ready(lab.Duelist, lab.Lunge) && lab.Cast(lab.Duelist, lab.Lunge, lab.Pyromancer)) casts++;
                await Frames.Step();
            }

            Assert.AreEqual(10, casts, "ten Lunges");
            Assert.AreEqual(0f, lab.Value(lab.Duelist, lab.Mana), "spend all its mana");
            Assert.AreEqual(0f, lab.CooldownOf(lab.Duelist, lab.Lunge), "and though its cooldown is over");
            Assert.IsFalse(lab.Ready(lab.Duelist, lab.Lunge), "it can't pay for another");
            Assert.AreEqual(500f - 10 * 40f, lab.Value(lab.Pyromancer, lab.Health), "the Pyromancer, with no armor, took each 40");
        });

        /// <summary>Cooldown reduction shortens a cooldown by its share: none for the Warden, half for the Duelist, a fifth for the Oracle.</summary>
        [UnityTest]
        public IEnumerator Lab_Cooldown_reduction_shortens_cooldowns([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(lab.Warden, lab.Cleave, lab.Duelist), "the Warden cleaves");
            Assert.IsTrue(lab.Cast(lab.Duelist, lab.Lunge, lab.Warden), "the Duelist lunges");
            Assert.IsTrue(lab.Cast(lab.Oracle, lab.Smite, lab.Pyromancer), "the Oracle smites");
            await Frames.Step();

            Assert.AreEqual(4f, lab.CooldownOf(lab.Warden, lab.Cleave), 1e-4f, "Cleave's 4 s, with no reduction");
            Assert.AreEqual(1f, lab.CooldownOf(lab.Duelist, lab.Lunge), 1e-4f, "Lunge's 2 s, halved");
            Assert.AreEqual(2.4f, lab.CooldownOf(lab.Oracle, lab.Smite), 1e-4f, "Smite's 3 s, less a fifth");
        });

        /// <summary>Armor reduces a physical blow and magic resist a spell, each by 100 / (100 + resist).</summary>
        [UnityTest]
        public IEnumerator Lab_Armor_and_magic_resist_reduce_damage([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(lab.Duelist, lab.Lunge, lab.Warden), "a Lunge of 40 at the Warden");
            Assert.IsTrue(lab.Cast(lab.Warden, lab.Cleave, lab.Pyromancer), "a Cleave of 60 at the Pyromancer");
            Assert.IsTrue(lab.Cast(lab.Oracle, lab.Smite, lab.Pyromancer), "a Smite of 60 at the Pyromancer");
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Firebolt, lab.Duelist), "a Firebolt of 80 at the Duelist");
            await Frames.Step(2);

            Assert.AreEqual(800f - 20f, lab.Value(lab.Warden, lab.Health), "the Warden's 100 armor halves the Lunge");
            Assert.AreEqual(500f - 60f - 30f, lab.Value(lab.Pyromancer, lab.Health), "the Pyromancer takes all the Cleave, and half the Smite");
            Assert.AreEqual(600f - 64f + 5f, lab.Value(lab.Duelist, lab.Health),
                "the Duelist's 25 magic resist takes a fifth off the Firebolt, and its Lunge's 20 healed 5");
        });

        /// <summary>The Duelist heals by a quarter of the physical damage it deals, as it lands.</summary>
        [UnityTest]
        public IEnumerator Lab_Lifesteal_heals_the_duelist([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Firebolt, lab.Duelist), "a Firebolt hurts the Duelist");
            await Frames.Step(2);
            Assert.AreEqual(600f - 64f, lab.Value(lab.Duelist, lab.Health), "by 64");

            Assert.IsTrue(lab.Cast(lab.Duelist, lab.Lunge, lab.Pyromancer), "its Lunge deals 40 to the Pyromancer");
            await Frames.Step(2);
            Assert.AreEqual(600f - 64f + 10f, lab.Value(lab.Duelist, lab.Health), "and heals it 10");
            Assert.AreEqual(460f, lab.Value(lab.Pyromancer, lab.Health));

            await Frames.Until(() => lab.Ready(lab.Duelist, lab.Lunge), FramesFor(1f) + 2, "its cooldown is over");
            Assert.IsTrue(lab.Cast(lab.Duelist, lab.Lunge, lab.Warden), "a Lunge at the Warden deals 20 through its armor");
            await Frames.Step(2);
            Assert.AreEqual(600f - 64f + 10f + 5f, lab.Value(lab.Duelist, lab.Health), "and heals 5");
        });

        /// <summary>A silence stops a hero casting, and the Oracle's Purify lifts it.</summary>
        [UnityTest]
        public IEnumerator Lab_A_silence_stops_casting_and_a_cleanse_lifts_it([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Hush, lab.Duelist), "the Pyromancer silences the Duelist");
            await Frames.Step();
            Assert.IsTrue(lab.Has(lab.Duelist, lab.Silence), "who is silenced");
            Assert.IsFalse(lab.Ready(lab.Duelist, lab.Lunge), "and can't lunge");
            Assert.IsFalse(lab.Cast(lab.Duelist, lab.Lunge, lab.Pyromancer));

            Assert.IsTrue(lab.Cast(lab.Oracle, lab.Purify, lab.Duelist), "the Oracle purifies the Duelist");
            await Frames.Step(2);
            Assert.IsFalse(lab.Has(lab.Duelist, lab.Silence), "which lifts the silence");
            Assert.IsTrue(lab.Cast(lab.Duelist, lab.Lunge, lab.Pyromancer), "and the Duelist lunges");
        });

        /// <summary>
        /// The Inferno is a channel of three ticks a second apart. The Warden's Shield Bash stuns the Pyromancer after
        /// the first, which ends the channel through its While active group (FEAT-5): no more ticks land.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_stun_ends_the_channel([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Inferno, lab.Warden), "the Pyromancer channels the Inferno at the Warden");
            await Frames.Step();
            Assert.AreEqual(800f - 60f, lab.Value(lab.Warden, lab.Health), "whose first tick lands");
            Assert.IsTrue(lab.Casting(lab.Pyromancer, lab.Inferno), "and the channel goes on");

            Assert.IsTrue(lab.Cast(lab.Warden, lab.ShieldBash, lab.Pyromancer), "the Warden bashes the Pyromancer");
            await Frames.Until(() => !lab.Casting(lab.Pyromancer, lab.Inferno), 4, "and its stun ends the channel");
            Assert.IsTrue(lab.Has(lab.Pyromancer, lab.Stun), "while the Pyromancer is stunned");
            Assert.AreEqual(500f - 30f, lab.Value(lab.Pyromancer, lab.Health), "and took the bash's 30");

            await Frames.Step(FramesFor(3f));
            Assert.AreEqual(800f - 60f, lab.Value(lab.Warden, lab.Health), "no more ticks land");
        });

        /// <summary>
        /// While the Inferno runs, it blocks the Pyromancer's other spells, and a Blink, a move, cancels it (FEAT-5).
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_blink_cancels_the_channel_which_blocks_spells([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Inferno, lab.Warden), "the Pyromancer channels the Inferno");
            await Frames.Step();
            Assert.IsFalse(lab.Ready(lab.Pyromancer, lab.Firebolt), "which blocks its Firebolt, a spell");
            Assert.IsFalse(lab.Cast(lab.Pyromancer, lab.Firebolt, lab.Warden));

            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Blink), "a Blink, a move, isn't blocked");
            await Frames.Step();
            Assert.IsFalse(lab.Casting(lab.Pyromancer, lab.Inferno), "and cancels the channel");
            Assert.IsTrue(lab.Cast(lab.Pyromancer, lab.Firebolt, lab.Warden), "so the Firebolt can be cast");
        });

        /// <summary>
        /// Two on two for 8 seconds: the Warden and the Oracle against the Duelist and the Pyromancer, each hero's bot
        /// casting the first ability it's ready to, the Oracle healing whichever ally is lower. Every part of the kit plays,
        /// and everyone is standing at the end.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_Two_on_two([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            var casts = new Dictionary<IGameplayAbilitySystem, int>
            {
                [lab.Warden] = 0, [lab.Oracle] = 0, [lab.Duelist] = 0, [lab.Pyromancer] = 0,
            };
            int stuns = 0, silences = 0;

            bool Try(IGameplayAbilitySystem hero, Ability ability, IGameplayAbilitySystem target = null)
            {
                if (!lab.Ready(hero, ability) || !lab.Cast(hero, ability, target)) return false;
                casts[hero]++;
                if (ability == lab.ShieldBash) stuns++;
                if (ability == lab.Hush) silences++;
                return true;
            }

            for (int frame = 0; frame < FramesFor(8f); frame++)
            {
                var hurt = lab.Value(lab.Warden, lab.Health) / 800f < lab.Value(lab.Oracle, lab.Health) / 500f ? lab.Warden : lab.Oracle;
                _ = Try(lab.Warden, lab.ShieldBash, lab.Pyromancer) || Try(lab.Warden, lab.Cleave, lab.Duelist)
                    || Try(lab.Warden, lab.Bulwark) || Try(lab.Warden, lab.Rally);
                _ = Try(lab.Oracle, lab.Mend, hurt) || Try(lab.Oracle, lab.Smite, lab.Duelist) || Try(lab.Oracle, lab.Clarity, lab.Warden);
                if (lab.Has(lab.Warden, lab.Silence)) Try(lab.Oracle, lab.Purify, lab.Warden);
                _ = Try(lab.Duelist, lab.Execute, lab.Warden) || Try(lab.Duelist, lab.Flurry, lab.Warden) || Try(lab.Duelist, lab.Lunge, lab.Warden);
                _ = Try(lab.Pyromancer, lab.Hush, lab.Warden) || Try(lab.Pyromancer, lab.Firebolt, lab.Warden);

                await Frames.Step();
                foreach (var hero in casts.Keys)
                {
                    Assert.That(lab.Value(hero, lab.Health), Is.GreaterThan(0f), $"{hero.GetName()} is standing at frame {frame}");
                    Assert.That(lab.Value(hero, lab.Mana), Is.GreaterThanOrEqualTo(0f), "and its mana never goes below 0");
                }
            }

            foreach (var (hero, count) in casts) Assert.That(count, Is.GreaterThan(1), $"{hero.GetName()} cast more than once");
            Assert.That(stuns, Is.GreaterThan(0), "the Warden stunned the Pyromancer");
            Assert.That(silences, Is.GreaterThan(0), "and the Pyromancer silenced the Warden");
            Assert.That(lab.Value(lab.Warden, lab.Health), Is.LessThan(800f), "who took a beating");
        });
    }
}
