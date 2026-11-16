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

        // Its auras are components, in the Components section above Workers, which holds none of them (FN-14); the
        // Components line names each and says what it does, as "Aura: … within 5", where it counted them (EP-5)
        private static void AurasAreComponents(VisualElement root, params string[] components)
        {
            Assert.That(Fields(root, "Workers"), Has.No.Member("Auras"), "no Auras in Workers");
            var line = Summary(root, "Components");
            var named = line == "None" ? new string[0] : line.Split(new[] { " · " }, StringSplitOptions.None).Select(part => part.Split(':')[0]).ToArray();
            Assert.That(named, Is.EqualTo(components), $"the Components line names each: {line}");
            Assert.That(line == "None" || line.Split(new[] { " · " }, StringSplitOptions.None).All(part => part.Contains(": ")),
                $"and says what each does: {line}");
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
                effect.Components.Add(new AuraEffectComponent());
                item.OnHit.Add(new OnHitEntry());
                item.Components.Add(new AuraItemComponent());
                item.Components.Add(new AuraItemComponent());
                entity.Components.Add(new AuraEntityComponent());

                Inspect(effect, root =>
                {
                    Assert.That(Fields(root, "Impact"), Has.No.Member("Area"), "an effect's Area isn't in its Impact");
                    AurasAreComponents(root, "Aura");
                });

                // Its Area is a component, one at most, its fields drawn where it's held: a field of the area's whole drew
                // a foldout inside the component (FN-14)
                effect.Components.Add(new AreaEffectComponent());
                Assert.IsFalse(new AreaEffectComponent().AllowsSeveral, "an effect holds one area at most");
                Inspect(effect, root => AurasAreComponents(root, "Aura", "Area"));
                var area = new SerializedObject(effect).FindProperty("Components.Array.data[1].Area");
                var drawn = new EffectAreaDrawer().CreatePropertyGUI(area).Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(drawn, Is.EqualTo(new[] { "Radius", "Flat", "Takes", "IncludeCaster", "MaxTargets", "UseResolveOrder" }),
                    $"the area's own fields: {string.Join(", ", drawn)}");
                Inspect(item, root =>
                {
                    AurasAreComponents(root, "Aura", "Aura");
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("1 on hit"), "and Workers counts no aura");
                });
                Inspect(ability, root => AurasAreComponents(root));
                Inspect(entity, root => AurasAreComponents(root, "Aura"));
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
