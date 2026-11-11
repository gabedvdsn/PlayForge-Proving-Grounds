using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>A condition of a project's own (NUM-17): it holds where an effect's target is its caster.</summary>
    [System.Serializable]
    [GameplayCondition("Target Is Caster", Reads = NumberReads.Caster | NumberReads.Target)]
    public class TargetIsCasterCondition : GameplayCondition
    {
        protected override bool Evaluate(in NumberContext context) => ReferenceEquals(context.Caster, context.Target);

        public override string Describe() => "the target is the caster";
    }

    /// <summary>
    /// A scaler's own parameters as numbers (FEAT-29 phase 3): a Conditional's condition, a class that reads the number's
    /// context, with its branches; a Clamped's bounds and what it clamps; a Randomized's variance; Set By Caller's
    /// default; and a Threshold's tiers. Each keeps what it read as a plain float, and takes a kind.
    /// </summary>
    public class ScalerNumberTests : ProvingGroundsTest
    {
        private static float Current(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // times × Target's Health (base)
        private static ScalableFloat OfTargetsBase(Attribute health, float times) => new(new AttributeBackedScaler
        {
            CaptureAttribute = health, CaptureFrom = ESourceTarget.Target, CaptureWhat = EAttributeTargetBinary.Base,
        }, times);

        // The user's case: below 0.3 × Target's Health (base), −0.1 × Target's Health (base); else 0
        private static ConditionalScaler Execution(Attribute health) => new()
        {
            When = new AttributeCondition
            {
                On = NumberSide.Target, Attribute = health, What = EAttributeTargetBinary.Current,
                Comparison = EComparisonOperator.LessThan, Value = OfTargetsBase(health, 0.3f),
            },
            TrueValue = OfTargetsBase(health, -0.1f),
            FalseValue = 0f,
        };

        private static ScalableFloat ByLevel(params float[] values) =>
            new(new SimpleScaler { LevelValues = values, StretchOverLevels = false }, 1f, EMagnitudeOperation.UseScaler);

        private static ScalableFloat Reading(AbstractScaler scaler) => new(scaler, 1f, EMagnitudeOperation.UseScaler);

        private GameplayEffect Granting(string name, Tag tag) =>
            Make.Effect(name, new EffectConfig { Target = null, GrantedTags = new List<Tag> { tag } });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-17: conditions as classes, and the user's case
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The user's case, as one scaler: an execution that takes a tenth of its target's base Health where the target's
        /// Health is below 0.3 of its base, and nothing otherwise. The threshold was a plain float, and so was each
        /// branch, so it took a Group, a worker or code. The threshold and both branches are numbers (NUM-17, NUM-18).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_17_A_conditional_executes_below_part_of_the_targets_health([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var weak = World.Entity(hub, Make.Set(Make.Element(health, 50f, 200f)), "Weak");
            var hale = World.Entity(hub, Make.Set(Make.Element(health, 150f, 200f)), "Hale");
            await Frames.Step();

            var execution = Make.Effect("Execution", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = 1f,
                MagnitudeOp = EMagnitudeOperation.UseScaler, MagnitudeScaler = Execution(health),
            });
            Assert.IsTrue(World.Apply(caster, weak, execution), "applied to the weak");
            Assert.IsTrue(World.Apply(caster, hale, execution), "and to the hale");
            await Frames.Step();

            Assert.AreEqual(30f, Current(weak, health), "50 of 200 is below 0.3 × 200 = 60, so it takes 0.1 × 200 = 20");
            Assert.AreEqual(150f, Current(hale, health), "150 of 200 isn't, so it takes nothing");

            Assert.AreEqual(
                $"{NumberText.Number(-0.1f)} × Target's Health (base) if Target's Health < 0.3 × Target's Health (base), else 0",
                NumberText.Of(Reading(Execution(health))), "and the field says so in a line");
        });

        /// <summary>
        /// The conditions that come with the framework, each read against a number's context: a tag, counted by weight on
        /// either side; an attribute, current or base, against a number; the level, or how far up its range; two numbers
        /// compared; always and never; and all of, any of and not (NUM-17).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_17_The_built_in_conditions_read_the_context([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var marked = Make.NewTag("Marked");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 80f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 30f, 100f)), "Target");
            await Frames.Step();
            Assert.IsTrue(World.Apply(caster, target, Granting("Mark", marked)));
            Assert.IsTrue(World.Apply(caster, target, Granting("Mark Again", marked)));
            await Frames.Step();
            Assert.AreEqual(2, target.GetTagCache().GetWeight(marked), "the target holds the tag twice");

            var context = NumberContext.For(caster, target, 3, 5);
            TagCondition Has(NumberSide side, EComparisonOperator op = EComparisonOperator.GreaterThan, int count = 0) =>
                new() { On = side, Query = new TagQuery { Tag = marked, Operator = op, Magnitude = count } };

            Assert.IsTrue(Has(NumberSide.Target).Holds(context), "the target has the tag");
            Assert.IsFalse(Has(NumberSide.Caster).Holds(context), "and the caster hasn't");
            Assert.IsTrue(Has(NumberSide.Target, EComparisonOperator.GreaterOrEqual, 2).Holds(context), "a tag held twice counts twice on the target, as on the caster");
            Assert.IsFalse(Has(NumberSide.Target, EComparisonOperator.GreaterOrEqual, 3).Holds(context), "and not three times");

            AttributeCondition Health(NumberSide side, EAttributeTargetBinary what, EComparisonOperator op, ScalableFloat value) =>
                new() { On = side, Attribute = health, What = what, Comparison = op, Value = value };

            Assert.IsTrue(Health(NumberSide.Target, EAttributeTargetBinary.Current, EComparisonOperator.LessThan, 50f).Holds(context), "the target's 30 Health is below 50");
            Assert.IsFalse(Health(NumberSide.Caster, EAttributeTargetBinary.Current, EComparisonOperator.LessThan, 50f).Holds(context), "the caster's 80 isn't");
            Assert.IsTrue(Health(NumberSide.Target, EAttributeTargetBinary.Base, EComparisonOperator.Equal, 100f).Holds(context), "its base Health is 100");
            Assert.IsTrue(Health(NumberSide.Target, EAttributeTargetBinary.Current, EComparisonOperator.LessThan, OfTargetsBase(health, 0.5f)).Holds(context),
                "and a threshold that is a number of a kind: 30 is below 0.5 × its base");
            Assert.IsFalse(new AttributeCondition { On = NumberSide.Target, Attribute = Make.Attribute("Mana"), Value = 0f }.Holds(context),
                "an attribute the entity hasn't got doesn't hold");

            // Level 3 of 1 to 5 is halfway up
            Assert.IsTrue(new LevelCondition { Comparison = EComparisonOperator.GreaterOrEqual, Value = 3f }.Holds(context), "level 3 is at least 3");
            Assert.IsFalse(new LevelCondition { Comparison = EComparisonOperator.GreaterThan, Value = 3f }.Holds(context), "and not above it");
            Assert.IsTrue(new LevelCondition { Relative = true, Comparison = EComparisonOperator.GreaterOrEqual, Value = 0.5f }.Holds(context), "halfway up its range");
            Assert.IsFalse(new LevelCondition { Relative = true, Comparison = EComparisonOperator.GreaterThan, Value = 0.5f }.Holds(context), "and not past halfway");

            var castersHealth = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = health }, 1f);
            var twiceTargets = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = health, CaptureFrom = ESourceTarget.Target }, 2f);
            Assert.IsTrue(new CompareCondition { Left = castersHealth, Comparison = EComparisonOperator.GreaterThan, Right = twiceTargets }.Holds(context),
                "the caster's 80 Health is above twice the target's 30");
            Assert.IsFalse(new CompareCondition { Left = twiceTargets, Comparison = EComparisonOperator.GreaterThan, Right = castersHealth }.Holds(context), "and not the other way");

            Assert.IsTrue(new AlwaysCondition().Holds(context), "always");
            Assert.IsFalse(new NeverCondition().Holds(context), "never");

            var holds = Has(NumberSide.Target);
            var fails = Has(NumberSide.Caster);
            Assert.IsTrue(new AllCondition { Conditions = { holds, new AlwaysCondition() } }.Holds(context), "all of two that hold");
            Assert.IsFalse(new AllCondition { Conditions = { holds, fails } }.Holds(context), "and not where one doesn't");
            Assert.IsTrue(new AnyCondition { Conditions = { fails, holds } }.Holds(context), "any of two, one holding");
            Assert.IsFalse(new AnyCondition { Conditions = { fails, new NeverCondition() } }.Holds(context), "and not where neither does");
            Assert.IsTrue(new NotCondition { Condition = fails }.Holds(context), "not one that doesn't hold");
            Assert.IsFalse(new NotCondition { Condition = holds }.Holds(context), "and not one that does");

            // Where the context lacks what a condition reads, it doesn't hold, and neither does "not" it
            var noTarget = NumberContext.For(caster, null, 3, 5);
            Assert.IsFalse(Has(NumberSide.Target).Holds(noTarget), "with no target, the target's tag doesn't hold");
            Assert.IsFalse(new NotCondition { Condition = Has(NumberSide.Target) }.Holds(noTarget), "and neither does not having it");
            Assert.IsTrue(new AnyCondition { Conditions = { Has(NumberSide.Target), new AlwaysCondition() } }.Holds(noTarget), "any of still takes one that can be read");

            // A project's own, one class
            Assert.IsTrue(new TargetIsCasterCondition().Holds(NumberContext.For(caster, caster, 1)), "a project's own condition holds");
            Assert.IsFalse(new TargetIsCasterCondition().Holds(context), "and doesn't where it shouldn't");
            Assert.AreEqual(NumberReads.Caster | NumberReads.Target, new TargetIsCasterCondition().Reads, "it reads what its attribute says");
            Assert.AreEqual(NumberReads.Target, Has(NumberSide.Target).Reads, "a tag condition reads the side it looks at");
            Assert.AreEqual(NumberReads.Caster | NumberReads.Target, new AllCondition { Conditions = { holds, fails } }.Reads, "and a group reads what its members do");
        });

        /// <summary>A condition says itself in a line, with tags by their display names (NUM-17, ED-67).</summary>
        [Test]
        public void NUM_17_A_condition_says_itself_in_a_line()
        {
            var health = Make.Attribute("Health");
            var marked = Make.NewTag("Marked");
            var has = new TagCondition { On = NumberSide.Target, Query = new TagQuery { Tag = marked } };
            var stacked = new TagCondition { On = NumberSide.Caster, Query = new TagQuery { Tag = marked, Operator = EComparisonOperator.GreaterOrEqual, Magnitude = 3 } };
            var low = new AttributeCondition { On = NumberSide.Target, Attribute = health, Comparison = EComparisonOperator.LessOrEqual, Value = 50f };

            Assert.AreEqual($"Target has {marked}", NumberText.Of(has));
            Assert.AreEqual($"Caster's {marked} ≥ 3", NumberText.Of(stacked));
            Assert.AreEqual($"Target's {health.GetName()} ≤ 50", NumberText.Of(low));
            Assert.AreEqual("level > 4", NumberText.Of(new LevelCondition { Comparison = EComparisonOperator.GreaterThan, Value = 4f }));
            Assert.AreEqual("2 < 3", NumberText.Of(new CompareCondition { Left = 2f, Comparison = EComparisonOperator.LessThan, Right = 3f }));
            Assert.AreEqual("always", NumberText.Of(new AlwaysCondition()));
            Assert.AreEqual("never", NumberText.Of(new NeverCondition()));
            Assert.AreEqual($"Target has {marked} and Target's {health.GetName()} ≤ 50", NumberText.Of(new AllCondition { Conditions = { has, low } }));
            Assert.AreEqual($"Target has {marked} or Target's {health.GetName()} ≤ 50", NumberText.Of(new AnyCondition { Conditions = { has, low } }));
            Assert.AreEqual($"not (Target has {marked})", NumberText.Of(new NotCondition { Condition = has }));
            Assert.AreEqual("the target is the caster", NumberText.Of(new TargetIsCasterCondition()));
            Assert.AreEqual("no condition", NumberText.Of((GameplayCondition)null));

            Assert.AreEqual($"2 if Target has {marked}, else 1",
                NumberText.Of(Reading(new ConditionalScaler { When = has, TrueValue = 2f, FalseValue = 1f })), "and a Conditional says its condition between its branches");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-18: a Conditional's True and False
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Conditional's two branches were scaler slots. Each is a number: a constant reads as a Fixed Value scaler did,
        /// and a branch can be any kind, as a value by level (NUM-18).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_18_A_conditionals_branches_are_numbers([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var marked = Make.NewTag("Marked");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            await Frames.Step();

            var onMarked = new TagCondition { On = NumberSide.Target, Query = new TagQuery { Tag = marked } };
            var plain = new ScalableFloat(new ConditionalScaler { When = onMarked, TrueValue = 1.5f, FalseValue = 1f }, 2f);
            var byLevel = new ScalableFloat(new ConditionalScaler { When = onMarked, TrueValue = ByLevel(10f, 20f, 30f), FalseValue = 1f }, 2f);
            var context = NumberContext.For(caster, target, 2, 3);

            Assert.AreEqual(2f, plain.Evaluate(context), "unmarked: 2 × False, 1");
            Assert.AreEqual(2f, byLevel.Evaluate(context), "and so for the other");

            Assert.IsTrue(World.Apply(caster, target, Granting("Mark", marked)));
            await Frames.Step();

            Assert.AreEqual(3f, plain.Evaluate(context), "marked: 2 × True, 1.5");
            Assert.AreEqual(40f, byLevel.Evaluate(context), "and a True by level reads level 2's 20, twice");

            var spec = target.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster),
                Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f }));
            Assert.AreEqual(3f, plain.Evaluate(spec), "and it reads the same against an effect's spec");

            Assert.AreEqual(0f, Reading(new ConditionalScaler { When = null, TrueValue = 5f, FalseValue = 0f }).Evaluate(context), "with no condition, it reads False");
        });

        /// <summary>The user's case read as a number allocates nothing, through a context and through an effect's spec (NUM-18).</summary>
        [UnityTest]
        public IEnumerator NUM_18_Reading_a_conditional_allocates_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var marked = Make.NewTag("Marked");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var weak = World.Entity(hub, Make.Set(Make.Element(health, 50f, 200f)), "Weak");
            await Frames.Step();
            Assert.IsTrue(World.Apply(caster, weak, Granting("Mark", marked)));
            await Frames.Step();

            var execution = Reading(Execution(health));
            var grouped = Reading(new ConditionalScaler
            {
                When = new AllCondition
                {
                    Conditions =
                    {
                        new TagCondition { On = NumberSide.Target, Query = new TagQuery { Tag = marked } },
                        new NotCondition { Condition = new LevelCondition { Comparison = EComparisonOperator.GreaterThan, Value = 9f } },
                        new CompareCondition { Left = OfTargetsBase(health, 1f), Comparison = EComparisonOperator.GreaterThan, Right = 100f },
                    },
                },
                TrueValue = new ScalableFloat(new ClampedScaler { MinValue = 0f, MaxValue = 15f, Value = OfTargetsBase(health, 0.1f) }, 1f, EMagnitudeOperation.UseScaler),
                FalseValue = 0f,
            });
            var spec = weak.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster),
                Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f }));
            var context = NumberContext.For(caster, weak, 1);
            var ofSpec = NumberContext.Of(spec);

            Assert.AreEqual(-20f, execution.Evaluate(context), "it reads");
            Assert.AreEqual(-20f, execution.Evaluate(spec), "against the spec too");
            Assert.AreEqual(15f, grouped.Evaluate(ofSpec), "0.1 × 200, capped at 15");
            float sum = 0f;

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() =>
            {
                sum += execution.Evaluate(context);
                sum += execution.Evaluate(spec);
                sum += execution.Evaluate(ofSpec);
                sum += grouped.Evaluate(context);
                sum += grouped.Evaluate(ofSpec);
            }, runs: 500, warmup: 20);

            Assert.That(sum, Is.LessThan(0f), "it read");
            Assert.That(bytes, Is.EqualTo(0f), "bytes per read");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-19: a Clamped's Min, Max and Value
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Clamped scaler's Min and Max were plain floats, and what it clamped a scaler slot. All three are numbers, read
        /// with the scaler as their owner, so its Max can be 2 × its Min, and its Min can scale by level (NUM-19).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_19_A_clamped_scalers_bounds_are_numbers([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            await Frames.Step();
            NumberContext At(int level) => NumberContext.For(caster, null, level, 3);

            var plain = Reading(new ClampedScaler { MinValue = 150f, MaxValue = 400f, Value = ByLevel(100f, 300f, 500f) });
            Assert.AreEqual(150f, plain.Evaluate(At(1)), "100 is raised to the least");
            Assert.AreEqual(300f, plain.Evaluate(At(2)), "300 goes through");
            Assert.AreEqual(400f, plain.Evaluate(At(3)), "and 500 is capped");

            var relative = Reading(new ClampedScaler
            {
                MinValue = ByLevel(10f, 20f, 30f),
                MaxValue = new ScalableFloat(new RelativeToFieldKind { Field = nameof(ClampedScaler.MinValue) }, 2f),
                Value = 45f,
            });
            Assert.AreEqual(20f, relative.Evaluate(At(1)), "45 capped at 2 × the least, 10");
            Assert.AreEqual(40f, relative.Evaluate(At(2)), "and at 2 × 20");
            Assert.AreEqual(45f, relative.Evaluate(At(3)), "and inside 30 to 60");

            var spec = caster.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster),
                Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f }));
            var ofCaster = Reading(new ClampedScaler
            {
                MinValue = 0f,
                MaxValue = new ScalableFloat(new RelativeToFieldKind { Field = nameof(ClampedScaler.MinValue) }, 1f, EMagnitudeOperation.AddScaler) { Magnitude = 25f },
                Value = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = health }, 0.5f),
            });
            Assert.AreEqual(25f, ofCaster.Evaluate(spec), "against an effect's spec: half the caster's 100 Health, capped at its least + 25");

            Assert.AreEqual(1f, Reading(new ClampedScaler()).Evaluate(At(1)), "one made with nothing set clamps a value by level, 1, between 0 and 100");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-20: a Randomized's Variance, Min and Max
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Randomized scaler's Variance and its two multipliers were plain floats. Each is a number, read with the
        /// scaler as its owner: a constant gives the bounds it gave, a Variance by level widens them with the level, and
        /// its Max multiplier can be 3 × its Min (NUM-20).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_20_A_randomized_scalers_variance_is_a_number([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            await Frames.Step();
            var spec = caster.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster),
                Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f }));

            var plain = new RandomizedScaler { LevelValues = new[] { 100f }, StretchOverLevels = false, Variance = 0.25f };
            Assert.AreEqual(75f, plain.GetMinValue(spec), 0.001f, "a constant Variance gives the least it gave");
            Assert.AreEqual(125f, plain.GetMaxValue(spec), 0.001f, "and the most");

            var multiplied = new RandomizedScaler
            {
                LevelValues = new[] { 100f }, StretchOverLevels = false, VarianceMode = EVarianceMode.Multiplier,
                MinMultiplier = 0.5f,
                MaxMultiplier = new ScalableFloat(new RelativeToFieldKind { Field = nameof(RandomizedScaler.MinMultiplier) }, 3f),
            };
            Assert.AreEqual(50f, multiplied.GetMinValue(spec), 0.001f, "its Min multiplier, 0.5");
            Assert.AreEqual(150f, multiplied.GetMaxValue(spec), 0.001f, "and a Max that is 3 × its Min");
            multiplied.Initialize(spec);
            Assert.That(multiplied.Evaluate(spec), Is.InRange(50f, 150f), "and a roll lands between them");

            // 10% at level 1, 50% at level 2
            var widening = Reading(new RandomizedScaler { LevelValues = new[] { 100f }, StretchOverLevels = false, Variance = ByLevel(0.1f, 0.5f) });
            bool pastTenPercent = false;
            for (int i = 0; i < 200; i++)
            {
                Assert.That(widening.Evaluate(NumberContext.For(caster, null, 1, 2)), Is.InRange(90f - 0.001f, 110f + 0.001f), "level 1 varies by 10%");
                float wide = widening.Evaluate(NumberContext.For(caster, null, 2, 2));
                Assert.That(wide, Is.InRange(50f - 0.001f, 150f + 0.001f), "level 2 by 50%");
                pastTenPercent |= wide < 90f || wide > 110f;
            }
            Assert.IsTrue(pastTenPercent, "and level 2 does roll past level 1's bounds");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-21: Set By Caller's Default
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Set By Caller's Default was a plain float. It is a number, so where the caller set nothing it can fall back to
        /// a value by level; a constant reads as it did, and a value the caller set still wins (NUM-21).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_21_Set_by_callers_default_is_a_number([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var power = Make.NewTag("Power");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            await Frames.Step();

            var plain = Reading(new SetByCallerScaler { Key = power, Default = 3f });
            var byLevel = Reading(new SetByCallerScaler { Key = power, Default = ByLevel(5f, 7f) });
            var context = NumberContext.For(caster, null, 2, 2);
            Assert.AreEqual(3f, plain.Evaluate(context), "a constant default, as before");
            Assert.AreEqual(7f, byLevel.Evaluate(context), "and a default by level reads level 2's");

            var spec = caster.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster),
                Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f }));
            Assert.AreEqual(3f, plain.Evaluate(spec), "against a spec whose caller set nothing, the default");
            spec.SetByCaller(power, 12f);
            Assert.AreEqual(12f, plain.Evaluate(spec), "and what the caller set wins");
            Assert.AreEqual(12f, byLevel.Evaluate(NumberContext.Of(spec)), "over a default of any kind");

            Assert.AreEqual($"the caller's {power}, else 5/7 by level", NumberText.Of(byLevel), "and its line says the default as a number");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-22: a Threshold's tier values and Default
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Threshold scaler's tier values and its Default were plain floats. Each is a number: a constant tier reads as
        /// it did, and a tier, or the Default, can be any kind, as a share of the caster's attribute (NUM-22).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_22_A_threshold_scalers_tiers_are_numbers([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var caster = World.Entity(hub, Make.Set(Make.Element(strength, 40f, 40f)), "Caster");
            await Frames.Step();
            ScalableFloat OfStrength(float times) => new(new AttributeBackedScaler { CaptureAttribute = strength }, times);
            NumberContext At(int level) => NumberContext.For(caster, null, level, 5);

            var tiers = Reading(new ThresholdScaler
            {
                Thresholds = new[]
                {
                    new LevelThreshold { Level = 3, Value = 2f },
                    new LevelThreshold { Level = 5, Value = OfStrength(0.5f) },
                },
                DefaultValue = OfStrength(0.1f),
            });
            Assert.AreEqual(4f, tiers.Evaluate(At(1)), "below every tier: the default, 0.1 × the caster's 40 Strength");
            Assert.AreEqual(2f, tiers.Evaluate(At(3)), "a constant tier, as before");
            Assert.AreEqual(2f, tiers.Evaluate(At(4)), "which holds up to the next");
            Assert.AreEqual(20f, tiers.Evaluate(At(5)), "and a tier that is a kind: 0.5 × Strength");

            Assert.AreEqual(2f, Reading(new ThresholdScaler()).Evaluate(NumberContext.For(caster, null, 10, 20)), "one made with nothing set reads its own tiers: 1 from level 1, 2 from 10, 3 from 20");
            Assert.AreEqual(1f, Reading(new ThresholdScaler()).Evaluate(At(1)), "as before");
            Assert.AreEqual(6f, Reading(new ThresholdScaler { Thresholds = new LevelThreshold[0], DefaultValue = 6f }).Evaluate(At(2)), "with no tiers, the default");
            Assert.AreEqual(0f, Reading(new ThresholdScaler { Thresholds = new[] { new LevelThreshold { Level = 1 } } }).Evaluate(At(2)), "a tier made with no value reads 0");

            var even = new ThresholdScaler { MaxLevel = 20 };
            even.SetupEvenThresholds(2, 10f, 30f);
            Assert.AreEqual(30f, Reading(even).Evaluate(NumberContext.For(caster, null, 11, 20)), "tiers set up evenly still read");
        });
    }
}
