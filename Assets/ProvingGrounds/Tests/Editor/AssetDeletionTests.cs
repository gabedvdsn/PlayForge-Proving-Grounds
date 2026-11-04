using System;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>Deleting a PlayForge asset from The Forge or the asset browser.</summary>
    public class AssetDeletionTests
    {
        private const string Folder = "Assets/ED-8";

        [TearDown]
        public void RemoveFolder() => AssetDatabase.DeleteAsset(Folder);

        /// <summary>
        /// The Forge and the asset browser deleted an asset for good, without looking for the assets that use it. Both
        /// now go through one place, which names those assets, asks, and moves the asset to the trash. A test answers
        /// through its confirmation hook, since a dialog would block the run.
        /// </summary>
        [Test]
        public void ED_8_Deleting_a_used_asset_names_its_users_and_can_be_cancelled()
        {
            var deletion = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeAssetDeletion");
            Assert.IsNotNull(deletion, "deleting goes through one place that checks references");
            var confirm = deletion.GetField("Confirm", BindingFlags.Static | BindingFlags.NonPublic);

            AssetDatabase.CreateFolder("Assets", "ED-8");
            var cost = ScriptableObject.CreateInstance<GameplayEffect>();
            AssetDatabase.CreateAsset(cost, Folder + "/Mana Cost.asset");
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Cost = cost;
            AssetDatabase.CreateAsset(ability, Folder + "/Fireball.asset");
            AssetDatabase.SaveAssets();

            string asked = null;
            var previous = confirm.GetValue(null);
            confirm.SetValue(null, new Func<string, string, bool>((title, message) =>
            {
                asked = message;
                return false;
            }));
            try
            {
                AssetAction.Delete().Action(cost);
            }
            finally
            {
                confirm.SetValue(null, previous);
            }

            Assert.That(asked, Does.Contain("Fireball"), "the question names the asset that uses it");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameplayEffect>(Folder + "/Mana Cost.asset"), "cancelled, it stays");
        }
    }
}
