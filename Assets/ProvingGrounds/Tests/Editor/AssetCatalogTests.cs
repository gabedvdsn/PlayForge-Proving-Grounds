using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The asset catalog a save finds its assets by (FEAT-9). Its editor command names each PlayForge asset in the project
    /// by its GUID, which lasts as the asset is renamed or moved.
    /// </summary>
    public class AssetCatalogTests
    {
        private const string Folder = "Assets/FEAT-9";

        [TearDown]
        public void RemoveFolder() => AssetDatabase.DeleteAsset(Folder);

        /// <summary>
        /// Add Every PlayForge Asset names each PlayForge asset by its GUID, a sub-asset by its GUID and local file ID, and
        /// leaves what isn't one; run again, it adds nothing (FEAT-9).
        /// </summary>
        [Test]
        public void FEAT_9_Add_Every_PlayForge_Asset_names_each_by_its_GUID()
        {
            AssetDatabase.CreateFolder("Assets", "FEAT-9");
            var health = ScriptableObject.CreateInstance<Attribute>();
            AssetDatabase.CreateAsset(health, Folder + "/Health.asset");
            var hit = ScriptableObject.CreateInstance<GameplayEffect>();
            AssetDatabase.CreateAsset(hit, Folder + "/Hit.asset");
            var inner = ScriptableObject.CreateInstance<GameplayEffect>();
            inner.name = "Inner";
            AssetDatabase.AddObjectToAsset(inner, hit);
            var other = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            AssetDatabase.CreateAsset(other, Folder + "/Other Catalog.asset");
            AssetDatabase.SaveAssets();

            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            try
            {
                Assert.That(catalog.AddProjectAssets(), Is.GreaterThanOrEqualTo(3), "it adds the folder's three");

                string healthGuid = AssetDatabase.AssetPathToGUID(Folder + "/Health.asset");
                Assert.IsTrue(catalog.TryGetId(health, out string id) && id == healthGuid, "an asset is named by its GUID");
                Assert.IsTrue(catalog.TryFind(healthGuid, out Attribute found) && found == health, "and found by it");

                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(inner, out string guid, out long localId));
                Assert.IsTrue(catalog.TryGetId(inner, out id) && id == $"{guid}:{localId}", "a sub-asset by its file's GUID and its own ID");
                Assert.IsFalse(catalog.TryGetId(other, out _), "and a catalog isn't a PlayForge asset to name");
                Assert.IsFalse(catalog.TryFind(healthGuid, out GameplayEffect _), "an ID finds only the type it names");

                Assert.AreEqual(0, catalog.AddProjectAssets(), "run again, it adds nothing");
                Assert.IsFalse(catalog.Add(healthGuid, hit), "an ID is taken once");
                Assert.IsFalse(catalog.Add("another", health), "and an asset named once");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }
    }
}
