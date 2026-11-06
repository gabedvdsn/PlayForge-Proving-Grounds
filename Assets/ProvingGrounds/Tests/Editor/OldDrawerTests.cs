using System;
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
    /// The drawers older than Phase 4 that still set their colors, borders or fonts in C# (ED-66, split from ED-57): a
    /// requirement group, a tag query, a level threshold, a scaler group's member, a level source and an ability's
    /// behaviour. Each is drawn as the Inspector draws it, in a runtime panel, since batch mode has no graphics device.
    /// </summary>
    public class OldDrawerTests
    {
        /// <summary>An asset with a field of each kind, as the drawers draw them.</summary>
        public sealed class Holder : ScriptableObject
        {
            public AvoidRequireTagGroup Requirements = new() { RequireTags = new List<TagQuery>(), AvoidTags = new List<TagQuery>() };
            public TagQuery Query = new();
            public LevelThreshold[] Thresholds = { new() { Level = 2, Value = 5f, TierName = "Silver" } };
            public MagnitudeModifierGroupMember Member;
            public CachedMagnitudeModifierGroupMember CachedMember;
            [LinkedSource] public BaseForgeLevelProvider Source;
            public AbilityBehaviour Behaviour = new();
        }

        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("ED-66") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public PropertyField Draw(SerializedObject serialized, string path)
            {
                var field = new PropertyField(serialized.FindProperty(path));
                Document.rootVisualElement.Add(field);
                field.Bind(serialized);
                return field;
            }

            public void Destroy()
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        private static IEnumerator Until(Func<bool> done, double seconds = 1.0)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (!done() && EditorApplication.timeSinceStartup < until) yield return null;
        }

        // What the field's own drawer sets of the look the style sheet gives, not the drawers it hosts
        private static List<string> LookSetInCode(PropertyField field) => field.Query<VisualElement>().ToList()
            .Where(each => each.GetFirstAncestorOfType<PropertyField>() == field)
            .SelectMany(each => ForgeInspectorTests.LookSetInCode(each).Select(style => $"{each.GetType().Name} \"{(each as TextElement)?.text ?? each.name}\" sets {style}"))
            .ToList();

        private static void Press(Button button)
        {
            Assert.IsNotNull(button, "the button is there");
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        // A list's own footer button, not one of the lists its elements hold
        private static Button OwnButton(VisualElement field, string name)
        {
            var list = field?.Q<ListView>();
            return list?.Query<Button>(name).ToList().FirstOrDefault(button => button.GetFirstAncestorOfType<ListView>() == list);
        }

        /// <summary>
        /// A requirement group is the requirements table the effect's and the ability's are (ED-37), of one row, whose
        /// cells open its list with its name and template under it (ED-66). It drew a tinted box with green and red
        /// stripes, colored count badges and an Import of its own.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_66_A_requirement_group_is_the_requirements_table()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            holder.Requirements.RequireTags.Add(new TagQuery { Tag = Tag.Generate("Status.Wet") });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(Holder.Requirements));
                yield return Until(() => field.Q("RequirementsTable") != null);
                yield return Frames();

                Assert.IsNotNull(field.Q("RequirementsTable"), "the group is the requirements table");
                Assert.AreEqual(1, field.Query(className: "pf-table__row").ToList().Count, "of one row, the group's");
                var requires = field.Q<Button>("GroupRequires");
                Assert.IsNotNull(requires, "with what it requires");
                StringAssert.Contains("Status.Wet", requires.text, "which lists its tags");
                Assert.IsNotNull(field.Q<Button>("GroupAvoids"), "and what it must not have");

                Press(requires);
                yield return Frames();
                Assert.IsNotNull(field.Q<PropertyField>("Tags"), "a cell opens its list");
                Assert.IsNotNull(field.Q<TextField>("RequirementName"), "with the group's name under it");
                Assert.That(LookSetInCode(field), Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// A tag query is its tag, how tags match it, and how many it counts, in Unity's aligned fields (ED-66). It drew a
        /// tinted box with labels of its own size and color.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_66_A_tag_query_is_Unitys_fields()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(Holder.Query));
                yield return Until(() => field.Q<IntegerField>("Magnitude") != null);
                yield return Frames();

                Assert.IsNotNull(field.Q<PropertyField>("Tag"), "its tag");
                Assert.IsNotNull(field.Q<EnumField>("MatchMode"), "how tags match it");
                Assert.IsNotNull(field.Q<EnumField>("Operator"), "how it counts them");
                var count = field.Q<IntegerField>("Magnitude");
                Assert.IsNotNull(count, "and the count");
                count.value = 2;
                yield return Frames();
                Assert.AreEqual(2, holder.Query.Magnitude, "a count writes the query");
                Assert.That(LookSetInCode(field), Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// A threshold is its level, its value and its tier, Unity's fields bound to them, so an Undo shows in them
        /// (ED-66). It drew a tinted row with "Lv" in blue, an arrow as text, and fields that wrote on change and
        /// followed nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_66_A_threshold_is_a_row_of_Unitys_fields()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), $"{nameof(Holder.Thresholds)}.Array.data[0]");
                yield return Until(() => field.Q<IntegerField>("Level") != null);
                yield return Frames();

                var level = field.Q<IntegerField>("Level");
                Assert.IsNotNull(level, "its level");
                Assert.AreEqual(2, level.value);
                Assert.AreEqual(5f, field.Q<FloatField>("Value")?.value, "its value");
                Assert.AreEqual("Silver", field.Q<TextField>("Tier")?.value, "and its tier");

                Undo.IncrementCurrentGroup();
                level.value = 4;
                yield return Frames();
                Assert.AreEqual(4, holder.Thresholds[0].Level, "a level writes the threshold");
                Undo.PerformUndo();
                yield return Frames();
                Assert.AreEqual(2, holder.Thresholds[0].Level, "Undo takes it back");
                Assert.AreEqual(2, level.value, "and the field shows it");
                Assert.That(LookSetInCode(field), Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// A scaler group's member, cached or not, is how it combines, then its scaler (ED-66). It drew a stripe and a
        /// symbol in the operation's color, the scaler's kind beside them, and a "CACHED" badge.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_66_A_group_member_is_its_operation_then_its_scaler()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(holder);
                foreach (var path in new[] { nameof(Holder.Member), nameof(Holder.CachedMember) })
                {
                    var field = host.Draw(serialized, path);
                    yield return Until(() => field.Q<EnumField>("Operation") != null);
                    yield return Frames();
                    Assert.IsNotNull(field.Q<EnumField>("Operation"), $"{path} combines by its operation");
                    Assert.IsNotNull(field.Q<PropertyField>("Scaler"), $"{path} then has its scaler");
                    Assert.That(LookSetInCode(field), Is.Empty, $"{path}'s drawer sets no look in code");
                }
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// A level source is Unity's object field of level providers, whose picker lists them, with a line of the
        /// levels it gives (ED-66). It took any asset and warned at the wrong one, and drew a browser of its own, a
        /// colored type badge and a tinted box.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_66_A_level_source_is_Unitys_object_field()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "Fireball";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(Holder.Source));
                yield return Until(() => field.Q<ObjectField>("LevelSource") != null);
                yield return Frames();

                var source = field.Q<ObjectField>("LevelSource");
                Assert.IsNotNull(source, "the source is Unity's object field");
                Assert.AreEqual(typeof(BaseForgeLevelProvider), source.objectType, "whose picker lists the level providers");
                Assert.IsFalse(field.Query<Button>().ToList().Any(button => button.GetFirstAncestorOfType<ObjectField>() == null),
                    "with no buttons of its own");

                source.value = ability;
                yield return Frames();
                Assert.AreEqual(ability, holder.Source, "a pick sets the source");
                var line = field.Q<Label>("LevelSourceLine");
                Assert.IsNotNull(line, "and a line says what it gives");
                StringAssert.Contains("Level", line.text);
                Assert.That(LookSetInCode(field), Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
                Object.DestroyImmediate(holder);
            }
        }

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens
        private sealed class WindowLog : IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public List<string> Errors { get; } = new();

            public WindowLog()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(NoDevice))
                    Errors.Add($"{type}: {message}");
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>
        /// A Workers section's import lists the assets of every type that have a worker group, as an item's is a source for
        /// an ability's (ED-66). The worker group's own Import did, and it went with the group's box (ED-57), which left
        /// the section's, listing abilities alone.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_66_A_Workers_section_imports_from_any_type()
        {
            const string folder = "Assets/ED-66 Import";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-66 Import");
            var torch = ScriptableObject.CreateInstance<Item>();
            torch.Definition.Name = "Torch";
            AssetDatabase.CreateAsset(torch, $"{folder}/Torch.asset");
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "Fireball";
            var editor = UnityEditor.Editor.CreateEditor(ability);
            var host = new Host();
            var log = new WindowLog();
            ForgeImportWindow window = null;
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = editor.CreateInspectorGUI();
                host.Document.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                yield return Frames();

                Press(root.Q<Button>("WorkersImport"));
                yield return null;
                window = Resources.FindObjectsOfTypeAll<ForgeImportWindow>().FirstOrDefault();
                Assert.IsNotNull(window, "the section's import opens the window");
                var sources = window.rootVisualElement.Query<Button>(className: "pf-import__source").ToList().Select(row => row.name).ToList();
                Assert.That(sources, Has.Member("Torch"), "which lists an item with a worker group");
            }
            finally
            {
                if (window != null) window.Close();
                host.Destroy();
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(ability);
                AssetDatabase.DeleteAsset(folder);
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the window was open");
        }

        /// <summary>
        /// An ability's behaviour, drawn whole, is the fields the ability inspector shows of it: its targeting, its asset
        /// loaders, what cancels and what it blocks, and its stages, Unity's lists where a new loader starts empty
        /// (ED-66). It drew colored sections and lists of its own.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_66_A_ability_behaviour_is_its_fields()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            holder.Behaviour.AssetLoaders = new List<AbstractSetAssetsTask> { new SetEffectsTask() };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(Holder.Behaviour));
                yield return Until(() => OwnButton(field.Q<PropertyField>("AssetLoaders"), "unity-list-view__add-button") != null);
                yield return Frames();

                foreach (var name in new[] { "Targeting", "ImplicitTag", "AssetLoaders", "CancelledBy", "Blocks", "Stages" })
                    Assert.IsNotNull(field.Q<PropertyField>(name), name);
                var useImplicit = field.Q<Toggle>("UseImplicitTargeting");
                Assert.IsNotNull(useImplicit, "Use Implicit Targeting");
                var implicitTag = field.Q<PropertyField>("ImplicitTag");
                Assert.IsFalse(implicitTag.ClassListContains("pf-hidden"), "Implicit Tag shows while implicit targeting is on");
                useImplicit.value = false;
                yield return Frames();
                Assert.IsTrue(implicitTag.ClassListContains("pf-hidden"), "and hides while it's off");

                Press(OwnButton(field.Q<PropertyField>("AssetLoaders"), "unity-list-view__add-button"));
                yield return Frames();
                Assert.AreEqual(2, holder.Behaviour.AssetLoaders.Count, "the list adds a loader");
                Assert.IsNull(holder.Behaviour.AssetLoaders[1], "which starts empty");
                Assert.IsInstanceOf<SetEffectsTask>(holder.Behaviour.AssetLoaders[0], "and the one before keeps its own");
                Assert.That(LookSetInCode(field), Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }
    }
}
