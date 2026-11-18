using NUnit.Framework;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The user's two defaults of 2026-11-17 (EP-66): a new ability targets what its targeting picks, not its caster, so Use
    /// Implicit Targeting is off until it's turned on, as the Passive preset does; and a Select Position task's pointer
    /// reaches any distance.
    /// </summary>
    public class ImplicitTargetingDefaultTests
    {
        /// <summary>A new ability's behaviour leaves implicit targeting off (EP-66).</summary>
        [Test]
        public void EP_66_A_new_ability_targets_nothing_implicitly()
        {
            Assert.IsFalse(new AbilityBehaviour().UseImplicitTargeting, "a new behaviour targets what its targeting picks");
            var ability = ScriptableObject.CreateInstance<Ability>();
            try
            {
                Assert.IsFalse(ability.Behaviour.UseImplicitTargeting, "and so does a new ability");
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>A new Select Position task's pointer reaches any distance (EP-66).</summary>
        [Test]
        public void EP_66_A_select_position_task_reaches_any_distance()
        {
            Assert.AreEqual(float.PositiveInfinity, new SelectPositionTargetTask().MaxDistance.Magnitude, "its pointer's ray reaches any distance");
        }
    }
}
