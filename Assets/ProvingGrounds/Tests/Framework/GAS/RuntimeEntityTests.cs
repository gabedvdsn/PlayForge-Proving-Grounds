using NUnit.Framework;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class RuntimeEntityTests : ProvingGroundsTest
    {
        /// <summary>
        /// A runtime GAS's death ended its process through its relay, which one stepped by hand, outside ProcessControl,
        /// doesn't have, so its end of frame threw.
        /// </summary>
        [Test]
        public void GAS_42_A_runtime_GAS_stepped_by_hand_can_die()
        {
            var gas = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity("Hand Stepped", Make.UnusedSet()));
            gas.MarkDead();

            Assert.DoesNotThrow(() => gas.EndOfFrame());
        }

        /// <summary>
        /// A runtime GAS stepped by hand ends as it dies, and says so once. It ended through its relay, which one outside
        /// ProcessControl has none of, so it never ended: its abilities still took activations, and its System Disabled
        /// fired again at every end of frame after (GAS-80, seen mapping the architecture).
        /// </summary>
        [Test]
        public void GAS_80_A_runtime_GAS_stepped_by_hand_ends_as_it_dies()
        {
            var gas = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity("Hand Ended", Make.UnusedSet()));
            int disabled = 0;
            gas.GetCallbacks().OnSystemDisabled += () => disabled++;
            gas.MarkDead();
            for (int i = 0; i < 3; i++) gas.EndOfFrame();

            Assert.AreEqual(1, disabled, "its end is told once, not at every end of frame after");
            Assert.IsFalse(gas.GetAbilitySystem().Enabled, "and it ended: its abilities take no activation");
        }
    }
}
