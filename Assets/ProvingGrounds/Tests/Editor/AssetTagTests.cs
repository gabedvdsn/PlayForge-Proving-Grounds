using System;
using System.Collections;
using System.Collections.Generic;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The asset tag each asset inspector derives from the asset's name. Its fields send their events through a runtime
    /// panel, since batch mode has no graphics device to give an editor window one.
    /// </summary>
    public class AssetTagTests
    {
        // An asset type: how to make one with a name, and how to read and write its stored asset tag
        public sealed class Kind
        {
            public string Label;
            public Func<ScriptableObject> Make;
            public Action<ScriptableObject, string> Name;
            public Func<ScriptableObject, Tag> Get;
            public Action<ScriptableObject, Tag> Set;
            public override string ToString() => Label;
        }

        public static IEnumerable<Kind> Kinds()
        {
            yield return new Kind
            {
                Label = "Ability", Make = ScriptableObject.CreateInstance<Ability>,
                Name = (a, n) => ((Ability)a).Definition.Name = n,
                Get = a => ((Ability)a).Tags.AssetTag,
                Set = (a, t) => { var tags = ((Ability)a).Tags; tags.AssetTag = t; ((Ability)a).Tags = tags; },
            };
            yield return new Kind
            {
                Label = "Effect", Make = ScriptableObject.CreateInstance<GameplayEffect>,
                Name = (a, n) => ((GameplayEffect)a).Definition.Name = n,
                Get = a => ((GameplayEffect)a).Tags.AssetTag,
                Set = (a, t) => { var tags = ((GameplayEffect)a).Tags; tags.AssetTag = t; ((GameplayEffect)a).Tags = tags; },
            };
            yield return new Kind
            {
                Label = "Item", Make = ScriptableObject.CreateInstance<Item>,
                Name = (a, n) => ((Item)a).Definition.Name = n,
                Get = a => ((Item)a).Tags.AssetTag,
                Set = (a, t) => { var tags = ((Item)a).Tags; tags.AssetTag = t; ((Item)a).Tags = tags; },
            };
            yield return new Kind
            {
                Label = "Entity", Make = ScriptableObject.CreateInstance<EntityIdentity>,
                Name = (a, n) => ((EntityIdentity)a).Name = n,
                Get = a => ((EntityIdentity)a).AssetTag,
                Set = (a, t) => ((EntityIdentity)a).AssetTag = t,
            };
            yield return new Kind
            {
                Label = "AttributeSet", Make = ScriptableObject.CreateInstance<AttributeSet>,
                Name = (a, n) => ((AttributeSet)a).Name = n,
                Get = a => ((AttributeSet)a).AssetTag,
                Set = (a, t) => ((AttributeSet)a).AssetTag = t,
            };
        }

        private static Tag Stale()
        {
            var tag = default(Tag);
            tag.Name = "Stale";
            tag.DisplayName = "Stale";
            return tag;
        }

        private static (UnityEditor.Editor editor, VisualElement root) Inspect(Object asset)
        {
            var editor = UnityEditor.Editor.CreateEditor(asset);
            var root = editor.CreateInspectorGUI();
            Assert.IsNotNull(root, "the inspector builds");
            return (editor, root);
        }

        /// <summary>
        /// Every asset inspector rewrote the asset's tag from its name each time it built or refreshed, with no Undo and
        /// no modified mark, so merely looking at an asset changed it.
        /// </summary>
        [TestCaseSource(nameof(Kinds))]
        public void ED_12_Viewing_an_asset_never_writes_its_tag(Kind kind)
        {
            var asset = kind.Make();
            UnityEditor.Editor editor = null;
            try
            {
                kind.Name(asset, "Fire Ball");
                kind.Set(asset, Stale());

                VisualElement root;
                (editor, root) = Inspect(asset);

                Assert.AreEqual("Stale", kind.Get(asset).Name, "viewing left the stored tag alone");
                var update = root.Q<Button>("AssetTagUpdate");
                Assert.IsNotNull(update, "a stale tag offers Update");
                Assert.AreEqual(DisplayStyle.Flex, update.style.display.value, "and shows it");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>Renaming wrote the name, and then the tag, straight into the asset, so Undo took back neither.</summary>
        [UnityTest]
        public IEnumerator ED_12_Renaming_stores_the_tag_in_the_same_undo_step()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("ED-12") { hideFlags = HideFlags.HideAndDontSave };
            UnityEditor.Editor editor = null;
            try
            {
                ability.Definition.Name = "Fire Ball";
                var original = ability.Tags.AssetTag;

                var document = host.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                VisualElement root;
                (editor, root) = Inspect(ability);
                var nameField = root.Q<TextField>("Name");
                document.rootVisualElement.Add(nameField);

                Undo.IncrementCurrentGroup();
                nameField.value = "Ice Lance";
                using (var focusOut = FocusOutEvent.GetPooled(nameField, null, FocusChangeDirection.unspecified,
                           document.rootVisualElement.panel.focusController))
                    nameField.SendEvent(focusOut);

                Assert.AreEqual("Ice Lance", ability.Definition.Name, "renamed");
                Assert.That(ability.Tags.AssetTag.Name, Does.Contain("IceLance"), "the tag follows the name");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();

                Assert.AreEqual("Fire Ball", ability.Definition.Name, "one Undo takes back the name");
                // Undo restores the serialized tag, and a null string serializes as an empty one
                Assert.AreEqual(original.Name ?? "", ability.Tags.AssetTag.Name ?? "", "and the tag");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(panelSettings);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>A stale tag's Update button stores the tag the name gives, and one Undo takes it back.</summary>
        [UnityTest]
        public IEnumerator ED_12_Update_stores_a_stale_tag_as_one_undoable_change()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("ED-12") { hideFlags = HideFlags.HideAndDontSave };
            UnityEditor.Editor editor = null;
            try
            {
                ability.Definition.Name = "Fire Ball";
                var tags = ability.Tags;
                tags.AssetTag = Stale();
                ability.Tags = tags;

                var document = host.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                VisualElement root;
                (editor, root) = Inspect(ability);
                var update = root.Q<Button>("AssetTagUpdate");
                Assert.IsNotNull(update, "a stale tag offers Update");
                document.rootVisualElement.Add(update);

                Undo.IncrementCurrentGroup();
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = update;
                    update.SendEvent(submit);
                }

                Assert.That(ability.Tags.AssetTag.Name, Does.Contain("FireBall"), "stored");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();

                Assert.AreEqual("Stale", ability.Tags.AssetTag.Name, "one Undo takes it back");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(panelSettings);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// Renaming an asset in its inspector left its file as it was, so the two drifted apart (the user's request, ED-70).
        /// The file follows the name, with its type's prefix and suffix: a character a file can't have is left out, a name
        /// another file in its folder has takes a number, and an Undo of the rename brings the file back with the name.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_70_Renaming_an_asset_renames_its_file()
        {
            const string folder = "Assets/ED70Temp";
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("ED-70") { hideFlags = HideFlags.HideAndDontSave };
            UnityEditor.Editor editor = null;
            string Expected(string file) => $"{PlayForgeSettings.PrefixFor(typeof(Ability))}{file}{PlayForgeSettings.SuffixFor(typeof(Ability))}";
            try
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED70Temp");
                var taken = ScriptableObject.CreateInstance<Ability>();
                taken.Definition.Name = "ED70 Taken";
                AssetDatabase.CreateAsset(taken, $"{folder}/{Expected("ED70Taken")}.asset");
                var ability = ScriptableObject.CreateInstance<Ability>();
                ability.Definition.Name = "ED70 Fire";
                AssetDatabase.CreateAsset(ability, $"{folder}/{Expected("ED70Fire")}.asset");
                string FileOf() => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(ability));

                var document = host.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                VisualElement root;
                (editor, root) = Inspect(ability);
                var nameField = root.Q<TextField>("Name");
                document.rootVisualElement.Add(nameField);
                void Rename(string name)
                {
                    Undo.IncrementCurrentGroup();
                    nameField.value = name;
                    using var focusOut = FocusOutEvent.GetPooled(nameField, null, FocusChangeDirection.unspecified,
                        document.rootVisualElement.panel.focusController);
                    nameField.SendEvent(focusOut);
                }

                Rename("ED70 Blaze");
                Assert.AreEqual(Expected("ED70Blaze"), FileOf(), "the file follows the name");
                Rename("ED70: Ash?");
                Assert.AreEqual(Expected("ED70Ash"), FileOf(), "leaving out what a file name can't have");
                Rename("ED70 Taken");
                Assert.AreEqual($"{Expected("ED70Taken")} 1", FileOf(), "a name another file has takes a number");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("ED70: Ash?", ability.Definition.Name, "an Undo takes back the name");
                Assert.AreEqual(Expected("ED70Ash"), FileOf(), "and the file follows it back");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(panelSettings);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
