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
        /// Documentation pages: the page as HTML under guides/, and the heading as its anchor, named as the site names
        /// it (EP-68).
        /// </summary>
        [Test]
        public void EP_68_A_help_button_opens_its_section_on_the_docs_site()
        {
            var docs = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeDocs");
            var urlOf = docs.GetMethod("UrlOf", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(urlOf, "the table gives each topic its address on the site");
            string Url(string topic) => (string)urlOf.Invoke(null, new object[] { topic });

            const string guides = "https://gabedvdsn.github.io/PlayForge-Docs/guides/";
            Assert.AreEqual(guides + "EFFECTS.html#local-data", Url("effects/localdata"), "a section, at its heading's anchor");
            Assert.AreEqual(guides + "ATTRIBUTE_SETS.html#sub-sets", Url("attributesets/subsets"), "a heading's hyphen kept");
            Assert.AreEqual(guides + "ATTRIBUTES.html#used-by", Url("attributes/usage"), "a topic named apart from its heading");
            Assert.AreEqual(guides + "CUES.html", Url("cues"), "a whole page");
            Assert.AreEqual(guides + "GETTING_STARTED.html", Url("nowhere"), "an unknown topic, the getting-started page");
        }
    }
}
