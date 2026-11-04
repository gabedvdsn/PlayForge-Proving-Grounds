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
    /// The scaler drawer, built inside the inspector of the asset that holds the scaler. The inspector is added whole to a
    /// runtime panel, since batch mode has no graphics device to give an editor window one, so its bindings run there.
    /// </summary>
    public class ScalerDrawerTests
    {
        // A runtime panel to build an inspector in, and the inspector itself
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("ED-34") { hideFlags = HideFlags.HideAndDontSave };
            private UnityEditor.Editor editor;

            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Inspect(Object target)
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                Document.rootVisualElement.Add(root);
                // The Inspector binds what an editor builds to its SerializedObject
                root.Bind(editor.serializedObject);
                return root;
            }

            public void Destroy()
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        // Lets the panel run its bindings and scheduled rebuilds, which the editor's updates drive
        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        // Presses every scaler's Expand button, as a user opens them, and says how many it pressed
        private static int ExpandScalers(VisualElement root)
        {
            var buttons = root.Query<Button>().ToList().Where(button => button.tooltip == "Expand").ToList();
            foreach (var button in buttons)
            {
                using var submit = NavigationSubmitEvent.GetPooled();
                submit.target = button;
                button.SendEvent(submit);
            }
            return buttons.Count;
        }

        /// <summary>
        /// Opening a scaler wrote to it while drawing. On an effect with no level link, Lock mode cut its per-level table
        /// to one value; the other modes resized the table to Max Level, and a Max Level of 0 or less became 10.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_34_Opening_a_scaler_leaves_its_table_alone()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification ??= new GameplayEffectDuration();
            effect.DurationSpecification.DurationOperation ??= new ScalerMagnitudeOperation();
            effect.DurationSpecification.DurationPolicy = EEffectDurationPolicy.Durational;
            effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler
            {
                Configuration = ELevelConfig.LockToLevelProvider, MaxLevel = 5, LevelValues = new[] { 1f, 2f, 3f, 4f, 5f },
            };
            effect.DurationSpecification.DeltaTimeScaler = new SimpleScaler
            {
                Configuration = ELevelConfig.Unlocked, MaxLevel = 0, LevelValues = new[] { 2f, 4f, 6f },
            };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(effect);
                yield return Frames();

                int opened = 0;
                for (int round = 0; round < 3; round++)
                {
                    opened += ExpandScalers(root);
                    yield return Frames();
                }
                Assume.That(opened, Is.GreaterThanOrEqualTo(2), "both scalers were opened");

                var locked = effect.DurationSpecification.DurationOperation.Scaler;
                Assert.AreEqual(new[] { 1f, 2f, 3f, 4f, 5f }, locked.LevelValues, "Lock mode with no level link keeps its table");
                Assert.AreEqual(5, locked.MaxLevel, "and its Max Level");
                var unlocked = effect.DurationSpecification.DeltaTimeScaler;
                Assert.AreEqual(new[] { 2f, 4f, 6f }, unlocked.LevelValues, "an unlocked table keeps its own length");
                Assert.AreEqual(0, unlocked.MaxLevel, "and a Max Level of 0 stays 0");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// Building the drawer cloned a scaler that shared its reference id with another list element's, so drawing an
        /// attribute set changed it. The drawer now leaves the shared scaler alone and says so in place. The test draws
        /// the second element's scaler on its own, since a list draws its elements only as a laid-out view needs them.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_34_Opening_a_shared_scaler_leaves_it_shared()
        {
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            var shared = new CachedSimpleScaler();
            set.Attributes.Add(new AttributeSetElement { Current = new AttributeMagnitudeSpec { Scaling = shared } });
            set.Attributes.Add(new AttributeSetElement { Current = new AttributeMagnitudeSpec { Scaling = shared } });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(set);
                var scaling = serialized.FindProperty("Attributes.Array.data[1].Current.Scaling");
                Assume.That(scaling, Is.Not.Null, "the second element's scaler");
                Assume.That(scaling.managedReferenceId,
                    Is.EqualTo(serialized.FindProperty("Attributes.Array.data[0].Current.Scaling").managedReferenceId),
                    "shares the first one's reference id");
                var root = new PropertyField(scaling);
                host.Document.rootVisualElement.Add(root);
                root.Bind(serialized);
                yield return Frames();
                for (int round = 0; round < 3; round++)
                {
                    ExpandScalers(root);
                    yield return Frames();
                }

                Assert.AreSame(set.Attributes[0].Current.Scaling, set.Attributes[1].Current.Scaling,
                    "the two elements still share one scaler");
                var notes = root.Query<HelpBox>().ToList().Where(box => box.text.Contains("same scaler")).ToList();
                Assert.IsNotEmpty(notes, "and the drawer says so");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(set);
            }
        }

        /// <summary>
        /// Every scaler spread its values over a level range (Decision 28). A scaler the drawer makes maps levels exactly,
        /// and its Stretch Over Levels shows it, which a scaler saved before the option keeps on (ED-33).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_A_new_scaler_maps_levels_exactly()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification = new GameplayEffectDuration { DurationOperation = new ScalerMagnitudeOperation() };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(effect);
                var field = new PropertyField(serialized.FindProperty("DurationSpecification.DurationOperation.Scaler"));
                host.Document.rootVisualElement.Add(field);
                field.Bind(serialized);
                yield return Frames();

                var kind = field.Query<PopupField<string>>().ToList().FirstOrDefault();
                Assert.IsNotNull(kind, "the drawer offers a kind");
                kind.value = kind.choices.First(choice => choice.Trim() == "Simple");
                yield return Frames();
                var made = effect.DurationSpecification.DurationOperation.Scaler as SimpleScaler;
                Assert.IsNotNull(made, "choosing Simple makes a simple scaler");
                Assert.IsFalse(made.StretchOverLevels, "which maps levels exactly");

                ExpandScalers(field);
                yield return Frames();
                Assert.IsNotNull(field.Query<Toggle>().ToList().FirstOrDefault(toggle => toggle.label == "Stretch Over Levels"),
                    "and its Stretch Over Levels says so");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }
    }
}
