"""
Phase 2's gate: import PlayForge into a fresh Unity 6 project and report what the import says.

Creates an empty project, adds PlayForge (as a local package) and UniTask to its manifest, opens it once in batch mode
so everything imports and compiles, then reads the log. The gate is zero warnings and zero missing types; this prints
each, grouped, and exits 0 only when there are none.

Usage: python clean-import.py [--keep] [--project <dir>] [--scan <log>] [--samples]
  --keep      reuse an existing fresh project instead of making a new one. Assets import once, so a reopened
              project logs little: the gate needs a new one
  --project   where the fresh project goes (default: %TEMP%\\claude\\fresh)
  --scan      only read an import log already written, and report on it
  --samples   import the package's samples as Package Manager would, with the Input System the Demo needs, then
              open every scene, prefab and data asset in them and report missing scripts, references and types
"""
import json
import os
import re
import shutil
import subprocess
import sys

UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.0.33f1\Editor\Unity.exe"
PACKAGE = r"C:\Users\17074\Documents\GitHub\PlayForge\Assets\PlayForge"
UNITASK = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11"
INPUT_SYSTEM = "1.11.2"

# Opens every scene, prefab and data asset under Assets/Samples and logs what doesn't resolve, one line each
SAMPLE_CHECK = r'''using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SampleCheck
{
    private const string Root = "Assets/Samples";
    private static int _scripts, _references, _types;

    public static void Run()
    {
        _scripts = _references = _types = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { Root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects()) CheckHierarchy(path, root);
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab) CheckHierarchy(path, prefab);
        }
        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { Root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null)
                {
                    _scripts++;
                    Debug.Log($"SAMPLE CHECK missing script: {path}");
                    continue;
                }
                CheckObject(path, asset.name, asset);
            }
        }
        Debug.Log($"SAMPLE CHECK done: {_scripts} missing scripts, {_references} missing references, {_types} missing types");
    }

    private static void CheckHierarchy(string path, GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var go = transform.gameObject;
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0)
            {
                _scripts += missing;
                Debug.Log($"SAMPLE CHECK missing script: {path}: {Name(go)} ({missing})");
            }
            foreach (var component in go.GetComponents<Component>())
            {
                // A component whose script has no class comes back null, which the count above doesn't include
                if (component is null)
                {
                    _scripts++;
                    Debug.Log($"SAMPLE CHECK missing script: {path}: {Name(go)} (no class)");
                }
                else CheckObject(path, Name(go) + "/" + component.GetType().Name, component);
            }
        }
    }

    private static void CheckObject(string path, string where, Object obj)
    {
        if (SerializationUtility.HasManagedReferencesWithMissingTypes(obj))
        {
            _types++;
            Debug.Log($"SAMPLE CHECK missing type: {path}: {where}");
        }
        var it = new SerializedObject(obj).GetIterator();
        while (it.Next(true))
        {
            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null
                && it.objectReferenceInstanceIDValue != 0)
            {
                _references++;
                Debug.Log($"SAMPLE CHECK missing reference: {path}: {where}.{it.propertyPath}");
            }
        }
    }

    private static string Name(GameObject go) => go.transform.parent ? Name(go.transform.parent.gameObject) + "/" + go.name : go.name;
}
'''

args = sys.argv[1:]
keep = "--keep" in args
project = os.path.join(os.environ.get("TEMP", r"C:\Temp"), "claude", "fresh")
if "--project" in args:
    project = args[args.index("--project") + 1]
logs = os.path.join(os.path.dirname(project), "fresh-logs")
os.makedirs(logs, exist_ok=True)


def unity(*extra, log):
    path = os.path.join(logs, log)
    done = subprocess.run([UNITY, "-batchmode", "-nographics", *extra, "-quit", "-logFile", path])
    return done.returncode, open(path, encoding="utf-8", errors="replace").read()


