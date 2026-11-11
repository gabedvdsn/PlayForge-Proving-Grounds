using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-3, the user's note of 2026-11-11: wherever an attribute is read, the read chooses its retention groups: all of
    /// them, the whole value; only these; or all but these, a group naming the groups under it too. A hero's Strength is
    /// 20 of its own, 10 from an item's group and 5 from a ring's, under the item's: 35 in all.
    /// </summary>
    public class RetentionGroupReadTests : ProvingGroundsTest
    {
        private ForgeAttribute strength, result, resistance, health;
        private Tag items, ring;

        [SetUp]
        public void Groups()
        {
            items = Tag.Generate("Test.TW3.Items");
            ring = Tag.Generate("Test.TW3.Items.Ring");
        }

        // A hero of 20 Strength, 10 more from an item and 5 from a ring, each in its group
        private async Cysharp.Threading.Tasks.UniTask<IGameplayAbilitySystem> Hero(Hub hub)
        {
            strength = Make.Attribute("Strength");
            result = Make.Attribute("Result");
            resistance = Make.Attribute("Resistance");
            health = Make.Attribute("Health");
            var hero = World.Entity(hub, Make.Set(Make.Element(strength, 20f, 20f), Make.Element(result, 0f, 0f),
                Make.Element(resistance, 0.5f, 0.5f), Make.Element(health, 100f, 100f)), "Hero");
            await Frames.Step();
            Assert.IsTrue(World.Apply(hero, hero, Grouped("Item", strength, 10f, items)), "an item's 10 Strength");
            Assert.IsTrue(World.Apply(hero, hero, Grouped("Ring", strength, 5f, ring)), "a ring's 5");
            await Frames.Step();
            Assert.IsTrue(hero.TryGetAttributeValue(strength, out var all) && UnityEngine.Mathf.Approximately(all.CurrentValue, 35f), "35 in all");
            return hero;
        }

        private GameplayEffect Grouped(string name, ForgeAttribute attribute, float amount, Tag group)
        {
            var fx = Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = attribute, Magnitude = amount });
            fx.Definition.RetentionGroup = group;
            return fx;
        }

        private static RetentionGroups Read(ERetentionGroupRead read, params Tag[] groups) => new() { Read = read, Groups = new List<Tag>(groups) };

        // What a number gives, written into Result by an instant Override from the hero to itself
        private async Cysharp.Threading.Tasks.UniTask<float> Reads(IGameplayAbilitySystem hero, AbstractScaler number)
        {
            var write = Make.Effect("Write", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = result, Op = ECalculationOperation.Override,
                Magnitude = 1f, MagnitudeScaler = number, MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler,
            });
            Assert.IsTrue(World.Apply(hero, hero, write));
            await Frames.Step();
            return hero.TryGetAttributeValue(result, out var value) ? value.CurrentValue : float.NaN;
        }

        private AttributeBackedScaler StrengthOf(RetentionGroups groups) =>
            new() { CaptureAttribute = strength, CaptureFrom = ESourceTarget.Source, CaptureWhat = EAttributeTargetBinary.Current, Groups = groups };

        /// <summary>× Attribute reads the groups it names: the item's group and the ring's under it, all but them, or all.</summary>
        [UnityTest]
        public IEnumerator TW_3_X_Attribute_reads_the_groups_it_names([Values] Hub hub) => Live(async () =>
        {
            var hero = await Hero(hub);
            Assert.AreEqual(15f, await Reads(hero, StrengthOf(Read(ERetentionGroupRead.OnlyThese, items))), 0.01f,
                "only the items' group, the ring's under it too");
            Assert.AreEqual(5f, await Reads(hero, StrengthOf(Read(ERetentionGroupRead.OnlyThese, ring))), 0.01f, "only the ring's");
            Assert.AreEqual(20f, await Reads(hero, StrengthOf(Read(ERetentionGroupRead.AllButThese, items))), 0.01f, "all but the items': his own");
            Assert.AreEqual(35f, await Reads(hero, StrengthOf(Read(ERetentionGroupRead.All))), 0.01f, "all of it");
            Assert.AreEqual(35f, await Reads(hero, StrengthOf(null)), 0.01f, "as a read saved before the field reads");
        });

        /// <summary>An Attribute condition reads its groups: the items' 15 isn't above 16, where the whole 35 is.</summary>
        [UnityTest]
        public IEnumerator TW_3_An_attribute_condition_reads_its_groups([Values] Hub hub) => Live(async () =>
        {
            var hero = await Hero(hub);
            ConditionalScaler Above16(RetentionGroups groups) => new()
            {
                When = new AttributeCondition
                {
                    On = NumberSide.Caster, Attribute = strength, What = EAttributeTargetBinary.Current, Groups = groups,
                    Comparison = EComparisonOperator.GreaterThan, Value = 16f,
                },
                TrueValue = 1f, FalseValue = 0f,
            };
            Assert.AreEqual(0f, await Reads(hero, Above16(Read(ERetentionGroupRead.OnlyThese, items))), 0.01f, "the items' 15 isn't above 16");
            Assert.AreEqual(1f, await Reads(hero, Above16(Read(ERetentionGroupRead.All))), 0.01f, "the whole 35 is");
            Assert.That(new AttributeCondition { On = NumberSide.Caster, Attribute = strength, Groups = Read(ERetentionGroupRead.OnlyThese, items),
                Comparison = EComparisonOperator.GreaterThan, Value = 16f }.Describe(), Does.Contain("(" + items + ")"), "and says which");
        });

        /// <summary>
        /// A Math worker's Relative To reads its groups: a blow less the hero's resistance, his own 0.5 left out and a
        /// charm's 0.2 read, takes 20% off, not 70%.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_3_A_math_workers_relative_reads_its_groups([Values] Hub hub) => Live(async () =>
        {
            strength = Make.Attribute("Strength");
            resistance = Make.Attribute("Resistance");
            health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f), Make.Element(resistance, 0.5f, 0.5f));
            set.WorkerGroup.AttributeWorkers.Add(new MathAttributeWorker
            {
                TimingConfig = AbstractAttributeWorker.EChangeEventTiming.PreChange, TargetAttribute = health, AllowSelfModification = true,
                TargetModification = EAttributeTargetExpanded.Current, SignPolicy = ESignPolicyExtended.Negative,
                AnyImpactType = true, ImpactTypes = new List<Tag>(), AnyContextTag = true, ValidContextTags = new List<Tag>(),
                Operation = ECalculationOperation.Multiply, OperationTarget = EAttributeTarget.Current,
                OperationPolicy = EMathApplicationPolicy.OneMinus, From = EFromSelfSource.FromSelf, RelativeTo = resistance,
                RelativeMultiplier = 1f, RelativeGroups = Read(ERetentionGroupRead.OnlyThese, items),
            });
            var hero = World.Entity(hub, set, "Hero");
            var foe = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Foe");
            await Frames.Step();
            Assert.IsTrue(World.Apply(hero, hero, Grouped("Charm", resistance, 0.2f, items)), "a charm's 0.2");
            await Frames.Step();

            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
            Assert.IsTrue(World.Apply(foe, hero, blow));
            await Frames.Step();
            Assert.IsTrue(hero.TryGetAttributeValue(health, out var left));
            Assert.AreEqual(92f, left.CurrentValue, 0.01f, "10 less the charm's 20%, not 70%");
        });

        // A threshold that counts how often it fires
        private sealed class CountingThreshold : ThresholdAnalysisWorker
        {
            public int Fired;

            protected override IEnumerable<IRootAction> OnThresholdReached(IGameplayAbilitySystem system, AttributeValue value, FrameSummary frameSummary)
            {
                Fired++;
                yield break;
            }
        }

        /// <summary>A Threshold worker reads its groups: at 10 of the items' Strength, not the hero's 20 of his own.</summary>
        [UnityTest]
        public IEnumerator TW_3_A_threshold_reads_its_groups([Values] Hub hub) => Live(async () =>
        {
            strength = Make.Attribute("Strength");
            var threshold = new CountingThreshold
            {
                Target = strength, Groups = Read(ERetentionGroupRead.OnlyThese, items), Threshold = 10f,
                Comparison = EThresholdComparison.GreaterThanOrEqual, TriggerOnce = true,
            };
            var set = Make.Set(Make.Element(strength, 20f, 20f));
            set.WorkerGroup.AnalysisWorkers.Add(threshold);
            var hero = World.Entity(hub, set, "Hero");
            await Frames.Step(2);
            Assert.AreEqual(0, threshold.Fired, "his own 20 isn't the items'");

            Assert.IsTrue(World.Apply(hero, hero, Grouped("Item", strength, 10f, items)), "an item's 10");
            await Frames.Step(2);
            Assert.AreEqual(1, threshold.Fired, "the items' 10 reaches it");
        });
    }
}
