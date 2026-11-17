using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The Task Sequence Examples sample, as the user's notes of 2026-11-16 asked for it (EP-29): named for what it shows,
    /// task sequences, and showing every part of them. Read from the package's sources, as the sample's assembly loads only
    /// where it's imported.
    /// </summary>
    public class TaskSequenceExamplesTests
    {
        private const string Sample = "Samples~/Task Sequence Examples";

        private static string PackageRoot =>
            PackageInfo.FindForAssembly(typeof(Tag).Assembly)?.resolvedPath?.Replace('\\', '/');

        // Each type whose every public member, of its own, the sample uses; and what of them a reader never calls, the
        // plumbing a run calls itself
        private static readonly Type[] Whole =
        {
            typeof(TaskSequenceBuilder), typeof(StageBuilder), typeof(BranchBuilder), typeof(TaskSequence),
            typeof(TaskSequenceChain), typeof(SyncedTaskSequence), typeof(SequenceTaskLibrary),
            typeof(SyncSequenceTaskLibrary), typeof(SequenceEventContext)
        };

        private static readonly Dictionary<Type, string[]> Chosen = new()
        {
            [typeof(SequenceDataPacket)] = new[]
            {
                "Runtime", "Chain", "Interrupt", "SkipStage", "SkipAndMaintain", "JumpToStage", "RestartSequence", "Inject",
                "InjectStageLocal", "CheckInjectConditions", "BreakStageRepeat", "InterruptChain", "SkipSequence",
                "SetStageTimeout", "Default", "SceneRoot", "SceneLocal"
            },
            [typeof(TaskSequenceRuntime)] = new[]
            {
                "Pause", "Resume", "DataOfRun", "IsPausedRun", "Delay", "GetRepeatCount", "IsRepeatBroken", "Inject",
                "InjectStageLocal", "CheckConditions", "RequestStartStageTimeout", "StageIndex", "CurrentStage", "IsRunning",
                "CompletedSuccessfully", "FailureException", "ElapsedTime", "IsPaused", "Clock", "ClockOfRun"
            },
        };

        // Each kind of task, policy and injection, and the rest a reader names
        private static readonly Type[] Named =
        {
            typeof(SequenceTaskBase), typeof(SyncSequenceTaskBase), typeof(DelegateSequenceTask), typeof(MainThreadSequenceTask),
            typeof(DelegateSyncTask), typeof(ISequenceStagePolicy), typeof(WhenAllStagePolicy), typeof(WhenAnyStagePolicy),
            typeof(WhenNStagePolicy), typeof(RepeatUntilSkippedPolicy), typeof(ISequenceInjection),
            typeof(DelegateSequenceInjection), typeof(InterruptSequenceInjection), typeof(SkipStageInjection),
            typeof(SkipAndMaintainInjection), typeof(StopMaintainedLastInjection), typeof(StopMaintainedAtIndexInjection),
            typeof(StopMaintainedAllInjection), typeof(JumpToStageInjection), typeof(BreakStageRepeatInjection),
            typeof(ChainInterruptInjection), typeof(TaskSequenceProcess), typeof(TaskSequenceReference),
            typeof(SequenceTimeout), typeof(UnscaledClock), typeof(ScaledClock)
        };

        /// <summary>
        /// The sample shows every part of the task sequence framework (EP-29, the user's "exemplify usage of all task sequence
        /// content"): each public member of the builders, a sequence and its chain, the synced runner, both libraries and an
        /// event's context is used in its code, and the data packet's and the runtime's controls; each kind of task,
        /// policy and injection is named, and [TaskSequenceMethod] marks what an ability's dropdown offers.
        /// </summary>
        [Test]
        public void EP_29_The_task_sequence_examples_use_every_part_of_the_framework()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var folder = Path.Combine(PackageRoot, Sample);
            Assert.That(Directory.Exists(folder), $"the sample is at {Sample}");
            var files = Directory.GetFiles(folder, "*.cs");
            Assert.That(files, Is.Not.Empty, "with its code");
            string code = string.Join("\n", files.Select(path => CodeOf(File.ReadAllText(path))));

            var missing = new List<string>();
            foreach (var type in Whole)
                missing.AddRange(MembersOf(type).Where(name => !Uses(code, name)).Select(name => $"{type.Name}.{name}"));
            foreach (var (type, names) in Chosen)
                missing.AddRange(names.Where(name => !Uses(code, name)).Select(name => $"{type.Name}.{name}"));
            missing.AddRange(Named.Where(type => !Uses(code, type.Name)).Select(type => type.Name));
            if (!Regex.IsMatch(code, @"\[TaskSequenceMethod\b")) missing.Add("[TaskSequenceMethod]");

            Assert.That(missing, Is.Empty, $"{missing.Count} parts the sample doesn't use: {string.Join(", ", missing)}");
        }

        /// <summary>
        /// The sample reads Unity's console, input, random numbers and time only through PlayForge's own, as the user asked
        /// of it: ForgeLog, ForgeInput, ForgeRandom and ForgeTime (EP-29). It wrote to the console straight in 348 places and
        /// read Unity's input, Random and Time in 107 more.
        /// </summary>
        [Test]
        public void EP_29_The_task_sequence_examples_read_through_PlayForge()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var unity = new Regex(@"(?<![\w.])(?:UnityEngine\.)?(?:Debug\.Log\w*|Input\.\w+|Camera\.main|Random\.\w+|Time\.\w+)\b");
            var folder = Path.Combine(PackageRoot, Sample);
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.cs") : new string[0];
            // A folder of nothing reads nothing of Unity's, so the sample's code is there first
            Assert.That(files, Is.Not.Empty, $"the sample's code is at {Sample}");
            var reads = files
                .SelectMany(path => CodeOf(File.ReadAllText(path)).Split('\n')
                    .Select((line, at) => (path, line, at)))
                .Where(each => unity.IsMatch(each.line))
                .Select(each => $"{Path.GetFileName(each.path)}:{each.at + 1}: {unity.Match(each.line).Value}")
                .ToList();
            Assert.That(reads, Is.Empty, $"Unity's own read: {string.Join(", ", reads.Take(12))}");
        }

        private static IEnumerable<string> MembersOf(Type type) =>
            type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(member => member switch
                {
                    MethodInfo method => !method.IsSpecialName,
                    PropertyInfo or FieldInfo or EventInfo => true,
                    _ => false
                })
                .Select(member => member.Name)
                .Distinct();

        private static bool Uses(string code, string name) => Regex.IsMatch(code, $@"\b{Regex.Escape(name)}\b");

        // The code, its comments blanked and its lines kept: a member named only in a comment isn't used. A string's text
        // stays, as an interpolated string's holes are code
        private static string CodeOf(string text)
        {
            var code = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/' && (i == 0 || text[i - 1] != ':'))
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    if (i < text.Length) code.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    for (i += 2; i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'); i++)
                        if (text[i] == '\n') code.Append('\n');
                    i++;
                    continue;
                }
                code.Append(c);
            }
            return code.ToString();
        }
    }
}
