using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FarEmerald.PlayForge.Editor;
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

            var docs = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeDocs");
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

        /// <summary>
        /// A help button opens its section on PlayForge's documentation site, which DocFX builds from these same
        /// Documentation pages (EP-68). Each page is a folder under guides/, its opening at index.html and each of its
        /// sections a page of its own, named as the site names its heading (EP-69).
        /// </summary>
        [Test]
        public void EP_69_A_help_button_opens_its_sections_own_page()
        {
            var docs = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeDocs");
            var urlOf = docs.GetMethod("UrlOf", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(urlOf, "the table gives each topic its address on the site");
            string Url(string topic) => (string)urlOf.Invoke(null, new object[] { topic });

            const string guides = "https://gabedvdsn.github.io/PlayForge-Docs/guides/";
            Assert.AreEqual(guides + "effects/local-data.html", Url("effects/localdata"), "a section, its own page");
            Assert.AreEqual(guides + "attribute-sets/sub-sets.html", Url("attributesets/subsets"),
                "the page's folder a hyphen for the underscore, and a heading's hyphen kept");
            Assert.AreEqual(guides + "attributes/used-by.html", Url("attributes/usage"), "a topic named apart from its heading");
            Assert.AreEqual(guides + "cues/index.html", Url("cues"), "a whole page, at its opening");
            Assert.AreEqual(guides + "getting-started/index.html", Url("nowhere"), "an unknown topic, the getting-started page");
        }
    }
}
