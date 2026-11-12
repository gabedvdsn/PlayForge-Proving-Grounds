using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-8, Decision 48: chance, built in. A Chance condition rolls once for a hit and keeps the roll; a chance can be
    /// pseudo-random, each caster's streak its own; luck rolls more and keeps the best, or the worst, by a chance's own
    /// number or the project's attribute; an ability rolls its chance to activate once; a hit's proc coefficient, its
    /// effect's or its ability's, scales its on-hit effects' chance and duration, and 0 sets nothing off.
    /// </summary>
    public class ChanceTests : ProvingGroundsTest
    {
        private ForgeAttribute health, luck;
        private Tag marked;

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

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            luck = Make.Attribute("Luck");
            marked = Make.NewTag("Test.TW8.Marked");
        }

        [TearDown]
        public void PutBackChance()
        {
            if (randomSet) ForgeRandom.Use(previousRandom);
            randomSet = false;
        }

        private ScriptedRandom Rolls(params float[] rolls)
        {
            var scripted = new ScriptedRandom(rolls);
            var was = ForgeRandom.Use(scripted);
            if (!randomSet) previousRandom = was;
            randomSet = true;
            return scripted;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name, float luckValue = 0f) => World.Entity(hub, Make.Set(
            Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
            Make.Element(luck, luckValue, luckValue)), name);

        private GameplayEffect Blow(float amount) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });

        private float Value(IGameplayAbilitySystem gas) => gas.TryGetAttributeValue(health, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>A Chance condition read twice in one hit reads the same, drawn once; read with no hit, it rolls each time.</summary>
        [UnityTest]
        public IEnumerator TW_8_A_Chance_condition_rolls_once_for_a_hit([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            await Frames.Step();
            var spec = target.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(caster), Blow(10f));
            var condition = new ChanceCondition { Chance = 0.5f };

            var rolls = Rolls(0.1f, 0.9f);
            var context = NumberContext.Of(spec);
            Assert.IsTrue(condition.Holds(context), "a 0.1 under its half holds");
            Assert.IsTrue(condition.Holds(context), "and holds again, the hit's roll kept");
            Assert.AreEqual(1, rolls.Drawn, "drawn once for the hit");
            Assert.IsFalse(condition.Holds(NumberContext.Of(caster as ISource)), "read with no hit, it rolls again, a 0.9");
            Assert.AreEqual(2, rolls.Drawn);
        });

        /// <summary>A pseudo-random chance's odds rise by its constant with each miss, a hit starts them again, each caster its own.</summary>
        [UnityTest]
        public IEnumerator TW_8_A_pseudo_random_chance_raises_its_odds_after_each_miss([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var other = Fighter(hub, "Other");
            await Frames.Step();
            Assert.AreEqual(0.0847f, ForgeChance.Constant(0.25f), 0.0005f, "a quarter's constant, as Dota's table has it");

            var roll = new ChanceRoll { PseudoRandom = true };
            var key = new object();
            Rolls(0.5f);
            var context = NumberContext.Of(caster as ISource);
            int first = -1;
            for (int i = 1; i <= 8 && first < 0; i++)
                if (ForgeChance.Roll(0.25f, in context, roll, key)) first = i;
            Assert.AreEqual(6, first, "each miss adds the constant: the sixth's odds, about 0.51, take a 0.5");
            Assert.IsFalse(ForgeChance.Roll(0.25f, in context, roll, key), "and the hit starts the odds again");

            for (int i = 0; i < 4; i++) ForgeChance.Roll(0.25f, in context, roll, key);
            var elsewhere = NumberContext.Of(other as ISource);
            Assert.IsFalse(ForgeChance.Roll(0.25f, in elsewhere, roll, key), "another caster's streak is its own, at its first odds");
        });

        /// <summary>Luck rolls more and keeps the best, or the worst; a chance's own, or else the caster's Luck attribute.</summary>
        [UnityTest]
        public IEnumerator TW_8_Luck_keeps_the_best_or_the_worst_of_more_rolls([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster", luckValue: 1f);
            await Frames.Step();
            var context = NumberContext.Of(caster as ISource);

            var rolls = Rolls(0.9f, 0.8f, 0.1f);
            Assert.IsTrue(ForgeChance.Roll(0.3f, in context, new ChanceRoll { OwnLuck = true, Luck = 2f }, null), "a luck of 2, the best of three");
            Assert.AreEqual(3, rolls.Drawn, "three draws");
            rolls = Rolls(0.1f, 0.2f, 0.9f);
            Assert.IsFalse(ForgeChance.Roll(0.3f, in context, new ChanceRoll { OwnLuck = true, Luck = -2f }, null), "a luck of −2, the worst of three");

            // The attribute playing its project's Luck role (TW-29; TW-28's fixed Luck slot before it)
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_LUCK, Attribute = luck });
            var was = ForgeProject.Use(project);
            try
            {
                rolls = Rolls(0.9f, 0.1f);
                Assert.IsTrue(ForgeChance.Roll(0.3f, in context, new ChanceRoll(), null), "the caster's Luck of 1, the project's, the best of two");
                Assert.AreEqual(2, rolls.Drawn, "two draws");
            }
            finally
            {
                ForgeProject.Use(was);
                Object.DestroyImmediate(project);
            }
        });

        /// <summary>An ability rolls its chance to activate once for each activation, and says so where it misses.</summary>
        [UnityTest]
        public IEnumerator TW_8_An_ability_rolls_its_chance_to_activate_once([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var gamble = Make.Ability("Gamble", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Blow(10f) } }));
            gamble.Definition.ChanceToActivate = 0.5f;
            await Frames.Step();
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(gamble, 1, out _), "it's known");

            var rolls = Rolls(0.9f);
            Assert.IsFalse(abilities.TryActivateAbility(gamble, out var refusal), "a 0.9 misses its half");
            Assert.AreEqual(EActivationRefusal.Chance, refusal.Reason, "refused by its chance");
            Assert.AreEqual(1, rolls.Drawn, "drawn once");

            rolls = Rolls(0.1f);
            Assert.IsTrue(abilities.TryActivateAbility(gamble, out _), "a 0.1 comes up");
            Assert.AreEqual(1, rolls.Drawn, "drawn once again");
            await Frames.Step(3);
            Assert.AreEqual(90f, Value(caster), 0.01f, "and it runs");
        });

        private OnHitEntry Marks(float seconds) => new()
        {
            Attribute = health,
            Effects = new List<GameplayEffect>
            {
                Make.Effect("Mark", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = seconds, GrantedTags = new List<Tag> { marked },
                }),
            },
        };

        /// <summary>A hit's proc coefficient scales its on-hit effects' chance and duration; 0 sets nothing off.</summary>
        [UnityTest]
        public IEnumerator TW_8_A_hits_proc_coefficient_scales_its_on_hits([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var third = Fighter(hub, "Third");
            var dagger = Make.Item("Dagger");
            var mark = Marks(4f);
            dagger.OnHit.Add(mark);
            var blow = Blow(10f);
            blow.OwnProcCoefficient = true;
            blow.ProcCoefficient = 0f;
            await Frames.Step();
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(dagger, out _));

            Assert.IsTrue(World.Apply(holder, first, blow));
            await Frames.Step(2);
            Assert.AreEqual(-1, first.GetTagCache().GetWeight(marked), "a coefficient of 0 sets nothing off");

            blow.ProcCoefficient = 0.5f;
            Rolls(0.4f);
            Assert.IsTrue(World.Apply(holder, second, blow));
            await Frames.Step(2);
            Assert.IsTrue(second.TryGetEffectContainer(mark.Effects[0], out var container), "half its chance takes a 0.4");
            Assert.AreEqual(2f, container.TotalDuration, 0.01f, "for half its 4 s");

            Rolls(0.6f);
            Assert.IsTrue(World.Apply(holder, third, blow));
            await Frames.Step(2);
            Assert.AreEqual(-1, third.GetTagCache().GetWeight(marked), "and misses a 0.6");
        });

        /// <summary>An effect with no coefficient of its own carries its ability's.</summary>
        [UnityTest]
        public IEnumerator TW_8_An_abilitys_proc_coefficient_stands_where_its_effect_sets_none([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var dagger = Make.Item("Dagger");
            dagger.OnHit.Add(Marks(4f));
            var strike = Make.Ability("Strike", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Blow(10f) } }));
            strike.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            strike.Definition.ProcCoefficient = 0f;
            await Frames.Step();
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(dagger, out _));
            var abilities = holder.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(strike, 1, out _));

            async UniTask Cast(IGameplayAbilitySystem at)
            {
                var data = ProcessDataPacket.Default();
                data.SetPrimary(Tags.TARGET_REAL, (ITarget)at);
                Assert.IsTrue(abilities.TryActivateAbility(strike, out _, data), "the strike casts");
                await Frames.Step(4);
            }

            await Cast(first);
            Assert.AreEqual(90f, Value(first), 0.01f, "the strike lands");
            Assert.AreEqual(-1, first.GetTagCache().GetWeight(marked), "its ability's coefficient of 0 sets nothing off");

            strike.Definition.ProcCoefficient = 1f;
            await Cast(second);
            Assert.IsTrue(second.GetTagCache().GetWeight(marked) > 0, "and a coefficient of 1 does");
        });
    }
}
