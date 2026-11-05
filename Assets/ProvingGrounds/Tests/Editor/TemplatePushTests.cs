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
    /// Templates, as prefabs work (Q23, ED-13): a child of a template asset takes the template's values when the
    /// template is pushed to its children, and keeps each field it overrides, which its inspector marks. Saving a
    /// template opened a dialog in the middle of the save, loaded every asset of its type, and changed the children with
    /// no Undo.
    /// </summary>
    public class TemplatePushTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<string> made = new();

        private const string ChainFolder = "Assets/ED-44";

        [TearDown]
        public void Clean()
        {
            foreach (var path in made) AssetDatabase.DeleteAsset(path);
            made.Clear();
            if (AssetDatabase.IsValidFolder(ChainFolder)) AssetDatabase.DeleteAsset(ChainFolder);
        }

        /// <summary>
        /// A push found each child's own children by searching every asset of its type in the project and loading each one,
        /// so a push down a family searched the project once for every member of it: down a chain of 20, among 1,000 other
        /// abilities, it took about twice as long as among 100, and a push to 2,000 children loaded 2,000 assets 2,000
        /// times. It searches the project once.
        /// </summary>
        [Test]
        public void ED_44_A_push_searches_the_project_once_however_big_the_family()
        {
            double few = PushDownAChain(100), many = PushDownAChain(1000);

            TestContext.WriteLine($"ms to push down a chain of 20, among 100 → 1,000 other abilities: {few:F1} → {many:F1}");
            Assert.That(many, Is.LessThan(1.5 * few), "the push takes about as long however many other abilities there are");
        }

        // Milliseconds to push the head of a chain of 20 abilities, each the template of the next, among `others`
        // abilities: the fastest of three pushes
        private double PushDownAChain(int others)
        {
            if (AssetDatabase.IsValidFolder(ChainFolder)) AssetDatabase.DeleteAsset(ChainFolder);
            AssetDatabase.CreateFolder("Assets", "ED-44");
            var chain = new List<Ability>();
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < others; i++) Chained($"Other {i}");
                for (int i = 0; i < 20; i++)
                {
                    var link = Chained($"Link {i}");
                    if (i > 0)
                    {
                        link.SetTemplate(chain[i - 1]);
                        EditorUtility.SetDirty(link);
                    }
                    chain.Add(link);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();

            double fastest = double.MaxValue;
            for (int round = 0; round < 3; round++)
            {
                Edit(chain[0], nameof(Ability.MaxLevel), 9 + round);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var pushed = Push(chain[0]);
                watch.Stop();
                fastest = System.Math.Min(fastest, watch.Elapsed.TotalMilliseconds);
                Assert.AreEqual(19, pushed.Count, "the push reaches each link down the chain");
                Assert.That(chain.All(link => link.MaxLevel == 9 + round), "and each takes the head's value");
            }
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(ChainFolder);
            return fastest;
        }

        private static Ability Chained(string name)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = name;
            ability.MaxLevel = 4;
            AssetDatabase.CreateAsset(ability, $"{ChainFolder}/{name}.asset");
            return ability;
        }

        private Ability Save(string name, int maxLevel)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = name;
            ability.MaxLevel = maxLevel;
            var path = $"Assets/ED-13 {name}.asset";
            AssetDatabase.CreateAsset(ability, path);
            made.Add(path);
            return ability;
        }

        // A template and its children, each set as an inspector's Set Template sets it
        private (Ability template, Ability first, Ability second) Family()
        {
            var template = Save("Template", 4);
            var first = Save("First Child", 4);
            var second = Save("Second Child", 4);
            first.SetTemplate(template);
            second.SetTemplate(template);
            EditorUtility.SetDirty(first);
            EditorUtility.SetDirty(second);
            AssetDatabase.SaveAssets();
            return (template, first, second);
        }

        // Edits a field as its inspector does, through a SerializedObject, then flushes the edit's modifications, as the
        // editor does at the end of each frame
        private static void Edit(Object asset, string path, int value)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty(path).intValue = value;
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
        }

        private static List<BaseForgeAsset> Push(BaseForgeAsset template)
        {
            var push = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTemplatePush");
            Assert.IsNotNull(push, "a template can be pushed to its children");
            return (List<BaseForgeAsset>)push.GetMethod("PushToChildren", AnyStatic).Invoke(null, new object[] { template });
        }

        /// <summary>Saving a template changes no child, and asks nothing: a push does, when asked for.</summary>
        [Test]
        public void ED_13_Saving_a_template_changes_no_child()
        {
            var (template, first, _) = Family();
            Edit(template, nameof(Ability.MaxLevel), 9);
            AssetDatabase.SaveAssets();
            Assert.AreEqual(4, first.MaxLevel, "the child keeps its value until the template is pushed");
        }

        /// <summary>
        /// An edit to a child's field makes it the child's own, and Push to Children copies the template into each child
        /// but the fields it overrides. One Undo takes the push back from every child.
        /// </summary>
        [Test]
        public void ED_13_Push_to_Children_keeps_each_childs_overrides()
        {
            var (template, first, second) = Family();
            Edit(first, nameof(Ability.MaxLevel), 7);
            Assert.IsTrue(first.IsFieldOverridden(nameof(Ability.MaxLevel)), "an edit to a child's field makes it the child's own");

            Edit(template, nameof(Ability.MaxLevel), 9);
            Edit(template, nameof(Ability.StartingLevel), 2);
            Assert.IsFalse(template.IsFieldOverridden(nameof(Ability.MaxLevel)), "a template's own edits override nothing");

            Undo.IncrementCurrentGroup();
            var pushed = Push(template);
            Assert.That(pushed, Has.Member(first).And.Member(second), "the push reaches each child");
            Assert.AreEqual(9, second.MaxLevel, "a child takes the template's value");
            Assert.AreEqual(7, first.MaxLevel, "but not a field it overrides");
            Assert.AreEqual(2, first.StartingLevel, "and takes the rest");
            Assert.IsFalse(second.IsFieldOverridden(nameof(Ability.MaxLevel)), "and the push itself overrides nothing");

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreEqual(4, second.MaxLevel, "one Undo takes it back");
            Assert.AreEqual(1, first.StartingLevel, "from every child");
        }

        /// <summary>
        /// One Undo takes back an edit to a child's field and the override it made, as it does a prefab instance's, and each
        /// Undo takes back its own edit's override alone.
        /// </summary>
        [Test]
        public void ED_13_One_Undo_takes_back_an_edit_and_its_override()
        {
            var (_, first, _) = Family();
            var before = EditorJsonUtility.ToJson(first);
            Undo.IncrementCurrentGroup();
            Edit(first, nameof(Ability.MaxLevel), 7);
            var afterMaxLevel = EditorJsonUtility.ToJson(first);
            Undo.IncrementCurrentGroup();
            Edit(first, nameof(Ability.StartingLevel), 2);
            Assert.IsTrue(first.IsFieldOverridden(nameof(Ability.MaxLevel)) && first.IsFieldOverridden(nameof(Ability.StartingLevel)),
                "each edit makes its field the child's own");

            Undo.PerformUndo();
            Assert.AreEqual(1, first.StartingLevel, "one Undo takes the last edit back");
            Assert.IsFalse(first.IsFieldOverridden(nameof(Ability.StartingLevel)), "and its override");
            Assert.IsTrue(first.IsFieldOverridden(nameof(Ability.MaxLevel)), "but not the one before");
            Assert.AreEqual(afterMaxLevel, EditorJsonUtility.ToJson(first), "so the child is as it was after the first edit");

            Undo.PerformUndo();
            Assert.AreEqual(4, first.MaxLevel, "the next Undo takes the first edit back");
            Assert.IsFalse(first.IsFieldOverridden(nameof(Ability.MaxLevel)), "and its override");
            Assert.AreEqual(before, EditorJsonUtility.ToJson(first), "so the child is as it was");

            Undo.PerformRedo();
            Assert.AreEqual(7, first.MaxLevel, "Redo makes the edit again");
            Assert.IsTrue(first.IsFieldOverridden(nameof(Ability.MaxLevel)), "and its override");
            Assert.IsFalse(first.IsFieldOverridden(nameof(Ability.StartingLevel)), "but not the next edit's");
            Assert.AreEqual(afterMaxLevel, EditorJsonUtility.ToJson(first), "so the child is as it was after the first edit");

            Undo.PerformRedo();
            Assert.AreEqual(2, first.StartingLevel, "the next Redo makes the next edit again");
            Assert.IsTrue(first.IsFieldOverridden(nameof(Ability.StartingLevel)), "and its override");

            Undo.PerformUndo();
            Assert.IsFalse(first.IsFieldOverridden(nameof(Ability.StartingLevel)), "and an Undo after a Redo takes it back again");
            Assert.IsTrue(first.IsFieldOverridden(nameof(Ability.MaxLevel)), "and leaves the one before");
        }

        /// <summary>A child's inspector marks each field it overrides, as a prefab instance's does, and Revert takes it back.</summary>
        [UnityTest]
        public IEnumerator ED_13_A_childs_inspector_marks_its_overrides()
        {
            var (template, first, _) = Family();
            Edit(first, nameof(Ability.MaxLevel), 7);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("ED-13") { hideFlags = HideFlags.HideAndDontSave };
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = settings;
            UnityEditor.Editor editor = null;
            try
            {
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(first);
                var root = editor.CreateInspectorGUI();
                document.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                for (int i = 0; i < 4; i++) yield return null;

                Assert.IsTrue(root.Q(nameof(Ability.MaxLevel)).ClassListContains("pf-override"), "Max Level is marked as the child's own");
                Assert.IsFalse(root.Q(nameof(Ability.StartingLevel)).ClassListContains("pf-override"), "Starting Level isn't");

                var revert = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTemplatePush")
                    .GetMethod("RevertToTemplate", AnyStatic);
                revert.Invoke(null, new object[] { editor.serializedObject.FindProperty(nameof(Ability.MaxLevel)) });
                for (int i = 0; i < 4; i++) yield return null;
                Assert.AreEqual(4, first.MaxLevel, "Revert to Template takes the template's value");
                Assert.IsFalse(first.IsFieldOverridden(nameof(Ability.MaxLevel)), "and the field is the template's again");
                Assert.IsFalse(root.Q(nameof(Ability.MaxLevel)).ClassListContains("pf-override"), "so its mark goes");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
