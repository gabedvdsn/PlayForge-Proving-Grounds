using System;
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
    /// Project Settings → PlayForge, as the user's editor pass of 2026-11-15 asked (EP-3): a short description of PlayForge
    /// and Forge Projects at its top, where a dense note was; room between its parts; each row's foldout arrow in one
    /// column; and each New Assets folder shown within the active project's folder. The page is laid out in a runtime
    /// panel in the editor's theme, as batch mode gives the Settings window none, over a project of the test's own.
    /// </summary>
    public class SettingsPageLayoutTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Folder = "Assets/EP-3 Project";
        private const float Tolerance = 1f;

        private static Type Pages => typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.PlayForgeSettingsPages", true);

        private ForgeProject previous;
        private bool used;

        [SetUp]
        public void Make()
        {
            Assume.That(AssetDatabase.IsValidFolder(Folder), Is.False, "the folder is free");
            AssetDatabase.CreateFolder("Assets", "EP-3 Project");
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            AssetDatabase.CreateAsset(project, $"{Folder}/EP-3 Project.asset");
            previous = PlayForgeSettings.UseProject(project);
            used = true;
        }

        [TearDown]
        public void Clean()
        {
            if (used) PlayForgeSettings.UseProject(previous);
            used = false;
            AssetDatabase.DeleteAsset(Folder);
        }

        // The page in a runtime panel in the editor's theme, as wide as a Settings window's content
        private sealed class Host : IDisposable
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("EP-3") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Build()
            {
                var theme = EditorGUIUtility.isProSkin ? "Dark" : "Light";
                var sheet = EditorGUIUtility.Load($"StyleSheets/Generated/DefaultCommon{theme}_inter.uss.asset") as StyleSheet;
                Assert.IsNotNull(sheet, "the editor theme's style sheet loads");
                var root = new VisualElement();
                root.styleSheets.Add(sheet);
                root.style.width = 640;
                root.style.height = 3200;
                Document.rootVisualElement.Add(root);
                return (VisualElement)Pages.GetMethod("BuildProjectPage", AnyStatic).Invoke(null, new object[] { root });
            }

            public void Dispose()
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static IEnumerator Laid(VisualElement page)
        {
            for (int i = 0; i < 8; i++) yield return null;
        }

        private static bool Shown(VisualElement element)
        {
            var bound = element.worldBound;
            if (float.IsNaN(bound.x) || bound.width <= 0f || bound.height <= 0f) return false;
            for (var each = element; each != null; each = each.parent)
                if (each.resolvedStyle.display == DisplayStyle.None || each.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        // The page's rows: each child of a part of its own, a PropertyField, a field, a foldout or a table
        private static List<VisualElement> Rows(VisualElement page)
        {
            var parts = new[] { "PlayForgeFolder", "ForgeProject", "Authoring" };
            var rows = new List<VisualElement>();
            foreach (var part in parts)
            {
                var holder = page.Q(name: part);
                if (holder == null) continue;
                if (part == "PlayForgeFolder") rows.Add(holder);
                else rows.AddRange(holder.Children().Where(child => child is PropertyField || child is Foldout || child is BaseField<Object>));
            }
            return rows;
        }

        // A row's foldout arrow on its first line, where it has one
        private static VisualElement ArrowOf(VisualElement row)
        {
            float top = row.worldBound.y;
            return row.Query<VisualElement>(className: Toggle.checkmarkUssClassName).ToList()
                .Where(each => each.GetFirstAncestorOfType<Foldout>() != null && Shown(each) && each.worldBound.y <= top + 16f)
                .OrderBy(each => each.worldBound.x)
                .FirstOrDefault();
        }

        /// <summary>
        /// The page opens on a short description of PlayForge and Forge Projects, two sentences at most; and no note on it
        /// runs on, as the one at its top did, over 600 characters of roles and rules.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_3_The_page_opens_on_a_short_description()
        {
            using var host = new Host();
            yield return null;
            Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
            var page = host.Build();
            yield return Laid(page);

            var description = page.Q<Label>("PlayForgeDescription");
            Assert.IsNotNull(description, "a description under the title");
            StringAssert.Contains("Forge Project", description.text, "of PlayForge and Forge Projects");
            Assert.That(description.text.Count(c => c == '.'), Is.LessThanOrEqualTo(2), $"two sentences at most: {description.text}");
            var notes = page.Query<Label>(className: "pf-note").ToList().Select(note => note.text).Where(text => !string.IsNullOrEmpty(text)).ToList();
            var running = notes.Where(text => text.Length > 220).ToList();
            Assert.That(running, Is.Empty, $"no note runs on: {string.Join(" | ", running)}");
        }

        /// <summary>Each heading has room above it, 16 px at least past what comes before it, and the title room above it.</summary>
        [UnityTest]
        public IEnumerator EP_3_The_pages_parts_have_room()
        {
            using var host = new Host();
            yield return null;
            Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
            var page = host.Build();
            yield return Laid(page);

            var title = page.Q<Label>(className: "pf-settings__title");
            Assert.That(title.worldBound.y - page.worldBound.y, Is.GreaterThanOrEqualTo(8f), "the title has room above it");
            // Above each heading, what ends nearest it, in its part or the one before: no element that holds it or that it holds,
            // and nothing of the page's scroll bars, which run beside its content
            var content = page is ScrollView scroll ? scroll.contentContainer : page;
            var shown = content.Query<VisualElement>().ToList().Where(Shown).ToList();
            var tight = new List<string>();
            foreach (var heading in page.Query<Label>(className: "pf-settings__heading").ToList())
            {
                float top = heading.worldBound.y;
                var above = shown.Where(each => each != heading && !each.Contains(heading) && !heading.Contains(each)
                                                && each.worldBound.yMax <= top + 0.5f && each.worldBound.height > 0f).ToList();
                if (above.Count == 0) continue;
                float room = top - above.Max(each => each.worldBound.yMax);
                if (room < 16f) tight.Add($"{heading.text} {room:0.#}");
            }
            Assert.That(tight, Is.Empty, $"headings with too little room above them: {string.Join(", ", tight)}");
        }

        /// <summary>
        /// Each row's foldout arrow is in one column, its lists', its rules' and its defaults': Unity hung a class's arrow to
        /// the left of a list's.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_3_The_pages_arrows_start_in_one_column()
        {
            using var host = new Host();
            yield return null;
            Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
            var page = host.Build();
            yield return Laid(page);

            var arrows = Rows(page).Select(row => (row, arrow: ArrowOf(row))).Where(each => each.arrow != null)
                .ToDictionary(each => $"{each.row.name} ({each.row.GetType().Name})", each => each.arrow.worldBound.x);
            Assert.That(arrows.Count, Is.GreaterThanOrEqualTo(6), $"the rows that fold: {string.Join(", ", arrows.Keys)}");
            string Starts() => string.Join(", ", arrows.Select(each => $"{each.Key} {each.Value:0.#}"));
            Assert.That(arrows.Values.Max() - arrows.Values.Min(), Is.LessThan(Tolerance), $"each arrow in one column: {Starts()}");
        }

        /// <summary>New Assets shows each type's folder within the active project's: the project's folder, then the type's.</summary>
        [UnityTest]
        public IEnumerator EP_3_New_Assets_shows_each_folder_within_the_projects()
        {
            using var host = new Host();
            yield return null;
            Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
            var page = host.Build();
            yield return Laid(page);

            var row = page.Q(name: "NewAssets")?.Q(name: nameof(Ability));
            Assert.IsNotNull(row, "the abilities' row");
            Assert.AreEqual($"{Folder}/", row.Q<Label>("FolderRoot")?.text, "the project's folder");
            Assert.AreEqual("Abilities", row.Q<TextField>("Folder")?.value, "then the abilities', within it");
        }
    }
}
