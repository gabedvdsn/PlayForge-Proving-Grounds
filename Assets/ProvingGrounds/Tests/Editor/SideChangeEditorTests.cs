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
    /// TW-18 in the editor, as FN-26 has it: an effect's side change is a component, Side Change, in its Components section,
    /// one at most; a field of its own sat in its Tags section, with a switch. The sides it names show while it names them.
    /// </summary>
    public class SideChangeEditorTests
    {
        [Test]
        public void FN_26_An_effects_side_change_is_a_component()
        {
            Assert.IsNull(typeof(GameplayEffect).GetField("SideChange"), "an effect has no side change field of its own");
            Assert.IsFalse(new SideChangeEffectComponent().AllowsSeveral, "and holds one Side Change at most");

            var charm = ScriptableObject.CreateInstance<GameplayEffect>();
            var editor = UnityEditor.Editor.CreateEditor(charm);
            try
            {
                var root = editor.CreateInspectorGUI();
                var names = root.Q(name: "Tags_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(names, Has.No.Member("SideChange"), $"nor one in its Tags section: {string.Join(", ", names)}");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(charm);
            }
        }

        [Test]
        public void FN_26_A_side_changes_sides_show_as_it_names_them()
        {
            var pacify = ScriptableObject.CreateInstance<GameplayEffect>();
            var change = new SideChangeEffectComponent { Source = ESideSource.Named };
            pacify.Components.Add(change);
            try
            {
                VisualElement Drawn()
                {
                    var serialized = new SerializedObject(pacify);
                    var component = serialized.FindProperty(nameof(GameplayEffect.Components)).GetArrayElementAtIndex(0);
                    return new EffectComponentDrawer().CreatePropertyGUI(component);
                }

                var named = Drawn().Q(name: nameof(SideChangeEffectComponent.Sides));
                Assert.IsNotNull(named, "a side change has its sides");
                Assert.IsFalse(named.ClassListContains("pf-hidden"), "which show where it names them");

                change.Source = ESideSource.Caster;
                Assert.IsTrue(Drawn().Q(name: nameof(SideChangeEffectComponent.Sides)).ClassListContains("pf-hidden"),
                    "and hide where it gives its caster's");
            }
            finally
            {
                Object.DestroyImmediate(pacify);
            }
        }
    }
}
