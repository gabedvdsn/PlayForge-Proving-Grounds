"""
The buyer-side smoke: in the export's check project, the fresh project that imported PlayForge-<version>.unitypackage,
PlayForge's windows open from their menus and each sample scene plays a while, with no error or exception logged.

export-unitypackage.py --tests makes that project, the package under Assets/Plugins/PlayForge as a buyer's import puts
it, with the Test Framework. This writes two test assemblies into it for the run and takes them out after, so it stays a
buyer's import:
- EditMode, PlayForgeSmoke.Edit: The Forge, the Debugger and Problems each open from Window/PlayForge, in a project
  with no Forge Project yet, draw for 30 frames and close;
- PlayMode, PlayForgeSmoke.Play: each scene under the samples loads in play mode and runs 10 seconds, PlayForge taken
  down after as Proving Grounds' sample tests take it down, so each scene starts as a press of Play starts it.
The Test Framework fails a test on any error or exception logged. Unity runs with graphics, as a buyer's editor does.

With --edition core, it smokes PlayForge Core's check project, which export-unitypackage.py --edition core --tests
makes: the Debugger opens from its menu, and in play mode a Process Bootstrapper brings ProcessControl up, with no
ability system in the package, and a sequence runs on it to its end.

Usage: python smoke-check.py [--out <dir>] [--edition core]
  --out      the export's folder, as export-unitypackage.py's --out (default: %TEMP%\\claude\\export)
  --edition  core for PlayForge Core's check project; the full package's otherwise
"""
import io
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.0.33f1\Editor\Unity.exe"

EDIT_ASMDEF = r'''{
    "name": "PlayForgeSmoke.Edit",
    "rootNamespace": "PlayForgeSmoke",
    "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
'''

PLAY_ASMDEF = r'''{
    "name": "PlayForgeSmoke.Play",
    "rootNamespace": "PlayForgeSmoke",
    "references": ["FarEmerald.PlayForge", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
'''

WINDOWS = r'''using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PlayForgeSmoke
{
    // PlayForge's windows, each opened from its menu item as a buyer opens it: it opens, draws and closes, nothing logged
    public class WindowsSmoke
    {
        [UnityTest, Timeout(120000)]
        public IEnumerator Each_window_opens_from_its_menu([Values(TITLES)] string title)
        {
            var before = Resources.FindObjectsOfTypeAll<EditorWindow>().ToList();
            Assert.That(EditorApplication.ExecuteMenuItem("Window/PlayForge/" + title), Is.True, "its menu item ran");
            for (int i = 0; i < 30; i++)
            {
                foreach (var each in Resources.FindObjectsOfTypeAll<EditorWindow>()) each.Repaint();
                yield return null;
            }
            var opened = Resources.FindObjectsOfTypeAll<EditorWindow>().Where(window => !before.Contains(window)).ToList();
            Assert.That(opened.Any(window => window.titleContent.text.Contains(title)), Is.True,
                $"a window titled {title} opened; opened: {string.Join(", ", opened.Select(window => window.titleContent.text))}");
            int elements = opened.Sum(window => window.rootVisualElement.Query<VisualElement>().ToList().Count);
            Debug.Log($"SMOKE opened {title}: {opened.Count} window(s), {elements} elements");
            foreach (var window in opened) window.Close();
            yield return null;
        }
    }
}
'''

SCENES = r'''using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PlayForgeSmoke
{
    // Each sample scene, as the package ships it, loads in play mode and runs a while, nothing logged
    public class SampleScenesSmoke
    {
        private const string Root = "Assets/Plugins/PlayForge/Samples";
        private const float Seconds = 10f;

        // The panel settings the scene's UI documents draw on, disposed after as Proving Grounds' sample tests do
        private readonly List<PanelSettings> panels = new();
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly PropertyInfo PanelOf = typeof(PanelSettings).GetProperty("panel", Members);
        private static readonly MethodInfo DisposePanel = typeof(PanelSettings).GetMethod("DisposePanel", Members, null, System.Type.EmptyTypes, null);

        public static IEnumerable<string> Scenes() =>
            AssetDatabase.FindAssets("t:Scene", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path);

        [Test]
        public void The_samples_have_their_scenes()
        {
            Assert.That(Scenes().Count(), Is.GreaterThanOrEqualTo(4), string.Join(", ", Scenes()));
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Each_sample_scene_plays([ValueSource(nameof(Scenes))] string scene)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(scene), "the scene loaded");
            panels.Clear();
            panels.AddRange(Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(document => document.panelSettings).Where(settings => settings != null).Distinct());
            int frames = 0;
            float until = Time.realtimeSinceStartup + Seconds;
            while (Time.realtimeSinceStartup < until)
            {
                frames++;
                yield return null;
            }
            Assert.That(frames, Is.GreaterThan(30), "frames ran");
            Assert.That(ProcessControl.Instance, Is.Not.Null, "PlayForge came up");
            Debug.Log($"SMOKE played {scene}: {frames} frames in {Seconds} seconds");
        }

        [UnityTearDown]
        public IEnumerator TakePlayForgeDown()
        {
            Time.timeScale = 1f;
            if (ProcessControl.Instance) ProcessControl.Instance.TerminateAllImmediately();
            for (int i = 0; i < 3; i++) yield return null;
            var empty = SceneManager.CreateScene($"After {Time.frameCount}");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (loaded != empty && loaded.isLoaded) yield return SceneManager.UnloadSceneAsync(loaded);
            }
            if (GameRoot.Instance) Object.Destroy(GameRoot.Instance.gameObject);
            if (ProcessControl.Instance) Object.Destroy(ProcessControl.Instance.gameObject);
            yield return null;
            ProcessControl.Instance = null;
            GameRoot.Instance = null;
            TagRegistry.Reset();
            if (PanelOf != null && DisposePanel != null)
                foreach (var settings in panels)
                {
                    if (settings == null) continue;
                    PanelOf.GetValue(settings);
                    DisposePanel.Invoke(settings, null);
                }
            panels.Clear();
        }
    }
}
'''

