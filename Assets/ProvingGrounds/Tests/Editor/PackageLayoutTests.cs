using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The package as its files lie and its code is named (EP-25, the user's notes of 2026-11-16): each assembly's code under
    /// its own name, each folder named for what it holds, and no file of nothing. The package is read where it's resolved.
    /// </summary>
    public class PackageLayoutTests
    {
        private static string PackageRoot =>
            PackageInfo.FindForAssembly(typeof(Tag).Assembly)?.resolvedPath?.Replace('\\', '/');

        // What a compiler or Unity adds to an assembly, which no one names
        private static bool Made(Type type) =>
            type.Name.StartsWith("<") || type.Name.StartsWith("__") || type.Name.StartsWith("$") ||
            type.Name.StartsWith("UnitySourceGenerated") ||
            type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() != null;

        /// <summary>
        /// Each of the package's assemblies keeps its types in the namespace of its name: the runtime's in
        /// FarEmerald.PlayForge, its inspector attributes among them, which were in FarEmerald.PlayForge.Extended; the
        /// editor's in FarEmerald.PlayForge.Editor, which was FarEmerald.PlayForge.Extended.Editor; the Lab's, its editor's
        /// and the test kit's in theirs (EP-25).
        /// </summary>
        [Test]
        public void EP_25_Each_assembly_keeps_its_code_under_its_name()
        {
            var wrong = new List<string>();
            foreach (var name in new[] { "FarEmerald.PlayForge", "FarEmerald.PlayForge.Editor", "FarEmerald.PlayForge.Lab",
                         "FarEmerald.PlayForge.Lab.Editor", "FarEmerald.PlayForge.Testing" })
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(each => each.GetName().Name == name);
                Assert.IsNotNull(assembly, $"{name} is loaded");
                wrong.AddRange(assembly.GetTypes()
                    .Where(type => !type.IsNested && !Made(type) && type.Namespace != name)
                    .Select(type => $"{name}: {type.FullName}"));
            }
            Assert.That(wrong, Is.Empty, $"types under another name: {string.Join(", ", wrong.Take(12))}");
        }

        /// <summary>
        /// Each sample's code is under its assembly's name, as its assembly definition's root namespace says: the Battle's
        /// in FarEmerald.PlayForge.Samples.Battle, which was Samples.Battles, and Code Examples' in
        /// FarEmerald.PlayForge.Examples, which was FarEmerald.PlayForge.Extended.Examples (EP-25). Read from the package's
        /// sources, as a sample's assemblies load only where it's imported.
        /// </summary>
        [Test]
        public void EP_25_Each_samples_code_is_under_its_assemblys_name()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var samples = Path.Combine(PackageRoot, "Samples~").Replace('\\', '/');
            var definitions = Directory.GetFiles(samples, "*.asmdef", SearchOption.AllDirectories).Select(path => path.Replace('\\', '/')).ToList();
            Assert.That(definitions, Is.Not.Empty, "the samples have assemblies");
            var wrong = new List<string>();
            foreach (var asmdef in definitions)
            {
                string json = File.ReadAllText(asmdef);
                string name = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                string root = Regex.Match(json, "\"rootNamespace\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                if (root != name) wrong.Add($"{Path.GetFileName(asmdef)}'s root namespace \"{root}\"");
                foreach (var script in Directory.GetFiles(Path.GetDirectoryName(asmdef), "*.cs", SearchOption.AllDirectories))
                {
                    if (OwnerOf(script.Replace('\\', '/'), samples) != asmdef) continue;
                    foreach (Match declared in Regex.Matches(File.ReadAllText(script), @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline))
                        if (declared.Groups[1].Value != name) wrong.Add($"{Path.GetFileName(script)}: {declared.Groups[1].Value}");
                }
            }
            Assert.That(wrong, Is.Empty, $"sample code under another name: {string.Join(", ", wrong.Take(12))}");
        }

        // The assembly definition nearest a script, within the samples' folder
        private static string OwnerOf(string script, string samples)
        {
            for (var folder = Path.GetDirectoryName(script)?.Replace('\\', '/'); folder != null && folder.Length >= samples.Length;
                 folder = Path.GetDirectoryName(folder)?.Replace('\\', '/'))
            {
                var found = Directory.GetFiles(folder, "*.asmdef");
                if (found.Length > 0) return found[0].Replace('\\', '/');
            }
            return null;
        }

        /// <summary>
        /// Each folder of the package is named for what it holds (EP-25): no CustomPropertyDrawers over the whole editor,
        /// whose drawers, inspectors, windows and The Forge each have a folder of their own; no folder in a folder of its
        /// own name; the scalers with the numbers, not an effect's Modifier beside its effect Modifiers; no Authored folder
        /// over the runtime; and each inspector in Editor/Inspectors, but the Lab's, with the Lab.
        /// </summary>
        [Test]
        public void EP_25_Each_folder_is_named_for_what_it_holds()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var folders = Directory.GetDirectories(PackageRoot, "*", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/').Substring(PackageRoot.Length + 1))
                .Where(path => !path.StartsWith(".") && !path.Contains("/."))
                .ToList();
            Assert.That(folders.Where(path => path.Split('/').Contains("CustomPropertyDrawers")), Is.Empty, "no CustomPropertyDrawers folder");
            Assert.That(new[] { "Editor/Drawers", "Editor/Inspectors", "Editor/Numbers", "Editor/TheForge", "Editor/Windows", "Editor/Projects", "Editor/Util" },
                Is.SubsetOf(folders), "the editor's parts, each a folder");
            Assert.That(folders.Where(path =>
            {
                var parts = path.Split('/');
                return parts.Length > 1 && parts[parts.Length - 1] == parts[parts.Length - 2];
            }), Is.Empty, "no folder in one of its own name");
            Assert.That(folders, Has.No.Member("Runtime/PlayForge/GameplayEffect/Modifier"), "the scalers aren't an effect's Modifier");
            Assert.That(folders, Has.Member("Runtime/PlayForge/Numbers/Scalers"), "they're with the numbers");
            Assert.That(folders, Has.No.Member("Runtime/PlayForge/Authored"), "no Authored folder over the runtime");
            Assert.That(folders, Has.No.Member("Editor/Tools/ForgeTemplatePropogation"), "a folder spelled as its scripts are");

            var stray = Directory.GetFiles(Path.Combine(PackageRoot, "Editor"), "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/').Substring(PackageRoot.Length + 1))
                .Where(path => !path.StartsWith("Editor/Inspectors/") && !path.StartsWith("Editor/Lab/"))
                .Where(path => Regex.IsMatch(File.ReadAllText(Path.Combine(PackageRoot, path)), @"^\s*\[CustomEditor\(", RegexOptions.Multiline))
                .ToList();
            Assert.That(stray, Is.Empty, $"inspectors outside Editor/Inspectors: {string.Join(", ", stray)}");
        }

        /// <summary>
        /// No file of nothing ships (EP-25): an empty namespace, empty classes and a factory nothing took, and two files of
        /// commented-out code, one with an enum nothing read, are gone.
        /// </summary>
        [Test]
        public void EP_25_No_file_of_nothing_ships()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var runtime = typeof(Tag).Assembly;
            foreach (var name in new[] { "ManagementInitDeferment", "ValidationRuleset", "EntitySpec", "EffectContainerFactory", "EAttributeModificationMethod" })
                Assert.IsNull(runtime.GetType($"FarEmerald.PlayForge.{name}"), $"{name} is gone");
            // A hidden folder, as a tool's own beside the package, is none of Unity's, nor the package's
            var files = Directory.GetFiles(PackageRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Substring(PackageRoot.Length).Contains("/."))
                .Select(Path.GetFileName)
                .ToList();
            foreach (var file in new[] { "WorkerStore.cs", "AbilitySequenceInjectionBridge.cs", "SourcedModifiedAttributeCache.cs" })
                Assert.That(files, Has.No.Member(file), $"{file} is gone");
        }
    }
}
