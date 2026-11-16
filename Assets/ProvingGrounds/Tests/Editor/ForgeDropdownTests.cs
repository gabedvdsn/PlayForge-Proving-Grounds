using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended;
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
    /// PlayForge's dropdown (EP-1, the user's editor pass of 2026-11-15): every picker and every field's menu opens it in
    /// place of Unity's. Its choices sit in the submenus their paths name; its search finds a choice in any of them; its
    /// Context section says more of the highlighted choice, as the contexts a tag is for; and it's no wider or taller than
    /// Preferences → PlayForge's Menu Width and Menu Height. Its types are the editor's own, so they're read by reflection.
    /// </summary>
    public class ForgeDropdownTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly EditorAssembly = typeof(TagDrawer).Assembly;

        private static Type Kit(string name) => EditorAssembly.GetType("FarEmerald.PlayForge.Extended.Editor." + name, true);

        private static object Make(string type, params object[] args) => Activator.CreateInstance(Kit(type), Any, null, args, null);

        private static object Get(object target, string member)
        {
            var type = target as Type ?? target.GetType();
            var owner = target is Type ? null : target;
            var property = type.GetProperty(member, Any);
            return property != null ? property.GetValue(owner) : type.GetField(member, Any)?.GetValue(owner);
        }

        private static object Call(object target, string method, params object[] args)
        {
            var type = target as Type ?? target.GetType();
            var found = type.GetMethods(Any).Where(each => each.Name == method && each.GetParameters().Length == args.Length).ToList();
            Assert.That(found, Has.Count.EqualTo(1), $"one {type.Name}.{method} of {args.Length} arguments");
            return found[0].Invoke(target is Type ? null : target, args);
        }

        // A choice of a dropdown, and what a dropdown's tree holds, read by name
        private static object Add(object dropdown, string path, string name, Action picked = null, string context = null, bool isChecked = false, bool enabled = true) =>
            Call(dropdown, "Add", path, name, picked, context, isChecked, enabled);

        private static IEnumerable<object> Children(object node) => ((IEnumerable)Get(node, "Children")).Cast<object>();

        private static string NameOf(object node) => (bool)Get(node, "IsSeparator") ? "—" : (string)Get(node, "Name");

        private static IEnumerable<string> Names(object node) => Children(node).Select(NameOf);

        private static object Menu(object node, string name) => Call(node, "Menu", name);

        private static IEnumerable<object> Items(object dropdown) => ((IEnumerable)Get(dropdown, "Items")).Cast<object>();

        private static object ItemNamed(object dropdown, string name) => Items(dropdown).FirstOrDefault(item => (string)Get(item, "Name") == name);

        private static IReadOnlyList<string> Rows(object view) => (IReadOnlyList<string>)Get(view, "Rows");

        /// <summary>
        /// A choice sits in the submenus its path names, a slash between each; a menu lists its submenus first, by name, a
        /// line, then its choices; a fixed choice, as None, sits above them all, over a line.
        /// </summary>
        [Test]
        public void EP_1_A_dropdown_files_its_choices_in_the_submenus_their_paths_name()
        {
            var dropdown = Make("ForgeDropdown", "Kind");
            Call(dropdown, "AddFixed", "None", null, null);
            Add(dropdown, "", "Loose");
            Add(dropdown, "Effects/Damage", "Burn");
            Add(dropdown, "Effects", "Apply");
            Add(dropdown, "Waits", "Delay");

            var root = Call(dropdown, "Build");
            Assert.That(Names(root), Is.EqualTo(new[] { "None", "—", "Effects", "Waits", "—", "Loose" }),
                "None over a line, then the submenus by name, a line, and the choices");
            var effects = Menu(root, "Effects");
            Assert.That(Names(effects), Is.EqualTo(new[] { "Damage", "—", "Apply" }), "a submenu's own submenus first too");
            Assert.That(Names(Menu(effects, "Damage")), Is.EqualTo(new[] { "Burn" }), "and a choice two deep");

            var empty = Make("ForgeDropdown", "Kind");
            Assert.That(Names(Call(empty, "Build")), Is.EqualTo(new[] { "Nothing to choose" }), "one that offers nothing says so");
        }

        /// <summary>Its search finds a choice in any submenu, by any word of its name or its path; an empty search shows the menu.</summary>
        [Test]
        public void EP_1_Its_search_finds_a_choice_in_any_submenu()
        {
            var dropdown = Make("ForgeDropdown", "Kind");
            Add(dropdown, "Effects/Damage", "Burn");
            Add(dropdown, "Waits", "Delay");
            var view = Make("ForgeDropdownView", dropdown);

            Assert.That(Rows(view), Is.EqualTo(new[] { "Effects", "Waits" }), "the top's submenus");
            Call(view, "Search", "bur");
            Assert.That(Rows(view), Is.EqualTo(new[] { "Burn" }), "a search finds a choice two deep");
            Assert.AreEqual("Search", Get(view, "Heading"), "and says it's searching");
            Call(view, "Search", "damage");
            Assert.That(Rows(view), Is.EqualTo(new[] { "Burn" }), "by its menu's name too");
            Call(view, "Search", "");
            Assert.That(Rows(view), Is.EqualTo(new[] { "Effects", "Waits" }), "and an empty search shows the menu again");

            Assert.IsTrue((bool)Call(view, "Open", "Effects"), "a submenu opens");
            Assert.AreEqual("Effects", Get(view, "Heading"), "under its name");
            Call(view, "Back");
            Assert.AreEqual("Kind", Get(view, "Heading"), "and Back goes back to the top");
        }

        /// <summary>
        /// Its Context section says more of the highlighted choice, and nothing of one with no more to say; a dropdown no
        /// choice of which says more has no Context section.
        /// </summary>
        [Test]
        public void EP_1_Its_Context_says_more_of_the_highlighted_choice()
        {
            var dropdown = Make("ForgeDropdown", "Effects");
            Add(dropdown, "", "Burn", context: "Fire damage over six seconds");
            Add(dropdown, "", "Chill");
            var view = Make("ForgeDropdownView", dropdown);

            Assert.IsTrue((bool)Get(view, "ShowsContext"), "it has a Context section");
            Assert.IsTrue((bool)Call(view, "Highlight", "Burn"));
            Assert.AreEqual("Fire damage over six seconds", Get(view, "ContextText"), "which says the highlighted choice's");
            Call(view, "Highlight", "Chill");
            Assert.AreEqual("", Get(view, "ContextText"), "and nothing for one with nothing to say");

            var plain = Make("ForgeDropdown", "Kind");
            Add(plain, "", "Delay");
            Assert.IsFalse((bool)Get(Make("ForgeDropdownView", plain), "ShowsContext"), "a dropdown with nothing to say has none");
        }

        /// <summary>A pick runs its choice; a choice that can't be picked runs nothing, though it highlights to say why.</summary>
        [Test]
        public void EP_1_A_pick_runs_its_choice_and_a_disabled_one_nothing()
        {
            string ran = null;
            var dropdown = Make("ForgeDropdown", "Contexts");
            Add(dropdown, "", "Granted", () => ran = "Granted");
            Add(dropdown, "", "Event", () => ran = "Event", "Declared in code", true, false);
            var view = Make("ForgeDropdownView", dropdown);

            Assert.IsTrue((bool)Call(view, "Highlight", "Event"), "a choice that can't be picked highlights");
            Assert.AreEqual("Declared in code", Get(view, "ContextText"), "to say why");
            Call(view, "Submit");
            Assert.IsNull(ran, "and runs nothing");
            Call(view, "Highlight", "Granted");
            Call(view, "Submit");
            Assert.AreEqual("Granted", ran, "a choice that can runs");
        }

        /// <summary>It's no wider than Menu Width and no taller than Menu Height, and a short one is as tall as its choices.</summary>
        [Test]
        public void EP_1_It_keeps_within_the_menu_size_Preferences_sets()
        {
            var preferences = typeof(PlayForgePreferences);
            var width = Get(preferences, "MenuWidth");
            var height = Get(preferences, "MenuHeight");
            string widthKey = "PlayForge_MenuWidth", heightKey = "PlayForge_MenuHeight";
            bool hadWidth = EditorPrefs.HasKey(widthKey), hadHeight = EditorPrefs.HasKey(heightKey);
            int keptWidth = EditorPrefs.GetInt(widthKey), keptHeight = EditorPrefs.GetInt(heightKey);
            try
            {
                width.GetType().GetProperty("Value").SetValue(width, 260);
                height.GetType().GetProperty("Value").SetValue(height, 200);

                var tall = Make("ForgeDropdown", "Long");
                for (int i = 0; i < 100; i++) Add(tall, "", $"A choice of a long name, as a project's own kind may have, {i}");
                var size = (Vector2)Call(tall, "SizeFor", 100f);
                Assert.LessOrEqual(size.x, 260f, "no wider than Menu Width");
                Assert.LessOrEqual(size.y, 200f, "nor taller than Menu Height");

                var shortOne = Make("ForgeDropdown", "Short");
                Add(shortOne, "", "One");
                Add(shortOne, "", "Two");
                Assert.Less(((Vector2)Call(shortOne, "SizeFor", 100f)).y, 200f, "and a short one is no taller than its choices need");
            }
            finally
            {
                if (hadWidth) EditorPrefs.SetInt(widthKey, keptWidth);
                else EditorPrefs.DeleteKey(widthKey);
                if (hadHeight) EditorPrefs.SetInt(heightKey, keptHeight);
                else EditorPrefs.DeleteKey(heightKey);
            }
        }

        /// <summary>
        /// The tag picker says the contexts a tag is for, the user's example; None and New Tag… come first, and the field's
        /// tag is checked.
        /// </summary>
        [Test]
        public void EP_1_The_tag_picker_says_the_contexts_a_tag_is_for()
        {
            var holder = ScriptableObject.CreateInstance<Ability>();
            try
            {
                ForgeTagRegistry.RefreshCache();
                var burn = Tag.Generate("Test.EP1.Burn");
                ForgeTagRegistry.RegisterTagUsage(burn, new[] { ForgeContext.Granted }, holder, "Granted");
                var picker = Make("ForgeTagPicker", new List<Tag> { burn }, true, false, "Tags", null, null, burn);
                var dropdown = Get(picker, "Dropdown");

                Assert.That(Items(dropdown).Take(2).Select(item => (string)Get(item, "Name")), Is.EqualTo(new[] { "None", "New Tag…" }), "None and New Tag… first");
                var item = ItemNamed(dropdown, "Test.EP1.Burn");
                Assert.IsNotNull(item, "the tag is offered");
                StringAssert.Contains(ForgeContext.Granted, (string)Get(item, "Context"), "its Context names the contexts it's for");
                Assert.IsTrue((bool)Get(item, "Checked"), "and the field's tag is checked");
            }
            finally
            {
                Object.DestroyImmediate(holder);
                ForgeTagRegistry.MarkDirty();
            }
        }

        /// <summary>The kind picker offers None first and checks the kind the field holds.</summary>
        [Test]
        public void EP_1_The_kind_picker_checks_the_kind_held()
        {
            var kinds = new List<(Type, string)> { (typeof(ApplyEffectsAbilityTask), "Apply Effects"), (typeof(DelayTask), "Delay") };
            var picker = Make("ForgeTypePicker", kinds, true, "Ability Task", null, typeof(DelayTask));
            var dropdown = Get(picker, "Dropdown");
            Assert.IsFalse((bool)Get(ItemNamed(dropdown, "None"), "Checked"), "None isn't checked where the field holds a kind");
            Assert.IsTrue((bool)Get(ItemNamed(dropdown, "Delay"), "Checked"), "the kind it holds is");
            Assert.IsFalse((bool)Get(ItemNamed(dropdown, "Apply Effects"), "Checked"), "and no other");
        }

        /// <summary>
        /// The asset picker files assets in more than one folder under a submenu for each, below the folder they share, and
        /// its Context says an asset's description and its file.
        /// </summary>
        [Test]
        public void EP_1_The_asset_picker_files_assets_by_folder_and_says_their_files()
        {
            const string folder = "Assets/EP1 Picker";
            try
            {
                AssetDatabase.CreateFolder("Assets", "EP1 Picker");
                AssetDatabase.CreateFolder(folder, "Fire");
                AssetDatabase.CreateFolder(folder, "Ice");
                var burn = ScriptableObject.CreateInstance<GameplayEffect>();
                AssetDatabase.CreateAsset(burn, $"{folder}/Fire/Burn.asset");
                var chill = ScriptableObject.CreateInstance<GameplayEffect>();
                AssetDatabase.CreateAsset(chill, $"{folder}/Ice/Chill.asset");

                var assets = new List<(Object, string)> { (null, "None"), (burn, "Burn"), (chill, "Chill") };
                var picker = Make("ForgeAssetPicker", assets, "Effect", "None", null, burn);
                var dropdown = Get(picker, "Dropdown");
                var root = Call(dropdown, "Build");
                Assert.That(Names(root), Is.EqualTo(new[] { "None", "—", "Fire", "Ice" }), "a submenu for each folder");
                Assert.That(Names(Menu(root, "Fire")), Is.EqualTo(new[] { "Burn" }), "holding its assets");
                StringAssert.Contains($"{folder}/Fire/Burn.asset", (string)Get(ItemNamed(dropdown, "Burn"), "Context"), "whose Context says its file");
                Assert.IsTrue((bool)Get(ItemNamed(dropdown, "Burn"), "Checked"), "the field's asset checked");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// A kind popup, as a number field's Kind, opens PlayForge's dropdown in place of Unity's menu, each kind in the
        /// submenus its menu path names; a pick there is the popup's value.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_1_A_kind_popup_opens_PlayForges_dropdown()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("EP-1") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                var popup = new PopupField<string>("Kind", new List<string> { "Constant", "Clamped" }, "Constant")
                {
                    formatListItemCallback = choice => choice == "Clamped" ? "Scaler/Clamped" : choice,
                };
                Call(Kit("ForgePopupDropdown"), "Use", popup, "Kind", null);
                document.rootVisualElement.Add(popup);
                yield return null;

                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = popup;
                    popup.SendEvent(submit);
                }
                yield return null;

                var shown = Get(Kit("ForgeDropdown"), "LastShown");
                Assert.IsNotNull(shown, "the popup opened PlayForge's dropdown");
                Assert.AreEqual("Kind", Get(shown, "Title"));
                var root = Call(shown, "Build");
                Assert.That(Names(Menu(root, "Scaler")), Is.EqualTo(new[] { "Clamped" }), "Clamped under Scaler, as its path says");
                Assert.IsTrue((bool)Get(ItemNamed(shown, "Constant"), "Checked"), "the popup's value checked");
                ((Action)Get(ItemNamed(shown, "Clamped"), "Picked")).Invoke();
                Assert.AreEqual("Clamped", popup.value, "and a pick is its value");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        /// <summary>
        /// Every picker is PlayForge's dropdown: none is Unity's AdvancedDropdown any more, the effect field's pick of an
        /// unlinked effect among them.
        /// </summary>
        [Test]
        public void EP_1_Every_picker_is_PlayForges_dropdown()
        {
            var advanced = typeof(UnityEditor.IMGUI.Controls.AdvancedDropdown);
            var unity = EditorAssembly.GetTypes().Where(type => advanced.IsAssignableFrom(type)).Select(type => type.FullName).ToList();
            Assert.IsEmpty(unity, $"no picker is Unity's dropdown: {string.Join(", ", unity)}");
            foreach (var picker in new[] { "ForgeTypePicker", "ForgeTagPicker", "ForgeAssetPicker", "ForgeSequencePicker" })
                Assert.AreEqual(Kit("ForgeDropdown"), Kit(picker).GetProperty("Dropdown", Any)?.PropertyType, $"{picker} shows PlayForge's dropdown");
        }
    }
}
