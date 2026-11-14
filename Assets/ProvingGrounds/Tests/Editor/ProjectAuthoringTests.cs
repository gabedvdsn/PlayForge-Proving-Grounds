using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-30, Decision 57: the authoring settings live on the Forge Project, each game's its own: where each type's new
    /// assets go and how their files are named, each type's defaults, and the Cost and Cooldown templates. They were the
    /// whole Unity project's, in ProjectSettings/PlayForgeSettings.asset, which the first project to need them takes, once.
    /// Each test uses projects of its own, so it never writes to the project's own.
    /// </summary>
    public class ProjectAuthoringTests
    {
        private readonly List<string> madeFolders = new();
        private ForgeProject previous;
        private bool used;

        [TearDown]
        public void Restore()
        {
            if (used) PlayForgeSettings.UseProject(previous);
            used = false;
            foreach (var folder in madeFolders) AssetDatabase.DeleteAsset(folder);
            madeFolders.Clear();
        }

        // A project of the test's own, in a folder of its own
        private ForgeProject MakeProject(string name)
        {
            var folder = $"Assets/{name}";
            Assume.That(AssetDatabase.IsValidFolder(folder), Is.False, $"{folder} is free");
            AssetDatabase.CreateFolder("Assets", name);
            madeFolders.Add(folder);
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            AssetDatabase.CreateAsset(project, $"{folder}/Forge Project.asset");
            return project;
        }

        private void Use(ForgeProject project)
        {
            var was = PlayForgeSettings.UseProject(project);
            if (!used) previous = was;
            used = true;
        }

        /// <summary>Each project keeps its own: a folder and file names set in one are in its asset, and another has its own.</summary>
        [Test]
        public void TW_30_Each_project_keeps_its_own_authoring_settings()
        {
            var arena = MakeProject("TW-30 Arena");
            var dungeon = MakeProject("TW-30 Dungeon");

            Use(arena);
            PlayForgeSettings.SetNewAssets(typeof(Ability), "Assets/Data/TW-30 Abilities", "AB_", "_A");
            Assert.AreEqual("Assets/Data/TW-30 Abilities", PlayForgeSettings.FolderFor(typeof(Ability)), "the arena's folder for abilities");
            Assert.AreEqual("AB_", PlayForgeSettings.PrefixFor(typeof(Ability)), "and its prefix");
            Assert.AreEqual("Assets/Data/TW-30 Abilities", arena.Settings.Authoring.NewAssets.Find(entry => entry.Type == nameof(Ability))?.Folder,
                "held by the arena's project");
            StringAssert.Contains("TW-30 Abilities", File.ReadAllText(AssetDatabase.GetAssetPath(arena)), "and saved in its asset");

            Use(dungeon);
            Assert.AreEqual(PlayForgeSettings.DefaultFolder(typeof(Ability)), PlayForgeSettings.FolderFor(typeof(Ability)), "the dungeon has its own");
            Assert.AreEqual("", PlayForgeSettings.PrefixFor(typeof(Ability)), "with no prefix");

            Use(arena);
            Assert.AreEqual("AB_", PlayForgeSettings.PrefixFor(typeof(Ability)), "and the arena's are as it left them");
        }
    }
}
