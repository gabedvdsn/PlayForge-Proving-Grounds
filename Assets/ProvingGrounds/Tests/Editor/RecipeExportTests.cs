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
        private const string Formulas = "Assets/AC3 Formulas";
        private const string Starter = "Assets/AC5 Starter Set";

        [TearDown]
        public void Clean()
        {
            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.DeleteAsset(Formulas);
            AssetDatabase.DeleteAsset(Starter);
        }

        /// <summary>
        /// AC-3 and AC-5: the formula presets and the starter set save beside the recipes, each folder its own, the
        /// starter set's hits on the recipes' saved Health, and a rebuild keeps them in place.
        /// </summary>
        [Test]
        public void AC_5_The_formulas_and_the_starter_set_save_beside_the_recipes()
        {
            Assert.AreEqual(19 + 7 + 10, RecipeExport.SaveAll(Root, Formulas, Starter), "the recipes, seven formula assets and ten starter assets");

            string boltPath = $"{Starter}/Bolt/Bolt Hit.asset";
            string boltGuid = AssetDatabase.AssetPathToGUID(boltPath);
            var health = AssetDatabase.LoadAssetAtPath<Attribute>($"{Root}/Attributes/Health.asset");
            var boltHit = AssetDatabase.LoadAssetAtPath<GameplayEffect>(boltPath);
            Assert.AreSame(health, boltHit.ImpactSpecification.AttributeTarget, "the bolt hits the recipes' Health");
            var reciprocal = AssetDatabase.LoadAssetAtPath<ScalerAsset>($"{Formulas}/Armor/Armor Reciprocal 0.06.asset");
            Assert.AreEqual("1 ÷ (1 + 0.06 × Armor)", reciprocal.DisplayName, "the formula is named for its math");
            var shield = AssetDatabase.LoadAssetAtPath<GameplayEffect>($"{Starter}/Shield/Shield.asset");
            Assert.That(shield.Modifiers[0].Rule.Changes[0], Is.InstanceOf<ChangeImpactChange>(), "the shield's absorb is saved");

            Assert.AreEqual(36, RecipeExport.SaveAll(Root, Formulas, Starter), "a rebuild saves them again");
            Assert.AreEqual(boltGuid, AssetDatabase.AssetPathToGUID(boltPath), "keeping their GUIDs");
        }

        [Test]
        public void TW_23_The_recipes_save_as_assets_and_keep_their_GUIDs()
        {
            // Nineteen since the Lifesteal recipe's Lifesteal attribute, beside Vampirism (FN-22)
            Assert.AreEqual(19, RecipeExport.Save(Root), "nineteen assets");

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

            Assert.AreEqual(19, RecipeExport.Save(Root), "a rebuild saves them again");
            Assert.AreEqual(curseGuid, AssetDatabase.AssetPathToGUID(cursePath), "keeping the curse's GUID");
            Assert.AreSame(curse, AssetDatabase.LoadAssetAtPath<GameplayEffect>(cursePath), "in the asset itself");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<GameplayEffect>($"{Root}/Bursting Curse/Curse Burst.asset"),
                curse.ImpactSpecification.Packets[0].ContainedEffect, "which still bursts into the saved Curse Burst");
        }
    }
}
