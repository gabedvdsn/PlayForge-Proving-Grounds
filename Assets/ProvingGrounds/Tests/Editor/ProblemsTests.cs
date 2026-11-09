using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// One validator feeds the Problems view and the badges (Q8, ED-15). It checks each PlayForge asset for what its
    /// inspector warns about in place, and follows the project, so an edit checks its asset again. Validate All checked
    /// four things, flagged every effect with no workers, and showed 15 problems in a dialog; the Audit wrote to the
    /// Console.
    /// </summary>
    public class ProblemsTests
    {
        private readonly List<string> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var path in made) AssetDatabase.DeleteAsset(path);
            made.Clear();
        }

        private T Save<T>(T asset, string name) where T : ScriptableObject
        {
            var path = $"Assets/ED-15 {name}.asset";
            AssetDatabase.CreateAsset(asset, path);
            made.Add(path);
            return asset;
        }

        private static List<string> Messages(Object asset) => ForgeValidator.Check(asset).Select(problem => problem.Message).ToList();

        // An asset tag as its name gives it, as Identity stores it
        private static Tag TagOf(string name)
        {
            var tag = default(Tag);
            tag.Name = name.Split('.')[0] + "." + name.Split('.')[1];
            tag.DisplayName = name.Split('.')[1];
            return tag;
        }

        // An ability set to take a provider's levels, with none
        private static Ability LinkedAbility()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "Linked";
            var tags = ability.Tags;
            tags.AssetTag = TagOf("Ability.Linked");
            ability.Tags = tags;
            ability.LinkMode = EAbilityLinkMode.LinkedToProvider;
            return ability;
        }

        private static GameplayEffect Effect(string name)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition.Name = name;
            var tags = effect.Tags;
            tags.AssetTag = TagOf("Effect." + name);
            effect.Tags = tags;
            effect.DurationSpecification = new GameplayEffectDuration { DurationPolicy = EEffectDurationPolicy.Instant };
            effect.ImpactSpecification = new GameplayEffectImpact();
            return effect;
        }

        /// <summary>
        /// The validator finds what the inspectors warn about in place: a link with no provider, an impact its duration
        /// never makes, a field that reads a scaler it hasn't got, a set element with no attribute, a stale asset tag. An
        /// effect with no workers, which Validate All flagged, has no problem.
        /// </summary>
        [Test]
        public void ED_15_The_validator_finds_what_the_inspectors_warn_about()
        {
            var linked = LinkedAbility();
            var instant = Effect("Instant");
            instant.ImpactSpecification.AdditionalImpacts = new List<AttributeImpactEntry> { new() { Delivery = EImpactDelivery.Periodic } };
            var missing = Effect("Missing");
            missing.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Durational,
                DurationOperation = new ScalableFloat { RealMagnitude = EMagnitudeOperation.UseScaler },
            };
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            set.Name = "Stats";
            set.AssetTag = TagOf("AttributeSet.Stats");
            set.Attributes.Add(new AttributeSetElement());
            var stale = Effect("Burning");
            var tags = stale.Tags;
            tags.AssetTag = TagOf("Effect.Old");
            stale.Tags = tags;
            var clean = Effect("Clean");
            try
            {
                Assert.That(Messages(linked), Has.Some.Contains("No level provider is set"), "a link with no provider");
                Assert.That(ForgeValidator.Check(linked).Select(problem => problem.Severity), Has.All.EqualTo(ForgeProblemSeverity.Warning));
                Assert.That(Messages(instant), Has.Some.Contains("never applies"), "an impact the duration never makes");
                Assert.That(Messages(missing), Has.Some.Contains("Duration").And.Contains("reads its scaler"), "a field with no scaler");
                Assert.That(Messages(set), Has.Some.Contains("has no attribute"), "an element with no attribute");
                Assert.That(Messages(stale), Has.Some.Contains("Effect.Burning"), "a tag the name no longer gives");
                Assert.That(Messages(clean), Is.Empty, "an effect with no workers has no problem");
            }
            finally
            {
                foreach (var each in new Object[] { linked, instant, missing, set, stale, clean }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// An ability's cost and cooldown apply only from a stage set to Apply Usage Effects, and nothing said so when no
        /// stage was: the Tactics lab's Slash had a cooldown that never applied (ED-41). A cost or a cooldown that no stage
        /// applies is a problem, which names what goes unapplied, until a stage applies them.
        /// </summary>
        [Test]
        public void ED_41_A_cost_or_cooldown_no_stage_applies_is_a_problem()
        {
            var slash = ScriptableObject.CreateInstance<Ability>();
            slash.Definition.Name = "Slash";
            var tags = slash.Tags;
            tags.AssetTag = TagOf("Ability.Slash");
            slash.Tags = tags;
            slash.Behaviour = new AbilityBehaviour { Stages = new List<AbilityTaskBehaviourStage> { new AbilityTaskBehaviourStage() } };
            var cooldown = Effect("Cooldown");
            var cost = Effect("Cost");
            try
            {
                Assert.That(Messages(slash), Has.None.Contains("usage effects"), "with neither, there's nothing to apply");

                slash.Cooldown = cooldown;
                Assert.That(Messages(slash), Has.Some.Contains("No stage applies usage effects, so the cooldown"), "a cooldown no stage applies");
                slash.Cost = cost;
                Assert.That(Messages(slash), Has.Some.Contains("the cost and the cooldown"), "and a cost with it");

                slash.Behaviour.Stages[0].ApplyUsageEffects = true;
                Assert.That(Messages(slash), Has.None.Contains("usage effects"), "and none once a stage applies them");
            }
            finally
            {
                foreach (var each in new Object[] { slash, cooldown, cost }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>An edit checks its asset again, with no refresh: fixing the problem takes it off the list.</summary>
        [UnityTest]
        public IEnumerator ED_15_An_edit_checks_its_asset_again()
        {
            var ability = Save(LinkedAbility(), "Linked Ability");
            int changes = 0;
            void Count() => changes++;
            ForgeValidator.Changed += Count;
            try
            {
                Assert.AreEqual(1, ForgeValidator.ProblemsOf(ability).Count, "the link with no provider");
                Assert.That(ForgeValidator.All.Select(problem => problem.Asset), Has.Member(ability), "among the project's problems");

                var serialized = new SerializedObject(ability);
                serialized.FindProperty(nameof(Ability.LinkMode)).intValue = (int)EAbilityLinkMode.Standalone;
                serialized.ApplyModifiedProperties();
                for (int i = 0; i < 4; i++) yield return null;

                Assert.That(ForgeValidator.ProblemsOf(ability), Is.Empty, "fixed, the asset has none");
                Assert.That(ForgeValidator.All.Select(problem => problem.Asset), Has.No.Member(ability), "and it's off the list");
                Assert.That(changes, Is.GreaterThan(0), "and the validator said so");
            }
            finally
            {
                ForgeValidator.Changed -= Count;
            }
        }

        /// <summary>
        /// The Problems view lists each problem with its asset, and a click selects the asset, where Validate All showed 15
        /// in a dialog nothing in could be selected.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_15_The_Problems_view_lists_each_problem_and_selects_its_asset()
        {
            var ability = Save(LinkedAbility(), "Listed Ability");
            var effect = Effect("Listed");
            effect.ImpactSpecification.AdditionalImpacts = new List<AttributeImpactEntry> { new() { Delivery = EImpactDelivery.Periodic } };
            Save(effect, "Listed Effect");
            var log = new WindowLog();
            try
            {
                var window = ForgeProblemsWindow.ShowWindow();
                for (int i = 0; i < 4; i++) yield return null;

                var list = window.rootVisualElement.Q<MultiColumnListView>("Problems");
                Assert.IsNotNull(list, "the view lists the problems");
                var problems = list.itemsSource.Cast<ForgeProblem>().ToList();
                Assert.That(problems.Select(problem => problem.Asset), Has.Member(ability).And.Member(effect), "both assets' problems");

                Selection.activeObject = null;
                list.SetSelection(problems.FindIndex(problem => problem.Asset == effect));
                Assert.AreSame(effect, Selection.activeObject, "a click selects the asset");
                StringAssert.Contains("problem", window.rootVisualElement.Q<Label>("ProblemsStatus").text, "the status bar counts them");
            }
            finally
            {
                // Closing a window logs the batch mode's "No graphic device" too
                foreach (var window in Resources.FindObjectsOfTypeAll<ForgeProblemsWindow>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the view was open");
        }

        /// <summary>An inspector's header counts its asset's problems, and the count follows an edit.</summary>
        [UnityTest]
        public IEnumerator ED_15_An_inspectors_header_counts_its_problems()
        {
            var ability = Save(LinkedAbility(), "Counted Ability");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("ED-15") { hideFlags = HideFlags.HideAndDontSave };
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = settings;
            UnityEditor.Editor editor = null;
            try
            {
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(ability);
                var root = editor.CreateInspectorGUI();
                document.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                for (int i = 0; i < 4; i++) yield return null;

                var count = root.Q<Button>("ProblemCount");
                Assert.IsNotNull(count, "the header has a problem count");
                Assert.AreEqual(DisplayStyle.Flex, count.resolvedStyle.display, "shown while the asset has one");
                StringAssert.Contains("1 problem", count.text);

                editor.serializedObject.FindProperty(nameof(Ability.LinkMode)).intValue = (int)EAbilityLinkMode.Standalone;
                editor.serializedObject.ApplyModifiedProperties();
                for (int i = 0; i < 6; i++) yield return null;
                Assert.AreEqual(DisplayStyle.None, count.resolvedStyle.display, "and hidden once it's fixed");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        /// <summary>In batch mode, as on a build server, the validator writes each problem to the Console (Q8).</summary>
        [Test]
        public void ED_15_The_validator_writes_each_problem_to_the_Console()
        {
            Save(LinkedAbility(), "Logged Ability");
            LogAssert.ignoreFailingMessages = true;
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex(@"ED-15 Logged Ability.*No level provider is set"));
                ForgeValidator.LogAll();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Quick fixes (FEAT-25, the user's request)
        // ═══════════════════════════════════════════════════════════════════════════

        // The one problem an asset has whose message holds the text
        private static ForgeProblem ProblemOf(Object asset, string text)
        {
            var found = ForgeValidator.Check(asset).Where(problem => problem.Message.Contains(text)).ToList();
            Assert.AreEqual(1, found.Count, $"one problem says \"{text}\"");
            Assert.IsNotNull(found[0].Rule, "and it names the rule that found it");
            return found[0];
        }

        // Applies a problem's fix as the Problems view does, as one Undo step
        private static void Fix(ForgeProblem problem, string name)
        {
            Assert.AreEqual(name, problem.Fix?.Name, "it has a quick fix that says what it does");
            Undo.IncrementCurrentGroup();
            problem.Fix.Apply();
        }

        /// <summary>
        /// Each problem a simple step fixes has a Quick Fix, which does what it says: a link with no provider takes its own
        /// levels, a field that reads a scaler it hasn't got reads its number, a magnitude outside its bounds is held to
        /// them, a set's element with no attribute goes, a stale asset tag is stored, and a scaler two fields hold is copied
        /// for the second. One Undo takes a fix back. A problem that needs a choice, as which stage applies usage effects,
        /// has none (FEAT-25).
        /// </summary>
        [Test]
        public void FEAT_25_A_quick_fix_does_what_it_says()
        {
            var made = new List<Object>();
            try
            {
                var ability = LinkedAbility();
                made.Add(ability);
                Fix(ProblemOf(ability, "level provider"), "Use Own Levels");
                Assert.AreEqual(EAbilityLinkMode.Standalone, ability.LinkMode, "the ability takes its own levels");
                Undo.PerformUndo();
                Assert.AreEqual(EAbilityLinkMode.LinkedToProvider, ability.LinkMode, "and one Undo takes the fix back");

                var effect = Effect("Unscaled");
                made.Add(effect);
                effect.ImpactSpecification.MagnitudeOperation = new ScalableFloat { Magnitude = 5f, RealMagnitude = EMagnitudeOperation.UseScaler };
                Fix(ProblemOf(effect, "has none, so it reads 0"), "Make It Constant");
                Assert.AreEqual(EMagnitudeOperation.UseMagnitude, effect.ImpactSpecification.MagnitudeOperation.RealMagnitude, "the field reads its number");

                var health = ScriptableObject.CreateInstance<Attribute>();
                health.Name = "Health";
                made.Add(health);
                var set = ScriptableObject.CreateInstance<AttributeSet>();
                set.Name = "Stats";
                set.AssetTag = TagOf("AttributeSet.Stats");
                made.Add(set);
                set.Attributes.Add(new AttributeSetElement
                {
                    Attribute = health,
                    Current = new AttributeMagnitudeSpec { Magnitude = 150f },
                    Base = new AttributeMagnitudeSpec { Magnitude = 150f },
                    Overflow = new AttributeOverflowData { Policy = EAttributeOverflowPolicy.ZeroToCeil, Ceil = new AttributeValue(100f, 100f) },
                });
                set.Attributes.Add(new AttributeSetElement());
                Fix(ProblemOf(set, "Base 150 is outside its bounds"), "Clamp to Bounds");
                Assert.AreEqual(100f, set.Attributes[0].Base.Magnitude, "Base is held to its bounds");
                Assert.AreEqual(100f, set.Attributes[0].Current.Magnitude, "and Current with it");
                Fix(ProblemOf(set, "has no attribute"), "Remove the Element");
                Assert.AreEqual(1, set.Attributes.Count, "the element with no attribute goes");

                var renamed = LinkedAbility();
                made.Add(renamed);
                renamed.LinkMode = EAbilityLinkMode.Standalone;
                renamed.Definition.Name = "Renamed";
                Fix(ProblemOf(renamed, "but its name gives"), "Store the Tag");
                Assert.That(Messages(renamed), Has.None.Contains("but its name gives"), "the tag its name gives is stored");

                // A tag whose path is right, with no display name, as the Demos' were saved, says that, not the path twice
                var unnamed = LinkedAbility();
                made.Add(unnamed);
                unnamed.LinkMode = EAbilityLinkMode.Standalone;
                var unnamedTags = unnamed.Tags;
                var bare = unnamedTags.AssetTag;
                bare.DisplayName = "";
                unnamedTags.AssetTag = bare;
                unnamed.Tags = unnamedTags;
                Fix(ProblemOf(unnamed, "has no display name, but its name gives it Linked"), "Store the Tag");
                Assert.AreEqual("Linked", unnamed.Tags.AssetTag.DisplayName, "and its fix stores the display name");

                var shared = Effect("Shared");
                made.Add(shared);
                var scaler = new SimpleScaler { LevelValues = new[] { 1f, 2f } };
                shared.DurationSpecification = new GameplayEffectDuration
                {
                    DurationPolicy = EEffectDurationPolicy.Durational,
                    DurationOperation = new ScalableFloat { RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = scaler },
                    TickIntervalOperation = new ScalableFloat { RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = scaler },
                };
                Fix(ProblemOf(shared, "hold one scaler"), "Give It Its Own");
                Assert.AreNotSame(shared.DurationSpecification.DurationOperation.Scaler, shared.DurationSpecification.TickIntervalOperation.Scaler,
                    "each field holds a scaler of its own");
                Assert.AreEqual(new[] { 1f, 2f }, shared.DurationSpecification.TickIntervalOperation.Scaler.LevelValues, "with the same values");

                var unapplied = LinkedAbility();
                made.Add(unapplied);
                unapplied.LinkMode = EAbilityLinkMode.Standalone;
                unapplied.Cooldown = ScriptableObject.CreateInstance<GameplayEffect>();
                made.Add(unapplied.Cooldown);
                Assert.IsNull(ProblemOf(unapplied, "No stage applies usage effects").Fix, "a problem that needs a choice has no quick fix");
            }
            finally
            {
                foreach (var each in made) if (each != null) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Problems view's By Rule view groups problems under the rule each breaks, and a rule whose problems a step
        /// fixes has Fix All, which fixes them all as one Undo step (FEAT-25).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_25_By_Rule_groups_problems_and_fixes_them_all()
        {
            var first = Save(LinkedAbility(), "Rule Ability A");
            var second = Save(LinkedAbility(), "Rule Ability B");
            var log = new WindowLog();
            try
            {
                var window = ForgeProblemsWindow.ShowWindow();
                for (int i = 0; i < 4; i++) yield return null;
                var root = window.rootVisualElement;
                root.Q<ToolbarSearchField>("Search").value = "Rule Ability";
                var byRule = root.Q<ToolbarToggle>("ByRule");
                Assert.IsNotNull(byRule, "the toolbar switches to By Rule");
                byRule.value = true;
                for (int i = 0; i < 4; i++) yield return null;

                var groups = root.Query(name: "RuleGroup").ToList();
                var links = groups.FirstOrDefault(group => group.Q<Label>("RuleName")?.text == "Links with no level provider");
                Assert.IsNotNull(links, "the link problems sit under their rule");
                Assert.AreEqual("2", links.Q<Label>("RuleCount")?.text, "both of them");
                Assert.AreEqual(2, links.Query(name: "RuleProblem").ToList().Count, "each a row");
                Assert.AreEqual(2, links.Query<Button>("Fix").ToList().Count, "each with its quick fix");

                Undo.IncrementCurrentGroup();
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    var fixAll = links.Q<Button>("FixAll");
                    Assert.IsNotNull(fixAll, "the rule fixes them all");
                    submit.target = fixAll;
                    fixAll.SendEvent(submit);
                }
                Assert.AreEqual(EAbilityLinkMode.Standalone, first.LinkMode, "Fix All fixes the first");
                Assert.AreEqual(EAbilityLinkMode.Standalone, second.LinkMode, "and the second");
                Undo.PerformUndo();
                Assert.AreEqual(EAbilityLinkMode.LinkedToProvider, first.LinkMode, "one Undo takes both back");
                Assert.AreEqual(EAbilityLinkMode.LinkedToProvider, second.LinkMode, "together");
            }
            finally
            {
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
}
