using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// The Forge's Settings tab and the settings it writes. Settings live in EditorPrefs, which every project on the
    /// machine shares, so each test puts back every setting it can touch.
    /// </summary>
    public class TheForgeSettingsTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Prefix = "PlayForge_";

        // Every setting The Forge writes, by type
        private static readonly string[] BoolSettings =
        {
            "RememberTypeFilter", "DoubleClickVisualize", "ShowFileNames", "ShowFullTagPath", "GroupTagsByRoot",
            "AutoRefresh", "ShowNotifications", "Ability_AutoLevel", "Attribute_AutoBaseValue", "Entity_AutoLevel",
            "Item_AutoMaxStack",
        };
        private static readonly string[] IntSettings =
        {
            "RecentlyCreatedRows", "EditorRefreshDelayMs", "EditorForceRefreshDelayMs", "Effect_DurationPolicy",
            "Effect_StackingPolicy", "Item_DefaultMaxStack", "ForgeLogMinLevel",
        };
        private static readonly string[] FloatSettings = { "Attribute_DefaultBaseValue" };
        private static readonly string[] StringSettings = TheForge.AssetTypes
            .SelectMany(type => new[] { "Path_", "Prefix_", "Postfix_" }.Select(setting => setting + type.Type.Name))
            .Concat(new[] { "LastTypeFilter", "ScalerTemplate_DefaultCategory", "RequirementTemplate_DefaultCategory" })
            .ToArray();

        // Templates are content you registered, not settings, so Reset All keeps them
        private static readonly string[] Templates = { "Tags_Ability", "EffectTemplate_Cost" };

        private readonly Dictionary<string, object> _kept = new();

        [SetUp]
        public void KeepPrefs()
        {
            _kept.Clear();
            foreach (var key in BoolSettings) Keep(key, EditorPrefs.GetBool);
            foreach (var key in IntSettings) Keep(key, EditorPrefs.GetInt);
            foreach (var key in FloatSettings) Keep(key, EditorPrefs.GetFloat);
            foreach (var key in StringSettings.Concat(Templates)) Keep(key, EditorPrefs.GetString);
        }

        private void Keep<T>(string key, Func<string, T> read) =>
            _kept[Prefix + key] = EditorPrefs.HasKey(Prefix + key) ? read(Prefix + key) : null;

        [TearDown]
        public void RestorePrefs()
        {
            foreach (var (key, value) in _kept)
            {
                switch (value)
                {
                    case bool b: EditorPrefs.SetBool(key, b); break;
                    case int i: EditorPrefs.SetInt(key, i); break;
                    case float f: EditorPrefs.SetFloat(key, f); break;
                    case string s: EditorPrefs.SetString(key, s); break;
                    default: EditorPrefs.DeleteKey(key); break;
                }
            }
            ForgeLog.MinimumLevel = ForgeLogSettingsBridge.CurrentLevel;
        }

        private static void Invoke(TheForge forge, string method, params object[] args) =>
            typeof(TheForge).GetMethod(method, Private).Invoke(forge, args);

        private static VisualElement RowOf(VisualElement root, string label) =>
            root.Query<Label>().Where(l => l.text == label).First().parent;

        /// <summary>Reset All cleared 7 of The Forge's settings and its paths and names, and left the rest.</summary>
        [Test]
        public void ED_6_Reset_All_resets_every_setting()
        {
            foreach (var key in BoolSettings) EditorPrefs.SetBool(Prefix + key, true);
            foreach (var key in IntSettings) EditorPrefs.SetInt(Prefix + key, 3);
            foreach (var key in FloatSettings) EditorPrefs.SetFloat(Prefix + key, 3f);
            foreach (var key in StringSettings.Concat(Templates)) EditorPrefs.SetString(Prefix + key, "ED-6");

            var forge = ScriptableObject.CreateInstance<TheForge>();
            try
            {
                Invoke(forge, "ResetAllSettings");
            }
            finally
            {
                Object.DestroyImmediate(forge);
            }

            var left = BoolSettings.Concat(IntSettings).Concat(FloatSettings).Concat(StringSettings)
                .Where(key => EditorPrefs.HasKey(Prefix + key));
            Assert.That(left, Is.Empty, "settings Reset All left as they were");
            Assert.That(Templates.Where(key => !EditorPrefs.HasKey(Prefix + key)), Is.Empty, "templates Reset All removed");
        }

        /// <summary>
        /// Reset All went to the Analysis tab, a copy of the Analysis tab's own refresh, so the Settings tab never showed
        /// the defaults it had just restored.
        /// </summary>
        [Test]
        public void ED_6_Reset_All_shows_the_defaults_on_the_Settings_tab()
        {
            EditorPrefs.SetBool(Prefix + "ShowFileNames", false);
            var forge = ScriptableObject.CreateInstance<TheForge>();
            try
            {
                Invoke(forge, "CreateGUI");
                Invoke(forge, "ShowTab", 3);
                Assert.IsFalse(RowOf(forge.rootVisualElement, "Show file names in grouped view").Q<Toggle>().value,
                    "the tab shows the setting");

                Invoke(forge, "ResetAllSettings");

                Assert.AreEqual(3, typeof(TheForge).GetField("currentTab", Private).GetValue(forge), "still on Settings");
                Assert.IsTrue(RowOf(forge.rootVisualElement, "Show file names in grouped view").Q<Toggle>().value,
                    "the tab shows the default");
            }
            finally
            {
                Object.DestroyImmediate(forge);
            }
        }

        /// <summary>
        /// The inspector delay's number field had its range check backwards: it saved values out of range and turned
        /// valid ones away.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_6_An_inspector_delay_in_range_is_saved()
        {
            const int refreshDelayMax = 2503; // ForgeDrawerStyles.DefaultConfigureDelayMsMax, which is internal
            EditorPrefs.DeleteKey(Prefix + "EditorRefreshDelayMs");
            var forge = ScriptableObject.CreateInstance<TheForge>();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("ED-6") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                // A field sends its change event only through a panel. Batch mode has no graphics device to give an
                // editor window one, so a runtime panel carries the Settings tab instead
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                var settings = new VisualElement();
                typeof(TheForge).GetField("settingsContentContainer", Private).SetValue(forge, settings);
                Invoke(forge, "BuildGeneralSettingsTab");
                document.rootVisualElement.Add(settings);

                var row = RowOf(settings, "Inspector Refresh Delay Ms");
                row.Q<IntegerField>().value = 1000;
                Assert.AreEqual(1000, EditorPrefs.GetInt(Prefix + "EditorRefreshDelayMs", -1), "saved");
                Assert.AreEqual(1000, row.Q<SliderInt>().value, "the slider follows");

                row.Q<IntegerField>().value = 9000;
                Assert.AreEqual(refreshDelayMax, EditorPrefs.GetInt(Prefix + "EditorRefreshDelayMs", -1),
                    "out of range, it saves the nearest limit");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(panelSettings);
                Object.DestroyImmediate(forge);
            }
        }

        /// <summary>"Auto-refresh on window focus" was saved but never read, so the window never refreshed on focus.</summary>
        [UnityTest]
        public IEnumerator ED_6_Focus_picks_up_an_asset_made_elsewhere()
        {
            const string path = "Assets/ED-6 Made Elsewhere.asset";
            EditorPrefs.SetBool(Prefix + "AutoRefresh", true);
            var forge = ScriptableObject.CreateInstance<TheForge>();
            try
            {
                Invoke(forge, "CreateGUI");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<Ability>(), path);
                yield return null;
                yield return null;

                // What Unity does when the window gets focus back
                typeof(TheForge).GetMethod("OnFocus", Private)?.Invoke(forge, null);

                var cached = (List<ScriptableObject>)typeof(TheForge).GetField("cachedAssets", Private).GetValue(forge);
                Assert.That(cached, Does.Contain(AssetDatabase.LoadAssetAtPath<Ability>(path)));
            }
            finally
            {
                Object.DestroyImmediate(forge);
                AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>
        /// "Display full tag path" changed nothing: the tag field's text returned the full path before the code that
        /// read the setting.
        /// </summary>
        [Test]
        public void ED_25_Display_full_tag_path_changes_the_tag_field()
        {
            var tag = Tag.Generate("Status.Debuff.Burn");
            var drawer = new TagDrawer();
            var text = typeof(TagDrawer).GetMethod("GetDisplayText", Private);

            EditorPrefs.SetBool(Prefix + "ShowFullTagPath", true);
            Assert.AreEqual("Status.Debuff.Burn", text.Invoke(drawer, new object[] { tag }), "full path");

            EditorPrefs.SetBool(Prefix + "ShowFullTagPath", false);
            Assert.AreEqual("…Burn", text.Invoke(drawer, new object[] { tag }), "leaf only");
        }

        /// <summary>"Group tags by root" showed off in Settings, but the tag field treats an unset value as on.</summary>
        [Test]
        public void ED_25_Group_tags_by_root_shows_the_default_the_tag_field_uses()
        {
            EditorPrefs.DeleteKey(Prefix + "GroupTagsByRoot");
            var forge = ScriptableObject.CreateInstance<TheForge>();
            try
            {
                var settings = new VisualElement();
                typeof(TheForge).GetField("settingsContentContainer", Private).SetValue(forge, settings);
                Invoke(forge, "BuildGeneralSettingsTab");

                var shown = RowOf(settings, "Group tags by root").Q<Toggle>().value;
                var used = (bool)typeof(TagDrawer).GetMethod("GroupByRoot", Private).Invoke(new TagDrawer(), null);
                Assert.AreEqual(used, shown);
            }
            finally
            {
                Object.DestroyImmediate(forge);
            }
        }

        /// <summary>The View tab's tag list kept display switches of its own and read neither setting.</summary>
        [Test]
        public void ED_25_The_View_tab_follows_the_tag_settings()
        {
            EditorPrefs.SetBool(Prefix + "ShowFullTagPath", true);
            EditorPrefs.SetBool(Prefix + "GroupTagsByRoot", false);
            var forge = ScriptableObject.CreateInstance<TheForge>();
            try
            {
                var content = new VisualElement();
                typeof(TheForge).GetField("contentContainer", Private).SetValue(forge, content);
                Invoke(forge, "BuildTagContextFilterBar");

                var chips = content.Query<Label>().ToList().Select(chip => chip.text).ToList();
                Assert.That(chips, Does.Contain("Full Path ✓"), "full paths");
                Assert.That(chips, Does.Contain("Group by Root"), "not grouped");
            }
            finally
            {
                Object.DestroyImmediate(forge);
            }
        }
    }
}
