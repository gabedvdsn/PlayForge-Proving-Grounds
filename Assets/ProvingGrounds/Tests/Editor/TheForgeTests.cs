using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The Forge, a list and a detail (Q5): a toolbar with Create, the type filters, the search and the problem count; a
    /// list with sortable columns; a detail pane that holds the selected asset's inspector under its summary (Q6); and a
    /// status bar. It follows the project, with no Refresh (Q30).
    /// </summary>
    public class TheForgeTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<string> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var path in made) AssetDatabase.DeleteAsset(path);
            made.Clear();
        }

        // Runs a test that opens The Forge, guarding the batch mode's "No graphic device" log, then closes it and checks no
        // other error was logged
        private static IEnumerator Guarded(IEnumerator test)
        {
            var log = new WindowLog();
            try
            {
                while (test.MoveNext()) yield return test.Current;
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while The Forge was open");
        }

        private T Save<T>(T asset, string name) where T : ScriptableObject
        {
            var path = $"Assets/ED-27 {name}.asset";
            AssetDatabase.CreateAsset(asset, path);
            made.Add(path);
            return asset;
        }

        private static Ability Ability(string name, int maxLevel = 4)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = name;
            ability.MaxLevel = maxLevel;
            return ability;
        }

        private static IEnumerator Frames(int count = 4)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static TheForge Open() => (TheForge)typeof(TheForge).GetMethod("Open", AnyStatic).Invoke(null, null);

        private static MultiColumnListView ListOf(TheForge forge) => forge.rootVisualElement.Q<MultiColumnListView>("Assets");

        private static List<Object> Listed(TheForge forge) => ListOf(forge).itemsSource.Cast<Object>().ToList();

        /// <summary>
        /// A typed table acted only on a double click, while the grouped list selected on one (ED-27). The list has one
        /// click model: a click selects the row and shows the asset's inspector, under its summary, in the detail pane.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_27_One_click_selects_a_row_and_shows_its_asset() => Guarded(ED_27_One_click_selects_a_row_and_shows_its_assetBody());

        private IEnumerator ED_27_One_click_selects_a_row_and_shows_its_assetBody()
        {
            var fire = Save(Ability("Fireball"), "Fireball");
            var frost = Save(Ability("Frost"), "Frost");
            var forge = Open();
            yield return Frames();

            Assert.IsNotNull(ListOf(forge), "The Forge lists the assets");
            var listed = Listed(forge);
            Assert.That(listed, Has.Member(fire).And.Member(frost), "every asset");
            ListOf(forge).SetSelection(listed.IndexOf(frost));
            yield return Frames();

            var detail = forge.rootVisualElement.Q("Detail");
            Assert.AreEqual("Frost", detail.Q<Label>("HeaderName")?.text, "one click shows the asset's inspector");
            var summary = detail.Q("Summary");
            Assert.IsNotNull(summary, "under its summary");
            Assert.That(summary.Query<Label>().ToList().Select(label => label.text), Has.Some.Contains("Leveling"),
                "which reads its sections' one-line summaries");
        }

        /// <summary>
        /// The Forge refreshed by its Refresh button, or on focus after the project changed. It follows the project
        /// now: an asset made elsewhere joins the list, and the status bar counts it.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_27_The_list_follows_the_project() => Guarded(ED_27_The_list_follows_the_projectBody());

        private IEnumerator ED_27_The_list_follows_the_projectBody()
        {
            var forge = Open();
            yield return Frames();
            Assert.IsNull(forge.rootVisualElement.Query<Button>().Where(button => button.text is "↻" or "Refresh").ToList().FirstOrDefault(),
                "no Refresh button");
            int before = Listed(forge).Count;

            var elsewhere = Save(Ability("Made Elsewhere"), "Made Elsewhere");
            yield return Frames(8);

            Assert.That(Listed(forge), Has.Member(elsewhere), "an asset made elsewhere joins the list");
            Assert.AreEqual(before + 1, Listed(forge).Count);
            StringAssert.Contains($"{before + 1} assets", forge.rootVisualElement.Q<Label>("ForgeStatus").text, "and the status bar counts it");
        }

        /// <summary>A type filter, which shows its type icon, and the search narrow the list.</summary>
        [UnityTest]
        public IEnumerator ED_27_The_type_filter_and_the_search_narrow_the_list() => Guarded(ED_27_The_type_filter_and_the_search_narrow_the_listBody());

        private IEnumerator ED_27_The_type_filter_and_the_search_narrow_the_listBody()
        {
            var fire = Save(Ability("Fireball"), "Fireball");
            var burning = ScriptableObject.CreateInstance<GameplayEffect>();
            burning.Definition.Name = "Burning";
            Save(burning, "Burning");
            var forge = Open();
            yield return Frames();

            var abilities = forge.rootVisualElement.Q<ToolbarToggle>(nameof(Ability));
            Assert.IsNotNull(abilities?.Q(className: "pf-glyph"), "the Ability filter shows its type icon");
            abilities.value = true;
            yield return Frames();
            Assert.That(Listed(forge), Has.Member(fire).And.No.Member(burning), "the filter shows abilities alone");

            abilities.value = false;
            forge.rootVisualElement.Q<ToolbarSearchField>("Search").value = "Burn";
            yield return Frames();
            Assert.That(Listed(forge), Has.Member(burning).And.No.Member(fire), "the search shows what matches");
        }

        /// <summary>
        /// The Forge counts the project's problems in its toolbar and its status bar (Q8), from the one validator
        /// (ED-15).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_15_The_Forge_counts_the_projects_problems() => Guarded(ED_15_The_Forge_counts_the_projects_problemsBody());

        private IEnumerator ED_15_The_Forge_counts_the_projects_problemsBody()
        {
            var linked = Ability("Linked Nowhere");
            linked.LinkMode = EAbilityLinkMode.LinkedToProvider;
            Save(linked, "Linked Nowhere");
            var forge = Open();
            yield return Frames();

            var count = forge.rootVisualElement.Q<ToolbarButton>("ProblemCount");
            Assert.IsNotNull(count, "the toolbar counts the problems");
            StringAssert.Contains("problem", count.text);
            StringAssert.Contains("problem", forge.rootVisualElement.Q<Label>("ForgeStatus").text, "and so does the status bar");
            Assert.That(ForgeValidator.ProblemsOf(linked), Is.Not.Empty, "from the validator");
        }

        /// <summary>
        /// The Visualizer was blank for items and sets, and after a domain reload (ED-9). The detail pane's summary
        /// shows each type, and the window keeps its selection, which a reload restores.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_9_The_summary_shows_each_type_and_the_selection_is_kept() => Guarded(ED_9_The_summary_shows_each_type_and_the_selection_is_keptBody());

        private IEnumerator ED_9_The_summary_shows_each_type_and_the_selection_is_keptBody()
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.Definition.Name = "Torch";
            Save(item, "Torch");
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            set.Name = "Goblin Stats";
            Save(set, "Goblin Stats");
            var forge = Open();
            yield return Frames();

            foreach (var asset in new Object[] { item, set })
            {
                TheForge.OpenToAsset(asset);
                yield return Frames();
                var summary = forge.rootVisualElement.Q("Detail").Q("Summary");
                Assert.That(summary?.Query<Label>().ToList(), Is.Not.Empty, $"{asset.GetType().Name} has a summary");
            }

            // What a domain reload keeps of a window: its serialized fields
            var kept = EditorJsonUtility.ToJson(forge);
            var reloaded = ScriptableObject.CreateInstance<TheForge>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(kept, reloaded);
                Assert.AreSame(set, typeof(TheForge).GetProperty("Selected", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reloaded),
                    "the selection outlives a reload");
            }
            finally
            {
                Object.DestroyImmediate(reloaded);
            }
        }

        /// <summary>Every column's values were strings, sorted as strings, so 10 came before 9 (ED-7).</summary>
        [Test]
        public void ED_7_A_numeric_column_sorts_by_number()
        {
            var nine = Ability("Nine", 9);
            var ten = Ability("Ten", 10);
            try
            {
                var sorted = (List<ScriptableObject>)typeof(TheForge).GetMethod("Sorted", AnyStatic)
                    .Invoke(null, new object[] { new List<ScriptableObject> { ten, nine }, "Levels", true });
                Assert.That(sorted, Is.EqualTo(new ScriptableObject[] { nine, ten }));
            }
            finally
            {
                Object.DestroyImmediate(nine);
                Object.DestroyImmediate(ten);
            }
        }

        /// <summary>The Forge's tag list reads the tag preferences the tag field reads (ED-25).</summary>
        [Test]
        public void ED_25_The_tag_list_follows_the_tag_preferences()
        {
            const string key = "PlayForge_ShowFullTagPath";
            bool had = EditorPrefs.HasKey(key), was = EditorPrefs.GetBool(key);
            var text = typeof(TheForge).GetMethod("TagText", AnyStatic);
            var tag = Tag.Generate("Status.Debuff.Burn");
            try
            {
                EditorPrefs.SetBool(key, true);
                Assert.AreEqual("Status.Debuff.Burn", text.Invoke(null, new object[] { tag }), "the full path");
                EditorPrefs.SetBool(key, false);
                Assert.AreEqual("Burn", text.Invoke(null, new object[] { tag }), "the leaf");
            }
            finally
            {
                if (had) EditorPrefs.SetBool(key, was);
                else EditorPrefs.DeleteKey(key);
            }
        }

        /// <summary>A rebindable shortcut opens The Forge with its search focused (Q29).</summary>
        [Test]
        public void ED_27_A_shortcut_opens_The_Forge()
        {
            Assert.That(ShortcutManager.instance.GetAvailableShortcutIds(), Has.Member("PlayForge/The Forge"));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-48: column widths
        // ═══════════════════════════════════════════════════════════════════════════

        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // The window's content, moved into a runtime panel of a set size, where it's laid out: batch mode gives an editor
        // window no size on Windows, and a narrow one on Linux. It takes the window root's style sheets and classes, and
        // Unity's own editor style sheet, which styles the split and the lists
        private sealed class LaidOut : System.IDisposable
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("Laid Out") { hideFlags = HideFlags.HideAndDontSave };
            private readonly EditorWindow window;
            public VisualElement Root { get; } = new() { name = "LaidOut" };

            public LaidOut(EditorWindow window, float width, float height)
            {
                this.window = window;
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var theme = EditorGUIUtility.isProSkin ? "Dark" : "Light";
                var unity = EditorGUIUtility.Load($"StyleSheets/Generated/DefaultCommon{theme}_inter.uss.asset") as StyleSheet;
                Assert.IsNotNull(unity, "Unity's editor style sheet loads");
                Root.styleSheets.Add(unity);
                var from = window.rootVisualElement;
                for (int i = 0; i < from.styleSheets.count; i++) Root.styleSheets.Add(from.styleSheets[i]);
                foreach (var name in from.GetClasses()) Root.AddToClassList(name);
                Root.style.width = width;
                Root.style.height = height;
                foreach (var child in from.Children().ToList()) Root.Add(child);
                document.rootVisualElement.Add(Root);
            }

            // The content goes back to its window, which closes it as it closes
            public void Dispose()
            {
                if (window != null)
                    foreach (var child in Root.Children().ToList()) window.rootVisualElement.Add(child);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        // The preference that keeps a table's column widths, which a test puts back as it was
        private sealed class KeptPreference : System.IDisposable
        {
            private readonly string key;
            private readonly bool had;
            private readonly string was;

            public KeptPreference(string key)
            {
                this.key = key;
                had = EditorPrefs.HasKey(key);
                was = EditorPrefs.GetString(key, "");
                EditorPrefs.DeleteKey(key);
            }

            public void Dispose()
            {
                if (had) EditorPrefs.SetString(key, was);
                else EditorPrefs.DeleteKey(key);
            }
        }

        // A column's width as its list lays it out
        private static float Laid(Column column) => (float)typeof(Column).GetProperty("desiredWidth", AnyInstance).GetValue(column);

        // Drags a column's right edge, as its header's handle does, once the header has laid its columns out
        private static IEnumerator Drag(VisualElement list, Column column, float by)
        {
            var header = list.Q(className: "unity-multi-column-header");
            Assert.IsNotNull(header, "the list has its header");
            var layout = header.GetType().GetProperty("columnLayout", AnyInstance)?.GetValue(header);
            Assert.IsNotNull(layout, "whose layout sizes the columns");
            var type = layout.GetType();
            // The header lays its columns out on its window's scheduler, which batch mode's window never runs
            yield return null;
            Assume.That(header.layout.width, Is.GreaterThan(100f), "the header is laid out");
            if ((bool)type.GetProperty("isDirty", AnyInstance).GetValue(layout)) header.GetType().GetMethod("DoLayout", AnyInstance).Invoke(header, null);
            type.GetMethod("BeginDragResize", AnyInstance).Invoke(layout, new object[] { column, 0f, false });
            type.GetMethod("DragResize", AnyInstance).Invoke(layout, new object[] { column, by });
            type.GetMethod("EndDragResize", AnyInstance).Invoke(layout, new object[] { column, false });
        }

        private static IEnumerator Press(LaidOut laid, string toggleName)
        {
            var toggle = laid.Root.Q<ToolbarToggle>(toggleName);
            Assert.IsNotNull(toggle, $"the toolbar has {toggleName}");
            toggle.value = true;
            yield return Frames();
        }

        /// <summary>
        /// A column's width wasn't remembered: a width dragged in the asset list was lost through the tag view, and the
        /// other way round (ED-48, the user's report). Each list's dragged width stays through the other list, and the
        /// window remembers both: closed and opened again, as a domain reload builds it again, each column has the width
        /// it was dragged to.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_48_A_dragged_column_keeps_its_width() => Guarded(ED_48_A_dragged_column_keeps_its_widthBody());

        private IEnumerator ED_48_A_dragged_column_keeps_its_widthBody()
        {
            using var assets = new KeptPreference("PlayForge_Columns_Forge.Assets");
            using var tags = new KeptPreference("PlayForge_Columns_Forge.Tags");
            Save(Ability("Fireball"), "Fireball");
            var forge = Open();
            yield return Frames();
            float typeWidth, forWidth;
            using (var laid = new LaidOut(forge, 1280f, 720f))
            {
                yield return Press(laid, "Ability");
                var assetList = laid.Root.Q<MultiColumnListView>("Assets");
                var type = assetList.columns["Type"];
                float before = Laid(type);
                yield return Drag(assetList, type, 20f);
                yield return Frames();
                typeWidth = Laid(type);
                Assert.That(typeWidth, Is.GreaterThan(before + 5f), "the drag widens Type");

                yield return Press(laid, "Tags");
                var tagList = laid.Root.Q<MultiColumnListView>("TagList");
                var contexts = tagList.columns["Contexts"];
                before = Laid(contexts);
                yield return Drag(tagList, contexts, 20f);
                yield return Frames();
                forWidth = Laid(contexts);
                Assert.That(forWidth, Is.GreaterThan(before + 5f), "the drag widens For");

                yield return Press(laid, "Ability");
                Assert.That(Laid(type), Is.EqualTo(typeWidth).Within(1f), "Type keeps its width through the tag view");
                yield return Press(laid, "Tags");
                Assert.That(Laid(contexts), Is.EqualTo(forWidth).Within(1f), "and For its width through the asset view");
            }

            forge.Close();
            yield return Frames();
            forge = Open();
            yield return Frames();
            Assert.That(ListOf(forge).columns["Type"].width.value, Is.EqualTo(typeWidth).Within(1f), "the window remembers Type's width");
            Assert.That(forge.rootVisualElement.Q<MultiColumnListView>("TagList").columns["Contexts"].width.value, Is.EqualTo(forWidth).Within(1f),
                "and For's");
        }

        // A window logs "No graphic device" as it opens and closes in batch mode, which this guard sets aside
        private sealed class WindowLog : System.IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public readonly List<string> Errors = new();

            public WindowLog()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert && !message.Contains(NoDevice)) Errors.Add(message);
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
