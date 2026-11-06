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
    /// The tag field (ED-55, the user's report): Unity's field, its label in the label column, the tag's parents in
    /// secondary text and its leaf in strong text, by its display name, which opens Unity's searchable dropdown of the
    /// tags its context offers. It was a button with a colored border, text that changed on hover, a label it dropped
    /// inside a list, and a picker drawn in tinted boxes in code. Each field is drawn in a runtime panel, since batch mode
    /// has no graphics device to give an editor window one.
    /// </summary>
    public class TagFieldTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string FullPathKey = "PlayForge_ShowFullTagPath";
        private static readonly string RetentionPath = $"{nameof(GameplayEffect.Definition)}.{nameof(GameplayEffectDefinition.RetentionGroup)}";

        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("ED-55") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public PropertyField Draw(SerializedObject serialized, string path, string label)
            {
                var field = new PropertyField(serialized.FindProperty(path), label);
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

        private bool hadFullPath;
        private bool fullPathWas;

        [SetUp]
        public void Keep()
        {
            hadFullPath = EditorPrefs.HasKey(FullPathKey);
            fullPathWas = EditorPrefs.GetBool(FullPathKey);
        }

        [TearDown]
        public void Restore()
        {
            if (hadFullPath) EditorPrefs.SetBool(FullPathKey, fullPathWas);
            else EditorPrefs.DeleteKey(FullPathKey);
        }

        private static GameplayEffect Effect(Tag group)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition.Name = "Chill";
            effect.Definition.RetentionGroup = group;
            return effect;
        }

        private static string Text(VisualElement field, string name) => field.Q<Label>(name)?.text;

        /// <summary>
        /// A tag field is Unity's field: its label in the label column, the tag's parents in secondary text and its leaf
        /// in strong text, or the whole path with the full-path setting on; a unique tag by its display name, not its
        /// padded name; None for no tag. It sets no look in code.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_55_A_tag_field_shows_its_parents_and_leaf()
        {
            var effect = Effect(Tag.Generate("Status.Chilled"));
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                EditorPrefs.SetBool(FullPathKey, false);
                var serialized = new SerializedObject(effect);
                var field = host.Draw(serialized, RetentionPath, "Retention Group");
                yield return Frames();

                Assert.AreEqual("Retention Group", field.Q<Label>(className: "unity-base-field__label")?.text, "its label is in the label column");
                Assert.AreEqual("Status.", Text(field, "Parents"), "its parents");
                Assert.AreEqual("Chilled", Text(field, "Leaf"), "and its leaf");
                Assert.That(ForgeInspectorTests.LookSetInCode(field).Concat(field.Query<VisualElement>().ToList().SelectMany(ForgeInspectorTests.LookSetInCode)),
                    Is.Empty, "it sets no look in code");

                EditorPrefs.SetBool(FullPathKey, true);
                field.RemoveFromHierarchy();
                field = host.Draw(serialized, RetentionPath, "Retention Group");
                yield return Frames();
                Assert.AreEqual("Status.Chilled", Text(field, "Leaf"), "with the full-path setting on, the whole path");

                EditorPrefs.SetBool(FullPathKey, false);
                effect.Definition.RetentionGroup = Tag.GenerateAsUnique("Status.Frozen");
                serialized.Update();
                field.RemoveFromHierarchy();
                field = host.Draw(serialized, RetentionPath, "Retention Group");
                yield return Frames();
                Assert.AreEqual("Frozen", Text(field, "Leaf"), "a unique tag by its display name, not its padded name");

                effect.Definition.RetentionGroup = default;
                serialized.Update();
                field.RemoveFromHierarchy();
                field = host.Draw(serialized, RetentionPath, "Retention Group");
                yield return Frames();
                Assert.AreEqual("None", Text(field, "Leaf"), "and no tag as None");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>A tag picked goes into the field, its name and its display name, as one Undo step.</summary>
        [Test]
        public void ED_55_A_picked_tag_is_one_undo_step()
        {
            var effect = Effect(Tag.Generate("Status.Chilled"));
            try
            {
                var serialized = new SerializedObject(effect);
                var pick = typeof(TagDrawer).GetMethod("Pick", AnyStatic);
                Assert.IsNotNull(pick, "the drawer picks a tag");
                var frozen = Tag.GenerateAsUnique("Status.Frozen");
                Undo.IncrementCurrentGroup();
                pick.Invoke(null, new object[] { serialized.FindProperty(RetentionPath), frozen, new string[0] });
                Assert.AreEqual(frozen.Name, effect.Definition.RetentionGroup.Name, "the field takes the tag");
                Assert.AreEqual("Status.Frozen", effect.Definition.RetentionGroup.DisplayName, "with its display name");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Status.Chilled", effect.Definition.RetentionGroup.Name, "and one Undo takes it back");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// The picker lists the tags by their display names, each under its root while Group Tags by Root is on and its
        /// root has more than one, and in one list otherwise, in natural order.
        /// </summary>
        [Test]
        public void ED_55_The_picker_lists_tags_by_display_name_under_their_roots()
        {
            var picker = typeof(TagDrawer).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTagPicker");
            Assert.IsNotNull(picker, "the field has a picker");
            var rows = picker.GetMethod("Rows", AnyStatic);
            Assert.IsNotNull(rows, "which lists its rows");
            var tags = new List<Tag> { Tag.Generate("Status.Chilled"), Tag.GenerateAsUnique("Status.Frozen"), Tag.Generate("Damage.Fire") };
            Assert.That((List<string>)rows.Invoke(null, new object[] { tags, true }),
                Is.EqualTo(new[] { "Damage.Fire", "Status/Status.Chilled", "Status/Status.Frozen" }), "under their roots");
            Assert.That((List<string>)rows.Invoke(null, new object[] { tags, false }),
                Is.EqualTo(new[] { "Damage.Fire", "Status.Chilled", "Status.Frozen" }), "or in one list");
        }
    }
}
