using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended;
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
        // ED-58 to ED-60: names, sorting and tags
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The Forge's list showed each asset's file name beside its name (ED-58, the user's report). The list shows the
        /// asset's name alone, and the selected asset's header shows its file name under Show in Project.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_58_The_file_name_is_in_the_header_not_the_list() => Guarded(ED_58_The_file_name_is_in_the_header_not_the_listBody());

        private IEnumerator ED_58_The_file_name_is_in_the_header_not_the_listBody()
        {
            var fire = Save(Ability("Fireball"), "Fire File");
            var forge = Open();
            yield return Frames();

            var cell = ListOf(forge).columns["Name"].makeCell();
            Assert.IsNull(cell.Q<Label>("File"), "the list's name cell holds no file name");

            ListOf(forge).SetSelection(Listed(forge).IndexOf(fire));
            yield return Frames();
            var header = forge.rootVisualElement.Q("Detail").Q(className: "pf-asset-header");
            Assert.IsNotNull(header, "the detail shows the asset's header");
            Assert.AreEqual("Show in Project", header.Q<Button>("OpenInForge")?.text, "with Show in Project");
            var file = header.Q<Label>("HeaderFile");
            Assert.IsNotNull(file, "and a line under it");
            Assert.AreEqual(fire.name, file.text, "which names the asset's file");
            Assert.IsFalse(file.ClassListContains("pf-asset-header__under--empty"), "and shows");
        }

        /// <summary>
        /// The file name sat under Show in Project, so that button stood two lines tall in the header's row, and the
        /// problem count, Help and the menu sat level with its middle (ED-65, the user's report). The actions share one row,
        /// and the file name has a row of its own under it, on the right.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_65_The_file_name_has_a_row_of_its_own_under_the_actions() =>
            Guarded(ED_65_The_file_name_has_a_row_of_its_own_under_the_actionsBody());

        private IEnumerator ED_65_The_file_name_has_a_row_of_its_own_under_the_actionsBody()
        {
            var fire = Save(Ability("Fireball"), "Fire File");
            var forge = Open();
            yield return Frames();
            ListOf(forge).SetSelection(Listed(forge).IndexOf(fire));
            yield return Frames();

            var header = forge.rootVisualElement.Q("Detail").Q(className: "pf-asset-header");
            Assert.IsNotNull(header, "the detail shows the asset's header");
            var row = header.Q(name: "HeaderActionRow");
            Assert.IsNotNull(row, "the actions share a row");
            foreach (var action in new[] { "ProblemCount", "OpenInForge", "HeaderHelp", "HeaderMenu" })
                Assert.AreSame(row, header.Q(name: action)?.parent, $"{action} is in it");
            var file = header.Q<Label>("HeaderFile");
            Assert.AreEqual(fire.name, file?.text, "the file name shows");
            var lines = file.parent;
            Assert.AreNotSame(row, lines, "in a row of its own");
            Assert.AreSame(row.parent, lines.parent, "beside the actions' row");
            Assert.Greater(lines.parent.IndexOf(lines), lines.parent.IndexOf(row), "and under it");
            Assert.IsTrue(lines.ClassListContains("pf-asset-header__lines"), "on the right");
        }

        /// <summary>
        /// The Forge's tag view couldn't sort its columns (ED-59, the user's report). Its Tag, For and Uses columns sort,
        /// in either direction. And the asset list's sort, which the window keeps through a domain reload, shows in its
        /// header again as the window is built.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_59_The_tag_view_sorts_its_columns() => Guarded(ED_59_The_tag_view_sorts_its_columnsBody());

        private IEnumerator ED_59_The_tag_view_sorts_its_columnsBody()
        {
            // The tag registry reads the project's assets, so each tag is in the assets that use it
            var once = Tag.Generate("Test.ED59.Once");
            var twice = Tag.Generate("Test.ED59.Twice");
            var a = Ability("ED59 A");
            a.Tags.ContextTags = new List<Tag> { once, twice };
            Save(a, "ED59 A");
            var b = Ability("ED59 B");
            b.Tags.ContextTags = new List<Tag> { twice };
            Save(b, "ED59 B");
            var forge = Open();
            yield return Frames();
            forge.rootVisualElement.Q<ToolbarToggle>("Tags").value = true;
            forge.rootVisualElement.Q<ToolbarSearchField>("Search").value = "ED59";
            yield return Frames();

            var tags = forge.rootVisualElement.Q<MultiColumnListView>("TagList");
            Assert.That(tags.columns.Select(column => column.sortable), Is.All.True, "each column of the tag view sorts");
            List<Tag> Shown() => tags.itemsSource.Cast<ForgeTagRegistry.TagUsageRecord>().Select(record => record.Tag).ToList();
            void SortBy(string column, SortDirection direction)
            {
                tags.sortColumnDescriptions.Clear();
                tags.sortColumnDescriptions.Add(new SortColumnDescription(column, direction));
            }

            SortBy("Uses", SortDirection.Descending);
            yield return Frames();
            Assert.That(Shown(), Is.EqualTo(new[] { twice, once }), "by uses, the most first");
            SortBy("Uses", SortDirection.Ascending);
            yield return Frames();
            Assert.That(Shown(), Is.EqualTo(new[] { once, twice }), "and the fewest");
            SortBy("Tag", SortDirection.Descending);
            yield return Frames();
            Assert.That(Shown(), Is.EqualTo(new[] { twice, once }), "by tag, from the end");

            // A domain reload builds the window again, with the fields it serializes
            forge.rootVisualElement.Q<ToolbarToggle>("Tags").value = false;
            var assets = ListOf(forge);
            assets.sortColumnDescriptions.Clear();
            assets.sortColumnDescriptions.Add(new SortColumnDescription("Type", SortDirection.Descending));
            yield return Frames();
            forge.rootVisualElement.Clear();
            typeof(TheForge).GetMethod("CreateGUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(forge, null);
            yield return Frames();
            var shown = ListOf(forge).sortColumnDescriptions.ToList();
            Assert.That(shown.Select(each => (each.columnName, each.direction)), Is.EqualTo(new[] { ("Type", SortDirection.Descending) }),
                "the asset list's header shows the sort the window kept");
        }

        /// <summary>
        /// A tag made unique, as the tag picker makes one, showed its padded name in The Forge: its path with a generated
        /// suffix (ED-60, the user's report). The tag list, a tag's detail and the Asset Tag column show its display name.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_60_A_unique_tag_shows_its_display_name() => Guarded(ED_60_A_unique_tag_shows_its_display_nameBody());

        private IEnumerator ED_60_A_unique_tag_shows_its_display_nameBody()
        {
            const string key = "PlayForge_ShowFullTagPath";
            bool had = EditorPrefs.HasKey(key), was = EditorPrefs.GetBool(key);
            var tag = Tag.GenerateAsUnique("Test.ED60.Fireball");
            Assume.That(tag.Name, Is.Not.EqualTo("Test.ED60.Fireball"), "a unique tag's name is padded");
            // The tag registry reads the project's assets, so the tag is in one
            var fire = Ability("Fireball");
            fire.Tags.AssetTag = tag;
            Save(fire, "ED60 Fireball");
            var text = typeof(TheForge).GetMethod("TagText", AnyStatic);
            try
            {
                EditorPrefs.SetBool(key, true);
                Assert.AreEqual("Test.ED60.Fireball", text.Invoke(null, new object[] { tag }), "the tag list's full path");
                Assert.AreEqual("Test.ED60.Fireball", typeof(TheForge).GetMethod("AssetTagOf", AnyStatic).Invoke(null, new object[] { fire }),
                    "the Asset Tag column");
                EditorPrefs.SetBool(key, false);
                Assert.AreEqual("Fireball", text.Invoke(null, new object[] { tag }), "and its leaf");

                var forge = Open();
                yield return Frames();
                forge.rootVisualElement.Q<ToolbarToggle>("Tags").value = true;
                forge.rootVisualElement.Q<ToolbarSearchField>("Search").value = "ED60";
                yield return Frames();
                var tags = forge.rootVisualElement.Q<MultiColumnListView>("TagList");
                var records = tags.itemsSource.Cast<ForgeTagRegistry.TagUsageRecord>().ToList();
                int at = records.FindIndex(record => record.Tag == tag);
                Assert.That(at, Is.GreaterThanOrEqualTo(0), "the search finds the tag by its display name");
                tags.SetSelection(at);
                yield return Frames();
                var title = forge.rootVisualElement.Q("Detail").Q(className: "pf-forge__summary-title") as Label;
                Assert.AreEqual("Test.ED60.Fireball", title?.text, "and its detail names it by its display name");
            }
            finally
            {
                if (had) EditorPrefs.SetBool(key, was);
                else EditorPrefs.DeleteKey(key);
            }
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

        // ═══════════════════════════════════════════════════════════════════════════
        // FN-6 and FN-7: a row's words, and the columns hidden
        // ═══════════════════════════════════════════════════════════════════════════

        // An element's middle against its row's, as the list lays them out
        private static void AssertInItsRowsMiddle(VisualElement element, string what)
        {
            Assert.IsNotNull(element, $"{what} is drawn");
            var row = element.parent;
            while (row != null && !row.ClassListContains("unity-collection-view__item") && !row.ClassListContains("unity-list-view__item")) row = row.parent;
            Assert.IsNotNull(row, $"{what} is in a row");
            Assert.That(element.worldBound.center.y, Is.EqualTo(row.worldBound.center.y).Within(1f), $"{what} sits in its row's middle");
        }

        /// <summary>
        /// The Forge's rows set their words and icons in the middle of the row's height, and a text column's words keep off
        /// the column's left edge: a list's cells sat at the top of their rows, and the Tags view's words pressed against
        /// the window's edge (FN-6).
        /// </summary>
        [UnityTest]
        public IEnumerator FN_6_A_rows_words_sit_in_its_middle_off_its_left_edge() => Guarded(FN_6_Body());

        private IEnumerator FN_6_Body()
        {
            Save(Ability("Fireball"), "Fireball");
            var forge = Open();
            yield return Frames();
            using var laid = new LaidOut(forge, 1280f, 720f);
            yield return Press(laid, "Ability");
            yield return Frames();
            var assets = laid.Root.Q<MultiColumnListView>("Assets");
            AssertInItsRowsMiddle(assets.Q(className: "pf-forge__name-cell")?.Q("Icon"), "an asset's icon");
            AssertInItsRowsMiddle(assets.Q<Label>(className: "pf-forge__name"), "its name");
            var type = assets.Q<Label>(className: "pf-forge__cell");
            AssertInItsRowsMiddle(type, "its type");
            Assert.That(type.resolvedStyle.paddingLeft, Is.GreaterThanOrEqualTo(3f), "which keeps off its column's left edge");

            yield return Press(laid, "Tags");
            yield return Frames();
            var tag = laid.Root.Q<MultiColumnListView>("TagList").Q<Label>(className: "pf-forge__cell");
            Assume.That(tag, Is.Not.Null, "the Tags view lists a tag");
            AssertInItsRowsMiddle(tag, "a tag");
            Assert.That(tag.resolvedStyle.paddingLeft, Is.GreaterThanOrEqualTo(3f), "which keeps off the window's edge");
        }

        /// <summary>
        /// A column hidden from the header's menu came back as the window was closed and opened again, as a domain reload
        /// builds it again: Type and Asset Tag, which the user always hides (FN-7). The window remembers the columns hidden,
        /// as it does their widths, and a column shown again stays shown.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_7_A_hidden_column_stays_hidden() => Guarded(FN_7_Body());

        private IEnumerator FN_7_Body()
        {
            using var hidden = new KeptPreference("PlayForge_HiddenColumns_Forge.Assets");
            var forge = Open();
            yield return Frames();
            var columns = ListOf(forge).columns;
            Assume.That(columns["Type"].visible && columns["AssetTag"].visible, "Type and Asset Tag show at first");
            // As the header's menu hides a column
            columns["Type"].visible = false;
            columns["AssetTag"].visible = false;
            yield return Frames();
            forge.Close();
            yield return Frames();

            forge = Open();
            yield return Frames();
            columns = ListOf(forge).columns;
            Assert.IsFalse(columns["Type"].visible, "the window remembers Type is hidden");
            Assert.IsFalse(columns["AssetTag"].visible, "and Asset Tag");
            Assert.IsTrue(columns["Name"].visible && columns["Levels"].visible, "and shows the rest");
            columns["Type"].visible = true;
            yield return Frames();
            forge.Close();
            yield return Frames();

            forge = Open();
            yield return Frames();
            Assert.IsTrue(ListOf(forge).columns["Type"].visible, "a column shown again stays shown");
            Assert.IsFalse(ListOf(forge).columns["AssetTag"].visible, "the other stays hidden");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // FN-8: renaming
        // ═══════════════════════════════════════════════════════════════════════════

        private const string RenameFolder = "Assets/FN-8 Rename";

        // An asset of a type The Forge lists, saved in the test's folder, named as given where it has a name of its own
        private static T Saved<T>(T asset, string file) where T : ScriptableObject
        {
            if (!AssetDatabase.IsValidFolder(RenameFolder)) AssetDatabase.CreateFolder("Assets", "FN-8 Rename");
            AssetDatabase.CreateAsset(asset, $"{RenameFolder}/{file}.asset");
            return asset;
        }

        private static string FileOf(Object asset) => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(asset));

        /// <summary>
        /// The Forge renamed nothing: an asset's name was its inspector's to change, where it had one, and a Requirement
        /// template or a Scaler asset had none (FN-8). A row's menu, and an asset header's, offer Rename…, a dialog whose
        /// Rename renames the asset as its inspector does: its name, the tag its name gives, and its file, which follows the
        /// name back through an Undo.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_8_A_rows_menu_renames_its_asset() => Guarded(FN_8_A_rows_menu_renames_its_assetBody());

        private IEnumerator FN_8_A_rows_menu_renames_its_assetBody()
        {
            try
            {
                var bolt = Saved(Ability("FN8 Bolt"), "FN8 Bolt");
                var tagBefore = bolt.Tags.AssetTag.Name;
                var forge = Open();
                yield return Frames();
                TheForge.OpenToAsset(bolt);
                yield return Frames();

                var row = new DropdownMenu();
                typeof(TheForge).GetMethod("FillAssetMenu", AnyInstance).Invoke(forge, new object[] { row });
                var rename = row.MenuItems().OfType<DropdownMenuAction>().FirstOrDefault(action => action.name == "Rename…");
                Assert.IsNotNull(rename, "a row's menu offers Rename…");
                var editor = UnityEditor.Editor.CreateEditor(bolt);
                try
                {
                    var header = (GenericMenu)typeof(BasePlayForgeEditor).GetMethod("AssetMenu", AnyInstance).Invoke(editor, new object[] { new VisualElement() });
                    Assert.That(ItemsOf(header), Has.Member("Rename…"), "and so does its header's");
                }
                finally
                {
                    Object.DestroyImmediate(editor);
                }

                rename.Execute();
                yield return Frames();
                var dialog = Resources.FindObjectsOfTypeAll<ForgeRenameWindow>().SingleOrDefault();
                Assert.IsNotNull(dialog, "which opens a dialog");
                var field = dialog.rootVisualElement.Q<TextField>("NewName");
                Assert.AreEqual("FN8 Bolt", field?.value, "with the asset's name to type over");
                field.value = "FN8 Lightning";
                var confirm = dialog.rootVisualElement.Q<Button>("Rename");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = confirm;
                    confirm.SendEvent(submit);
                }
                yield return Frames();

                Assert.AreEqual("FN8 Lightning", bolt.Definition.Name, "Rename renames the ability");
                Assert.AreNotEqual(tagBefore, bolt.Tags.AssetTag.Name, "its tag follows its name");
                // A file leaves out the name's spaces, as a new asset's file does
                StringAssert.Contains("FN8Lightning", FileOf(bolt), "and its file");
                Assert.IsEmpty(Resources.FindObjectsOfTypeAll<ForgeRenameWindow>(), "and the dialog closes");

                Undo.PerformUndo();
                yield return Frames();
                Assert.AreEqual("FN8 Bolt", bolt.Definition.Name, "an Undo takes the name back");
                // A tag with no name saves as an empty one
                Assert.AreEqual(tagBefore ?? "", bolt.Tags.AssetTag.Name ?? "", "and the tag");
                StringAssert.Contains("FN8Bolt", FileOf(bolt), "and the file follows");

                // F2 on the list renames its selected asset, as in the Project window
                var list = ListOf(forge);
                list.SetSelection(Listed(forge).IndexOf(bolt));
                yield return Frames();
                using (var f2 = KeyDownEvent.GetPooled('\0', KeyCode.F2, EventModifiers.None))
                {
                    f2.target = list;
                    list.SendEvent(f2);
                }
                yield return Frames();
                var opened = Resources.FindObjectsOfTypeAll<ForgeRenameWindow>().SingleOrDefault();
                Assert.IsNotNull(opened, "F2 on the list opens the dialog for its selected asset");
                Assert.AreEqual("FN8 Bolt", opened.rootVisualElement.Q<TextField>("NewName")?.value, "with its name");
                opened.Close();
            }
            finally
            {
                AssetDatabase.DeleteAsset(RenameFolder);
            }
        }

        /// <summary>
        /// Each type The Forge lists renames: a PlayForge asset's own name, the attribute's among them; a Scaler asset's
        /// scaler and a Requirement template's requirements, whose names its lists show; and an asset with no name of its
        /// own, as a Value Pipeline, by its file (FN-8).
        /// </summary>
        [Test]
        public void FN_8_Each_type_The_Forge_lists_renames()
        {
            try
            {
                var effect = ScriptableObject.CreateInstance<GameplayEffect>();
                effect.Definition.Name = "FN8 Burn";
                var item = ScriptableObject.CreateInstance<Item>();
                item.Definition.Name = "FN8 Sword";
                var attribute = ScriptableObject.CreateInstance<Attribute>();
                attribute.Name = "FN8 Grit";
                var set = ScriptableObject.CreateInstance<AttributeSet>();
                set.Name = "FN8 Hero Set";
                var entity = ScriptableObject.CreateInstance<EntityIdentity>();
                entity.Name = "FN8 Hero";
                var scaler = ScriptableObject.CreateInstance<ScalerAsset>();
                scaler.Scaler = new SimpleScaler { Name = "FN8 Curve", LevelValues = new[] { 1f, 2f } };
                var template = ScriptableObject.CreateInstance<RequirementTemplate>();
                template.Requirements = new AvoidRequireTagGroup { Name = "FN8 Grounded" };
                var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
                var assets = new (ScriptableObject Asset, string File, string Renamed)[]
                {
                    (effect, "FN8 Burn", "FN8 Scorch"), (item, "FN8 Sword", "FN8 Blade"), (attribute, "FN8 Grit", "FN8 Nerve"),
                    (set, "FN8 Hero Set", "FN8 Champion Set"), (entity, "FN8 Hero", "FN8 Champion"), (scaler, "FN8 Curve", "FN8 Ramp"),
                    (template, "FN8 Grounded", "FN8 Rooted"), (pipeline, "FN8 Pipeline", "FN8 Flow"),
                };
                foreach (var (asset, file, _) in assets) Saved(asset, file);

                foreach (var (asset, file, renamed) in assets)
                {
                    Assert.IsTrue(ForgeRenames.CanRename(asset), $"{file} can be renamed");
                    ForgeRenames.Rename(asset, renamed);
                    Assert.AreEqual(renamed, ForgeRenames.NameOf(asset), $"{file} is {renamed} now");
                    // A file leaves out a name's spaces, as a new asset's file does, unless it's the asset's only name
                    StringAssert.Contains(renamed.Replace(" ", ""), FileOf(asset).Replace(" ", ""), "and so is its file");
                }
                Assert.AreEqual("FN8 Flow", FileOf(pipeline), "a file that is its asset's only name keeps the name's spaces");
                Assert.AreEqual("FN8 Scorch", effect.Definition.Name, "the effect by its own name");
                Assert.AreEqual("FN8 Nerve", attribute.Name, "the attribute too");
                Assert.AreEqual("FN8 Ramp", scaler.Scaler.Name, "a Scaler asset by its scaler's");
                Assert.AreEqual("FN8 Rooted", template.Requirements.Name, "a template by its requirements'");
            }
            finally
            {
                AssetDatabase.DeleteAsset(RenameFolder);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // TAG-2: a core tag's text in New Tag
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// New Tag made the very tag one of PlayForge's own is where its path was that tag's text, and said nothing, so a
        /// designer's "Blocked" state was the core Blocked event (TAG-2, Decision 71). It says so as the path is typed, and
        /// offers the core tag; Create makes nothing of that path.
        /// </summary>
        [UnityTest]
        public IEnumerator TAG_2_New_Tag_warns_of_a_core_tags_text() => Guarded(TAG_2_New_Tag_warns_of_a_core_tags_textBody());

        private IEnumerator TAG_2_New_Tag_warns_of_a_core_tags_textBody()
        {
            var type = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeNewTagWindow");
            var given = new List<Tag>();
            var window = (EditorWindow)type.GetMethod("Show", AnyStatic).Invoke(null, new object[] { "", (System.Action<Tag>)(tag => given.Add(tag)) });
            try
            {
                yield return Frames();
                // Batch mode may build a window's view only as it's shown
                if (window.rootVisualElement.Q("TagPath") == null) type.GetMethod("CreateGUI", AnyInstance).Invoke(window, null);
                var root = window.rootVisualElement;
                var path = root.Q<TextField>("TagPath");
                var core = root.Q<HelpBox>("CoreTag");
                Assert.IsNotNull(path, "New Tag has its path");
                Assert.IsNotNull(core, "and a word for a core tag's text");
                Assert.IsTrue(core.ClassListContains("pf-hidden"), "hidden while the path is empty");

                path.value = "TAG2.Own.Path";
                yield return Frames();
                Assert.IsTrue(core.ClassListContains("pf-hidden"), "and for a path of the project's own");

                path.value = Tags.BLOCKED.ToString();
                yield return Frames();
                Assert.IsFalse(core.ClassListContains("pf-hidden"), "a core tag's text is called out as it's typed");
                StringAssert.Contains(Tags.BLOCKED.ToString(), core.text, "by the core tag's name");

                type.GetMethod("Create", AnyInstance).Invoke(window, null);
                Assert.IsEmpty(given, "Create makes nothing of that path");

                var use = core.Q<Button>("UseCoreTag");
                Assert.IsNotNull(use, "it offers the core tag");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = use;
                    use.SendEvent(submit);
                }
                yield return Frames();
                Assert.That(given, Is.EqualTo(new[] { Tags.BLOCKED }), "which Use It gives the field");
            }
            finally
            {
                if (window != null) window.Close();
            }
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

        // The summary's text, label by label
        private static List<string> SummaryOf(TheForge forge) =>
            forge.rootVisualElement.Q("Detail").Q("Summary").Query<Label>().ToList().Select(label => label.text).ToList();

        /// <summary>
        /// A Scaler asset in The Forge had an empty summary and couldn't be renamed there (the user's notes, ED-74), and a
        /// Scaler asset's and a Requirement Template's descriptions could only be read in the boxes that edit them (ED-78).
        /// The summary says each's description, as text, and a line for each of its parts; a Scaler asset's inspector
        /// names it, and The Forge lists it by the name.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_74_A_scaler_asset_has_a_summary_and_a_name_to_change() => Guarded(ED_74_A_scaler_asset_has_a_summary_and_a_name_to_changeBody());

        private IEnumerator ED_74_A_scaler_asset_has_a_summary_and_a_name_to_changeBody()
        {
            var scaler = ScriptableObject.CreateInstance<ScalerAsset>();
            scaler.Description = "Fire's damage, level by level";
            scaler.Scaler = new SimpleScaler { Name = "Fire Damage", LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = false };
            Save(scaler, "Fire Damage");
            var template = ScriptableObject.CreateInstance<RequirementTemplate>();
            template.Description = "Only while wet";
            template.Requirements = new AvoidRequireTagGroup
            {
                RequireTags = new List<TagQuery> { new() { Tag = Tag.Generate("Status.Wet") } },
                AvoidTags = new List<TagQuery>(),
            };
            Save(template, "Wet Only");
            var forge = Open();
            yield return Frames();

            TheForge.OpenToAsset(scaler);
            yield return Frames();
            var summary = SummaryOf(forge);
            Assert.That(summary, Has.Member("Fire's damage, level by level"), "a Scaler asset's summary says its description: " + string.Join(" | ", summary));
            Assert.That(summary, Has.Some.Contains("10/20/30"), "and what it reads");
            Assert.That(summary, Has.Member("Used By"), "and what uses it");

            var rename = forge.rootVisualElement.Q("Detail").Q<TextField>("ScalerName");
            Assert.IsNotNull(rename, "its inspector names it, to change");
            rename.value = "Blaze Damage";
            yield return Frames(8);
            Assert.AreEqual("Blaze Damage", scaler.DisplayName, "a new name renames it");
            Assert.AreEqual("Blaze Damage", typeof(TheForge).GetMethod("NameOf", AnyStatic).Invoke(null, new object[] { scaler }),
                "and The Forge lists it by the name");

            TheForge.OpenToAsset(template);
            yield return Frames();
            summary = SummaryOf(forge);
            Assert.That(summary, Has.Member("Only while wet"), "a Requirement Template's summary says its description: " + string.Join(" | ", summary));
            Assert.That(summary, Has.Some.Contains("Wet"), "and what it requires");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-76: back and forward
        // ═══════════════════════════════════════════════════════════════════════════

        private static Object ShownIn(TheForge forge) => (Object)typeof(TheForge).GetProperty("Selected", AnyInstance).GetValue(forge);

        // Presses a button, as a click does
        private static void Press(Button button)
        {
            Assert.IsNotNull(button, "the button is there");
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        /// <summary>
        /// The Forge went to an asset and had no way back to the one before (the user's request, ED-76). It goes back and
        /// forward over the assets it has shown, as a browser does, and a new asset after going back drops the way forward.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_76_The_forge_goes_back_and_forward() => Guarded(ED_76_The_forge_goes_back_and_forwardBody());

        private IEnumerator ED_76_The_forge_goes_back_and_forwardBody()
        {
            var fire = Save(Ability("ED76 Fireball"), "ED76 Fireball");
            var frost = Save(Ability("ED76 Frost"), "ED76 Frost");
            var ice = Save(Ability("ED76 Ice"), "ED76 Ice");
            var forge = Open();
            yield return Frames();
            var back = forge.rootVisualElement.Q<ToolbarButton>("Back");
            var forward = forge.rootVisualElement.Q<ToolbarButton>("Forward");
            Assert.IsNotNull(back, "the toolbar has Back");
            Assert.IsNotNull(forward, "and Forward");

            TheForge.OpenToAsset(fire);
            yield return Frames();
            ListOf(forge).SetSelection(Listed(forge).IndexOf(frost));
            yield return Frames();
            Assert.AreSame(frost, ShownIn(forge), "a click shows Frost");

            Press(back);
            yield return Frames();
            Assert.AreSame(fire, ShownIn(forge), "Back shows the asset before it");
            Assert.IsTrue(forward.enabledSelf, "and Forward is on");
            Press(forward);
            yield return Frames();
            Assert.AreSame(frost, ShownIn(forge), "Forward the one gone back from");

            Press(back);
            yield return Frames();
            TheForge.OpenToAsset(ice);
            yield return Frames();
            Assert.IsFalse(forward.enabledSelf, "a new asset after going back drops the way forward");
            Press(back);
            yield return Frames();
            Assert.AreSame(fire, ShownIn(forge), "and Back goes to the one it came from");
        }

        /// <summary>
        /// A row of an asset's Used By selected its asset in the Project window (the user's request, ED-76): it opens the
        /// asset in The Forge, and Back returns to the asset it was in.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_76_Used_by_opens_its_asset_in_the_forge() => Guarded(ED_76_Used_by_opens_its_asset_in_the_forgeBody());

        private IEnumerator ED_76_Used_by_opens_its_asset_in_the_forgeBody()
        {
            var cost = ScriptableObject.CreateInstance<GameplayEffect>();
            cost.Definition.Name = "ED76 Cost";
            Save(cost, "ED76 Cost");
            var fire = Ability("ED76 Burning Hands");
            fire.Cost = cost;
            Save(fire, "ED76 Burning Hands");
            AssetDatabase.SaveAssets();
            var forge = Open();
            yield return Frames();

            TheForge.OpenToAsset(cost);
            yield return Frames(8);
            var row = forge.rootVisualElement.Q("Detail").Query<Button>(className: "pf-used-by__row").ToList()
                .FirstOrDefault(each => each.name == "ED76 Burning Hands");
            Assert.IsNotNull(row, "the cost's Used By lists the ability");
            Press(row);
            yield return Frames();
            Assert.AreSame(fire, ShownIn(forge), "a click opens it in The Forge");
            Press(forge.rootVisualElement.Q<ToolbarButton>("Back"));
            yield return Frames();
            Assert.AreSame(cost, ShownIn(forge), "and Back returns to the cost");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-83: a tag's contexts
        // ═══════════════════════════════════════════════════════════════════════════

        private static System.Type EditorType(string name) => typeof(TheForge).Assembly.GetType($"FarEmerald.PlayForge.Extended.Editor.{name}");

        // Declares a tag for contexts, as The Forge does; none takes its declaration away
        private static void Declare(Tag tag, params string[] contexts) =>
            EditorType("ForgeTagContexts").GetMethod("Declare", AnyStatic).Invoke(null, new object[] { tag, contexts });

        // Whether a field of a context offers a tag, Universal ones set aside
        private static bool Offers(string context, Tag tag)
        {
            ForgeTagRegistry.RefreshCache();
            return ForgeTagRegistry.GetTagsForContext(new[] { context }, false).Any(each => each.Name == tag.Name);
        }

        private static void Toggle(VisualElement contexts, string context) =>
            contexts.GetType().GetMethod("Toggle", AnyInstance).Invoke(contexts, new object[] { context });

        /// <summary>
        /// A tag's contexts came only from the fields that used it, so they couldn't be changed, in The Forge or as a tag
        /// was made (the user's report, ED-83). The Forge's tag detail declares a tag for contexts, in the project's Tag
        /// Library, so their fields offer it, and taking them away takes the declaration away. New Tag's contexts start as
        /// its field's, and another chosen declares the new tag for it too.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_83_A_tags_contexts_can_be_changed() => Guarded(ED_83_A_tags_contexts_can_be_changedBody());

        private IEnumerator ED_83_A_tags_contexts_can_be_changedBody()
        {
            var tag = Tag.Generate("Test.ED83.Soaked");
            var fire = Ability("ED83 Fireball");
            fire.Tags.ContextTags = new List<Tag> { tag };
            Save(fire, "ED83 Fireball");
            Tag made = default;
            var forge = Open();
            yield return Frames();
            try
            {
                Assume.That(Offers(ForgeContext.Granted, tag), Is.False, "no Granted field offers it yet");
                forge.rootVisualElement.Q<ToolbarToggle>("Tags").value = true;
                forge.rootVisualElement.Q<ToolbarSearchField>("Search").value = "ED83";
                yield return Frames();
                var tags = forge.rootVisualElement.Q<MultiColumnListView>("TagList");
                var records = tags.itemsSource.Cast<ForgeTagRegistry.TagUsageRecord>().ToList();
                int at = records.FindIndex(record => record.Tag == tag);
                Assume.That(at, Is.GreaterThanOrEqualTo(0), "The Forge lists the tag");
                tags.SetSelection(at);
                yield return Frames();

                var field = forge.rootVisualElement.Q("Detail").Q("TagContexts");
                Assert.IsNotNull(field, "the tag's detail has the contexts it's declared for");
                Toggle(field, ForgeContext.Granted);
                yield return Frames(8);
                Assert.IsTrue(Offers(ForgeContext.Granted, tag), "declared for Granted, a Granted field offers it");
                Assert.IsTrue(Offers(ForgeContext.ContextIdentifier, tag), "as its use's context still does");

                field = forge.rootVisualElement.Q("Detail").Q("TagContexts");
                Assert.IsNotNull(field, "the detail, built again, still has them");
                Toggle(field, ForgeContext.Granted);
                yield return Frames(8);
                Assert.IsFalse(Offers(ForgeContext.Granted, tag), "and Granted taken away, it doesn't");

                // New Tag, from a field of the Context Identifier context, with Granted chosen as well
                var newTag = EditorType("ForgeNewTagWindow");
                var window = (EditorWindow)newTag.GetMethod("ShowFor", AnyStatic)
                    .Invoke(null, new object[] { "Test.ED83.", new[] { ForgeContext.ContextIdentifier }, (System.Action<Tag>)(each => made = each) });
                yield return Frames();
                // Batch mode may build a window's view only as it's shown
                if (window.rootVisualElement.Q("TagPath") == null) newTag.GetMethod("CreateGUI", AnyInstance).Invoke(window, null);
                var contexts = window.rootVisualElement.Q("TagContexts");
                Assert.IsNotNull(contexts, "New Tag has the tag's contexts");
                Assert.AreEqual(ForgeContext.ContextIdentifier, (contexts as BaseField<string>)?.value, "starting as its field's");
                Toggle(contexts, ForgeContext.Granted);
                window.rootVisualElement.Q<TextField>("TagPath").value = "Test.ED83.Drenched";
                newTag.GetMethod("Create", AnyInstance).Invoke(window, null);
                yield return Frames();
                Assert.IsTrue(made.IsValid, "it makes the tag");
                Assert.IsTrue(Offers(ForgeContext.Granted, made), "declared for Granted, chosen beside its field's context");
            }
            finally
            {
                Declare(tag);
                if (made.IsValid) Declare(made);
            }
        }

        /// <summary>
        /// The Forge opened its list 360 wide each time, however the user left it (ED-72, the user's request; its columns'
        /// widths were kept already, ED-48). It opens as wide as it was left.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_72_The_forge_opens_its_list_as_wide_as_it_was_left() => Guarded(ED_72_The_forge_opens_its_list_as_wide_as_it_was_leftBody());

        private IEnumerator ED_72_The_forge_opens_its_list_as_wide_as_it_was_leftBody()
        {
            using var kept = new KeptPreference("PlayForge_Forge.Split");
            EditorPrefs.SetString("PlayForge_Forge.Split", "480");
            var forge = Open();
            yield return Frames();
            var split = forge.rootVisualElement.Q<TwoPaneSplitView>("Split");
            Assert.IsNotNull(split, "The Forge splits its list from its detail");
            Assert.AreEqual(480f, split.fixedPaneInitialDimension, 0.5f, "and opens the list as wide as it was left");
        }

        // The items of Unity's own menu, by their text
        private static List<string> ItemsOf(GenericMenu menu)
        {
            var items = (IEnumerable)typeof(GenericMenu).GetField("m_MenuItems", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(menu);
            Assume.That(items, Is.Not.Null, "Unity's menu keeps its items where the test reads them");
            return items.Cast<object>()
                .Select(item => (item.GetType().GetField("content")?.GetValue(item) as GUIContent)?.text ?? "")
                .Where(text => text.Length > 0).ToList();
        }

        /// <summary>
        /// An asset header's three dots offered the header's own actions alone, and a right-click on the asset's row in
        /// The Forge's list others (the user's request, ED-71). One builder serves both: the header offers Show in Project,
        /// Open in a Window, Duplicate and Move to Trash after its own, as the row does.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_71_An_assets_header_menu_offers_what_its_row_does() => Guarded(ED_71_An_assets_header_menu_offers_what_its_row_doesBody());

        private IEnumerator ED_71_An_assets_header_menu_offers_what_its_row_doesBody()
        {
            var fire = Save(Ability("ED71 Fireball"), "ED71 Fireball");
            var forge = Open();
            yield return Frames();
            TheForge.OpenToAsset(fire);
            yield return Frames();

            var row = new DropdownMenu();
            typeof(TheForge).GetMethod("FillAssetMenu", AnyInstance).Invoke(forge, new object[] { row });
            var rowItems = row.MenuItems().OfType<DropdownMenuAction>().Select(action => action.name).ToList();

            var editor = UnityEditor.Editor.CreateEditor(fire);
            try
            {
                var header = (GenericMenu)typeof(BasePlayForgeEditor).GetMethod("AssetMenu", AnyInstance)
                    .Invoke(editor, new object[] { new VisualElement() });
                var headerItems = ItemsOf(header);
                foreach (var action in new[] { "Show in Project", "Open in a Window", "Duplicate", "Move to Trash" })
                {
                    Assert.That(rowItems, Has.Member(action), $"a row offers {action}");
                    Assert.That(headerItems, Has.Member(action), $"and so does the header: {string.Join(", ", headerItems)}");
                }
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }
    }
}
