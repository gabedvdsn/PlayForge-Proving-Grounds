using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The worker group and the sequence reference drawers (ED-57, the user's report). A worker group was a box of its
    /// own, with a colored border, colored count badges, a type tag and an Import of its own, from 90 styles set in C#,
    /// inside a Workers section whose header already counts and imports them. A sequence reference was an IMGUI popup.
    /// </summary>
    public class WorkerGroupDrawerTests
    {
        /// <summary>An asset with a worker group and a sequence reference, as the drawers draw them.</summary>
        public sealed class Holder : ScriptableObject
        {
            public StandardWorkerGroup Workers = new();
            public TaskSequenceReference Sequence;
        }

        // A sequence the test project offers, as a [TaskSequenceMethod] does; the drawer only names it
        [TaskSequenceMethod("ED-57 Sequence")]
        private static TaskSequence Ed57Sequence() => null;

        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("ED-57") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public PropertyField Draw(SerializedObject serialized, string path)
            {
                var field = new PropertyField(serialized.FindProperty(path));
                Document.rootVisualElement.Add(field);
                field.Bind(serialized);
                return field;
            }

            public void Destroy()
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        private static IEnumerator Until(Func<bool> done, double seconds = 1.0)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (!done() && EditorApplication.timeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// A worker group draws no Name: nothing reads it, and the Workers section it sits in names it (FN-4).
        /// </summary>
        [UnityTest]
        public IEnumerator FN_4_A_worker_group_draws_no_name()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(Holder.Workers));
                yield return Until(() => field.Q<PropertyField>("AttributeWorkers") != null);
                yield return Frames();

                Assume.That(field.Q<PropertyField>("AttributeWorkers"), Is.Not.Null, "the group is drawn");
                Assert.IsNull(field.Q<TextField>("WorkerGroupName"), "with no Name");
                Assert.That(field.Query<TextField>().ToList().Where(each => each.label == "Name"), Is.Empty, "nor any field called that");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// A worker group is its four lists of workers, Unity's reorderable lists, where a new worker starts empty for its
        /// kind to be picked; and it sets no look in code (ED-57). Its name, which nothing shows, it no longer draws (FN-4).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_57_A_worker_group_is_its_four_lists()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(Holder.Workers));
                yield return Until(() => field.Q<PropertyField>("AttributeWorkers")?.Q<Button>("unity-list-view__add-button") != null);
                yield return Frames();

                foreach (var list in new[] { "AttributeWorkers", "ImpactWorkers", "TagWorkers", "AnalysisWorkers" })
                    Assert.IsNotNull(field.Q<PropertyField>(list)?.Q<ListView>(), $"{list}, Unity's list");
                Assert.IsNull(field.Q(name: "WorkerGroupContainer"), "and no box of its own");

                var add = field.Q<PropertyField>("AttributeWorkers").Q<Button>("unity-list-view__add-button");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = add;
                    add.SendEvent(submit);
                }
                yield return Frames();
                Assert.AreEqual(1, holder.Workers.AttributeWorkers.Count, "the list adds a worker");
                Assert.IsNull(holder.Workers.AttributeWorkers[0], "which starts empty");

                var wrong = field.Query<VisualElement>().ToList()
                    .Where(each => each.GetFirstAncestorOfType<PropertyField>() == field)
                    .SelectMany(each => ForgeInspectorTests.LookSetInCode(each).Select(style => $"{each.GetType().Name} \"{(each as TextElement)?.text ?? each.name}\" sets {style}"))
                    .ToList();
                Assert.That(wrong, Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        // Every name under an item, its submenus' among them
        private static IEnumerable<string> Names(UnityEditor.IMGUI.Controls.AdvancedDropdownItem item) =>
            item.children.SelectMany(child => new[] { child.name }.Concat(Names(child)));

        /// <summary>
        /// A sequence reference is a field as a kind field is, the sequence's name in its box, which opens a searchable
        /// dropdown of the project's sequences, None first; a pick is one Undo step (ED-57, FN-24). It was Unity's popup, one
        /// long list, and an IMGUI popup before that, which wrote "(None)".
        /// </summary>
        [UnityTest]
        public IEnumerator FN_24_A_sequence_reference_picks_from_a_searchable_dropdown()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(holder);
                var field = host.Draw(serialized, nameof(Holder.Sequence));
                yield return Until(() => field.Q(name: "Sequence") != null);
                yield return Frames();

                Assert.IsNull(field.Q<PopupField<string>>("Sequence"), "no popup");
                Assert.AreEqual("None", field.Q(name: "Sequence")?.Q<Label>("TypeName")?.text, "an empty one reads None in its box");

                var drawer = typeof(FarEmerald.PlayForge.Extended.Editor.TaskSequenceReferenceDrawer);
                var picker = drawer.Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeSequencePicker");
                Assert.IsNotNull(picker, "it opens a dropdown of its own");
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var entries = drawer.GetMethod("Entries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
                var dropdown = Activator.CreateInstance(picker, flags, null, new[] { entries, null }, null);
                var root = (UnityEditor.IMGUI.Controls.AdvancedDropdownItem)picker.GetMethod("BuildRoot", flags).Invoke(dropdown, null);
                Assert.AreEqual("None", root.children.First().name, "None first");
                Assert.That(Names(root), Has.Member("ED-57 Sequence"), "it offers the project's sequences");

                var pick = drawer.GetMethod("Pick", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                Assert.IsNotNull(pick, "a pick is the drawer's to make");
                var sequence = typeof(WorkerGroupDrawerTests).GetMethod(nameof(Ed57Sequence), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Undo.IncrementCurrentGroup();
                pick.Invoke(null, new object[] { serialized.FindProperty(nameof(Holder.Sequence)), sequence });
                yield return Frames();
                Assert.IsTrue(holder.Sequence.IsValid, "a pick names the sequence");
                Assert.AreEqual("ED-57 Sequence", field.Q(name: "Sequence")?.Q<Label>("TypeName")?.text, "which its box shows");
                Undo.PerformUndo();
                Assert.IsFalse(holder.Sequence.IsValid, "and one Undo takes it back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }
    }
}
