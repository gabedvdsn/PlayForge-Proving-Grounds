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
    /// TW-29 in the editor: Project Settings → PlayForge shows a Forge Project's settings, its roles and its default crowd
    /// control, and fills the built-in roles by name; an attribute's inspector says the roles it plays in its project; an
    /// ability's Activation holds its attribute costs after its Cost; and the Problems window says where a cost spends
    /// nothing. Each case that needs an asset in a project makes a project of its own in a folder it takes away after.
    /// </summary>
    public class ProjectSettingsEditorTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Folder = "Assets/TW-29 Project";

        private readonly List<Object> made = new();
        private bool madeFolder;

        [TearDown]
        public void Clean()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
            if (madeFolder) AssetDatabase.DeleteAsset(Folder);
            madeFolder = false;
        }

        private T Instance<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            made.Add(instance);
            return instance;
        }

        // A project of the test's own, in a folder of its own, and an asset in it
        private ForgeProject MakeProject()
        {
            Assume.That(AssetDatabase.IsValidFolder(Folder), Is.False, "the folder is free");
            AssetDatabase.CreateFolder("Assets", "TW-29 Project");
            madeFolder = true;
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            AssetDatabase.CreateAsset(project, $"{Folder}/Forge Project.asset");
            return project;
        }

        private static T InProject<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, $"{Folder}/{name}.asset");
            return asset;
        }

        private static Attribute NamedAttribute(string name)
        {
            var attribute = InProject<Attribute>(name);
            attribute.Name = name;
            EditorUtility.SetDirty(attribute);
            return attribute;
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

        private static List<string> Messages(Object asset) => ForgeValidator.Check(asset).Select(problem => problem.Message).ToList();

        private static Type Pages => typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.PlayForgeSettingsPages");

        // A runtime panel to build the section in, since batch mode can't give the Settings window one
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("TW-29") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Build(ForgeProject project)
            {
                var section = (VisualElement)Pages.GetMethod("ForgeProjectSettings", AnyStatic).Invoke(null, new object[] { project });
                Document.rootVisualElement.Add(section);
                return section;
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

        /// <summary>The page shows a project's roles and its crowd control, sets them, and fills the built-in roles by name.</summary>
        [UnityTest]
        public IEnumerator TW_29_The_settings_page_edits_a_projects_settings()
        {
            var project = MakeProject();
            var health = NamedAttribute("Health");
            NamedAttribute("Luck");
            var rules = InProject<CrowdControlRules>("Rules");
            AssetDatabase.SaveAssets();

            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var section = host.Build(project);
                yield return Frames();

                Assert.IsNotNull(section.Q<PropertyField>("Roles"), "its roles");
                var crowdControl = section.Q<PropertyField>("CrowdControl")?.Q<ObjectField>();
                Assert.IsNotNull(crowdControl, "and its crowd control");
                crowdControl.value = rules;
                yield return Frames();
                Assert.AreSame(rules, project.Settings.CrowdControl, "set on the page, the project's");

                var fill = section.Q<Button>("FillRolesByName");
                Assert.IsNotNull(fill, "Fill Roles by Name");
                using (var click = NavigationSubmitEvent.GetPooled())
                {
                    click.target = fill;
                    fill.SendEvent(click);
                }
                yield return Frames();
                Assert.AreSame(health, project.Settings.AttributeFor(Tags.ROLE_HEALTH), "the Health role, filled by the attribute of its name");
                Assert.AreEqual("Luck", project.Settings.AttributeFor(Tags.ROLE_LUCK)?.Name, "and the Luck role");
                Assert.IsNull(project.Settings.AttributeFor(Tags.ROLE_MOVEMENT_SPEED), "none for a role no attribute is named for");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>
        /// The page holds the project's Workers, which every entity in it has, the Lifesteal worker among them as a project
        /// starts; and Fill Roles by Name fills the Lifesteal role (FN-15).
        /// </summary>
        [UnityTest]
        public IEnumerator FN_15_The_settings_page_holds_the_projects_workers()
        {
            var project = MakeProject();
            var lifesteal = NamedAttribute("Lifesteal");
            AssetDatabase.SaveAssets();
            Assert.That(project.Settings.Workers.ImpactWorkers, Has.Some.InstanceOf<LifestealImpactWorker>(), "a project starts with the Lifesteal worker");

            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var section = host.Build(project);
                yield return Frames();
                Assert.IsNotNull(section.Q<PropertyField>("Workers"), "its workers, every entity's, on the page");
                Assert.IsNotNull(section.Q<PropertyField>("PurgeLevels"), "and its Purge Levels (FN-16)");

                var fill = section.Q<Button>("FillRolesByName");
                using (var click = NavigationSubmitEvent.GetPooled())
                {
                    click.target = fill;
                    fill.SendEvent(click);
                }
                yield return Frames();
                Assert.AreSame(lifesteal, project.Settings.AttributeFor(Tags.ROLE_LIFESTEAL), "the Lifesteal role, filled by the attribute of its name");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>An attribute says the roles it plays in its project; one that plays none says nothing.</summary>
        [Test]
        public void TW_29_An_attributes_inspector_says_the_roles_it_plays()
        {
            var project = MakeProject();
            var health = NamedAttribute("Health");
            var vigor = NamedAttribute("Vigor");
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            EditorUtility.SetDirty(project);
            AssetDatabase.SaveAssets();

            Inspect(health, root =>
            {
                var part = root.Q<Label>("ProjectPart");
                Assert.IsNotNull(part, "a note of its part");
                StringAssert.Contains(Tags.ROLE_HEALTH.ToString(), part.text, "it names the role");
                StringAssert.Contains(project.name, part.text, "and its project");
                Assert.AreNotEqual(DisplayStyle.None, part.style.display.value, "shown");
            });
            Inspect(vigor, root =>
            {
                var part = root.Q<Label>("ProjectPart");
                Assert.That(part == null || part.style.display.value == DisplayStyle.None, "one that plays none has none");
            });
        }

        /// <summary>An ability's Activation holds its attribute costs after its Cost, and its line says them.</summary>
        [Test]
        public void TW_29_An_abilitys_Activation_holds_its_attribute_costs()
        {
            var mana = Instance<Attribute>();
            mana.Name = "Mana";
            var bolt = Instance<Ability>();
            bolt.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = 30f });

            Inspect(bolt, root =>
            {
                var container = root.Q(name: "Activation_CONTAINER");
                Assert.IsNotNull(container, "the inspector has an Activation section");
                var names = container.Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(names.IndexOf("AttributeCosts"), Is.EqualTo(names.IndexOf("Cost") + 1), $"after Cost: {string.Join(", ", names)}");
                Assert.IsFalse(names.Contains("ManaCost"), "and no Mana Cost");
                StringAssert.Contains("30 Mana", container.Q<Label>(className: "pf-section__summary").text, "its line says the cost");
            });
        }

        /// <summary>A cost that names nothing to spend is a problem, and so is one naming a role its project doesn't fill.</summary>
        [Test]
        public void TW_29_A_cost_with_nothing_to_spend_is_a_problem()
        {
            var project = MakeProject();
            var energy = TagOf("Test.TW29.Energy");
            var bolt = InProject<Ability>("Bolt");
            bolt.AttributeCosts.Add(new AttributeCost { Amount = 10f });
            Assert.That(Messages(bolt), Has.Some.Contains("names no attribute"), "a cost naming neither an attribute nor a role");

            bolt.AttributeCosts[0].Role = energy;
            Assert.That(Messages(bolt), Has.Some.Contains("no attribute plays"), "one naming a role its project doesn't fill");

            var mana = NamedAttribute("Mana");
            project.Settings.Roles.Add(new AttributeRole { Role = energy, Attribute = mana });
            Assert.That(Messages(bolt), Has.None.Contains("no attribute plays"), "with the role filled, none");
        }

        private static Tag TagOf(string name) => Tag.Generate(name);
    }
}
