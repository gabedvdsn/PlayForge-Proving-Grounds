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
    /// Whether a drawer's part is collapsed (ED-18). The drawers kept it in static dictionaries keyed by property path
    /// alone, so it was shared by every asset and lost on a domain reload. Each drawer is built in a runtime panel, since
    /// batch mode has no graphics device to give an editor window one, so its bindings run there.
    /// </summary>
    public class DrawerCollapseTests
    {
        // A runtime panel to draw properties in
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("ED-18") { hideFlags = HideFlags.HideAndDontSave };
            private readonly UIDocument document;

            public Host()
            {
                document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
            }

            public IPanel Panel => document.rootVisualElement?.panel;

            public PropertyField Draw(SerializedObject serialized, string path)
            {
                var property = serialized.FindProperty(path);
                Assert.IsNotNull(property, $"{path} exists");
                var field = new PropertyField(property);
                document.rootVisualElement.Add(field);
                field.Bind(serialized);
                return field;
            }

            public void Clear() => document.rootVisualElement.Clear();

            public void Destroy()
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        // Lets the panel run its bindings and the drawers' scheduled rebuilds
        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        // Presses a button, as a user does
        private static void Press(Button button)
        {
            Assert.IsNotNull(button, "the button is there");
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static Button Find(VisualElement field, string tooltip) =>
            field.Query<Button>().ToList().FirstOrDefault(button => button.tooltip == tooltip);

        // A drawer's own collapse button, the first in it, which reads Expand while the drawer is collapsed
        private static Button CollapseButton(VisualElement field) => field.Query<Button>().ToList()
            .FirstOrDefault(button => button.tooltip == "Expand" || button.tooltip == "Collapse");

        private static bool IsCollapsed(VisualElement field)
        {
            var button = CollapseButton(field);
            Assert.IsNotNull(button, "the drawer has a collapse button");
            return button.tooltip == "Expand";
        }

        /// <summary>
        /// Opening the source requirements on one effect opened them on every effect, since the state was kept by
        /// property path alone.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_18_Opening_a_drawer_on_one_asset_leaves_another_as_it_was()
        {
            var first = ScriptableObject.CreateInstance<GameplayEffect>();
            var second = ScriptableObject.CreateInstance<GameplayEffect>();
            first.SourceRequirements = new EffectTagRequirements();
            second.SourceRequirements = new EffectTagRequirements();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Panel, Is.Not.Null, "the document has a panel");
                const string path = nameof(GameplayEffect.SourceRequirements);
                var firstField = host.Draw(new SerializedObject(first), path);
                var secondSerialized = new SerializedObject(second);
                var secondField = host.Draw(secondSerialized, path);
                yield return Frames();
                var secondWas = IsCollapsed(secondField);

                Press(CollapseButton(firstField));
                yield return Frames();
                Assume.That(IsCollapsed(firstField), Is.Not.EqualTo(secondWas), "the first effect's requirements change");

                host.Clear();
                secondField = host.Draw(secondSerialized, path);
                yield return Frames();
                Assert.AreEqual(secondWas, IsCollapsed(secondField), "the second effect's requirements stay as they were");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        /// <summary>
        /// A worker's collapse state stayed at its place in the list, so a moved worker took the state of the worker
        /// that had been there. It follows the worker now, which a [SerializeReference] field holds as an object.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_18_A_moved_worker_keeps_its_collapse_state()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.WorkerGroup ??= new StandardWorkerGroup();
            ability.WorkerGroup.AnalysisWorkers = new List<AbstractAnalysisWorker>
            {
                new ThresholdAnalysisWorker { Threshold = 1 },
                new ThresholdAnalysisWorker { Threshold = 2 },
            };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(ability);
                const string list = nameof(Ability.WorkerGroup) + "." + nameof(StandardWorkerGroup.AnalysisWorkers);
                string Element(int index) => $"{list}.Array.data[{index}]";

                var top = host.Draw(serialized, Element(0));
                var bottom = host.Draw(serialized, Element(1));
                yield return Frames();
                Assume.That(IsCollapsed(top) || IsCollapsed(bottom), Is.False, "both workers start open");
                Press(CollapseButton(bottom));
                yield return Frames();
                Assume.That(IsCollapsed(bottom), Is.True, "the second worker collapses");

                host.Clear();
                serialized.FindProperty(list).MoveArrayElement(1, 0);
                serialized.ApplyModifiedProperties();
                Assume.That(((ThresholdAnalysisWorker)ability.WorkerGroup.AnalysisWorkers[0]).Threshold, Is.EqualTo(2f),
                    "the second worker moves to the top");
                top = host.Draw(serialized, Element(0));
                bottom = host.Draw(serialized, Element(1));
                yield return Frames();
                Assert.IsTrue(IsCollapsed(top), "the collapsed worker stays collapsed at the top");
                Assert.IsFalse(IsCollapsed(bottom), "and the open one stays open below it");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// Deleting a stage opened every stage of the ability, since the stage drawer could only forget the states it
        /// kept by place. Each later stage keeps its own now.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_18_A_stage_keeps_its_collapse_state_when_one_before_it_is_deleted()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Behaviour ??= new AbilityBehaviour();
            ability.Behaviour.Stages = new List<AbilityTaskBehaviourStage>
            {
                new AbilityTaskBehaviourStage(), new AbilityTaskBehaviourStage(), new AbilityTaskBehaviourStage(),
            };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(ability);
                string Stage(int index) =>
                    $"{nameof(Ability.Behaviour)}.{nameof(AbilityBehaviour.Stages)}.Array.data[{index}]";

                var stages = Enumerable.Range(0, 3).Select(index => host.Draw(serialized, Stage(index))).ToList();
                yield return Frames();
                Assume.That(stages.Any(IsCollapsed), Is.False, "the stages start open");
                Press(CollapseButton(stages[1]));
                yield return Frames();
                Assume.That(IsCollapsed(stages[1]), Is.True, "the second stage collapses");

                Press(Find(stages[0], "Delete this stage"));
                host.Clear();
                Assume.That(ability.Behaviour.Stages.Count, Is.EqualTo(2), "the first stage is deleted");
                var first = host.Draw(serialized, Stage(0));
                var second = host.Draw(serialized, Stage(1));
                yield return Frames();
                Assert.IsTrue(IsCollapsed(first), "the collapsed stage, now first, stays collapsed");
                Assert.IsFalse(IsCollapsed(second), "and the open one after it stays open");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
            }
        }
    }
}
