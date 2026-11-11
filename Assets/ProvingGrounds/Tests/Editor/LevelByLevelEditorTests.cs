using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>An effect level by level in its inspector, which The Forge shows (FEAT-32, LVL-4).</summary>
    public class LevelByLevelEditorTests
    {
        /// <summary>
        /// An effect's inspector has a Levels section: a row for each impact with its value at each level, and under the
        /// table, the parts behind the selected cell, the first impact's first level until another is picked (LVL-4).
        /// </summary>
        [Test]
        public void LVL_4_An_effect_shows_its_levels_and_the_parts_behind_a_cell()
        {
            var health = ScriptableObject.CreateInstance<Attribute>();
            health.Name = "Health";
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = health,
                MagnitudeOperation = new ScalableFloat(
                    new SimpleScaler { LevelValues = new[] { -10f, -20f, -30f }, StretchOverLevels = false }, 1f, EMagnitudeOperation.UseScaler),
                ImpactTypes = new System.Collections.Generic.List<Tag>(),
                Packets = System.Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Instant,
                DurationOperation = new ScalableFloat(0f),
                TicksOperation = new ScalableInt(0),
                TickIntervalOperation = new ScalableFloat(1f),
                StackingConfigs = new System.Collections.Generic.List<AbstractStackingConfig>(),
            };
            var editor = UnityEditor.Editor.CreateEditor(effect);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assume.That(root, Is.Not.Null, "the inspector builds");
                var view = root.Q("EffectLevels");
                Assert.IsNotNull(view, "the effect shows its levels");

                var rows = view.Query<VisualElement>(className: "pf-levels__row").ToList();
                Assert.That(rows.Count, Is.GreaterThanOrEqualTo(2), "a header and a row for its impact");
                var header = rows[0].Query<Label>(className: "pf-levels__cell").ToList().Select(cell => cell.text);
                Assert.That(header, Is.EqualTo(new[] { "1", "2", "3", "4", "5" }), "levels 1 to 5, as it isn't linked");
                var impact = rows.First(row => row.Q<Label>(className: "pf-levels__name").text.StartsWith("Health"));
                var cells = impact.Query<Label>(className: "pf-levels__cell").ToList().Select(cell => cell.text);
                Assert.That(cells, Is.EqualTo(new[] { "-10", "-20", "-30", "-30", "-30" }), "its impact at each level, the last past its values");

                var parts = view.Q<Label>("Parts");
                StringAssert.Contains("Health as it lands, level 1", parts.text, "the parts behind the first cell");
                StringAssert.Contains("= -10", parts.text, "with what the read gave");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(health);
            }
        }
    }
}
