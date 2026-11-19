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
        public static void CaptureAndQuit()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-docsOut");
            Begin(at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath("DocsScreenshots"), true);
        }

        private static void Begin(string folder, bool quit)
        {
            if (string.IsNullOrEmpty(folder)) return;
            output = folder;
            quitAfter = quit;
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
                debugger.Close();
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
                EditorApplication.ExitPlaymode();
                yield return 3f;
            }
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

        private static void Place(EditorWindow window, int width, int height)
        {
            window.position = new Rect(80, 80, width, height);
            window.Focus();
            window.Repaint();
        }

        // The window drawn into a texture by its view, as the editor draws a tab dragged out, and saved as a PNG; or only
        // the part of it an element covers, where one is given
        private static void Save(EditorWindow window, string name, UnityEngine.UIElements.VisualElement part = null)
        {
            if (window == null) return;
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
            if (part != null)
            {
                // The element's rect in the image, whose rows run bottom up: its world rect is the panel's, which starts
                // above the window's own root, at the tab the window hangs from
                var bound = part.worldBound;
                bound.position -= window.rootVisualElement.worldBound.position;
                int x = Mathf.Clamp(Mathf.RoundToInt(bound.x * scale), 0, width - 1);
                int w = Mathf.Clamp(Mathf.RoundToInt(bound.width * scale), 1, width - x);
                int h = Mathf.Clamp(Mathf.RoundToInt(bound.height * scale), 1, height);
                int y = Mathf.Clamp(height - Mathf.RoundToInt(bound.yMax * scale), 0, height - h);
                var cropped = new Texture2D(w, h, TextureFormat.RGB24, false);
                cropped.SetPixels(image.GetPixels(x, y, w, h));
                cropped.Apply();
                Object.DestroyImmediate(image);
                image = cropped;
            }
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
            Debug.Log($"[Docs] {name}.png");
        }
    }
}
