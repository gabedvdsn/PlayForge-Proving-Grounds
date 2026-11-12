using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using FarEmerald.PlayForge.Extended.Editor;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>TW-14 in the Effect inspector: each contained effect's row says whom it lands on.</summary>
    public class RecipientEditorTests
    {
        [Test]
        public void TW_14_A_contained_effects_row_says_whom_it_lands_on()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            try
            {
                var serialized = new SerializedObject(effect);
                var packets = serialized.FindProperty($"{nameof(GameplayEffect.ImpactSpecification)}.{nameof(GameplayEffectImpact.Packets)}");
                Assert.IsNotNull(packets, "an effect has contained effects");
                packets.arraySize = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // The row its list draws for each, built as its list builds it
                var row = new ContainedEffectPacketDrawer().CreatePropertyGUI(packets.GetArrayElementAtIndex(0));
                Assert.IsNotNull(row.Q<EnumField>("Policy"), "the row says when it lands");
                Assert.IsNotNull(row.Q<EnumField>("LandsOn"), "and whom it lands on");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }
    }
}
