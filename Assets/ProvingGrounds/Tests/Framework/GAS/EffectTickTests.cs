using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EffectTickTests : ProvingGroundsTest
    {
        public enum TickContainer
        {
            NonStacking,
            SharedDuration,
            QueuedDuration,
            IndependentDurations
        }

        /// <summary>
        /// The period and duration timers lose the same delta each frame, so the last tick and the expiry
        /// fall on the same frame, and rounding used to decide which came first. When the period timer ended
        /// a hair above zero, the effect expired and took its last tick with it, permanently. At a fixed
        /// frame time that decided every run the same way: 5 ticks in 5 s landed 4 at each rate here.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_33_The_last_tick_lands_at_a_fixed_frame_rate(
            [Values(60, 120, 144)] int fps, [Values] TickContainer container) => Live(async () =>
        {
            Frames.SetFrameRate(fps);

            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 50f, 100f, EAttributeOverflowPolicy.ZeroToBase)));

            World.Apply(gas, gas, Regen(health, 5f, 5, container));

            await Frames.Seconds(5f);
            await Frames.Step(3);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(75f).Within(0.001f), "five ticks of 5 on 50");
        });

        /// <summary>
        /// A Durational effect with Tick On Application and no periodic ticks derives OnApply for Add: one commit, made as
        /// it's applied. Only the Instant path committed OnApply, so the change never landed. The Audit flags the shape,
        /// but it has to work all the same.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_50_Tick_On_Application_alone_lands_its_change([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var strike = Make.Effect("Strike", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 1f, TickOnApplication = true,
                Target = health, Op = ECalculationOperation.Add, Magnitude = -30f,
            });

            World.Apply(gas, gas, strike);
            await Frames.Step(2);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(70f), "the strike landed as it was applied");

            await Frames.Seconds(1.5f);
            gas.TryGetAttributeValue(health, out value);
            Assert.That(value.CurrentValue, Is.EqualTo(70f), "and a commit stays after the effect ends");
        });

        /// <summary>
        /// A timer's rounding grows with its size, and with every frame it's stepped, so over minutes the
        /// duration drifts away from the ticks by far more than it does over seconds. The last tick has to
        /// land on a long effect as well.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_33_The_last_tick_lands_on_a_long_duration(
            [Values(60, 144)] int fps, [Values] TickContainer container) => Live(async () =>
        {
            Frames.SetFrameRate(fps);

            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 0f, 1000f, EAttributeOverflowPolicy.ZeroToBase)));

            World.Apply(gas, gas, Regen(health, 300f, 300, container));

            await Frames.Seconds(300f);
            await Frames.Step(3);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(300f).Within(0.001f), "a tick a second for five minutes");
        });

        /// <summary>
        /// A period that doesn't divide evenly is rounded to a float, so its ticks add up to a touch more or less
        /// than the duration, however exact the timers are.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_33_The_last_tick_lands_when_the_period_is_rounded(
            [Values(30, 60, 90, 120, 144, 240)] int fps, [Values(0.7f, 1f, 2f, 10f)] float duration, [Values(3, 7, 9)] int ticks) => Live(async () =>
        {
            Frames.SetFrameRate(fps);

            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 0f, 1000f, EAttributeOverflowPolicy.ZeroToBase)));

            World.Apply(gas, gas, Regen(health, duration, ticks, TickContainer.NonStacking));

            await Frames.Seconds(duration);
            await Frames.Step(3);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(ticks * 5f).Within(0.001f), $"{ticks} ticks in {duration} s at {fps} fps");
        });

        private GameplayEffect Regen(Attribute health, float duration, int ticks, TickContainer container)
        {
            var config = new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational,
                Target = health, Op = ECalculationOperation.Add, Magnitude = 5f,
                Duration = duration, PeriodicTicks = true, Ticks = ticks,
            };

            if (duration >= 60f) config.Magnitude = 1f;

            if (container != TickContainer.NonStacking)
            {
                config.Reapply = EEffectReApplicationPolicy.StackExistingContainers;
                config.StackAmount = 1;
                config.StackPolicy = container switch
                {
                    TickContainer.SharedDuration => EEffectStackPolicy.StacksShareOneDuration,
                    TickContainer.QueuedDuration => EEffectStackPolicy.DurationTakenFromOneStack,
                    _ => EEffectStackPolicy.StacksHaveIndependentDurations
                };
            }

            return Make.Effect("Regen", config);
        }
    }
}
