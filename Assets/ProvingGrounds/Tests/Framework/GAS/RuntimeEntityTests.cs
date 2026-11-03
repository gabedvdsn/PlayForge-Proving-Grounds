using NUnit.Framework;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class RuntimeEntityTests : ProvingGroundsTest
    {
        /// <summary>
        /// A runtime GAS's death ended its process through its relay, which one stepped by hand, outside ProcessControl,
        /// doesn't have, so its end of frame threw.
        /// </summary>
        [Test, Category("Red")]
        public void GAS_42_A_runtime_GAS_stepped_by_hand_can_die()
        {
            var gas = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity("Hand Stepped", Make.UnusedSet()));
            gas.MarkDead();

            Assert.DoesNotThrow(() => gas.EndOfFrame());
        }
    }
}
