using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A commit is measured against the live value, after the modifiers, and lands there as it was measured
    /// (Decision 4). It used to join the baseline, under the modifiers, which then read it back through themselves.
    /// </summary>
    public class CommitSpaceTests : ProvingGroundsTest
    {
        /// <summary>Under a ×1.5 aura a 30 hit took 45: the −30 was stored under the aura and multiplied by it.</summary>
        [UnityTest]
        public IEnumerator GAS_15_A_hit_under_a_multiplier_takes_what_it_says([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var aura = Make.Effect("Aura", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 0.2f, Target = health,
                Op = ECalculationOperation.Multiply, Magnitude = 1.5f,
            });
            var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f });

            World.Apply(gas, gas, aura);
            await Frames.Step();
            Assert.That(Current(gas, health), Is.EqualTo(150f).Within(0.001f), "the aura");

            World.Apply(gas, gas, hit);
            await Frames.Step();
            Assert.That(Current(gas, health), Is.EqualTo(120f).Within(0.001f), "a 30 hit takes 30");

            await Frames.Seconds(0.3f);
            Assert.That(Current(gas, health), Is.EqualTo(70f).Within(0.001f), "and after the aura, 30 are still gone");
        });

        /// <summary>
        /// The control: committed history still lands before an override, so an override pins the value while it
        /// holds, and what landed under it shows once it's gone.
        /// </summary>
        [UnityTest]
        public IEnumerator An_override_still_pins_a_value_a_commit_lands_under([Values] Hub hub) => Live(async () =>
        {
            var speed = Make.Attribute("Speed");
            var gas = World.Entity(hub, Make.Set(Make.Element(speed, 100f, 100f)));
            var stun = Make.Effect("Stun", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 0.2f, Target = speed,
                Op = ECalculationOperation.Override, Magnitude = 0f,
            });
            var slow = Make.Effect("Slow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = speed, Magnitude = -30f });

            World.Apply(gas, gas, stun);
            await Frames.Step();
            World.Apply(gas, gas, slow);
            await Frames.Step();
            Assert.That(Current(gas, speed), Is.EqualTo(0f).Within(0.001f), "the stun holds it at 0");

            await Frames.Seconds(0.3f);
            Assert.That(Current(gas, speed), Is.EqualTo(70f).Within(0.001f), "and the slow shows after it");
        });

        private static float Current(IGameplayAbilitySystem gas, Attribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }
    }
}
