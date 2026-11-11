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
    /// Forge Projects (FEAT-34, Decision 44): a project is a folder with a project asset at its root. The Forge shows the
    /// active project's assets alone and switches between projects, new assets go into the active one, and a project
    /// names its own assets for saves.
    /// </summary>
    public class ForgeProjectTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly System.Type Projects = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeProjects");
        private readonly List<string> folders = new();
        private Object keptActive;

        [SetUp]
        public void Keep() => keptActive = Active;

        [TearDown]
        public void Clean()
        {
            foreach (var folder in folders) AssetDatabase.DeleteAsset(folder);
            folders.Clear();
            Active = keptActive;
            foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
        }

        // The active project, as The Forge reads and sets it
        private static Object Active
        {
            get => (Object)Projects.GetProperty("Active", AnyStatic).GetValue(null);
            set => Projects.GetProperty("Active", AnyStatic).SetValue(null, value);
        }

        // A project of its own, in a folder of the name given, which the test deletes
        private ForgeProject Project(string folder)
        {
            CreateFolders(folder);
            if (!folder.Contains("/Inner")) folders.Add(folder);
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            var name = folder.Substring(folder.LastIndexOf('/') + 1);
            AssetDatabase.CreateAsset(project, $"{folder}/{name}.asset");
            return project;
        }

        private static void CreateFolders(string folder)
        {
            var parts = folder.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                var parent = string.Join("/", parts.Take(i));
                if (!AssetDatabase.IsValidFolder($"{parent}/{parts[i]}")) AssetDatabase.CreateFolder(parent, parts[i]);
            }
        }

        private static Ability AbilityIn(string folder, string name)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = name;
            AssetDatabase.CreateAsset(ability, $"{folder}/{name}.asset");
            return ability;
        }

        private static IEnumerator Frames(int count = 4)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static List<Object> Listed(TheForge forge) =>
            forge.rootVisualElement.Q<MultiColumnListView>("Assets").itemsSource.Cast<Object>().ToList();

        /// <summary>
        /// The Forge showed every PlayForge asset in the project (the user's request, FEAT-34). It shows the active Forge
        /// Project's alone: those under its folder, and in no project inside it, as a folder's project is the innermost
        /// that holds it. Its project menu switches to another.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_34_The_forge_shows_the_active_projects_assets()
        {
            var log = new LogGuard();
            try
            {
                var arena = Project("Assets/FEAT34 Arena");
                var dungeon = Project("Assets/FEAT34 Dungeon");
                Project("Assets/FEAT34 Arena/Inner");
                var fire = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Fireball");
                var inner = AbilityIn("Assets/FEAT34 Arena/Inner", "FEAT34 Inner Bolt");
                var torch = AbilityIn("Assets/FEAT34 Dungeon", "FEAT34 Torchlight");
                Active = arena;

                var forge = (TheForge)typeof(TheForge).GetMethod("Open", AnyStatic).Invoke(null, null);
                yield return Frames();
                var listed = Listed(forge);
                Assert.That(listed, Has.Member(fire), "The Forge shows the active project's assets");
                Assert.That(listed, Has.No.Member(torch), "and not another's");
                Assert.That(listed, Has.No.Member(inner), "nor a project's inside it");

                var menu = forge.rootVisualElement.Q<ToolbarMenu>("Project");
                Assert.IsNotNull(menu, "its toolbar has the project menu");
                Assert.AreEqual("FEAT34 Arena", menu.text, "naming the active project");
                var other = menu.menu.MenuItems().OfType<DropdownMenuAction>().FirstOrDefault(action => action.name == "FEAT34 Dungeon");
                Assert.IsNotNull(other, "which lists the others");
                other.Execute();
                yield return Frames(8);
                Assert.AreSame(dungeon, Active, "choosing one makes it the active one");
                listed = Listed(forge);
                Assert.That(listed, Has.Member(torch), "and The Forge shows its assets");
                Assert.That(listed, Has.No.Member(fire), "and not the one before's");
            }
            finally
            {
                // Closed while the guard is on: a window shown in batch mode says there's no graphics device as it's drawn
                foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while The Forge was open");
        }

        /// <summary>A new asset goes into the active project: its type's folder within the project's (FEAT-34).</summary>
        [Test]
        public void FEAT_34_A_new_asset_goes_into_the_active_project()
        {
            Active = Project("Assets/FEAT34 Arena");
            var made = (Object)typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeAssetCreation")
                .GetMethod("CreateNew", AnyStatic).Invoke(null, new object[] { typeof(Ability), "FEAT34 Made", null });
            StringAssert.StartsWith("Assets/FEAT34 Arena/", AssetDatabase.GetAssetPath(made), "it's made in the active project's folder");
        }

        /// <summary>
        /// A project names its own assets for saves, as the Asset Catalog it grew from names every one: those in its folder
        /// and in no project inside it (FEAT-34, FEAT-9).
        /// </summary>
        [Test]
        public void FEAT_34_A_project_names_its_own_assets_for_saves()
        {
            var arena = Project("Assets/FEAT34 Arena");
            Project("Assets/FEAT34 Arena/Inner");
            var fire = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Fireball");
            var inner = AbilityIn("Assets/FEAT34 Arena/Inner", "FEAT34 Inner Bolt");
            Assert.That(arena.AddOwnAssets(), Is.GreaterThanOrEqualTo(1), "it adds its assets");
            Assert.IsTrue(arena.TryGetId(fire, out _), "naming its own");
            Assert.IsFalse(arena.TryGetId(inner, out _), "and not a project's inside it");
        }

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens
        private sealed class LogGuard : System.IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public List<string> Errors { get; } = new();

            public LogGuard()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(NoDevice))
                    Errors.Add($"{type}: {message}");
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
