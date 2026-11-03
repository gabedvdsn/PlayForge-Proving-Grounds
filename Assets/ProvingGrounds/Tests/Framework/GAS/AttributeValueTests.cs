using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class AttributeValueTests : ProvingGroundsTest
    {
        /// <summary>
        /// A randomized scaler rolled on every evaluation, and a modifier is evaluated whenever its attribute recomputes,
        /// so a random buff re-rolled with every change to the attribute. It keeps one roll per application (Decision 12).
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_27_A_randomized_buff_keeps_its_roll([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var gas = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f)));
            World.Apply(gas, gas, Lucky(strength, new RandomizedScaler
            {
                VarianceMode = EVarianceMode.Multiplier, MinMultiplier = 0f, MaxMultiplier = 100f,
            }));
            await Frames.Step();
            float rolled = Current(gas, strength);

            await Hits(gas, strength, 3);

            Assert.That(Current(gas, strength), Is.EqualTo(rolled - 3f).Within(1e-3f), "each hit took 1, and the buff held");
        });

        private GameplayEffect Lucky(Attribute strength, RandomizedScaler scaler)
        {
            return Make.Effect("Lucky", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = strength, Op = ECalculationOperation.Add,
                Magnitude = 1f, MagnitudeScaler = scaler, MagnitudeOp = EMagnitudeOperation.UseScaler,
            });
        }

        private async Cysharp.Threading.Tasks.UniTask Hits(IGameplayAbilitySystem gas, Attribute attribute, int count)
        {
            for (int i = 0; i < count; i++)
            {
                gas.TryModifyAttribute(attribute, SourcedModifiedAttributeValue.GenerateSimple(gas, attribute, -1f, 0f));
                await Frames.Step();
            }
        }

        /// <summary>
        /// Elements set to Combine ended up as the first alone, since combining did nothing. They add up (Decision 13),
        /// and the first combined itself and each of the others twice, which would have counted them double.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_27_Combined_elements_add_up([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var mine = Make.Element(health, 50f, 50f);
            mine.CollisionPolicy = EAttributeElementCollisionPolicy.Combine;
            var theirs = Make.Element(health, 30f, 30f);
            theirs.CollisionPolicy = EAttributeElementCollisionPolicy.Combine;
            var set = Make.Set(mine);
            set.SubSets.Add(Make.Set(theirs));

            var gas = World.Entity(hub, set);
            await Frames.Step();

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(80f), "Current");
            Assert.That(value.BaseValue, Is.EqualTo(80f), "and Base");
        });

        /// <summary>
        /// A derived attribute's refresh fired OnAttributeChanged alone: no post-change worker saw it and the frame
        /// summary never heard of it. It's the system's own change, typed DisallowImpact (Decision 14), and takes the
        /// post-change half of the pipeline.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_27_A_derived_change_reaches_post_change_workers([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var power = Make.Attribute("AttackPower");
            var gas = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f), Derived(power, strength, linkCurrentToBase: true)));
            await Frames.Step();

            gas.GetAttributeSystem().ProvideWorker(new ProbeAttributeWorker("Watcher", power, Faults, AbstractAttributeWorker.EChangeEventTiming.PostChange));
            int received = 0;
            gas.GetCallbacks().OnImpactReceived += impact =>
            {
                if (impact.Attribute == power) received++;
            };

            gas.TryModifyAttribute(strength, SourcedModifiedAttributeValue.GenerateSimple(gas, strength, 5f, 5f));
            await Frames.Step();

            gas.TryGetAttributeValue(power, out var value);
            Assert.That(value.BaseValue, Is.EqualTo(30f), "AttackPower followed Strength");
            Assert.That(Faults.Hits("Watcher.Intercept"), Is.EqualTo(1), "a post-change worker saw it");
            Assert.That(received, Is.EqualTo(1), "and it reached the frame summary");
        });

        /// <summary>A derived Base moves Current with it when the attribute scales with Base, as any change to Base does.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_27_A_derived_Base_scales_Current([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var power = Make.Attribute("AttackPower");
            var derived = Derived(power, strength, linkCurrentToBase: false, current: 20f);
            derived.Constraints.AutoScaleWithBase = true;
            var gas = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f), derived));
            await Frames.Step();

            gas.TryModifyAttribute(strength, SourcedModifiedAttributeValue.GenerateSimple(gas, strength, 5f, 5f));
            await Frames.Step(2);

            Assert.That(Current(gas, power), Is.EqualTo(30f), "Base went from 20 to 30, so Current did too");
        });

        /// <summary>Golden S8's derived attribute: Base is twice Strength.</summary>
        private AttributeSetElement Derived(Attribute power, Attribute strength, bool linkCurrentToBase, float current = 0f)
        {
            return Make.Element(power, current, 0f,
                baseSpecOverride: new AttributeMagnitudeSpec
                {
                    Magnitude = 2f,
                    RealMagnitude = EMagnitudeOperation.MultiplyWithScaler,
                    Scaling = new CachedAttributeScaler { CaptureAttribute = strength },
                },
                linkCurrentToBase: linkCurrentToBase);
        }

        private static float Current(IGameplayAbilitySystem gas, Attribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }
    }
}
