using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EffectTickTests : ProvingGroundsTest
    {
        /// <summary>
        /// The period and duration timers lose the same delta each frame, so the last tick and the expiry
        /// fall on the same frame, and only rounding decides which comes first. When the period timer ends
        /// a hair above zero, the effect expires and takes its last tick with it, permanently. At a fixed
        /// frame time that decides every run the same way: 5 ticks in 5 s lands 4 at each rate here.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_33_The_last_tick_lands_at_a_fixed_frame_rate([Values(60, 120, 144)] int fps) => Live(async () =>
        {
            Frames.SetFrameRate(fps);

            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 50f, 100f, EAttributeOverflowPolicy.ZeroToBase)));
            var regen = Make.Effect("Regen", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational,
                Target = health, Op = ECalculationOperation.Add, Magnitude = 5f,
                Duration = 5f, PeriodicTicks = true, Ticks = 5,
            });

            World.Apply(gas, gas, regen);

            await Frames.Seconds(5f);
            await Frames.Step(3);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(75f).Within(0.001f), "five ticks of 5 on 50");
        });
    }
}
