using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// What an effect does, level by level, and what makes each of its numbers (FEAT-32): a number explains its read as a
    /// tree of parts, each with what it read and what it gave, and says what it needs where its context lacks it; an
    /// effect reads level by level, against a caster and a target where it's given them, with each impact's line and
    /// parts, its contained effects and its chance; and it says what it does at a level, in lines.
    /// </summary>
    public class LevelByLevelTests : ProvingGroundsTest
    {
        private static ScalableFloat Reading(AbstractScaler scaler) => new(scaler, 1f, EMagnitudeOperation.UseScaler);

        private static ScalableFloat ByLevel(params float[] values) =>
            Reading(new SimpleScaler { LevelValues = values, StretchOverLevels = false });

        private static ScalableFloat OfTargetsBase(Attribute health, float times) => new(new AttributeBackedScaler
        {
            CaptureAttribute = health, CaptureFrom = ESourceTarget.Target, CaptureWhat = EAttributeTargetBinary.Base,
        }, times);

        // The user's case: below 0.3 × Target's Health (base), −0.1 × Target's Health (base); else 0
        private static ConditionalScaler Execution(Attribute health) => new()
        {
            When = new AttributeCondition
            {
                On = NumberSide.Target, Attribute = health, Comparison = EComparisonOperator.LessThan, Value = OfTargetsBase(health, 0.3f),
            },
            TrueValue = OfTargetsBase(health, -0.1f),
            FalseValue = 0f,
        };

        private static string N(float value) => NumberText.Number(value);

        // ═══════════════════════════════════════════════════════════════════════════
        // LVL-1: a number explains its read
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A number explains its read as a tree: the user's case gives −20, its condition holds as 50 is below 60, and
        /// the branch it takes gives −0.1 × 200. Each part has what it read and what it gave (LVL-1).
        /// </summary>
        [UnityTest]
        public IEnumerator LVL_1_A_number_explains_its_read([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var weak = World.Entity(hub, Make.Set(Make.Element(health, 50f, 200f)), "Weak");
            await Frames.Step();

            var trace = NumberTrace.Of(Reading(Execution(health)), NumberContext.For(caster, weak, 1));
            Assert.AreEqual(-20f, trace.Value, 0.001f, "the read gives −20");
            Assert.IsTrue(trace.Resolved, "and everything it read was there");

            var conditional = trace.Parts.Single();
            Assert.AreEqual("Conditional", conditional.Kind, "its kind is the Conditional");
            var condition = conditional.Parts.First(part => part.Role == "When");
            Assert.IsTrue(condition.Holds, "whose condition holds");
            var compared = condition.Parts.Select(part => part.Value).ToArray();
            Assert.AreEqual(2, compared.Length, "it compared two values");
            Assert.AreEqual(50f, compared[0], 0.001f, "the target's 50 Health");
            Assert.AreEqual(60f, compared[1], 0.001f, "against 0.3 × 200");
            var branch = conditional.Parts.First(part => part.Role == "True");
            Assert.AreEqual(-20f, branch.Value, 0.001f, "and its True branch gives −0.1 × 200");
            Assert.AreEqual(200f, branch.Parts.Single().Value, 0.001f, "of the target's base Health, 200");

            var text = trace.ToString();
            StringAssert.Contains($"= {N(-20f)}", text, "the tree says the read");
            StringAssert.Contains("holds", text, "that the condition holds");
            StringAssert.Contains($"Target's Health (base) = {N(200f)}", text, "and what each part read");
        });

        /// <summary>
        /// A part whose context lacks what it reads says what it needs, and the read with it: a share of the target's
        /// Health read where there's no target (LVL-1).
        /// </summary>
        [UnityTest]
        public IEnumerator LVL_1_A_part_it_cant_read_says_what_it_needs([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            await Frames.Step();

            var trace = NumberTrace.Of(OfTargetsBase(health, 0.5f), NumberContext.For(caster, null, 1));
            Assert.IsFalse(trace.Resolved, "it can't be worked out here");
            Assert.AreEqual("the target", trace.Needs, "as it needs the target");
            var part = trace.Unresolved.Single();
            Assert.AreEqual("the target", part.Missing, "and its part says so");
            StringAssert.Contains("needs the target", trace.ToString(), "as does the tree");

            var resolved = NumberTrace.Of(OfTargetsBase(health, 0.5f), NumberContext.For(caster, caster, 1));
            Assert.IsTrue(resolved.Resolved, "with a target it can");
            Assert.AreEqual(50f, resolved.Value, 0.001f, "0.5 × 100");
        });

        /// <summary>A Group explains each member, with how it joins: added, multiplied or added after (LVL-1).</summary>
        [UnityTest]
        public IEnumerator LVL_1_A_group_explains_each_member([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var caster = World.Entity(hub, Make.Set(Make.Element(strength, 40f, 40f)), "Caster");
            await Frames.Step();

            var group = new ScalerGroup();
            group.AddMember(new ConstantScaler { Value = 5f }, ECalculationOperation.Add);
            group.AddMember(new AttributeBackedScaler { CaptureAttribute = strength }, ECalculationOperation.Add);
            group.AddMember(new ConstantScaler { Value = 2f }, ECalculationOperation.Multiply);
            group.AddMember(new ConstantScaler { Value = 3f }, ECalculationOperation.FlatBonus);
            var trace = NumberTrace.Of(Reading(group), NumberContext.For(caster, null, 1));

            Assert.AreEqual(93f, trace.Value, 0.001f, "(5 + 40) × 2 + 3");
            var members = trace.Parts.Single().Parts;
            Assert.That(members.Select(member => member.Value), Is.EqualTo(new[] { 5f, 40f, 2f, 3f }), "each member's value");
            Assert.That(members.Select(member => member.Role), Is.EqualTo(new[] { "Add", "Add", "Multiply", "Flat Bonus" }), "with how it joins");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // LVL-2: an effect level by level, with its parts
        // ═══════════════════════════════════════════════════════════════════════════

        private GameplayEffect Effect(string name, Attribute target, ScalableFloat magnitude,
            EEffectDurationPolicy policy = EEffectDurationPolicy.Instant)
        {
            var effect = Make.Effect(name, new EffectConfig { Policy = policy, Target = target });
            effect.ImpactSpecification.MagnitudeOperation = magnitude;
            return effect;
        }

        /// <summary>
        /// An effect reads level by level: each impact's number at each level, its line, and its parts at each level
        /// (LVL-2).
        /// </summary>
        [Test]
        public void LVL_2_An_effect_reads_level_by_level_with_its_parts()
        {
            var health = Make.Attribute("Health");
            var strike = Effect("Strike", health, ByLevel(-10f, -20f, -30f));

            var series = AbilityInspector.BuildEffectSeries(strike, 1, 3, null, null);
            var impact = series.Impacts.Single();
            Assert.That(impact.Magnitude, Is.EqualTo(new[] { -10f, -20f, -30f }), "its number at each level");
            Assert.AreEqual(NumberText.Of(strike.ImpactSpecification.MagnitudeOperation), impact.Line, "its line");
            Assert.AreEqual(3, impact.Parts.Length, "its parts at each level");
            Assert.AreEqual(-20f, impact.Parts[1].Value, 0.001f, "level 2's");
            Assert.IsTrue(string.IsNullOrEmpty(impact.Needs), "and it needs nothing it hasn't");
        }

        /// <summary>
        /// What an effect's number reads of its target is worked out against a target given it, and said where it isn't:
        /// Mana Void's 0.8 for each point of mana its target is missing (LVL-2).
        /// </summary>
        [UnityTest]
        public IEnumerator LVL_2_What_an_effect_reads_resolves_against_a_target([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var mana = Make.Attribute("Mana");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)), "Anti-Mage");
            var drained = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f), Make.Element(mana, 100f, 400f)), "Drained");
            await Frames.Step();

            var current = new ScalerGroup();
            current.AddMember(new AttributeBackedScaler { CaptureAttribute = mana, CaptureFrom = ESourceTarget.Target }, ECalculationOperation.Add);
            current.AddMember(new ConstantScaler { Value = -1f }, ECalculationOperation.Multiply);
            var missing = new ScalerGroup();
            missing.AddMember(new AttributeBackedScaler { CaptureAttribute = mana, CaptureFrom = ESourceTarget.Target, CaptureWhat = EAttributeTargetBinary.Base },
                ECalculationOperation.Add);
            missing.AddMember(current, ECalculationOperation.Add);
            var manaVoid = Effect("Mana Void", health, new ScalableFloat(missing, -0.8f));

            var alone = AbilityInspector.BuildEffectSeries(manaVoid, 1, 1, caster, null);
            Assert.AreEqual("the target", alone.Impacts.Single().Needs, "with no target, it says it needs one");

            var against = AbilityInspector.BuildEffectSeries(manaVoid, 1, 1, caster, drained);
            var impact = against.Impacts.Single();
            Assert.AreEqual(-240f, impact.Magnitude[0], 0.01f, "against a target missing 300 mana, 0.8 × 300");
            Assert.IsTrue(string.IsNullOrEmpty(impact.Needs), "with everything it reads there");
        });

        /// <summary>An effect's contained effects read with it, with when they land and their chance (LVL-2).</summary>
        [Test]
        public void LVL_2_An_effects_contained_effects_and_chance_read_too()
        {
            var health = Make.Attribute("Health");
            var crit = Effect("Crit", health, -225f);
            crit.ImpactSpecification.ChanceToApply = new ScalableFloat(0.15f);
            var attack = Effect("Attack", health, -100f);
            attack.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = crit } };

            var series = AbilityInspector.BuildEffectSeries(attack, 1, 2, null, null);
            Assert.That(series.Chance, Is.EqualTo(new[] { 1f, 1f }), "the attack always lands");
            var contained = series.Contained.Single();
            Assert.AreEqual(EApplyTickRemove.OnApply, contained.Policy, "its crit as it lands");
            Assert.AreSame(crit, contained.Series.Effect);
            Assert.That(contained.Series.Chance, Is.EqualTo(new[] { 0.15f, 0.15f }), "on its chance");
            Assert.That(contained.Series.Impacts.Single().Magnitude, Is.EqualTo(new[] { -225f, -225f }), "for 225 more");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // LVL-3: an effect says what it does at a level
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// An effect says what it does at a level, an impact a line: a curse's ticks over its time, a buff's change while
        /// it lasts, and a contained effect on its chance (LVL-3).
        /// </summary>
        [Test]
        public void LVL_3_An_effect_says_what_it_does_at_a_level()
        {
            var health = Make.Attribute("Health");
            var armor = Make.Attribute("Armor");
            var curse = Make.Effect("Curse", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 12f, PeriodicTicks = true, Ticks = 12, Target = health,
            });
            curse.ImpactSpecification.MagnitudeOperation = ByLevel(-10f, -15f);
            var ward = Effect("Ward", armor, 20f, EEffectDurationPolicy.Durational);
            ward.DurationSpecification.DurationOperation = new ScalableFloat(5f);
            var crit = Effect("Crit", health, -225f);
            crit.ImpactSpecification.ChanceToApply = new ScalableFloat(0.15f);
            var attack = Effect("Attack", health, -100f);
            attack.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = crit } };

            var cursed = AbilityInspector.BuildEffectSeries(curse, 1, 2, null, null).Describe(2);
            Assert.That(cursed, Has.Member($"{N(-15f)} Health each tick, 12 ticks over 12 s"), string.Join(" | ", cursed));
            var warded = AbilityInspector.BuildEffectSeries(ward, 1, 1, null, null).Describe(1);
            Assert.That(warded, Has.Member($"+{N(20f)} Armor while it lasts, 5 s"), string.Join(" | ", warded));
            var attacked = AbilityInspector.BuildEffectSeries(attack, 1, 1, null, null).Describe(1);
            Assert.That(attacked, Has.Member($"{N(-100f)} Health"), string.Join(" | ", attacked));
            Assert.That(attacked, Has.Member($"and Crit as it lands, 15% of the time: {N(-225f)} Health"), string.Join(" | ", attacked));
        }
    }
}
