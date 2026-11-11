using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>An effect's stack behaviours as an effect stacks (ED-75).</summary>
    public class StackBehaviourTests : ProvingGroundsTest
    {
        private static int Stacks(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.TryGetEffectContainer(effect, out var container) ? (container as AbstractStackingEffectContainer)?.Stacks ?? 1 : 0;

        /// <summary>
        /// A behaviour that rounds its stacks to the nearest, as number fields' Round does (NUM-1), threw as the effect
        /// stacked: its rounding knew Floor and Ceil alone. 2.5 stacks round to 3, a half away from zero (ED-75).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_75_A_behaviour_that_rounds_to_the_nearest_stacks([Values] Hub hub) => Live(async () =>
        {
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            await Frames.Step();
            var stacking = Make.Effect("Stacking", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = null,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
            });
            stacking.DurationSpecification.StackingConfigs.Add(new StackAmountOperation
            {
                Operation = EMagnitudeOperation.UseScaler, Scaler = new ConstantScaler { Value = 2.5f }, Rounding = ERoundingOperation.Round,
            });

            Assert.IsTrue(World.Apply(caster, target, stacking), "the effect lands");
            int first = Stacks(target, stacking);
            Assert.IsTrue(World.Apply(caster, target, stacking), "and lands again, stacking");
            Assert.AreEqual(3, Stacks(target, stacking) - first, "2.5 stacks, rounded to the nearest, are 3");
        });
    }
}
