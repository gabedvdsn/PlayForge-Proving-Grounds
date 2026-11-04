using System.Collections;
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
    /// The drawers that pick a [SerializeReference] field's type. A field is drawn with a PropertyField of its own in a
    /// runtime panel, since batch mode has no graphics device to give an editor window one, so its bindings run there.
    /// </summary>
    public class TypeRefDrawerTests
    {
        /// <summary>
        /// A drawer with a default type wrote it into an empty field as it drew, so opening an asset changed it, and a
        /// field whose class Unity couldn't find lost the data Unity kept for it (ED-12). An empty scaler behaviour shows.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_Drawing_an_empty_type_field_leaves_it_empty()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification ??= new GameplayEffectDuration();
            effect.DurationSpecification.DurationOperation ??= new ScalerMagnitudeOperation();
            var scaler = new SimpleScaler();
            scaler.Behaviours.Add(null);
            effect.DurationSpecification.DurationOperation.Scaler = scaler;
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("ED-12") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                var serialized = new SerializedObject(effect);
                var slot = serialized.FindProperty("DurationSpecification.DurationOperation.Scaler.Behaviours.Array.data[0]");
                Assume.That(slot, Is.Not.Null, "the scaler's first behaviour");
                var field = new PropertyField(slot);
                document.rootVisualElement.Add(field);
                field.Bind(serialized);
                for (int i = 0; i < 4; i++) yield return null;

                Assert.IsNull(((SimpleScaler)effect.DurationSpecification.DurationOperation.Scaler).Behaviours[0],
                    "drawing the empty behaviour left it empty");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// An Inject task's injection got its Interrupt default only from the drawer, as it drew. A new task now starts
        /// with it, from the field's initializer, which the type picker runs when it makes a task (ED-12).
        /// </summary>
        [Test]
        public void ED_12_A_new_inject_task_interrupts()
        {
            Assert.IsInstanceOf<InterruptSequenceInjection>(new InjectAbilityTask().Injection);
        }
    }
}
