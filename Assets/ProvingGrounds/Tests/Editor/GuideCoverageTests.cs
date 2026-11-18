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
    /// The guides the package ships tell of what it holds (EP-70): each public type of the editor, of tags and of time,
    /// input and logging is named in one, as the docs site links a type's page to the guides that name it; and the
    /// extending guide names only real bases, hooks and attributes. A type is named where a guide's code span holds its
    /// name as a word, as `ForgeDrawerStyles.SectionConfig` names SectionConfig.
    /// </summary>
    public class GuideCoverageTests
    {
        private static string PackageRoot =>
            PackageInfo.FindForAssembly(typeof(Tag).Assembly)?.resolvedPath?.Replace('\\', '/');

        // A span of code in a guide, and a word in it
        private static readonly Regex Code = new(@"`([^`\n]+)`");
        private static readonly Regex Word = new(@"\b[A-Za-z_]\w*\b");

        // A public type's declaration in a source file: its name, the first group a type's, the second a delegate's
        private static readonly Regex Declaration = new(
            @"^\s*public\s+(?:(?:sealed|abstract|static|partial|readonly)\s+)*(?:class|struct|interface|enum|record)\s+(\w+)" +
            @"|^\s*public\s+delegate\s+[\w<>\[\],.? ]+?\s+(\w+)\s*[<(]", RegexOptions.Multiline);

        // The package's own assemblies: its runtime, the Creator Lab's, its editor's and its test kit's
        private static IEnumerable<Assembly> PackageAssemblies() =>
            AppDomain.CurrentDomain.GetAssemblies().Where(assembly =>
            {
                var name = assembly.GetName().Name;
                return name == "FarEmerald.PlayForge" || name == "FarEmerald.PlayForge.Lab" || name == "FarEmerald.PlayForge.Editor"
                       || name == "FarEmerald.PlayForge.Lab.Editor" || name == "FarEmerald.PlayForge.Testing";
            });

        // An assembly's public types; none of a dynamic assembly's, or of one that can't list them
        private static IEnumerable<Type> VisibleTypes(Assembly assembly)
        {
            if (assembly.IsDynamic) return Enumerable.Empty<Type>();
            try { return assembly.GetTypes().Where(type => type.IsVisible).ToList(); }
            catch (ReflectionTypeLoadException loading) { return loading.Types.Where(type => type != null && type.IsVisible).ToList(); }
            catch (Exception) { return Enumerable.Empty<Type>(); }
        }

        private static string NameOf(Type type) => type.Name.Split('`')[0];

        // Every word of every guide's code spans
        private static HashSet<string> NamedInGuides()
        {
            var words = new HashSet<string>();
            foreach (var guide in Directory.GetFiles(Path.Combine(PackageRoot, "Documentation"), "*.md"))
                foreach (Match span in Code.Matches(File.ReadAllText(guide)))
                    foreach (Match word in Word.Matches(span.Groups[1].Value))
                        words.Add(word.Value);
            return words;
        }

        // The public types the package's source declares under its paths, folders or files, that are public from outside:
        // a public type nested in an internal one isn't
        private static List<string> PublicTypesUnder(params string[] paths)
        {
            var visible = new HashSet<string>(PackageAssemblies().SelectMany(VisibleTypes).Select(NameOf));
            var files = paths.Select(path => Path.Combine(PackageRoot, path))
                .SelectMany(path => Directory.Exists(path) ? Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories) : new[] { path });
            return files.SelectMany(file => Declaration.Matches(File.ReadAllText(file)).Cast<Match>())
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
                .Where(visible.Contains)
                .Distinct()
                .OrderBy(name => name)
                .ToList();
        }

        private static void AssertEachNamed(string what, params string[] paths)
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var types = PublicTypesUnder(paths);
            Assert.That(types.Count, Is.GreaterThan(3), $"{what}'s public types are found");
            var named = NamedInGuides();
            var missing = types.Where(type => !named.Contains(type)).ToList();
            Assert.That(missing, Is.Empty,
                $"{missing.Count} of {what}'s {types.Count} public types no guide names: {string.Join(", ", missing.Take(24))}");
        }

        /// <summary>Each public type of the editor and the Creator Lab's editor is named in a guide.</summary>
        [Test]
        public void EP_70_A_Each_public_type_of_the_editor_is_named_in_a_guide() => AssertEachNamed("the editor", "Editor");

        /// <summary>Each public type of tags is named in a guide.</summary>
        [Test]
        public void EP_70_B_Each_public_type_of_tags_is_named_in_a_guide() => AssertEachNamed("tags", "Runtime/PlayForge/Tag");

        /// <summary>Each public type of time, randomness, input and logging is named in a guide.</summary>
        [Test]
        public void EP_70_C_Each_public_type_of_time_input_and_logging_is_named_in_a_guide() =>
            AssertEachNamed("time, input and logging", "Runtime/PlayForge/Utils", "Runtime/PlayForge/Numbers/Chance",
                "Runtime/PlayForge/Core/ForgeLog.cs");

        // A type a guide's own example declares, in a code block
        private static readonly Regex Declared = new(@"\b(?:class|struct|interface|enum|record)\s+(\w+)");

        /// <summary>
        /// The extending guide names only real extension points: each base class or interface it names, as
        /// `AbstractEffectWorker` or `IForgeClock`, and each attribute, as `[ForgeMenu]`, is a type there is, the package's,
        /// Unity's or .NET's, or one its own examples declare; and each member it names on one of the package's types, as
        /// `AbstractAbilityComponent.OnActivate`, is on that type or a base.
        /// </summary>
        [Test]
        public void EP_70_D_The_extending_guide_names_only_real_bases_hooks_and_attributes()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var path = Path.Combine(PackageRoot, "Documentation", "EXTENDING.md");
            Assert.That(File.Exists(path), Is.True, "the package ships the extending guide");
            var text = File.ReadAllText(path);
            var byName = PackageAssemblies().SelectMany(VisibleTypes).GroupBy(NameOf).ToDictionary(group => group.Key, group => group.ToList());
            var known = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().SelectMany(VisibleTypes).Select(NameOf));
            foreach (Match block in Regex.Matches(text, @"```csharp\s*\n(.*?)```", RegexOptions.Singleline))
                foreach (Match declared in Declared.Matches(block.Groups[1].Value))
                    known.Add(declared.Groups[1].Value);
            var spans = Code.Matches(text).Cast<Match>().Select(match => match.Groups[1].Value.Trim()).Distinct().ToList();
            var wrong = new List<string>();
            var checkedNames = 0;
            foreach (var span in spans)
            {
                var attribute = Regex.Match(span, @"^\[(\w+)(?:\(.*\))?\]$");
                if (attribute.Success)
                {
                    checkedNames++;
                    var name = attribute.Groups[1].Value;
                    if (!known.Contains(name) && !known.Contains(name + "Attribute")) wrong.Add(span);
                    continue;
                }
                var baseType = Regex.Match(span, @"^((?:Abstract|I(?=[A-Z]))\w+)(?:<[^>]*>)?$");
                if (baseType.Success)
                {
                    checkedNames++;
                    if (!known.Contains(baseType.Groups[1].Value)) wrong.Add(span);
                    continue;
                }
                var member = Regex.Match(span, @"^(\w+)(?:<[^>]*>)?\.(\w+)(?:\(.*\))?$");
                if (member.Success && byName.TryGetValue(member.Groups[1].Value, out var owners))
                {
                    checkedNames++;
                    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                    bool Has(Type type)
                    {
                        for (var each = type; each != null; each = each.BaseType)
                            if (each.GetMember(member.Groups[2].Value, all).Length > 0) return true;
                        return type.GetInterfaces().Any(face => face.GetMember(member.Groups[2].Value, all).Length > 0);
                    }
                    if (!owners.Any(Has)) wrong.Add(span);
                }
            }
            Assert.That(checkedNames, Is.GreaterThan(30), "the guide names the extension points it covers");
            Assert.That(wrong, Is.Empty, $"names there are none of: {string.Join(", ", wrong)}");
        }
    }
}
