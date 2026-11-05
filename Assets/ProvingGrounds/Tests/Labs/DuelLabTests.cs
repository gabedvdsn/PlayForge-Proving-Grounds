using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Duel lab's bot. It plays the lab's rounds as fighters would, under a live ProcessControl and the lab's clock of
    /// 1/64 s a frame, and checks what each leaves; the harness checks its invariants as each ends. Phase 5's gate is every
    /// lab green.
    /// </summary>
    [Category("Lab")]
    public class DuelLabTests : ProvingGroundsTest
    {
        private DuelLab Lab(Hub hub) => new(World, Make, hub);

        // The lab's clock for the round, and the game's back after it
        private sealed class LabTime : IDisposable
        {
            private readonly IForgeClock previous = ForgeTime.Use(new DuelClock());
            public void Dispose() => ForgeTime.Use(previous);
        }

        /// <summary>A jab strikes once its startup is over, not before: the other fighter is whole for its first frames.</summary>
        [UnityTest]
        public IEnumerator Lab_A_jab_strikes_after_its_startup([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Jab), "Kestrel jabs");
            await Frames.Step(DuelLab.JabStartup - 1);
            Assert.AreEqual(1000f, lab.Value(lab.Brand), "and in its startup Brand is whole");
            await Frames.Until(() => lab.Value(lab.Brand) < 1000f, 4, "until the active frames strike");
            Assert.AreEqual(1000f - DuelLab.JabDamage, lab.Value(lab.Brand), "for the jab's damage");
        });

        /// <summary>
        /// A hit stops both fighters' time for the move's hit-stop (FEAT-7): their clocks stand at 0, and then go on. A jab
        /// that hits lasts its hit-stop longer than its frame data.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_hit_stops_both_fighters([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Jab), "Kestrel jabs");
            await Frames.Until(() => lab.Value(lab.Brand) < 1000f, DuelLab.JabStartup + 4, "the jab hits");
            await Frames.Step(2);
            Assert.AreEqual(0f, lab.Kestrel.GetClock().TimeScale, "Kestrel's time stops");
            Assert.AreEqual(0f, lab.Brand.GetClock().TimeScale, "and Brand's");

            int frames = 0;
            while (lab.Doing(lab.Kestrel, lab.Jab) && frames < 60)
            {
                await Frames.Step();
                frames++;
            }
            Assert.AreEqual(1f, lab.Kestrel.GetClock().TimeScale, "both go on after it");
            Assert.AreEqual(1f, lab.Brand.GetClock().TimeScale);
            Assert.That(frames, Is.GreaterThanOrEqualTo(DuelLab.JabHitStop - 3 + DuelLab.JabHitRecovery),
                "and the jab ran its hit-stop and its hit recovery");
        });

        /// <summary>
        /// A guarded jab misses, and is committed to its whiff (TS-7): the claim holds through it, so a fireball pressed in
        /// it waits in the buffer instead of firing, and no hit-stop comes. Pressed late in the whiff, the fireball is still
        /// in its window as the claim goes, and fires then (FEAT-6).
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_whiff_commits_the_jab([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(World.Apply(lab.Brand, lab.Brand, lab.Guard), "Brand guards");
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Jab), "Kestrel jabs");
            await Frames.Step(DuelLab.JabStartup + DuelLab.JabActive + 2);
            Assert.AreEqual(1000f, lab.Value(lab.Brand), "the guard takes it");
            Assert.AreEqual(1f, lab.Kestrel.GetClock().TimeScale, "with no hit-stop");
            Assert.IsTrue(lab.Kestrel.GetAbilitySystem().IsClaiming, "and Kestrel is committed to the whiff");
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Fireball), "so a fireball pressed in it");
            Assert.IsTrue(lab.Kestrel.GetAbilitySystem().IsBuffered(IndexOf(lab.Kestrel, lab.Fireball)), "waits in the buffer");
            Assert.IsFalse(lab.Doing(lab.Kestrel, lab.Fireball), "instead of firing");

            await Frames.Until(() => !lab.Doing(lab.Kestrel, lab.Jab), DuelLab.JabWhiff + 4, "the whiff ends");
            await Frames.Until(() => lab.Doing(lab.Kestrel, lab.Fireball), 3, "and the fireball, still in its window, fires as the claim goes");
        });

        /// <summary>
        /// A fireball pressed in a jab's startup waits in the buffer (FEAT-6). On a hit it fires as the jab's hit recovery
        /// starts, which a special cancels (FEAT-5): a hit-confirm, and both land. The buffer's window waited out the
        /// hit-stop, as Kestrel's time stood still.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_buffered_fireball_confirms_a_hit([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Jab), "Kestrel jabs");
            await Frames.Step();
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Fireball), "and presses a fireball in the startup, which waits");
            Assert.IsTrue(lab.Kestrel.GetAbilitySystem().IsBuffered(IndexOf(lab.Kestrel, lab.Fireball)), "in the buffer");

            await Frames.Until(() => lab.Doing(lab.Kestrel, lab.Fireball), 40, "the fireball fires");
            Assert.IsFalse(lab.Doing(lab.Kestrel, lab.Jab), "cancelling the jab's hit recovery");
            await Frames.Until(() => lab.Value(lab.Brand) <= 1000f - DuelLab.JabDamage - DuelLab.SpecialDamage, 40, "and it lands");
            Assert.AreEqual(1000f - DuelLab.JabDamage - DuelLab.SpecialDamage, lab.Value(lab.Brand), "the jab, then the fireball");
        });

        /// <summary>On a miss the jab's whiff outlasts the fireball's window, so the buffered press runs out and nothing fires (FEAT-6).</summary>
        [UnityTest]
        public IEnumerator Lab_A_buffered_fireball_runs_out_on_a_whiff([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(World.Apply(lab.Brand, lab.Brand, lab.Guard), "Brand guards");
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Jab), "Kestrel jabs");
            await Frames.Step();
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Fireball), "and presses a fireball in the startup");

            await Frames.Until(() => !lab.Doing(lab.Kestrel, lab.Jab), 40, "the jab whiffs and recovers");
            await Frames.Step(2);
            Assert.IsFalse(lab.Doing(lab.Kestrel, lab.Fireball), "and the fireball's 12 frames ran out first");
            Assert.IsFalse(lab.Kestrel.GetAbilitySystem().IsBuffered(IndexOf(lab.Kestrel, lab.Fireball)), "so it's gone from the buffer");
        });

        /// <summary>
        /// A jab pressed in a jab's hit recovery waits for the recovery, which blocks normals, and then fires: the recovery
        /// isn't cut short by a normal, and the second jab follows the first (FEAT-5, FEAT-6).
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_jab_waits_out_the_hit_recovery([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Jab), "Kestrel jabs");
            await Frames.Until(() => lab.Value(lab.Brand) < 1000f, DuelLab.JabStartup + 4, "it hits");
            await Frames.Until(() => lab.Kestrel.GetClock().TimeScale > 0f, DuelLab.JabHitStop + 4, "the hit-stop ends");
            Assert.IsTrue(lab.Press(lab.Kestrel, lab.Jab), "Kestrel presses a jab in the hit recovery");
            Assert.AreEqual(1000f - DuelLab.JabDamage, lab.Value(lab.Brand), "which doesn't strike yet");

            await Frames.Until(() => lab.Value(lab.Brand) <= 1000f - 2 * DuelLab.JabDamage, 40, "it strikes once the recovery is over");
        });

        /// <summary>
        /// A round: each fighter's bot presses a jab when it can, and a fireball after a jab that hits; a fighter guards
        /// now and then. The round plays out, both fighters take damage, and both stand.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_round([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            int presses = 0;
            for (int frame = 0; frame < 320; frame++)
            {
                foreach (var fighter in new[] { lab.Kestrel, lab.Brand })
                {
                    var other = lab.Other(fighter);
                    bool guarding = lab.Doing(fighter, lab.Jab) == false && frame % 96 == (fighter == lab.Kestrel ? 0 : 48);
                    if (guarding) World.Apply(fighter, fighter, lab.Guard);
                    if (frame % 96 == (fighter == lab.Kestrel ? 24 : 72)) fighter.RemoveGameplayEffect(lab.Guard);
                    if (!lab.Busy(fighter) && frame % 8 == (fighter == lab.Kestrel ? 0 : 4) && lab.Press(fighter, lab.Jab)) presses++;
                    if (lab.Doing(fighter, lab.Jab) && frame % 8 == (fighter == lab.Kestrel ? 2 : 6)) lab.Press(fighter, lab.Fireball);
                }
                await Frames.Step();
                Assert.That(lab.Value(lab.Kestrel), Is.GreaterThan(0f), $"Kestrel stands at frame {frame}");
                Assert.That(lab.Value(lab.Brand), Is.GreaterThan(0f), $"and Brand");
            }

            Assert.That(presses, Is.GreaterThan(10), "the fighters jabbed");
            Assert.That(lab.Value(lab.Kestrel), Is.LessThan(1000f), "and Kestrel took hits");
            Assert.That(lab.Value(lab.Brand), Is.LessThan(1000f), "and Brand did");

            // The round can end in a hit-stop, so the fighters finish what they're doing first
            await Frames.Until(() => !lab.Busy(lab.Kestrel) && !lab.Busy(lab.Brand), 120, "the fighters settle");
            Assert.AreEqual(1f, lab.Kestrel.GetClock().TimeScale, "and no hit-stop is left standing");
            Assert.AreEqual(1f, lab.Brand.GetClock().TimeScale);
        });

        private static int IndexOf(IGameplayAbilitySystem fighter, Ability move) =>
            fighter.GetAbilitySystem().TryGetAbilityContainer(move, out var container) ? container.Index : -1;
    }
}
