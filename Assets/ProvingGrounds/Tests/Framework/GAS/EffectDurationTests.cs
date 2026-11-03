using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EffectDurationTests : ProvingGroundsTest
    {
        /// <summary>
        /// The duration was read before its scaler was initialized, and a scaler backed by an attribute reads
        /// nothing until then, so the effect was over as it began.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_22_A_scaled_duration_is_read_once_its_scaler_is_ready([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var marked = Make.NewTag("Test.Status.Marked");
            var effect = Make.Effect("Scaled Duration", new EffectConfig { Policy = EEffectDurationPolicy.Durational, GrantedTags = { marked } });
            effect.DurationSpecification.DurationOperation = Scaled(0.5f);

            World.Apply(gas, gas, effect);
            await Frames.Seconds(0.25f);
            Assert.That(gas.GetTagWeight(marked), Is.GreaterThan(0), "half way through, it's still on");

            await Frames.Seconds(0.35f);
            Assert.That(gas.GetTagWeight(marked), Is.LessThanOrEqualTo(0), "and then it's over");
        });

        /// <summary>The number of ticks had a scaler that was never initialized at all.</summary>
        [UnityTest]
        public IEnumerator GAS_22_A_scaled_tick_count_is_read_once_its_scaler_is_ready([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var drain = Make.Effect("Drain", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Target = health, Magnitude = -1f, Duration = 0.4f, PeriodicTicks = true
            });
            drain.DurationSpecification.TicksOperation = new ScalerIntegerMagnitudeOperation
            {
                RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = new ReadyScaler(4f), Rounding = ERoundingOperation.Floor
            };

            World.Apply(gas, gas, drain);
            await Frames.Seconds(0.5f);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(96f).Within(0.001f), "four ticks of 1");
        });

        /// <summary>An infinite effect's tick interval was read as its bare magnitude, past its scaler.</summary>
        [UnityTest]
        public IEnumerator GAS_24_An_infinite_effect_ticks_at_its_scaled_interval([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var drain = Make.Effect("Endless Drain", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = health, Magnitude = -1f, PeriodicTicks = true
            });
            drain.DurationSpecification.TickIntervalOperation = Scaled(0.1f);

            World.Apply(gas, gas, drain);
            await Frames.Seconds(0.55f);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(95f).Within(1f), "a tick every tenth of a second");
        });

        private static ScalerMagnitudeOperation Scaled(float value)
        {
            return new ScalerMagnitudeOperation { RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = new ReadyScaler(value) };
        }

        /// <summary>A scaler that reads nothing until it's initialized, as one backed by an attribute does.</summary>
        private sealed class ReadyScaler : AbstractScaler
        {
            private readonly float _value;
            private bool _ready;

            public ReadyScaler(float value) => _value = value;

            public override void Initialize(IAttributeImpactCarrier deriv) => _ready = true;

            public override float Evaluate(IAttributeImpactCarrier deriv) => _ready ? _value : 0f;
        }
    }
}
