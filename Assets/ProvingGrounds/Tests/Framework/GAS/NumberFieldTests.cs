using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The distance from the caster to the target: a kind of number of a project's own, written as the extension API's
    /// docs write it (FEAT-29, NUM-3). It is one class with one method.
    /// </summary>
    [Serializable]
    [NumberKind("Distance to Target", Category = "Space", Reads = NumberReads.Caster | NumberReads.Target)]
    public class DistanceToTarget : NumberKind
    {
        [Tooltip("Along the ground, leaving height out")]
        public bool Flat = true;

        protected override float Evaluate(in NumberContext context)
        {
            var from = context.Caster.GetTargetingPacket().position;
            var to = context.Target.GetTargetingPacket().position;
            if (Flat) from.y = to.y = 0f;
            return Vector3.Distance(from, to);
        }

        public override string Describe() => "the distance to the target";
    }

    /// <summary>A task that reads a number as it starts, as a converted task's range will, and keeps what it read.</summary>
    public class ReadNumberTask : AbstractAbilityTask
    {
        public static readonly List<float> Read = new();

        public ScalableFloat Number = 0f;

        public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
        {
            Read.Add(Number.Evaluate(NumberContext.Of(data)));
            return UniTask.CompletedTask;
        }

        public override bool IsCriticalSection => false;
    }

    /// <summary>
    /// FEAT-29's foundation: one number type for every authored gameplay number, grown from the magnitude field, read
    /// against a context any site can build, and a kind of a project's own as one class with one method.
    /// </summary>
    public class NumberFieldTests : ProvingGroundsTest
    {
        private static float Health(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-1: the type
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A float converts to a number, which is a constant: it reads its number wherever it's read, and says so, for
        /// editor previews and setup code. A number of another kind isn't, and its Constant is its number (NUM-1).
        /// </summary>
        [Test]
        public void NUM_1_A_float_converts_to_a_constant_number()
        {
            ScalableFloat range = 20f;
            Assert.IsTrue(range.IsConstant, "a float makes a constant");
            Assert.AreEqual(20f, range.Constant, "of the float");
            Assert.AreEqual(20f, range.Evaluate(default(NumberContext)), "read anywhere, even with nothing to read");

            ScalableInt ticks = 3;
            Assert.IsTrue(ticks.IsConstant);
            Assert.AreEqual(3, ticks.Evaluate(default(NumberContext)), "an int makes a whole-number constant");

            var byLevel = new ScalableFloat(new SimpleScaler { LevelValues = new[] { 1f, 2f }, StretchOverLevels = false }, 4f);
            Assert.IsFalse(byLevel.IsConstant, "a number with a scaler that it combines with isn't a constant");
            Assert.AreEqual(4f, byLevel.Constant, "and its Constant is its number");

            var numberOnly = new ScalableFloat(new SimpleScaler(), 7f, EMagnitudeOperation.UseMagnitude);
            Assert.IsTrue(numberOnly.IsConstant, "one that reads its number alone is");
        }

        /// <summary>
        /// The magnitude classes are the number types now. Their old names stay a version as obsolete subclasses, so code
        /// that names them compiles and reads as it did (NUM-1).
        /// </summary>
        [Test]
        public void NUM_1_The_magnitude_classes_old_names_still_read()
        {
#pragma warning disable CS0618
            Assert.IsTrue(typeof(ScalerMagnitudeOperation).IsSubclassOf(typeof(ScalableFloat)), "the float magnitude is a ScalableFloat");
            Assert.IsTrue(typeof(ScalerIntegerMagnitudeOperation).IsSubclassOf(typeof(ScalableInt)), "the whole one a ScalableInt");
            Assert.IsNotNull(System.Attribute.GetCustomAttribute(typeof(ScalerMagnitudeOperation), typeof(ObsoleteAttribute)), "and both are obsolete");
            Assert.IsNotNull(System.Attribute.GetCustomAttribute(typeof(ScalerIntegerMagnitudeOperation), typeof(ObsoleteAttribute)));

            ScalableFloat old = new ScalerMagnitudeOperation
            {
                Magnitude = 2f, RealMagnitude = EMagnitudeOperation.MultiplyWithScaler, Scaler = new ConstantScaler { Value = 3f },
            };
#pragma warning restore CS0618
            Assert.AreEqual(6f, old.Evaluate(default(NumberContext)), "one made by its old name reads as it did");

            foreach (var name in new[] { "Magnitude", "RealMagnitude", "Scaler" })
                Assert.IsNotNull(typeof(ScalableFloat).GetField(name), $"a number keeps the magnitude's saved {name}");
            foreach (var name in new[] { "Magnitude", "RealMagnitude", "Scaler", "Rounding" })
                Assert.IsNotNull(typeof(ScalableInt).GetField(name), $"a whole number keeps its saved {name}");
        }

        /// <summary>A whole number rounds down, up, or now to the nearest, half away from zero (NUM-1).</summary>
        [Test]
        public void NUM_1_A_whole_number_rounds_down_up_or_to_the_nearest()
        {
            int Read(float value, ERoundingOperation rounding) =>
                new ScalableInt(new ConstantScaler { Value = value }, 0, EMagnitudeOperation.UseScaler, rounding).Evaluate(default(NumberContext));

            Assert.AreEqual(2, Read(2.5f, ERoundingOperation.Floor), "Floor");
            Assert.AreEqual(3, Read(2.5f, ERoundingOperation.Ceil), "Ceil");
            Assert.AreEqual(3, Read(2.5f, ERoundingOperation.Round), "Round takes a half up");
            Assert.AreEqual(2, Read(2.4f, ERoundingOperation.Round), "and below a half down");
            Assert.AreEqual(-3, Read(-2.5f, ERoundingOperation.Round), "and a negative half away from zero");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-2: the context
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A context says what its site has: an effect's spec has a caster, a target, a level, the caller's magnitudes and
        /// itself as the carrier; an entity on its own has itself and its level; a cue's lands on the cue's entity; an
        /// activation's data has its target and its magnitudes; a test's has what it's given (NUM-2).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_2_A_context_says_what_its_site_has([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            var spec = target.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster), Make.Effect("Hit", new EffectConfig { Target = health, Magnitude = -1f }));
            await Frames.Step();

            var ofSpec = NumberContext.Of(spec);
            Assert.IsTrue(ofSpec.Has(NumberReads.Level | NumberReads.Caster | NumberReads.Target | NumberReads.Caller | NumberReads.Carrier), $"a spec's: {ofSpec}");
            Assert.AreSame(caster, ofSpec.Caster, "its caster is its origin's");
            Assert.AreSame(target, ofSpec.Target, "its target the spec's");
            Assert.IsFalse(ofSpec.Has(NumberReads.Data), "it has no activation's data");

            var ofEntity = NumberContext.Of(caster);
            Assert.IsTrue(ofEntity.Has(NumberReads.Level | NumberReads.Caster), $"an entity's: {ofEntity}");
            Assert.IsFalse(ofEntity.Has(NumberReads.Target), "it has no target");
            Assert.IsFalse(ofEntity.Has(NumberReads.Caller), "and no caller");

            var ofCue = NumberContext.Of(new CueContext(caster, spec));
            Assert.AreSame(caster, ofCue.Target, "a cue's lands on the cue's entity");
            Assert.IsTrue(ofCue.Has(NumberReads.Carrier), "with its effect as the carrier");

            var ofWorker = NumberContext.Of(new EffectWorkerContext(target, spec, null, null));
            Assert.AreSame(spec, ofWorker.Carrier, "an effect worker's reads its carrier");

            var key = Make.NewTag("Data.Key");
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.DERIVATION, IEffectOrigin.GenerateSourceDerivation(caster));
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            data.SetByCaller(key, 9f);
            var ofData = NumberContext.Of(data);
            Assert.IsTrue(ofData.Has(NumberReads.Level | NumberReads.Caster | NumberReads.Target | NumberReads.Caller | NumberReads.Data), $"data's: {ofData}");
            Assert.IsTrue(ofData.TryGetSetByCaller(key, out float set) && set == 9f, "it reads the caller's magnitude");

            var forTest = NumberContext.For(caster, null, 3);
            Assert.AreEqual(3, forTest.Level.CurrentValue, "a test's has its level");
            Assert.IsTrue(forTest.Has(NumberReads.Level | NumberReads.Caster));
            Assert.IsFalse(forTest.Has(NumberReads.Target), "and no target where it's given none");
            Assert.IsFalse(default(NumberContext).Has(NumberReads.Level), "an empty context has nothing");
        });

        /// <summary>
        /// A scaler reads through a context with no carrier, as a task's number does: By Level at the context's level, an
        /// attribute on either side, and a whole number of them rounded (NUM-2).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_2_A_scaler_reads_through_a_context_with_no_carrier([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var caster = World.Entity(hub, Make.Set(Make.Element(strength, 7f, 7f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(strength, 40f, 40f)), "Target");
            await Frames.Step();

            var byLevel = new ScalableFloat(new SimpleScaler { LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = false }, 0f, EMagnitudeOperation.UseScaler);
            Assert.AreEqual(20f, byLevel.Evaluate(NumberContext.For(caster, target, 2)), "By Level reads the context's level");
            Assert.AreEqual(30f, byLevel.Evaluate(NumberContext.For(caster, target, 5)), "and past its end its last");

            var ofCaster = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = strength, CaptureFrom = ESourceTarget.Source }, 0.5f);
            Assert.AreEqual(3.5f, ofCaster.Evaluate(NumberContext.For(caster, target, 1)), 0.001f, "0.5 × the caster's Strength");
            var ofTarget = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = strength, CaptureFrom = ESourceTarget.Target }, 0.5f);
            Assert.AreEqual(20f, ofTarget.Evaluate(NumberContext.For(caster, target, 1)), 0.001f, "0.5 × the target's");

            var captured = new ScalableFloat(new AttributeBackedScaler
            {
                CaptureAttribute = strength, CaptureFrom = ESourceTarget.Target, CaptureWhen = ECaptureAttributeWhen.OnCreation,
            }, 1f);
            Assert.AreEqual(40f, captured.Evaluate(NumberContext.For(caster, target, 1)), 0.001f, "one that captures as it's made captures as it's read");

            var whole = new ScalableInt(new AttributeBackedScaler { CaptureAttribute = strength }, 2, EMagnitudeOperation.MultiplyWithScaler, ERoundingOperation.Round);
            Assert.AreEqual(14, whole.Evaluate(NumberContext.For(caster, target, 1)), "a whole number reads one too");
        });

        /// <summary>
        /// A constant is a field read: reading one allocates nothing (NUM-2, Phase 3's zero-allocation gate).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_2_Reading_a_constant_allocates_nothing() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            await Frames.Step();
            ScalableFloat range = 20f;
            ScalableInt count = 3;
            var context = NumberContext.For(caster, null, 1);
            float sum = 0f;

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() =>
            {
                sum += range.Evaluate(context);
                sum += count.Evaluate(context);
                sum += range.Evaluate(NumberContext.Of(caster));
            }, runs: 500, warmup: 20);

            Assert.That(sum, Is.GreaterThan(0f), "it read");
            Assert.That(bytes, Is.EqualTo(0f), "bytes per read");
        });

        /// <summary>
        /// A scaler read through a context with no carrier evaluates against a reusable carrier the context fills, so a
        /// read allocates nothing: By Level, an attribute, and a caller's magnitude (NUM-2).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_2_Reading_a_scaler_through_a_context_allocates_nothing() => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var caster = World.Entity(Hub.Runtime, Make.Set(Make.Element(strength, 7f, 7f)), "Caster");
            await Frames.Step();
            var key = Make.NewTag("Data.Key");
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.DERIVATION, IEffectOrigin.GenerateSourceDerivation(caster));
            data.SetByCaller(key, 4f);

            var byLevel = new ScalableFloat(new SimpleScaler { LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = false }, 0f, EMagnitudeOperation.UseScaler);
            var ofStrength = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = strength }, 2f);
            var ofCaller = new ScalableFloat(new SetByCallerScaler { Key = key, Default = 1f }, 0f, EMagnitudeOperation.UseScaler);
            var context = NumberContext.Of(data);
            float sum = 0f;

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() =>
            {
                sum += byLevel.Evaluate(context);
                sum += ofStrength.Evaluate(context);
                sum += ofCaller.Evaluate(context);
            }, runs: 500, warmup: 20);

            Assert.That(sum, Is.GreaterThan(0f), "it read");
            Assert.That(bytes, Is.EqualTo(0f), "bytes per read");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-3: a kind of your own
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A kind of a project's own is tested without an effect: a context for a test, and the number read in it. Where
        /// the context lacks what it reads, the number reads its own value (NUM-3).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_3_A_kind_of_your_own_is_tested_without_an_effect() => Live(async () =>
        {
            var caster = World.MonoEntity(name: "Caster");
            var target = World.MonoEntity(name: "Target");
            caster.transform.position = new Vector3(0f, 0f, 0f);
            target.transform.position = new Vector3(3f, 9f, 4f);
            await Frames.Step();

            var falloff = new ScalableFloat(new DistanceToTarget(), 0.5f);
            Assert.AreEqual(2.5f, falloff.Evaluate(NumberContext.For(caster, target, 1)), 0.001f, "0.5 × the flat distance, 5");
            Assert.AreEqual("0.5 × the distance to the target", falloff.ToString(), "and it says what it reads");

            var noTarget = new ScalableFloat(new DistanceToTarget(), 0.5f);
            Assert.AreEqual(0.5f, noTarget.Evaluate(NumberContext.For(caster, null, 1)), "with no target, it reads its number");

            var inCode = ScalableFloat.From(context => context.Level.CurrentValue * 2f);
            Assert.AreEqual(6f, inCode.Evaluate(NumberContext.For(caster, target, 3)), "an asset built in code can take a function");
        });

        /// <summary>
        /// A kind of a project's own works in an effect's magnitude, as a blow that falls off with distance: it's a scaler,
        /// so the effect reads it against its spec (NUM-3).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_3_A_kind_of_your_own_works_in_an_effect() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.MonoEntity(Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.MonoEntity(Make.Set(Make.Element(health, 100f, 100f)), "Target");
            caster.transform.position = Vector3.zero;
            target.transform.position = new Vector3(6f, 0f, 8f);
            var blow = Make.Effect("Blow", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -2f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = new DistanceToTarget(),
            });
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, blow), "the blow lands");
            await Frames.Step();
            Assert.AreEqual(80f, Health(target, health), 0.001f, "for 2 × the distance, 10");
        });

        /// <summary>
        /// A kind of a project's own works in a task's number, read as the task starts against the activation's data: the
        /// ability's caster and the target it was given (NUM-3).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_3_A_kind_of_your_own_works_in_a_task() => Live(async () =>
        {
            var caster = World.MonoEntity(name: "Caster");
            var target = World.MonoEntity(name: "Target");
            caster.transform.position = Vector3.zero;
            target.transform.position = new Vector3(0f, 0f, 12f);
            ReadNumberTask.Read.Clear();
            var reach = Make.Ability("Reach", Make.Stage(new ReadNumberTask { Number = new ScalableFloat(new DistanceToTarget(), 1f) }));
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(reach, 1, out int index), "the caster knows Reach");
            await Frames.Step();

            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, data)), "Reach activates");
            await Frames.Until(() => !abilities.IsExecuting, 10, "and ends");

            Assert.AreEqual(new List<float> { 12f }, ReadNumberTask.Read, "its task read the distance to its target as it started");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-4: Set By Caller from any context
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Set By Caller number read in a task reads the magnitude the caller set on the activation's data. It read only
        /// an effect spec's, so anything else read its default (NUM-4).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_4_A_task_reads_the_callers_magnitude() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            var radius = Make.NewTag("Data.Radius");
            ReadNumberTask.Read.Clear();
            var nova = Make.Ability("Nova", Make.Stage(new ReadNumberTask
            {
                Number = new ScalableFloat(new SetByCallerScaler { Key = radius, Default = 1f }, 0f, EMagnitudeOperation.UseScaler),
            }));
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(nova, 1, out int index), "the caster knows Nova");
            await Frames.Step();

            var data = ProcessDataPacket.Default();
            data.SetByCaller(radius, 6f);
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, data)), "Nova, for 6, activates");
            await Frames.Until(() => !abilities.IsExecuting, 10, "and ends");

            Assert.AreEqual(new List<float> { 6f }, ReadNumberTask.Read, "its task read the radius the caller set");
        });

        /// <summary>
        /// A process's own attribute that reads Set By Caller reads the magnitude set on the data that spawned it, as a
        /// projectile's speed set by its caster (NUM-4).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_4_A_processs_attribute_reads_the_callers_magnitude() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            var speedKey = Make.NewTag("Data.Speed");
            var arrow = World.Template<ProjectileMonoProcess>("Arrow");
            arrow.ProcessTiming = EProcessStepTiming.None;
            arrow.Effects = new List<GameplayEffect>();
            arrow.Attributes = new List<ProcessAttribute>
            {
                new()
                {
                    Role = Tags.PROJECTILE_SPEED, StartsFrom = EProcessAttributeStart.Own,
                    Value = new ScalableFloat(new SetByCallerScaler { Key = speedKey, Default = 5f }, 0f, EMagnitudeOperation.UseScaler),
                },
            };
            var target = World.MonoEntity(name: "Target");
            target.transform.position = new Vector3(0f, 0f, 1000f);
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            data.SetPrimary(Tags.DERIVATION, IEffectOrigin.GenerateSourceDerivation(caster));
            data.SetByCaller(speedKey, 40f);

            var fired = World.Spawn(arrow, data);
            await Frames.Step();
            Assert.IsTrue(fired.TryGetRole(Tags.PROJECTILE_SPEED, out float speed), "the arrow carries a speed");
            Assert.AreEqual(40f, speed, "the one its caster set, not its default");
        });

        /// <summary>
        /// An effect's Set By Caller reads its spec's magnitudes, as it did, and a cue's or a worker's number reads its
        /// effect's (NUM-4).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_4_An_effects_number_reads_its_specs_magnitude([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var damage = Make.NewTag("Data.Damage");
            var spec = gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(gas), Make.Effect("Hit", new EffectConfig { Target = health, Magnitude = -1f }));
            spec.SetByCaller(damage, 12f);
            await Frames.Step();

            var number = new ScalableFloat(new SetByCallerScaler { Key = damage, Default = 1f }, 0f, EMagnitudeOperation.UseScaler);
            Assert.AreEqual(12f, number.Evaluate(NumberContext.Of(spec)), "an effect's");
            Assert.AreEqual(12f, number.Evaluate(NumberContext.Of(new CueContext(gas, spec))), "its cue's");
            Assert.AreEqual(12f, number.Evaluate(NumberContext.Of(new EffectWorkerContext(gas, spec, null, null))), "its worker's");
            Assert.AreEqual(1f, number.Evaluate(NumberContext.Of(gas)), "and an entity's on its own, which has none, its default");
        });
    }
}
