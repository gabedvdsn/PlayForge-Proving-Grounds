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
    /// <summary>TW-13 in the Effect inspector: its Worker Group in its Workers section, after its effect workers, counted in its line.</summary>
    public class EffectWorkerGroupEditorTests
    {
        [Test]
        public void TW_13_An_effects_Workers_section_holds_its_Worker_Group()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.WorkerGroup = new StandardWorkerGroup();
            effect.WorkerGroup.AnalysisWorkers.Add(new ThresholdAnalysisWorker());
            var editor = UnityEditor.Editor.CreateEditor(effect);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                var section = root.Q(name: "Workers_CONTAINER");
                var names = section.Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(names.IndexOf("WorkerGroup"), Is.EqualTo(names.IndexOf("Workers") + 1), $"after its effect workers: {string.Join(", ", names)}");
                Assert.That(section.Q<Label>(className: "pf-section__summary").text, Is.EqualTo("1 worker"), "its line counts the group's");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
            }
        }
    }
}
