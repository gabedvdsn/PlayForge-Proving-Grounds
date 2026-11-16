using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Editor;
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
    /// PlayForge's settings: the project's, on Project Settings → PlayForge and kept in its Forge Project since TW-30 (in
    /// ProjectSettings/PlayForgeSettings.asset before), and each user's, on Preferences → PlayForge and kept in EditorPrefs.
    /// Each test works in a Forge Project of its own, and puts back the settings and the EditorPrefs entries it touches,
    /// since EditorPrefs is the machine's.
    /// </summary>
    public class PlayForgeSettingsTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string ProjectFolder = "Assets/ED-21 Project";

        // The project the tests write to, of their own, whose asset holds the project's settings (TW-30)
        private ForgeProject project;
        private ForgeProject previousProject;
        private string SettingsFile => AssetDatabase.GetAssetPath(project);
        private const string Prefix = "PlayForge_";

        // The EditorPrefs entries the tests touch, by kind
        private static readonly string[] StringKeys =
        {
            // The tables' column widths (ED-48), and the columns they hide (FN-7), which Use Defaults forgets
            Prefix + "Columns_Debugger.Entities", Prefix + "Columns_Debugger.Processes", Prefix + "Columns_Debugger.Tags",
            Prefix + "Columns_Debugger.Timeline", Prefix + "Columns_Forge.Assets", Prefix + "Columns_Forge.Tags", Prefix + "Columns_Problems",
            Prefix + "HiddenColumns_Debugger.Entities", Prefix + "HiddenColumns_Debugger.Processes", Prefix + "HiddenColumns_Debugger.Tags",
            Prefix + "HiddenColumns_Debugger.Timeline", Prefix + "HiddenColumns_Forge.Assets", Prefix + "HiddenColumns_Forge.Tags",
            Prefix + "HiddenColumns_Problems",
        };
        private static readonly string[] BoolKeys =
        {
            Prefix + "ShowFullTagPath", Prefix + "GroupTagsByRoot",
        };
        private static readonly string[] IntKeys = { Prefix + "ForgeLogMinLevel" };

        private readonly Dictionary<string, object> kept = new();
        private readonly List<string> made = new();

        [SetUp]
        public void Keep()
        {
            kept.Clear();
            foreach (var key in StringKeys) kept[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
            foreach (var key in BoolKeys) kept[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetBool(key) : null;
            foreach (var key in IntKeys) kept[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetInt(key) : null;
            Assume.That(AssetDatabase.IsValidFolder(ProjectFolder), Is.False, "the project's folder is free");
            AssetDatabase.CreateFolder("Assets", "ED-21 Project");
            project = ScriptableObject.CreateInstance<ForgeProject>();
            AssetDatabase.CreateAsset(project, $"{ProjectFolder}/Forge Project.asset");
            previousProject = PlayForgeSettings.UseProject(project);
        }

        [TearDown]
        public void Restore()
        {
            foreach (var (key, value) in kept)
            {
                switch (value)
                {
                    case string s: EditorPrefs.SetString(key, s); break;
                    case bool b: EditorPrefs.SetBool(key, b); break;
                    case int i: EditorPrefs.SetInt(key, i); break;
                    default: EditorPrefs.DeleteKey(key); break;
                }
            }
            ForgeLog.MinimumLevel = ForgeLogSettingsBridge.CurrentLevel;
            PlayForgeSettings.UseProject(previousProject);
            AssetDatabase.DeleteAsset(ProjectFolder);
            foreach (var path in made) AssetDatabase.DeleteAsset(path);
            made.Clear();
        }

        private T Make<T>(string path) where T : ScriptableObject
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), path);
            made.Add(path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static Type Pages => typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Editor.PlayForgeSettingsPages");

        // A runtime panel to build a page in, since batch mode can't give the Settings window one
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("ED-21") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Build(string page)
            {
                var root = new VisualElement();
                Document.rootVisualElement.Add(root);
                Pages.GetMethod(page, AnyStatic).Invoke(null, new object[] { root });
                return root;
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

        /// <summary>
        /// A new asset's file is its name alone (the user's note of 2026-11-15, EP-13): a type's new assets have a folder and
        /// no file prefix or suffix, and the page's New Assets table shows each type's folder alone. A file of a new asset
        /// and a renamed one took its type's prefix and suffix, which the table set.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_13_New_assets_have_no_prefix_or_suffix()
        {
            Assert.IsNull(typeof(NewAssetSettings).GetField("Prefix"), "a type's new assets have no prefix");
            Assert.IsNull(typeof(NewAssetSettings).GetField("Suffix"), "nor a suffix");
            Assert.IsNull(typeof(PlayForgeSettings).GetMethod("PrefixFor", AnyStatic), "so the settings read none");
            Assert.IsNull(typeof(PlayForgeSettings).GetMethod("SuffixFor", AnyStatic), "of either");

            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Build("BuildProjectPage");
                yield return Frames();
                var table = root.Q("NewAssets");
                Assert.IsNotNull(table, "the page has its New Assets table");
                Assert.IsNull(table.Q("Prefix"), "with no Prefix field");
                Assert.IsNull(table.Q("Suffix"), "nor a Suffix field");
                Assert.That(table.Query<Label>().ToList().Select(label => label.text), Has.None.EqualTo("Prefix").And.None.EqualTo("Suffix"),
                    "and no column for either");
                Assert.IsNotNull(root.Q<Button>("UseDefaultFolders"), "Use Default Folders puts the folders back");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>
        /// Every setting was an EditorPref, which every project on the machine shares (ED-21). A folder set now is the
        /// project's: it's written to its Forge Project's asset (TW-30), which travels with the project, and the machine's
        /// EditorPrefs are left as they were.
        /// </summary>
        [Test]
        public void ED_21_A_shared_setting_belongs_to_the_project()
        {
            var machine = EditorPrefs.GetString(Prefix + "Path_Ability", "unset");

            PlayForgeSettings.SetNewAssets(typeof(Ability), "Assets/ED-21 Abilities");

            Assert.AreEqual("Assets/ED-21 Abilities", PlayForgeSettings.FolderFor(typeof(Ability)), "new abilities go there");
            Assert.AreEqual(machine, EditorPrefs.GetString(Prefix + "Path_Ability", "unset"), "the machine's EditorPrefs stay as they were");
            Assert.IsTrue(File.Exists(SettingsFile), "the project's asset keeps its settings");
            StringAssert.Contains("ED-21 Abilities", File.ReadAllText(SettingsFile), "which holds the folder");
        }

        /// <summary>
        /// Project Settings → PlayForge shows each type's folder and file names, its defaults and the Cost and Cooldown
        /// effects, bound to the project's settings, so an edit is saved in its asset and one Undo takes it back (ED-21,
        /// TW-30).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_21_The_Project_Settings_page_edits_the_shared_settings()
        {
            var ability = Make<Ability>("Assets/ED-21 Default.asset");
            PlayForgeSettings.SetNewAssets(typeof(Ability), "Assets/ED-21 Before");
            PlayForgeSettings.SetDefaults(typeof(Ability), ability, new ScriptableObject[] { ability });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Build("BuildProjectPage");
                yield return Frames();

                var row = root.Q("NewAssets").Q(nameof(Ability));
                Assert.IsNotNull(row, "a row for abilities");
                var folder = row.Q<TextField>("Folder");
                Assert.AreEqual("Assets/ED-21 Before", folder.value, "showing their folder");
                var defaults = root.Q<Foldout>(nameof(Ability));
                Assert.AreSame(ability, defaults.Q<ObjectField>("NewAsset").value, "and their New Asset default");
                Assert.IsNotNull(root.Q<ObjectField>("CostEffect"), "and the Cost effect");

                Undo.IncrementCurrentGroup();
                folder.value = "Assets/ED-21 After";
                yield return Frames();
                Assert.AreEqual("Assets/ED-21 After", PlayForgeSettings.FolderFor(typeof(Ability)), "the edit is the setting");
                StringAssert.Contains("ED-21 After", File.ReadAllText(SettingsFile), "and it's saved");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                yield return Frames();
                Assert.AreEqual("Assets/ED-21 Before", PlayForgeSettings.FolderFor(typeof(Ability)), "one Undo takes it back");
                StringAssert.Contains("ED-21 Before", File.ReadAllText(SettingsFile), "and saves it again");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>
        /// Preferences → PlayForge shows each user's view toggles, the log level among them, and Use Defaults puts every one
        /// back. Reset All on The Forge's Settings tab missed some (ED-6), and the tab showed Group Tags by Root off for an
        /// unset value, which the tag field treats as on (ED-25).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_21_The_Preferences_page_edits_each_users_toggles()
        {
            EditorPrefs.DeleteKey(Prefix + "GroupTagsByRoot");
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Build("BuildPreferencesPage");
                yield return Frames();

                var used = (bool)typeof(TagDrawer).GetMethod("GroupByRoot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(new TagDrawer(), null);
                Assert.AreEqual(used, root.Q<Toggle>("GroupTagsByRoot").value, "Group Tags by Root shows what the tag field uses");

                // The Forge's list shows no file names, so the page has no switch for them (ED-58)
                Assert.IsNull(root.Q<Toggle>("ShowFileNames"), "the page has no Show File Names");
                var fullPaths = root.Q<Toggle>("ShowFullTagPath");
                fullPaths.value = true;
                Assert.IsTrue(EditorPrefs.GetBool(Prefix + "ShowFullTagPath", false), "a toggle sets its preference");
                foreach (var key in BoolKeys) EditorPrefs.SetBool(key, !EditorPrefs.GetBool(key, true));
                EditorPrefs.SetString(Prefix + "Columns_Forge.Assets", "Type=120");
                EditorPrefs.SetString(Prefix + "HiddenColumns_Forge.Assets", "Type");
                ForgeLogSettingsBridge.SetLevel(ForgeLog.ELevel.Error);

                var useDefaults = root.Q<Button>("UseDefaults");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = useDefaults;
                    useDefaults.SendEvent(submit);
                }

                Assert.That(BoolKeys.Concat(IntKeys).Append(Prefix + "Columns_Forge.Assets")
                        .Append(Prefix + "HiddenColumns_Forge.Assets").Where(EditorPrefs.HasKey),
                    Is.Empty, "Use Defaults leaves no preference set, a table's column widths and hidden columns among them (FN-7)");
                Assert.IsFalse(fullPaths.value, "and the page shows the defaults");
                Assert.AreEqual(ForgeLog.ELevel.Info, ForgeLog.MinimumLevel, "the log level among them");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>
        /// "Display full tag path" changed nothing: the tag field's text returned the full path before the code that read
        /// the setting (ED-25). The field shows the whole path with the setting on, and its parents and its leaf apart
        /// with it off, the parents in secondary text and the leaf in strong text (ED-55); TagFieldTests draws it.
        /// </summary>
        [Test]
        public void ED_25_Show_Full_Tag_Paths_changes_the_tag_field()
        {
            var tag = Tag.Generate("Status.Debuff.Burn");
            var parts = typeof(TagDrawer).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeTagField")?
                .GetMethod("Parts", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(parts, "the tag field says how it reads a tag");
            Assert.AreEqual(("", "Status.Debuff.Burn"), ((string, string))parts.Invoke(null, new object[] { tag, true }), "the full path");
            Assert.AreEqual(("Status.Debuff.", "Burn"), ((string, string))parts.Invoke(null, new object[] { tag, false }), "the parents and the leaf");
        }

        /// <summary>
        /// Use Default Folders and Names puts every type's folder and file names back, and keeps the defaults and the Cost
        /// and Cooldown effects, which are content you chose (ED-6).
        /// </summary>
        [Test]
        public void ED_6_Use_Default_Folders_keeps_the_defaults()
        {
            var ability = Make<Ability>("Assets/ED-6 Default.asset");
            var cost = Make<GameplayEffect>("Assets/ED-6 Cost.asset");
            foreach (var type in new[] { typeof(Ability), typeof(GameplayEffect), typeof(ScalerAsset) })
                PlayForgeSettings.SetNewAssets(type, "Assets/ED-6");
            PlayForgeSettings.SetDefaults(typeof(Ability), ability, new ScriptableObject[] { ability });
            PlayForgeSettings.SetUsageEffect("Cost", cost);

            PlayForgeSettings.UseDefaultFolders();

            Assert.AreEqual(PlayForgeSettings.DefaultFolder(typeof(Ability)), PlayForgeSettings.FolderFor(typeof(Ability)), "abilities' folder");
            Assert.AreEqual("Effects", PlayForgeSettings.FolderFor(typeof(GameplayEffect)), "effects' folder, within the project's (EP-10)");
            Assert.AreEqual(PlayForgeSettings.DefaultFolder(typeof(ScalerAsset)), PlayForgeSettings.FolderFor(typeof(ScalerAsset)), "and Scaler assets'");
            Assert.AreSame(ability, PlayForgeSettings.NewAssetDefault(typeof(Ability)), "the defaults stay");
            Assert.AreSame(cost, PlayForgeSettings.UsageEffect("Cost"), "and so does the Cost effect");
        }
    }
}
