using System;
using System.IO;
using System.Linq;
using FarEmerald.PlayForge.ProvingGrounds.Recipes.Building;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// AC-1: the Additional Content sample's code is an assembly of its own, which nothing in PlayForge references, so a
    /// project builds without it, and its tests compile only where tests do. Read from the package's own files, as a
    /// package's Samples~ folder isn't imported. AC-2: the export saves the sample's Sparring scene, every handler on,
    /// where the sample is in the project, as CI and the private copy import it.
    /// </summary>
    public class AdditionalContentSampleTests
    {
        private const string Assembly = "FarEmerald.PlayForge.AdditionalContent";

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name;
            public string[] references = Array.Empty<string>();
            public string[] defineConstraints = Array.Empty<string>();
            public bool autoReferenced = true;
        }

        private static string PackageRoot =>
            PackageInfo.FindForAssetPath("Packages/com.faremerald.playforge/package.json")?.resolvedPath;

        private static string SampleRoot => Path.Combine(PackageRoot, "Samples~", "Additional Content");

        private static AssemblyDefinition Read(string path) => JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path));

        private const string Recipes = "Assets/AC2 Recipes";
        private const string Formulas = "Assets/AC2 Formulas";
        private const string Starter = "Assets/AC2 Starter Set";
        private const string Sparring = "Assets/AC2 Sparring";

        [TearDown]
        public void Clean()
        {
            foreach (var root in new[] { Recipes, Formulas, Starter, Sparring }) AssetDatabase.DeleteAsset(root);
        }

        private static Type Sample(string name) =>
            Type.GetType($"FarEmerald.PlayForge.AdditionalContent.{name}, FarEmerald.PlayForge.AdditionalContent");

        [Test]
        public void AC_1_The_additional_content_sample_is_an_assembly_of_its_own()
        {
            Assert.IsNotNull(PackageRoot, "PlayForge is a package here");
            string scripts = Path.Combine(SampleRoot, "Scripts");
            string own = Path.Combine(scripts, Assembly + ".asmdef");
            Assert.IsTrue(File.Exists(own), "the sample's scripts have an assembly definition");
            var definition = Read(own);
            Assert.AreEqual(Assembly, definition.name);
            Assert.That(definition.references, Is.SubsetOf(new[] { "FarEmerald.PlayForge", "Unity.TextMeshPro", "UnityEngine.UI" }),
                "it reads PlayForge, uGUI and TextMeshPro alone");
            Assert.IsEmpty(definition.defineConstraints, "it compiles wherever the sample is imported");

            var sources = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories);
            Assert.That(sources, Is.Not.Empty, "it has scripts");
            foreach (var source in sources)
                Assert.IsTrue(File.Exists(source + ".meta"), $"{Path.GetFileName(source)} has a meta, as a sample's files need");

            string tests = Path.Combine(scripts, "Tests", Assembly + ".Tests.asmdef");
            Assert.IsTrue(File.Exists(tests), "its tests have an assembly of their own");
            var testDefinition = Read(tests);
            Assert.That(testDefinition.defineConstraints, Does.Contain("UNITY_INCLUDE_TESTS"), "they compile only where tests do");
            Assert.That(testDefinition.references, Does.Contain(Assembly));
            Assert.IsFalse(testDefinition.autoReferenced);

            // Nothing outside the sample names it: the runtime, the editor and the test kit build without it
            var outside = Directory.GetFiles(PackageRoot, "*.asmdef", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Samples~/"))
                .Where(p => Read(p).references.Contains(Assembly))
                .Select(Path.GetFileName)
                .ToList();
            Assert.IsEmpty(outside, "no PlayForge assembly references the sample");
        }

        [Test]
        public void AC_2_The_export_saves_the_sparring_scene_with_every_handler_on()
        {
            Assume.That(Sample("Sparring"), Is.Not.Null, "the sample's scripts are in the project");
            RecipeExport.SaveAll(Recipes, Formulas, Starter, Sparring);
            string path = $"{Sparring}/Sparring.unity";
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), "the scene is saved beside its dummy");
            var identity = AssetDatabase.LoadAssetAtPath<EntityIdentity>($"{Sparring}/Sparring Dummy.asset");
            Assert.IsNotNull(identity, "and the dummy's identity");
            Assert.AreEqual(2, identity.StartingAbilities.Count, "which starts with the melee attack and the bolt");

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.IsTrue(roots.Any(r => PrefabUtility.GetCorrespondingObjectFromSource(r) is { } source &&
                                             AssetDatabase.GetAssetPath(source) == RecipeExport.BootstrapperPath),
                    "PlayForge's Bootstrapper brings PlayForge up");
                Assert.IsTrue(roots.Any(r => r.GetComponent<Camera>() != null && r.GetComponent(Sample("CameraShaker")) != null),
                    "the camera has the sample's shaker");
                var dummies = roots.Select(r => r.GetComponent<GameplayAbilitySystem>()).Where(g => g != null).ToArray();
                Assert.AreEqual(2, dummies.Length, "two dummies");
                foreach (var dummy in dummies)
                {
                    Assert.AreSame(identity, dummy.EntityData, $"{dummy.name} is the dummy");
                    foreach (var name in new[] { "FloatingNumbers", "HitFlash", "HitSound", "StatusBar" })
                    {
                        var feedback = dummy.GetComponent(Sample(name));
                        Assert.IsNotNull(feedback, $"{dummy.name} has {name}");
                        Assert.IsNotNull(Sample(name).GetField("Attribute").GetValue(feedback), $"its {name} shows its health");
                    }
                    var sparring = dummy.GetComponent(Sample("Sparring"));
                    Assert.IsNotNull(sparring, $"{dummy.name} spars");
                    var partner = (GameplayAbilitySystem)Sample("Sparring").GetField("Partner").GetValue(sparring);
                    Assert.IsTrue(partner != null && partner != dummy && dummies.Contains(partner), "with the other");
                }
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            RecipeExport.SaveAll(Recipes, Formulas, Starter, Sparring);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), "a rebuild saves over it, keeping its GUID");
        }
    }
}
