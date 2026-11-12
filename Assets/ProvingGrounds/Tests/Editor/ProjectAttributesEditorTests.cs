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
    /// TW-28: the project's attributes in the editor. Project Settings → PlayForge makes the asset that holds them and
    /// sets each slot, filling the empty ones by name; an attribute's inspector says its part; an ability's Activation holds
    /// its Mana Cost; and the Problems window says where a Mana Cost has nothing to pay it.
    /// </summary>
    public class ProjectAttributesEditorTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string AssetPath = "Assets/Resources/" + ProjectAttributes.ResourceName + ".asset";

        private readonly List<Object> made = new();
        private readonly List<string> madePaths = new();
        private ProjectAttributes previous;
        private bool projectSet;
        private bool madeResources;

        [TearDown]
        public void Clean()
        {
            if (projectSet) ForgeAttributes.Use(previous);
            projectSet = false;
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
            var made = ScriptableObject.CreateInstance<T>();
            this.made.Add(made);
            return made;
        }

        private void Use(ProjectAttributes project)
        {
            var was = ForgeAttributes.Use(project);
            if (!projectSet) previous = was;
            projectSet = true;
        }

        private static Type Pages => typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.PlayForgeSettingsPages");

        // A runtime panel to build a page in, since batch mode can't give the Settings window one
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("TW-28") { hideFlags = HideFlags.HideAndDontSave };
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
                Pages.GetMethod("BuildProjectPage", AnyStatic).Invoke(null, new object[] { root });
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

        private static string Summary(VisualElement root, string section)
        {
            var container = root.Q(name: $"{section}_CONTAINER");
            Assert.IsNotNull(container, $"the inspector has a {section} section");
            return container.Q<Label>(className: "pf-section__summary").text;
        }

        private static string Plain(string name) => new string((name ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        /// <summary>
        /// The page's Project Attributes make the asset that holds them, in a Resources folder so a build has it, filling
        /// its slots by name; a slot set on the page is the asset's; and Fill by Name fills only the empty ones.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_28_The_Project_Settings_page_sets_the_project_attributes()
        {
            Assume.That(AssetDatabase.LoadAssetAtPath<ProjectAttributes>(AssetPath), Is.Null, "the project has no attributes of its own yet");
            madeResources = !AssetDatabase.IsValidFolder("Assets/Resources");
            // Assets on disk, which go by their paths, not destroyed as instances are
            var mine = ScriptableObject.CreateInstance<Attribute>();
            mine.Name = "TW-28 Vigor";
            AssetDatabase.CreateAsset(mine, "Assets/TW-28 Vigor.asset");
            madePaths.Add("Assets/TW-28 Vigor.asset");
            var named = ScriptableObject.CreateInstance<Attribute>();
            named.Name = "Mana";
            AssetDatabase.CreateAsset(named, "Assets/TW-28 Mana.asset");
            madePaths.Add("Assets/TW-28 Mana.asset");

            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Build();
                yield return Frames();

                var section = root.Q("ProjectAttributes");
                Assert.IsNotNull(section, "the page has the project's attributes");
                var create = section.Q<Button>("CreateProjectAttributes");
                Assert.IsNotNull(create, "with a button that makes their asset, as the project has none");
                using (var click = NavigationSubmitEvent.GetPooled())
                {
                    click.target = create;
                    create.SendEvent(click);
                }
                yield return Frames();
                madePaths.Insert(0, AssetPath);

                var asset = AssetDatabase.LoadAssetAtPath<ProjectAttributes>(AssetPath);
                Assert.IsNotNull(asset, "made in a Resources folder");
                Assert.AreSame(asset, ForgeAttributes.Project, "and the project's attributes are its");
                Assert.AreEqual("mana", Plain(asset.Mana ? asset.Mana.Name : null), "its Mana filled by name");

                section = root.Q("ProjectAttributes");
                var healthField = section.Q<ObjectField>(nameof(ProjectAttributes.Health));
                Assert.IsNotNull(healthField, "a field for each slot");
                foreach (var slot in new[] { nameof(ProjectAttributes.Mana), nameof(ProjectAttributes.Luck), nameof(ProjectAttributes.MovementSpeed) })
                    Assert.IsNotNull(section.Q<ObjectField>(slot), $"{slot} too");
                healthField.value = mine;
                yield return Frames();
                Assert.AreSame(mine, asset.Health, "a slot set on the page is the asset's");

                asset.Mana = null;
                var fill = section.Q<Button>("FillByName");
                Assert.IsNotNull(fill, "Fill by Name");
                using (var click = NavigationSubmitEvent.GetPooled())
                {
                    click.target = fill;
                    fill.SendEvent(click);
                }
                yield return Frames();
                Assert.AreSame(mine, asset.Health, "fills only the empty slots, so the Health set stays");
                Assert.AreEqual("mana", Plain(asset.Mana ? asset.Mana.Name : null), "and the empty Mana is filled again");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>An attribute in one of the project's slots says so in its inspector; another says nothing.</summary>
        [Test]
        public void TW_28_An_attributes_inspector_says_its_part_in_the_project()
        {
            var health = Instance<Attribute>();
            health.Name = "Health";
            var other = Instance<Attribute>();
            other.Name = "Vigor";
            var project = Instance<ProjectAttributes>();
            project.Health = health;
            Use(project);

            Inspect(health, root =>
            {
                var part = root.Q<Label>("ProjectPart");
                Assert.IsNotNull(part, "a note of its part");
                StringAssert.Contains("This project's Health", part.text);
                Assert.AreNotEqual(DisplayStyle.None, part.style.display.value, "shown");
            });
            Inspect(other, root =>
            {
                var part = root.Q<Label>("ProjectPart");
                Assert.That(part == null || part.style.display.value == DisplayStyle.None, "another has none");
            });
        }

        /// <summary>An ability's Activation holds its Mana Cost after its Cost, and its line says the cost where it has no Cost effect.</summary>
        [Test]
        public void TW_28_An_abilitys_Activation_holds_its_Mana_Cost()
        {
            var mana = Instance<Attribute>();
            mana.Name = "Mana";
            var project = Instance<ProjectAttributes>();
            project.Mana = mana;
            Use(project);
            var bolt = Instance<Ability>();
            bolt.ManaCost = 30f;

            Inspect(bolt, root =>
            {
                var names = root.Q(name: "Activation_CONTAINER").Query<PropertyField>().ToList().Select(field => field.name).ToList();
                Assert.That(names.IndexOf("ManaCost"), Is.EqualTo(names.IndexOf("Cost") + 1), $"Mana Cost after Cost: {string.Join(", ", names)}");
                StringAssert.Contains("30 Mana", Summary(root, "Activation"), "its line says the cost");
            });
        }

        /// <summary>A Mana Cost with no Mana of the project's to pay it is a problem; it's a cost, so one no stage applies is too.</summary>
        [Test]
        public void TW_28_A_Mana_Cost_with_nothing_to_pay_it_is_a_problem()
        {
            var bolt = Instance<Ability>();
            bolt.ManaCost = 30f;
            Use(Instance<ProjectAttributes>());

            var messages = ForgeValidator.Check(bolt).Select(problem => problem.Message).ToList();
            Assert.That(messages, Has.Some.Contains("no Mana"), $"no Mana to pay it: {string.Join(" | ", messages)}");
            Assert.That(messages, Has.Some.Contains("the cost never applies"), "and no stage applies it");

            var project = Instance<ProjectAttributes>();
            project.Mana = Instance<Attribute>();
            Use(project);
            messages = ForgeValidator.Check(bolt).Select(problem => problem.Message).ToList();
            Assert.That(messages, Has.None.Contains("no Mana"), "with the project's Mana, it's paid");
        }
    }
}
