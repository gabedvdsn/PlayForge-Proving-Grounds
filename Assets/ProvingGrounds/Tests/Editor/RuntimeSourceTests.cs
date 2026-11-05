using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>Rules the runtime's source keeps, read from the package's files.</summary>
    public class RuntimeSourceTests
    {
        private static readonly Regex UnityTimeOrRandom = new(
            @"(?<![\w.])(?:UnityEngine\.)?(?:Time\.(?:deltaTime|unscaledDeltaTime|fixedDeltaTime|smoothDeltaTime|time|unscaledTime|fixedTime|fixedUnscaledTime|frameCount|timeAsDouble|unscaledTimeAsDouble|realtimeSinceStartup|realtimeSinceStartupAsDouble)|Random\.(?:value|Range|insideUnitCircle|insideUnitSphere|onUnitSphere|rotation|rotationUniform|InitState|state|ColorHSV))\b");

        // A line's code, without its comments and string literals, carrying a block comment over to the next line
        private static string CodeOf(string line, ref bool inBlock)
        {
            var code = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (inBlock)
                {
                    if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        inBlock = false;
                        i++;
                    }
                    continue;
                }
                if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
                if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    inBlock = true;
                    i++;
                    continue;
                }
                if (line[i] == '"')
                {
                    // A string's text is no read of anything
                    i++;
                    while (i < line.Length && line[i] != '"')
                    {
                        if (line[i] == '\\') i++;
                        i++;
                    }
                    code.Append("\"\"");
                    continue;
                }
                code.Append(line[i]);
            }
            return code.ToString();
        }

        /// <summary>
        /// The runtime read Unity's Time and Random in a dozen files, so no clock or seed of its own could stand in, as a
        /// replay, a server or a test needs (FEAT-13, Decision 30). It reads them only through ForgeTime and ForgeRandom.
        /// </summary>
        [Test]
        public void FEAT_13_The_runtime_reads_time_and_random_numbers_only_through_ForgeTime()
        {
            var runtime = Path.GetFullPath("Packages/com.faremerald.playforge/Runtime");
            Assert.That(Directory.Exists(runtime), Is.True, $"the package's runtime is at {runtime}");

            var found = new List<string>();
            foreach (var file in Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "ForgeTime.cs") continue;
                var lines = File.ReadAllLines(file);
                bool inBlock = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (UnityTimeOrRandom.IsMatch(CodeOf(lines[i], ref inBlock)))
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
            Assert.That(found, Is.Empty, "the runtime reads time and random numbers through ForgeTime and ForgeRandom");
        }
    }
}
