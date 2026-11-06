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
        /// A worker group is its name and its four lists of workers, Unity's reorderable lists, where a new worker starts
        /// empty for its kind to be picked; and it sets no look in code (ED-57).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_57_A_worker_group_is_its_name_and_four_lists()
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

                Assert.IsNotNull(field.Q<TextField>("WorkerGroupName"), "its name");
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

        /// <summary>
        /// A sequence reference is Unity's popup of the project's sequences, None first; a pick is one Undo step (ED-57).
        /// It was an IMGUI popup, which wrote "(None)".
        /// </summary>
        [UnityTest]
        public IEnumerator ED_57_A_sequence_reference_is_Unitys_popup()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(Holder.Sequence));
                yield return Until(() => field.Q<PopupField<string>>("Sequence") != null);
                yield return Frames();

                var popup = field.Q<PopupField<string>>("Sequence");
                Assert.IsNotNull(popup, "the reference is Unity's popup");
                Assert.AreEqual("None", popup.value, "an empty one reads None");
                Assert.That(popup.choices, Has.Member("ED-57 Sequence"), "it offers the project's sequences");

                Undo.IncrementCurrentGroup();
                popup.value = "ED-57 Sequence";
                yield return Frames();
                Assert.IsTrue(holder.Sequence.IsValid, "a pick names the sequence");
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
