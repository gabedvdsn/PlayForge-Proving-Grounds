using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The effect-removed callback hands over the application that left, as the applied callback does with the one that
    /// landed (NET-1): it handed over only the asset, so a listener couldn't tell one application of an effect from
    /// another, as a network layer's mirror must.
    /// </summary>
    public class EffectRemovedCallbackTests : ProvingGroundsTest
    {
        /// <summary>An effect's removal is told with the spec that left, the one its landing was told with (NET-1).</summary>
        [UnityTest]
        public IEnumerator NET_1_The_removed_callback_hands_over_the_spec_that_left([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var ward = Make.Effect("Ward", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            object applied = null;
            object removed = null;
            gas.GetCallbacks().OnEffectApplied += landed => applied = landed;
            gas.GetCallbacks().OnEffectRemoved += left => removed = left;

            Assert.IsTrue(World.Apply(gas, gas, ward), "a ward lands");
            gas.RemoveGameplayEffect(ward);
            await Frames.Step(2);

            Assert.IsInstanceOf<GameplayEffectSpec>(applied, "its landing is told with its spec");
            Assert.AreSame(applied, removed, "and its removal with the spec that left, the same one");
        });
    }
}
