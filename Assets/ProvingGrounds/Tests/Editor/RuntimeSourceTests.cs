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
            Assert.That(ReadsOf(UnityTimeOrRandom, except: "ForgeTime.cs"), Is.Empty,
                "the runtime reads time and random numbers through ForgeTime and ForgeRandom");
        }

        private static readonly Regex UnityInputOrMainCamera = new(
            @"(?<![\w.])(?:UnityEngine\.)?(?:Input\.\w+|Camera\.main\b|(?:InputSystem\.)?(?:Mouse|Pointer|Keyboard|Touchscreen|Gamepad|Joystick|Pen)\.current\b)");

        /// <summary>
        /// Targeting read the Input Manager's mouse and Camera.main in five tasks, so a game on the Input System, with a
        /// camera of its own or a test couldn't stand in, and a scene with no main camera threw at the click (FEAT-11,
        /// Decision 30). The runtime reads input only through ForgeInput, whose defaults are the one place it reads Unity's.
        /// </summary>
        [Test]
        public void FEAT_11_The_runtime_reads_input_only_through_ForgeInput()
        {
            Assert.That(ReadsOf(UnityInputOrMainCamera, except: "ForgeInput.cs"), Is.Empty,
                "the runtime reads the player's input and the camera through ForgeInput");
        }

        // Each line of the package's runtime whose code reads what the rule matches, but in the one file let do it
        private static List<string> ReadsOf(Regex rule, string except)
        {
            var runtime = Path.GetFullPath("Packages/com.faremerald.playforge/Runtime");
            Assert.That(Directory.Exists(runtime), Is.True, $"the package's runtime is at {runtime}");

            var found = new List<string>();
            foreach (var file in Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == except) continue;
                var lines = File.ReadAllLines(file);
                bool inBlock = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (rule.IsMatch(CodeOf(lines[i], ref inBlock)))
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
            return found;
        }

        /// <summary>
        /// Interfaces nothing takes, which a project could still name, are gone (Decision 44, FEAT-36): IValidationReady
        /// and the generic IValidationRule, IAttributeHandler, IGameplayConfigUser and ILockedTagProvider, and the
        /// commented-out RuntimeProxy's file. IAbilityValidationRule, which an ability's activation rules take, declares
        /// its own Validate.
        /// </summary>
        [Test]
        public void FEAT_36_Interfaces_nothing_takes_are_gone()
        {
            var runtime = typeof(Ability).Assembly;
            foreach (var name in new[] { "IValidationReady", "IValidationRule`1", "IAttributeHandler", "IGameplayConfigUser", "ILockedTagProvider" })
                Assert.IsNull(runtime.GetType($"FarEmerald.PlayForge.{name}"), $"{name} is gone");
            Assert.IsNotNull(typeof(IAbilityValidationRule).GetMethod("Validate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly),
                "an ability's activation rule declares its own Validate");
            var proxy = Path.GetFullPath("Packages/com.faremerald.playforge/Runtime/PlayForge/Utils/RuntimeProxy.cs");
            Assert.IsFalse(File.Exists(proxy), "RuntimeProxy's commented-out file is gone");
        }
    }
}
