"""
Phase 2's gate: import PlayForge into a fresh Unity 6 project and report what the import says.

Creates an empty project, adds PlayForge (as a local package) and UniTask to its manifest, opens it once in batch mode
so everything imports and compiles, then reads the log. The gate is zero warnings and zero missing types; this prints
each, grouped, and exits 0 only when there are none.

Usage: python clean-import.py [--keep] [--project <dir>] [--scan <log>]
  --keep      reuse an existing fresh project instead of making a new one. Assets import once, so a reopened
              project logs little: the gate needs a new one
  --project   where the fresh project goes (default: %TEMP%\\claude\\fresh)
  --scan      only read an import log already written, and report on it
"""
import json
import os
import re
import shutil
import subprocess
import sys

UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.0.33f1\Editor\Unity.exe"
PACKAGE = r"C:\Users\17074\Documents\GitHub\PlayForge\Assets\PlayForge"
UNITASK = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"

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

    manifest_path = os.path.join(project, "Packages", "manifest.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    manifest["dependencies"]["com.faremerald.playforge"] = "file:" + PACKAGE.replace("\\", "/")
    manifest["dependencies"]["com.cysharp.unitask"] = UNITASK
    json.dump(manifest, open(manifest_path, "w", encoding="utf-8"), indent=2)

    code, log = unity("-projectPath", project, log="import.log")

# What the gate counts, each as (name, pattern). A line is reported once, under the first that matches
RULES = [
    ("compile errors", re.compile(r"error CS\d+")),
    ("compile warnings", re.compile(r"warning CS\d+")),
    # Unity's warning for a script in a package that no assembly definition covers
    ("uncompiled scripts", re.compile(r"will not be compiled")),
    ("missing types", re.compile(r"TypeLoadException|missing type|could not be found|Could not extract GUID|"
                                 r"The referenced script .* is missing|has invalid class|Unknown managed type|"
                                 r"is not a valid type", re.I)),
    # A line that starts with an exception's name; the stack frames under it aren't counted again
    ("exceptions", re.compile(r"^[\w.]+Exception\b")),
    ("asset warnings", re.compile(r"^(?!.*warning CS).*\b(warning|Warning)\b")),
]
IGNORE = re.compile(r"Licensing|licensing|\[Licensing|Curl error|Unable to connect|Package Manager|Shader warning in",
                    re.I)

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
total = 0
for name, lines in found.items():
    total += len(lines)
    print(f"{name}: {len(lines)}")
    for line in lines[:40]:
        print("   ", line.strip()[:220])
    if len(lines) > 40:
        print(f"    … and {len(lines) - 40} more")
sys.exit(0 if total == 0 and code == 0 else 1)
