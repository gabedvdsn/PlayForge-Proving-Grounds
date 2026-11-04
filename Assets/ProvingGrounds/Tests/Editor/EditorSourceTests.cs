using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>Scans of PlayForge's editor sources and their meta files, as the package ships them.</summary>
    public class EditorSourceTests
    {
        private static string EditorFolder
        {
            get
            {
                var package = PackageInfo.FindForAssetPath("Packages/com.faremerald.playforge/package.json");
                Assert.IsNotNull(package, "PlayForge is installed as a package");
                return Path.Combine(package.resolvedPath, "Editor");
            }
        }

        /// <summary>
        /// Leftover debugging wrote to the Console: every change to an impact's attribute, each asset browser's config,
        /// a swallowed exception as plain text, each new tag, each copied tag path. A plain log now names its source in
        /// brackets, such as [PlayForge], and confirms something the user did.
        /// </summary>
        [Test]
        public void ED_17_Plain_logs_name_their_source()
        {
            var plainLog = new Regex(@"\bDebug\.Log\(");
            var named = new Regex(@"\bDebug\.Log\(\$?""\[");
            var offenders = Directory.GetFiles(EditorFolder, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, number: i + 1)))
                .Where(x => plainLog.IsMatch(x.line) && !x.line.TrimStart().StartsWith("//") && !named.IsMatch(x.line))
                .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
                .ToList();
            Assert.That(offenders, Is.Empty);
        }

        /// <summary>
        /// Four editor scripts' meta files named UXML files as default references, and none of those files exists.
        /// </summary>
        [Test]
        public void ED_28_Script_default_references_name_assets_that_exist()
        {
            var reference = new Regex(@"^\s*- (\w+): \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}");
            var dangling = Directory.GetFiles(EditorFolder, "*.cs.meta", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file).Select(line => (file, match: reference.Match(line))))
                .Where(x => x.match.Success && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(x.match.Groups[2].Value)))
                .Select(x => $"{Path.GetFileName(x.file)}: {x.match.Groups[1].Value}")
                .ToList();
            Assert.That(dangling, Is.Empty);
        }

        /// <summary>
        /// Drawers polled on a timer: the Impact drawer re-checked its operation and its effect's duration every 250 ms,
        /// and the Effect drawer rebuilt its link info every 500 ms, for as long as an inspector stayed open (ED-11).
        /// A view now follows the properties it shows, so no editor code repeats a timer.
        /// </summary>
        [Test]
        public void ED_11_No_editor_code_repeats_a_timer()
        {
            var repeating = new Regex(@"\.Every\(");
            var offenders = Directory.GetFiles(EditorFolder, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, number: i + 1)))
                .Where(x => repeating.IsMatch(x.line) && !x.line.TrimStart().StartsWith("//"))
                .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
                .ToList();
            Assert.That(offenders, Is.Empty);
        }
    }
}
