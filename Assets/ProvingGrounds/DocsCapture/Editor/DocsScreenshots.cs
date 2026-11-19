using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Editor;
using FarEmerald.PlayForge.Lab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.DocsCapture
{
    /// <summary>
    /// Captures PlayForge's editor tools for the documentation site: each window at a set size, drawn into a PNG by the
    /// editor itself, so nothing on the screen over it shows. Run it from Window → PlayForge → Docs → Capture Screenshots, or from
    /// the command line with -executeMethod FarEmerald.PlayForge.ProvingGrounds.DocsCapture.DocsScreenshots.CaptureAndQuit
    /// -docsOut "folder", in a project that has the Demo sample at Assets/PlayForgeDemo. Leave the editor in front while it
    /// runs: play mode can stop while the editor is in the background, and the Demo's, the Debugger's and the Creator
    /// Lab's shots need it running.
    /// </summary>
    public static class DocsScreenshots
    {
        private const string DemoScene = "Assets/PlayForgeDemo/Demo.unity";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static string output;
        private static bool quitAfter;
        private static bool closeUpsOnly;
        private static IEnumerator shots;
        private static double resumeAt;
        private static bool optionsKept;
        private static bool optionsEnabled;
        private static EnterPlayModeOptions options;
        // The preferences the run sets, each as the run found it, put back after: null for one that wasn't set
        private static readonly Dictionary<string, string> keptPrefs = new Dictionary<string, string>();

        [MenuItem("Window/PlayForge/Docs/Capture Screenshots")]
        public static void Capture() =>
            Begin(EditorUtility.SaveFolderPanel("Save the screenshots in", Application.dataPath, "editor"), false);

        /// <summary>The command line's entry: the screenshots go in -docsOut's folder, then the editor quits.</summary>
        public static void CaptureAndQuit() => Begin(OutputArg(), true);

        /// <summary>The command line's entry for the close-ups of the inspectors' parts alone, a minute's run.</summary>
        public static void CaptureCloseUpsAndQuit() => Begin(OutputArg(), true, true);

        private static string OutputArg()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-docsOut");
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath("DocsScreenshots");
        }

        private static void Begin(string folder, bool quit, bool onlyCloseUps = false)
        {
            if (string.IsNullOrEmpty(folder)) return;
            output = folder;
            quitAfter = quit;
            closeUpsOnly = onlyCloseUps;
            Directory.CreateDirectory(output);
            shots = Shots();
            resumeAt = 0;
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
        }

        // Runs the shots one step at a time, each step's yield the seconds to wait before the next
        private static void Step()
        {
            if (EditorApplication.timeSinceStartup < resumeAt) return;
            bool more;
            try
            {
                more = shots.MoveNext();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                more = false;
            }
            if (more)
            {
                resumeAt = EditorApplication.timeSinceStartup + (shots.Current is float seconds ? seconds : 0.5f);
                return;
            }
            EditorApplication.update -= Step;
            Restore();
            Debug.Log($"[Docs] Screenshots written to {output}");
            if (quitAfter) EditorApplication.Exit(0);
        }

        private static IEnumerator Shots()
        {
            yield return 3f;
            // The Demo's Forge Project, so The Forge lists its assets. Which project is active is a preference this Unity
            // project keeps, so the run keeps it as it found it, and puts it back after
            var demo = AssetDatabase.FindAssets("t:ForgeProject").Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/PlayForgeDemo/")).Select(AssetDatabase.LoadAssetAtPath<ForgeProject>)
                .FirstOrDefault(project => project != null);
            var projects = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeProjects");
            if (demo != null)
            {
                KeepPref(projects?.GetProperty("ActiveKey", Any)?.GetValue(null) as string);
                projects?.GetProperty("Active", Any)?.SetValue(null, demo);
            }
            // Each table as it first opens for anyone, every column shown at its own width, and The Forge's list wide
            // enough for the assets' names
            var widths = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeColumnWidths");
            foreach (var table in new[] { "Forge.Assets", "Forge.Tags", "Problems", "Debugger.Entities", "Debugger.Processes",
                         "Debugger.Tags", "Debugger.Timeline" })
            foreach (var keyOf in new[] { "KeyOf", "HiddenKeyOf" })
            {
                var key = widths?.GetMethod(keyOf, Any)?.Invoke(null, new object[] { table }) as string;
                KeepPref(key);
                if (key != null) EditorPrefs.DeleteKey(key);
            }
            var listKey = typeof(TheForge).GetProperty("ListWidthKey", Any)?.GetValue(null) as string;
            KeepPref(listKey);
            if (listKey != null) EditorPrefs.SetString(listKey, "800");
            yield return 1f;

            if (closeUpsOnly)
            {
                var closeUpEffect = Find<GameplayEffect>(asset => asset.name.Contains("Damage")) ?? Find<GameplayEffect>(_ => true);
                var closeUpAbility = Find<Ability>(asset => asset.name.Contains("Fireball")) ?? Find<Ability>(_ => true);
                foreach (var step in CloseUps(demo, closeUpEffect, closeUpAbility)) yield return step;
                yield break;
            }

            // The Forge: every asset beside an effect, its toolbar, an ability's summary and inspector, and the Tags view
            var forge = Floating<TheForge>(1600, 900);
            yield return 2f;
            Save(forge, "the-forge-toolbar", forge.rootVisualElement.Q("ForgeToolbar"));
            var refill = typeof(TheForge).GetMethod("Refill", Any, null, Type.EmptyTypes, null);
            // An effect near the top of the list, shown as Open in The Forge shows it, then every type listed again
            var shown = Find<GameplayEffect>(asset => asset.name.Contains("Arrow Damage")) ?? Find<GameplayEffect>(_ => true);
            if (shown != null)
            {
                TheForge.OpenToAsset(shown);
                typeof(TheForge).GetField("typeFilter", Any)?.SetValue(forge, "");
                refill?.Invoke(forge, null);
            }
            yield return 2f;
            Save(forge, "the-forge");
            var ability = Find<Ability>(asset => asset.name.Contains("Fireball")) ?? Find<Ability>(_ => true);
            if (ability != null) TheForge.OpenToAsset(ability);
            yield return 2f;
            Save(forge, "the-forge-ability");
            // The Tags view on a tag the Demo's spells share
            typeof(TheForge).GetField("tagsShown", Any)?.SetValue(forge, true);
            typeof(TheForge).GetField("selectedTag", Any)?.SetValue(forge, "Ability.Spell");
            refill?.Invoke(forge, null);
            yield return 2f;
            Save(forge, "the-forge-tags");
            typeof(TheForge).GetField("tagsShown", Any)?.SetValue(forge, false);
            forge.Close();

            // The windows The Forge opens: New Project, New Asset and Rename. Each is a utility window, which the editor
            // sizes once it shows, so each is placed again after
            var newProject = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeNewProjectWindow")
                ?.GetMethod("Open", Any)?.Invoke(null, null) as EditorWindow;
            if (newProject != null)
            {
                yield return 0.5f;
                Place(newProject, 520, 100);
                yield return 1.5f;
                Save(newProject, "new-forge-project-window");
                newProject.Close();
            }
            var newAsset = ForgeNewAssetWindow.Show(typeof(GameplayEffect), _ => { });
            yield return 0.5f;
            Place(newAsset, 520, 150);
            yield return 1.5f;
            Save(newAsset, "new-asset-window");
            newAsset.Close();
            if (ability != null)
            {
                var rename = ForgeRenameWindow.Show(ability);
                yield return 0.5f;
                Place(rename, 520, 100);
                yield return 1.5f;
                Save(rename, "rename-window");
                rename.Close();
            }

            // The Forge Project's own inspector
            Selection.activeObject = demo;
            var projectInspector = Floating(typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow"), 560, 900);
            yield return 2f;
            if (demo != null) Save(projectInspector, "forge-project-inspector");
            projectInspector.Close();

            // An effect in Unity's Inspector, as PlayForge's inspector draws it
            var effect = Find<GameplayEffect>(asset => asset.name.Contains("Damage")) ?? Find<GameplayEffect>(_ => true);
            Selection.activeObject = effect;
            var inspector = Floating(typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow"), 560, 980);
            yield return 2f;
            Save(inspector, "inspector-effect");
            inspector.Close();

            // The Problems view
            var problems = ForgeProblemsWindow.ShowWindow();
            Place(problems, 1100, 640);
            yield return 2f;
            Save(problems, "problems");
            typeof(ForgeProblemsWindow).GetMethod("ShowView", Any, null, new[] { typeof(bool) }, null)
                ?.Invoke(problems, new object[] { true });
            yield return 1.5f;
            Save(problems, "problems-view-by-rule");
            problems.Close();

            // The two settings pages
            var settings = SettingsService.OpenProjectSettings("Project/PlayForge");
            Place(settings, 1100, 900);
            yield return 2f;
            Save(settings, "project-settings");
            settings.Close();
            var preferences = SettingsService.OpenUserPreferences("Preferences/PlayForge");
            Place(preferences, 1100, 760);
            yield return 2f;
            Save(preferences, "preferences");
            preferences.Close();

            // The Demo in play, and the Debugger beside it: play mode entered without a domain reload, so this run goes on
            if (File.Exists(DemoScene))
            {
                EditorSceneManager.OpenScene(DemoScene);
                optionsKept = true;
                optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
                options = EditorSettings.enterPlayModeOptions;
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                var game = Floating(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"), 1280, 760);
                EditorApplication.EnterPlaymode();
                yield return 8f;
                Save(game, "demo");
                // The store's, a few seconds on, while the fight is under way: 1280 by 720, and 1300 by 867 for the
                // cover's 1950 by 1300
                Place(game, 1280, 720);
                yield return 3f;
                Save(game, "store/demo");
                Place(game, 1300, 866.6667f);
                yield return 1.5f;
                Save(game, "store/cover-base");
                // The Debugger, then an entity selected in the scene, which it follows once it's open, so it shows the
                // entity's attributes and effects
                var debugger = Floating<RuntimeDebugger>(1360, 800);
                yield return 2f;
                var hero = Object.FindObjectsByType<GameplayAbilitySystem>(FindObjectsSortMode.None)
                    .FirstOrDefault(each => each.name.Contains("Ranger"));
                if (hero != null)
                {
                    Selection.activeGameObject = null;
                    yield return 0.5f;
                    Selection.activeGameObject = hero.gameObject;
                }
                yield return 3f;
                Save(debugger, "debugger");
                Save(debugger, "debugger-entities");
                // Its other views, each chosen by its toolbar's toggle, as a click chooses it, with a row selected as a
                // click selects one: the Ranger's process, the first tag an entity holds, and the first track
                foreach (var (name, list) in new[] { ("Processes", "processList"), ("Tags", "tagTree"), ("Timeline", "trackList") })
                {
                    var toggle = debugger.rootVisualElement.Q<ToolbarToggle>("Show" + name);
                    if (toggle == null) break;
                    toggle.value = true;
                    yield return 1.5f;
                    var view = typeof(RuntimeDebugger).GetField(list, Any)?.GetValue(debugger) as BaseVerticalCollectionView;
                    if (view is BaseTreeView tree)
                    {
                        tree.ExpandAll();
                        yield return 0.5f;
                        var rows = tree.viewController;
                        for (int row = 0; rows != null && row < rows.GetItemsCount(); row++)
                        {
                            if (rows.HasChildrenByIndex(row)) continue;
                            tree.SetSelection(row);
                            break;
                        }
                    }
                    else if (view != null && view.itemsSource != null && view.itemsSource.Count > 0)
                    {
                        view.SetSelection(name == "Processes" ? Math.Min(2, view.itemsSource.Count - 1) : 0);
                    }
                    yield return 1.5f;
                    Save(debugger, "debugger-" + name.ToLowerInvariant());
                }
                // The store's: the Timeline, then the Entities view, at 1280 by 720
                Place(debugger, 1280, 720);
                yield return 1.5f;
                Save(debugger, "store/timeline");
                var entities = debugger.rootVisualElement.Q<ToolbarToggle>("ShowEntities");
                if (entities != null) entities.value = true;
                yield return 1.5f;
                Save(debugger, "store/debugger");
                debugger.Close();

                // The hero's hub in the Inspector, as the scene runs it
                if (hero != null)
                {
                    Selection.activeGameObject = hero.gameObject;
                    var hub = Floating(InspectorType, 640, 1000);
                    yield return 2f;
                    Save(hub, "entity-hub-inspector");
                    hub.Close();
                    Selection.activeGameObject = null;
                }
                EditorApplication.ExitPlaymode();
                yield return 3f;
            }

            // The Creator Lab: its menu opens its scene and plays; then a battle of three of the Demo's heroes a side,
            // fighting, with a unit selected, so it shows the units and a unit's panel. Seated as a quick battle seats
            // them, but each named, as a quick battle picks at random from every entity, the Game Root and dummies too
            if (EditorApplication.ExecuteMenuItem("Window/PlayForge/Creator Lab"))
            {
                yield return 10f;
                var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                Place(game, 1280, 760);
                var lab = Object.FindFirstObjectByType<CreatorLab>();
                if (lab != null)
                {
                    lab.SetMode(LabMode.Battle);
                    lab.Clear();
                    var heroes = new[] { "Vanguard", "Ranger", "Oracle", "Shade", "Pyromancer", "Vanguard" };
                    for (int seat = 0; seat < heroes.Length; seat++)
                    {
                        var hero = Find<EntityIdentity>(asset => asset.name == heroes[seat]);
                        if (hero != null) lab.AddBot(hero, seat < 3 ? LabSides.Blue : LabSides.Red);
                    }
                    lab.Announce();
                    lab.Play();
                    yield return 4f;
                    if (lab.Units.Count > 0) lab.Select(lab.Units[0]);
                }
                yield return 2f;
                Save(game, "creator-lab");
                Place(game, 1280, 720);
                yield return 2f;
                Save(game, "store/lab");
                EditorApplication.ExitPlaymode();
                yield return 3f;
            }

            // Last, as a failure in them ends the run: the inspectors' parts, each a close-up of one; the windows a number
            // field's menu, an inspector's Import and The Forge's Spreadsheets menu open; each asset guide's sections, as
            // a Demo asset's inspector draws them, named for their titles, as the guide's headings are; and the Asset
            // Store's shots of The Forge and the Problems view, 1280 by 720, which a 150% screen makes 1920 by 1080
            foreach (var step in CloseUps(demo, effect, ability)) yield return step;
            foreach (var step in ToolWindows()) yield return step;
            foreach (var (asset, prefix) in GuideAssets())
                foreach (var step in SectionShots(asset, prefix)) yield return step;
            foreach (var step in StoreForge()) yield return step;
        }

        // Close-ups of PlayForge's inspectors, each part cropped from a Demo asset's inspector, scrolled to show it
        private static IEnumerable CloseUps(ForgeProject demo, GameplayEffect effect, Ability ability)
        {
            var inspectorType = typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");

            // An effect made from a template: its header, a number field, a tag field and a field it overrides
            var templated = Find<GameplayEffect>(asset => asset.name == "Fireball Damage") ?? effect;
            if (templated != null)
            {
                Selection.activeObject = templated;
                var inspector = Floating(inspectorType, 640, 1000);
                yield return 2.5f;
                var root = inspector.rootVisualElement;
                var header = root.Q(className: "pf-asset-header");
                if (header != null) Save(inspector, "inspector-header", header);
                // Every section open, so each field has its size; then the first field of each kind drawn, and the
                // smallest override
                var expanded = SectionExpanded;
                foreach (var section in root.Query(className: "pf-section").ToList()) expanded?.SetValue(section, true);
                yield return 1f;
                // The smallest number field, a single row; a tag field showing a tag with a path; and the top of the
                // smallest override, enough to show its mark
                var number = Drawn(root, "pf-magnitude").Where(each => each.worldBound.height >= 16).OrderBy(each => each.worldBound.height).FirstOrDefault();
                foreach (var step in Scrolled(inspector, "number-field", number)) yield return step;
                var tags = Drawn(root, "pf-tag-field").ToList();
                var tag = tags.FirstOrDefault(each => each.Query<TextElement>().ToList().Any(text => (text.text ?? "").Contains('.'))) ?? tags.FirstOrDefault();
                foreach (var step in Scrolled(inspector, "tag-field", tag)) yield return step;
                var overridden = Drawn(root, "pf-override").Where(each => each.worldBound.height >= 20).OrderBy(each => each.worldBound.height).FirstOrDefault();
                foreach (var step in Scrolled(inspector, "template-overrides", overridden, 220f)) yield return step;
                inspector.Close();
            }

            // An ability: an effect field and its levels line, then every section closed, to show each one's summary.
            // Closing a section from code leaves the open state the inspector keeps for a user's clicks as it was
            if (ability != null)
            {
                Selection.activeObject = ability;
                var inspector = Floating(inspectorType, 640, 1000);
                yield return 2.5f;
                var root = inspector.rootVisualElement;
                foreach (var step in Scrolled(inspector, "effect-field-levels", root.Q(className: "pf-link-line")?.parent)) yield return step;
                var expanded = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeSection")?.GetProperty("Expanded", Any);
                var sections = root.Query(className: "pf-section").ToList();
                foreach (var section in sections) expanded?.SetValue(section, false);
                var scroll = sections.FirstOrDefault()?.GetFirstAncestorOfType<ScrollView>();
                if (scroll != null) scroll.scrollOffset = Vector2.zero;
                yield return 1.5f;
                var header = root.Q(className: "pf-asset-header");
                if (header != null && sections.Count > 0) Save(inspector, "inspector-sections", Around(inspector, header, sections.Last()));
                inspector.Close();
            }

            // A Scaler asset's Try section
            var scaler = AssetDatabase.FindAssets("t:ScalerAsset", new[] { "Assets/PlayForgeDemo" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ScalerAsset>).FirstOrDefault(asset => asset != null);
            if (scaler != null)
            {
                Selection.activeObject = scaler;
                var inspector = Floating(inspectorType, 640, 1000);
                yield return 2.5f;
                foreach (var step in Scrolled(inspector, "try-section", inspector.rootVisualElement.Q(className: "pf-try"))) yield return step;
                inspector.Close();
            }

            // An asset field with its button: the Forge Project's, which holds several
            if (demo != null)
            {
                Selection.activeObject = demo;
                var inspector = Floating(inspectorType, 640, 1000);
                yield return 2.5f;
                var button = inspector.rootVisualElement.Query<Button>().ToList()
                    .FirstOrDefault(each => (each.text == "New" || each.text == "Open") && each.parent?.Q<ObjectField>() != null);
                foreach (var step in Scrolled(inspector, "asset-field", button?.parent)) yield return step;
                inspector.Close();
            }

            // A process's inspector: a Demo prefab whose root has a MonoBehaviour process
            var process = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PlayForgeDemo" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .FirstOrDefault(each => each != null && each.GetComponent<AbstractMonoProcess>() != null);
            if (process != null)
            {
                Selection.activeObject = process;
                var inspector = Floating(inspectorType, 640, 900);
                yield return 2.5f;
                Save(inspector, "process-inspector");
                inspector.Close();
            }
            Selection.activeObject = null;
        }

        // The windows a number field's menu, an inspector's Import and The Forge's Spreadsheets menu open, each shown as
        // its menu shows it, and closed with nothing applied
        private static IEnumerable ToolWindows()
        {
            var effects = AssetDatabase.FindAssets("t:GameplayEffect", new[] { "Assets/PlayForgeDemo" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameplayEffect>).Where(each => each != null).OrderBy(each => each.name).ToList();

            // A scaler a number field holds as a table of its own: the property the field's menu hands these windows
            SerializedProperty table = null;
            GameplayEffect owner = null;
            foreach (var each in effects)
            {
                table = TableScalerOf(new SerializedObject(each));
                if (table == null) continue;
                owner = each;
                break;
            }
            if (table != null)
            {
                var name = owner.name;
                foreach (var step in Shown(Guarded(() => ScalerValuesWindow.Show(table, name)), "import-values-window", 560, 420)) yield return step;
                var quickFill = Guarded(() =>
                {
                    QuickFillWizard.Show(table, () => { });
                    return Resources.FindObjectsOfTypeAll<QuickFillWizard>().FirstOrDefault();
                });
                foreach (var step in Shown(quickFill, "quick-fill-window", 520, 380)) yield return step;
                var operation = table.serializedObject.FindProperty(ParentPath(table.propertyPath));
                var save = operation == null ? null : Guarded(() => SaveScalerAssetWindow.Show(operation, name));
                foreach (var step in Shown(save, "save-as-scaler-asset-window", 520, 200)) yield return step;
            }

            // Import, as an effect inspector's Import opens it
            var into = effects.FirstOrDefault(each => each.name.Contains("Damage")) ?? effects.FirstOrDefault();
            if (into != null)
            {
                foreach (var step in Shown(Guarded(() => ForgeImportWindow.ShowForAsset(new SerializedObject(into))), "import-window", 720, 560))
                    yield return step;
            }

            // A sheet's changes, as The Forge's Spreadsheets → Import lists them: the Demo's effects' numbers, one changed
            var sheet = Guarded(() =>
            {
                var csv = ChangeOne(ForgeSpreadsheets.NumbersOf(effects.Cast<Object>()));
                return ForgeSpreadsheetWindow.Show(ForgeSpreadsheets.Read(csv), "Demo Effects.csv");
            });
            foreach (var step in Shown(sheet, "spreadsheet-import-window", 900, 560)) yield return step;
        }

        private static Type InspectorType => typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");

        // A section's Expanded, which opens or closes it as drawn, leaving the open state its inspector keeps alone
        private static PropertyInfo SectionExpanded =>
            typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeSection")?.GetProperty("Expanded", Any);

        // The Demo asset each asset guide shows, and the prefix its images take
        private static IEnumerable<(Object asset, string prefix)> GuideAssets()
        {
            var picks = new (Func<Object> find, string prefix)[]
            {
                (() => Find<Ability>(each => each.name == "Fireball"), "ability"),
                (() => Find<GameplayEffect>(each => each.name == "Burning"), "effect"),
                (() => Find<FarEmerald.PlayForge.Attribute>(each => each.name == "Health"), "attribute"),
                (() => Find<AttributeSet>(each => each.name == "Ranger Attributes"), "attribute-set"),
                (() => Find<Item>(each => each.name == "Arcane Tome"), "item"),
                (() => Find<EntityIdentity>(each => each.name == "Ranger"), "entity"),
                (() => Find<ValuePipeline>(each => each.name == "Physical Damage"), "value-pipeline"),
                (() => Find<ScalerAsset>(_ => true), "scaler"),
            };
            foreach (var (find, prefix) in picks)
            {
                var asset = Guarded(find);
                if (asset != null) yield return (asset, prefix);
            }
        }

        // An asset's inspector: its top, every section open, then a close-up of each of its own sections, and of its Try
        private static IEnumerable SectionShots(Object asset, string prefix)
        {
            Selection.activeObject = asset;
            var inspector = Floating(InspectorType, 640, 1000);
            yield return 2.5f;
            var root = inspector.rootVisualElement;
            var expanded = SectionExpanded;
            var sections = root.Query(className: "pf-section").ToList().Where(each => !InSection(each)).ToList();
            foreach (var section in sections) expanded?.SetValue(section, true);
            yield return 1f;
            Save(inspector, prefix + "-inspector");
            foreach (var section in sections)
            {
                var title = (section.Q(className: "pf-section__title") as Label)?.text;
                if (string.IsNullOrEmpty(title)) continue;
                foreach (var step in Scrolled(inspector, prefix + "-" + Slug(title), section)) yield return step;
            }
            var tryIt = root.Q(className: "pf-try");
            if (tryIt != null)
                foreach (var step in Scrolled(inspector, prefix + "-try", tryIt)) yield return step;
            inspector.Close();
            Selection.activeObject = null;
        }

        private static bool InSection(VisualElement element)
        {
            for (var at = element.parent; at != null; at = at.parent)
                if (at.ClassListContains("pf-section")) return true;
            return false;
        }

        private static string Slug(string text) =>
            new string(text.ToLowerInvariant().Select(each => char.IsLetterOrDigit(each) ? each : '-').ToArray()).Trim('-');

        // The store's shots of The Forge: every asset beside an ability, then an ability, an effect and a value pipeline
        // each with the sections that show it best open; and the Problems view on a copy of an effect broken on purpose
        private static IEnumerable StoreForge()
        {
            // The list narrower, leaving the asset room, and its Type and Asset Tag hidden, leaving the names room; the run
            // puts both back after
            var listKey = typeof(TheForge).GetProperty("ListWidthKey", Any)?.GetValue(null) as string;
            if (listKey != null) EditorPrefs.SetString(listKey, "500");
            var hiddenKey = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeColumnWidths")
                ?.GetMethod("HiddenKeyOf", Any)?.Invoke(null, new object[] { "Forge.Assets" }) as string;
            if (hiddenKey != null) EditorPrefs.SetString(hiddenKey, "AssetTag;Type");
            var forge = Floating<TheForge>(1280, 720);
            yield return 2f;
            var refill = typeof(TheForge).GetMethod("Refill", Any, null, Type.EmptyTypes, null);
            var fireball = Find<Ability>(each => each.name == "Fireball");
            if (fireball != null)
            {
                TheForge.OpenToAsset(fireball);
                typeof(TheForge).GetField("typeFilter", Any)?.SetValue(forge, "");
                refill?.Invoke(forge, null);
                yield return 2f;
                Save(forge, "store/forge");
                TheForge.OpenToAsset(fireball);
                yield return 2f;
                OpenOnly(forge, "Stages", "Components");
                yield return 1.5f;
                Save(forge, "store/ability");
            }
            var burning = Find<GameplayEffect>(each => each.name == "Burning");
            if (burning != null)
            {
                TheForge.OpenToAsset(burning);
                yield return 2f;
                OpenOnly(forge, "Duration", "Impact", "Cues", "Components");
                yield return 1.5f;
                Save(forge, "store/effect");
            }
            var pipeline = Find<ValuePipeline>(each => each.name == "Physical Damage");
            if (pipeline != null)
            {
                TheForge.OpenToAsset(pipeline);
                yield return 2f;
                Save(forge, "store/pipeline");
            }
            forge.Close();

            var broken = BreakACopy();
            if (broken == null) yield break;
            yield return 3f;
            var problems = ForgeProblemsWindow.ShowWindow();
            Place(problems, 1280, 720);
            yield return 2f;
            typeof(ForgeProblemsWindow).GetMethod("ShowView", Any, null, new[] { typeof(bool) }, null)
                ?.Invoke(problems, new object[] { true });
            yield return 1.5f;
            Save(problems, "store/problems");
            problems.Close();
            AssetDatabase.DeleteAsset(broken);
            yield return 1f;
        }

        // Every section of a window's inspector closed but those named
        private static void OpenOnly(EditorWindow window, params string[] titles)
        {
            var expanded = SectionExpanded;
            foreach (var section in window.rootVisualElement.Query(className: "pf-section").ToList())
            {
                var title = (section.Q(className: "pf-section__title") as Label)?.text;
                expanded?.SetValue(section, titles.Contains(title));
            }
        }

        // A copy of a Demo effect whose levels come from an ability, that ability cleared, so the Problems view flags it
        // and offers its fix; its path, for the caller to delete, or null
        private static string BreakACopy()
        {
            try
            {
                var effects = AssetDatabase.FindAssets("t:GameplayEffect", new[] { "Assets/PlayForgeDemo" })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path);
                foreach (var from in effects)
                {
                    var effect = AssetDatabase.LoadAssetAtPath<GameplayEffect>(from);
                    if (effect == null) continue;
                    var provider = AbilityReferenceOf(new SerializedObject(effect));
                    if (provider == null) continue;
                    var to = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(from).Replace('\\', '/') + "/Frost Bolt Damage.asset");
                    if (!AssetDatabase.CopyAsset(from, to)) return null;
                    var copy = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameplayEffect>(to));
                    copy.FindProperty(provider).objectReferenceValue = null;
                    // A name of its own, which its copied asset tag doesn't follow, for a second problem with its fix
                    var named = NamedField(copy);
                    if (named != null) named.stringValue = "Frost Bolt Damage";
                    copy.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                    return to;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return null;
        }

        // An asset's own name, the first text field called Name
        private static SerializedProperty NamedField(SerializedObject asset)
        {
            var each = asset.GetIterator();
            while (each.Next(true))
            {
                if (each.propertyType == SerializedPropertyType.String && each.name == "Name") return each.Copy();
            }
            return null;
        }

        // The path of the first field an asset holds an ability in, as an effect's level provider is
        private static string AbilityReferenceOf(SerializedObject asset)
        {
            var each = asset.GetIterator();
            while (each.Next(true))
            {
                if (each.propertyType == SerializedPropertyType.ObjectReference && each.objectReferenceValue is Ability)
                    return each.propertyPath;
            }
            return null;
        }

        // An element's close-up, its scroll view scrolled to show it first; no taller than tallest, where given
        private static IEnumerable Scrolled(EditorWindow window, string name, VisualElement part, float tallest = 0f)
        {
            if (part == null)
            {
                Debug.LogWarning($"[Docs] {name}: not found");
                yield break;
            }
            var scroll = part.GetFirstAncestorOfType<ScrollView>();
            scroll?.ScrollTo(part);
            yield return 1f;
            // An inspector's header stays at the top as its content scrolls: the element below it, not under it
            var header = window.rootVisualElement.Q(className: "pf-asset-header");
            if (scroll != null && header != null && header != part && part.worldBound.yMin < header.worldBound.yMax + 4)
            {
                scroll.scrollOffset -= new Vector2(0, header.worldBound.yMax + 8 - part.worldBound.yMin);
                yield return 1f;
            }
            if (tallest <= 0f || part.worldBound.height <= tallest)
            {
                Save(window, name, part, 6f);
                yield break;
            }
            var bound = part.worldBound;
            bound.position -= window.rootVisualElement.worldBound.position;
            Save(window, name, new Rect(bound.x, bound.y, bound.width, tallest), 6f);
        }

        // The elements of a class that are drawn, with a size of their own, in the window's order
        private static IEnumerable<VisualElement> Drawn(VisualElement root, string className) =>
            root.Query(className: className).ToList().Where(each =>
                each.resolvedStyle.display != DisplayStyle.None && each.worldBound.width > 60 && each.worldBound.height > 12);

        // A window a menu item opens, placed again once the editor has sized it, saved and closed
        private static IEnumerable Shown(EditorWindow window, string name, int width, int height)
        {
            if (window == null)
            {
                Debug.LogWarning($"[Docs] {name}: not shown");
                yield break;
            }
            yield return 0.5f;
            Place(window, width, height);
            yield return 1.5f;
            Save(window, name);
            window.Close();
        }

        // What a menu item's call returns, or null where it throws, so one window's failure leaves the run going
        private static T Guarded<T>(Func<T> make) where T : class
        {
            try
            {
                return make();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        // The first scaler a number field holds as a table of its own, in the order the inspector draws them
        private static SerializedProperty TableScalerOf(SerializedObject asset)
        {
            var each = asset.GetIterator();
            while (each.Next(true))
            {
                if (each.propertyType == SerializedPropertyType.ManagedReference &&
                    (each.managedReferenceFullTypename ?? "").EndsWith("SimpleScaler")) return each.Copy();
            }
            return null;
        }

        private static string ParentPath(string path)
        {
            int at = path.LastIndexOf('.');
            return at > 0 ? path.Substring(0, at) : path;
        }

        // The sheet with the first number after its header row raised by one, so its import has a change to list
        private static string ChangeOne(string csv)
        {
            var lines = csv.Split('\n');
            for (int row = 1; row < lines.Length; row++)
            {
                var cells = lines[row].Split(',');
                for (int cell = cells.Length - 1; cell > 0; cell--)
                {
                    var text = cells[cell].TrimEnd('\r');
                    if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) continue;
                    cells[cell] = (value + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + cells[cell].Substring(text.Length);
                    lines[row] = string.Join(",", cells);
                    return string.Join("\n", lines);
                }
            }
            return csv;
        }

        // The rect from one element's top to another's bottom, in the window's root's space
        private static Rect Around(EditorWindow window, VisualElement first, VisualElement last)
        {
            var origin = window.rootVisualElement.worldBound.position;
            var top = first.worldBound;
            var bottom = last.worldBound;
            float xMin = Mathf.Min(top.xMin, bottom.xMin), xMax = Mathf.Max(top.xMax, bottom.xMax);
            return new Rect(xMin - origin.x, top.yMin - origin.y, xMax - xMin, bottom.yMax - top.yMin);
        }

        // Puts back what the run changed: the play mode options, and the active project, unset again if it was
        private static void Restore()
        {
            if (optionsKept)
            {
                EditorSettings.enterPlayModeOptionsEnabled = optionsEnabled;
                EditorSettings.enterPlayModeOptions = options;
                optionsKept = false;
            }
            foreach (var kept in keptPrefs)
            {
                if (kept.Value == null) EditorPrefs.DeleteKey(kept.Key);
                else EditorPrefs.SetString(kept.Key, kept.Value);
            }
            keptPrefs.Clear();
        }

        // Keeps a preference as the run first finds it, before the run sets it
        private static void KeepPref(string key)
        {
            if (string.IsNullOrEmpty(key) || keptPrefs.ContainsKey(key)) return;
            keptPrefs[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
        }

        private static T Find<T>(Func<T, bool> which) where T : Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/PlayForgeDemo/"))
                .Select(AssetDatabase.LoadAssetAtPath<T>).Where(asset => asset != null).FirstOrDefault(which);

        private static T Floating<T>(int width, int height) where T : EditorWindow =>
            (T)Floating(typeof(T), width, height);

        // A window of its own, floating, so its size is its own
        private static EditorWindow Floating(Type type, int width, int height)
        {
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            window.Show();
            Place(window, width, height);
            return window;
        }

        private static void Place(EditorWindow window, float width, float height)
        {
            window.position = new Rect(80, 80, width, height);
            window.Focus();
            window.Repaint();
        }

        // The part of a window an element covers, with a margin round it, saved as a PNG; or the whole window, where no
        // element is given
        private static void Save(EditorWindow window, string name, VisualElement part = null, float margin = 0f)
        {
            if (window == null) return;
            if (part == null)
            {
                SaveArea(window, name, null);
                return;
            }
            // The element's world rect is the panel's, which starts above the window's own root, at the tab the window
            // hangs from
            var bound = part.worldBound;
            bound.position -= window.rootVisualElement.worldBound.position;
            Save(window, name, bound, margin);
        }

        // An area of a window, in its root's space, with a margin round it, saved as a PNG
        private static void Save(EditorWindow window, string name, Rect area, float margin = 6f) =>
            SaveArea(window, name, new Rect(area.x - margin, area.y - margin, area.width + 2 * margin, area.height + 2 * margin));

        // The window drawn into a texture by its view, as the editor draws a tab dragged out, and saved as a PNG; or only
        // an area of it, as much of the area as the window shows
        private static void SaveArea(EditorWindow window, string name, Rect? area)
        {
            var view = typeof(EditorWindow).GetField("m_Parent", Any)?.GetValue(window);
            var grab = view?.GetType().GetMethod("GrabPixels", Any, null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            if (grab == null)
            {
                Debug.LogError($"[Docs] {name}: no view to grab");
                return;
            }
            // The view's rect in pixels, the screen's scale applied
            var scale = EditorGUIUtility.pixelsPerPoint;
            var size = window.position.size;
            var width = Mathf.RoundToInt(size.x * scale);
            var height = Mathf.RoundToInt(size.y * scale);
            var target = new RenderTexture(width, height, 24);
            grab.Invoke(view, new object[] { target, new Rect(0, 0, width, height) });
            var was = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = was;
            // The view draws top row first, so its pixels come out upside down
            var pixels = image.GetPixels32();
            var flipped = new Color32[pixels.Length];
            for (int row = 0; row < height; row++)
                Array.Copy(pixels, row * width, flipped, (height - 1 - row) * width, width);
            image.SetPixels32(flipped);
            image.Apply();
            if (area is Rect bound)
            {
                // The area's edges in pixels, kept within the window; the image's rows run bottom up
                int left = Mathf.Clamp(Mathf.RoundToInt(bound.xMin * scale), 0, width - 1);
                int right = Mathf.Clamp(Mathf.RoundToInt(bound.xMax * scale), left + 1, width);
                int top = Mathf.Clamp(Mathf.RoundToInt(bound.yMin * scale), 0, height - 1);
                int bottom = Mathf.Clamp(Mathf.RoundToInt(bound.yMax * scale), top + 1, height);
                int w = right - left, h = bottom - top;
                var cropped = new Texture2D(w, h, TextureFormat.RGB24, false);
                cropped.SetPixels(image.GetPixels(left, height - bottom, w, h));
                cropped.Apply();
                Object.DestroyImmediate(image);
                image = cropped;
            }
            var file = Path.Combine(output, name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
            Debug.Log($"[Docs] {name}.png");
        }
    }
}
