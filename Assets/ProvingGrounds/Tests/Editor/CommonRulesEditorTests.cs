using System;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Editor;
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
    /// with no lifesteal of its own (FN-22); and an attribute's Gains Scaled By.
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
    }
}
