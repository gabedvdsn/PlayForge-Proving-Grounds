using System;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The samples hooked up as they ship (SP-36). The Demo's Flame Wall named its fire, a process on a prefab, but the
    /// process's class sat in a script of another name, which binds by Unity's fallback alone, and a copy saved while it
    /// didn't load read None, so the wall burned nothing. And a sample's assets opened The Forge's Problems on
    /// warnings: each tag under its sample's prefix, and Burning, whose damage read its stacks as they already ticked.
    /// </summary>
    public class SampleHookupTests
    {
        // The samples where this project holds them: as Proving Grounds' local runs copy them in, as CI's gates do, or as
        // the Package Manager imports them
        private static string[] SampleFolders() =>
            new[] { "Assets/PlayForgeDemo", "Assets/PlayForgeAdditional", "Assets/PlayForgeTaskSequences", "Assets/PlayForgeProcessGroups",
                    "Assets/PlayForgeSurvivors", "Assets/PlayForge Demo", "Assets/PlayForge Additional Content",
                    "Assets/PlayForge Task Sequence Examples", "Assets/PlayForge Process Group Examples", "Assets/PlayForge Survivors",
                    "Assets/Samples/PlayForge" }
                .Where(AssetDatabase.IsValidFolder).ToArray();

        // PlayForge's assemblies a game builds with, its samples' among them: not editor code, nor tests
        private static IEnumerable<Type> ShippedTypes() => AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
            {
                var name = assembly.GetName().Name;
                return name.StartsWith("FarEmerald.PlayForge", StringComparison.Ordinal) && !name.Contains(".Editor")
                       && !name.Contains("Tests") && !name.Contains("Testing");
            })
            .SelectMany(assembly => assembly.GetTypes());

        /// <summary>
        /// Each component and asset class PlayForge and its samples ship has a script of its own name, which is how Unity
        /// binds one saved in a prefab, a scene or an asset (SP-36).
        /// </summary>
        [Test]
        public void SP_36_Each_component_and_asset_class_has_a_script_of_its_name()
        {
            var bound = new HashSet<Type>(AssetDatabase.FindAssets("t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonoScript>).Where(script => script != null).Select(script => script.GetClass())
                .Where(type => type != null));
            var unbound = ShippedTypes()
                .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition && !type.IsNested
                               && (typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type)))
                .Where(type => !bound.Contains(type))
                .Select(type => $"{type.FullName} ({type.Assembly.GetName().Name})")
                .OrderBy(name => name, StringComparer.Ordinal).ToList();
            Assert.That(unbound, Is.Empty, $"each has a script of its own name: {string.Join(", ", unbound)}");
        }

        /// <summary>No sample's prefab holds a missing script (SP-36).</summary>
        [Test]
        public void SP_36_No_sample_prefab_holds_a_missing_script()
        {
            var folders = SampleFolders();
            Assume.That(folders, Is.Not.Empty, "the samples are in the project");
            var missing = new List<string>();
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", folders).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var part in prefab.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(part.gameObject) > 0) missing.Add($"{path}: {part.name}");
            }
            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }

        /// <summary>
        /// Each sample's assets pass The Forge's checks, with no warning or error, so a game that imports a sample opens The
        /// Forge's Problems on nothing of the sample's (SP-36, with ED-91).
        /// </summary>
        [Test]
        public void SP_36_The_samples_assets_pass_The_Forges_checks()
        {
            var folders = SampleFolders();
            Assume.That(folders, Is.Not.Empty, "the samples are in the project");
            var found = new List<string>();
            foreach (var path in AssetDatabase.FindAssets("", folders).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                // PlayForge's assets, not a scene, which can't be loaded as one
                if (!path.EndsWith(".asset", StringComparison.Ordinal)) continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>())
                    foreach (var problem in ForgeValidator.Check(asset))
                        if (problem.Severity != ForgeProblemSeverity.Note) found.Add($"{path}: [{problem.Severity}] {problem.Message}");
            }
            Assert.That(found, Is.Empty, $"{found.Count} problems: {string.Join("\n", found.Take(12))}");
        }
    }
}
