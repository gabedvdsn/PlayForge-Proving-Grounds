using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class WorkerTests : ProvingGroundsTest
    {
        /// <summary>A set's workers were given to the GAS by the set and again by the GAS, so each ran twice.</summary>
        [UnityTest]
        public IEnumerator GAS_18_A_sets_impact_worker_runs_once_per_impact([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            set.WorkerGroup.ImpactWorkers.Add(new ProbeImpactWorker("Watcher", health, Faults));

            var source = World.Entity(hub, set, "Source");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });

            World.Apply(source, target, hit);
            await Frames.Step(2);

            Assert.That(Faults.Hits("Watcher.Inline"), Is.EqualTo(1));
        });

        /// <summary>Tag workers ticked in Update and again at the end of the frame, so at twice their rate.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_19_A_tag_worker_ticks_once_a_frame([Values] Hub hub) => Live(async () =>
        {
            var set = Make.Set(Make.Element(Make.Attribute("Unused"), 1f, 1f));
            set.WorkerGroup.TagWorkers.Add(new ProbeTagWorker("Ticker", Faults));
            World.Entity(hub, set);
            await Frames.Step(3);

            int before = Faults.Hits("Ticker.Tick");
            await Frames.Step(10);

            Assert.That(Faults.Hits("Ticker.Tick") - before, Is.EqualTo(10));
        });
    }
}
