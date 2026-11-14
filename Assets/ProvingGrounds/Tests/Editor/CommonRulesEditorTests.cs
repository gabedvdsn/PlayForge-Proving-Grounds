using System;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-15 in the inspectors: a holder's lifesteal its Lifesteal attribute, with no list of its own (FN-15), and an effect
    /// with no lifesteal of its own (FN-22); and an attribute's Gains Scaled By. The Problems view names an older save's list.
    /// </summary>
    public class CommonRulesEditorTests
    {
        /// <summary>
        /// An effect's Impact held a Lifesteal of its own, a share its caster's rose by for its hits; lifesteal is its
        /// caster's Lifesteal alone now, so its Impact has none (FN-22).
        /// </summary>
        [Test]
        public void FN_22_An_effects_impact_has_no_lifesteal_of_its_own()
        {
            var strike = ScriptableObject.CreateInstance<GameplayEffect>();
            try
            {
                Inspect(strike, root =>
                {
                    var impact = Fields(root, "Impact");
                    Assert.That(impact, Is.Not.Empty, "the inspector has an Impact section");
                    Assert.That(impact, Has.No.Member("Lifesteal"), $"with no lifesteal of its own: {string.Join(", ", impact)}");
                });
            }
            finally
            {
                Object.DestroyImmediate(strike);
            }
        }

        private static void Inspect(Object target, Action<VisualElement> check)
        {
            var editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                check(root);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        private static List<string> Fields(VisualElement root, string section) =>
            root.Q(name: $"{section}_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();

        [Test]
        public void TW_15_The_inspectors_hold_lifesteal_and_gains_scaled_by()
        {
            var mask = ScriptableObject.CreateInstance<Item>();
            var strike = ScriptableObject.CreateInstance<GameplayEffect>();
            var health = ScriptableObject.CreateInstance<Attribute>();
            try
            {
                Inspect(mask, root =>
                {
                    var workers = Fields(root, "Workers");
                    Assert.That(workers, Has.No.Member("Lifesteal"), $"a holder's lifesteal is its Lifesteal, no list of its own: {string.Join(", ", workers)}");
                    Assert.That(root.Q(name: "Workers_CONTAINER").Q<Label>(className: "pf-section__summary").text, Is.EqualTo("None"));
                });
                Inspect(health, root => Assert.IsNotNull(root.Q<PropertyField>("GainsScaledBy"), "an attribute's Gains Scaled By"));
            }
            finally
            {
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(strike);
                Object.DestroyImmediate(health);
            }
        }

        /// <summary>
        /// An older save's Lifesteal list heals no more, a holder's lifesteal being its Lifesteal: kept, hidden, under its
        /// name, and the Problems view says where one is, on an item, an ability or an entity (FN-15).
        /// </summary>
        [Test]
        public void FN_15_Problems_say_where_an_older_lifesteal_list_heals_no_more()
        {
            foreach (var holder in new ScriptableObject[]
                     {
                         ScriptableObject.CreateInstance<Item>(), ScriptableObject.CreateInstance<Ability>(), ScriptableObject.CreateInstance<EntityIdentity>(),
                     })
            {
                try
                {
                    var messages = ForgeValidator.Check(holder).Select(problem => problem.Message).ToList();
                    Assert.That(messages, Has.None.Contains("older save's Lifesteal"), $"a {holder.GetType().Name} with none is no problem");

                    // As an older save left it: an entry in its list, under the list's name
                    var serialized = new SerializedObject(holder);
                    var list = serialized.FindProperty("Lifesteal") ?? serialized.FindProperty("legacyLifesteal");
                    Assert.IsNotNull(list, $"a {holder.GetType().Name} keeps an older save's list");
                    list.arraySize = 1;
                    serialized.ApplyModifiedPropertiesWithoutUndo();

                    messages = ForgeValidator.Check(holder).Select(problem => problem.Message).ToList();
                    Assert.That(messages, Has.Some.Contains("older save's Lifesteal"), $"one with an entry: {string.Join(" | ", messages)}");
                }
                finally
                {
                    Object.DestroyImmediate(holder);
                }
            }
        }
    }
}
