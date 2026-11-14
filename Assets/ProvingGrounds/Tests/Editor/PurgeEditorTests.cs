using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// FN-16 in the editor: an effect's Purges in its Tags section, after Removes Effects Tagged; a purge's filter named
    /// without "Purge Filter". And ED-85: an effect only looked at is left as it was, and a flags field stores the flags
    /// picked, where Unity's own field stored a Purges' Applied By of every flag as Everything, -1, as it bound.
    /// </summary>
    public class PurgeEditorTests
    {
        // A runtime panel to build an inspector in, as the Inspector holds one, so its bindings run (ED-12's host)
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("ED-85") { hideFlags = HideFlags.HideAndDontSave };
            private UnityEditor.Editor editor;

            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Inspect(Object target)
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                Document.rootVisualElement.Add(root);
                // The Inspector binds what an editor builds to its SerializedObject
                root.Bind(editor.serializedObject);
                return root;
            }

            public void Destroy()
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        // Lets the panel run its bindings, which the editor's updates drive
        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        // The lines of the one JSON the other lacks, each way
        private static List<string> Changes(string before, string after)
        {
            var was = before.Split('\n').Select(line => line.Trim()).ToList();
            var now = after.Split('\n').Select(line => line.Trim()).ToList();
            return was.Except(now).Select(line => "- " + line).Concat(now.Except(was).Select(line => "+ " + line)).ToList();
        }

        /// <summary>
        /// An effect only looked at is left as it was (ED-85). Its Purges' Applied By, Any, every flag, was stored as
        /// Everything, -1, by Unity's own flags field as it bound: every effect the inspector showed changed, a template's
        /// child took it as an override, and the inspector's SerializedObject moved while its bindings were still being
        /// made, which Unity asserted on (IsInSyncWithParentSerializedObject).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_85_An_effect_only_looked_at_is_left_as_it_was()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var before = EditorJsonUtility.ToJson(effect, true);
                host.Inspect(effect);
                yield return Frames();
                var after = EditorJsonUtility.ToJson(effect, true);
                Assert.AreEqual(EPurgeRelation.Any, effect.Purges.AppliedBy, "its Purges' Applied By still Any");
                Assert.That(Changes(before, after), Is.Empty, "the effect's lines that changed");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// A flags field stores the flags picked, of those its type has (ED-85): some, those; every one, Any, not
        /// Everything, -1, which would take a flag the type gains later.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_85_A_flags_field_stores_the_flags_picked()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(effect);
                yield return Frames();
                var field = root.Query<EnumFlagsField>().ToList().FirstOrDefault(each => each.label == "Applied By");
                Assert.IsNotNull(field, "the Purges' Applied By field");

                field.value = EPurgeRelation.Self | EPurgeRelation.Enemies;
                yield return Frames();
                Assert.AreEqual(EPurgeRelation.Self | EPurgeRelation.Enemies, effect.Purges.AppliedBy, "some flags, those");

                field.value = (EPurgeRelation)~0;
                yield return Frames();
                Assert.AreEqual(EPurgeRelation.Any, effect.Purges.AppliedBy, "Everything, every flag it has: Any");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// Each flags field of PlayForge's has a drawer of its own (ED-85), so none is drawn by Unity's field, which writes
        /// as it binds: a new one fails here until it has one.
        /// </summary>
        [Test]
        public void ED_85_Each_flags_field_has_a_drawer_of_its_own()
        {
            var drawerType = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(drawerType, Is.Not.Null, "a drawer names the type it draws");
            var drawn = typeof(PurgeFilterDrawer).Assembly.GetTypes()
                .SelectMany(type => type.GetCustomAttributes<CustomPropertyDrawer>(false))
                .Select(attribute => drawerType.GetValue(attribute) as System.Type)
                .Where(type => type != null).ToHashSet();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var flags = typeof(GameplayEffect).Assembly.GetTypes()
                .Where(type => type.IsSerializable || typeof(Object).IsAssignableFrom(type))
                .SelectMany(type => type.GetFields(fields))
                .Where(field => !field.IsNotSerialized && (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null))
                .Select(field => field.FieldType)
                .Where(type => type.IsEnum && type.GetCustomAttribute<System.FlagsAttribute>() != null)
                .Distinct().ToList();
            Assume.That(flags, Is.Not.Empty, "PlayForge has flags fields");
            var undrawn = flags.Where(type => !drawn.Contains(type)).Select(type => type.Name).OrderBy(name => name).ToList();
            Assert.That(undrawn, Is.Empty, "flags fields Unity's own field draws");
        }

        [Test]
        public void FN_16_An_effects_purges_are_in_its_tags_section()
        {
            var dispel = ScriptableObject.CreateInstance<GameplayEffect>();
            var editor = UnityEditor.Editor.CreateEditor(dispel);
            try
            {
                var root = editor.CreateInspectorGUI();
                var fields = root.Q(name: "Tags_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(fields.IndexOf("Purges"), Is.EqualTo(fields.IndexOf("RemovesEffectsTagged") + 1),
                    $"its Purges after its Removes Effects Tagged: {string.Join(", ", fields)}");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(dispel);
            }
        }

        [Test]
        public void FN_16_A_purges_filter_reads_by_its_kind()
        {
            var clean = typeof(PurgeFilterDrawer).GetMethod("CleanTypeName",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.AreEqual("ExceptTagged", clean.Invoke(new PurgeFilterDrawer(), new object[] { nameof(ExceptTaggedPurgeFilter) }),
                "Except Tagged, named without Purge Filter");
        }
    }
}
