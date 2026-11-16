using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// A Forge Project's files (EP-10, EP-11, Decision 89, the user's editor pass of 2026-11-15): a new project is made at
    /// once in a folder of its own under the PlayForge Folder, Assets/PlayForge by default, as Assets/PlayForge/Arena, its
    /// assets in a folder for each type there and its Game Root entity in its GameRoot folder; the Bootstrap prefabs a scene
    /// starts PlayForge with are made in the PlayForge Folder's Bootstrap folder where they're missing, the Bootstrapper
    /// playing the project; and a project never lands in the folder PlayForge's own code is in. The editor's types are read
    /// by reflection. Each test works in a PlayForge Folder of its own and puts back the setting and the active project.
    /// </summary>
    public class ForgeProjectLayoutTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Folder = "Assets/EP10 PlayForge";

        private static Type Kit(string name) => typeof(TagDrawer).Assembly.GetType("FarEmerald.PlayForge.Editor." + name, true);

        private static object Static(string type, string member, params object[] args)
        {
            var kit = Kit(type);
            var property = kit.GetProperty(member, Any);
            if (property != null && args.Length == 0) return property.GetValue(null);
            return kit.GetMethods(Any).Single(each => each.Name == member && each.GetParameters().Length == args.Length).Invoke(null, args);
        }

        private static void SetFolder(string folder) => Kit("ForgeFolders").GetProperty("Setting", Any).SetValue(null, folder);

        private string keptFolder;
        private ForgeProject keptActive;

        [SetUp]
        public void Keep()
        {
            keptFolder = (string)Static("ForgeFolders", "Setting");
            keptActive = (ForgeProject)Static("ForgeProjects", "Active");
            Assume.That(AssetDatabase.IsValidFolder(Folder), Is.False, "the test's folder is free");
            SetFolder(Folder);
        }

        [TearDown]
        public void PutBack()
        {
            SetFolder(keptFolder);
            Kit("ForgeProjects").GetProperty("Active", Any).SetValue(null, keptActive);
            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>
        /// A new project is made at once in a folder of its own under the PlayForge Folder, its type folders within it, its
        /// Game Root entity in its GameRoot folder; and it's the active one.
        /// </summary>
        [Test]
        public void EP_10_A_new_project_is_made_in_a_folder_of_its_own()
        {
            var arena = (ForgeProject)Static("ForgeProjects", "Make", "EP10Arena");
            Assert.AreEqual($"{Folder}/EP10Arena/EP10Arena.asset", AssetDatabase.GetAssetPath(arena), "in a folder of its own, under the PlayForge Folder");
            Assert.AreSame(arena, Static("ForgeProjects", "Active"), "the active one");
            Assert.AreEqual($"{Folder}/EP10Arena/Abilities", Static("ForgeProjects", "FolderIn", arena, typeof(Ability)), "its abilities in its Abilities folder");
            Assert.AreEqual($"{Folder}/EP10Arena/Effects", Static("ForgeProjects", "FolderIn", arena, typeof(GameplayEffect)), "its effects in Effects");

            Assert.IsNotNull(arena.GameRootEntity, "it carries its Game Root entity (EP-11)");
            Assert.AreEqual($"{Folder}/EP10Arena/GameRoot/GameRoot.asset", AssetDatabase.GetAssetPath(arena.GameRootEntity), "in its GameRoot folder");
            Assert.AreEqual("Game Root", arena.GameRootEntity.Name);
            Assert.That(arena.GameRootEntity.Affiliation.Select(side => side.ToString()), Is.EqualTo(new[] { "System" }), "on the System side");

            var dungeon = (ForgeProject)Static("ForgeProjects", "Make", "EP10Dungeon");
            Assert.AreNotSame(arena.GameRootEntity, dungeon.GameRootEntity, "each project carries its own");
        }

        /// <summary>
        /// The first project made makes the Bootstrap prefabs, in the PlayForge Folder's Bootstrap folder, the Bootstrapper
        /// naming the GameRoot's and ProcessControl's and playing the project; a prefab deleted comes back, and the
        /// Bootstrapper's name isn't a project's to take (EP-11).
        /// </summary>
        [Test]
        public void EP_11_The_first_project_makes_the_Bootstrap_prefabs()
        {
            var arena = (ForgeProject)Static("ForgeProjects", "Make", "EP10Arena");
            var bootstrapper = AssetDatabase.LoadAssetAtPath<Bootstrapper>($"{Folder}/Bootstrap/Bootstrapper.prefab");
            Assert.IsNotNull(bootstrapper, "the Bootstrapper prefab, in the Bootstrap folder");
            Assert.AreSame(arena, bootstrapper.Project, "playing the first project");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<GameRoot>($"{Folder}/Bootstrap/GameRoot.prefab"), bootstrapper.GameRootPrefab, "naming the GameRoot prefab");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<ProcessControl>($"{Folder}/Bootstrap/ProcessControl.prefab"), bootstrapper.ProcessControlPrefab, "and ProcessControl's");
            var gameRoot = bootstrapper.GameRootPrefab;
            Assert.AreEqual(EProcessLifecycle.Synchronous, gameRoot.ProcessLifecycle, "the GameRoot steps as a GAS must");
            Assert.AreEqual(EProcessStepTiming.UpdateAndLate, gameRoot.ProcessTiming, "in Update and LateUpdate (GAS-1)");

            Static("ForgeProjects", "Make", "EP10Dungeon");
            Assert.AreSame(arena, AssetDatabase.LoadAssetAtPath<Bootstrapper>($"{Folder}/Bootstrap/Bootstrapper.prefab").Project,
                "another project leaves the Bootstrapper playing the first");

            AssetDatabase.DeleteAsset($"{Folder}/Bootstrap/GameRoot.prefab");
            Assert.AreEqual(1, Static("ForgeBootstrap", "EnsurePrefabs", arena), "a deleted prefab is made again");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Bootstrapper>($"{Folder}/Bootstrap/Bootstrapper.prefab").GameRootPrefab,
                "and the Bootstrapper names it");

            Assert.IsNotNull(Static("ForgeProjects", "WhyNotName", "Bootstrap"), "a project can't take the Bootstrap folder's name");
            Assert.IsNull(Static("ForgeProjects", "WhyNotName", "Arena"), "and can take another");
        }

        /// <summary>
        /// The PlayForge Folder is where projects go, but where it's the folder PlayForge's code is in, they go beside it, so
        /// the code's folder never holds a project's files.
        /// </summary>
        [Test]
        public void EP_10_Projects_never_land_in_PlayForges_own_code_folder()
        {
            Assert.AreEqual("Assets/PlayForge", Static("ForgeFolders", "RootFor", "Assets/PlayForge", null), "under Packages, the folder itself");
            Assert.AreEqual("Assets/PlayForge", Static("ForgeFolders", "RootFor", "Assets/PlayForge", "Assets/Plugins/PlayForge"),
                "and with the code at Assets/Plugins/PlayForge, as the Asset Store installs it (EP-12)");
            Assert.AreEqual("Assets/PlayForge Projects", Static("ForgeFolders", "RootFor", "Assets/PlayForge", "Assets/PlayForge"),
                "beside the code where the folder is the code's");
            Assert.AreEqual("Assets/PlayForge Projects", Static("ForgeFolders", "RootFor", "Assets/PlayForge/Games", "Assets/PlayForge"),
                "or inside it");
        }

        /// <summary>
        /// The Forge asks for a first project where there's none: a name, and Create, which makes it as New Project does. It's
        /// laid out in a runtime panel, as batch mode gives a window none, so Create's click is dispatched.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_10_The_Forge_asks_for_a_first_project()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("EP-10") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var first = (VisualElement)typeof(TheForge).GetMethod("FirstProject", Any).Invoke(null, null);
                document.rootVisualElement.Add(first);
                yield return null;

                var name = first.Q<TextField>("FirstProjectName");
                Assert.IsNotNull(name, "a name");
                var create = first.Q<Button>("CreateFirstProject");
                Assert.IsNotNull(create, "and Create");
                name.value = "Bootstrap";
                Press(create);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<ForgeProject>($"{Folder}/Bootstrap/Bootstrap.asset"), "not by the Bootstrap folder's name");
                StringAssert.Contains("Bootstrap", first.Q<Label>("FirstProjectProblem").text, "which it says why it can't take");

                name.value = "EP10First";
                Press(create);
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ForgeProject>($"{Folder}/EP10First/EP10First.asset"),
                    "Create makes the project, in a folder of its own");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static void Press(Button button)
        {
            using var click = NavigationSubmitEvent.GetPooled();
            click.target = button;
            button.SendEvent(click);
        }
    }
}
