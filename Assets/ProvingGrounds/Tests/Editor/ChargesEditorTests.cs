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
    /// TW-17 in the editor: an ability's Activation holds its Charges after its Cooldown, and their Recharge while they're
    /// counted. ED-41's note and problem are gone, as a cost or a cooldown no stage takes applies as the ability starts;
    /// and charges that a built-in recharge brings back, with no Cooldown to bring them, are a problem: they never run out.
    /// </summary>
    public class ChargesEditorTests
    {
        // Spent charges never come back: so many uses
        private sealed class LimitedUses : AbstractChargeRecharge
        {
            public override int InHand(AbilityCharges charges) => charges.Max - charges.Spent;
            public override void Spend(AbilityCharges charges, ProcessDataPacket data) => charges.Spent++;
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

        private static List<string> Fields(VisualElement root, string section)
        {
            var container = root.Q(name: $"{section}_CONTAINER");
            Assert.IsNotNull(container, $"the inspector has a {section} section");
            return container.Query<PropertyField>().ToList().Select(field => field.name).ToList();
        }

        private static List<string> Messages(Object asset) => ForgeValidator.Check(asset).Select(problem => problem.Message).ToList();

        /// <summary>Charges are a component, with their Recharge; the Activation section holds neither (FN-14).</summary>
        [Test]
        public void TW_17_Charges_are_a_component_with_their_recharge()
        {
            var shrapnel = Instance<Ability>();
            shrapnel.Cooldown = Instance<GameplayEffect>();
            Inspect(shrapnel, root =>
            {
                Assert.That(Fields(root, "Activation"), Has.No.Member("Charges").And.No.Member("Recharge"), "Activation holds no charges");
                Assert.IsNull(root.Q<HelpBox>("UnappliedUsage"), "and no note of usage no stage applies: it applies as the ability starts");
            });

            shrapnel.Components.Add(new ChargesAbilityComponent { Charges = 3 });
            // A component's fields, as its drawer lays them out under its kind
            var drawn = new AbilityComponentDrawer().CreatePropertyGUI(new SerializedObject(shrapnel).FindProperty("Components.Array.data[0]"))
                .Query<PropertyField>().ToList().Select(field => field.name).ToList();
            Assert.That(drawn, Has.Member("Charges").And.Member("Recharge"), $"its Charges component, with their Recharge: {string.Join(", ", drawn)}");
            Inspect(shrapnel, root =>
            {
                Assert.That(Fields(root, "Components"), Has.Member("Components"), "in its Components section");
                Assert.AreEqual(DisplayStyle.None, root.Q<HelpBox>("ChargesNeverRunOut").style.display.value, "no note, with a Cooldown");
            });

            shrapnel.Cooldown = null;
            Inspect(shrapnel, root =>
                Assert.AreNotEqual(DisplayStyle.None, root.Q<HelpBox>("ChargesNeverRunOut").style.display.value,
                    "and a note under the components where they never run out"));
        }

        /// <summary>
        /// A cost and a cooldown no stage takes are no problem now; charges with no Cooldown to bring them back are, under a
        /// built-in recharge, not one of an author's own.
        /// </summary>
        [Test]
        public void TW_17_Usage_no_stage_takes_is_no_problem_and_charges_with_no_cooldown_are()
        {
            var slash = Instance<Ability>();
            slash.Behaviour = new AbilityBehaviour { Stages = new List<AbilityTaskBehaviourStage> { new AbilityTaskBehaviourStage() } };
            slash.Cooldown = Instance<GameplayEffect>();
            slash.Cost = Instance<GameplayEffect>();
            Assert.That(Messages(slash), Has.None.Contains("usage effects"), "a cost and a cooldown no stage takes apply as it starts");

            var potion = Instance<Ability>();
            var charges = new ChargesAbilityComponent { Charges = 3 };
            potion.Components.Add(charges);
            Assert.That(Messages(potion), Has.Some.Contains("never run out"), "three charges and no Cooldown to bring them back");
            charges.Recharge = new AllAtOnceRecharge();
            Assert.That(Messages(potion), Has.Some.Contains("never run out"), "nor all at once");
            potion.Cooldown = Instance<GameplayEffect>();
            Assert.That(Messages(potion), Has.None.Contains("never run out"), "with a Cooldown, they do");

            potion.Cooldown = null;
            charges.Recharge = new LimitedUses();
            Assert.That(Messages(potion), Has.None.Contains("never run out"), "a recharge of one's own decides for itself");
        }
    }
}
