using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// LAB-12: an effect's journal. Each application, refusal (by requirement, immunity or chance), tick, impact and
    /// removal, with its number's trace as it lands, and each worker that ran, by its hook and its side, with the actions
    /// it queued and whether they ran; recorded only while a journal listens. LAB-13: each hit's entry in the combat log
    /// carries its change's steps, as made, each attribute worker's change (before and after, inline or queued), and what
    /// landed (Decision 88).
    /// </summary>
    public class EffectJournalTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private readonly List<Object> made = new();
        private ForgeProject previous;
        private bool projectSet;

        [SetUp]
        public void Attributes() => health = Make.Attribute("Health");

        [TearDown]
        public void PutBackTheProject()
        {
            if (projectSet) ForgeProject.Use(previous);
            projectSet = false;
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) =>
            World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        // An action that does nothing, named for what queued it
        private sealed class Nothing : IRootAction
        {
            public Nothing(string description) => Description = description;
            public int Priority => 0;
            public bool IsValid => true;
            public string Description { get; }
            public void Execute(ISource system) { }
        }

        // An effect worker that queues one action as it hears each of its effect's moments, a tick only as one executes
        private sealed class Noting : AbstractEffectWorker
        {
            public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx) => Note("Applied");

            public override IEnumerable<IRootAction> OnEffectTick(EffectWorkerContext ctx) =>
                ctx.ExecuteTicks > 0 ? Note("Ticked") : Array.Empty<IRootAction>();

            public override IEnumerable<IRootAction> OnEffectRemoval(EffectWorkerContext ctx) => Note("Removed");

            public override IEnumerable<IRootAction> OnEffectImpact(EffectWorkerContext ctx) => Note("Hit");

            private static IEnumerable<IRootAction> Note(string what) => new IRootAction[] { new Nothing(what) };
        }

        // Sets one of an effect's lists of tags that name other effects, as an immunity's
        private static void Name(GameplayEffect effect, string list, params Tag[] tags)
        {
            var field = typeof(GameplayEffectTags).GetField(list);
            Assert.IsNotNull(field, $"an effect has {list}");
            object copy = effect.Tags;
            field.SetValue(copy, new List<Tag>(tags));
            effect.Tags = (GameplayEffectTags)copy;
        }

        /// <summary>
        /// An effect refused said nothing of why, so a journal couldn't tell a miss from an immunity (LAB-12). The entity
        /// it was refused by tells it and why: its chance missed, a requirement didn't hold, or an immunity named it.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_12_An_effect_refused_says_why([Values] Hub hub) => Live(async () =>
        {
            var gas = Fighter(hub, "Target");
            var refused = new List<(GameplayEffect effect, EEffectRefusal why)>();
            gas.GetCallbacks().OnEffectRefused += (spec, why) => refused.Add((spec.Base, why));
            await Frames.Step();

            var missed = Make.Effect("Missed", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
            missed.ImpactSpecification.ChanceToApply = new ScalableFloat(0f);
            Assert.IsFalse(World.Apply(gas, gas, missed), "a chance of none misses");

            var marked = Make.NewTag("Test.Status.Marked");
            var execute = Make.Effect("Execute", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -50f });
            execute.TargetRequirements.ApplicationRequirements = new AvoidRequireTagGroup
            {
                AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery> { TagQuery.HasTag(marked) },
            };
            Assert.IsFalse(World.Apply(gas, gas, execute), "an unmarked target refuses what needs the mark");

            var debuff = Make.NewTag("Test.Status.Debuff");
            var ward = Make.Effect("Ward", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });
            Name(ward, "BlocksEffectsTagged", debuff);
            var poison = Make.Effect("Poison", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });
            poison.Tags.ContextTags = new List<Tag> { debuff };
            Assert.IsTrue(World.Apply(gas, gas, ward), "the ward lands");
            await Frames.Step();
            Assert.IsFalse(World.Apply(gas, gas, poison), "and refuses the debuff it names");

            Assert.That(refused, Is.EqualTo(new[]
            {
                (missed, EEffectRefusal.Chance), (execute, EEffectRefusal.Requirements), (poison, EEffectRefusal.Immunity),
            }), "each refusal, and why");
        });

        /// <summary>
        /// Nothing kept an effect's life in one place, nor told which of its workers ran and what they queued (LAB-12).
        /// A journal listening to both sides records its application, with its number's trace, each tick, each impact and
        /// its removal; and each of its workers' runs, by hook and side, with the actions each queued and whether they
        /// ran. Let go, it records nothing more.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_12_A_journal_records_an_effects_life([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 2f, PeriodicTicks = true, Ticks = 2, Target = health,
                Magnitude = -5f, Workers = new List<AbstractEffectWorker> { new Noting() },
            });
            var journal = new EffectJournal();
            journal.Listen(target);
            journal.Listen(caster);
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, burn), "the burn lands");
            await Frames.Seconds(2.5f);

            var entries = journal.Of(burn).ToList();
            var applied = entries.Single(entry => entry.Kind == EEffectJournalKind.Applied);
            Assert.AreSame(target, applied.Entity, "it landed on the target");
            Assert.AreSame(caster, applied.Source, "from the caster");
            Assert.IsNotNull(applied.Trace, "with its number's trace");
            Assert.AreEqual(-5f, applied.Trace.Value, 0.01f, "which reads -5");

            Assert.That(entries.Count(entry => entry.Kind == EEffectJournalKind.Ticked), Is.EqualTo(2), "two ticks");
            var impacts = entries.Where(entry => entry.Kind == EEffectJournalKind.Impact).ToList();
            Assert.That(impacts.Select(entry => entry.Change.CurrentValue), Is.EqualTo(new[] { -5f, -5f }), "two hits of 5");
            Assert.That(impacts.Select(entry => entry.Attribute), Has.All.SameAs(health), "to Health");
            Assert.That(entries.Count(entry => entry.Kind == EEffectJournalKind.Removed), Is.EqualTo(1), "and its removal");

            var runs = entries.Where(entry => entry.Kind == EEffectJournalKind.Worker).ToList();
            Assert.That(runs.Select(run => run.Hook), Is.EquivalentTo(new[]
            {
                EEffectWorkerHook.Application, EEffectWorkerHook.Tick, EEffectWorkerHook.Tick, EEffectWorkerHook.Impact,
                EEffectWorkerHook.Impact, EEffectWorkerHook.Removal,
            }), "its worker heard it land, each tick, each hit and its end");
            Assert.That(runs.Where(run => run.Hook == EEffectWorkerHook.Impact).Select(run => run.Side),
                Has.All.EqualTo(EWorkerSide.Dealer), "each hit on the dealer's side, as its impact workers run");
            Assert.That(runs.Where(run => run.Hook != EEffectWorkerHook.Impact).Select(run => run.Side),
                Has.All.EqualTo(EWorkerSide.Target), "the rest on the target's");
            Assert.That(runs.Select(run => run.Actions.Count), Has.All.EqualTo(1), "each queuing one action");
            var first = runs.First(run => run.Hook == EEffectWorkerHook.Application).Actions[0];
            Assert.AreEqual("Applied", first.Description, "named as its worker named it");
            Assert.AreEqual(EJournalActionState.Ran, first.State, "and it ran");
            StringAssert.Contains("Burn", runs[0].ToString(), "a run reads as a line, its effect named");

            journal.Dispose();
            int written = journal.Entries.Count;
            Assert.IsTrue(World.Apply(caster, target, burn), "the burn lands again");
            await Frames.Step(2);
            Assert.AreEqual(written, journal.Entries.Count, "a journal let go records nothing more");
        });

        /// <summary>
        /// A journal hears the entities it listens to and no others, and keeps its last as many as it's told (LAB-12).
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_12_A_journal_hears_whom_it_listens_to_and_keeps_its_last([Values] Hub hub) => Live(async () =>
        {
            var heard = Fighter(hub, "Heard");
            var other = Fighter(hub, "Other");
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f });
            using var journal = new EffectJournal(keep: 4);
            journal.Listen(heard);
            await Frames.Step();

            Assert.IsTrue(World.Apply(other, other, blow), "a blow it doesn't hear");
            Assert.That(journal.Entries, Is.Empty, "nothing of an entity it doesn't listen to");

            for (int i = 0; i < 5; i++) Assert.IsTrue(World.Apply(heard, heard, blow));
            await Frames.Step();
            Assert.That(journal.Entries.Count, Is.EqualTo(4), "its last four");
            Assert.That(journal.Written, Is.GreaterThan(4), "of more written");
            Assert.That(journal.Entries.Select(entry => entry.Entity), Has.All.SameAs(heard), "all of the one it hears");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // LAB-13: a hit's steps in the combat log
        // ═══════════════════════════════════════════════════════════════════════════

        private void UseLog(ECombatLogRecords records)
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            made.Add(project);
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            project.Settings.CombatLog = new CombatLogSettings { Keep = 100, Records = records };
            var was = ForgeProject.Use(project);
            if (!projectSet) previous = was;
            projectSet = true;
        }

        // A pre-change worker that halves every loss to Health, as the target's armor
        private static MathAttributeWorker Halving(ForgeAttribute health, ForgeAttribute armor) => new()
        {
            TimingConfig = AbstractAttributeWorker.EChangeEventTiming.PreChange,
            TargetAttribute = health,
            AllowSelfModification = true,
            TargetModification = EAttributeTargetExpanded.Current,
            SignPolicy = ESignPolicyExtended.Negative,
            From = EFromSelfSource.FromSelf,
            RelativeTo = armor,
            Operation = ECalculationOperation.Multiply,
            OperationTarget = EAttributeTarget.Current,
            OperationPolicy = EMathApplicationPolicy.OneMinus,
            AnyImpactType = true,
            ImpactTypes = new List<Tag>(),
        };

        // A post-change worker that queues one action for each change it hears
        private sealed class Answering : AbstractAttributeWorker
        {
            private readonly IAttribute attribute;
            public Answering(IAttribute attribute) => this.attribute = attribute;
            public override EWorkerExecution Execution => EWorkerExecution.Deferred;
            public override EChangeEventTiming Timing => EChangeEventTiming.PostChange;
            public override bool PreValidateWorkFor(ChangeValue change) => true;
            public override bool ValidateWorkFor(WorkerContext ctx) => true;
            public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx) => new IRootAction[] { new Nothing("Answer") };
            public override bool RegisterWithHandler(AttributeChangeMomentHandler preChange, AttributeChangeMomentHandler postChange) =>
                postChange.AddWorker(attribute, this);
            public override bool DeRegisterFromHandler(AttributeChangeMomentHandler preChange, AttributeChangeMomentHandler postChange) =>
                postChange.RemoveWorker(attribute, this);
        }

        /// <summary>
        /// A hit's combat log entry told its pipeline's stages and nothing after, so a hit an armor halved read as if it
        /// were made at half (LAB-13). It carries its change as made, each attribute worker's change to it, before and
        /// after, inline or queued, and what landed; and the entity tells each step as it's taken, to whoever listens.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_13_A_hits_entry_carries_each_workers_change([Values] Hub hub) => Live(async () =>
        {
            UseLog(ECombatLogRecords.HitsTaken);
            var armor = Make.Attribute("Armor");
            var attacker = Fighter(hub, "Attacker");
            var target = World.Entity(hub, Make.Set(
                Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(armor, 0.5f, 0.5f)), "Target");
            var halving = Halving(health, armor);
            var answering = new Answering(health);
            target.GetAttributeSystem().ProvideWorker(halving);
            target.GetAttributeSystem().ProvideWorker(answering);
            var told = new List<ChangeStep>();
            target.GetCallbacks().OnAttributeChangeStep += (in ChangeStep step) => told.Add(step);
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f });
            await Frames.Step();

            Assert.IsTrue(World.Apply(attacker, target, blow));
            await Frames.Step();

            var taken = target.GetCombatLog().Entries.Single();
            Assert.AreEqual(-30f, taken.Made.CurrentValue, 0.01f, "made as -30");
            Assert.AreEqual(-15f, taken.Change.CurrentValue, 0.01f, "landed as -15");
            var steps = taken.ChangeSteps;
            Assert.That(steps.Count, Is.EqualTo(2), "the armor's step and the answer's");

            var halved = steps[0];
            Assert.AreSame(halving, halved.Worker, "the armor first, before the change");
            Assert.AreEqual(EChangeStepKind.Worker, halved.Kind);
            Assert.AreEqual(AbstractAttributeWorker.EChangeEventTiming.PreChange, halved.Timing);
            Assert.AreEqual(EWorkerExecution.Inline, halved.Execution, "inline");
            Assert.AreEqual(-30f, halved.Before.CurrentValue, 0.01f, "from -30");
            Assert.AreEqual(-15f, halved.After.CurrentValue, 0.01f, "to -15");

            var answer = steps[1];
            Assert.AreSame(answering, answer.Worker, "then the answer, after it");
            Assert.AreEqual(AbstractAttributeWorker.EChangeEventTiming.PostChange, answer.Timing);
            Assert.AreEqual(EWorkerExecution.Deferred, answer.Execution, "queued");
            Assert.AreEqual(1, answer.Queued, "one action");

            StringAssert.Contains("as made", taken.StepsText, "its steps read as a line");
            StringAssert.Contains("landed", taken.StepsText);
            Assert.That(told.Select(step => step.Worker), Is.EqualTo(new AbstractAttributeWorker[] { halving, answering }),
                "and the entity told each step as it was taken");
        });
    }
}
