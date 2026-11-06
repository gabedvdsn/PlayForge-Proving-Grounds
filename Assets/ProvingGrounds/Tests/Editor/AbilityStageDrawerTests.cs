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
    /// An ability's stage (ED-64, split from ED-56, the user's report): it folds as an attribute set element does, to a
    /// line of what it holds; its tasks are Unity's reorderable list, where a new task starts empty; and its condition
    /// shows where it looks for its tag only once it has one. It drew a colored badge for each stage and fact, ▶ and ▼
    /// as text, and a list of its own with ▲, ▼ and × buttons, from styles set in C#.
    /// </summary>
    public class AbilityStageDrawerTests
    {
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("ED-64") { hideFlags = HideFlags.HideAndDontSave };
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

        private const string StagePath = "Behaviour.Stages.Array.data[0]";

        // An ability of one stage: a delay and a channel, which applies its usage effects
        private static Ability TwoTaskAbility()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "Staged";
            ability.Behaviour.Stages = new List<AbilityTaskBehaviourStage>
            {
                new()
                {
                    Tasks = new List<AbstractAbilityTask> { new DelayTask { DelayMilliseconds = 200 }, new ChannelAbilityTask { ChannelDuration = 1f } },
                    ApplyUsageEffects = true,
                },
            };
            return ability;
        }

        /// <summary>
        /// A stage folds to one line of what it holds, its tasks are Unity's list, and it sets no look in code (ED-64).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_64_A_stage_folds_to_a_line_with_its_tasks_in_Unitys_list()
        {
            var ability = TwoTaskAbility();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(ability), StagePath);
                yield return Until(() => field.Q<Label>("StageTitle") != null);
                yield return Frames();

                Assert.AreEqual("Stage 1", field.Q<Label>("StageTitle")?.text, "the stage is named by its place");
                Assert.AreEqual("2 tasks · All · Applies usage effects", field.Q<Label>("Summary")?.text, "and folds to a line of what it holds");
                Assert.IsNotNull(field.Q<PropertyField>("Tasks")?.Q<ListView>(), "its tasks are Unity's list");
                Assert.IsNull(field.Q<Button>(className: "pf-stage__delete"), "which removes a stage, not a button of its own");

                var wrong = field.Query<VisualElement>().ToList()
                    .Where(each => each.GetFirstAncestorOfType<PropertyField>() == field)
                    .SelectMany(each => ForgeInspectorTests.LookSetInCode(each).Select(style => $"{each.GetType().Name} \"{(each as TextElement)?.text ?? each.name}\" sets {style}"))
                    .ToList();
                Assert.That(wrong, Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// A task the list adds starts empty, for its kind to be picked. Grown as an array, a list of objects a field holds
        /// by reference gave the new element the last one's object, so the two tasks were one (ED-64).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_64_A_new_task_starts_empty()
        {
            var ability = TwoTaskAbility();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(ability), StagePath);
                yield return Until(() => field.Q<PropertyField>("Tasks")?.Q<Button>("unity-list-view__add-button") != null);
                yield return Frames();

                var add = field.Q<PropertyField>("Tasks").Q<Button>("unity-list-view__add-button");
                Assert.IsNotNull(add, "the list adds a task");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = add;
                    add.SendEvent(submit);
                }
                yield return Frames();

                var tasks = ability.Behaviour.Stages[0].Tasks;
                Assert.AreEqual(3, tasks.Count, "a third task");
                Assert.IsNull(tasks[2], "which starts empty");
                Assert.IsInstanceOf<ChannelAbilityTask>(tasks[1], "and the one before keeps its own");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// A stage's condition shows where it looks for its tag, and whether it runs while the tag is missing, only once it
        /// names a tag: with none, the stage always runs, and Unity's foldout showed all three (ED-64).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_64_A_condition_shows_its_subject_once_it_has_a_tag()
        {
            var ability = TwoTaskAbility();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(ability);
                var field = host.Draw(serialized, $"{StagePath}.{nameof(AbilityTaskBehaviourStage.Condition)}");
                yield return Until(() => field.Q(name: "ConditionOn") != null);
                yield return Frames();

                var on = field.Q(name: "ConditionOn");
                var missing = field.Q(name: "ConditionWhenMissing");
                Assert.IsNotNull(on, "the condition has where it looks");
                Assert.IsTrue(on.ClassListContains("pf-hidden"), "hidden while it names no tag");
                Assert.IsTrue(missing.ClassListContains("pf-hidden"), "as is When Missing");

                var tag = serialized.FindProperty($"{StagePath}.{nameof(AbilityTaskBehaviourStage.Condition)}.{nameof(AbilityStageCondition.Tag)}");
                tag.FindPropertyRelative(nameof(Tag.Name)).stringValue = "Ability.Hit";
                serialized.ApplyModifiedProperties();
                yield return Frames();
                Assert.IsFalse(on.ClassListContains("pf-hidden"), "with a tag, it shows where it looks");
                Assert.IsFalse(missing.ClassListContains("pf-hidden"), "and When Missing");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
            }
        }
    }
}
