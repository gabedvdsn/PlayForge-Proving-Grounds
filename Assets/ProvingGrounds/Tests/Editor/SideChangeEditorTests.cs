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
    /// TW-18 in the editor: an effect's Side Change sits in its Tags section after the abilities it lends; its fields show
    /// while it changes sides, and the sides it names while it names them.
    /// </summary>
    public class SideChangeEditorTests
    {
        [Test]
        public void TW_18_An_effects_side_change_is_in_its_Tags_section()
        {
            var charm = ScriptableObject.CreateInstance<GameplayEffect>();
            var editor = UnityEditor.Editor.CreateEditor(charm);
            try
            {
                var root = editor.CreateInspectorGUI();
                var names = root.Q(name: "Tags_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(names.IndexOf("SideChange"), Is.EqualTo(names.IndexOf("GrantedAbilities") + 1),
                    $"after the abilities it lends: {string.Join(", ", names)}");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(charm);
            }
        }

        [Test]
        public void TW_18_A_side_changes_fields_show_as_it_changes_sides()
        {
            var charm = ScriptableObject.CreateInstance<GameplayEffect>();
            var pacify = ScriptableObject.CreateInstance<GameplayEffect>();
            pacify.SideChange = new SideChange { Changes = true, Source = ESideSource.Named };
            try
            {
                VisualElement Row(GameplayEffect effect) =>
                    new SideChangeDrawer().CreatePropertyGUI(new SerializedObject(effect).FindProperty(nameof(GameplayEffect.SideChange)));

                var off = Row(charm);
                Assert.IsNotNull(off.Q<Toggle>(nameof(SideChange.Changes)), "a switch");
                Assert.AreEqual(DisplayStyle.None, off.Q("SideChangeFields").style.display.value, "and nothing more while it's off");

                var named = Row(pacify);
                Assert.AreNotEqual(DisplayStyle.None, named.Q("SideChangeFields").style.display.value, "its fields while it's on");
                Assert.AreNotEqual(DisplayStyle.None, named.Q(nameof(SideChange.Sides)).style.display.value, "with the sides it names");

                pacify.SideChange.Source = ESideSource.Caster;
                var casters = Row(pacify);
                Assert.AreEqual(DisplayStyle.None, casters.Q(nameof(SideChange.Sides)).style.display.value, "and none where it gives its caster's");
            }
            finally
            {
                Object.DestroyImmediate(charm);
                Object.DestroyImmediate(pacify);
            }
        }
    }
}
