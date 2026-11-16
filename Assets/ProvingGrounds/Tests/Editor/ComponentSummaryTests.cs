using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// An asset's summary breaks its components down (EP-5, the user's editor pass of 2026-11-15): a Components section's
    /// line, which The Forge's summary shows, says each component and what it does with its numbers, as an ability's Cast
    /// Range; it said how many there were. And the ability breakdown reads its components, what sets it off and its chance.
    /// </summary>
    public class ComponentSummaryTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static string ComponentsLine(IEnumerable<AbstractForgeComponent> components) =>
            (string)typeof(TagDrawer).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeSummaries", true)
                .GetMethod("ComponentsLine", Any).Invoke(null, new object[] { components });

        private static Ability Ranged()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "EP5 Snipe";
            ability.Components = new List<AbstractAbilityComponent>
            {
                new CastRangeAbilityComponent { Range = 600f },
                new ChargesAbilityComponent { Charges = 3 },
            };
            return ability;
        }

        /// <summary>A Components line names each component and what it does, its numbers among it; None for none.</summary>
        [Test]
        public void EP_5_A_components_line_says_what_each_does()
        {
            var ability = Ranged();
            try
            {
                var line = ComponentsLine(ability.Components);
                StringAssert.Contains("Cast Range: Range 600", line, "the cast range, by its number");
                StringAssert.Contains("Charges: 3 charges, each recharging on its own", line, "and the charges, by theirs");
                Assert.AreEqual("None", ComponentsLine(new List<AbstractAbilityComponent>()), "none says None");
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>Each component PlayForge ships says what it does, as it starts.</summary>
        [Test]
        public void EP_5_Each_component_PlayForge_ships_says_what_it_does()
        {
            var silent = typeof(Tag).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(AbstractForgeComponent).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
                .Where(type => string.IsNullOrWhiteSpace(((AbstractForgeComponent)Activator.CreateInstance(type)).Describe()))
                .Select(type => type.Name)
                .ToList();
            Assert.IsEmpty(silent, $"components that say nothing: {string.Join(", ", silent)}");
        }

        /// <summary>
        /// The ability breakdown reads its components, what sets it off on its own, its chance to activate, and what it
        /// spends of its entity's attributes.
        /// </summary>
        [Test]
        public void EP_5_The_ability_breakdown_reads_its_components()
        {
            var ability = Ranged();
            var mana = ScriptableObject.CreateInstance<Attribute>();
            mana.Name = "EP5 Mana";
            try
            {
                ability.Definition.ActivateImmediately = true;
                ability.Definition.ChanceToActivate = 0.5f;
                ability.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = 20f });
                var breakdown = AbilityInspector.Build(ability);
                Assert.That(breakdown.Components, Has.Member(("Cast Range", "Range 600")), "its cast range");
                var lines = breakdown.Describe(1);
                Assert.That(lines, Has.Member("Cast Range: Range 600"), $"which it describes: {string.Join(" | ", lines)}");
                Assert.That(lines, Has.Member("Activates: at its entity's start"), "with what sets it off");
                Assert.That(lines.Any(line => line.StartsWith("Chance to activate:")), $"and its chance: {string.Join(" | ", lines)}");
                Assert.That(lines, Has.Member("Cost: 20 EP5 Mana"), $"and what it spends: {string.Join(" | ", lines)}");
            }
            finally
            {
                Object.DestroyImmediate(mana);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>An ability's inspector says its components in its Components section's line, which The Forge's summary shows.</summary>
        [UnityTest]
        public IEnumerator EP_5_An_abilitys_Components_section_says_its_components()
        {
            var ability = Ranged();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("EP-5") { hideFlags = HideFlags.HideAndDontSave };
            UnityEditor.Editor editor = null;
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(ability);
                var root = editor.CreateInspectorGUI();
                document.rootVisualElement.Add(root);
                for (int i = 0; i < 6; i++) yield return null;
                var said = root.Query<Label>().ToList().Select(label => label.text).Where(text => text != null).ToList();
                Assert.That(said, Has.Some.Contains("Cast Range: Range 600"), "the Components section's line says the cast range");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(ability);
            }
        }
    }
}
