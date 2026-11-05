using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Summoner lab's bot. It plays the lab's rounds under a live ProcessControl and the lab's clock of half a second a
    /// frame, and checks what each leaves. Phase 5's gate is every lab green.
    /// </summary>
    [Category("Lab")]
    public class SummonerLabTests : ProvingGroundsTest
    {
        private SummonerLab Lab(Hub hub) => new(World, Make, hub);

        // The lab's clock for the round, and the game's back after it
        private sealed class LabTime : IDisposable
        {
            private readonly IForgeClock previous = ForgeTime.Use(new SummonerClock());
            public void Dispose() => ForgeTime.Use(previous);
        }

        private static int FramesFor(float seconds) => (int)Math.Ceiling(seconds / SummonerLab.SecondsPerFrame);

        /// <summary>An imp lasts its lifetime: as its Summoned effect runs out, the imp ends, and its process with it.</summary>
        [UnityTest]
        public IEnumerator Lab_An_imp_lasts_its_lifetime([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            var imp = lab.Summon();
            Assert.IsNotNull(imp, "the summoner calls an imp");
            await Frames.Step(FramesFor(SummonerLab.Lifetime) - 2);
            Assert.IsTrue(lab.Present(imp), "which is there through its lifetime");
            await Frames.Until(() => !lab.Present(imp), 8, "and gone once it runs out");
        });

        /// <summary>
        /// The bond heals each imp while the summoner stands, and not while it kneels: its ticks ask the source, the
        /// summoner, for its Standing tag as each comes due.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_The_bond_heals_while_the_summoner_stands([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            var imp = lab.Summon();
            float start = lab.Value(imp);
            await Frames.Step(FramesFor(2f));
            float stood = lab.Value(imp);
            Assert.That(stood, Is.GreaterThan(start), "the standing summoner's bond heals the imp");

            lab.Kneel();
            await Frames.Step(1);
            float knelt = lab.Value(imp);
            await Frames.Step(FramesFor(2f));
            Assert.AreEqual(knelt, lab.Value(imp), "but not while the summoner kneels");

            lab.Rise();
            await Frames.Step(FramesFor(2f));
            Assert.That(lab.Value(imp), Is.GreaterThan(knelt), "and again once it rises");
        });

        /// <summary>Imps despawn as their summoner dies: each is a process the summoner handles, so each ends with it.</summary>
        [UnityTest]
        public IEnumerator Lab_Imps_despawn_as_their_summoner_dies([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            var imps = Enumerable.Range(0, 3).Select(_ => lab.Summon()).ToList();
            await Frames.Step();
            Assert.That(imps.All(lab.Present), "three imps are there");

            var doom = Make.Effect("Doom", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = lab.Health, Magnitude = -1000f });
            Assert.IsTrue(World.Apply(lab.Summoner, lab.Summoner, doom), "the summoner is struck down");
            await Frames.Until(() => imps.All(imp => !lab.Present(imp)), 6, "and its imps go with it");
        });

        /// <summary>
        /// A ritual: the summoner calls an imp each two seconds for ten, kneels in the middle and rises, then falls. Every imp
        /// comes and goes, and ProcessControl ends with the processes it began with, less the summoner.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_ritual([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = Lab(hub);
            await Frames.Step();
            int before = World.Control.Active;

            for (int frame = 0; frame < FramesFor(10f); frame++)
            {
                if (frame % FramesFor(2f) == 0) Assert.IsNotNull(lab.Summon(), "an imp comes");
                if (frame == FramesFor(4f)) lab.Kneel();
                if (frame == FramesFor(6f)) lab.Rise();
                await Frames.Step();
            }
            Assert.That(lab.Imps.Count(lab.Present), Is.GreaterThan(1), "several imps are there");

            var doom = Make.Effect("Doom", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = lab.Health, Magnitude = -1000f });
            World.Apply(lab.Summoner, lab.Summoner, doom);
            await Frames.Until(() => lab.Imps.All(imp => !lab.Present(imp)), 6, "the summoner falls and every imp goes");
            await Frames.Step(2);
            Assert.AreEqual(before - 1, World.Control.Active, "ProcessControl holds what it began with, less the summoner");
        });
    }
}
