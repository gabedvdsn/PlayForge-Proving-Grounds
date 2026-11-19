"""
The Asset Store export (Decision 87): build the package as a buyer gets it, in a throwaway project, export it as a
.unitypackage, and import that into a fresh project to check it. PlayForge's own repository never changes.

In the throwaway project, Assets/Plugins/PlayForge is the package as PlayForge's working tree has it, where a buyer's
import puts it, so the Forge Projects PlayForge makes in Assets/PlayForge are never in its folder (EP-12, Decision 89),
with:
- UniTask 2.5.11's source in PlayForge/ThirdParty/UniTask, from GitHub's archive of its tag, and its licence in
  PlayForge/Third-Party Notices.txt. A package under Assets resolves no dependency, so it carries its own;
- the samples package.json lists, copied out of Samples~ into PlayForge/Samples, as Unity imports no folder whose name
  ends in ~.
The internal docs live in PlayForge's own Docs/, outside the package, so it leaves nothing out.
The folders it makes take GUIDs worked out from the paths they had under Assets/PlayForge, so each export names them as
the exports before EP-12 did.

A package under Assets doesn't resolve package.json's dependencies either, so the upload declares them for the buyer's
import to install (PUBLISHING.md): com.unity.ugui and com.unity.profiling.core. Both projects take them, as an import
would.

The check: a fresh project imports the .unitypackage, and the gate is clean-import.py's: zero compile errors and
warnings, zero missing types, and no missing script, reference or type in the samples' scenes, prefabs and assets.
With --tests, the check project also takes the Test Framework and runs PlayForge's golden tests; with --build, it builds
a Windows player through PlayForge's BuildVerification.

Usage: python export-unitypackage.py [--out <dir>] [--skip-check] [--tests] [--build]
  --out         where the throwaway project, the check project, the logs and the .unitypackage go
                (default: %TEMP%\\claude\\export)
  --skip-check  export only
  --tests       the check project runs the golden tests too, with the Test Framework added
  --build       and builds a Windows player, its scenes none, as BuildVerification makes one

The throwaway project stays, so the Asset Store Tools can upload Assets/Plugins/PlayForge from it.
"""
import io
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
import uuid
import zipfile

UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.0.33f1\Editor\Unity.exe"
PACKAGE = r"C:\Users\17074\Documents\GitHub\PlayForge\Assets\PlayForge"
UNITASK_VERSION = "2.5.11"
UNITASK_ARCHIVE = f"https://github.com/Cysharp/UniTask/archive/refs/tags/{UNITASK_VERSION}.zip"
UNITASK_ROOT = f"UniTask-{UNITASK_VERSION}/"
UNITASK_SOURCE = UNITASK_ROOT + "src/UniTask/Assets/Plugins/UniTask/"
# What the upload declares, as package.json does for a UPM install
DEPENDENCIES = {"com.unity.ugui": "2.0.0", "com.unity.profiling.core": "1.0.2"}
TEST_FRAMEWORK = {"com.unity.test-framework": "1.4.5"}
# What the package holds that ships not: nothing, the internal docs living outside it (EP-21)
LEFT_OUT = set()
# The namespace the made folders' GUIDs come from, so an export names its folders as the last did
GUIDS = uuid.UUID("6f1c2b8e-5a0d-4c3e-9b7a-2d4f8e1a6c90")

EXPORT = r'''using System;
using UnityEditor;
using UnityEngine;

public static class PlayForgeExport
{
    public static void Run()
    {
        var path = Environment.GetEnvironmentVariable("PLAYFORGE_EXPORT_PATH");
        AssetDatabase.ExportPackage("Assets/Plugins/PlayForge", path, ExportPackageOptions.Recurse);
        Debug.Log("PLAYFORGE EXPORT done: " + path);
    }
}
'''

