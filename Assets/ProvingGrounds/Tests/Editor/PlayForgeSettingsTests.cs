using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// PlayForge's settings: the project's, on Project Settings → PlayForge and kept in ProjectSettings/PlayForgeSettings.asset,
    /// and each user's, on Preferences → PlayForge and kept in EditorPrefs. Each test puts back the settings and the
    /// EditorPrefs entries it touches, since EditorPrefs is the machine's.
    /// </summary>
    public class PlayForgeSettingsTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string SettingsFile = "ProjectSettings/PlayForgeSettings.asset";
        private const string Prefix = "PlayForge_";
        private static string ProjectTemplatesKey => Prefix + "TemplateRegistry_" + PlayerSettings.productGUID.ToString("N");

        // A template whose asset is in another project, so no GUID here resolves it
        private const string ForeignGuid = "0123456789abcdef0123456789abcdef";

        // The EditorPrefs entries the tests touch, by kind
        private static readonly string[] StringKeys =
        {
            Prefix + "Path_Ability", Prefix + "Prefix_Ability", Prefix + "Postfix_Ability", Prefix + "TemplateRegistry",
            Prefix + "EffectTemplate_Cost", Prefix + "EffectTemplate_Cooldown", Prefix + "LastTypeFilter",
            // The tables' column widths (ED-48), which Use Defaults forgets
            Prefix + "Columns_Debugger.Entities", Prefix + "Columns_Debugger.Processes", Prefix + "Columns_Debugger.Tags",
            Prefix + "Columns_Debugger.Timeline", Prefix + "Columns_Forge.Assets", Prefix + "Columns_Forge.Tags", Prefix + "Columns_Problems",
        };
        private static readonly string[] BoolKeys =
        {
            Prefix + "ShowFullTagPath", Prefix + "GroupTagsByRoot", Prefix + "RememberTypeFilter", Prefix + "ShowFileNames",
            Prefix + "DoubleClickVisualize", Prefix + "AutoRefresh", Prefix + "ShowNotifications",
        };
        private static readonly string[] IntKeys = { Prefix + "RecentlyCreatedRows", Prefix + "ForgeLogMinLevel" };

        private readonly Dictionary<string, object> kept = new();
        private string keptSettings;
        private readonly List<string> made = new();

        [SetUp]
        public void Keep()
        {
            kept.Clear();
            foreach (var key in StringKeys.Append(ProjectTemplatesKey)) kept[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
            foreach (var key in BoolKeys) kept[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetBool(key) : null;
            foreach (var key in IntKeys) kept[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetInt(key) : null;
            keptSettings = EditorJsonUtility.ToJson(PlayForgeSettings.instance);
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
            EditorJsonUtility.FromJsonOverwrite(keptSettings, PlayForgeSettings.instance);
            PlayForgeSettings.instance.SaveSettings();
            foreach (var path in made) AssetDatabase.DeleteAsset(path);
            made.Clear();
        }

        private T Make<T>(string path) where T : ScriptableObject
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), path);
            made.Add(path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static Type Pages => typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.PlayForgeSettingsPages");

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
        /// Every setting was an EditorPref, which every project on the machine shares (ED-21). A folder set now is the
        /// project's: it's written to ProjectSettings/PlayForgeSettings.asset, which travels with the project, and the
        /// machine's EditorPrefs are left as they were.
        /// </summary>
        [Test]
        public void ED_21_A_shared_setting_belongs_to_the_project()
        {
            var machine = EditorPrefs.GetString(Prefix + "Path_Ability", "unset");

            PlayForgeSettings.SetNewAssets(typeof(Ability), "Assets/ED-21 Abilities", "AB_", "_X");

            Assert.AreEqual("Assets/ED-21 Abilities", PlayForgeSettings.FolderFor(typeof(Ability)), "new abilities go there");
            Assert.AreEqual("AB_", PlayForgeSettings.PrefixFor(typeof(Ability)));
            Assert.AreEqual("_X", PlayForgeSettings.SuffixFor(typeof(Ability)));
            Assert.AreEqual(machine, EditorPrefs.GetString(Prefix + "Path_Ability", "unset"), "the machine's EditorPrefs stay as they were");
            Assert.IsTrue(File.Exists(SettingsFile), "the project keeps a settings file");
            StringAssert.Contains("ED-21 Abilities", File.ReadAllText(SettingsFile), "which holds the folder");
        }

        /// <summary>
        /// A project's first use takes the settings The Forge kept in EditorPrefs: each type's folder and file names, the
        /// templates the project registered, which become its defaults, and the Cost and Cooldown templates. A template
        /// whose asset is in another project is left out, and the entry every project shared stays as it was (ED-21, ED-22).
        /// </summary>
        [Test]
        public void ED_21_A_project_takes_the_settings_it_kept_in_EditorPrefs()
        {
            var ability = Make<Ability>("Assets/ED-21 Template.asset");
            var cost = Make<GameplayEffect>("Assets/ED-21 Cost.asset");
            var abilityGuid = AssetDatabase.AssetPathToGUID("Assets/ED-21 Template.asset");
            EditorPrefs.SetString(Prefix + "Path_Ability", "Assets/ED-21 Old");
            EditorPrefs.SetString(Prefix + "Prefix_Ability", "Old_");
            EditorPrefs.SetString(Prefix + "Postfix_Ability", "_1");
            EditorPrefs.SetString(Prefix + "EffectTemplate_Cost", AssetDatabase.AssetPathToGUID("Assets/ED-21 Cost.asset"));
            EditorPrefs.DeleteKey(Prefix + "EffectTemplate_Cooldown");
            EditorPrefs.SetString(ProjectTemplatesKey,
                "{\"Tags\":[],\"Collections\":[{\"TypeName\":\"Ability\",\"Templates\":[" +
                $"{{\"AssetGUID\":\"{abilityGuid}\",\"TemplateTagName\":\"\",\"IsDefault\":true,\"DisplayName\":\"Here\"}}," +
                $"{{\"AssetGUID\":\"{ForeignGuid}\",\"TemplateTagName\":\"\",\"IsDefault\":false,\"DisplayName\":\"Elsewhere\"}}]}}]}}");
            const string shared = "{\"Collections\":[]}";
            EditorPrefs.SetString(Prefix + "TemplateRegistry", shared);

            var data = new PlayForgeSettingsData();
            typeof(PlayForgeSettings).GetMethod("TakeMachineSettings", AnyStatic).Invoke(null, new object[] { data });

            var abilities = data.NewAssets.Single(entry => entry.Type == nameof(Ability));
            Assert.AreEqual("Assets/ED-21 Old", abilities.Folder, "the folder");
            Assert.AreEqual("Old_", abilities.Prefix, "the prefix");
            Assert.AreEqual("_1", abilities.Suffix, "the suffix");
            var defaults = data.Defaults.Single(entry => entry.Type == nameof(Ability));
            Assert.AreSame(ability, defaults.NewAsset, "the default template is the type's New Asset default");
            Assert.That(defaults.Import, Is.EqualTo(new[] { ability }), "its templates are its Import defaults, without another project's");
            Assert.AreSame(cost, data.CostEffect, "the Cost template");
            Assert.IsNull(data.CooldownEffect, "and no Cooldown template, as none was set");
            Assert.AreEqual(shared, EditorPrefs.GetString(Prefix + "TemplateRegistry"), "the shared entry stays for the other projects");
        }

        /// <summary>
        /// Project Settings → PlayForge shows each type's folder and file names, its defaults and the Cost and Cooldown
        /// effects, bound to the settings, so an edit is saved and one Undo takes it back (ED-21).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_21_The_Project_Settings_page_edits_the_shared_settings()
        {
            var ability = Make<Ability>("Assets/ED-21 Default.asset");
            PlayForgeSettings.SetNewAssets(typeof(Ability), "Assets/ED-21 Before", "", "");
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

                var fileNames = root.Q<Toggle>("ShowFileNames");
                fileNames.value = false;
                Assert.IsFalse(EditorPrefs.GetBool(Prefix + "ShowFileNames", true), "a toggle sets its preference");
                foreach (var key in BoolKeys) EditorPrefs.SetBool(key, !EditorPrefs.GetBool(key, true));
                EditorPrefs.SetInt(Prefix + "RecentlyCreatedRows", 3);
                EditorPrefs.SetString(Prefix + "LastTypeFilter", nameof(Ability));
                EditorPrefs.SetString(Prefix + "Columns_Forge.Assets", "Type=120");
                ForgeLogSettingsBridge.SetLevel(ForgeLog.ELevel.Error);

                var useDefaults = root.Q<Button>("UseDefaults");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = useDefaults;
                    useDefaults.SendEvent(submit);
                }

                Assert.That(BoolKeys.Concat(IntKeys).Append(Prefix + "LastTypeFilter").Append(Prefix + "Columns_Forge.Assets").Where(EditorPrefs.HasKey),
                    Is.Empty, "Use Defaults leaves no preference set, a table's column widths among them");
                Assert.IsTrue(fileNames.value, "and the page shows the defaults");
                Assert.AreEqual(ForgeLog.ELevel.Info, ForgeLog.MinimumLevel, "the log level among them");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>
        /// "Display full tag path" changed nothing: the tag field's text returned the full path before the code that read
        /// the setting (ED-25).
        /// </summary>
        [Test]
        public void ED_25_Show_Full_Tag_Paths_changes_the_tag_field()
        {
            var tag = Tag.Generate("Status.Debuff.Burn");
            var drawer = new TagDrawer();
            var text = typeof(TagDrawer).GetMethod("GetDisplayText", BindingFlags.Instance | BindingFlags.NonPublic);

            EditorPrefs.SetBool(Prefix + "ShowFullTagPath", true);
            Assert.AreEqual("Status.Debuff.Burn", text.Invoke(drawer, new object[] { tag }), "the full path");

            EditorPrefs.SetBool(Prefix + "ShowFullTagPath", false);
            Assert.AreEqual("…Burn", text.Invoke(drawer, new object[] { tag }), "the leaf");
        }

        /// <summary>
        /// Use Default Folders and Names puts every type's folder and file names back, and keeps the defaults and the Cost
        /// and Cooldown effects, which are content you chose (ED-6).
        /// </summary>
        [Test]
        public void ED_6_Use_Default_Folders_and_Names_keeps_the_defaults()
        {
            var ability = Make<Ability>("Assets/ED-6 Default.asset");
            var cost = Make<GameplayEffect>("Assets/ED-6 Cost.asset");
            foreach (var type in new[] { typeof(Ability), typeof(GameplayEffect), typeof(ScalerAsset) })
                PlayForgeSettings.SetNewAssets(type, "Assets/ED-6", "P_", "_S");
            PlayForgeSettings.SetDefaults(typeof(Ability), ability, new ScriptableObject[] { ability });
            PlayForgeSettings.SetUsageEffect("Cost", cost);

            PlayForgeSettings.UseDefaultFoldersAndNames();

            Assert.AreEqual(PlayForgeSettings.DefaultFolder(typeof(Ability)), PlayForgeSettings.FolderFor(typeof(Ability)), "abilities' folder");
            Assert.AreEqual("Assets/Data/Effects", PlayForgeSettings.FolderFor(typeof(GameplayEffect)), "effects' folder");
            Assert.AreEqual("", PlayForgeSettings.PrefixFor(typeof(ScalerAsset)), "a prefix");
            Assert.AreEqual("", PlayForgeSettings.SuffixFor(typeof(ScalerAsset)), "a suffix");
            Assert.AreSame(ability, PlayForgeSettings.NewAssetDefault(typeof(Ability)), "the defaults stay");
            Assert.AreSame(cost, PlayForgeSettings.UsageEffect("Cost"), "and so does the Cost effect");
        }
    }
}
