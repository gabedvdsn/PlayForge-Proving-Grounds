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
    /// The Forge's view preferences, which live in EditorPrefs, as each user's own: every project on the machine shares
    /// them, so each test puts back every one it can touch. Their page, and the project's settings, are
    /// PlayForgeSettingsTests' (ED-21).
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
            "RecentlyCreatedRows", "Effect_DurationPolicy", "Effect_StackingPolicy", "Item_DefaultMaxStack",
            "ForgeLogMinLevel",
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
