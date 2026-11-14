using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using FarEmerald.PlayForge.Extended.Editor;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-31 in the editor: Project Settings → PlayForge shows the project's Resolve Order after its crowd control, and an
    /// effect's Area and the Apply Effects tasks each say whether they follow it.
    /// </summary>
    public class ResolveOrderEditorTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void TW_31_The_project_settings_show_the_resolve_order()
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            try
            {
                var pages = typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.PlayForgeSettingsPages");
                var section = (VisualElement)pages.GetMethod("ForgeProjectSettings", AnyStatic).Invoke(null, new object[] { project });
                var fields = section.Query<PropertyField>().ToList();
                int crowdControl = fields.FindIndex(field => field.name == "CrowdControl");
                int resolveOrder = fields.FindIndex(field => field.name == "ResolveOrder");
                Assert.That(resolveOrder, Is.GreaterThan(crowdControl).And.GreaterThan(-1), "the Resolve Order, after the crowd control");
            }
            finally
            {
                Object.DestroyImmediate(project);
            }
        }

        [Test]
        public void TW_31_An_area_and_the_apply_tasks_follow_it_unless_told_not_to()
        {
            Assert.IsTrue(new EffectArea().UseResolveOrder, "an effect's Area follows it");
            Assert.IsTrue(new ApplyEffectsAbilityTask().UseResolveOrder, "Apply Effects does");
            Assert.IsTrue(new ApplyRemoveEffectsAbilityTask().UseResolveOrder, "and Apply/Remove Effects");

            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            try
            {
                // An effect's Area is a component (FN-14)
                effect.Components.Add(new AreaEffectComponent());
                var area = new SerializedObject(effect).FindProperty($"Components.Array.data[0].{nameof(AreaEffectComponent.Area)}.{nameof(EffectArea.UseResolveOrder)}");
                Assert.IsNotNull(area, "the Area's own toggle is serialized, so its inspector shows it");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }
    }
}
