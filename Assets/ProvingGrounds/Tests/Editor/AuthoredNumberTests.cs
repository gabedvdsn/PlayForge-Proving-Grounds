using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
    /// Every authored number is Scalable (FN-19, Decision 83): an ability's buffer window, an effect's stack limits and its
    /// changes' stacks, a cue's, an aura's, an item's, a worker's, an attribute's and an attribute set's, and an ability
    /// rule's value. Each starts with the number it had, one made in code too, and a folded field's line shows it.
    /// </summary>
    public class AuthoredNumberTests
    {
        private static readonly Assembly Runtime = typeof(GameplayEffect).Assembly;

        // Each field, whether it's a whole number, and what one made in code starts with (NaN: a struct's, which has none)
        private static readonly (string Type, string Field, bool Whole, float Starts)[] Numbers =
        {
            ("AbilityDefinition", "BufferWindow", false, 0.3f),
            ("Attribute", "DecayPerSecond", false, 0f),
            ("StackBoundsConfig", "Min", true, 0f),
            ("StackBoundsConfig", "Max", true, 0f),
            ("InitializationOperation", "InitializationStacks", true, 0f),
            ("StacksChange", "MostStacks", true, 0f),
            ("StacksChange", "StacksPerLanding", true, 0f),
            ("CameraShakeCue", "Strength", false, 1f),
            ("CameraShakeCue", "Duration", false, 0.2f),
            ("PrefabCue", "Lifetime", false, 2f),
            ("SoundCue", "Volume", false, 1f),
            ("AuraEntry", "Interval", false, 0.25f),
            ("ItemDefinition", "MaxStackSize", true, 0f),
            ("SetBonusItemComponent", "Pieces", true, 2f),
            ("ThresholdAnalysisWorker", "Threshold", false, 0f),
            ("CantFallBelowAttributeWorker", "Floor", false, 1f),
            ("ReturnDamageAttributeWorker", "Share", false, 1f),
            ("AbstractRelativeAttributeWorker", "RelativeMultiplier", false, 1f),
            ("AttributeChangeEffectWorker", "EveryTicks", true, 0f),
            ("AbstractTagWorker", "TickPause", true, -1f),
            ("TagWorkerRequirementPacket", "RequiredWeight", true, float.NaN),
            ("AttributeConstraints", "SnapInterval", false, 0f),
            ("AttributeValidation", "Value", false, 0f),
        };

        private static Type Find(string name)
        {
            var type = Runtime.GetTypes().FirstOrDefault(each => each.Name == name && each.Namespace != null &&
                                                                 each.Namespace.StartsWith("FarEmerald.PlayForge"));
            Assert.IsNotNull(type, $"PlayForge has {name}");
            return type;
        }

        // A kind of its own to make, for a field an abstract class holds: the first PlayForge kind made from it
        private static Type Made(Type type) => !type.IsAbstract ? type
            : Runtime.GetTypes().FirstOrDefault(each => !each.IsAbstract && type.IsAssignableFrom(each) && each.GetConstructor(Type.EmptyTypes) != null);

        [Test]
        public void FN_19_Every_authored_number_is_Scalable()
        {
            foreach (var (typeName, fieldName, whole, _) in Numbers)
            {
                var field = Find(typeName).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
                Assert.IsNotNull(field, $"{typeName} has {fieldName}");
                Assert.AreEqual(whole ? typeof(ScalableInt) : typeof(ScalableFloat), field.FieldType,
                    $"{typeName}.{fieldName} is a Scalable {(whole ? "whole number" : "number")}");
            }
        }

        /// <summary>
        /// One made in code starts with the number it had, a plain one: an area's Max Targets had none, so one made in code
        /// threw as it landed (FN-19a).
        /// </summary>
        [Test]
        public void FN_19_Each_made_in_code_starts_with_its_number()
        {
            foreach (var (typeName, fieldName, _, starts) in Numbers)
            {
                if (float.IsNaN(starts)) continue;
                var type = Made(Find(typeName));
                if (type == null) continue;
                object made = typeof(ScriptableObject).IsAssignableFrom(type) ? ScriptableObject.CreateInstance(type) : Activator.CreateInstance(type);
                try
                {
                    var value = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(made);
                    float? constant = value switch
                    {
                        ScalableFloat number when number.IsConstant => number.Constant,
                        ScalableInt number when number.IsConstant => number.Constant,
                        _ => null,
                    };
                    Assert.IsNotNull(constant, $"{type.Name}.{fieldName} made in code is a plain Scalable number, not {value ?? "none"}");
                    Assert.AreEqual(starts, constant.Value, 0.0001f, $"{type.Name}.{fieldName} starts at {starts}");
                }
                finally
                {
                    if (made is Object asset) Object.DestroyImmediate(asset);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // A folded field's line
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>An asset with a field of a kind the generic drawers fold: an item component.</summary>
        public sealed class ComponentHolder : ScriptableObject
        {
            [SerializeReference] public AbstractItemComponent Component;
        }

        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("FN-19") { hideFlags = HideFlags.HideAndDontSave };
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

        private static IEnumerator Until(Func<bool> done, double seconds = 1.0)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (!done() && EditorApplication.timeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// A folded field's line showed its plain numbers alone, so a Scalable one, as a Uses component's Uses, was missing
        /// from it, and every number FN-19 made Scalable would have gone from theirs (FN-19). It reads as the number's own
        /// summary does: "Uses 3".
        /// </summary>
        [UnityTest]
        public IEnumerator FN_19_A_folded_line_shows_its_Scalable_numbers()
        {
            var holder = ScriptableObject.CreateInstance<ComponentHolder>();
            holder.Component = new UsesItemComponent();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(ComponentHolder.Component));
                yield return Until(() => field.Q<Label>("Summary") != null);
                for (int i = 0; i < 4; i++) yield return null;

                var summary = field.Q<Label>("Summary")?.text;
                Assert.IsNotNull(summary, "it folds to a line");
                StringAssert.Contains("Uses 3", summary, "with its Scalable number");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }
    }
}
