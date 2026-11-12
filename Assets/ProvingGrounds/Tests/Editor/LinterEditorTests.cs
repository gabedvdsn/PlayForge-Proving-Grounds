using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-25, Decision 65: the content linter's checks in the Problems window. Stacks read twice, filters that never
    /// match, and events nothing sends; a quick fix where one plain step fixes it.
    /// </summary>
    public class LinterEditorTests
    {
        private const string Folder = "Assets/TW25 Lint";
        private Tag attack, spell, shout;

        [SetUp]
        public void MakeFolder()
        {
            Assume.That(AssetDatabase.IsValidFolder(Folder), Is.False, $"{Folder} is free");
            AssetDatabase.CreateFolder("Assets", "TW25 Lint");
            attack = Tag.Generate("Test.TW25.Attack");
            spell = Tag.Generate("Test.TW25.Spell");
            shout = Tag.Generate("Test.TW25.Shout");
        }

        [TearDown]
        public void Clean()
        {
            AssetDatabase.DeleteAsset(Folder);
            ForgeLint.Forget();
        }

        private static T Saved<T>(T asset, string name) where T : ScriptableObject
        {
            asset.name = name;
            AssetDatabase.CreateAsset(asset, $"{Folder}/{name}.asset");
            return asset;
        }

        private static List<ForgeProblem> Problems(Object asset, string about)
        {
            ForgeLint.Forget();
            return ForgeValidator.Check(asset).Where(problem => problem.Message.Contains(about)).ToList();
        }

        private static EffectTagRequirements NoRequirements() => new()
        {
            ApplicationRequirements = new AvoidRequireTagGroup(),
            OngoingRequirements = new AvoidRequireTagGroup(),
            RemovalRequirements = new AvoidRequireTagGroup(),
        };

        // An effect, saved: one impact on no attribute in particular, its number given, dealing the impact types given
        private static GameplayEffect Effect(string name, EEffectDurationPolicy policy, ScalableFloat magnitude, int ticks = 0,
            bool stacks = false, params Tag[] dealing)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition = new GameplayEffectDefinition { Name = name, Textures = new List<TextureItem>() };
            effect.Tags = new GameplayEffectTags { AssetTag = Tag.Generate($"Test.TW25.{name.Replace(" ", "")}"), ContextTags = new List<Tag>(), GrantedTags = new List<Tag>() };
            effect.Workers = new List<AbstractEffectWorker>();
            effect.SourceRequirements = NoRequirements();
            effect.TargetRequirements = NoRequirements();
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AffiliationPolicy = EAffiliationPolicy.AlwaysAllow,
                ImpactTypes = new List<Tag>(dealing),
                MagnitudeOperation = magnitude,
                Packets = System.Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = policy,
                DurationOperation = new ScalableFloat(5f),
                EnablePeriodicTicks = ticks > 0,
                TicksOperation = new ScalableInt { Magnitude = ticks },
                TickIntervalOperation = new ScalableFloat(1f),
                StackingConfigs = new List<AbstractStackingConfig>(),
                ReApplicationPolicy = stacks ? EEffectReApplicationPolicy.StackExistingContainers : EEffectReApplicationPolicy.AppendNewContainer,
            };
            return Saved(effect, name);
        }

        private static ScalableFloat Reading(AbstractScaler scaler) => new(scaler, -20f, EMagnitudeOperation.MultiplyWithScaler);

        /// <summary>
        /// A stacking effect's periodic impact ticks once for each stack, so a number that reads its stacks reads them twice:
        /// a warning, its fix a plain number. A lasting impact reading its stacks, or a periodic one that doesn't stack, is
        /// no problem.
        /// </summary>
        [Test]
        public void TW_25_A_periodic_impact_reading_its_own_stacks_reads_them_twice()
        {
            var hangover = Effect("Hangover", EEffectDurationPolicy.Durational, Reading(new EffectStacksScaler()), ticks: 5, stacks: true);
            var problem = Problems(hangover, "stacks").SingleOrDefault();
            Assert.IsNotNull(problem, "a stacking DoT that reads its stacks reads them twice");
            Assert.AreEqual(ForgeProblemSeverity.Warning, problem.Severity);
            Assert.IsNotNull(problem.Fix, "with a quick fix");
            problem.Fix.Apply();
            Assert.IsTrue(AbstractScaler.IsNone(hangover.ImpactSpecification.MagnitudeOperation.Scaler), "which reads no stacks");
            Assert.That(Problems(hangover, "stacks"), Is.Empty, "and the problem's gone");

            var rage = Effect("Rage", EEffectDurationPolicy.Durational, Reading(new EffectStacksScaler()), stacks: true);
            Assert.That(Problems(rage, "stacks"), Is.Empty, "a lasting impact counts its stacks once");
            var lone = Effect("Lone Burn", EEffectDurationPolicy.Durational, Reading(new EffectStacksScaler()), ticks: 5);
            Assert.That(Problems(lone, "stacks"), Is.Empty, "and one that doesn't stack reads 1");
        }

        /// <summary>An effect that reads an item's stacks, granted by no item, reads 1: a warning, until an item grants it.</summary>
        [Test]
        public void TW_25_An_items_stacks_read_in_an_effect_no_item_grants_is_a_problem()
        {
            var pile = Effect("Pile Bonus", EEffectDurationPolicy.Infinite, Reading(new ItemStacksScaler()));
            var problem = Problems(pile, "item's stacks").SingleOrDefault();
            Assert.IsNotNull(problem, "no item grants it, so its stacks read 1");
            Assert.AreEqual(ForgeProblemSeverity.Warning, problem.Severity);

            var coins = Saved(ScriptableObject.CreateInstance<Item>(), "Coins");
            coins.GrantedEffects.Add(pile);
            EditorUtility.SetDirty(coins);
            Assert.That(Problems(pile, "item's stacks"), Is.Empty, "an item that grants it gives it stacks to read");
        }

        /// <summary>
        /// An On Hit's, a lifesteal's or a trigger's impact type that no effect deals never matches: a warning each. Where
        /// the filter keeps a type an effect deals, its fix removes the one none does; a filter of that type alone would
        /// take every hit without it, so it has none. A type an effect deals is no problem.
        /// </summary>
        [Test]
        public void TW_25_A_filter_of_an_impact_type_no_effect_deals_never_matches()
        {
            Effect("Strike", EEffectDurationPolicy.Instant, new ScalableFloat(-10f), dealing: attack);
            var dagger = ScriptableObject.CreateInstance<Item>();
            dagger.OnHit.Add(new OnHitEntry { ImpactTypes = new List<Tag> { attack, spell } });
            dagger.Lifesteal.Add(new LifestealEntry { ImpactTypes = new List<Tag> { spell } });
            Saved(dagger, "Dagger");

            var problems = Problems(dagger, "no effect in the project deals");
            Assert.AreEqual(2, problems.Count, $"the On Hit's and the lifesteal's Spell, and not their Attack: {string.Join(" | ", problems)}");
            var onHit = problems.Single(problem => problem.PropertyPath.StartsWith(nameof(Item.OnHit)));
            var lifesteal = problems.Single(problem => problem.PropertyPath.StartsWith(nameof(Item.Lifesteal)));
            Assert.IsNotNull(onHit.Fix, "the On Hit keeps Attack, so Spell can go");
            Assert.IsNull(lifesteal.Fix, "and the lifesteal, of Spell alone, would take every hit without it");
            onHit.Fix.Apply();
            Assert.That(dagger.OnHit[0].ImpactTypes, Is.EqualTo(new[] { attack }), "which takes Spell out");

            var guard = ScriptableObject.CreateInstance<Ability>();
            guard.Definition.Triggers.Add(new AbilityTrigger
            {
                Source = EAbilityTriggerSource.GameplayEvent, Tag = Tags.IMPACT_TAKEN, ImpactTypes = new List<Tag> { spell },
            });
            // Kill carries no impact, so its trigger's filter is never read
            guard.Definition.Triggers.Add(new AbilityTrigger
            {
                Source = EAbilityTriggerSource.GameplayEvent, Tag = Tags.KILL, ImpactTypes = new List<Tag> { spell },
            });
            Saved(guard, "Guard");
            var trigger = Problems(guard, "no effect in the project deals");
            Assert.AreEqual(1, trigger.Count, $"a trigger's filter too, on an event that carries an impact: {string.Join(" | ", trigger)}");
            StringAssert.StartsWith("Trigger 1 ", trigger[0].Message, "the Impact Taken one");
        }

        /// <summary>
        /// A trigger on an event that isn't the core's and that no asset sends is a note, as code may send it; a core event,
        /// one a threshold sends, or one a project's own stop stage sends from a pipeline, is no problem.
        /// </summary>
        [Test]
        public void TW_25_A_trigger_on_an_event_nothing_sends_is_a_note()
        {
            var listener = ScriptableObject.CreateInstance<Ability>();
            listener.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.GameplayEvent, Tag = shout });
            listener.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.GameplayEvent, Tag = Tags.KILL });
            listener.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.TagAdded, Tag = Tag.Generate("Test.TW25.Tagged") });
            listener.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.GameplayEvent, Tag = ParryStage.Parried });
            Saved(listener, "Listener");

            var notes = Problems(listener, "no asset in the project sends");
            Assert.AreEqual(2, notes.Count, $"Shout and Parried, which nothing sends, and not Kill, the core's, nor a tag trigger: {string.Join(" | ", notes)}");
            Assert.That(notes.Select(note => note.Severity), Has.All.EqualTo(ForgeProblemSeverity.Note), "notes, as code may send them");

            var boss = ScriptableObject.CreateInstance<AttributeSet>();
            boss.Attributes = new List<AttributeSetElement>();
            boss.SubSets = new List<AttributeSet>();
            boss.WorkerGroup = new StandardWorkerGroup();
            boss.WorkerGroup.AnalysisWorkers.Add(new ThresholdAnalysisWorker { Event = shout });
            Saved(boss, "Shouting Boss");
            notes = Problems(listener, "no asset in the project sends");
            Assert.AreEqual(1, notes.Count, "a threshold that sends Shout answers it");
            StringAssert.StartsWith("Trigger 4 ", notes[0].Message, "and Parried is left");

            var parrying = ScriptableObject.CreateInstance<ValuePipeline>();
            parrying.Stages.Add(new ParryStage());
            Saved(parrying, "Parrying");
            Assert.That(Problems(listener, "no asset in the project sends"), Is.Empty, "a pipeline whose stage parries answers that");
        }

        /// <summary>
        /// A note is listed in the Problems view under a filter of its own, and left out of the problems it counts and an
        /// asset's own, which an inspector's header and The Forge count.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_25_The_Problems_view_lists_a_note_apart()
        {
            var listener = ScriptableObject.CreateInstance<Ability>();
            listener.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.GameplayEvent, Tag = shout });
            Saved(listener, "Quiet Listener");
            ForgeLint.Forget();
            var log = new WindowLog();
            try
            {
                Assert.That(ForgeValidator.NotesOf(listener).Select(note => note.Severity), Is.EqualTo(new[] { ForgeProblemSeverity.Note }), "it has the note");
                Assert.That(ForgeValidator.ProblemsOf(listener).Select(problem => problem.Message), Has.None.Contains("no asset in the project sends"),
                    "which isn't among its problems");
                Assert.That(ForgeValidator.All.Select(problem => problem.Severity), Has.None.EqualTo(ForgeProblemSeverity.Note), "nor the project's");

                var window = ForgeProblemsWindow.ShowWindow();
                for (int i = 0; i < 4; i++) yield return null;
                var list = window.rootVisualElement.Q<MultiColumnListView>("Problems");
                Assert.That(list.itemsSource.Cast<ForgeProblem>().Where(problem => problem.Asset == listener).Select(problem => problem.Severity),
                    Has.Member(ForgeProblemSeverity.Note), "the view lists it");
                StringAssert.Contains("note", window.rootVisualElement.Q<Label>("ProblemsStatus").text, "and its status names the notes apart");

                var notes = window.rootVisualElement.Q<ToolbarToggle>("Notes");
                Assert.IsNotNull(notes, "under a filter of their own");
                notes.value = false;
                yield return null;
                Assert.That(list.itemsSource.Cast<ForgeProblem>().Select(problem => problem.Severity), Has.None.EqualTo(ForgeProblemSeverity.Note),
                    "which hides them");
            }
            finally
            {
                // Closing a window logs the batch mode's "No graphic device" too
                foreach (var window in Resources.FindObjectsOfTypeAll<ForgeProblemsWindow>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the view was open");
        }

        // A window logs "No graphic device" as it opens in batch mode, which the smoke suite's guard sets aside
        private sealed class WindowLog : System.IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public readonly List<string> Errors = new();

            public WindowLog()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert && !message.Contains(NoDevice)) Errors.Add(message);
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }

    /// <summary>A stop stage of a project's own, as a parry, which sends an event of its own as it stops a hit (TW-25's test).</summary>
    [System.Serializable]
    public class ParryStage : AbstractStopStage
    {
        public static readonly Tag Parried = Tag.Generate("Test.TW25.Parried");

        public override Tag StoppedEvent => Parried;
        public override Tag DealerEvent => default;
        protected override string Verb => "parried";
    }
}
