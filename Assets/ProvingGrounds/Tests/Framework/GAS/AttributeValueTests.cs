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
        [UnityTest]
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

        /// <summary>The control for the test above: set to roll every time, a buff re-rolls when its attribute recomputes.</summary>
        [UnityTest]
        public IEnumerator A_randomized_scaler_set_to_roll_every_time_rerolls([Values] Hub hub) => Live(async () =>
        {
            UnityEngine.Random.InitState(27);
            var strength = Make.Attribute("Strength");
            var gas = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f)));
            World.Apply(gas, gas, Lucky(strength, new RandomizedScaler
            {
                VarianceMode = EVarianceMode.Multiplier, MinMultiplier = 0f, MaxMultiplier = 100f, RollOncePerSpec = false,
            }));
            await Frames.Step();
            float rolled = Current(gas, strength);

            await Hits(gas, strength, 3);

            Assert.That(Current(gas, strength), Is.Not.EqualTo(rolled - 3f).Within(1e-3f));
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
        [UnityTest]
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
        /// A set that included itself, directly or through another, sent every walk over its sub-sets round forever, so
        /// building the entity's attributes overflowed the stack. A set reached twice now counts once.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_44_A_set_that_includes_itself_counts_once([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var mana = Make.Attribute("Mana");
            var outer = Make.Set(Make.Element(health, 100f, 100f));
            var inner = Make.Set(Make.Element(mana, 50f, 50f));
            outer.SubSets.Add(inner);
            inner.SubSets.Add(outer);
            outer.SubSets.Add(outer);

            var gas = World.Entity(hub, outer);
            await Frames.Step();

            gas.TryGetAttributeValue(health, out var h);
            gas.TryGetAttributeValue(mana, out var m);
            Assert.That(h.CurrentValue, Is.EqualTo(100f), "Health");
            Assert.That(m.CurrentValue, Is.EqualTo(50f), "Mana");
            Assert.That(gas.GetTagWeight(outer.AssetTag), Is.EqualTo(1), "the set's tag, once");
            Assert.That(gas.GetTagWeight(inner.AssetTag), Is.EqualTo(1), "the inner set's, once");
        });

        /// <summary>
        /// A set reached by two paths counted twice: its combined elements were added twice, and its tag weighed 2. A set
        /// reached twice now counts once.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_44_A_set_reached_by_two_paths_counts_once([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var shared = Make.Element(health, 30f, 30f);
            shared.CollisionPolicy = EAttributeElementCollisionPolicy.Combine;
            var common = Make.Set(shared);
            var left = Make.Set();
            var right = Make.Set();
            left.SubSets.Add(common);
            right.SubSets.Add(common);
            var top = Make.Set();
            top.SubSets.Add(left);
            top.SubSets.Add(right);

            var gas = World.Entity(hub, top);
            await Frames.Step();

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(30f), "the shared set's Health, counted once");
            Assert.That(gas.GetTagWeight(common.AssetTag), Is.EqualTo(1), "and its tag, once");
        });

        /// <summary>A combined element scaled from another attribute follows it, as a lone one does.</summary>
        [UnityTest]
        public IEnumerator GAS_27_A_combined_part_follows_what_it_scales_from([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var health = Make.Attribute("Health");
            var mine = Make.Element(health, 50f, 50f);
            mine.CollisionPolicy = EAttributeElementCollisionPolicy.Combine;
            var theirs = Derived(health, strength, linkCurrentToBase: true);
            theirs.CollisionPolicy = EAttributeElementCollisionPolicy.Combine;
            var set = Make.Set(Make.Element(strength, 10f, 10f), mine);
            set.SubSets.Add(Make.Set(theirs));

            var gas = World.Entity(hub, set);
            await Frames.Step();
            Assert.That(Current(gas, health), Is.EqualTo(70f), "50, and twice Strength's 10");

            gas.TryModifyAttribute(strength, SourcedModifiedAttributeValue.GenerateSimple(gas, strength, 5f, 5f));
            await Frames.Step();

            Assert.That(Current(gas, health), Is.EqualTo(80f), "50, and twice Strength's 15");
        });

        /// <summary>
        /// A derived attribute's refresh fired OnAttributeChanged alone: no post-change worker saw it and the frame
        /// summary never heard of it. It's the system's own change, typed DisallowImpact (Decision 14), and takes the
        /// post-change half of the pipeline.
        /// </summary>
        [UnityTest]
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
                if (ReferenceEquals(impact.Attribute, power)) received++;
            };

            gas.TryModifyAttribute(strength, SourcedModifiedAttributeValue.GenerateSimple(gas, strength, 5f, 5f));
            await Frames.Step();

            gas.TryGetAttributeValue(power, out var value);
            Assert.That(value.BaseValue, Is.EqualTo(30f), "AttackPower followed Strength");
            Assert.That(Faults.Hits("Watcher.Intercept"), Is.EqualTo(1), "a post-change worker saw it");
            Assert.That(received, Is.EqualTo(1), "and it reached the frame summary");
        });

        /// <summary>A derived Base moves Current with it when the attribute scales with Base, as any change to Base does.</summary>
        [UnityTest]
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

        /// <summary>
        /// The rescale took its share of Current after the bounds had clamped it, not before. Under ZeroToBase an 80/100
        /// attribute that lost half its Base was clamped to 50, and a quarter of that, taken off the 80 under the clamp,
        /// left it above the new Base, so it stayed at 50, not 40. The other two shares were right by luck or by nothing
        /// clamped.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_37_A_clamped_attribute_keeps_its_share_of_Base([Values] Hub hub, [Values(100f, 80f, 30f)] float current) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var element = Make.Element(health, current, 100f, EAttributeOverflowPolicy.ZeroToBase);
            element.Constraints.AutoScaleWithBase = true;
            var gas = World.Entity(hub, Make.Set(element));
            await Frames.Step();

            World.Apply(gas, gas, Make.Effect("Frailty", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = health, Slot = EAttributeTarget.Base, Magnitude = -50f,
            }));
            await Frames.Step(2);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.BaseValue, Is.EqualTo(50f), "Base halved");
            Assert.That(value.CurrentValue, Is.EqualTo(current / 2f).Within(1e-3f), "and Current kept its share");
        });

        /// <summary>
        /// The control for the test above: with nothing clamped, a change to Current and Base together adds first and
        /// then scales, as it always has.
        /// </summary>
        [UnityTest]
        public IEnumerator A_change_to_Current_and_Base_adds_then_scales([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var element = Make.Element(health, 50f, 100f);
            element.Constraints.AutoScaleWithBase = true;
            var gas = World.Entity(hub, Make.Set(element));
            await Frames.Step();

            World.Apply(gas, gas, Make.Effect("Growth", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Slot = EAttributeTarget.CurrentAndBase, Magnitude = 50f,
            }));
            await Frames.Step(2);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.BaseValue, Is.EqualTo(150f), "Base");
            Assert.That(value.CurrentValue, Is.EqualTo(150f), "Current: 50 and 50 more, then half again");
        });

        /// <summary>A set settling colliding elements by UseAverage took the first alone, and one set to Ignore threw.</summary>
        [UnityTest]
        public IEnumerator GAS_40_A_set_averages_or_ignores_colliding_elements(
            [Values] Hub hub, [Values(EValueCollisionPolicy.UseAverage, EValueCollisionPolicy.Ignore)] EValueCollisionPolicy policy) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 50f, 50f));
            set.SubSets.Add(Make.Set(Make.Element(health, 30f, 30f)));
            set.CollisionResolutionPolicy = policy;

            var gas = World.Entity(hub, set);
            await Frames.Step();

            float expected = policy == EValueCollisionPolicy.UseAverage ? 40f : 50f;
            Assert.That(Current(gas, health), Is.EqualTo(expected), policy == EValueCollisionPolicy.UseAverage ? "the average" : "the first");
        });

        /// <summary>
        /// A set element with no attribute, as a list's "+" leaves one, threw as the entity started: the set's elements
        /// were filed in a dictionary by their attribute, and a null key throws, so the entity got none of its attributes.
        /// An element with no attribute gives nothing, and the rest of the set stands.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_54_An_element_with_no_attribute_gives_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f), Make.Element(null, 5f, 5f));

            var gas = World.Entity(hub, set);
            await Frames.Step();

            Assert.That(Current(gas, health), Is.EqualTo(100f), "the set's other attributes stand");
        });

        /// <summary>
        /// A set settling colliding elements by UseMinimum took the first: the search for the lowest began at the lowest
        /// float, so no value was ever below it. It takes the element with the lowest Base, as UseMaximum takes the highest.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_55_Use_Minimum_takes_the_lowest([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            set.SubSets.Add(Make.Set(Make.Element(health, 60f, 60f)));
            set.CollisionResolutionPolicy = EValueCollisionPolicy.UseMinimum;

            var gas = World.Entity(hub, set);
            await Frames.Step();

            Assert.That(Current(gas, health), Is.EqualTo(60f), "the lower Base");
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

        /// <summary>
        /// An empty slot in a scaler's Behaviours, which a list's "+" leaves, threw on every evaluation. The behaviour
        /// drawer hid it by writing a default into an empty slot as it drew; it no longer writes while drawing, so an
        /// empty slot now changes nothing (ED-12).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_An_empty_scaler_behaviour_changes_nothing([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var withEmpty = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 100f)));
            var without = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 100f)));
            var emptySlot = new SimpleScaler { Scaling = UnityEngine.AnimationCurve.Constant(0f, 1f, 5f) };
            emptySlot.Behaviours.Add(null);

            World.Apply(withEmpty, withEmpty, Empowered(strength, emptySlot));
            World.Apply(without, without,
                Empowered(strength, new SimpleScaler { Scaling = UnityEngine.AnimationCurve.Constant(0f, 1f, 5f) }));
            await Frames.Step();

            Assert.That(Current(without, strength), Is.GreaterThan(10f), "the scaler's value lands");
            Assert.That(Current(withEmpty, strength), Is.EqualTo(Current(without, strength)).Within(1e-3f),
                "and the empty behaviour changes nothing");
        });

        private GameplayEffect Empowered(Attribute strength, AbstractScaler scaler) => Make.Effect("Empowered",
            new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = strength, Op = ECalculationOperation.Add,
                MagnitudeScaler = scaler, MagnitudeOp = EMagnitudeOperation.UseScaler,
            });

        private static float Current(IGameplayAbilitySystem gas, Attribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }
    }
}
