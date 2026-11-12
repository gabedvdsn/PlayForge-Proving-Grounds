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
    /// TW-10 in the inspectors: an effect's Area in its Impact section; Auras in each Workers section, after Modifiers,
    /// and its line counting them.
    /// </summary>
    public class AreasEditorTests
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

        private static List<string> Fields(VisualElement root, string section)
        {
            var container = root.Q(name: $"{section}_CONTAINER");
            Assert.IsNotNull(container, $"the inspector has a {section} section");
            return container.Query<PropertyField>().ToList().Select(field => field.name).ToList();
        }

        private static string Summary(VisualElement root, string section) =>
            root.Q(name: $"{section}_CONTAINER").Q<Label>(className: "pf-section__summary").text;

        private static void AurasAfterModifiers(VisualElement root)
        {
            var workers = Fields(root, "Workers");
            Assert.That(workers.IndexOf("Auras"), Is.EqualTo(workers.IndexOf("Modifiers") + 1), $"Auras after Modifiers: {string.Join(", ", workers)}");
        }

        [Test]
        public void TW_10_The_inspectors_hold_Area_and_Auras()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            var item = ScriptableObject.CreateInstance<Item>();
            var ability = ScriptableObject.CreateInstance<Ability>();
            var entity = ScriptableObject.CreateInstance<EntityIdentity>();
            try
            {
                effect.Auras.Add(new AuraEntry());
                item.OnHit.Add(new OnHitEntry());
                item.Auras.Add(new AuraEntry());
                item.Auras.Add(new AuraEntry());
                entity.Auras.Add(new AuraEntry());

                Inspect(effect, root =>
                {
                    Assert.That(Fields(root, "Impact"), Does.Contain("Area"), "an effect's Area, in its Impact");
                    AurasAfterModifiers(root);
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("1 aura"), "the line counts its auras");
                });
                Inspect(item, root =>
                {
                    AurasAfterModifiers(root);
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("1 on hit, 2 auras"));
                });
                Inspect(ability, AurasAfterModifiers);
                Inspect(entity, root =>
                {
                    AurasAfterModifiers(root);
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("1 aura"));
                });
            }
            finally
            {
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(ability);
                Object.DestroyImmediate(entity);
            }
        }
    }
}
