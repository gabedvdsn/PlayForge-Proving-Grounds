using System;
using System.Collections;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Fields shown only where they apply (FN-23): an ability's input buffer under Queue or its entity's policy, which may be
    /// Queue; an effect's Proc Coefficient under Own Proc Coefficient; a roll only for a chance that can miss; and a roll's
    /// Luck under Own Luck. Each hides, not disables, and shows again as the field it reads changes.
    /// </summary>
    public class ShownFieldsTests
    {
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

        private static bool Hidden(VisualElement root, string name)
        {
            var field = root.Q(name: name);
            Assert.IsNotNull(field, $"the inspector has {name}");
            return field.ClassListContains("pf-hidden");
        }

        [Test]
        public void FN_23_An_abilitys_buffer_shows_under_Queue_or_its_entitys_policy()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            try
            {
                foreach (var (policy, shown) in new[]
                         {
                             (EAbilityActivationPolicyExtended.UseLocalPolicy, true),
                             (EAbilityActivationPolicyExtended.QueueActivationIfBusy, true),
                             (EAbilityActivationPolicyExtended.AlwaysActivate, false),
                             (EAbilityActivationPolicyExtended.ActivateIfIdle, false),
                         })
                {
                    ability.Definition.ActivationPolicy = policy;
                    Inspect(ability, root =>
                    {
                        Assert.AreEqual(!shown, Hidden(root, "BufferWindow"), $"Buffer Window under {policy}");
                        Assert.AreEqual(!shown, Hidden(root, "BufferPriority"), $"Buffer Priority under {policy}");
                    });
                }
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void FN_23_An_effects_proc_coefficient_shows_under_its_own()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            try
            {
                effect.OwnProcCoefficient = false;
                Inspect(effect, root => Assert.IsTrue(Hidden(root, "ProcCoefficient"), "its ability's stands, so its own is hidden"));
                effect.OwnProcCoefficient = true;
                Inspect(effect, root => Assert.IsFalse(Hidden(root, "ProcCoefficient"), "under Own Proc Coefficient, its own shows"));
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void FN_23_A_roll_shows_only_for_a_chance_that_can_miss()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.ImpactSpecification = new GameplayEffectImpact();
            try
            {
                Inspect(ability, root => Assert.IsTrue(Hidden(root, "ActivationRoll"), "a chance to activate of 1 has nothing to roll"));
                Inspect(effect, root => Assert.IsTrue(Hidden(root, "ChanceRoll"), "nor a chance to apply of 1"));

                ability.Definition.ChanceToActivate = 0.5f;
                effect.ImpactSpecification.ChanceToApply = 0.25f;
                Inspect(ability, root => Assert.IsFalse(Hidden(root, "ActivationRoll"), "a chance of a half rolls"));
                Inspect(effect, root => Assert.IsFalse(Hidden(root, "ChanceRoll"), "and a quarter"));

                ability.Definition.ChanceToActivate = new ScalableFloat(new SimpleScaler(), 1f);
                Inspect(ability, root => Assert.IsFalse(Hidden(root, "ActivationRoll"), "and a chance of a kind, which may miss"));
            }
            finally
            {
                Object.DestroyImmediate(ability);
                Object.DestroyImmediate(effect);
            }
        }

        // A roll's drawer, found by its name, so the test reads as it should where there's none
        private static VisualElement RollDrawn(SerializedProperty roll)
        {
            var type = typeof(TaskSequenceReferenceDrawer).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ChanceRollDrawer");
            Assert.IsNotNull(type, "a roll has a drawer of its own");
            return ((PropertyDrawer)Activator.CreateInstance(type)).CreatePropertyGUI(roll);
        }

        [Test]
        public void FN_23_A_rolls_luck_shows_under_its_own_luck()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            try
            {
                var path = $"{nameof(Ability.Definition)}.{nameof(AbilityDefinition.ActivationRoll)}";
                var serialized = new SerializedObject(ability);
                Assert.IsTrue(Hidden(RollDrawn(serialized.FindProperty(path)), "Luck"),
                    "without its own luck, the project's Luck role is read, so Luck is hidden");
                ability.Definition.ActivationRoll.OwnLuck = true;
                serialized.Update();
                Assert.IsFalse(Hidden(RollDrawn(serialized.FindProperty(path)), "Luck"), "with it, its Luck shows");
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>A hidden field shows again as the field it reads changes, from outside the inspector as an Undo does.</summary>
        [UnityTest]
        public IEnumerator FN_23_A_hidden_field_shows_as_what_it_reads_changes()
        {
            // A scene's object, as ED-61's: Unity counts no change to one it doesn't save, so its bindings see none
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            var editor = UnityEditor.Editor.CreateEditor(effect);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("FN-23 Panel") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = editor.CreateInspectorGUI();
                document.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.IsTrue(Hidden(root, "ProcCoefficient"), "hidden without its own");

                var other = new SerializedObject(effect);
                other.FindProperty(nameof(GameplayEffect.OwnProcCoefficient)).boolValue = true;
                other.ApplyModifiedProperties();
                // The bindings look for a change made outside them on a timer of their own
                var until = EditorApplication.timeSinceStartup + 1.0;
                while (Hidden(root, "ProcCoefficient") && EditorApplication.timeSinceStartup < until) yield return null;
                Assert.IsFalse(Hidden(root, "ProcCoefficient"), "and shown once it has its own");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
            }
        }
    }
}
