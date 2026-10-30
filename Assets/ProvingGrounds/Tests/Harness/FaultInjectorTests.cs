using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class FaultInjectorTests : ProvingGroundsTest
    {
        [UnityTest]
        public IEnumerator An_unarmed_site_counts_and_stays_quiet() => Live(async () =>
        {
            Faults.Trip("site");
            Faults.Trip("site");

            Assert.That(Faults.Hits("site"), Is.EqualTo(2));
            Assert.That(Faults.Fired("site"), Is.EqualTo(0));

            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator A_fault_fires_on_its_hit_and_only_as_often_as_armed() => Live(async () =>
        {
            Faults.Arm("site", onHit: 2, times: 2, reported: false);

            Faults.Trip("site");
            Assert.Throws<InjectedFault>(() => Faults.Trip("site"));
            Assert.Throws<InjectedFault>(() => Faults.Trip("site"));
            Faults.Trip("site");

            Assert.That(Faults.Hits("site"), Is.EqualTo(4));
            Assert.That(Faults.Fired("site"), Is.EqualTo(2));

            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator A_disarmed_site_stays_quiet() => Live(async () =>
        {
            Faults.Arm("site", times: FaultInjector.Always, reported: false);
            Faults.Disarm("site");

            Faults.Trip("site");

            Assert.That(Faults.Fired("site"), Is.EqualTo(0));
            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator A_throwing_update_is_reported_and_stalls_nothing() => Live(async () =>
        {
            var broken = World.Probe("Broken", EProcessLifecycle.Synchronous);
            var after = World.Probe("After", EProcessLifecycle.Synchronous);
            Faults.Arm(broken.Recorder.Site(ProbeHook.Update), times: 3);

            await Frames.Step(5);

            Assert.That(Faults.Fired(broken.Recorder.Site(ProbeHook.Update)), Is.EqualTo(3));
            Assert.That(after.Recorder[ProbeHook.Update], Is.EqualTo(5), "the next process still steps");
        });

        [UnityTest]
        public IEnumerator A_mono_process_has_the_same_sites() => Live(async () =>
        {
            var template = World.MonoProbeTemplate("Broken Mono", EProcessLifecycle.Synchronous, EProcessStepTiming.UpdateAndLate);
            var clone = World.Spawn(template);
            string site = clone.Recorder.Site(ProbeHook.LateUpdate);
            Faults.Arm(site);

            await Frames.Step(2);

            Assert.That(Faults.Fired(site), Is.EqualTo(1));
            Assert.That(clone.Recorder[ProbeHook.LateUpdate], Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator A_callback_subscriber_can_throw() => Live(async () =>
        {
            var gas = World.MonoEntity();
            gas.Callbacks.OnFrameComplete += _ => Faults.Trip("frame complete");
            Faults.Arm("frame complete");

            await Frames.Step(2);

            Assert.That(Faults.Fired("frame complete"), Is.EqualTo(1));
            Assert.That(Faults.Hits("frame complete"), Is.EqualTo(2), "it comes back the next frame");
        });

        [UnityTest]
        public IEnumerator Every_worker_kind_passes_through_its_sites() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            set.WorkerGroup.AttributeWorkers.Add(new ProbeAttributeWorker("Attribute Worker", health, Faults));
            set.WorkerGroup.ImpactWorkers.Add(new ProbeImpactWorker("Impact Worker", health, Faults));
            set.WorkerGroup.AnalysisWorkers.Add(new ProbeAnalysisWorker("Analysis Worker", Faults));
            set.WorkerGroup.TagWorkers.Add(new ProbeTagWorker("Tag Worker", Faults));

            var gas = World.RuntimeEntity(set);
            var hit = Make.Effect("Hit", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f, Slot = EAttributeTarget.Current,
                Workers = new System.Collections.Generic.List<AbstractEffectWorker> { new ProbeEffectWorker("Effect Worker", Faults) },
            });

            World.Apply(gas, gas, hit);
            await Frames.Step(2);

            Assert.That(Faults.Hits("Attribute Worker.Intercept"), Is.GreaterThan(0), "attribute worker");
            Assert.That(Faults.Hits("Impact Worker.Inline"), Is.GreaterThan(0), "impact worker");
            Assert.That(Faults.Hits("Analysis Worker.Analyze"), Is.GreaterThan(0), "analysis worker");
            Assert.That(Faults.Hits("Tag Worker.Activate"), Is.GreaterThan(0), "tag worker");
            Assert.That(Faults.Hits("Effect Worker.Application"), Is.GreaterThan(0), "effect worker");
        });

        [UnityTest]
        public IEnumerator A_sequence_task_has_sites_too() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Sequence Task", frames: 1);
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Probe Sequence").Task(task).Build());

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);

            await Frames.Until(() => Ledger.Cleans("Sequence Task") == 1, 10, "the task is cleaned");

            Assert.That(Faults.Hits(task.Site(TaskHook.Prepare)), Is.EqualTo(1));
            Assert.That(Faults.Hits(task.Site(TaskHook.Execute)), Is.EqualTo(1));
            Assert.That(Faults.Hits(task.Site(TaskHook.Clean)), Is.EqualTo(1));
        });
    }
}
