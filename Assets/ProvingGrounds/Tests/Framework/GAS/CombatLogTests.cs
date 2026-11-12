using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-22, Decision 62: each entity's combat log. The Forge Project says what it writes, the hits an entity takes and
    /// deals, its other attribute changes and the gameplay events it's sent by tag, and how many it keeps, its last; a hit
    /// carries its dealer, its effect, its attribute, the value before and after, its breakdown (TW-2), and whether it killed.
    /// </summary>
    public class CombatLogTests : ProvingGroundsTest
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

        // A project whose Health role is the test's, writing what's given
        private void UseLog(ECombatLogRecords records, int keep = 100, params Tag[] events)
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            made.Add(project);
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            project.Settings.CombatLog = new CombatLogSettings { Keep = keep, Records = records, Events = new List<Tag>(events) };
            var was = ForgeProject.Use(project);
            if (!projectSet) previous = was;
            projectSet = true;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) =>
            World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        private GameplayEffect Blow(float amount) =>
            Make.Effect($"Blow {amount}", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = amount });

        private static List<CombatLogEntry> Log(IGameplayAbilitySystem gas) => gas.GetCombatLog().Entries.ToList();

        /// <summary>A hit is written on both sides: who dealt it and took it, its effect, its attribute, before and after, and its breakdown.</summary>
        [UnityTest]
        public IEnumerator TW_22_A_hit_is_written_on_both_sides_with_its_breakdown([Values] Hub hub) => Live(async () =>
        {
            UseLog(ECombatLogRecords.HitsTaken | ECombatLogRecords.HitsDealt);
            var attacker = Fighter(hub, "Attacker");
            var target = Fighter(hub, "Target");
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            made.Add(pipeline);
            pipeline.Stages = new List<AbstractValueStage> { new MultiplyStage { By = 2f } };
            var blow = Blow(-10f);
            blow.ImpactSpecification.Pipeline = pipeline;
            await Frames.Step();

            Assert.IsTrue(World.Apply(attacker, target, blow));
            await Frames.Step();

            var taken = Log(target).Single();
            Assert.AreEqual(ECombatLogKind.Taken, taken.Kind, "the target took a hit");
            Assert.AreSame(attacker, taken.Other, "from the attacker");
            Assert.AreSame(blow, taken.Effect, "by its blow");
            Assert.AreSame(health, taken.Attribute, "to its Health");
            Assert.AreEqual(-20f, taken.Change.CurrentValue, 0.01f, "for 20, doubled");
            Assert.AreEqual(100f, taken.Before.CurrentValue, 0.01f, "from 100");
            Assert.AreEqual(80f, taken.After.CurrentValue, 0.01f, "to 80");
            Assert.That(taken.Steps.Select(step => step.After), Has.Some.EqualTo(-20f).Within(0.01f), "with its breakdown, × 2 giving -20");
            Assert.IsFalse(taken.Killed);

            var dealt = Log(attacker).Single();
            Assert.AreEqual(ECombatLogKind.Dealt, dealt.Kind, "the attacker dealt it");
            Assert.AreSame(target, dealt.Other, "to the target");
            Assert.AreSame(blow, dealt.Effect);
            Assert.AreEqual(-20f, dealt.Change.CurrentValue, 0.01f);
            Assert.AreEqual(-20f, target.GetCombatLog().Total(ECombatLogKind.Taken, health), 0.01f, "the target's hits taken total -20");
        });

        /// <summary>
        /// Each entity writes what the project subscribes to: with Changes alone, its own change and no hit; with nothing,
        /// nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_22_A_project_writes_what_it_subscribes_to([Values] Hub hub) => Live(async () =>
        {
            UseLog(ECombatLogRecords.Changes);
            var attacker = Fighter(hub, "Attacker");
            var target = Fighter(hub, "Target");
            await Frames.Step();

            Assert.IsTrue(World.Apply(attacker, target, Blow(-10f)), "a hit");
            target.TryModifyAttribute(health, SourcedModifiedAttributeValue.GenerateUnworkable(target, health, -5f, 0f));
            await Frames.Step();

            var entries = Log(target);
            Assert.That(entries.Select(entry => entry.Kind), Is.EqualTo(new[] { ECombatLogKind.Changed }), "its own change, and not the hit");
            Assert.AreEqual(-5f, entries[0].Change.CurrentValue, 0.01f);
            Assert.That(Log(attacker), Is.Empty, "and nothing dealt");

            UseLog(ECombatLogRecords.None);
            Assert.IsTrue(World.Apply(attacker, target, Blow(-10f)));
            await Frames.Step();
            Assert.AreEqual(1, Log(target).Count, "subscribed to nothing, it writes nothing more");
        });

        /// <summary>
        /// The gameplay events it's sent are written where the project subscribes their tags, though no ability listens:
        /// the one who died hears Died from its killer, the killer Kill of the one it killed, and the killing blow is marked.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_22_Subscribed_events_are_written_and_a_killing_blow_marked([Values] Hub hub) => Live(async () =>
        {
            UseLog(ECombatLogRecords.HitsTaken, 100, Tags.DIED, Tags.KILL);
            var attacker = Fighter(hub, "Attacker");
            var target = Fighter(hub, "Target");
            var blow = Blow(-1000f);
            await Frames.Step();

            Assert.IsTrue(World.Apply(attacker, target, blow));
            await Frames.Until(() => target.IsDead, 30, "the target dies");
            await Frames.Step(2);

            var entries = Log(target);
            var killing = entries.Single(entry => entry.Kind == ECombatLogKind.Taken);
            Assert.IsTrue(killing.Killed, "its last blow killed it");
            var died = entries.Single(entry => entry.Kind == ECombatLogKind.Event);
            Assert.AreEqual(Tags.DIED, died.Event, "it heard it died");
            Assert.AreSame(attacker, died.Other, "at the attacker's hand");

            var kill = Log(attacker).Single(entry => entry.Kind == ECombatLogKind.Event);
            Assert.AreEqual(Tags.KILL, kill.Event, "the attacker heard its kill");
            Assert.AreSame(target, kill.Other, "of the target");
        });

        /// <summary>It keeps its last N, the oldest going first; with 0 it keeps none.</summary>
        [UnityTest]
        public IEnumerator TW_22_It_keeps_its_last_N([Values] Hub hub) => Live(async () =>
        {
            UseLog(ECombatLogRecords.HitsTaken, keep: 3);
            var attacker = Fighter(hub, "Attacker");
            var target = Fighter(hub, "Target");
            await Frames.Step();

            for (int i = 1; i <= 5; i++) Assert.IsTrue(World.Apply(attacker, target, Blow(-i)));
            await Frames.Step();
            Assert.That(Log(target).Select(entry => entry.Change.CurrentValue), Is.EqualTo(new[] { -3f, -4f, -5f }), "its last three, oldest first");
            Assert.AreEqual(5, target.GetCombatLog().Written, "of five written");

            UseLog(ECombatLogRecords.HitsTaken, keep: 0);
            Assert.IsTrue(World.Apply(attacker, target, Blow(-1f)));
            await Frames.Step();
            Assert.That(Log(target), Is.Empty, "with 0, it keeps none");
        });
    }
}
