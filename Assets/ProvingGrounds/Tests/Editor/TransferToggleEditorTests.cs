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
    /// TW-20 in the editor: an effect's Impact holds its Transfers after its Lifesteal, an ability's Activation its Toggles,
    /// and the Problems view says where either never works.
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
                steal.Transfers.Add(Transfer(EImpactDelivery.OnApply, agility, agility));
                Assert.That(Messages(steal), Has.None.Contains("Transfer"), "a steal as an instant effect lands is no problem");

                steal.Transfers.Add(Transfer(EImpactDelivery.Continuous, agility, agility));
                Assert.That(Messages(steal), Has.Some.StartsWith("Transfer 2: An Instant effect makes no Continuous impact"), "one that lasts can't");
                steal.Transfers.Add(Transfer(EImpactDelivery.OnApply, agility, null));
                Assert.That(Messages(steal), Has.Some.StartsWith("Transfer 3: It names no attribute"), "nor one with nothing to get");

                shield.Toggles = steal;
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
                var impact = Fields(steal, "Impact");
                Assert.That(impact.IndexOf("Transfers"), Is.EqualTo(impact.IndexOf("Lifesteal") + 1), $"Transfers after Lifesteal: {string.Join(", ", impact)}");
                var activation = Fields(shield, "Activation");
                Assert.That(activation, Does.Contain("Toggles"), $"an ability's Toggles: {string.Join(", ", activation)}");
            }
            finally
            {
                Object.DestroyImmediate(steal);
                Object.DestroyImmediate(shield);
            }
        }
    }
}
