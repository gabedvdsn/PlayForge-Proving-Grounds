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

        // The symbols, in Unicode's symbol blocks, that have an emoji form (the Emoji property), which a font may draw in
        // color: from Miscellaneous Technical, Miscellaneous Symbols, Dingbats, and Miscellaneous Symbols and Arrows
        private static readonly (int First, int Last)[] EmojiSymbols =
        {
            (0x231A, 0x231B), (0x2328, 0x2328), (0x23CF, 0x23CF), (0x23E9, 0x23F3), (0x23F8, 0x23FA),
            (0x2600, 0x2604), (0x260E, 0x260E), (0x2611, 0x2611), (0x2614, 0x2615), (0x2618, 0x2618), (0x261D, 0x261D),
            (0x2620, 0x2620), (0x2622, 0x2623), (0x2626, 0x2626), (0x262A, 0x262A), (0x262E, 0x262F), (0x2638, 0x263A),
            (0x2640, 0x2640), (0x2642, 0x2642), (0x2648, 0x2653), (0x265F, 0x2660), (0x2663, 0x2663), (0x2665, 0x2666),
            (0x2668, 0x2668), (0x267B, 0x267B), (0x267E, 0x267F), (0x2692, 0x2697), (0x2699, 0x2699), (0x269B, 0x269C),
            (0x26A0, 0x26A1), (0x26A7, 0x26A7), (0x26AA, 0x26AB), (0x26B0, 0x26B1), (0x26BD, 0x26BE), (0x26C4, 0x26C5),
            (0x26C8, 0x26C8), (0x26CE, 0x26CF), (0x26D1, 0x26D1), (0x26D3, 0x26D4), (0x26E9, 0x26EA), (0x26F0, 0x26F5),
            (0x26F7, 0x26FA), (0x26FD, 0x26FD), (0x2702, 0x2702), (0x2705, 0x2705), (0x2708, 0x270D), (0x270F, 0x270F),
            (0x2712, 0x2712), (0x2714, 0x2714), (0x2716, 0x2716), (0x271D, 0x271D), (0x2721, 0x2721), (0x2728, 0x2728),
            (0x2733, 0x2734), (0x2744, 0x2744), (0x2747, 0x2747), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755),
            (0x2757, 0x2757), (0x2763, 0x2764), (0x2795, 0x2797), (0x27A1, 0x27A1), (0x27B0, 0x27B0), (0x27BF, 0x27BF),
            (0x2B05, 0x2B07), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
        };

        // A pictograph, an emoji variation selector, or a symbol with an emoji form
        private static bool IsEmoji(int codePoint) =>
            codePoint is >= 0x1F000 and <= 0x1FAFF or 0xFE0F ||
            EmojiSymbols.Any(range => codePoint >= range.First && codePoint <= range.Last);

        /// <summary>
        /// The editor showed emoji as icons: pictographs such as a link or a clipboard, and symbols with an emoji form
        /// such as the warning sign and the lightning bolt, which an emoji font draws in color. An icon is the kit's
        /// glyph or a word (Decision 20, ED-20). Text symbols with no emoji form, such as a check mark, go with each
        /// surface's redesign.
        /// </summary>
        [Test]
        public void ED_20_No_editor_text_shows_an_emoji()
        {
            var offenders = Directory.GetFiles(EditorFolder, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, number: i + 1)))
                .Where(x => !x.line.TrimStart().StartsWith("//") && HasEmoji(x.line))
                .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
                .ToList();
            Assert.That(offenders, Is.Empty);
        }

        private static bool HasEmoji(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                var codePoint = char.IsSurrogatePair(line, i) ? char.ConvertToUtf32(line, i++) : line[i];
                if (IsEmoji(codePoint)) return true;
            }
            return false;
        }

        /// <summary>
        /// The editor has one style sheet, PlayForge.uss, which every PlayForge window and inspector reads (ED-20). It
        /// had a style sheet nothing loaded and two UXML layouts nothing used.
        /// </summary>
        [Test]
        public void ED_20_The_editor_has_one_style_sheet_and_no_unused_layouts()
        {
            var sheets = Directory.GetFiles(EditorFolder, "*.uss", SearchOption.AllDirectories).Select(Path.GetFileName);
            Assert.That(sheets, Is.EquivalentTo(new[] { "PlayForge.uss" }), "style sheets");
            Assert.That(Directory.GetFiles(EditorFolder, "*.uxml", SearchOption.AllDirectories), Is.Empty, "layouts");
        }

        /// <summary>
        /// Drawers kept whether a part is collapsed in static dictionaries keyed by property path alone, so it was shared
        /// by every asset and lost on a domain reload (ED-18). Each drawer keeps it per asset, for the editor session.
        /// </summary>
        [Test]
        public void ED_18_No_drawer_keeps_collapse_state_in_a_static_dictionary()
        {
            var staticState = new Regex(@"\bstatic\s+(readonly\s+)?Dictionary<string,\s*bool>");
            var offenders = Directory.GetFiles(EditorFolder, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, number: i + 1)))
                .Where(x => staticState.IsMatch(x.line) && !x.line.TrimStart().StartsWith("//"))
                .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
                .ToList();
            Assert.That(offenders, Is.Empty);
        }
    }
}
