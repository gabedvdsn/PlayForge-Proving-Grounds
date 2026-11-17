using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // ForgeRandom and its sources have a file of their own since EP-25, out of ForgeTime.cs
            Assert.That(ReadsOf(UnityTimeOrRandom, "ForgeTime.cs", "ForgeRandom.cs"), Is.Empty,
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
            Assert.That(ReadsOf(UnityInputOrMainCamera, "ForgeInput.cs"), Is.Empty,
                "the runtime reads the player's input and the camera through ForgeInput");
        }

        private static readonly Regex UnityLog = new(@"(?<![\w.])(?:UnityEngine\.)?Debug\.Log\w*\s*\(");

        /// <summary>
        /// The runtime logged straight to Unity's console in 70 places, ProcessControl's, the action queue's, the tags' and
        /// the task sequences' among them, so no level held them back and no listener, the Creator Lab's log among them,
        /// heard them (EP-27, the user's answer of 2026-11-16). It logs through ForgeLog, whose own file is the one place
        /// that writes to the console.
        /// </summary>
        [Test]
        public void EP_27_The_runtime_logs_through_ForgeLog()
        {
            Assert.That(ReadsOf(UnityLog, "ForgeLog.cs"), Is.Empty, "the runtime logs through ForgeLog");
        }

        private static readonly Regex FrameStep = new(@"(?<![\w.])ForgeTime\.(?:DeltaTime|DeltaTimeFor|UnscaledDeltaTime)\b");

        /// <summary>
        /// A sequence read its time from its packet's owner or the game's clock, so nothing could give it a clock of its
        /// own (EP-32, the user's question of 2026-11-17). The task sequences and the abilities' tasks read their step of
        /// time through their run's clock, which takes the packet's, the sequence's, the owner's and the game's in turn.
        /// </summary>
        [Test]
        public void EP_32_Sequences_read_their_time_through_their_runs_clock()
        {
            var found = new List<string>();
            foreach (var folder in new[] { "PlayForge/Core/TaskSequence", "PlayForge/AbilitySystem/Tasks" })
                found.AddRange(ReadsOf(FrameStep, folder, new string[0]));
            Assert.That(found, Is.Empty, "the sequences and the abilities' tasks read their time through their run's clock");
        }

        // Each line of the package's runtime whose code reads what the rule matches, but in the files let do it
        private static List<string> ReadsOf(Regex rule, params string[] except) => ReadsOf(rule, "", except);

        // Each line of a folder of the package's runtime whose code reads what the rule matches, but in the files let do it
        private static List<string> ReadsOf(Regex rule, string folder, string[] except)
        {
            var runtime = Path.GetFullPath(Path.Combine("Packages/com.faremerald.playforge/Runtime", folder));
            Assert.That(Directory.Exists(runtime), Is.True, $"the package's runtime is at {runtime}");

            var found = new List<string>();
            foreach (var file in Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories))
            {
                if (System.Array.IndexOf(except, Path.GetFileName(file)) >= 0) continue;
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

        // The core: the code every component of PlayForge's and of a project's goes through, which may need no special case
        // for one: the GAS's core, an ability's spec, the ability and effect systems, purging, items, the ability proxy and
        // the targeting tasks; and in the editor, the Problems view's rules and the ability inspector
        private static readonly string[] Core =
        {
            "Runtime/PlayForge/GAS/Core/GASCore.cs",
            "Runtime/PlayForge/AbilitySystem/Core/AbilitySpec.cs",
            "Runtime/PlayForge/AbilitySystem/Core/AbilitySystemComponent.cs",
            "Runtime/PlayForge/GAS/Core/EffectSystemComponent.cs",
            "Runtime/PlayForge/GameplayEffect/Core/Purging.cs",
            "Runtime/PlayForge/ItemSystem/Item/Item.cs",
            "Runtime/PlayForge/AbilitySystem/Ability/AbilityProxy/Core/AbilityProxy.cs",
            "Runtime/PlayForge/AbilitySystem/Tasks/Targeting",
            "Editor/Problems/ForgeRules.cs",
            "Editor/Inspectors/AbilityEditor.cs",
        };

        // A type declared in a namespace, not one nested in a type, which is its own
        private static readonly Regex DeclaredType = new(@"(?m)^ {0,4}(?:public |internal )?(?:sealed |static |abstract |partial |readonly )*(?:class|struct|enum|interface) (\w+)");
        private static readonly Regex Extension = new(@"\bstatic\s+[\w<>\[\],.?\s]+?\s+(\w+)\s*(?:<[^>]*>)?\s*\(\s*this\b");

        /// <summary>
        /// The core named the components it served, Toggle, Charges and Cast Range in an ability's spec and its proxy, the
        /// auras and the Respawn's wait in the GAS's core, the toggle in purging, the auras in items and the effect
        /// system, and the components' rules in the Problems view and the ability inspector, so a component of a project's
        /// own couldn't reach as far (EP-37 to EP-41, the user's question of 2026-11-17). It names none: no component or
        /// entity service, by its class, and nothing declared beside a component, its helpers and its shortcuts.
        /// </summary>
        [Test]
        public void EP_41_The_core_names_no_component()
        {
            var package = Path.GetFullPath("Packages/com.faremerald.playforge");
            var components = ComponentNames();
            var named = new HashSet<string>(components);
            Assert.That(components, Has.Member(nameof(ToggleAbilityComponent)).And.Member(nameof(RespawnEntityComponent)), "the components are found");

            // Each file that declares a component: what it declares, and any shortcut it gives
            foreach (var (_, code) in ComponentFiles(package, components))
            {
                foreach (Match declared in DeclaredType.Matches(code)) named.Add(declared.Groups[1].Value);
                foreach (Match shortcut in Extension.Matches(code)) named.Add(shortcut.Groups[1].Value);
            }

            var anyNamed = new Regex($@"\b(?:{string.Join("|", named.Select(Regex.Escape))})\b");
            var found = new List<string>();
            foreach (var part in Core)
            {
                var path = Path.Combine(package, part);
                var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories) : new[] { path };
                foreach (var file in files)
                {
                    Assert.That(File.Exists(file), Is.True, $"the core's {part} is at {file}");
                    var lines = File.ReadAllLines(file);
                    bool inBlock = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var match = anyNamed.Match(CodeOf(lines[i], ref inBlock));
                        if (match.Success) found.Add($"{Path.GetFileName(file)}:{i + 1}: {match.Value}");
                    }
                }
            }
            Assert.That(found, Is.Empty, $"the core names no component, nor what's declared beside one: {string.Join("; ", found)}");
        }

        /// <summary>
        /// The components' shortcuts, extension methods beside them on the types they serve (an ability's charges and their
        /// recharge, the effect it toggles and whether it's on, an entity's auras), were a second way to read a built-in
        /// component that a project's own didn't get. The user chose to drop them before 1.0.0 (EP-45, Decision 90): code
        /// reads a component through <c>Find&lt;T&gt;()</c> or the component itself, and an entity's service through
        /// <c>GetService&lt;T&gt;()</c>. No file that declares a component or a service declares an extension method.
        /// </summary>
        [Test]
        public void EP_45_A_component_gives_no_shortcuts()
        {
            var package = Path.GetFullPath("Packages/com.faremerald.playforge");
            var files = ComponentFiles(package, ComponentNames()).ToList();
            Assert.That(files.Select(file => Path.GetFileName(file.file)), Has.Member("AbilityComponents.cs").And.Member("Auras.cs"),
                "the components' files are found");

            var shortcuts = new List<string>();
            foreach (var (file, code) in files)
                foreach (Match shortcut in Extension.Matches(code)) shortcuts.Add($"{Path.GetFileName(file)}: {shortcut.Groups[1].Value}");
            Assert.That(shortcuts, Is.Empty, $"a component is read through Find<T>() or itself, not a shortcut: {string.Join("; ", shortcuts)}");
        }

        // The runtime's components and entity services, by class name
        private static HashSet<string> ComponentNames()
        {
            var runtime = typeof(Ability).Assembly;
            var service = runtime.GetType("FarEmerald.PlayForge.AbstractEntityService");
            var names = new HashSet<string>();
            foreach (var type in runtime.GetTypes())
            {
                if (type.IsAbstract) continue;
                if (typeof(AbstractForgeComponent).IsAssignableFrom(type) || (service != null && service.IsAssignableFrom(type))) names.Add(type.Name);
            }
            return names;
        }

        // Each runtime file that declares one of the components, with its code
        private static IEnumerable<(string file, string code)> ComponentFiles(string package, HashSet<string> components)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(package, "Runtime"), "*.cs", SearchOption.AllDirectories))
            {
                var code = CodeOfFile(file);
                if (DeclaredType.Matches(code).Cast<Match>().Any(declared => components.Contains(declared.Groups[1].Value)))
                    yield return (file, code);
            }
        }

        // A file's code, without its comments and string literals
        private static string CodeOfFile(string file)
        {
            var code = new StringBuilder();
            bool inBlock = false;
            foreach (var line in File.ReadAllLines(file)) code.AppendLine(CodeOf(line, ref inBlock));
            return code.ToString();
        }
    }
}
