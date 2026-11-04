using System.Collections;
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
    /// The editor's views follow what they show instead of polling (ED-11). A view is built in a runtime panel, since
    /// batch mode has no graphics device to give an editor window one, so its bindings run there.
    /// </summary>
    public class EditorPollingTests
    {
        // Lets the panel run its bindings until the condition holds, or a second passes
        private static IEnumerator Until(System.Func<bool> condition)
        {
            var giveUp = EditorApplication.timeSinceStartup + 1.0;
            while (!condition() && EditorApplication.timeSinceStartup < giveUp) yield return null;
        }

        /// <summary>
        /// Every asset inspector listened to the editor's update every frame of its life, to catch an object picker's
        /// choice (ED-11). It listens only while a picker is open.
        /// </summary>
        [Test]
        public void ED_11_An_idle_inspector_listens_to_no_update()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            var editor = UnityEditor.Editor.CreateEditor(effect);
            try
            {
                Assume.That(editor.CreateInspectorGUI(), Is.Not.Null, "the inspector builds");
                var listeners = EditorApplication.update?.GetInvocationList() ?? new System.Delegate[0];
                Assert.IsEmpty(listeners.Where(listener => listener.Target is FarEmerald.PlayForge.Extended.Editor.BasePlayForgeEditor),
                    "no inspector listens while no picker is open");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// The asset header stayed put while the content scrolled under it. The inspector hid the Inspector's own scroll
        /// view and gave its content one of its own, sized by measuring the window on loops that rescheduled themselves
        /// every 200 and 300 ms for as long as it stayed open (ED-11). The header scrolls with the content now (Q17), so
        /// the inspector keeps no scroll view of its own and measures nothing.
        /// </summary>
        [Test]
        public void ED_11_The_asset_header_scrolls_with_the_content()
        {
            var attribute = ScriptableObject.CreateInstance<Attribute>();
            var editor = UnityEditor.Editor.CreateEditor(attribute);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assume.That(root, Is.Not.Null, "the inspector builds");
                Assert.IsEmpty(root.Children().OfType<ScrollView>(), "the inspector keeps no scroll view of its own");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(attribute);
            }
        }

        /// <summary>
        /// The control for removing the Impact drawer's 250 ms timer: its Multiply Behaviour still shows for a Multiply
        /// impact alone, and follows a change to the operation, an Undo among them.
        /// </summary>
        [UnityTest]
        public IEnumerator The_impact_drawer_follows_its_operation()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.ImpactSpecification ??= new GameplayEffectImpact();
            effect.ImpactSpecification.ImpactOperation = ECalculationOperation.Add;
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("ED-11") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                var serialized = new SerializedObject(effect);
                var impact = new PropertyField(serialized.FindProperty(nameof(GameplayEffect.ImpactSpecification)));
                document.rootVisualElement.Add(impact);
                impact.Bind(serialized);
                for (int i = 0; i < 4; i++) yield return null;

                VisualElement Multiply() => impact.Query<PropertyField>().ToList()
                    .FirstOrDefault(field => field.bindingPath.EndsWith(nameof(GameplayEffectImpact.MultiplyBehaviour)));
                Assume.That(Multiply(), Is.Not.Null, "the drawer has Multiply Behaviour");
                Assert.AreEqual(DisplayStyle.None, Multiply().resolvedStyle.display, "an Add impact hides it");

                Undo.IncrementCurrentGroup();
                var operation = serialized.FindProperty(
                    $"{nameof(GameplayEffect.ImpactSpecification)}.{nameof(GameplayEffectImpact.ImpactOperation)}");
                operation.enumValueIndex = (int)ECalculationOperation.Multiply;
                serialized.ApplyModifiedProperties();
                yield return Until(() => Multiply().resolvedStyle.display == DisplayStyle.Flex);
                Assert.AreEqual(DisplayStyle.Flex, Multiply().resolvedStyle.display, "a Multiply impact shows it");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                yield return Until(() => Multiply().resolvedStyle.display == DisplayStyle.None);
                Assert.AreEqual(DisplayStyle.None, Multiply().resolvedStyle.display, "and an Undo hides it again");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(effect);
            }
        }
    }
}
