using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>The help (?) buttons on PlayForge's inspectors, and the pages they open.</summary>
    public class HelpLinkTests
    {
        /// <summary>
        /// 49 help buttons linked to docs.playforge.dev, a domain PlayForge doesn't own, and some named the wrong page.
        /// Decision 18 points them at the package's own Documentation pages, through one table of links.
        /// </summary>
        [Test]
        public void ED_16_Every_help_button_opens_a_page_with_its_section()
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.faremerald.playforge/package.json");
            Assert.IsNotNull(package, "PlayForge is installed as a package");
            var sources = Directory.GetFiles(Path.Combine(package.resolvedPath, "Editor"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
                .ToList();
            Assert.That(sources.Where(source => source.Contains("docs.playforge.dev")), Is.Empty,
                "no link to a site PlayForge doesn't own");

            var docs = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeDocs");
            Assert.IsNotNull(docs, "one table of links");
            var resolve = docs.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            var topic = new Regex(@"(?:HelpTopic = |GetDocumentationTopic\(\) => )""([a-z/\-]+)""");
            var topics = sources.SelectMany(source => topic.Matches(source).Select(match => match.Groups[1].Value))
                .Distinct()
                .ToList();
            Assert.That(topics.Count, Is.GreaterThan(40), "each inspector section's help topic");

            foreach (var name in topics)
            {
                var (page, heading) = ((string, string))resolve.Invoke(null, new object[] { name });
                // An unknown topic falls back to the getting-started page, which no section names on purpose
                Assert.That(page == "GETTING_STARTED.md" && heading == null, Is.False, $"{name} is in the table");
                var path = Path.Combine(package.resolvedPath, "Documentation", page);
                Assert.That(File.Exists(path), Is.True, $"{name}: {page} exists");
                if (heading != null)
                    Assert.That(File.ReadAllLines(path), Does.Contain("## " + heading), $"{name}: {page} has '{heading}'");
            }
        }
    }
}
