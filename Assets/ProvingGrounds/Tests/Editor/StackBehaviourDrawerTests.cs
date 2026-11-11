using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
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
    /// An effect's Stack Behaviours (ED-75): the behaviours are configs of a stacking container, a type apart from the
    /// effect configs' drawer, so they had no drawer, and Unity's own field gave a new element no kind to pick, so none
    /// could be added. Each element picks its kind and shows its fields, as a worker does, named for what it does.
    /// </summary>
    public class StackBehaviourDrawerTests
    {
        private static GameplayEffect Stacking(params AbstractStackingConfig[] behaviours)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Durational,
                ReApplicationPolicy = EEffectReApplicationPolicy.StackExistingContainers,
                StackingConfigs = new List<AbstractStackingConfig>(behaviours),
            };
            return effect;
        }

        // A field drawn in a runtime panel of its own, as the type picker's tests draw one
        private static IEnumerator Drawn(Object target, string path, Action<PropertyField> check)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("ED-75") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(target);
                var property = serialized.FindProperty(path);
                Assume.That(property, Is.Not.Null, path);
                var field = new PropertyField(property);
                document.rootVisualElement.Add(field);
                field.Bind(serialized);
                for (int i = 0; i < 6; i++) yield return null;
                check(field);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private const string First = "DurationSpecification.StackingConfigs.Array.data[0]";

        /// <summary>A stack behaviour is a kind field, its kind's name in the box, with its fields under it (ED-75).</summary>
        [UnityTest]
        public IEnumerator ED_75_A_stack_behaviour_shows_its_kind_and_its_fields()
        {
            var effect = Stacking(new StackBoundsConfig { Min = 0, Max = 3 });
            try
            {
                yield return Drawn(effect, First, field =>
                {
                    Assert.IsNotNull(field.Q(name: "TypeField"), "the behaviour's kind is a field");
                    Assert.AreEqual("Stack Bounds", field.Q<Label>("TypeName")?.text, "named for what it does");
                    Assert.AreEqual(3, field.Q<PropertyField>("Max")?.Q<IntegerField>()?.value, "with its own fields under it");
                    Assert.IsNotNull(field.Q<PropertyField>("OverflowEffects"), "its overflow effects among them");
                });
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>A new, empty element, as the list's "+" makes, offers the behaviours to pick from (ED-75).</summary>
        [UnityTest]
        public IEnumerator ED_75_An_empty_behaviour_offers_a_kind_to_pick()
        {
            var effect = Stacking((AbstractStackingConfig)null);
            try
            {
                yield return Drawn(effect, First, field =>
                {
                    Assert.IsNotNull(field.Q(name: "TypeField"), "an empty behaviour is a kind field to pick from");
                    Assert.AreEqual("None", field.Q<Label>("TypeName")?.text, "showing none yet");
                });
                Assert.IsNull(effect.DurationSpecification.StackingConfigs[0], "and drawing it writes nothing");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// The picker offers the three behaviours by what they do: Stack Bounds, Scaled Stack Amount, which the duration's
        /// own Stack Amount isn't, and Starting Stacks (ED-75).
        /// </summary>
        [Test]
        public void ED_75_The_picker_offers_the_behaviours_by_what_they_do()
        {
            var effect = Stacking((AbstractStackingConfig)null);
            try
            {
                var prop = new SerializedObject(effect).FindProperty(First);
                var kinds = ((IEnumerable<(Type Type, string Name)>)typeof(AbstractTypeRefDrawer<AbstractStackingConfig>)
                    .GetMethod("KindsFor", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(new StackBehaviourDrawer(), new object[] { prop })).ToList();
                Assert.That(kinds, Has.Some.EqualTo((typeof(StackBoundsConfig), "Stack Bounds")));
                Assert.That(kinds, Has.Some.EqualTo((typeof(StackAmountOperation), "Scaled Stack Amount")));
                Assert.That(kinds, Has.Some.EqualTo((typeof(InitializationOperation), "Starting Stacks")));
                Assert.That(kinds.Select(kind => kind.Type), Has.None.Matches<Type>(type => type.IsAbstract), "no abstract kind");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }
    }
}
