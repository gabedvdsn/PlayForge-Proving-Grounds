using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Phase 4's gate, from the UI interview's Q36: in each editor theme, every PlayForge inspector and window opens
    /// without an exception, and an edit made in an inspector undoes. An inspector opens on new assets, on a multiple
    /// selection and on every asset the Demos sample ships. It is built in a runtime panel that carries the theme's style
    /// sheet, since that is where its bindings run in batch mode. A window opens the way the editor opens it.
    /// </summary>
    [TestFixture(EditorTheme.Dark)]
    [TestFixture(EditorTheme.Light)]
    public class EditorSmokeTests
    {
        public enum EditorTheme { Light, Dark }

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // The asset types PlayForge draws an inspector for
        private static readonly Type[] AssetTypes =
        {
            typeof(Ability), typeof(Attribute), typeof(AttributeSet), typeof(GameplayEffect), typeof(EntityIdentity),
            typeof(Item),
        };

        public sealed class SmokeProcess : AbstractMonoProcess
        {
            public override UniTask RunProcess(CancellationToken token) => UniTask.CompletedTask;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The theme
        // ═══════════════════════════════════════════════════════════════════════════

        // The editor's theme. Setting it also saves it as the user's own choice, machine-wide, so each fixture puts that
        // back as it was
        private static readonly PropertyInfo SkinIndex = typeof(EditorGUIUtility).GetProperty("skinIndex", AnyStatic);
        private const string SkinPreference = "UserSkin";

        private readonly EditorTheme theme;
        private bool themeSet;
        private int skinBefore;
        private bool hadSkinPreference;
        private int skinPreference;

        public EditorSmokeTests(EditorTheme theme) => this.theme = theme;

        [OneTimeSetUp]
        public void UseTheme()
        {
            Assume.That(SkinIndex, Is.Not.Null, "the editor's theme can be set");
            hadSkinPreference = EditorPrefs.HasKey(SkinPreference);
            skinPreference = EditorPrefs.GetInt(SkinPreference, 0);
            skinBefore = (int)SkinIndex.GetValue(null);
            themeSet = true;
            SkinIndex.SetValue(null, theme == EditorTheme.Dark ? 1 : 0);
            Assume.That(EditorGUIUtility.isProSkin, Is.EqualTo(theme == EditorTheme.Dark), "the editor takes the theme");
            CopyDemos();
        }

        [OneTimeTearDown]
        public void RestoreTheme()
        {
            CloseWindows();
            DeleteDemoCopies();
            if (!themeSet) return;
            SkinIndex.SetValue(null, skinBefore);
            if (hadSkinPreference) EditorPrefs.SetInt(SkinPreference, skinPreference);
            else EditorPrefs.DeleteKey(SkinPreference);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The Demos' assets
        // ═══════════════════════════════════════════════════════════════════════════

        // A package's Samples~ folder isn't imported, so the suite copies the Demos' data in while it runs
        private const string DemoCopies = "Assets/PlayForge Smoke Demos";
        private static readonly Dictionary<string, string> DemoPaths = new();

        private static string DemosFolder
        {
            get
            {
                var package = PackageInfo.FindForAssetPath("Packages/com.faremerald.playforge/package.json");
                return package == null ? null : Path.Combine(package.resolvedPath, "Samples~", "Demos");
            }
        }

        // Every asset in the Demos' Data folders, by its path in the sample
        public static IEnumerable<string> DemoAssets()
        {
            var folder = DemosFolder;
            if (folder == null || !Directory.Exists(folder)) yield break;
            foreach (var file in Directory.GetFiles(folder, "*.asset", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var inSample = file.Substring(folder.Length + 1).Replace('\\', '/');
                if (("/" + inSample).Contains("/Data/")) yield return inSample;
            }
        }

        private static void CopyDemos()
        {
            DeleteDemoCopies();
            DemoPaths.Clear();
            var folder = DemosFolder;
            foreach (var inSample in DemoAssets())
            {
                var source = Path.Combine(folder, inSample);
                // A project that imported the sample has the asset already, and a copy would clash with its GUID
                var guid = File.ReadLines(source + ".meta").Select(line => line.Trim())
                    .FirstOrDefault(line => line.StartsWith("guid: "))?.Substring(6);
                var imported = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(imported) && AssetDatabase.LoadMainAssetAtPath(imported) != null)
                {
                    DemoPaths[inSample] = imported;
                    continue;
                }
                var copy = $"{DemoCopies}/{inSample}";
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(source, copy, true);
                File.Copy(source + ".meta", copy + ".meta", true);
                DemoPaths[inSample] = copy;
            }
            AssetDatabase.Refresh();
        }

        private static void DeleteDemoCopies()
        {
            if (AssetDatabase.IsValidFolder(DemoCopies)) AssetDatabase.DeleteAsset(DemoCopies);
            if (!Directory.Exists(DemoCopies)) return;
            Directory.Delete(DemoCopies, true);
            File.Delete(DemoCopies + ".meta");
            AssetDatabase.Refresh();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // What an inspector opens on
        // ═══════════════════════════════════════════════════════════════════════════

        public sealed class Subject
        {
            public string Name;
            // Makes the selection, and adds each object it makes to the list, to destroy after
            public Func<List<Object>, Object[]> Make;
            public override string ToString() => Name;
        }

        private static T Keep<T>(List<Object> made, T made1) where T : Object
        {
            made.Add(made1);
            return made1;
        }

        private static Object NewAsset(List<Object> made, Type type) => Keep(made, ScriptableObject.CreateInstance(type));

        private static Object NewProcess(List<Object> made) =>
            Keep(made, new GameObject("Smoke Process")).AddComponent<SmokeProcess>();

        public static IEnumerable<Subject> NewSubjects()
        {
            foreach (var type in AssetTypes)
            {
                yield return new Subject { Name = $"a new {type.Name}", Make = made => new[] { NewAsset(made, type) } };
                yield return new Subject
                {
                    Name = $"two new {type.Name}s", Make = made => new[] { NewAsset(made, type), NewAsset(made, type) },
                };
            }
            yield return new Subject { Name = "a MonoProcess", Make = made => new[] { NewProcess(made) } };
            yield return new Subject { Name = "two MonoProcesses", Make = made => new[] { NewProcess(made), NewProcess(made) } };
            // A Scaler asset's inspector has no asset header, so it isn't among the asset types (ED-33)
            yield return new Subject { Name = "a new Scaler asset", Make = made => new[] { NewAsset(made, typeof(ScalerAsset)) } };
        }

        // A new asset of each type, a MonoProcess, and the fullest Demo asset in each of the Demos' folders
        public static IEnumerable<Subject> EditSubjects()
        {
            foreach (var type in AssetTypes)
                yield return new Subject { Name = $"a new {type.Name}", Make = made => new[] { NewAsset(made, type) } };
            yield return new Subject { Name = "a MonoProcess", Make = made => new[] { NewProcess(made) } };
            yield return new Subject { Name = "a new Scaler asset", Make = made => new[] { NewAsset(made, typeof(ScalerAsset)) } };

            var folder = DemosFolder;
            foreach (var group in DemoAssets().GroupBy(inSample => Path.GetDirectoryName(inSample)))
            {
                var fullest = group.OrderByDescending(inSample => new FileInfo(Path.Combine(folder, inSample)).Length).First();
                yield return new Subject
                {
                    Name = $"Demo {fullest}",
                    Make = _ =>
                    {
                        Assume.That(DemoPaths.ContainsKey(fullest), "the Demos sample is in the package");
                        return new[] { AssetDatabase.LoadMainAssetAtPath(DemoPaths[fullest]) };
                    },
                };
            }
        }

        // A runtime panel to build inspectors in, carrying the theme's style sheet
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("Smoke") { hideFlags = HideFlags.HideAndDontSave };
            private readonly List<UnityEditor.Editor> editors = new();
            private readonly EditorTheme theme;

            public UIDocument Document { get; }

            public Host(EditorTheme theme)
            {
                this.theme = theme;
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Inspect(params Object[] targets)
            {
                Assert.That(targets, Has.All.Not.Null, "the selection exists");
                var editor = UnityEditor.Editor.CreateEditor(targets);
                editors.Add(editor);
                // A PlayForge inspector builds its own UI. Any other is Unity's default inspector
                var root = editor.CreateInspectorGUI() ?? new InspectorElement(editor);
                var sheet = EditorGUIUtility.Load($"StyleSheets/Generated/DefaultCommon{theme}_inter.uss.asset") as StyleSheet;
                Assert.IsNotNull(sheet, $"the {theme} theme's style sheet loads");
                if (!Document.rootVisualElement.styleSheets.Contains(sheet)) Document.rootVisualElement.styleSheets.Add(sheet);
                // In a scroll view, as the Inspector holds an inspector, so it takes its full height. Squeezed into the
                // panel, a list drew only some of its rows
                var scroll = new ScrollView();
                scroll.Add(root);
                Document.rootVisualElement.Add(scroll);
                // The Inspector binds what an editor builds to its SerializedObject
                root.Bind(editor.serializedObject);
                return root;
            }

            public void Destroy()
            {
                foreach (var editor in editors) if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static void Destroy(List<Object> made)
        {
            foreach (var each in made)
                if (each != null) Object.DestroyImmediate(each);
        }

        // Lets the panel run its bindings and the UI's scheduled work
        private static IEnumerator Frames(int count = 4)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        // Presses a button, as a user does
        private static void Press(Button button)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static bool Shown(VisualElement element)
        {
            for (var each = element; each != null; each = each.hierarchy.parent)
                if (each.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        // Opens every collapsed part, so the suite reaches what's inside: the inspector's sections, the drawers' collapsed
        // parts and every foldout. A drawer rebuilds as it opens, and may show more to open, so this goes on until
        // nothing is left closed. A section is shown without its header's arrow, which would save its state as the
        // user's, machine-wide, where a collapsed section would otherwise hide its fields from the suite
        private static IEnumerator ExpandAll(VisualElement root)
        {
            for (int pass = 0; pass < 10; pass++)
            {
                var sections = root.Query(className: "pf-section__content").ToList()
                    .Where(section => section.resolvedStyle.display == DisplayStyle.None).ToList();
                var closed = root.Query<Button>().ToList()
                    .Where(button => (button.tooltip == "Expand" || button.text == "▶") && !button.ClassListContains("pf-section__arrow")
                                     && button.enabledInHierarchy && Shown(button))
                    .ToList();
                var foldouts = root.Query<Foldout>().ToList().Where(foldout => !foldout.value && Shown(foldout)).ToList();
                if (sections.Count == 0 && closed.Count == 0 && foldouts.Count == 0) yield break;
                foreach (var section in sections) section.style.display = DisplayStyle.Flex;
                foreach (var foldout in foldouts) foldout.value = true;
                foreach (var button in closed) Press(button);
                yield return Frames(2);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Every inspector opens
        // ═══════════════════════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Every_inspector_opens([ValueSource(nameof(NewSubjects))] Subject subject)
        {
            var made = new List<Object>();
            var host = new Host(theme);
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(subject.Make(made));
                yield return Frames();
                yield return ExpandAll(root);
            }
            finally
            {
                host.Destroy();
                Destroy(made);
            }
        }

        [UnityTest]
        public IEnumerator Every_Demo_asset_opens([ValueSource(nameof(DemoAssets))] string demo)
        {
            Assume.That(DemoPaths.ContainsKey(demo), "the Demos sample is in the package");
            var asset = AssetDatabase.LoadMainAssetAtPath(DemoPaths[demo]);
            Assert.IsNotNull(asset, $"{demo} loads");
            var host = new Host(theme);
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(asset);
                yield return Frames();
                yield return ExpandAll(root);

                // The suite's own check: every element of every list is drawn, so its drawer opens too. Squeezed into
                // the panel, a list drew only some of its rows
                var undrawn = root.Query<ListView>().ToList()
                    .Where(list => list.Query(className: ListView.itemUssClassName).ToList().Count < (list.itemsSource?.Count ?? 0))
                    .Select(list => list.bindingPath ?? list.name).ToList();
                Assert.That(undrawn, Is.Empty, "lists that draw fewer rows than they hold");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>Each inspector PlayForge draws is one the suite opens, so a new inspector is opened too.</summary>
        [Test]
        public void Every_inspector_is_opened()
        {
            var inspected = typeof(CustomEditor).GetField("m_InspectedType", Private);
            Assume.That(inspected, Is.Not.Null, "an inspector names the type it draws");
            var opened = AssetTypes.Append(typeof(SmokeProcess)).Append(typeof(ScalerAsset)).ToList();
            var missing = typeof(TheForge).Assembly.GetTypes()
                .SelectMany(type => type.GetCustomAttributes<CustomEditor>(false))
                .Select(attribute => (Type)inspected.GetValue(attribute))
                .Where(type => !opened.Any(type.IsAssignableFrom))
                .Select(type => type.Name).Distinct().OrderBy(name => name).ToList();
            Assert.That(missing, Is.Empty, "inspectors the suite doesn't open");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The look comes from the style sheet (ED-20)
        // ═══════════════════════════════════════════════════════════════════════════

        // Every color Docs/EDITOR_STYLE.md lists, by its variable in PlayForge's style sheet, in Dark and in Light
        private static readonly (string Variable, string Dark, string Light)[] Tokens =
        {
            ("--pf-panel", "#383838", "#C8C8C8"),
            ("--pf-tab-strip", "#282828", "#A5A5A5"),
            ("--pf-toolbar", "#3C3C3C", "#CBCBCB"),
            ("--pf-header", "#3E3E3E", "#D5D5D5"),
            ("--pf-line", "#232323", "#999999"),
            ("--pf-inner-line", "#2E2E2E", "#B5B5B5"),
            ("--pf-field", "#2A2A2A", "#F0F0F0"),
            ("--pf-field-border", "#212121", "#A5A5A5"),
            ("--pf-button", "#585858", "#E4E4E4"),
            ("--pf-button-border", "#303030", "#A0A0A0"),
            ("--pf-list-header", "#353535", "#CDCDCD"),
            ("--pf-list-body", "#414141", "#DEDEDE"),
            ("--pf-alternate-row", "#3B3B3B", "#CDCDCD"),
            ("--pf-help-box", "#414141", "#DCDCDC"),
            ("--pf-help-box-border", "#2B2B2B", "#ABABAB"),
            ("--pf-selection", "#2C5D87", "#3A72B0"),
            ("--pf-toggle-on", "#46607C", "#9DB7D8"),
            ("--pf-text", "#C4C4C4", "#1F1F1F"),
            ("--pf-strong-text", "#D2D2D2", "#090909"),
            ("--pf-secondary-text", "#B0B0B0", "#4A4A4A"),
            ("--pf-button-text", "#EEEEEE", "#090909"),
            ("--pf-link", "#81B4FF", "#0A4FA8"),
            ("--pf-type-ability", "#80ADE1", "#1757A0"),
            ("--pf-type-effect", "#A979E0", "#4F277E"),
            ("--pf-type-entity", "#D26BAC", "#B61B7D"),
            ("--pf-type-item", "#4AD077", "#1D6033"),
            ("--pf-type-attribute", "#A8E8E6", "#1F5F5D"),
            ("--pf-type-attribute-set", "#88C8D8", "#13687E"),
            ("--pf-warning", "#DBB468", "#D9A21B"),
            ("--pf-warning-text", "#DBB468", "#6B4C10"),
            ("--pf-destructive", "#EA8A85", "#902E28"),
        };

        // Each asset type's icon color, by the variable that holds it
        private static readonly Dictionary<Type, string> TypeColors = new()
        {
            [typeof(Ability)] = "--pf-type-ability",
            [typeof(GameplayEffect)] = "--pf-type-effect",
            [typeof(EntityIdentity)] = "--pf-type-entity",
            [typeof(Item)] = "--pf-type-item",
            [typeof(Attribute)] = "--pf-type-attribute",
            [typeof(AttributeSet)] = "--pf-type-attribute-set",
        };

        private Color Expected(string variable)
        {
            var token = Tokens.First(each => each.Variable == variable);
            ColorUtility.TryParseHtmlString(theme == EditorTheme.Dark ? token.Dark : token.Light, out var color);
            return color;
        }

        private static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.003f && Mathf.Abs(a.g - b.g) < 0.003f && Mathf.Abs(a.b - b.b) < 0.003f;

        // The styles a control may only take from the style sheet: its colors, borders, text and box
        private static IEnumerable<string> StylesSetInCode(VisualElement element)
        {
            var style = element.style;
            if (style.backgroundColor.keyword != StyleKeyword.Null) yield return "background-color";
            if (style.color.keyword != StyleKeyword.Null) yield return "color";
            if (style.borderTopColor.keyword != StyleKeyword.Null || style.borderBottomColor.keyword != StyleKeyword.Null ||
                style.borderLeftColor.keyword != StyleKeyword.Null || style.borderRightColor.keyword != StyleKeyword.Null)
                yield return "border-color";
            if (style.borderTopWidth.keyword != StyleKeyword.Null || style.borderBottomWidth.keyword != StyleKeyword.Null ||
                style.borderLeftWidth.keyword != StyleKeyword.Null || style.borderRightWidth.keyword != StyleKeyword.Null)
                yield return "border-width";
            if (style.borderTopLeftRadius.keyword != StyleKeyword.Null || style.borderTopRightRadius.keyword != StyleKeyword.Null ||
                style.borderBottomLeftRadius.keyword != StyleKeyword.Null || style.borderBottomRightRadius.keyword != StyleKeyword.Null)
                yield return "border-radius";
            if (style.fontSize.keyword != StyleKeyword.Null) yield return "font-size";
            if (style.unityFontStyleAndWeight.keyword != StyleKeyword.Null) yield return "font-style";
            if (style.width.keyword != StyleKeyword.Null || style.height.keyword != StyleKeyword.Null) yield return "size";
            if (style.paddingTop.keyword != StyleKeyword.Null || style.paddingBottom.keyword != StyleKeyword.Null ||
                style.paddingLeft.keyword != StyleKeyword.Null || style.paddingRight.keyword != StyleKeyword.Null)
                yield return "padding";
            if (style.marginTop.keyword != StyleKeyword.Null || style.marginBottom.keyword != StyleKeyword.Null ||
                style.marginLeft.keyword != StyleKeyword.Null || style.marginRight.keyword != StyleKeyword.Null)
                yield return "margin";
            if (style.display.keyword != StyleKeyword.Null) yield return "display";
        }

        /// <summary>
        /// An inspector carries PlayForge's one style sheet in the editor's theme, and each color
        /// Docs/EDITOR_STYLE.md lists resolves to its value in that theme. The inspectors drew in hard-coded dark colors,
        /// whatever the theme (ED-20).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_20_An_inspector_takes_each_color_from_the_style_sheet()
        {
            var attribute = ScriptableObject.CreateInstance<Attribute>();
            var host = new Host(theme);
            var wrong = new List<string>();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(attribute);
                yield return Frames();
                foreach (var (variable, _, _) in Tokens)
                {
                    if (!root.customStyle.TryGetValue(new CustomStyleProperty<Color>(variable), out var color)) wrong.Add($"{variable} is missing");
                    else if (!Same(color, Expected(variable))) wrong.Add($"{variable} is #{ColorUtility.ToHtmlStringRGB(color)}");
                }
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(attribute);
            }
            Assert.That(wrong, Is.Empty, $"colors in the {theme} theme");
        }

        /// <summary>
        /// The asset header shows the asset's type icon, in its type's color and nowhere else (ED-20).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_20_The_header_shows_the_type_icon_in_its_color()
        {
            var made = new List<Object>();
            var host = new Host(theme);
            var wrong = new List<string>();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                foreach (var type in AssetTypes)
                {
                    var root = host.Inspect(NewAsset(made, type));
                    yield return Frames();
                    var icon = root.Q(className: "pf-asset-header")?.Q(className: "pf-type-icon");
                    if (icon == null) wrong.Add($"{type.Name}: no type icon");
                    else if (!Same(icon.resolvedStyle.color, Expected(TypeColors[type])))
                        wrong.Add($"{type.Name}: #{ColorUtility.ToHtmlStringRGB(icon.resolvedStyle.color)}");
                }
            }
            finally
            {
                host.Destroy();
                Destroy(made);
            }
            Assert.That(wrong, Is.Empty);
        }

        /// <summary>
        /// The asset header and each section header take their look from the style sheet alone: no color, border, font
        /// or box set in code (ED-20). Each set dozens of them, in colors that held in the Dark theme only.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_20_The_header_and_sections_take_their_look_from_the_style_sheet()
        {
            var made = new List<Object>();
            var host = new Host(theme);
            var wrong = new List<string>();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                foreach (var type in AssetTypes)
                {
                    var root = host.Inspect(NewAsset(made, type));
                    yield return Frames();
                    var parts = root.Query(className: "pf-asset-header").ToList()
                        .Concat(root.Query(className: "pf-section__header").ToList()).ToList();
                    if (parts.Count < 2) wrong.Add($"{type.Name}: {parts.Count} headers");
                    foreach (var part in parts)
                    foreach (var element in part.Query<VisualElement>().ToList())
                    foreach (var style in StylesSetInCode(element))
                        wrong.Add($"{type.Name}: {element.GetType().Name} \"{element.name}\" sets {style}");
                }
            }
            finally
            {
                host.Destroy();
                Destroy(made);
            }
            Assert.That(wrong.Distinct(), Is.Empty);
        }

        /// <summary>
        /// A section header shows Import, Clear and Help at all times. They appeared only while the pointer was over the
        /// header (Q18, ED-20).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_20_A_section_shows_its_actions_at_all_times()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            var host = new Host(theme);
            var hidden = new List<string>();
            var actions = 0;
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(effect);
                yield return Frames();
                foreach (var action in root.Query(className: "pf-section__action").ToList())
                {
                    actions++;
                    if (!Shown(action)) hidden.Add(action.tooltip);
                }
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
            Assert.That(actions, Is.GreaterThan(0), "the sections have actions");
            Assert.That(hidden, Is.Empty, "actions hidden until the pointer passes");
        }

        /// <summary>
        /// The Forge and the GAS Debugger take the editor's theme and the style sheet. Each painted its window a
        /// hard-coded dark gray, whatever the theme (ED-20).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_20_The_Forge_and_the_GAS_Debugger_take_the_theme()
        {
            var log = new WindowLog();
            var wrong = new List<string>();
            var themeClass = theme == EditorTheme.Dark ? "pf-theme-dark" : "pf-theme-light";
            try
            {
                CloseWindows();
                foreach (var open in new Func<EditorWindow>[]
                         {
                             () => { TheForge.ShowWindow(); return EditorWindow.GetWindow<TheForge>(); },
                             () => { GASDebugger.ShowWindow(); return EditorWindow.GetWindow<GASDebugger>(); },
                         })
                {
                    var window = open();
                    yield return Frames();
                    var root = window.rootVisualElement;
                    var name = window.GetType().Name;
                    if (!root.ClassListContains(themeClass)) wrong.Add($"{name} has no {themeClass}");
                    if (root.style.backgroundColor.keyword != StyleKeyword.Null) wrong.Add($"{name} paints its background in code");
                    if (!root.Children().Prepend(root).Any(each => each.styleSheets.count > 0 && Enumerable.Range(0, each.styleSheets.count)
                            .Any(i => each.styleSheets[i].name == "PlayForge")))
                        wrong.Add($"{name} lacks the style sheet");
                }
            }
            finally
            {
                CloseWindows();
                log.Dispose();
            }
            Assert.That(wrong, Is.Empty);
            Assert.That(log.Errors, Is.Empty, "errors while the windows were open");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Every window opens
        // ═══════════════════════════════════════════════════════════════════════════

        public sealed class Window
        {
            public string Name;
            public Type Type;
            // Opens the window as the editor does, adding each object it makes to the list, to destroy after
            public Action<List<Object>> Open;
            public override string ToString() => Name;
        }

        private const string ScalerPath = "DurationSpecification.DurationOperation.Scaler";

        private static SerializedProperty Property(Object target, string path)
        {
            var property = new SerializedObject(target).FindProperty(path);
            Assert.IsNotNull(property, $"{target.GetType().Name} has {path}");
            return property;
        }

        private static T New<T>(List<Object> made) where T : ScriptableObject => Keep(made, ScriptableObject.CreateInstance<T>());

        private static GameplayEffect EffectWithScaler(List<Object> made)
        {
            var effect = New<GameplayEffect>(made);
            effect.DurationSpecification ??= new GameplayEffectDuration();
            effect.DurationSpecification.DurationOperation ??= new ScalerMagnitudeOperation();
            effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler();
            return effect;
        }

        private static void OpenForge(int tab)
        {
            TheForge.ShowWindow();
            typeof(TheForge).GetMethod("ShowTab", Private).Invoke(EditorWindow.GetWindow<TheForge>(), new object[] { tab });
        }

        public static IEnumerable<Window> Windows()
        {
            string[] tabs = { "Create", "View", "Analysis", "Settings" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = i;
                yield return new Window { Name = $"The Forge, {tabs[tab]}", Type = typeof(TheForge), Open = _ => OpenForge(tab) };
            }
            yield return new Window
            {
                Name = "The Forge, Settings, Assets", Type = typeof(TheForge),
                Open = _ =>
                {
                    OpenForge(3);
                    typeof(TheForge).GetMethod("ShowSettingsSubTab", Private)
                        .Invoke(EditorWindow.GetWindow<TheForge>(), new object[] { 1 });
                },
            };
            yield return new Window
            {
                Name = "Visualizer", Type = typeof(PlayForgeVisualizer),
                Open = made => TheForge.OpenVisualizer(New<Ability>(made)),
            };
            yield return new Window { Name = "GAS Debugger", Type = typeof(GASDebugger), Open = _ => GASDebugger.ShowWindow() };
            yield return new Window
            {
                Name = "Process Control", Type = typeof(ProcessControlDebugger), Open = _ => ProcessControlDebugger.ShowWindow(),
            };
            yield return new Window { Name = "Tag Debugger", Type = typeof(RuntimeTagViewer), Open = _ => RuntimeTagViewer.ShowWindow() };
            yield return new Window
            {
                Name = "Asset Browser", Type = typeof(AssetBrowserWindow),
                Open = _ => AssetBrowserWindow.Show(AssetBrowserConfig.Create("Smoke").ForTypes(typeof(Ability))),
            };
            // The one Import window serves sections, whole assets, worker groups and requirement groups (ED-32)
            yield return new Window
            {
                Name = "Import", Type = typeof(ForgeImportWindow),
                Open = made => ForgeImportWindow.ShowFor(new SerializedObject(New<GameplayEffect>(made)), "Duration",
                    new[] { nameof(GameplayEffect.DurationSpecification) }),
            };
            // Import Values and Save as Scaler Asset, from a magnitude field's menu (ED-33)
            yield return new Window
            {
                Name = "Import Values", Type = typeof(ScalerValuesWindow),
                Open = made => ScalerValuesWindow.Show(Property(EffectWithScaler(made), ScalerPath), "Smoke Duration"),
            };
            yield return new Window
            {
                Name = "Save as Scaler Asset", Type = typeof(SaveScalerAssetWindow),
                Open = made => SaveScalerAssetWindow.Show(Property(EffectWithScaler(made), "DurationSpecification.DurationOperation"),
                    "Smoke Duration"),
            };
            yield return new Window
            {
                Name = "Quick Fill Wizard", Type = typeof(QuickFillWizard),
                Open = made => QuickFillWizard.Show(Property(EffectWithScaler(made), ScalerPath), () => { }),
            };
            yield return new Window
            {
                Name = "Create Linked Effect", Type = typeof(CreateLinkedEffectDialog),
                Open = made =>
                {
                    var ability = New<Ability>(made);
                    var dialog = ScriptableObject.CreateInstance<CreateLinkedEffectDialog>();
                    dialog.Initialize(ability, ability, _ => { }, null);
                    dialog.ShowUtility();
                },
            };
            yield return new Window
            {
                Name = "Level Provider Picker", Type = typeof(LevelProviderPickerWindow),
                Open = made =>
                {
                    var picker = ScriptableObject.CreateInstance<LevelProviderPickerWindow>();
                    picker.Initialize(Property(New<GameplayEffect>(made), "_linkedSource"), () => { });
                    picker.ShowUtility();
                },
            };
            foreach (var info in TheForge.AssetTypes.Where(info => info.CanCreate))
            {
                yield return new Window
                {
                    Name = $"Create {info.DisplayName}", Type = typeof(CreateAssetDialog),
                    Open = _ =>
                    {
                        var dialog = ScriptableObject.CreateInstance<CreateAssetDialog>();
                        dialog.Initialize(info, "Assets", () => { });
                        dialog.ShowUtility();
                    },
                };
            }
            yield return new Window
            {
                Name = "Template Tag", Type = typeof(EditTemplateTagDialog),
                Open = _ =>
                {
                    var dialog = ScriptableObject.CreateInstance<EditTemplateTagDialog>();
                    dialog.Initialize(new TemplateTag("Smoke", Color.gray), () => { });
                    dialog.ShowUtility();
                },
            };
            var typePicker = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.TypePickerWindow");
            yield return new Window
            {
                Name = "Type Picker", Type = typePicker,
                Open = _ => typePicker.GetMethod("Open", AnyStatic).Invoke(null,
                    new object[] { new Rect(0, 0, 240, 20), typeof(AbstractAbilityTask), (Action<Type>)(_ => { }), null }),
            };
        }

        private static void CloseWindows()
        {
            var playForge = typeof(TheForge).Assembly;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Assembly == playForge).ToList())
                window.Close();
        }

        // Runs an IMGUI window's OnGUI once, for layout, as its view would. Batch mode repaints no view, so it never
        // runs on its own
        private static void Draw(EditorWindow window)
        {
            if (window.GetType().GetMethod("OnGUI", Private | BindingFlags.Public) == null) return;
            var view = window.rootVisualElement.hierarchy.parent?.Children().OfType<IMGUIContainer>().FirstOrDefault();
            Assert.IsNotNull(view, $"{window.GetType().Name} has an IMGUI view");
            var handle = typeof(IMGUIContainer).GetMethod("HandleIMGUIEvent", Private, null,
                new[] { typeof(Event), typeof(bool) }, null);
            Assume.That(handle, Is.Not.Null, "an IMGUI view can be handed an event");
            try
            {
                handle.Invoke(view, new object[] { new Event { type = EventType.Layout }, false });
            }
            catch (TargetInvocationException e)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
        }

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens. Every other
        // error or exception logged while it's open is the window's own
        private sealed class WindowLog : IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public List<string> Errors { get; } = new();

            public WindowLog()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(NoDevice))
                    Errors.Add($"{type}: {message}");
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [UnityTest]
        public IEnumerator Every_window_opens([ValueSource(nameof(Windows))] Window window)
        {
            var made = new List<Object>();
            var log = new WindowLog();
            try
            {
                CloseWindows();
                window.Open(made);
                yield return Frames();
                var opened = Resources.FindObjectsOfTypeAll(window.Type).OfType<EditorWindow>().ToList();
                Assert.IsNotEmpty(opened, $"{window.Type.Name} opens");
                foreach (var each in opened) Draw(each);
                yield return Frames();
            }
            finally
            {
                CloseWindows();
                Destroy(made);
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the window was open");
        }

        private sealed class DrawnWindow : EditorWindow
        {
            public int Draws;
            private void OnGUI() => Draws++;
        }

        /// <summary>The suite's own check: handing an IMGUI window's view a layout event runs its OnGUI.</summary>
        [Test]
        public void An_IMGUI_window_draws()
        {
            var log = new WindowLog();
            var window = ScriptableObject.CreateInstance<DrawnWindow>();
            try
            {
                window.Show();
                Draw(window);
                Assert.That(window.Draws, Is.GreaterThan(0), "OnGUI ran");
            }
            finally
            {
                window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty);
        }

        /// <summary>Each window PlayForge has is one the suite opens, so a new window is opened too.</summary>
        [Test]
        public void Every_window_is_opened()
        {
            var opened = new HashSet<Type>(Windows().Select(window => window.Type));
            var missing = typeof(TheForge).Assembly.GetTypes()
                .Where(type => typeof(EditorWindow).IsAssignableFrom(type) && !type.IsAbstract && !opened.Contains(type))
                .Select(type => type.Name).OrderBy(name => name).ToList();
            Assert.That(missing, Is.Empty, "windows the suite doesn't open");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Every edit undoes
        // ═══════════════════════════════════════════════════════════════════════════

        // The link questions, answered yes while edits are made, since a dialog would block the run
        private sealed class AnswerLinksYes : IDisposable
        {
            private static readonly FieldInfo Hook = typeof(BasePlayForgeEditor).GetField("ConfirmLinkChange", AnyStatic);
            private readonly object original;

            public AnswerLinksYes()
            {
                original = Hook?.GetValue(null);
                Hook?.SetValue(null, (Func<string, string, string, bool>)((_, _, _) => true));
            }

            public void Dispose() => Hook?.SetValue(null, original);
        }

        // A control a user edits a value through: a text, number, toggle, popup or enum field
        private static bool IsEditable(VisualElement element) => element switch
        {
            Toggle toggle => !toggle.ClassListContains(Foldout.toggleUssClassName),
            TextField text => !text.isReadOnly,
            IntegerField or LongField or FloatField or DoubleField or SliderInt or Slider => true,
            PopupField<string> popup => popup.choices?.Count > 1,
            EnumField field => field.value != null && !field.value.GetType().IsDefined(typeof(FlagsAttribute), false),
            _ => false,
        };

        private static List<VisualElement> Controls(VisualElement root) => root.Query<VisualElement>().ToList()
            .Where(element => element.panel != null && element.enabledInHierarchy && IsEditable(element) && Shown(element))
            .ToList();

        // Commits a typed value, as a user does, by moving the focus away. Only a delayed field waits for that
        private static void Commit(TextField field)
        {
            if (!field.isDelayed) return;
            using var focusOut = FocusOutEvent.GetPooled(field, null, FocusChangeDirection.unspecified, field.panel.focusController);
            field.SendEvent(focusOut);
        }

        // Changes a control's value to another one it allows
        private static void Change(VisualElement control)
        {
            switch (control)
            {
                case Toggle toggle: toggle.value = !toggle.value; break;
                case TextField text: text.value += "x"; Commit(text); break;
                case IntegerField number: number.value += 1; break;
                case LongField number: number.value += 1; break;
                case FloatField number: number.value += 1.5f; break;
                case DoubleField number: number.value += 1.5; break;
                case SliderInt slider: slider.value = slider.value < slider.highValue ? slider.value + 1 : slider.lowValue; break;
                case Slider slider:
                    slider.value = slider.value < slider.highValue
                        ? Mathf.Min(slider.highValue, slider.value + (slider.highValue - slider.lowValue) / 4f)
                        : slider.lowValue;
                    break;
                case PopupField<string> popup: popup.index = (popup.index + 1) % popup.choices.Count; break;
                case EnumField field:
                    var values = Enum.GetValues(field.value.GetType());
                    field.value = (Enum)values.GetValue((Array.IndexOf(values, field.value) + 1) % values.Length);
                    break;
            }
        }

        private static string Describe(VisualElement control)
        {
            var label = control.GetType().GetProperty("label")?.GetValue(control) as string;
            var bound = (control as IBindable)?.bindingPath;
            var within = control.GetFirstAncestorOfType<PropertyField>()?.bindingPath;
            var name = string.IsNullOrEmpty(label) ? control.name : label;
            return $"{control.GetType().Name} \"{name}\"" +
                   (string.IsNullOrEmpty(bound) ? "" : $" bound to {bound}") +
                   (string.IsNullOrEmpty(within) || within == bound ? "" : $" in {within}");
        }

        // What one sweep of an inspector found
        private sealed class Sweep
        {
            public readonly List<string> Broken = new();
            public int Edits;
            public int Controls;
        }

        // Changes every value the inspector shows, one at a time, and takes each change back with one Undo. The target
        // must then be exactly as it was. A change that leaves the target as it was, such as a search box, asks nothing
        private static IEnumerator SweepEdits(VisualElement root, Object target, Sweep sweep)
        {
            // An edit that records no Undo must find nothing else to undo
            Undo.ClearAll();
            for (int i = 0; ; i++)
            {
                // The inspector may rebuild after an edit, so its controls are found again each time
                var controls = Controls(root);
                if (i >= controls.Count) yield break;
                sweep.Controls = Math.Max(sweep.Controls, controls.Count);
                var control = controls[i];
                var before = EditorJsonUtility.ToJson(target);
                Undo.IncrementCurrentGroup();
                Change(control);
                yield return Frames(2);
                if (EditorJsonUtility.ToJson(target) == before) continue;

                sweep.Edits++;
                Undo.PerformUndo();
                yield return Frames(2);
                if (EditorJsonUtility.ToJson(target) != before) sweep.Broken.Add(Describe(control));
            }
        }

        /// <summary>
        /// Every value an inspector shows, with every collapsed part opened, is changed as a user would change it, and
        /// one Undo takes each change back.
        /// </summary>
        [UnityTest]
        public IEnumerator Every_edit_in_an_inspector_undoes([ValueSource(nameof(EditSubjects))] Subject subject)
        {
            var made = new List<Object>();
            var host = new Host(theme);
            var answers = new AnswerLinksYes();
            var sweep = new Sweep();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var target = subject.Make(made)[0];
                var root = host.Inspect(target);
                yield return Frames();
                yield return ExpandAll(root);
                yield return SweepEdits(root, target, sweep);
            }
            finally
            {
                answers.Dispose();
                host.Destroy();
                Destroy(made);
            }
            TestContext.Out.WriteLine($"[Smoke] {subject}: {sweep.Controls} controls, {sweep.Edits} edits");
            Assert.That(sweep.Broken, Is.Empty, $"edits one Undo doesn't take back, of {sweep.Edits} made");
            Assert.That(sweep.Edits, Is.GreaterThan(0), "the inspector shows values to edit");
        }

        // An asset whose inspector writes its one field straight into it, with no Undo, as PlayForge's inspectors did
        // before ED-12
        public sealed class DirectWriteAsset : ScriptableObject
        {
            public string Value = "before";
        }

        [CustomEditor(typeof(DirectWriteAsset))]
        public sealed class DirectWriteEditor : UnityEditor.Editor
        {
            public override VisualElement CreateInspectorGUI()
            {
                var asset = (DirectWriteAsset)target;
                var field = new TextField("Value") { value = asset.Value };
                field.RegisterValueChangedCallback(change => asset.Value = change.newValue);
                return field;
            }
        }

        /// <summary>The suite's own check: an edit that records no Undo is one the sweep reports.</summary>
        [UnityTest]
        public IEnumerator The_sweep_finds_an_edit_that_doesnt_undo()
        {
            var asset = ScriptableObject.CreateInstance<DirectWriteAsset>();
            var host = new Host(theme);
            var sweep = new Sweep();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(asset);
                yield return Frames();
                yield return SweepEdits(root, asset, sweep);
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(asset);
            }
            Assert.That(sweep.Edits, Is.EqualTo(1), "the field's edit is made");
            Assert.That(sweep.Broken, Has.Count.EqualTo(1), "and reported");
        }
    }
}