CORE = r'''using System.Collections;
using FarEmerald.PlayForge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayForgeSmoke
{
    // PlayForge Core as a buyer's scene has it: a Process Bootstrapper brings ProcessControl up, with no ability system
    // in the package, and a sequence runs on it to its end, nothing logged
    public class CoreSmoke
    {
        [UnityTest, Timeout(60000)]
        public IEnumerator A_sequence_runs_on_ProcessControl_alone()
        {
            Assert.That(typeof(ProcessControl).Assembly.GetType("FarEmerald.PlayForge.GameRoot"), Is.Null,
                "the core edition, with no ability system");
            var host = new GameObject("Process Bootstrapper");
            host.SetActive(false);
            host.AddComponent<ProcessBootstrapper>();
            host.SetActive(true);
            Assert.That(ProcessControl.Instance, Is.Not.Null, "ProcessControl came up");

            int ran = 0;
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Smoke").Delay(0.25f).Do(_ => ran++).Build());
            Assert.That((bool)ProcessControl.Register(sequence).Run(), Is.True, "a sequence registers");
            int frames = 0;
            float until = Time.realtimeSinceStartup + 5f;
            while (ran == 0 && Time.realtimeSinceStartup < until)
            {
                frames++;
                yield return null;
            }
            Assert.That(ran, Is.EqualTo(1), "and runs to its end");
            Debug.Log($"SMOKE core: ProcessControl up alone, and a sequence ran to its end in {frames} frames");
        }

        [UnityTearDown]
        public IEnumerator TakePlayForgeDown()
        {
            if (ProcessControl.Instance) ProcessControl.Instance.TerminateAllImmediately();
            yield return null;
            if (ProcessControl.Instance) Object.Destroy(ProcessControl.Instance.gameObject);
            yield return null;
            ProcessControl.Instance = null;
            TagRegistry.Reset();
        }
    }
}
'''

args = sys.argv[1:]
out = os.path.join(os.environ.get("TEMP", r"C:\Temp"), "claude", "export")
if "--out" in args:
    out = args[args.index("--out") + 1]
core = "--edition" in args and args[args.index("--edition") + 1] == "core"
suffix = "-core" if core else ""
check = os.path.join(out, "check" + suffix)
logs = os.path.join(out, "logs" + suffix)
if not os.path.isdir(os.path.join(check, "Assets", "Plugins", "PlayForge")):
    sys.exit(f"no check project with PlayForge at {check}: run export-unitypackage.py{' --edition core' if core else ''} "
             "--tests first")
# The windows each edition opens from Window/PlayForge, and what plays
titles = ['"Debugger"'] if core else ['"The Forge"', '"Debugger"', '"Problems"']
windows = WINDOWS.replace("TITLES", ", ".join(titles))
play = ("CoreSmoke.cs", CORE) if core else ("SampleScenesSmoke.cs", SCENES)
manifest = io.open(os.path.join(check, "Packages", "manifest.json"), encoding="utf-8").read()
if "com.unity.test-framework" not in manifest:
    sys.exit("the check project has no Test Framework: run export-unitypackage.py --tests")
os.makedirs(logs, exist_ok=True)

smoke = os.path.join(check, "Assets", "PlayForgeSmoke")


def take_out():
    if os.path.isdir(smoke):
        shutil.rmtree(smoke)
    if os.path.exists(smoke + ".meta"):
        os.remove(smoke + ".meta")


take_out()
for folder, files in (("Edit", {"PlayForgeSmoke.Edit.asmdef": EDIT_ASMDEF, "WindowsSmoke.cs": windows}),
                      ("Play", {"PlayForgeSmoke.Play.asmdef": PLAY_ASMDEF, play[0]: play[1]})):
    os.makedirs(os.path.join(smoke, folder))
    for name, text in files.items():
        io.open(os.path.join(smoke, folder, name), "w", encoding="utf-8", newline="\n").write(text)

passed = True
try:
    for platform, assembly in (("EditMode", "PlayForgeSmoke.Edit"), ("PlayMode", "PlayForgeSmoke.Play")):
        results = os.path.join(logs, f"smoke-{platform}.xml")
        log = os.path.join(logs, f"smoke-{platform}.log")
        for each in (results, log):
            if os.path.exists(each):
                os.remove(each)
        done = subprocess.run([UNITY, "-batchmode", "-projectPath", check, "-runTests", "-testPlatform", platform,
                               "-assemblyNames", assembly, "-testResults", results, "-logFile", log])
        text = io.open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
        print(f"{platform}: exit={done.returncode}")
        for line in sorted(set(re.findall(r"^.*error CS\d+.*$", text, re.M)))[:10]:
            print("  compile error:", line.strip()[:300])
        for line in re.findall(r"^SMOKE .*$", text, re.M):
            print("   ", line.strip()[:300])
        if not os.path.exists(results):
            print(f"  no results; see {log}")
            passed = False
            continue
        run = ET.parse(results).getroot()
        print(f"  {run.get('passed')} of {run.get('total')} passed, {run.get('failed')} failed")
        for case in run.iter("test-case"):
            if case.get("result") == "Failed":
                message = case.find("./failure/message")
                text = (message.text or "").strip().replace("\n", " | ") if message is not None else ""
                print(f"  FAILED {case.get('name')}: {text[:700]}")
        if done.returncode != 0 or run.get("failed") != "0" or run.get("total") in (None, "0"):
            passed = False
finally:
    take_out()
sys.exit(0 if passed else 1)
