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
    }
}
