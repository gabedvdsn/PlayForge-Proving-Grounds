using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>What a buyer is handed: the package's version and its changelog (SP-31, Decision 87).</summary>
    public class ReleaseTests
    {
        private static PackageInfo Package()
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.faremerald.playforge/package.json");
            Assert.IsNotNull(package, "PlayForge is installed as a package");
            return package;
        }

        /// <summary>
        /// The first submission is 1.0.0, with a short entry for buyers. The refactor's detailed history, change by change
        /// with its tracker ID, is the repository's <c>Docs/HISTORY.md</c>, which doesn't ship (SP-31, Decision 87).
        /// </summary>
        [Test]
        public void SP_31_The_package_is_1_0_0_and_its_changelog_is_a_buyers()
        {
            var package = Package();
            Assert.AreEqual("1.0.0", package.version, "the first submission's version");

            var changelog = File.ReadAllText(Path.Combine(package.resolvedPath, "CHANGELOG.md"));
            var versions = Regex.Matches(changelog, @"^## \[([^\]]+)\]", RegexOptions.Multiline)
                .Select(match => match.Groups[1].Value)
                .ToList();
            Assert.That(versions.FirstOrDefault(), Is.EqualTo("1.0.0"), "its newest entry is the version's");

            var ids = Regex.Matches(changelog, @"\b(?:PC|TS|GAS|ED|SP|FN|FEAT|TW|LAB|LVL|AC|NUM)-[0-9A-Z]+\b")
                .Select(match => match.Value)
                .Distinct()
                .ToList();
            Assert.That(ids, Is.Empty, "a buyer's changelog names no tracker IDs: the detailed history is Docs/HISTORY.md's");
            Assert.That(changelog.Split('\n').Length, Is.LessThan(80), "and it's short");
        }

        [System.Serializable]
        private class VersionDefine
        {
            public string name;
            public string expression;
            public string define;
        }

        [System.Serializable]
        private class AssemblyDefinition
        {
            public string name;
            public string[] references = { };
            public string[] precompiledReferences = { };
            public string[] defineConstraints = { };
            public VersionDefine[] versionDefines = { };
        }

        /// <summary>
        /// From the Asset Store, the package sits under Assets, where Unity defines UNITY_INCLUDE_TESTS for every assembly,
        /// so a test assembly compiles in a project with no Test Framework and fails: the export's check found the golden
        /// tests' 4 errors, as the clean import found the samples' (SP-29). Each assembly that takes the Test Framework
        /// compiles only where it is, its version define naming it (SP-32).
        /// </summary>
        [Test]
        public void SP_32_Every_test_assembly_compiles_only_with_the_Test_Framework()
        {
            var package = Package();
            var tests = Directory.GetFiles(package.resolvedPath, "*.asmdef", SearchOption.AllDirectories)
                .Select(path => (path, asmdef: UnityEngine.JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path))))
                .Where(each => each.asmdef.references.Contains("UnityEngine.TestRunner")
                               || each.asmdef.precompiledReferences.Contains("nunit.framework.dll"))
                .ToList();
            Assert.That(tests.Count, Is.GreaterThanOrEqualTo(4), "the golden tests, the test kit and the samples' tests");

            foreach (var (path, asmdef) in tests)
            {
                Assert.That(asmdef.defineConstraints, Does.Contain("UNITY_TESTS_FRAMEWORK"), $"{asmdef.name} needs the Test Framework");
                Assert.That(asmdef.versionDefines.Any(each => each.name == "com.unity.test-framework" && each.define == "UNITY_TESTS_FRAMEWORK"),
                    Is.True, $"{asmdef.name} defines it where the Test Framework is ({Path.GetFileName(path)})");
            }
        }
    }
}
