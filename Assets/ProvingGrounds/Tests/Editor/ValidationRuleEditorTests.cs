using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The ability validation rules' pass (EP-34, EP-35, the user's notes of 2026-11-17): Check Range, the Cast Range
    /// component's check, shown among the Target Checks while the ability holds the component and tied to it; each list
    /// offered its own rules; and every rule kind serializable, described and sided.
    /// </summary>
    public class ValidationRuleEditorTests
    {
        private readonly List<Object> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private Ability NewAbility()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            made.Add(ability);
            return ability;
        }

        // ─── Check Range, the Cast Range component's check (EP-34) ───────────────────────────────────────────────────

        /// <summary>
        /// Check Range read its range from a tag's weight or Local Data, so with the Cast Range component holding the range
        /// it was a second range to keep in step, added by hand (EP-34, the user's "an honorary, invincible member, tied
        /// totally to the cast range component"). The component brings it to the Target Checks, no list holds it, it passes
        /// as the component enforces the range, and it goes with the component.
        /// </summary>
        [Test]
        public void EP_34_A_Cast_Range_brings_Check_Range_to_the_Target_Checks()
        {
            var ability = NewAbility();
            Assert.That(ability.TargetChecks.OfType<CheckRangeValidation>(), Is.Empty, "with no Cast Range, no Check Range");

            var range = new CastRangeAbilityComponent { Range = 6f };
            ability.Components.Add(range);
            var check = ability.TargetChecks.OfType<CheckRangeValidation>().SingleOrDefault();
            Assert.IsNotNull(check, $"the Target Checks hold Check Range: {string.Join(", ", ability.TargetChecks.Select(rule => rule.GetName()))}");
            Assert.AreSame(range, check.Component, "tied to its component");
            Assert.That(ability.TargetChecks.First(), Is.InstanceOf<IsAliveValidation>(), "after the rules the list holds");
            Assert.That(ability.TargetActivationRules, Has.None.InstanceOf<CheckRangeValidation>(), "which hold none of it");
            Assert.That(ability.CasterChecks, Has.None.InstanceOf<CheckRangeValidation>(), "and among the Target Checks alone");
            Assert.AreEqual("Check Range", check.GetName());
            StringAssert.Contains("6", check.Describe(), "it says the component's range");
            Assert.IsTrue(check.Validate(null, () => null, out _), "and passes: the component enforces the range");

            ability.Components.Remove(range);
            Assert.That(ability.TargetChecks, Has.None.InstanceOf<CheckRangeValidation>(), "gone with its component");
        }

        /// <summary>
        /// A Check Range held in a list stands for no range: Check Range is its Cast Range's alone (EP-34). Nothing moves
        /// one as its ability loads, as no PlayForge asset moves an older save's data (FN-21), so the Demo's Shield Bash,
        /// which held one reading the reach in its Local Data, holds a Cast Range in its place, and no asset of the
        /// samples or of Proving Grounds holds one.
        /// </summary>
        [Test]
        public void EP_34_No_asset_holds_a_Check_Range_in_its_lists()
        {
            var package = PackageInfo.FindForAssembly(typeof(Ability).Assembly)?.resolvedPath;
            Assume.That(package, Is.Not.Null, "PlayForge is a package here");
            var holding = new[] { Path.Combine(package, "Samples~"), Application.dataPath }
                .Where(Directory.Exists)
                .SelectMany(root => Directory.GetFiles(root, "*.asset", SearchOption.AllDirectories))
                .Where(path => !path.Replace('\\', '/').Contains("/."))
                .Where(path => File.ReadAllText(path).Contains("class: CheckRangeValidation"))
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
            Assert.That(holding, Is.Empty, $"assets holding a Check Range: {string.Join(", ", holding)}");
        }

        /// <summary>Check Range is no rule to add by hand (EP-34): the picker offers it to neither list.</summary>
        [Test]
        public void EP_34_Check_Range_isnt_offered_by_hand()
        {
            var ability = NewAbility();
            Assert.That(KindsOffered(ability, nameof(Ability.TargetActivationRules)), Has.No.Member("Check Range"), "the Target Checks don't offer it");
            Assert.That(KindsOffered(ability, nameof(Ability.SourceActivationRules)), Has.No.Member("Check Range"), "nor the Caster Checks");
        }

        /// <summary>
        /// The Requirements section shows Check Range under the Target Checks while the ability holds a Cast Range, locked
        /// and saying the component's range, and its line counts it among the checks (EP-34).
        /// </summary>
        [Test]
        public void EP_34_The_Requirements_section_shows_Check_Range_with_its_Cast_Range()
        {
            var ability = NewAbility();
            Inspect(ability, root => Assert.That(Rows(root), Is.Empty, "with no Cast Range, no row"));

            ability.Components.Add(new CastRangeAbilityComponent { Range = 7f });
            Inspect(ability, root =>
            {
                var rows = Rows(root);
                Assert.That(rows, Has.Count.EqualTo(1), "a Cast Range's row under the Target Checks");
                Assert.AreEqual("Check Range", rows[0].Q<Label>("ComponentCheckName")?.text, "named Check Range");
                StringAssert.Contains("7", rows[0].Q<Label>("ComponentCheckLine")?.text, "with the component's range");
                Assert.IsFalse(rows[0].enabledSelf, "and locked");
                StringAssert.Contains("Cast Range", rows[0].tooltip, "its tooltip says whose it is");
            });
        }

        // ─── The rules' pass (EP-35) ─────────────────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<Type, EValidationSide> Sides = new()
        {
            [typeof(CostValidation)] = EValidationSide.Caster,
            [typeof(CooldownValidation)] = EValidationSide.Caster,
            [typeof(AbilityLevelValidation)] = EValidationSide.Caster,
            [typeof(AffiliationValidation)] = EValidationSide.Target,
            [typeof(LineOfSightValidation)] = EValidationSide.Target,
            [typeof(FacingValidation)] = EValidationSide.Target,
            [typeof(CheckRangeValidation)] = EValidationSide.Target,
            [typeof(IsAliveValidation)] = EValidationSide.Either,
            [typeof(AttributeValidation)] = EValidationSide.Either,
            [typeof(TagWeightValidation)] = EValidationSide.Either,
            [typeof(HasEffectValidation)] = EValidationSide.Either,
            [typeof(HoldsItemValidation)] = EValidationSide.Either,
        };

        /// <summary>
        /// Every rule kind PlayForge offers is serializable, says what it does, and says which of the checks it belongs among
        /// (EP-35): some lacked [Serializable], and none said its side.
        /// </summary>
        [Test]
        public void EP_35_Every_rule_kind_is_serializable_described_and_sided()
        {
            var kinds = typeof(Ability).Assembly.GetTypes()
                .Where(type => typeof(IAbilityValidationRule).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                .ToList();
            Assert.That(kinds.Select(kind => kind.Name), Is.SupersetOf(Sides.Keys.Select(kind => kind.Name)), "the rules PlayForge offers");
            foreach (var kind in kinds)
            {
                Assert.IsFalse(string.IsNullOrEmpty(ForgeMenuAttribute.DescriptionOf(kind)), $"{kind.Name} says what it does");
                if (!typeof(IComponentValidationRule).IsAssignableFrom(kind))
                    Assert.IsTrue(kind.IsSerializable, $"{kind.Name} is [Serializable]");
            }
            foreach (var (kind, side) in Sides)
                Assert.AreEqual(side, ValidationSideAttribute.Of(kind), $"{kind.Name} belongs among the {side} checks");
        }

        /// <summary>
        /// Each list is offered its own rules (EP-35): the Caster Checks a cost, a cooldown and the ability's level, which ask
        /// of the ability as its caster holds it; the Target Checks an affiliation, a line of sight and a facing, which ask of
        /// a target against its caster; both the rules of either.
        /// </summary>
        [Test]
        public void EP_35_Each_list_is_offered_its_own_rules()
        {
            var ability = NewAbility();
            var caster = KindsOffered(ability, nameof(Ability.SourceActivationRules));
            var target = KindsOffered(ability, nameof(Ability.TargetActivationRules));
            var either = new[] { "Is Alive", "Attribute", "Tag Weight", "Has Effect", "Holds Item" };
            Assert.That(caster, Is.SupersetOf(either.Concat(new[] { "Cost", "Cooldown", "Ability Level" })), $"the Caster Checks: {string.Join(", ", caster)}");
            Assert.That(caster, Has.No.Member("Affiliation").And.No.Member("Line of Sight").And.No.Member("Facing"), "and no target's rule");
            Assert.That(target, Is.SupersetOf(either.Concat(new[] { "Affiliation", "Line of Sight", "Facing" })), $"the Target Checks: {string.Join(", ", target)}");
            Assert.That(target, Has.No.Member("Cost").And.No.Member("Cooldown").And.No.Member("Ability Level"), "and no caster's rule");
        }

        // The names of the kinds the rule picker offers a list's element
        private static List<string> KindsOffered(Ability ability, string list)
        {
            var serialized = new SerializedObject(ability);
            var property = serialized.FindProperty(list).GetArrayElementAtIndex(0);
            var drawerType = typeof(FarEmerald.PlayForge.Editor.TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.AbilityValidationRuleDrawer");
            var drawer = Activator.CreateInstance(drawerType);
            var kinds = (IEnumerable)drawerType.GetMethod("KindsFor", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(drawer, new object[] { property });
            return kinds.Cast<object>().Select(kind => (string)kind.GetType().GetField("Item2").GetValue(kind)).ToList();
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

        // The rows the Target Checks show for the checks the components stand behind
        private static List<VisualElement> Rows(VisualElement root) =>
            root.Q("TargetComponentChecks")?.Query(name: "ComponentCheck").ToList() ?? new List<VisualElement>();
    }
}
