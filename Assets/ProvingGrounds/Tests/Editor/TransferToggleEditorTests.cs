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
    /// TW-20 in the editor: an effect's transfers and an ability's toggle are components (FN-14), and the Problems view says
    /// where either never works.
    /// </summary>
    public class TransferToggleEditorTests
    {
        private static List<string> Fields(Object asset, string section)
        {
            var editor = UnityEditor.Editor.CreateEditor(asset);
            try
            {
                var root = editor.CreateInspectorGUI();
                var container = root.Q(name: $"{section}_CONTAINER");
                Assert.IsNotNull(container, $"the inspector has a {section} section");
                return container.Query<PropertyField>().ToList().Select(field => field.name).ToList();
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        private static List<string> Messages(Object asset) => ForgeValidator.Check(asset).Select(problem => problem.Message).ToList();

        private static AttributeTransfer Transfer(EImpactDelivery delivery, Attribute from, Attribute to) =>
            new() { Delivery = delivery, FromAttribute = from, ToAttribute = to, Amount = 1f };

        /// <summary>
        /// The Problems view says where a transfer never moves, as it does an additional impact's delivery: one its effect's
        /// duration can't make, or one with no attribute to give or get; and where an ability toggles an Instant effect.
        /// </summary>
        [Test]
        public void TW_20_Problems_say_where_a_transfer_or_a_toggle_never_works()
        {
            var steal = ScriptableObject.CreateInstance<GameplayEffect>();
            var shield = ScriptableObject.CreateInstance<Ability>();
            var agility = ScriptableObject.CreateInstance<Attribute>();
            try
            {
                steal.DurationSpecification = new GameplayEffectDuration { DurationPolicy = EEffectDurationPolicy.Instant };
                steal.Components.Add(new TransferEffectComponent { Transfer = Transfer(EImpactDelivery.OnApply, agility, agility) });
                Assert.That(Messages(steal), Has.None.Contains("Transfer"), "a steal as an instant effect lands is no problem");

                // Numbered among its transfers, an aura between them, and each pointing at its own component (FN-14)
                steal.Components.Add(new AuraEffectComponent());
                steal.Components.Add(new TransferEffectComponent { Transfer = Transfer(EImpactDelivery.Continuous, agility, agility) });
                Assert.That(Messages(steal), Has.Some.StartsWith("Transfer 2: An Instant effect makes no Continuous impact"), "one that lasts can't");
                steal.Components.Add(new TransferEffectComponent { Transfer = Transfer(EImpactDelivery.OnApply, agility, null) });
                Assert.That(Messages(steal), Has.Some.StartsWith("Transfer 3: It names no attribute"), "nor one with nothing to get");
                Assert.AreEqual("Components.Array.data[2].Transfer",
                    ForgeValidator.Check(steal).First(problem => problem.Message.StartsWith("Transfer 2")).PropertyPath, "the second, in its component");

                shield.Components.Add(new ToggleAbilityComponent { Effect = steal });
                Assert.That(Messages(shield), Has.Some.Contains("toggles an Instant effect"), "an Instant effect is never on");
                steal.DurationSpecification.DurationPolicy = EEffectDurationPolicy.Infinite;
                Assert.That(Messages(shield), Has.None.Contains("toggles an Instant effect"), "a lasting one toggles");
                Assert.That(Messages(steal), Has.None.StartsWith("Transfer 2"), "and holds while it lasts");
            }
            finally
            {
                Object.DestroyImmediate(steal);
                Object.DestroyImmediate(shield);
                Object.DestroyImmediate(agility);
            }
        }

        [Test]
        public void TW_20_The_inspectors_hold_transfers_and_toggles()
        {
            var steal = ScriptableObject.CreateInstance<GameplayEffect>();
            var shield = ScriptableObject.CreateInstance<Ability>();
            try
            {
                Assert.That(Fields(steal, "Impact"), Has.No.Member("Transfers"), "an effect's transfers aren't in Impact");
                steal.Components.Add(new TransferEffectComponent());
                steal.Components.Add(new TransferEffectComponent());
                Assert.That(Fields(steal, "Components"), Does.Contain("Components"), "but in its Components section");
                Assert.IsTrue(new TransferEffectComponent().AllowsSeveral, "each its own, as many as it moves");
                var transfer = new EffectComponentDrawer().CreatePropertyGUI(new SerializedObject(steal).FindProperty("Components.Array.data[1]"))
                    .Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(transfer, Does.Contain("Transfer"), $"a Transfer component, its transfer: {string.Join(", ", transfer)}");
                Assert.That(Fields(shield, "Activation"), Has.No.Member("Toggles"), "an ability's toggle isn't in Activation");
                shield.Components.Add(new ToggleAbilityComponent());
                Assert.That(Fields(shield, "Components"), Does.Contain("Components"), "but in its Components section");
                // A component's fields, as its drawer lays them out under its kind
                var drawn = new AbilityComponentDrawer().CreatePropertyGUI(new SerializedObject(shield).FindProperty("Components.Array.data[0]"))
                    .Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(drawn, Does.Contain("Effect"), $"a Toggle component, its effect: {string.Join(", ", drawn)}");
            }
            finally
            {
                Object.DestroyImmediate(steal);
                Object.DestroyImmediate(shield);
            }
        }
    }
}
