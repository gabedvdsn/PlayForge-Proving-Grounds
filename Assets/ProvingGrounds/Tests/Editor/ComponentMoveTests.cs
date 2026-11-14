using System.Collections;
using System.IO;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// FN-14, Decision 76: an asset saved before components kept its features in fields of their own, and as it loads,
    /// in the editor and in a build, each moves into a component; Move to Components saves each that moved, so the move
    /// holds. First the auras of effects, abilities, items and entities.
    /// </summary>
    public class ComponentMoveTests
    {
        private const string Folder = "Assets/FN-14 Move";

        [SetUp]
        public void MakeFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "FN-14 Move");
        }

        [TearDown]
        public void Clean() => AssetDatabase.DeleteAsset(Folder);

        // An asset of a kind, saved, then its file as a save before components wrote it: an aura in its old list, which
        // reaches no one's allies but itself, then loaded again
        private static T SavedWithAnOldAura<T>(string name) where T : ScriptableObject
        {
            var path = $"{Folder}/{name}.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), path);
            AssetDatabase.SaveAssets();
            var text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("  Auras: []"), $"a {typeof(T).Name} keeps its old list, empty");
            // Written anew, as a file an older save left, and loaded afresh, not over the asset just made
            AssetDatabase.DeleteAsset(path);
            File.WriteAllText(path, text.Replace("  Auras: []", "  Auras:\n  - Flat: 0\n    IncludeHolder: 0\n"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static object[] ComponentsOf(Object asset) =>
            ((IList)asset.GetType().GetField("Components").GetValue(asset)).Cast<object>().ToArray();

        [Test]
        public void FN_14_An_older_assets_auras_move_into_components_as_it_loads()
        {
            foreach (var (asset, kind) in new (Object Asset, System.Type Kind)[]
                     {
                         (SavedWithAnOldAura<GameplayEffect>("Effect"), typeof(AuraEffectComponent)),
                         (SavedWithAnOldAura<Ability>("Ability"), typeof(AuraAbilityComponent)),
                         (SavedWithAnOldAura<Item>("Item"), typeof(AuraItemComponent)),
                         (SavedWithAnOldAura<EntityIdentity>("Entity"), typeof(AuraEntityComponent)),
                     })
            {
                var components = ComponentsOf(asset);
                Assert.AreEqual(1, components.Length, $"a {asset.GetType().Name}'s old aura is a component as it loads");
                Assert.IsInstanceOf(kind, components[0], $"an {kind.Name}");
                var aura = ((IAuraComponent)components[0]).Aura;
                Assert.IsFalse(aura.Flat, "with the old aura's settings");
                Assert.IsFalse(aura.IncludeHolder, "each of them");
                Assert.IsTrue(((IMovesToComponents)asset).MovedToComponents, "and it says it moved, for a save to keep");
            }
        }

        [Test]
        public void FN_14_An_older_abilitys_charges_and_toggle_move_into_components_as_it_loads()
        {
            var shieldPath = $"{Folder}/Shield.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameplayEffect>(), shieldPath);
            var path = $"{Folder}/Ability.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<Ability>(), path);
            AssetDatabase.SaveAssets();
            var text = File.ReadAllText(path);

            // As a save before components wrote it: its charges, recharge and toggle under their old names, three charges and
            // the shield toggled
            AssetDatabase.DeleteAsset(path);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"legacyCharges:\n(\s+)Magnitude: \d+", "Charges:\n$1Magnitude: 3");
            text = text.Replace("legacyRecharge:", "Recharge:")
                .Replace("legacyToggles: {fileID: 0}", $"Toggles: {{fileID: 11400000, guid: {AssetDatabase.AssetPathToGUID(shieldPath)}, type: 2}}");
            Assert.That(text, Does.Contain("Charges:").And.Contain("Toggles: {fileID: 11400000"), "the file as an older save wrote it");
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ability = AssetDatabase.LoadAssetAtPath<Ability>(path);

            var charges = ability.Components.OfType<ChargesAbilityComponent>().ToArray();
            Assert.AreEqual(1, charges.Length, "its charges are a Charges component as it loads");
            Assert.AreEqual(3, charges[0].Charges.Magnitude, "three of them");
            var toggles = ability.Components.OfType<ToggleAbilityComponent>().ToArray();
            Assert.AreEqual(1, toggles.Length, "its toggle a Toggle component");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameplayEffect>(shieldPath), toggles[0].Effect, "toggling the shield");
            Assert.AreEqual(3, ability.Charges.Magnitude, "which the ability reads as its charges");
            Assert.IsTrue(((IMovesToComponents)ability).MovedToComponents, "and it says it moved");
        }

        [Test]
        public void FN_14_Move_to_Components_saves_each_asset_that_moved()
        {
            var effect = SavedWithAnOldAura<GameplayEffect>("Effect");
            var path = AssetDatabase.GetAssetPath(effect);
            Assert.IsTrue(((IMovesToComponents)effect).MovedToComponents, "it moved as it loaded");
            Assert.That(File.ReadAllText(path), Does.Not.Contain(nameof(AuraEffectComponent)), "its file not yet");

            Assert.That(ForgeComponentPass.MoveAll(), Is.GreaterThanOrEqualTo(1), "the pass saves what moved");
            var text = File.ReadAllText(path);
            Assert.That(text, Does.Contain(nameof(AuraEffectComponent)), "its aura saved as a component");
            Assert.That(text, Does.Contain("  Auras: []"), "its old list saved empty");
            Assert.IsFalse(((IMovesToComponents)effect).MovedToComponents, "with nothing left to keep");
        }
    }
}
