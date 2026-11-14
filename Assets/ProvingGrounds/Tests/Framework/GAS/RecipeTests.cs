using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FarEmerald.PlayForge.ProvingGrounds.Recipes;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-23, Decision 63: each recipe of Documentation/RECIPES.md, built from the recipe book that makes the Additional
    /// Content sample's assets, plays as the page says.
    /// </summary>
    public class RecipeTests : ProvingGroundsTest
    {
        private sealed class ScriptedRandom : IForgeRandom
        {
            private readonly float[] rolls;
            public ScriptedRandom(params float[] rolls) => this.rolls = rolls;
            public float Value => rolls[Drawn++ % rolls.Length];
            public int Drawn { get; private set; }
            public Vector2 InsideUnitCircle => Vector2.zero;
            public Vector3 InsideUnitSphere => Vector3.zero;
        }

        private MemoryRecipeMaker maker;
        private RecipeSet recipes;
        private IForgeRandom previousRandom;
        private bool randomSet;

        [SetUp]
        public void BuildTheRecipes()
        {
            maker = new MemoryRecipeMaker();
            recipes = RecipeBook.Build(maker);
        }

        [TearDown]
        public void Clean()
        {
            maker.Dispose();
            if (randomSet) ForgeRandom.Use(previousRandom);
            randomSet = false;
        }

        private void Rolls(params float[] rolls)
        {
            var was = ForgeRandom.Use(new ScriptedRandom(rolls));
            if (!randomSet) previousRandom = was;
            randomSet = true;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name, float health = 100f) =>
            World.Entity(hub, Make.Set(Make.Element(recipes.Health, health, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        private float HealthOf(IGameplayAbilitySystem gas) =>
            gas.TryGetAttributeValue(recipes.Health, out var value) ? value.CurrentValue : float.NaN;

        // A plain blow, the test's own, to set a recipe off
        private GameplayEffect Blow(float amount) =>
            Make.Effect($"Blow {amount}", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = recipes.Health, Magnitude = amount });

        /// <summary>A curse that bursts: 5 each second for 3 seconds, then 30 as it ends.</summary>
        [UnityTest]
        public IEnumerator TW_23_A_curse_ticks_then_bursts_as_it_ends([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var cursed = Fighter(hub, "Cursed");
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, cursed, recipes.Curse));
            await Frames.Until(() => cursed.GetEffectSystem().Shelf.Count == 0, 600, "the curse runs out");
            await Frames.Step(2);
            Assert.AreEqual(100f - 3 * 5f - 30f, HealthOf(cursed), 0.01f, "three ticks of 5, then a burst of 30");
        });

        /// <summary>A counter: Retaliate strikes back at whoever hits its holder, for 10.</summary>
        [UnityTest]
        public IEnumerator TW_23_A_counter_strikes_back_at_its_attacker([Values] Hub hub) => Live(async () =>
        {
            var attacker = Fighter(hub, "Attacker");
            var guard = Fighter(hub, "Guard");
            await Frames.Step();
            Assert.IsTrue(guard.GetAbilitySystem().GiveAbility(recipes.Retaliate, 1, out _), "the guard knows Retaliate");

            Assert.IsTrue(World.Apply(attacker, guard, Blow(-20f)));
            await Frames.Until(() => HealthOf(attacker) < 100f, 60, "the guard strikes back");
            Assert.AreEqual(90f, HealthOf(attacker), 0.01f, "for 10");
            Assert.AreEqual(80f, HealthOf(guard), 0.01f, "after taking 20");
        });

        /// <summary>A crit: Critical Strike doubles a hit on a 25% chance.</summary>
        [UnityTest]
        public IEnumerator TW_23_A_critical_strike_doubles_a_hit_on_its_chance([Values] Hub hub) => Live(async () =>
        {
            var attacker = Fighter(hub, "Attacker");
            var target = Fighter(hub, "Target");
            await Frames.Step();

            Rolls(0.1f);
            Assert.IsTrue(World.Apply(attacker, target, recipes.CritBlow));
            Assert.AreEqual(60f, HealthOf(target), 0.01f, "a roll under 0.25 crits: 40");
            Rolls(0.9f);
            Assert.IsTrue(World.Apply(attacker, target, recipes.CritBlow));
            Assert.AreEqual(40f, HealthOf(target), 0.01f, "one over it doesn't: 20");
        });

        /// <summary>An execute: Culling Blow takes the rest where it would leave its target under a fifth of its Health.</summary>
        [UnityTest]
        public IEnumerator TW_23_An_execute_takes_the_rest_below_its_share([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var low = Fighter(hub, "Low", 30f);
            var high = Fighter(hub, "High", 50f);
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, low, recipes.CullingBlow));
            Assert.IsTrue(World.Apply(caster, high, recipes.CullingBlow));
            await Frames.Step(2);
            Assert.AreEqual(0f, HealthOf(low), 0.01f, "15 would leave 15, under a fifth of 100: it takes the rest");
            Assert.AreEqual(35f, HealthOf(high), 0.01f, "where it leaves 35, it's a hit of 15");
        });

        /// <summary>
        /// Lifesteal: Vampirism raises its holder's Lifesteal by half, so each blow it deals heals it half of what it deals.
        /// An effect has no lifesteal of its own (FN-22): Vampiric Strike, a blow with its own, became Vampirism.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_23_Lifesteal_heals_its_caster_half_the_blow([Values] Hub hub) => Live(async () =>
        {
            // Through the Lifesteal worker its project gives every entity, by the Lifesteal role's attribute, healing the
            // Health role's (FN-15)
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = recipes.Health });
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_LIFESTEAL, Attribute = recipes.Lifesteal });
            var was = ForgeProject.Use(project);
            try
            {
                var caster = World.Entity(hub, Make.Set(
                    Make.Element(recipes.Health, 50f, 100f, EAttributeOverflowPolicy.ZeroToBase),
                    Make.Element(recipes.Lifesteal, 0f, 0f)), "Caster");
                var target = Fighter(hub, "Target");
                await Frames.Step();

                Assert.IsTrue(World.Apply(caster, caster, recipes.Vampirism));
                await Frames.Step();
                Assert.IsTrue(World.Apply(caster, target, Blow(-20f)));
                await Frames.Step(2);
                Assert.AreEqual(80f, HealthOf(target), 0.01f, "it deals 20");
                Assert.AreEqual(60f, HealthOf(caster), 0.01f, "and heals its caster 10");
            }
            finally
            {
                ForgeProject.Use(was);
                Object.DestroyImmediate(project);
            }
        });

        /// <summary>Damage return: Thorns sends half of each blow its holder takes back to the one who dealt it.</summary>
        [UnityTest]
        public IEnumerator TW_23_Thorns_returns_half_of_each_blow([Values] Hub hub) => Live(async () =>
        {
            var attacker = Fighter(hub, "Attacker");
            var holder = Fighter(hub, "Holder");
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, holder, recipes.Thorns), "Thorns is on");
            await Frames.Step();

            Assert.IsTrue(World.Apply(attacker, holder, Blow(-20f)));
            await Frames.Step(3);
            Assert.AreEqual(80f, HealthOf(holder), 0.01f, "the holder takes 20");
            Assert.AreEqual(90f, HealthOf(attacker), 0.01f, "and its attacker 10 back");
        });

        /// <summary>Can't die: while Undying lasts, no blow takes its target below 1.</summary>
        [UnityTest]
        public IEnumerator TW_23_Undying_keeps_its_target_at_1([Values] Hub hub) => Live(async () =>
        {
            var foe = Fighter(hub, "Foe");
            var graved = Fighter(hub, "Graved");
            await Frames.Step();
            Assert.IsTrue(World.Apply(foe, graved, recipes.Undying));
            await Frames.Step();

            Assert.IsTrue(World.Apply(foe, graved, Blow(-1000f)));
            await Frames.Step(3);
            Assert.AreEqual(1f, HealthOf(graved), 0.01f, "it holds at 1");
            Assert.IsFalse(graved.IsDead, "and lives");
        });

        /// <summary>On hit: Frost Blade's holder's hits each bite for 5 more.</summary>
        [UnityTest]
        public IEnumerator TW_23_An_on_hit_bites_with_each_hit([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            await Frames.Step();
            Assert.IsTrue(holder.GetItemSystem().GiveAndEquipItem(recipes.FrostBlade, out _), "Frost Blade's equipped");
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, target, Blow(-10f)));
            await Frames.Step(3);
            Assert.AreEqual(85f, HealthOf(target), 0.01f, "10, and Frost Bite's 5");
        });

        /// <summary>On kill: Reaper's Mark feeds its holder 20 for each kill, and nothing for a hit that doesn't kill.</summary>
        [UnityTest]
        public IEnumerator TW_23_An_on_kill_feeds_its_holder([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder", 50f);
            var survivor = Fighter(hub, "Survivor");
            var victim = Fighter(hub, "Victim", 10f);
            await Frames.Step();
            Assert.IsTrue(holder.GetItemSystem().GiveAndEquipItem(recipes.ReapersMark, out _), "Reaper's Mark is equipped");
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, survivor, Blow(-10f)));
            await Frames.Step(3);
            Assert.AreEqual(50f, HealthOf(holder), 0.01f, "a hit that doesn't kill feeds nothing");

            Assert.IsTrue(World.Apply(holder, victim, Blow(-10f)));
            await Frames.Step(3);
            Assert.AreEqual(70f, HealthOf(holder), 0.01f, "a kill feeds 20");
        });

        /// <summary>A phase: the boss enrages as its Health falls to half, once.</summary>
        [UnityTest]
        public IEnumerator TW_23_A_boss_turns_its_phase_at_half_health([Values] Hub hub) => Live(async () =>
        {
            var boss = World.Entity(hub, recipes.BossSet, "Boss");
            var hero = Fighter(hub, "Hero");
            await Frames.Step();
            bool Enraged() => boss.GetTagCache().GetWeight(RecipeBook.Enraged) > 0;

            Assert.IsTrue(World.Apply(hero, boss, Blow(-400f)));
            await Frames.Step(3);
            Assert.IsFalse(Enraged(), "at 600 of 1000, not yet");

            Assert.IsTrue(World.Apply(hero, boss, Blow(-200f)));
            await Frames.Until(Enraged, 30, "at 400 of 1000, it enrages");
        });
    }
}
