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
    /// <summary>TW-12 in the Attribute inspector: a Barrier's settings, under its Role, while its Role is Barrier.</summary>
    public class BarrierEditorTests
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

        [Test]
        public void TW_12_An_attributes_Barrier_settings_show_while_its_role_is_Barrier()
        {
            var plain = ScriptableObject.CreateInstance<Attribute>();
            var shield = ScriptableObject.CreateInstance<Attribute>();
            shield.Role = EAttributeRole.Barrier;
            try
            {
                Inspect(plain, root =>
                {
                    var settings = root.Q("BarrierSettings");
                    Assert.IsNotNull(settings, "the settings are there");
                    Assert.AreEqual(DisplayStyle.None, settings.style.display.value, "hidden for a Value");
                });
                Inspect(shield, root =>
                {
                    var settings = root.Q("BarrierSettings");
                    Assert.AreNotEqual(DisplayStyle.None, settings.style.display.value, "shown for a Barrier");
                    var names = settings.Query<PropertyField>().ToList().Select(field => field.name).ToList();
                    Assert.That(names, Is.EqualTo(new[] { "AbsorbsFor", "DecayPerSecond", "DecayAsShareOfBase", "FadesWithEffect" }),
                        "what it absorbs for, its decay, and its fade");
                });
            }
            finally
            {
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(shield);
            }
        }
    }
}
