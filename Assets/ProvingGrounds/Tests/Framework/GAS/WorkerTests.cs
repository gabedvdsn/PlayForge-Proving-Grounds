using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        [UnityTest]
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

        /// <summary>
        /// An instant effect's removal workers were handed an impact even when it made none: an empty one, on no
        /// attribute, so a worker that read the attribute threw.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_28_Removal_workers_get_no_impact_when_none_was_made([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var removal = new RemovalWatcher();
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));

            World.Apply(gas, gas, Make.Effect("Hit", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f,
                Workers = new List<AbstractEffectWorker> { removal },
            }));
            Assert.That(removal.Seen, Has.Count.EqualTo(1), "the hit's removal ran");
            Assert.That(removal.Seen[0].ImpactData?.Attribute, Is.SameAs(health), "with the impact it made");
            Assert.That(removal.Seen[0].Impacts.Select(impact => impact.Attribute), Is.EqualTo(new[] { health }), "which is every impact");

            World.Apply(gas, gas, Make.Effect("Utility", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = null,
                Workers = new List<AbstractEffectWorker> { removal },
            }));
            Assert.That(removal.Seen, Has.Count.EqualTo(2), "the utility's removal ran");
            Assert.That(removal.Seen[1].ImpactData.HasValue, Is.False, "with no impact, as it made none");
            Assert.That(removal.Seen[1].Impacts, Is.Empty, "in the list either");

            World.Apply(gas, gas, Make.Effect("Elsewhere", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = Make.Attribute("Mana"), Magnitude = -10f,
                Workers = new List<AbstractEffectWorker> { removal },
            }));
            Assert.That(removal.Seen, Has.Count.EqualTo(3), "the removal of a hit on an attribute the entity lacks ran");
            Assert.That(removal.Seen[2].ImpactData.HasValue, Is.False, "with no impact, as nothing committed");
            Assert.That(removal.Seen[2].Impacts, Is.Empty, "in the list either");

            await Frames.Step();
        });

        private sealed class RemovalWatcher : AbstractEffectWorker
        {
            public readonly List<EffectWorkerContext> Seen = new();

            public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx) => Array.Empty<IRootAction>();

            public override IEnumerable<IRootAction> OnEffectTick(EffectWorkerContext ctx) => Array.Empty<IRootAction>();

            public override IEnumerable<IRootAction> OnEffectRemoval(EffectWorkerContext ctx)
            {
                Seen.Add(ctx);
                return Array.Empty<IRootAction>();
            }

            public override IEnumerable<IRootAction> OnEffectImpact(EffectWorkerContext ctx) => Array.Empty<IRootAction>();
        }

        /// <summary>Nothing recorded an effect applied or removed, so analysis workers always found both lists empty.</summary>
        [UnityTest]
        public IEnumerator GAS_28_The_frame_summary_records_applied_and_removed_effects([Values] Hub hub) => Live(async () =>
        {
            var set = Make.Set(Make.Element(Make.Attribute("Unused"), 1f, 1f));
            var analyst = new EffectAnalyst();
            set.WorkerGroup.AnalysisWorkers.Add(analyst);
            var gas = World.Entity(hub, set);
            var buff = Make.Effect("Buff", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            await Frames.Step();

            World.Apply(gas, gas, buff);
            await Frames.Step(2);
            Assert.That(analyst.Applied, Is.EqualTo(new[] { buff }), "the application was recorded");

            gas.RemoveGameplayEffect(buff.GetAssetTag());
            await Frames.Step(2);
            Assert.That(analyst.Removed, Is.EqualTo(new[] { buff }), "and the removal");
        });

        private sealed class EffectAnalyst : AbstractAnalysisWorker
        {
            public readonly List<GameplayEffect> Applied = new();
            public readonly List<GameplayEffect> Removed = new();

            public override IEnumerable<IRootAction> Analyze(IGameplayAbilitySystem system, FrameSummary frameSummary)
            {
                Applied.AddRange(frameSummary.AppliedEffects.Select(spec => spec.Base));
                Removed.AddRange(frameSummary.RemovedEffects);
                return Array.Empty<IRootAction>();
            }
        }

        /// <summary>
        /// The rescale the system makes to keep Current in proportion to Base was typed AllowImpact, which passes every
        /// type filter, so a worker that takes unworkable impacts acted on it as if whoever changed Base had dealt it.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_35_A_rescale_reaches_no_typed_worker([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var fire = Make.NewTag("Test.Impact.Fire");
            var element = Make.Element(health, 100f, 100f);
            element.Constraints.AutoScaleWithBase = true;
            var set = Make.Set(element);
            set.WorkerGroup.ImpactWorkers.Add(new FireWorker(health, fire, Faults));

            var gas = World.Entity(hub, set);
            await Frames.Step();

            World.Apply(gas, gas, Make.Effect("Vigor", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = health, Slot = EAttributeTarget.Base, Magnitude = 50f,
            }));
            await Frames.Step(2);

            Assert.That(Current(gas, health), Is.EqualTo(150f), "the rescale still lands");
            Assert.That(Faults.Hits("Fire.Inline"), Is.EqualTo(0), "but a worker for fire doesn't see it");
        });

        /// <summary>The control for the test above: the worker does see a fire impact.</summary>
        [UnityTest]
        public IEnumerator A_typed_worker_sees_its_own_type([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var fire = Make.NewTag("Test.Impact.Fire");
            var set = Make.Set(Make.Element(health, 50f, 100f));
            set.WorkerGroup.ImpactWorkers.Add(new FireWorker(health, fire, Faults));
            var gas = World.Entity(hub, set);

            var burn = Make.Effect("Burn", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = 10f });
            burn.ImpactSpecification.ImpactTypes = new List<Tag> { fire };
            World.Apply(gas, gas, burn);
            await Frames.Step(2);

            Assert.That(Faults.Hits("Fire.Inline"), Is.EqualTo(1));
        });

        private sealed class FireWorker : AbstractContextImpactWorker
        {
            private readonly FaultInjector _faults;

            public override EWorkerExecution Execution => EWorkerExecution.Inline;

            public FireWorker(Attribute health, Tag fire, FaultInjector faults)
            {
                _faults = faults;
                ImpactedAttribute = health;
                AcceptUnworkableImpact = true;
                AnyImpactType = false;
                ImpactType = new List<Tag> { fire };
                ImpactTarget = EAttributeTargetExpanded.CurrentOrBase;
                ImpactSign = ESignPolicy.Positive;
                AllowSelfImpact = true;
                AnyContextTag = true;
                ImpactAbilityContextTags = new List<Tag>();
                WorkImpactType = new List<Tag>();
            }

            public override void ActivateInline(ImpactData impactData) => _faults.Trip("Fire.Inline");

            public override IEnumerable<IRootAction> CreateDeferredActions(ImpactWorkerContext ctx) => Array.Empty<IRootAction>();
        }

        /// <summary>
        /// A relative-operation worker set to InlineAndDeferred did its work in both halves, so lifesteal healed twice.
        /// Both halves do the same work, so it runs deferred.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_41_A_relative_worker_does_its_work_once([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var mana = Make.Attribute("Mana");
            var set = Make.Set(Make.Element(health, 100f, 100f), Make.Element(mana, 0f, 0f));
            set.WorkerGroup.ImpactWorkers.Add(new RelativeOperationContextImpactWorker
            {
                ImpactedAttribute = health, AnyImpactType = true, ImpactType = new List<Tag>(),
                ImpactTarget = EAttributeTargetExpanded.CurrentOrBase, ImpactSign = ESignPolicy.Negative,
                AnyContextTag = true, ImpactAbilityContextTags = new List<Tag>(),
                WorkAttribute = mana, WorkImpactType = new List<Tag>(), WorkSignPolicy = ESignPolicy.Positive,
                Operation = ECalculationOperation.Override, WithRespectTo = ESourceTarget.Source,
                ExecutionMode = EWorkerExecution.InlineAndDeferred,
            });

            var source = World.Entity(hub, set, "Source");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            World.Apply(source, target, Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f }));
            await Frames.Step(2);

            Assert.That(Current(source, mana), Is.EqualTo(100f), "the source's Health, taken once");
        });

        private static float Current(IGameplayAbilitySystem gas, Attribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }

        /// <summary>An impact worker set to run inline and deferred ran neither: the cache had no case for it.</summary>
        [UnityTest]
        public IEnumerator GAS_27_An_inline_and_deferred_impact_worker_runs_both([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            set.WorkerGroup.ImpactWorkers.Add(new ProbeImpactWorker("Both", health, Faults, EWorkerExecution.InlineAndDeferred));

            var source = World.Entity(hub, set, "Source");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            World.Apply(source, target, Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f }));
            await Frames.Step(2);

            Assert.That(Faults.Hits("Both.Inline"), Is.EqualTo(1), "the inline half");
            Assert.That(Faults.Hits("Both.Deferred"), Is.EqualTo(1), "and the deferred half");
        });

        /// <summary>
        /// A worker group's guard read "not found, or found one", which always held, so a GAS that didn't have its
        /// systems yet was handed its workers into nothing, and threw.
        /// </summary>
        [Test]
        public void GAS_27_A_GAS_without_its_systems_takes_no_workers()
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            set.WorkerGroup.AttributeWorkers.Add(new ProbeAttributeWorker("Attribute", health, Faults));
            set.WorkerGroup.ImpactWorkers.Add(new ProbeImpactWorker("Impact", health, Faults));

            var gas = World.NewMonoEntity(set);

            Assert.DoesNotThrow(() => set.InitWorkers(gas));
        }
    }
}
