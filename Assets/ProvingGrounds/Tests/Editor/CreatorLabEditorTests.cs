using System;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Lab;
using FarEmerald.PlayForge.Lab.Editor;
using NUnit.Framework;
using UnityEditor;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>The Creator Lab in the editor (FEAT-35): how it's opened, and the library it lists.</summary>
    public class CreatorLabEditorTests
    {
        /// <summary>Window → PlayForge → Creator Lab opens it, and the editor gives it its library and The Forge.</summary>
        [Test]
        public void LAB_1_The_Window_menu_opens_the_lab()
        {
            var open = typeof(CreatorLabMenu).GetMethod("OpenFromMenu", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(open, "the menu's command");
            Assert.AreEqual("Window/PlayForge/Creator Lab", open.GetCustomAttribute<MenuItem>()?.menuItem, "under Window → PlayForge");
            Assert.IsNotNull(CreatorLab.DefaultLibrary, "the editor gives the lab its library");
            Assert.IsNotNull(CreatorLab.OpenInForge, "and The Forge to open an asset in");
            Assert.IsInstanceOf<EditorLabLibrary>(CreatorLab.DefaultLibrary(), "every asset of the project");
        }

        /// <summary>The editor's library lists the project's assets by kind, each kind by name.</summary>
        [Test]
        public void LAB_5_The_editors_library_lists_the_projects_assets_by_kind()
        {
            var library = new EditorLabLibrary("Assets/ProvingGrounds/Dota");
            Assert.That(library.Name, Is.EqualTo("Every asset"));
            var abilities = library.Assets(LabAssetKind.Abilities).Select(LabAssets.NameOf).ToList();
            Assert.That(abilities, Has.Member("Counter Helix"), "its abilities");
            Assert.That(library.Assets(LabAssetKind.Items).Select(LabAssets.NameOf), Has.Member("Desolator"), "its items");
            Assert.That(library.Assets(LabAssetKind.Entities), Is.Not.Empty, "its entities");
            Assert.That(abilities, Is.Ordered.Using((System.Collections.IComparer)StringComparer.OrdinalIgnoreCase), "each kind by name");
            Assert.That(library.Assets(LabAssetKind.Effects).All(asset => asset is GameplayEffect), Is.True, "each under its own kind");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Forge Projects in the library (LAB-18)
        // ═══════════════════════════════════════════════════════════════════════════

        private const System.Reflection.BindingFlags AnyStatic =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        private static readonly Type ForgeProjects =
            typeof(FarEmerald.PlayForge.Extended.Editor.TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeProjects");

        // The active project, as The Forge reads and sets it
        private static Object ActiveProject
        {
            get => (Object)ForgeProjects.GetProperty("Active", AnyStatic).GetValue(null);
            set => ForgeProjects.GetProperty("Active", AnyStatic).SetValue(null, value);
        }

        // A project in a folder of its own, with an attribute in it
        private static (ForgeProject Project, Attribute Attribute) ProjectWith(string folder, string attributeName)
        {
            AssetDatabase.CreateFolder("Assets", folder);
            var project = UnityEngine.ScriptableObject.CreateInstance<ForgeProject>();
            AssetDatabase.CreateAsset(project, $"Assets/{folder}/{folder}.asset");
            var attribute = UnityEngine.ScriptableObject.CreateInstance<Attribute>();
            attribute.Name = attributeName;
            AssetDatabase.CreateAsset(attribute, $"Assets/{folder}/{attributeName}.asset");
            return (project, attribute);
        }

        /// <summary>
        /// The editor's library plays the active Forge Project's assets, as The Forge's pickers offer them, and switches the
        /// active project as The Forge does; switched there, it follows as it's read again.
        /// </summary>
        [Test]
        public void LAB_18_The_editors_library_plays_the_active_projects_assets_and_switches_them()
        {
            var kept = ActiveProject;
            try
            {
                var (arena, arenaHealth) = ProjectWith("LAB18 Arena", "LAB18 Arena Health");
                var (dungeon, dungeonHealth) = ProjectWith("LAB18 Dungeon", "LAB18 Dungeon Health");
                ActiveProject = arena;

                var library = new EditorLabLibrary();
                Assert.AreEqual(arena.name, library.Name, "it's named for the active project");
                Assert.That(library.Assets(LabAssetKind.Attributes), Has.Member(arenaHealth).And.No.Member(dungeonHealth),
                    "and plays its assets alone");
                Assert.That(library.Projects, Does.Contain(arena.name).And.Contain(dungeon.name), "it offers each project");
                Assert.AreEqual(library.Projects.ToList().IndexOf(arena.name), library.Project, "the active one chosen");

                library.UseProject(library.Projects.ToList().IndexOf(dungeon.name));
                Assert.AreSame(dungeon, ActiveProject, "switching it switches the active project, as The Forge does");
                Assert.AreEqual(dungeon.name, library.Name);
                Assert.That(library.Assets(LabAssetKind.Attributes), Has.Member(dungeonHealth).And.No.Member(arenaHealth),
                    "and plays the other's assets");

                ActiveProject = arena;
                Assert.AreEqual(library.Projects.ToList().IndexOf(arena.name), library.Project, "switched in The Forge, it says so");
                library.Refresh();
                Assert.That(library.Assets(LabAssetKind.Attributes), Has.Member(arenaHealth), "and plays it as it's read again");

                var folders = new EditorLabLibrary("Assets/LAB18 Dungeon");
                Assert.IsEmpty(folders.Projects, "a library of folders offers no projects");
                Assert.That(folders.Assets(LabAssetKind.Attributes), Has.Member(dungeonHealth), "and plays its folders' assets");
            }
            finally
            {
                ActiveProject = kept;
                AssetDatabase.DeleteAsset("Assets/LAB18 Arena");
                AssetDatabase.DeleteAsset("Assets/LAB18 Dungeon");
            }
        }
    }
}