if "--scan" in args:
    code = 0
    log = open(args[args.index("--scan") + 1], encoding="utf-8", errors="replace").read()
else:
    if not keep or not os.path.isdir(project):
        if os.path.isdir(project):
            shutil.rmtree(project)
        code, _ = unity("-createProject", project, log="create.log")
        if code != 0:
            sys.exit(f"creating the project failed ({code}); see {logs}\\create.log")

    samples = "--samples" in args
    manifest_path = os.path.join(project, "Packages", "manifest.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    manifest["dependencies"]["com.faremerald.playforge"] = "file:" + PACKAGE.replace("\\", "/")
    manifest["dependencies"]["com.cysharp.unitask"] = UNITASK
    if samples:
        manifest["dependencies"]["com.unity.inputsystem"] = INPUT_SYSTEM
    json.dump(manifest, open(manifest_path, "w", encoding="utf-8"), indent=2)

    if samples:
        # Where Package Manager puts an imported sample: Assets/Samples/<display name>/<version>/<sample>
        version = json.load(open(os.path.join(PACKAGE, "package.json"), encoding="utf-8"))["version"]
        for sample in json.load(open(os.path.join(PACKAGE, "package.json"), encoding="utf-8")).get("samples", []):
            source = os.path.join(PACKAGE, sample["path"])
            target = os.path.join(project, "Assets", "Samples", "PlayForge", version, sample["displayName"])
            if os.path.isdir(target):
                shutil.rmtree(target)
            shutil.copytree(source, target)
        editor = os.path.join(project, "Assets", "Editor")
        os.makedirs(editor, exist_ok=True)
        open(os.path.join(editor, "SampleCheck.cs"), "w", encoding="utf-8").write(SAMPLE_CHECK)
        code, log = unity("-projectPath", project, "-executeMethod", "SampleCheck.Run", log="import.log")
    else:
        code, log = unity("-projectPath", project, log="import.log")

# What the gate counts, each as (name, pattern). A line is reported once, under the first that matches
RULES = [
    ("compile errors", re.compile(r"error CS\d+")),
    ("compile warnings", re.compile(r"warning CS\d+")),
    # Unity's warning for a script in a package that no assembly definition covers
    ("uncompiled scripts", re.compile(r"will not be compiled")),
    # What the sample check found, with --samples
    ("sample problems", re.compile(r"^SAMPLE CHECK missing")),
    ("missing types", re.compile(r"TypeLoadException|missing type|could not be found|Could not extract GUID|"
                                 r"The referenced script .* is missing|has invalid class|Unknown managed type|"
                                 r"is not a valid type", re.I)),
    # A line that starts with an exception's name; the stack frames under it aren't counted again
    ("exceptions", re.compile(r"^[\w.]+Exception\b")),
    # Unity's own import warnings, and the USS parser's, which don't say "warning"
    ("asset warnings", re.compile(r"^(?!.*warning CS).*(\b(warning|Warning)\b|Unknown pseudo class|Unknown property)")),
]
IGNORE = re.compile(r"Licensing|licensing|\[Licensing|Curl error|Unable to connect|Package Manager|Shader warning in|"
                    r"^SAMPLE CHECK done", re.I)

found = {name: [] for name, _ in RULES}
for line in log.splitlines():
    if IGNORE.search(line):
        continue
    for name, pattern in RULES:
        if pattern.search(line):
            if line not in found[name]:
                found[name].append(line)
            break

print(f"import exit={code}  log: {logs}\\import.log")
for line in log.splitlines():
    if line.startswith("SAMPLE CHECK done"):
        print(line.strip())
total = 0
for name, lines in found.items():
    total += len(lines)
    print(f"{name}: {len(lines)}")
    for line in lines[:40]:
        print("   ", line.strip()[:220])
    if len(lines) > 40:
        print(f"    … and {len(lines) - 40} more")
sys.exit(0 if total == 0 and code == 0 else 1)
