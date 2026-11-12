using System;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Lab;
using FarEmerald.PlayForge.Lab.Editor;
using NUnit.Framework;
using UnityEditor;

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
    }
}
