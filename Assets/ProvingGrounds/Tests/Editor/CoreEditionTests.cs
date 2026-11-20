using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// PlayForge Process Sequencer, the core edition, on the user's word of 2026-11-19 (EP-84, its name EP-86):
    /// ProcessControl and task sequences sold on their own, as the full package's own files with the ability system
    /// left out. Its files, which <c>CoreEdition.txt</c> beside this test names, must compile with nothing else of
    /// PlayForge's: its runtime as a player's build and as the editor's, its editor as an assembly of its own over the
    /// runtime's, and its samples with its runtime. A change that ties one of them to the ability system fails here.
    /// </summary>
    public class CoreEditionTests
    {
        private static string Manifest => Path.Combine(Application.dataPath, "ProvingGrounds/Tests/Editor/CoreEdition.txt");

        // The package's folder on disk
        private static string PackageRoot =>
            PackageInfo.FindForAssembly(typeof(ProcessControl).Assembly)?.resolvedPath?.Replace('\\', '/');

        /// <summary>
        /// The scripts a section of the core edition's list names: each folder's scripts, each file, less each path left
        /// out; and what the list names that isn't there.
        /// </summary>
        internal static List<string> Scripts(string section, List<string> missing)
        {
            string root = PackageRoot;
            Assert.That(root, Is.Not.Null.And.Not.Empty, "PlayForge's package folder");
            var taken = new List<string>();
            var left = new List<string>();
            bool inSection = false;
            foreach (var raw in File.ReadAllLines(Manifest))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    inSection = line == $"[{section}]";
                    continue;
                }
                if (!inSection) continue;
                bool leave = line.StartsWith("!");
                var path = $"{root}/{line.TrimStart('!')}";
                if (leave)
                {
                    left.Add(path);
                    continue;
                }
                if (Directory.Exists(path)) taken.AddRange(Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Select(each => each.Replace('\\', '/')));
                else if (File.Exists(path)) { if (path.EndsWith(".cs")) taken.Add(path); }
                else missing.Add(line);
            }
            return taken.Where(each => !left.Any(leave => each == leave || each.StartsWith(leave + "/"))).Distinct().OrderBy(each => each).ToList();
        }

        // A scratch assembly's path, in the temporary folder
        private static string Scratch(string name) => Path.Combine(Path.GetTempPath(), $"PlayForgeCore{name}{Guid.NewGuid():N}.dll");

        // Compiles scripts into a scratch assembly with Unity's own compiler, referencing none of the project's script
        // assemblies but UniTask's and Unity's, and the assemblies given, and gives its errors
        internal static IEnumerator Compile(List<string> scripts, bool editor, List<string> errors, string output = null,
            params string[] references)
        {
            output ??= Scratch(editor ? "Editor" : "Player");
            var builder = new AssemblyBuilder(output, scripts.ToArray())
            {
                referencesOptions = ReferencesOptions.UseEngineModules,
                flags = editor ? AssemblyBuilderFlags.EditorAssembly : AssemblyBuilderFlags.None,
                additionalReferences = references,
            };
            builder.excludeReferences = Directory.GetFiles("Library/ScriptAssemblies", "*.dll")
                .Select(each => each.Replace('\\', '/'))
                .Where(each => !Kept(Path.GetFileNameWithoutExtension(each)))
                .ToArray();
            bool done = false;
            builder.buildFinished += (_, messages) =>
            {
                errors.AddRange(messages.Where(each => each.type == CompilerMessageType.Error).Select(each => each.message));
                done = true;
            };
            Assert.That(builder.Build(), Is.True, "the build started");
            // An editor frame in batch mode passes in a moment, so the wait is by the clock
            double until = EditorApplication.timeSinceStartup + 300;
            while (!done && EditorApplication.timeSinceStartup < until) yield return null;
            Assert.That(done, Is.True, "the build finished");
        }

        // What's wrong, the list's missing paths and the compile's first errors
        internal static string Problems(List<string> missing, List<string> errors) =>
            $"{missing.Count} listed but missing: {string.Join(", ", missing)}; {errors.Count} errors, the first: " +
            string.Join(" | ", errors.Take(12));

        // What the core edition may reference: UniTask, which it carries, and Unity's own packages
        private static bool Kept(string assembly) =>
            assembly.StartsWith("UniTask") || assembly.StartsWith("Unity.") || assembly.StartsWith("UnityEngine.");

        /// <summary>The core edition's runtime compiles alone, as a player's build compiles it (EP-84).</summary>
        [UnityTest]
        public IEnumerator EP_84_The_core_edition_compiles_alone_for_a_player()
        {
            var missing = new List<string>();
            var scripts = Scripts("runtime", missing);
            Assert.That(scripts.Count, Is.GreaterThan(40), "the core edition's scripts");
            var errors = new List<string>();
            yield return Compile(scripts, false, errors);
            Assert.That(missing.Count + errors.Count, Is.Zero, Problems(missing, errors));
        }

        /// <summary>The samples the core edition ships compile with its runtime alone, as a player's build compiles them (EP-84).</summary>
        [UnityTest]
        public IEnumerator EP_84_The_core_editions_samples_compile_with_it_alone()
        {
            var missing = new List<string>();
            var samples = Scripts("samples", missing);
            Assert.That(samples.Count, Is.GreaterThan(10), "the core edition's samples' scripts");
            var errors = new List<string>();
            yield return Compile(Scripts("runtime", missing).Concat(samples).ToList(), false, errors);
            Assert.That(missing.Count + errors.Count, Is.Zero, Problems(missing, errors));
        }

        /// <summary>
        /// The core edition's editor compiles with its runtime alone, as an assembly of its own over the runtime's, as
        /// Unity compiles the two (EP-84).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_84_The_core_editions_editor_compiles_with_it_alone()
        {
            var missing = new List<string>();
            var editor = Scripts("editor", missing);
            Assert.That(editor.Count, Is.GreaterThan(20), "the core edition's editor scripts");
            var errors = new List<string>();
            var runtime = Scratch("Runtime");
            yield return Compile(Scripts("runtime", missing), true, errors, runtime);
            Assert.That(missing.Count + errors.Count, Is.Zero, "the runtime it's over: " + Problems(missing, errors));
            yield return Compile(editor, true, errors, null, runtime);
            Assert.That(missing.Count + errors.Count, Is.Zero, Problems(missing, errors));
        }

        /// <summary>The core edition's runtime compiles alone in the editor too, where its editor-only code is in (EP-84).</summary>
        [UnityTest]
        public IEnumerator EP_84_The_core_edition_compiles_alone_in_the_editor()
        {
            var missing = new List<string>();
            var scripts = Scripts("runtime", missing);
            var errors = new List<string>();
            yield return Compile(scripts, true, errors);
            Assert.That(missing.Count + errors.Count, Is.Zero, Problems(missing, errors));
        }
    }
}
