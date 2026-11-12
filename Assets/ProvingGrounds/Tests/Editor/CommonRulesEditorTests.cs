using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-15 in the inspectors: a holder's Lifesteal in its Workers section, after Auras, counted in its line; an effect's own
    /// Lifesteal in its Impact; and an attribute's Gains Scaled By.
    /// </summary>
    public class CommonRulesEditorTests
    {
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
                mask.Lifesteal.Add(new LifestealEntry());
                Inspect(mask, root =>
                {
                    var workers = Fields(root, "Workers");
                    Assert.That(workers.IndexOf("Lifesteal"), Is.EqualTo(workers.IndexOf("Auras") + 1), $"after Auras: {string.Join(", ", workers)}");
                    Assert.That(root.Q(name: "Workers_CONTAINER").Q<Label>(className: "pf-section__summary").text, Is.EqualTo("1 lifesteal"));
                });
                Inspect(strike, root => Assert.That(Fields(root, "Impact"), Does.Contain("Lifesteal"), "an effect's own, in its Impact"));
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
