using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// What PlayForge ships, as a buyer gets it (the user's notes of 2026-11-16): its docs and what its code shows a user
    /// say nothing of the refactor's process (EP-21), each sample with assets imports as a Forge Project of its own
    /// (EP-22), and no sample ships tests (EP-24). The package is read where it's resolved, as the release check reads it.
    /// </summary>
    public class ShippedContentTests
    {
        // A tracker ID, a decision or an interview question, as the refactor's own records name them
        private static readonly Regex Process = new(
            @"\b(?:PC|TS|GAS|ED|SP|FEAT|TW|AC|FN|LVL|LAB|EP)-(?:[A-Z]{1,2}\d*[a-z]?|\d+[a-z]?)\b|\bDecision \d+\b");

        // A C# string literal on one line
        private static readonly Regex Literal = new(@"\$?@?""(?:[^""\\]|\\.)*""");

        private static string PackageRoot =>
            PackageInfo.FindForAssembly(typeof(Tag).Assembly)?.resolvedPath?.Replace('\\', '/');

        // Where each sample with assets is imported: as the gates' CI copies it in, or as a run of them here does
        private static readonly string[][] SampleFolders =
        {
            new[] { "Assets/PlayForge Demo", "Assets/PlayForgeDemo" },
            new[] { "Assets/PlayForge Additional Content", "Assets/PlayForgeAdditional" },
        };

        private static string[] SampleRoots()
        {
            var roots = SampleFolders.Select(each => each.FirstOrDefault(AssetDatabase.IsValidFolder)).ToArray();
            Assume.That(roots, Has.All.Not.Null, "the Demo and Additional Content are imported");
            return roots;
        }

        /// <summary>
        /// No shipped doc, the package's docs, its CHANGELOG and its samples' READMEs, names the refactor's process, as
        /// "(TW-30)" or "Decision 89"; the internal docs live in the repository, outside the package (EP-21).
        /// </summary>
        [Test]
        public void EP_21_No_shipped_doc_names_the_refactors_process()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var docs = Directory.GetFiles(Path.Combine(PackageRoot, "Documentation"), "*.md")
                .Concat(new[] { Path.Combine(PackageRoot, "CHANGELOG.md") })
                .Concat(Directory.GetFiles(Path.Combine(PackageRoot, "Samples~"), "README.md", SearchOption.AllDirectories));
            var named = docs.SelectMany(path => File.ReadAllLines(path).Select((line, at) => (path, at, line)))
                .Where(each => Process.IsMatch(each.line))
                .Select(each => $"{Path.GetFileName(each.path)}:{each.at + 1}: {Process.Match(each.line).Value}")
                .ToList();
            Assert.That(named, Is.Empty, $"docs naming the process: {string.Join(", ", named.Take(12))}");
            foreach (var internalDoc in new[] { "IMPACT_MODEL_REFACTOR.md", "PUBLISHING.md" })
                Assert.IsFalse(File.Exists(Path.Combine(PackageRoot, "Documentation", internalDoc)), $"{internalDoc} lives outside the package");
        }

        /// <summary>
        /// No string the package's code shows a user, a tooltip, a label, a note or a message, names the refactor's
        /// process, as a tooltip's "(TW-29)" (EP-21). Its tests' messages and its comments aren't a user's to read.
        /// </summary>
        [Test]
        public void EP_21_No_string_a_user_reads_names_the_refactors_process()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            // Not its tests', nor a hidden folder's, as a tool's own beside the package, which is none of Unity's
            var sources = Directory.GetFiles(PackageRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Tests/"))
                .Where(path => !path.Replace('\\', '/').Substring(PackageRoot.Length).Contains("/."));
            var named = (from path in sources
                         from each in File.ReadAllLines(path).Select((line, at) => (line, at))
                         let code = each.line.TrimStart()
                         where !code.StartsWith("//")
                         from Match literal in Literal.Matches(each.line)
                         where Process.IsMatch(literal.Value)
                         select $"{Path.GetFileName(path)}:{each.at + 1}: {Process.Match(literal.Value).Value}").ToList();
            Assert.That(named, Is.Empty, $"strings naming the process: {string.Join(", ", named.Take(12))}");
        }

        /// <summary>
        /// Each sample with assets imports as a Forge Project of its own, its project at its root with its Game Root
        /// entity, every PlayForge asset of it in the project: the Demo's and Additional Content's (EP-22).
        /// </summary>
        [Test]
        public void EP_22_Each_sample_with_assets_is_a_Forge_Project_of_its_own()
        {
            var folders = ForgeProject.Folders();
            foreach (var root in SampleRoots())
            {
                var project = folders.FirstOrDefault(each => each.Folder == root).Project;
                Assert.IsNotNull(project, $"{root} has a Forge Project at its root");
                Assert.IsNotNull(project.GameRootEntity, $"{project.name} carries its Game Root entity");
                Assert.AreEqual($"{root}/GameRoot/GameRoot.asset", AssetDatabase.GetAssetPath(project.GameRootEntity),
                    $"{project.name}'s Game Root entity is its own, in its GameRoot folder");
                var strays = AssetDatabase.FindAssets("t:ScriptableObject", new[] { root })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) is BaseForgeAsset && ForgeProject.Owner(path, folders) != project)
                    .ToList();
                Assert.That(strays, Is.Empty, $"every PlayForge asset of {root} in {project.name}: {string.Join(", ", strays.Take(8))}");
            }
        }

        /// <summary>
        /// No two of the package's assets share a GUID: the Additional Content's Game Root entity, made from the Demo's, kept
        /// the Demo's GUID, so where both samples were imported Unity gave one a new GUID and the Additional Content
        /// project's Game Root named neither (EP-22). A hidden folder, a tool's own, isn't the package's.
        /// </summary>
        [Test]
        public void EP_22_No_two_assets_share_a_GUID()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var owners = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            foreach (var meta in Directory.GetFiles(PackageRoot, "*.meta", SearchOption.AllDirectories))
            {
                string path = meta.Replace('\\', '/').Substring(PackageRoot.Length);
                if (path.Contains("/.")) continue;
                string guid = Regex.Match(File.ReadAllText(meta), @"^guid: ([0-9a-f]{32})", RegexOptions.Multiline).Groups[1].Value;
                if (guid.Length == 0) continue;
                if (!owners.TryGetValue(guid, out var paths)) owners[guid] = paths = new System.Collections.Generic.List<string>();
                paths.Add(path);
            }
            var shared = owners.Where(each => each.Value.Count > 1).Select(each => string.Join(" and ", each.Value)).ToList();
            Assert.That(shared, Is.Empty, $"assets sharing a GUID: {string.Join("; ", shared.Take(6))}");
        }

        /// <summary>
        /// Each scene of a sample with a Bootstrapper plays the sample's own Forge Project, so its GameRoot runs as that
        /// project's Game Root entity: the Demo's arena and Additional Content's Sparring (EP-22). Read from the scenes as
        /// saved, opening none of them.
        /// </summary>
        [Test]
        public void EP_22_Each_sample_scene_plays_its_samples_project()
        {
            var script = MonoImporter.GetAllRuntimeMonoScripts().FirstOrDefault(each => each != null && each.GetClass() == typeof(Bootstrapper));
            Assert.IsNotNull(script, "the Bootstrapper's script");
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string bootstrapper, out long _), "and its GUID");
            var reference = new Regex(@"^  Project: \{fileID: -?\d+(?:, guid: ([0-9a-f]{32}), type: \d+)?\}", RegexOptions.Multiline);

            var folders = ForgeProject.Folders();
            foreach (var root in SampleRoots())
            {
                var project = folders.FirstOrDefault(each => each.Folder == root).Project;
                Assert.IsNotNull(project, $"{root} has a Forge Project at its root");
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(project));
                int bootstrappers = 0;
                foreach (var scene in AssetDatabase.FindAssets("t:SceneAsset", new[] { root }).Select(AssetDatabase.GUIDToAssetPath))
                {
                    // Each of the scene's objects, by its header: a Bootstrapper's names its script
                    foreach (var block in File.ReadAllText(scene).Split(new[] { "\n--- !u!" }, System.StringSplitOptions.None))
                    {
                        if (!block.Contains($"guid: {bootstrapper}, type: 3}}")) continue;
                        bootstrappers++;
                        var plays = reference.Match(block);
                        Assert.IsTrue(plays.Success, $"{Path.GetFileName(scene)}'s Bootstrapper names a Project field");
                        Assert.AreEqual(guid, plays.Groups[1].Value, $"{Path.GetFileName(scene)}'s Bootstrapper plays {project.name}");
                    }
                }
                Assert.Greater(bootstrappers, 0, $"{root} has a scene with a Bootstrapper");
            }
        }

        /// <summary>
        /// No sample ships tests: a sample's tests live in Proving Grounds' SampleTests~, copied in beside the sample for a
        /// run, so no assembly of a sample references the test framework and no sample holds a Tests folder (EP-24).
        /// </summary>
        [Test]
        public void EP_24_No_sample_ships_tests()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var samples = Path.Combine(PackageRoot, "Samples~");
            var testing = Directory.GetFiles(samples, "*.asmdef", SearchOption.AllDirectories)
                .Where(path => Regex.IsMatch(File.ReadAllText(path), @"nunit\.framework|UnityEngine\.TestRunner|UNITY_INCLUDE_TESTS"))
                .Select(Path.GetFileName)
                .ToList();
            Assert.That(testing, Is.Empty, $"sample assemblies of tests: {string.Join(", ", testing)}");
            var folders = Directory.GetDirectories(samples, "Tests", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/').Substring(samples.Length))
                .ToList();
            Assert.That(folders, Is.Empty, $"sample folders of tests: {string.Join(", ", folders)}");
        }
    }
}
