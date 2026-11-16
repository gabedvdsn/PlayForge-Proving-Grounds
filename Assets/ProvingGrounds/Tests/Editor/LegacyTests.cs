using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Legacy, obsolete and asset-updating code is gone, all of it (FN-21, Decision 83): no field keeps an older save's data
    /// to move, no asset moves it as it loads, no menu upgrades assets, the settings keep no file of their own, no asset
    /// holds a legacy field, each saved as it is now, and nothing is obsolete.
    /// </summary>
    public class LegacyTests
    {
        private static readonly Assembly Runtime = typeof(GameplayEffect).Assembly;
        private static readonly Assembly EditorAssembly = typeof(PlayForgeSettings).Assembly;

        private static IEnumerable<Type> PlayForgeTypes() => Runtime.GetTypes().Concat(EditorAssembly.GetTypes());

        private const BindingFlags Every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        [Test]
        public void FN_21_No_code_moves_an_older_saves_data()
        {
            string[] gone =
            {
                "LegacyNumber", "IMovesToComponents", "LifestealEntry", "ForgeUpgrade", "ForgeComponentPass", "OlderLifestealRule",
                "PlayForgeSettingsData", "ScalerMagnitudeOperation", "ScalerIntegerMagnitudeOperation", "RetainedGroup",
            };
            var names = new HashSet<string>(PlayForgeTypes().Select(type => type.Name));
            foreach (var name in gone) Assert.IsFalse(names.Contains(name), $"{name} is gone");

            foreach (var type in PlayForgeTypes())
            {
                foreach (var field in type.GetFields(Every))
                {
                    Assert.IsNull(field.GetCustomAttribute<UnityEngine.Serialization.FormerlySerializedAsAttribute>(), $"{type.Name}.{field.Name} keeps no former name");
                    Assert.IsFalse(field.Name.StartsWith("legacy", StringComparison.Ordinal), $"{type.Name}.{field.Name} is no legacy field");
                }
            }

            foreach (var asset in new[] { typeof(Ability), typeof(GameplayEffect), typeof(Item), typeof(EntityIdentity), typeof(RequirementTemplate) })
                Assert.IsFalse(typeof(ISerializationCallbackReceiver).IsAssignableFrom(asset), $"{asset.Name} moves nothing as it loads");
            Assert.IsNull(typeof(FarEmerald.PlayForge.ForgeContext).GetField("Renamed", Every), "no old context name reads as a new one");
            var contexts = EditorAssembly.GetType("FarEmerald.PlayForge.Editor.ForgeTagContexts");
            Assert.IsNotNull(contexts, "the tag contexts' editor class is there");
            Assert.IsNull(contexts.GetMethod("MigrateLibraries", Every), "nor are the tag libraries' moved");
            Assert.IsFalse(typeof(PlayForgeSettings).BaseType?.Name.StartsWith("ScriptableSingleton", StringComparison.Ordinal) ?? false,
                "the settings keep no file of their own, a Forge Project holding them");

            foreach (var method in EditorAssembly.GetTypes().SelectMany(type => type.GetMethods(Every)))
            {
                var menu = method.GetCustomAttribute<MenuItem>();
                if (menu == null) continue;
                Assert.IsFalse(menu.menuItem.Contains("Upgrade Assets") || menu.menuItem.Contains("Move to Components"), $"no menu {menu.menuItem}");
            }
        }

        /// <summary>
        /// ProcessControl's 28 Register overloads, obsolete wrappers of its builder since FEAT-15, are gone, so a process
        /// registers through <c>ProcessControl.Register(process)</c> alone; and nothing else in PlayForge is obsolete
        /// (FN-21).
        /// </summary>
        [Test]
        public void FN_21_Nothing_is_obsolete()
        {
            var overloads = typeof(ProcessControl).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == nameof(ProcessControl.Register) && method.GetParameters().Any(each => each.IsOut))
                .Select(method => method.ToString());
            Assert.That(overloads, Is.Empty, "ProcessControl registers through its builder alone");

            var obsolete = PlayForgeTypes()
                .SelectMany(type => type.GetMembers(Every).Cast<MemberInfo>().Prepend(type)
                    .Where(member => member.GetCustomAttribute<ObsoleteAttribute>() != null)
                    .Select(member => member == type ? type.FullName : $"{type.Name}.{member.Name}"))
                .Distinct()
                .ToList();
            Assert.That(obsolete, Is.Empty, $"nothing in PlayForge is obsolete, as {obsolete.FirstOrDefault()}");
        }

        /// <summary>
        /// Every asset of the package, Proving Grounds and the samples imported is saved as it is now: none holds a field an
        /// older save's data moved through (FN-21).
        /// </summary>
        [Test]
        public void FN_21_No_asset_holds_an_older_saves_field()
        {
            var roots = new[] { "Packages/com.faremerald.playforge", "Assets/ProvingGrounds", "Assets/PlayForgeDemo", "Assets/PlayForgeAdditional",
                    "Assets/PlayForge Demo", "Assets/PlayForge Additional Content" }
                .Where(AssetDatabase.IsValidFolder).ToArray();
            var paths = AssetDatabase.FindAssets("", roots).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".asset") || path.EndsWith(".prefab") || path.EndsWith(".unity")).Distinct().ToList();
            Assert.That(paths, Is.Not.Empty, "there are assets");
            var legacy = new Regex(@"^(\s*legacy\w+|  Auras):", RegexOptions.Multiline);
            var held = new List<string>();
            foreach (var path in paths)
            {
                var file = FileUtil.GetPhysicalPath(path);
                if (!File.Exists(file)) continue;
                var found = legacy.Match(File.ReadAllText(file));
                if (found.Success) held.Add($"{path} ({found.Value.Trim()})");
            }
            Assert.That(held, Is.Empty, $"{held.Count} assets hold an older save's field, as {held.FirstOrDefault()}");
        }
    }
}
