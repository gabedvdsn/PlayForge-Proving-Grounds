using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using SyncSeq = FarEmerald.PlayForge.SyncSequenceTaskLibrary;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The task sequence framework's pass (EP-28, the user's notes of 2026-11-16): its two
    /// libraries hold the same tasks, both wait on the player through ForgeInput, a stage runs a set number of times, and
    /// ForgeInput and ForgeRandom give what a sequence asks of them. A synced runner is stepped here by hand, a step a
    /// frame; PlayMode's SequencePassTests runs the async side.
    /// </summary>
    public class TaskSequenceFrameworkTests
    {
        private const float Frame = 0.1f;

        private static string PackageRoot =>
            PackageInfo.FindForAssembly(typeof(Tag).Assembly)?.resolvedPath?.Replace('\\', '/');

        /// <summary>Input put in for a test, and the project's own put back after.</summary>
        private sealed class Keys : IForgeInput, IDisposable
        {
            private readonly IForgeInput previous;
            public readonly HashSet<KeyCode> Pressed = new();
            public readonly HashSet<KeyCode> Held = new();
            public readonly HashSet<int> Pointers = new();

            public Keys() => previous = ForgeInput.Use(this);
            public void Dispose() => ForgeInput.Use(previous);

            public bool PointerPressed(int button) => Pointers.Contains(button);

            public bool TryGetPointerRay(out Ray ray)
            {
                ray = default;
                return false;
            }

            public bool KeyPressed(KeyCode key) => Pressed.Contains(key);
            public bool KeyHeld(KeyCode key) => Held.Contains(key);
        }

        /// <summary>An input written before KeyHeld was: it says nothing of a held key.</summary>
        private sealed class EarlierInput : IForgeInput
        {
            public bool PointerPressed(int button) => false;

            public bool TryGetPointerRay(out Ray ray)
            {
                ray = default;
                return false;
            }
        }

        // ─── The files ──────────────────────────────────────────────────────────────────────────────────────────────

        // UTF-8 read as Windows-1252 and written back: "═" became "â•", "—" "â€”", "·" "Â·", "−" "âˆ’", "Δ" "Î”"
        private static readonly Regex Garbled = new("â[€•”†ˆ]|Â[·²°±]|Î”|Ã[—©]");

        /// <summary>
        /// The task library's banners and formulas were garbled, a file's UTF-8 read as Windows-1252 and saved back, so its
        /// rules read "â•â•" and its arcs "4Â·HÂ·tÂ·(1âˆ’t)" (EP-28). No file of the package reads so.
        /// </summary>
        [Test]
        public void EP_28_No_file_of_the_package_is_garbled()
        {
            Assume.That(PackageRoot, Is.Not.Null, "PlayForge is a package here");
            var text = new[] { ".cs", ".md", ".uss", ".uxml", ".json", ".asmdef" };
            var garbled = Directory.GetFiles(PackageRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => text.Contains(Path.GetExtension(path)))
                .Where(path => !path.Replace('\\', '/').Substring(PackageRoot.Length).Contains("/."))
                .SelectMany(path => File.ReadAllLines(path).Select((line, at) => (path, line, at)))
                .Where(each => Garbled.IsMatch(each.line))
                .Select(each => $"{Path.GetFileName(each.path)}:{each.at + 1}")
                .ToList();
            Assert.That(garbled, Is.Empty, $"garbled lines: {string.Join(", ", garbled.Take(12))}");
        }

        // ─── The libraries ──────────────────────────────────────────────────────────────────────────────────────────

        // What a run's own token tells, which a synced task has no run to read; and ArcForce, which physics flies between
        // FixedUpdates whatever a synced runner's timing, as ArcTo moves a transform along the same arc, synced
        private static readonly string[] AsyncOnly =
        {
            nameof(SequenceTaskLibrary.IsPaused), nameof(SequenceTaskLibrary.DeltaTime), nameof(SequenceTaskLibrary.ArcForce)
        };

        /// <summary>
        /// The synced library lacked a dozen of the async one's tasks, its paths, its typewriter, its element tweens to a
        /// target, its width and height among them, and the async one lacked the synced WaitUntil and WaitWhile (EP-28).
        /// Each holds what the other does, but for what only a run can do.
        /// </summary>
        [Test]
        public void EP_28_Both_libraries_hold_the_same_tasks()
        {
            var async = Names(typeof(SequenceTaskLibrary)).Except(AsyncOnly).ToList();
            var synced = Names(typeof(SyncSequenceTaskLibrary)).ToList();
            Assert.That(async.Except(synced), Is.Empty, "the synced library has each of the async one's tasks");
            Assert.That(synced.Except(async), Is.Empty, "and the async library each of the synced one's");
        }

        private static IEnumerable<string> Names(Type library) =>
            library.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name).Distinct();

        /// <summary>
        /// The synced library's new tasks finish where the async ones do, a step at a time: a path walked waypoint by
        /// waypoint in its time, a turn to face a transform, text typed a character at a time or all at once when skipped,
        /// an element's width, height, place, opacity and color tweened to a target, and a punch that comes back.
        /// </summary>
        [Test]
        public void EP_28_The_synced_library_s_new_tasks_finish_where_the_async_ones_do()
        {
            var mover = new GameObject("Mover");
            var target = new GameObject("Target");
            try
            {
                var path = new[] { new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 1f) };
                Assert.That(StepsToEnd(SyncSeq.MoveAlongPath(mover.transform, path, 1f)), Is.InRange(9, 12), "a path takes its time");
                Assert.That(Vector3.Distance(mover.transform.position, path[1]), Is.LessThan(0.001f), "and ends at its last waypoint");

                target.transform.position = new Vector3(4f, 0f, 3f);
                mover.transform.position = new Vector3(0f, 0f, 3f);
                StepsToEnd(SyncSeq.MoveAlongPathToTarget(mover.transform, new[] { new Vector3(2f, 0f, 3f) }, target.transform, 10f));
                Assert.That(Vector3.Distance(mover.transform.position, target.transform.position), Is.LessThanOrEqualTo(0.1f),
                    "a path to a target ends at the target");

                mover.transform.rotation = Quaternion.identity;
                target.transform.position = mover.transform.position + Vector3.right;
                StepsToEnd(SyncSeq.LookAt(mover.transform, target.transform, 0.3f));
                Assert.That(Vector3.Angle(mover.transform.forward, Vector3.right), Is.LessThan(0.5f), "a look ends facing the transform");
            }
            finally
            {
                Object.DestroyImmediate(mover);
                Object.DestroyImmediate(target);
            }

            var label = new Label();
            Assert.That(StepsToEnd(SyncSeq.TypeText(label, "Hey.", 0.1f, 1f)), Is.InRange(4, 6), "text types a character a step");
            Assert.AreEqual("Hey.", label.text, "and all of it");
            bool skip = false;
            var skippable = Runner(SyncSeq.TypeTextSkippable(label, "A long line", 0.1f, () => skip));
            var data = SequenceDataPacket.Default();
            Assert.IsFalse(skippable.Step(data, Frame), "typing");
            skip = true;
            Assert.IsTrue(skippable.Step(data, Frame), "a skip ends it at once");
            Assert.AreEqual("A long line", label.text, "with the whole text");

            var box = new VisualElement();
            StepsToEnd(SyncSeq.WidthTo(box, 0f, 120f, 0.3f));
            StepsToEnd(SyncSeq.HeightTo(box, 0f, 40f, 0.3f));
            StepsToEnd(SyncSeq.SlideElementTo(box, new Vector2(10f, 20f), 0.3f));
            StepsToEnd(SyncSeq.FadeElementTo(box, 0.25f, 0.3f));
            StepsToEnd(SyncSeq.ColorElementTo(box, Color.red, 0.3f));
            StepsToEnd(SyncSeq.PunchScaleElement(box, 0.5f, 0.4f));
            Assert.AreEqual(120f, box.style.width.value.value, 0.001f, "the width");
            Assert.AreEqual(40f, box.style.height.value.value, 0.001f, "the height");
            Assert.AreEqual(10f, box.style.translate.value.x.value, 0.001f, "the place");
            Assert.AreEqual(0.25f, box.style.opacity.value, 0.001f, "the opacity");
            Assert.AreEqual(Color.red, box.style.backgroundColor.value, "the color");
            Assert.AreEqual(Vector3.one, box.style.scale.value.value, "a punch comes back");

            Assert.AreEqual(1, StepsToEnd(SyncSeq.Yield()), "a yield is a step");
        }

        // ─── Waiting on the player ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A synced sequence couldn't wait on the player but by a predicate of its own (EP-28): WaitForKey ends on its key
        /// alone, read through ForgeInput, so a test's or a game's own input stands in.
        /// </summary>
        [Test]
        public void EP_28_A_synced_key_wait_ends_on_its_key()
        {
            using var keys = new Keys();
            var runner = Runner(SyncSeq.WaitForKey(KeyCode.Q));
            var data = SequenceDataPacket.Default();
            Assert.IsFalse(runner.Step(data, Frame), "nothing pressed");
            keys.Pressed.Add(KeyCode.E);
            Assert.IsFalse(runner.Step(data, Frame), "another key");
            keys.Pressed.Add(KeyCode.Q);
            Assert.IsTrue(runner.Step(data, Frame), "its key ends it");
        }

        /// <summary>WaitForAnyKey ends on any of its keys, and says which.</summary>
        [Test]
        public void EP_28_A_synced_wait_for_any_key_says_which()
        {
            using var keys = new Keys();
            var heard = KeyCode.None;
            var runner = Runner(SyncSeq.WaitForAnyKey(key => heard = key, KeyCode.Alpha1, KeyCode.Alpha2));
            var data = SequenceDataPacket.Default();
            Assert.IsFalse(runner.Step(data, Frame), "nothing pressed");
            keys.Pressed.Add(KeyCode.Alpha2);
            Assert.IsTrue(runner.Step(data, Frame), "one of its keys ends it");
            Assert.AreEqual(KeyCode.Alpha2, heard, "and it says which");
        }

        /// <summary>WaitForPointer ends on its button: 0 the primary, 1 the secondary.</summary>
        [Test]
        public void EP_28_A_synced_pointer_wait_ends_on_its_button()
        {
            using var keys = new Keys();
            var runner = Runner(SyncSeq.WaitForPointer(1));
            var data = SequenceDataPacket.Default();
            keys.Pointers.Add(0);
            Assert.IsFalse(runner.Step(data, Frame), "the other button");
            keys.Pointers.Add(1);
            Assert.IsTrue(runner.Step(data, Frame), "its button ends it");
        }

        /// <summary>
        /// ForgeInput said whether a key went down, and not whether it's held, so a hold to charge read Unity's input
        /// itself (EP-28). KeyHeld is the input's own, and an input written before it says no key is held.
        /// </summary>
        [Test]
        public void EP_28_An_input_says_whether_a_key_is_held()
        {
            Assert.IsFalse(((IForgeInput)new EarlierInput()).KeyHeld(KeyCode.Space), "an input written before KeyHeld holds nothing");
            using var keys = new Keys();
            keys.Held.Add(KeyCode.Space);
            Assert.IsTrue(ForgeInput.Current.KeyHeld(KeyCode.Space), "the input put in says it's held");
            Assert.IsFalse(ForgeInput.Current.KeyHeld(KeyCode.Return), "and not another");
        }

        /// <summary>
        /// ForgeRandom had no range, so code that wanted one, as a sample's, drew from Unity's Random (EP-28). A range
        /// draws from ForgeRandom's source: a float's between its ends, an int's from the first up to, not with, the
        /// second, as Unity's Random.Range does.
        /// </summary>
        [Test]
        public void EP_28_ForgeRandom_draws_ranges_from_its_source()
        {
            var previous = ForgeRandom.Use(new SeededRandom(7));
            try
            {
                var ints = new HashSet<int>();
                for (int i = 0; i < 500; i++)
                {
                    Assert.That(ForgeRandom.Range(2f, 5f), Is.InRange(2f, 5f), "a float between its ends");
                    ints.Add(ForgeRandom.Range(1, 4));
                }
                Assert.That(ints, Is.EquivalentTo(new[] { 1, 2, 3 }), "an int from the first up to the second");
                Assert.AreEqual(3, ForgeRandom.Range(3, 3), "an empty range gives its start");

                ForgeRandom.Use(new SeededRandom(11));
                float first = ForgeRandom.Range(0f, 10f);
                ForgeRandom.Use(new SeededRandom(11));
                Assert.AreEqual(first, ForgeRandom.Range(0f, 10f), "the source's seed repeats its ranges");
            }
            finally
            {
                ForgeRandom.Use(previous);
            }
        }

        // ─── A stage run a set number of times ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A stage repeated until something broke it, so "three times" needed a predicate on its repeat count (EP-28):
        /// WithRepeat(3) runs it three times in all, and its OnRepeat hears the two repeats, not the last run.
        /// </summary>
        [Test]
        public void EP_28_A_synced_stage_runs_as_many_times_as_it_is_told()
        {
            int runs = 0, repeats = 0;
            var runner = TaskSequenceBuilder.Create("Thrice")
                .Stage(stage => stage.WithRepeat(3).OnRepeat(() => repeats++).Do(_ => runs++))
                .Stage(stage => stage.Do(_ => { }))
                .BuildSyncRunner();
            var data = SequenceDataPacket.Default();
            int steps = 0;
            while (!runner.Step(data, Frame) && ++steps < 20) { }
            Assert.AreEqual(3, runs, "three runs");
            Assert.AreEqual(2, repeats, "two of them repeats");
            Assert.That(steps, Is.LessThan(20), "and the sequence goes on to its end");

            Assert.Throws<ArgumentOutOfRangeException>(() => TaskSequenceBuilder.Create("Never").Stage(stage => stage.WithRepeat(0)),
                "a stage runs at least once");
        }

        // Steps a task in a runner of its own until it ends, a frame's time a step; how many steps it took
        private static int StepsToEnd(ISequenceTask task, int fuse = 500)
        {
            var runner = Runner(task);
            var data = SequenceDataPacket.Default();
            for (int steps = 1; steps <= fuse; steps++)
                if (runner.Step(data, Frame)) return steps;
            Assert.Fail("the task never ended");
            return fuse;
        }

        private static SyncedTaskSequence Runner(ISequenceTask task) =>
            TaskSequenceBuilder.Create("Test").Stage(stage => stage.Task(task)).BuildSyncRunner();
    }
}
