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
    /// FN-16 in the editor: an effect's Purges in its Tags section, after Removes Effects Tagged; a purge's filter named
    /// without "Purge Filter".
    /// </summary>
    public class PurgeEditorTests
    {
        [Test]
        public void FN_16_An_effects_purges_are_in_its_tags_section()
        {
            var dispel = ScriptableObject.CreateInstance<GameplayEffect>();
            var editor = UnityEditor.Editor.CreateEditor(dispel);
            try
            {
                var root = editor.CreateInspectorGUI();
                var fields = root.Q(name: "Tags_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(fields.IndexOf("Purges"), Is.EqualTo(fields.IndexOf("RemovesEffectsTagged") + 1),
                    $"its Purges after its Removes Effects Tagged: {string.Join(", ", fields)}");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(dispel);
            }
        }

        [Test]
        public void FN_16_A_purges_filter_reads_by_its_kind()
        {
            var clean = typeof(PurgeFilterDrawer).GetMethod("CleanTypeName",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.AreEqual("ExceptTagged", clean.Invoke(new PurgeFilterDrawer(), new object[] { nameof(ExceptTaggedPurgeFilter) }),
                "Except Tagged, named without Purge Filter");
        }
    }
}
