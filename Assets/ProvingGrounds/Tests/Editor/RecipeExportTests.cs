using NUnit.Framework;
using UnityEditor;
using FarEmerald.PlayForge.ProvingGrounds.Recipes.Building;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-23, Decision 63: the recipe book saves as the Additional Content sample's assets, each referring to the others as
    /// saved, and a rebuild saves over them in place, keeping their GUIDs.
    /// </summary>
    public class RecipeExportTests
    {
        private const string Root = "Assets/TW23 Recipes";

        [TearDown]
        public void Clean() => AssetDatabase.DeleteAsset(Root);

        [Test]
        public void TW_23_The_recipes_save_as_assets_and_keep_their_GUIDs()
        {
            Assert.AreEqual(18, RecipeExport.Save(Root), "eighteen assets");

            string cursePath = $"{Root}/Bursting Curse/Curse.asset";
            string curseGuid = AssetDatabase.AssetPathToGUID(cursePath);
            var curse = AssetDatabase.LoadAssetAtPath<GameplayEffect>(cursePath);
            var burst = AssetDatabase.LoadAssetAtPath<GameplayEffect>($"{Root}/Bursting Curse/Curse Burst.asset");
            Assert.IsNotNull(curse, "the curse is saved");
            Assert.AreSame(burst, curse.ImpactSpecification.Packets[0].ContainedEffect, "and bursts into the saved Curse Burst");

            var retaliate = AssetDatabase.LoadAssetAtPath<Ability>($"{Root}/Counter/Retaliate.asset");
            Assert.AreEqual(Tags.IMPACT_TAKEN, retaliate.Definition.Triggers[0].Tag, "Retaliate answers Impact Taken");
            var boss = AssetDatabase.LoadAssetAtPath<AttributeSet>($"{Root}/Phase/Boss.asset");
            Assert.That(boss.WorkerGroup.AnalysisWorkers[0], Is.InstanceOf<ThresholdAnalysisWorker>(), "the boss watches its Health");

            Assert.AreEqual(18, RecipeExport.Save(Root), "a rebuild saves them again");
            Assert.AreEqual(curseGuid, AssetDatabase.AssetPathToGUID(cursePath), "keeping the curse's GUID");
            Assert.AreSame(curse, AssetDatabase.LoadAssetAtPath<GameplayEffect>(cursePath), "in the asset itself");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<GameplayEffect>($"{Root}/Bursting Curse/Curse Burst.asset"),
                curse.ImpactSpecification.Packets[0].ContainedEffect, "which still bursts into the saved Curse Burst");
        }
    }
}
