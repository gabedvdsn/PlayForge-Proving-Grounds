using System;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// EP-40, the component API in the editor: a component's rules and the part it adds to its ability's lines are its
    /// own, as PlayForge's components' are, so the Problems view's rules and the ability inspector name none of them. A
    /// component of a project's own, with a rule of its own, is checked and shown where a built-in's would be.
    /// </summary>
    public class ComponentRuleEditorTests
    {
        /// <summary>A component of a project's own: the heat its ability's use builds.</summary>
        [Serializable]
        public sealed class HeatAbilityComponent : AbstractAbilityComponent
        {
            public float Heat = 30f;

            public override string Describe() => $"{Heat:0.##} heat a use";

            public override string DescribeUsage() => Heat > 0f ? $"{Heat:0.##} heat" : null;
        }

        /// <summary>Its rule: heat past 100 a use, which overheats at once.</summary>
        public sealed class HeatRule : ForgeComponentRule<HeatAbilityComponent>
        {
            public override string Name => "Heat that overheats at once";

            protected override void Check(Object asset, HeatAbilityComponent component, int nth, string path, List<ForgeProblem> problems)
            {
                if (component.Heat > 100f)
                    problems.Add(new ForgeProblem(asset, ForgeProblemSeverity.Warning, "Its heat runs past 100 a use, so it overheats at once.", path));
            }
        }

        private readonly List<Object> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private T Instance<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            made.Add(instance);
            return instance;
        }

        private static void Inspect(Object target, Action<VisualElement> check)
        {
            var editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                check(root);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        private static string Summary(VisualElement root, string section) =>
            root.Q(name: $"{section}_CONTAINER").Q<Label>(className: "pf-section__summary").text;

        // The notes under an ability's Components list, each a component's problem
        private static List<string> Notes(VisualElement root)
        {
            var notes = root.Q(name: "ComponentNotes");
            Assert.IsNotNull(notes, "the ability's inspector has its components' notes under the list");
            return notes.Query<HelpBox>().ToList().Where(box => box.style.display.value != DisplayStyle.None).Select(box => box.text).ToList();
        }

        /// <summary>
        /// A rule of a project's own component is found as PlayForge's are, checking each such component an asset holds,
        /// its problem at the component's place; and the ability's inspector says it under its Components list, as it
        /// said the Charges' alone.
        /// </summary>
        [Test]
        public void EP_40_A_rule_of_ones_own_component_is_checked_and_shown_in_place()
        {
            var forge = Instance<Ability>();
            forge.Cooldown = Instance<GameplayEffect>();
            forge.Components.Add(new ChargesAbilityComponent { Charges = 2 });
            forge.Components.Add(new HeatAbilityComponent { Heat = 150f });

            var found = ForgeValidator.Check(forge).Where(problem => problem.Rule is HeatRule).ToList();
            Assert.AreEqual(1, found.Count, "its rule finds the heat");
            Assert.AreEqual("Components.Array.data[1]", found[0].PropertyPath, "at the component's place");
            Inspect(forge, root => Assert.That(Notes(root), Has.Some.StartsWith("Its heat runs past 100"), "and its inspector says it under the components"));

            forge.Cooldown = null;
            Inspect(forge, root => Assert.That(Notes(root), Has.Some.Contains("never run out").And.Some.StartsWith("Its heat runs past 100"),
                "beside the Charges' own, as it was"));

            ((HeatAbilityComponent)forge.Components[1]).Heat = 40f;
            forge.Cooldown = Instance<GameplayEffect>();
            Inspect(forge, root => Assert.IsEmpty(Notes(root), "with nothing wrong, no notes"));
        }

        /// <summary>
        /// What a component adds to its ability's use shows in its Activation line after the cost and the cooldown, as
        /// Charges' "3 charges" did, which the inspector named.
        /// </summary>
        [Test]
        public void EP_40_What_a_component_adds_to_its_abilitys_use_shows_in_its_Activation_line()
        {
            var vent = Instance<Ability>();
            vent.Cooldown = Instance<GameplayEffect>();
            vent.Cooldown.Definition.Name = "Vent Cooldown";
            vent.Components.Add(new HeatAbilityComponent { Heat = 30f });
            Inspect(vent, root => StringAssert.EndsWith("Vent Cooldown · 30 heat", Summary(root, "Activation"), "its heat after its cooldown"));

            vent.Components.Add(new ChargesAbilityComponent { Charges = 3 });
            Inspect(vent, root => StringAssert.EndsWith("Vent Cooldown · 30 heat · 3 charges", Summary(root, "Activation"), "and the charges, in the components' order"));
        }
    }
}
