using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// TW-16 in the editor: an ability's Activation holds Ignores Crowd Control, after Proc Coefficient; an entity's
    /// Abilities hold its Crowd Control; a rule's row names abilities' tags only where it stops abilities tagged; and
    /// Project Settings → PlayForge sets the project's rules.
    /// </summary>
    public class CrowdControlEditorTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string ProjectPath = "Assets/Resources/" + ProjectAttributes.ResourceName + ".asset";
        private const string RulesPath = "Assets/TW-16 Rules.asset";

        private readonly List<Object> made = new();
        private readonly List<string> madePaths = new();
        private bool madeResources;

        [TearDown]
        public void Clean()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
            foreach (var path in madePaths) AssetDatabase.DeleteAsset(path);
            madePaths.Clear();
            if (madeResources && AssetDatabase.IsValidFolder("Assets/Resources") &&
                AssetDatabase.FindAssets("", new[] { "Assets/Resources" }).Length == 0)
                AssetDatabase.DeleteAsset("Assets/Resources");
            madeResources = false;
            ForgeAttributes.Refresh();
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

        /// <summary>An ability's Activation holds what it ignores, and an entity's Abilities the rules they follow.</summary>
        [Test]
        public void TW_16_The_inspectors_hold_crowd_control()
        {
            Inspect(Instance<Ability>(), root =>
            {
                var names = Fields(root, "Activation");
                Assert.That(names.IndexOf("IgnoresCrowdControl"), Is.EqualTo(names.IndexOf("ProcCoefficient") + 1),
                    $"Ignores Crowd Control after Proc Coefficient: {string.Join(", ", names)}");
            });
            Inspect(Instance<EntityIdentity>(), root =>
                Assert.That(Fields(root, "Abilities"), Does.Contain("CrowdControl"), "an entity's own rules, with its abilities"));
        }

        /// <summary>A stun stops every ability, so its row names none; Silenced names the abilities it stops.</summary>
        [Test]
        public void TW_16_A_rules_row_names_ability_tags_only_where_it_stops_abilities_tagged()
        {
            var rules = Instance<CrowdControlRules>();
            rules.Rules.Add(new CrowdControlRule());
            rules.Rules.Add(new CrowdControlRule { Stops = ECrowdControlStops.AbilitiesTagged });
            var list = new SerializedObject(rules).FindProperty(nameof(CrowdControlRules.Rules));

            // Each row, built as its list builds it
            var every = new CrowdControlRuleDrawer().CreatePropertyGUI(list.GetArrayElementAtIndex(0));
            var tagged = new CrowdControlRuleDrawer().CreatePropertyGUI(list.GetArrayElementAtIndex(1));
            Assert.IsNotNull(every.Q<PropertyField>(nameof(CrowdControlRule.Tag)), "a rule's row has its tag");
            Assert.IsNotNull(every.Q<PropertyField>(nameof(CrowdControlRule.Stops)), "and what it stops");
            Assert.AreEqual(DisplayStyle.None, every.Q(nameof(CrowdControlRule.AbilityTags)).style.display.value, "every ability: no tags");
            Assert.AreNotEqual(DisplayStyle.None, tagged.Q(nameof(CrowdControlRule.AbilityTags)).style.display.value, "abilities tagged: their tags");
        }

        // A runtime panel to build the page in, since batch mode can't give the Settings window one
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("TW-16") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Build()
            {
                var root = new VisualElement();
                Document.rootVisualElement.Add(root);
                var pages = typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.PlayForgeSettingsPages");
                pages.GetMethod("BuildProjectPage", AnyStatic).Invoke(null, new object[] { root });
                return root;
            }

            public void Destroy()
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        /// <summary>The page's Project Attributes hold the project's crowd control rules, which an entity naming none follows.</summary>
        [UnityTest]
        public IEnumerator TW_16_The_Project_Settings_page_sets_the_projects_crowd_control()
        {
            Assume.That(AssetDatabase.LoadAssetAtPath<ProjectAttributes>(ProjectPath), Is.Null, "the project has no attributes of its own yet");
            madeResources = !AssetDatabase.IsValidFolder("Assets/Resources");
            if (madeResources) AssetDatabase.CreateFolder("Assets", "Resources");
            // Assets on disk, which go by their paths, not destroyed as instances are
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ProjectAttributes>(), ProjectPath);
            madePaths.Add(ProjectPath);
            var rules = ScriptableObject.CreateInstance<CrowdControlRules>();
            AssetDatabase.CreateAsset(rules, RulesPath);
            madePaths.Add(RulesPath);
            ForgeAttributes.Refresh();

            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Build();
                yield return Frames();

                var field = root.Q("ProjectAttributes")?.Q<ObjectField>(nameof(ProjectAttributes.CrowdControl));
                Assert.IsNotNull(field, "the page has the project's crowd control");
                Assert.AreEqual(typeof(CrowdControlRules), field.objectType, "which takes crowd control rules");
                field.value = rules;
                yield return Frames();

                Assert.AreSame(rules, AssetDatabase.LoadAssetAtPath<ProjectAttributes>(ProjectPath).CrowdControl, "set on the page, the asset's");
                Assert.AreSame(rules, ForgeAttributes.CrowdControl, "and the rules an entity naming none follows");
            }
            finally
            {
                host.Destroy();
            }
        }
    }
}
