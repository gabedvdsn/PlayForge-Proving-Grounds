using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-2's inspector: a Value Pipeline shows what it's for, a line of its stages, and its stages, each of a kind with
    /// its fields; and an effect's impact shows the pipeline it names.
    /// </summary>
    public class ValuePipelineEditorTests
    {
        [Test]
        public void TW_2_A_value_pipelines_inspector_shows_its_stages()
        {
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            pipeline.Stages = new List<AbstractValueStage>
            {
                new MultiplyStage { Label = "Amplification", By = 1.5f },
                new ChanceStage { Label = "Critical", Chance = 0.1f, Multiplier = 2f },
                new AbsorbStage { Label = "Barrier" },
            };
            UnityEditor.Editor editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(pipeline);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "it builds");
                Assert.IsNotNull(root.Q<TextField>("Description"), "what it's for");
                Assert.IsNotNull(root.Q<PropertyField>("Stages"), "its stages");
                var line = root.Q<Label>("PipelineLine");
                Assert.IsNotNull(line, "and a line of them");
                Assert.AreEqual("Amplification, then Critical, then Barrier", line.text, "in order, by their labels");
                Assert.AreEqual("Amplification, then Critical, then Barrier", pipeline.Describe(), "as the pipeline says itself");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(pipeline);
            }
        }

        [Test]
        public void TW_2_An_impact_shows_the_pipeline_it_names()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            try
            {
                var serialized = new UnityEditor.SerializedObject(effect);
                var impact = serialized.FindProperty(nameof(GameplayEffect.ImpactSpecification));
                var root = new FarEmerald.PlayForge.Extended.Editor.GameplayEffectImpactDrawer().CreatePropertyGUI(impact);
                var pipeline = root.Q<PropertyField>("Pipeline");
                Assert.IsNotNull(pipeline, "the first impact names its pipeline");
                Assert.AreEqual(impact.FindPropertyRelative(nameof(GameplayEffectImpact.Pipeline)).propertyPath, pipeline.bindingPath,
                    "the impact's own");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }
    }
}
