using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>TW-19 in the editor: an effect's Requirements section holds its conditions for each phase, after its tags.</summary>
    public class EffectConditionEditorTests
    {
        [Test]
        public void TW_19_An_effects_requirements_hold_its_conditions()
        {
            var finisher = ScriptableObject.CreateInstance<GameplayEffect>();
            var editor = UnityEditor.Editor.CreateEditor(finisher);
            try
            {
                var root = editor.CreateInspectorGUI();
                var names = root.Q(name: "Requirements_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();
                int tags = names.IndexOf("SourceRequirements");
                Assert.That(tags, Is.GreaterThanOrEqualTo(0), $"the tag requirements: {string.Join(", ", names)}");
                Assert.That(names.IndexOf("ApplyConditions"), Is.EqualTo(tags + 1), "then the Apply conditions");
                Assert.That(names.IndexOf("OngoingConditions"), Is.EqualTo(tags + 2), "the Ongoing ones");
                Assert.That(names.IndexOf("RemovalConditions"), Is.EqualTo(tags + 3), "and the Removal ones");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(finisher);
            }
        }
    }
}