# Opens every scene, prefab and data asset under the samples and logs what doesn't resolve, one line each
SAMPLE_CHECK = r'''using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SampleCheck
{
    private const string Root = "Assets/Plugins/PlayForge/Samples";
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

# What the gate counts, as clean-import.py does, each as (name, pattern). A line is reported once, under the first
RULES = [
    ("compile errors", re.compile(r"error CS\d+")),
    ("compile warnings", re.compile(r"warning CS\d+")),
    ("uncompiled scripts", re.compile(r"will not be compiled")),
    ("sample problems", re.compile(r"^SAMPLE CHECK missing")),
    ("missing types", re.compile(r"TypeLoadException|missing type|could not be found|Could not extract GUID|"
                                 r"The referenced script .* is missing|has invalid class|Unknown managed type|"
                                 r"is not a valid type", re.I)),
    ("exceptions", re.compile(r"^[\w.]+Exception\b")),
    ("asset warnings", re.compile(r"^(?!.*warning CS).*(\b(warning|Warning)\b|Unknown pseudo class|Unknown property)")),
]
# Unity's import progress, "Start importing <path>", names each asset, so an asset named for a warning, as a cue's
# "Slam Warning", read as one
IGNORE = re.compile(r"Licensing|licensing|\[Licensing|Curl error|Unable to connect|Package Manager|Shader warning in|"
                    r"Start importing |^SAMPLE CHECK done|^PLAYFORGE EXPORT done", re.I)

args = sys.argv[1:]
out = os.path.join(os.environ.get("TEMP", r"C:\Temp"), "claude", "export")
if "--out" in args:
    out = args[args.index("--out") + 1]
download = os.path.join(out, "download")
build = os.path.join(out, "build")
check = os.path.join(out, "check")
logs = os.path.join(out, "logs")
for each in (download, logs):
    os.makedirs(each, exist_ok=True)
version = json.load(open(os.path.join(PACKAGE, "package.json"), encoding="utf-8"))["version"]
unitypackage = os.path.join(out, f"PlayForge-{version}.unitypackage")


def unity(*extra, log, env=None):
    path = os.path.join(logs, log)
    done = subprocess.run([UNITY, "-batchmode", "-nographics", *extra, "-quit", "-logFile", path], env=env)
    return done.returncode, open(path, encoding="utf-8", errors="replace").read()


def problems(log):
    found = {name: [] for name, _ in RULES}
    for line in log.splitlines():
        if IGNORE.search(line):
            continue
        for name, pattern in RULES:
            if pattern.search(line):
                if line not in found[name]:
                    found[name].append(line)
                break
    return found


def report(title, code, log):
    found = problems(log)
    print(f"{title}: exit={code}")
    for line in log.splitlines():
        if line.startswith("SAMPLE CHECK done") or line.startswith("PLAYFORGE EXPORT done"):
            print("   ", line.strip())
    total = 0
    for name, lines in found.items():
        total += len(lines)
        if not lines:
            continue
        print(f"  {name}: {len(lines)}")
        for line in lines[:30]:
            print("     ", line.strip()[:220])
        if len(lines) > 30:
            print(f"      … and {len(lines) - 30} more")
    return total == 0 and code == 0


def fresh(project, log, packages):
    if os.path.isdir(project):
        shutil.rmtree(project)
    code, _ = unity("-createProject", project, log=log)
    if code != 0:
        sys.exit(f"creating {project} failed ({code}); see {logs}\\{log}")
    manifest_path = os.path.join(project, "Packages", "manifest.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    manifest["dependencies"].update(packages)
    json.dump(manifest, open(manifest_path, "w", encoding="utf-8"), indent=2)


def folder_meta(path, relative):
    # A folder this export makes, its GUID from its path
    meta = path + ".meta"
    if os.path.exists(meta):
        return
    guid = uuid.uuid5(GUIDS, relative.replace("\\", "/")).hex
    io.open(meta, "w", newline="\n").write(
        f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n"
        f"  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def file_meta(path, relative):
    meta = path + ".meta"
    guid = uuid.uuid5(GUIDS, relative.replace("\\", "/")).hex
    io.open(meta, "w", newline="\n").write(
        f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n"
        f"  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


# ─── UniTask's source, from GitHub's archive of its tag ─────────────────────────────────────────────────────────────
archive = os.path.join(download, f"UniTask-{UNITASK_VERSION}.zip")
if not os.path.exists(archive):
    print(f"downloading {UNITASK_ARCHIVE}")
    urllib.request.urlretrieve(UNITASK_ARCHIVE, archive)

# ─── The throwaway project ───────────────────────────────────────────────────────────────────────────────────────────
fresh(build, "build-create.log", DEPENDENCIES)
# Under Assets/Plugins, where the buyer's import puts it (EP-12)
target = os.path.join(build, "Assets", "Plugins", "PlayForge")


def leave_out(directory, names):
    relative = os.path.relpath(directory, PACKAGE).replace("\\", "/")
    skipped = []
    for name in names:
        path = name if relative == "." else f"{relative}/{name}"
        bare = path[:-5] if path.endswith(".meta") else path
        if bare == "Samples~" or bare in LEFT_OUT:
            skipped.append(name)
    return skipped


shutil.copytree(PACKAGE, target, ignore=leave_out)
# The package's folder keeps the GUID it has in PlayForge's own project
if os.path.exists(PACKAGE + ".meta"):
    shutil.copyfile(PACKAGE + ".meta", target + ".meta")
folder_meta(target, "Assets/PlayForge")

# The samples, out of Samples~
samples = os.path.join(target, "Samples")
os.makedirs(samples)
folder_meta(samples, "Assets/PlayForge/Samples")
for sample in json.load(open(os.path.join(PACKAGE, "package.json"), encoding="utf-8")).get("samples", []):
    destination = os.path.join(samples, sample["displayName"])
    shutil.copytree(os.path.join(PACKAGE, sample["path"]), destination)
    folder_meta(destination, f"Assets/PlayForge/Samples/{sample['displayName']}")

# UniTask, embedded: only its plugin folder and its licence come out of the archive, each path checked to stay inside
third_party = os.path.join(target, "ThirdParty")
os.makedirs(third_party)
folder_meta(third_party, "Assets/PlayForge/ThirdParty")
unitask = os.path.join(third_party, "UniTask")
licence = None
with zipfile.ZipFile(archive) as zipped:
    for member in zipped.infolist():
        name = member.filename
        if name == UNITASK_ROOT + "LICENSE":
            licence = zipped.read(member).decode("utf-8")
        if not name.startswith(UNITASK_SOURCE) or member.is_dir():
            continue
        relative = name[len(UNITASK_SOURCE):]
        if not relative or os.path.isabs(relative) or ".." in relative.split("/"):
            sys.exit(f"the archive names {name}, outside the plugin's folder")
        path = os.path.join(unitask, *relative.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as file:
            file.write(zipped.read(member))
    # The plugin folder's own meta, where the archive has one
    folder = UNITASK_SOURCE.rstrip("/") + ".meta"
    if folder in zipped.namelist():
        with open(unitask + ".meta", "wb") as file:
            file.write(zipped.read(folder))
if licence is None or not os.path.isdir(unitask):
    sys.exit("the archive has no UniTask plugin folder or licence")
folder_meta(unitask, "Assets/PlayForge/ThirdParty/UniTask")

notices = os.path.join(target, "Third-Party Notices.txt")
io.open(notices, "w", encoding="utf-8", newline="\n").write(
    "Third-Party Notices\n\n"
    "This package contains third-party software components governed by the license(s) below.\n\n"
    f"Component: UniTask {UNITASK_VERSION} (https://github.com/Cysharp/UniTask), in PlayForge/ThirdParty/UniTask.\n"
    "If your project already has UniTask from Package Manager, delete that folder after importing.\n"
    "License: MIT\n\n" + licence.strip() + "\n")
file_meta(notices, "Assets/PlayForge/Third-Party Notices.txt")

editor = os.path.join(build, "Assets", "Editor")
os.makedirs(editor, exist_ok=True)
io.open(os.path.join(editor, "PlayForgeExport.cs"), "w", encoding="utf-8").write(EXPORT)

if os.path.exists(unitypackage):
    os.remove(unitypackage)
env = dict(os.environ, PLAYFORGE_EXPORT_PATH=unitypackage)
code, log = unity("-projectPath", build, "-executeMethod", "PlayForgeExport.Run", log="build.log", env=env)
built = report("the throwaway project, built and exported", code, log)
if not os.path.exists(unitypackage):
    sys.exit(f"no .unitypackage came out; see {logs}\\build.log")
print(f"exported: {unitypackage} ({os.path.getsize(unitypackage) / 1e6:.1f} MB)")
if "--skip-check" in args:
    sys.exit(0 if built else 1)

# ─── The check: a fresh project imports it ───────────────────────────────────────────────────────────────────────────
tests = "--tests" in args
fresh(check, "check-create.log", {**DEPENDENCIES, **(TEST_FRAMEWORK if tests else {})})
code, log = unity("-projectPath", check, "-importPackage", unitypackage, log="import.log")
imported = report("a fresh project, importing it", code, log)

editor = os.path.join(check, "Assets", "Editor")
os.makedirs(editor, exist_ok=True)
io.open(os.path.join(editor, "SampleCheck.cs"), "w", encoding="utf-8").write(SAMPLE_CHECK)
code, log = unity("-projectPath", check, "-executeMethod", "SampleCheck.Run", log="samples.log")
sampled = report("its samples, each opened", code, log)

tested = True
if tests:
    results = os.path.join(logs, "golden.xml")
    path = os.path.join(logs, "golden.log")
    done = subprocess.run([UNITY, "-batchmode", "-nographics", "-projectPath", check, "-runTests", "-testPlatform",
                           "EditMode", "-assemblyNames", "FarEmerald.PlayForge.Tests.Golden", "-testResults", results,
                           "-logFile", path])
    summary = open(results, encoding="utf-8", errors="replace").read() if os.path.exists(results) else ""
    match = re.search(r'<test-run[^>]*total="(\d+)"[^>]*passed="(\d+)"[^>]*failed="(\d+)"', summary)
    print(f"the golden tests: exit={done.returncode}", f"{match.group(2)} of {match.group(1)} passed" if match else
          f"no results; see {path}")
    tested = done.returncode == 0 and match is not None and match.group(3) == "0"

# The Windows player, built from the check project as a buyer's would be, through PlayForge's own build check
played = True
if "--build" in args:
    path = os.path.join(logs, "player.log")
    done = subprocess.run([UNITY, "-batchmode", "-nographics", "-projectPath", check, "-executeMethod",
                           "FarEmerald.PlayForge.Editor.BuildVerification.BuildWindowsPlayer", "-logFile", path])
    result = next((line.strip() for line in open(path, encoding="utf-8", errors="replace")
                   if "[BuildVerification] Result:" in line), f"no result; see {path}")
    print(f"the Windows player: exit={done.returncode}", result)
    played = done.returncode == 0

sys.exit(0 if built and imported and sampled and tested and played else 1)
