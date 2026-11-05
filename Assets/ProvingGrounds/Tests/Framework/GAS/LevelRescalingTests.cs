using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Level-up rescaling, which the Roguelite lab needs: an effect whose magnitude reads its origin's level follows the
    /// level as it changes.
    /// </summary>
    public class LevelRescalingTests : ProvingGroundsTest
    {
        /// <summary>
        /// A lasting effect's magnitude was read as its modifier registered, and nothing read it again as its origin's
        /// level changed, so an item that levelled up kept its first level's bonus (GAS-58, which the Roguelite lab
        /// found). Levelled up, and back down, the item's effect follows.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_58_An_items_effect_rescales_as_the_item_levels([Values] Hub hub) => Live(async () =>
        {
            var might = Make.Attribute("Might");
            var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f)));
            var ages = Make.Effect("Ages", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = might, MagnitudeOp = EMagnitudeOperation.UseScaler,
                MagnitudeScaler = new SimpleScaler { StretchOverLevels = false, MaxLevel = 4, LevelValues = new[] { 4f, 8f, 12f, 16f } },
            });
            var crown = Make.Item("Crown");
            crown.MaxLevel = 4;
            crown.GrantedEffects = new List<GameplayEffect> { ages };
            Assert.IsTrue(gas.GetItemSystem().GiveAndEquipItem(crown, out _), "the crown goes on");
            await Frames.Step();
            float Might() => gas.TryGetAttributeValue(might, out var value) ? value.CurrentValue : float.NaN;
            Assert.AreEqual(4f, Might(), "+4 at its first level");

            Assert.IsTrue(gas.FindLevelSystem(out var levels), "the entity keeps levels");
            Assert.IsTrue(levels.TrySetLevel(crown.Tags.AssetTag, new IntValuePair(3, 4)).Success, "the crown levels up to its third");
            await Frames.Step();
            Assert.AreEqual(12f, Might(), "and its effect follows: +12");

            Assert.IsTrue(levels.TrySetLevel(crown.Tags.AssetTag, new IntValuePair(2, 4)).Success, "down to its second");
            await Frames.Step();
            Assert.AreEqual(8f, Might(), "+8");
        });
    }
}
