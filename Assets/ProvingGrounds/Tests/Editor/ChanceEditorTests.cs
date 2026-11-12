using System;
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
    /// TW-8 in the inspectors: an effect's Chance Roll under its Chance to Apply, and its proc coefficient in its Impact;
    /// an ability's Chance to Activate, Activation Roll and Proc Coefficient in its Activation.
    /// </summary>
    public class ChanceEditorTests
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

        private static System.Collections.Generic.List<string> Fields(VisualElement root, string section) =>
            root.Q(name: $"{section}_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();

        [Test]
        public void TW_8_The_inspectors_show_each_chance_and_proc_coefficient()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            var ability = ScriptableObject.CreateInstance<Ability>();
            try
            {
                Inspect(effect, root =>
                {
                    var requirements = Fields(root, "Requirements");
                    Assert.That(requirements.IndexOf("ChanceRoll"), Is.EqualTo(requirements.IndexOf("ChanceToApply") + 1),
                        $"its roll under its Chance to Apply: {string.Join(", ", requirements)}");
                    var impact = Fields(root, "Impact");
                    Assert.That(impact, Does.Contain("OwnProcCoefficient").And.Contain("ProcCoefficient"), "its proc coefficient in Impact");
                });
                Inspect(ability, root =>
                {
                    var activation = Fields(root, "Activation");
                    int triggers = activation.IndexOf("Triggers");
                    Assert.That(activation.IndexOf("ChanceToActivate"), Is.EqualTo(triggers + 1), $"after its triggers: {string.Join(", ", activation)}");
                    Assert.That(activation.IndexOf("ActivationRoll"), Is.EqualTo(triggers + 2), "with its roll");
                    Assert.That(activation.IndexOf("ProcCoefficient"), Is.EqualTo(triggers + 3), "and its proc coefficient");
                });
            }
            finally
            {
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ability);
            }
        }
    }
}
