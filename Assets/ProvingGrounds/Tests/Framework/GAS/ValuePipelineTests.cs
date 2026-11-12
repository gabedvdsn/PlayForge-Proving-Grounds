using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-2, the user's question of 2026-11-11 and Decision 46: a Value Pipeline, generic, which an effect names as its
    /// impact's calculation. Each hit goes through its stages as it lands, the caster's bonuses, the target's reductions, a
    /// critical roll, a barrier, so a change to every hit is a stage, not an edit to each attack.
    /// </summary>
    public class ValuePipelineTests : ProvingGroundsTest
    {
        private ForgeAttribute health, amplification, resistance, barrier;

        // Rolls that come out as the test says, and how many were drawn
        private sealed class ScriptedRandom : IForgeRandom
        {
            private readonly float[] rolls;
            public ScriptedRandom(params float[] rolls) => this.rolls = rolls;
            public int Drawn { get; private set; }
            public float Value => rolls[Drawn++ % rolls.Length];
            public Vector2 InsideUnitCircle => Vector2.zero;
            public Vector3 InsideUnitSphere => Vector3.zero;
        }

        private IForgeRandom previousRandom;
        private bool randomSet;

        [TearDown]
        public void PutBackChance()
        {
            if (randomSet) ForgeRandom.Use(previousRandom);
            randomSet = false;
        }

        private ScriptedRandom Rolls(params float[] rolls)
        {
            var scripted = new ScriptedRandom(rolls);
            previousRandom = ForgeRandom.Use(scripted);
            randomSet = true;
            return scripted;
        }

        private ValuePipeline Pipeline(params AbstractValueStage[] stages)
        {
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            pipeline.name = "Damage";
            pipeline.Stages = new List<AbstractValueStage>(stages);
            return pipeline;
        }

        // The attacker's amplification as 1 + x, the target's resistance as what's left: each read by its role (TW-1)
        private MultiplyStage Amplified() => new()
        {
            Label = "Amplification",
            By = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = amplification, CaptureFrom = ESourceTarget.Source }, 1f, EMagnitudeOperation.UseScaler),
        };

        private MultiplyStage Resisted() => new()
        {
            Label = "Resistance",
            By = new ScalableFloat(new AttributeBackedScaler { CaptureAttribute = resistance, CaptureFrom = ESourceTarget.Target }, 1f, EMagnitudeOperation.UseScaler),
        };

        private GameplayEffect Hit(string name, float amount, ValuePipeline pipeline, EEffectDurationPolicy policy = EEffectDurationPolicy.Instant,
            ForgeAttribute target = null)
        {
            var fx = Make.Effect(name, new EffectConfig { Policy = policy, Target = target ?? health, Magnitude = amount });
            fx.ImpactSpecification.Pipeline = pipeline;
            return fx;
        }

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private async Cysharp.Threading.Tasks.UniTask<(IGameplayAbilitySystem attacker, IGameplayAbilitySystem target)> Pair(Hub hub,
            float attackerAmplification = 0.5f, float targetResistance = 0.2f, float targetBarrier = 0f)
        {
            health = Make.Attribute("Health");
            amplification = Make.Attribute("Damage Amplification");
            amplification.Role = EAttributeRole.BonusShare;
            resistance = Make.Attribute("Damage Resistance");
            resistance.Role = EAttributeRole.Resistance;
            barrier = Make.Attribute("Barrier");
            var attacker = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f), Make.Element(amplification, attackerAmplification, attackerAmplification),
                Make.Element(resistance, 0f, 0f), Make.Element(barrier, 0f, 0f)), "Attacker");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f), Make.Element(amplification, 0f, 0f),
                Make.Element(resistance, targetResistance, targetResistance), Make.Element(barrier, targetBarrier, targetBarrier)), "Target");
            await Frames.Step();
            return (attacker, target);
        }

        /// <summary>
        /// A blow of 100 through the attacker's 50% amplification and the target's 20% resistance lands as 120, and the
        /// spec keeps the run: each stage, what it took and gave.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_2_A_pipeline_runs_each_hit_through_its_stages([Values] Hub hub) => Live(async () =>
        {
            var (attacker, target) = await Pair(hub);
            var blow = Hit("Blow", -100f, Pipeline(Amplified(), Resisted()));
            var spec = target.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(attacker), blow);
            Assert.IsTrue(target.ApplyGameplayEffect(spec), "the blow lands");
            await Frames.Step();

            Assert.AreEqual(1000f - 120f, Value(target, health), 0.01f, "100 × 1.5 × 0.8");
            Assert.IsNotNull(spec.Breakdown, "the spec keeps the run");
            Assert.AreEqual(2, spec.Breakdown.Steps.Count, "a step for each stage");
            Assert.AreEqual(-150f, spec.Breakdown.Steps[0].After, 0.01f, "amplified first");
            Assert.AreEqual(-120f, spec.Breakdown.Out, 0.01f, "then resisted");
            Assert.That(spec.Breakdown.ToString(), Does.Contain("Amplification").And.Contain("Resistance"), "and says so");
        });

        /// <summary>Two effects naming one pipeline both take its stages, and a stage added to it reaches both.</summary>
        [UnityTest]
        public IEnumerator TW_2_Every_effect_naming_a_pipeline_takes_its_stages([Values] Hub hub) => Live(async () =>
        {
            var (attacker, target) = await Pair(hub, targetResistance: 0f);
            var pipeline = Pipeline(Amplified());
            var blow = Hit("Blow", -100f, pipeline);
            var bolt = Hit("Bolt", -40f, pipeline);
            Assert.IsTrue(World.Apply(attacker, target, blow));
            Assert.IsTrue(World.Apply(attacker, target, bolt));
            await Frames.Step();
            Assert.AreEqual(1000f - 150f - 60f, Value(target, health), 0.01f, "both amplified");

            pipeline.Stages.Add(new MultiplyStage { Label = "Fury", By = 2f });
            Assert.IsTrue(World.Apply(attacker, target, blow));
            Assert.IsTrue(World.Apply(attacker, target, bolt));
            await Frames.Step();
            Assert.AreEqual(1000f - 210f - 300f - 120f, Value(target, health), 0.01f, "and both take the stage added");
        });

        /// <summary>A Chance stage rolls once for each hit as it lands: under the chance, triple, and the run notes it.</summary>
        [UnityTest]
        public IEnumerator TW_2_A_chance_stage_rolls_once_for_each_hit([Values] Hub hub) => Live(async () =>
        {
            var (attacker, target) = await Pair(hub, attackerAmplification: 0f, targetResistance: 0f);
            var blow = Hit("Blow", -100f, Pipeline(new ChanceStage { Label = "Critical", Chance = 0.5f, Multiplier = 3f }));
            var rolls = Rolls(0.1f, 0.9f);

            var first = target.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(attacker), blow);
            Assert.IsTrue(target.ApplyGameplayEffect(first));
            await Frames.Step();
            Assert.AreEqual(1000f - 300f, Value(target, health), 0.01f, "0.1 is under 0.5: triple");
            Assert.AreEqual("critical", first.Breakdown.Steps[0].Note, "noted");

            Assert.IsTrue(World.Apply(attacker, target, blow));
            await Frames.Step();
            Assert.AreEqual(1000f - 400f, Value(target, health), 0.01f, "0.9 isn't: 100");
            Assert.AreEqual(2, rolls.Drawn, "a roll for each hit");
        });

        /// <summary>An Absorb stage takes a blow on the target's barrier first, and the rest on its health.</summary>
        [UnityTest]
        public IEnumerator TW_2_An_absorb_stage_takes_a_blow_on_a_barrier_first([Values] Hub hub) => Live(async () =>
        {
            var (attacker, target) = await Pair(hub, attackerAmplification: 0f, targetResistance: 0f, targetBarrier: 30f);
            var blow = Hit("Blow", -100f, Pipeline(new AbsorbStage { Label = "Barrier", Into = barrier, On = NumberSide.Target }));
            Assert.IsTrue(World.Apply(attacker, target, blow));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(target, barrier), 0.01f, "the barrier takes 30");
            Assert.AreEqual(1000f - 70f, Value(target, health), 0.01f, "and health the rest");
        });

        /// <summary>
        /// A periodic impact runs each tick through it; a lasting one isn't run, as its value is read again as its attribute
        /// changes: +10 Strength under a ×2 pipeline stays +10.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_2_A_lasting_impact_isnt_run([Values] Hub hub) => Live(async () =>
        {
            var (attacker, _) = await Pair(hub);
            var strength = Make.Attribute("Strength");
            var holder = World.Entity(hub, Make.Set(Make.Element(strength, 20f, 20f)), "Holder");
            await Frames.Step();
            var bonus = Hit("Bonus", 10f, Pipeline(new MultiplyStage { By = 2f }), EEffectDurationPolicy.Infinite, strength);
            Assert.IsTrue(World.Apply(attacker, holder, bonus));
            await Frames.Step();
            Assert.AreEqual(30f, Value(holder, strength), 0.01f, "+10, as it is");
        });
    }
}
