using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using FarEmerald.PlayForge.ProvingGrounds.Recipes;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// AC-3 and AC-5, Decision 68: the Additional Content sample's formula presets, Scaler assets each named for what it
    /// computes, and the rest of its starter set: a melee attack, a bolt, a slow and a shield. Each, built from the books
    /// that make the sample's assets, reads or plays as its name says.
    /// </summary>
    public class AdditionalContentTests : ProvingGroundsTest
    {
        private MemoryRecipeMaker maker;
        private RecipeSet recipes;
        private FormulaSet formulas;
        private StarterSet starter;

        [SetUp]
        public void BuildTheBooks()
        {
            maker = new MemoryRecipeMaker();
            recipes = RecipeBook.Build(maker);
            formulas = FormulaBook.Build(maker);
            starter = StarterBook.Build(maker, recipes.Health);
        }

        [TearDown]
        public void Clean() => maker.Dispose();

        private static float ValueOf(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // An effect that adds what a formula reads to Reading, so the test reads it there
        private GameplayEffect Reader(ScalerAsset formula, ForgeAttribute reading) =>
            Make.Effect($"Read {formula.name}", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = reading,
                MagnitudeScaler = new SharedScaler { Asset = formula }, Magnitude = 1f, MagnitudeOp = EMagnitudeOperation.UseScaler,
            });

        // What an Armor formula reads for an entity with this Armor, the one the hit lands on
        private async UniTask<float> ReadArmor(Hub hub, ScalerAsset formula, float armor)
        {
            var reading = Make.Attribute("Reading");
            var target = World.Entity(hub, Make.Set(Make.Element(formulas.Armor, armor, armor), Make.Element(reading, 0f, 0f)), "Armored");
            var source = World.Entity(hub, name: "Source");
            await Frames.Step();
            Assert.IsTrue(World.Apply(source, target, Reader(formula, reading)), $"{formula.name} is read");
            await Frames.Step(2);
            return ValueOf(target, reading);
        }

        // What a stacks formula reads for an item held with this many stacks, whose granted effect reads it
        private async UniTask<float> ReadStacks(Hub hub, ScalerAsset formula, int stacks)
        {
            var reading = Make.Attribute("Reading");
            var holder = World.Entity(hub, Make.Set(Make.Element(reading, 0f, 0f)), "Holder");
            var charm = Make.Item("Charm");
            charm.Definition.MaxStackSize = 10;
            charm.GrantedEffects.Add(Reader(formula, reading));
            await Frames.Step();
            var items = holder.GetItemSystem();
            Assert.IsTrue(items.GiveAndEquipItem(charm, out _), "the charm is held");
            Assert.AreEqual(stacks - 1, items.GiveItem(charm, stacks - 1), $"with {stacks} stacks");
            await Frames.Step(2);
            return ValueOf(holder, reading);
        }

        /// <summary>
        /// Each formula reads as it's named: a hit's multiplier of 1 ÷ (1 + 0.06 × Armor) and 100 ÷ (100 + Armor), a flat
        /// −Armor, and an item's stacks through a line, a hyperbola and a power.
        /// </summary>
        [UnityTest]
        public IEnumerator AC_3_Each_formula_reads_as_its_name_says([Values] Hub hub) => Live(async () =>
        {
            Assert.AreEqual(0.25f, await ReadArmor(hub, formulas.ArmorReciprocal, 50f), 0.0001f, "1 ÷ (1 + 0.06 × 50)");
            Assert.AreEqual(100f / 150f, await ReadArmor(hub, formulas.ArmorHundredOver, 50f), 0.0001f, "100 ÷ (100 + 50)");
            Assert.AreEqual(-50f, await ReadArmor(hub, formulas.ArmorFlat, 50f), 0.0001f, "−50");
            Assert.AreEqual(0.45f, await ReadStacks(hub, formulas.StacksLinear, 3), 0.0001f, "0.15 × 3");
            Assert.AreEqual(1f - 1f / 1.45f, await ReadStacks(hub, formulas.StacksHyperbolic, 3), 0.0001f, "1 − 1 ÷ (1 + 0.15 × 3)");
            Assert.AreEqual(0.729f, await ReadStacks(hub, formulas.StacksExponential, 3), 0.0001f, "0.9 to the 3rd");
        });

        /// <summary>The formulas are named for what they compute, and none for a game.</summary>
        [Test]
        public void AC_3_The_formulas_are_named_for_their_math()
        {
            var names = new[]
            {
                formulas.ArmorReciprocal, formulas.ArmorHundredOver, formulas.ArmorFlat, formulas.StacksLinear, formulas.StacksHyperbolic,
                formulas.StacksExponential,
            };
            foreach (var formula in names)
            {
                Assert.That(formula.DisplayName, Does.Not.Contain("Dota").And.Not.Contain("Risk").And.Not.Contain("Hades"), formula.name);
                Assert.That(formula.Description, Is.Not.Empty, $"{formula.name} says what it's for");
            }
            Assert.AreEqual("1 ÷ (1 + 0.06 × Armor)", formulas.ArmorReciprocal.DisplayName);
            Assert.AreEqual("0.9 ^ Stacks", formulas.StacksExponential.DisplayName);
        }

        private static bool Cast(IGameplayAbilitySystem caster, Ability ability, IGameplayAbilitySystem target)
        {
            var abilities = caster.GetAbilitySystem();
            if (!abilities.HasAbility(ability)) Assert.IsTrue(abilities.GiveAbility(ability, 1, out _), $"{ability.name} is given");
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            return abilities.TryActivateAbility(ability, data);
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) => World.Entity(hub, Make.Set(
            Make.Element(recipes.Health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
            Make.Element(starter.MovementSpeed, 5f, 5f),
            Make.Element(starter.Shield, 0f, 0f, EAttributeOverflowPolicy.ZeroToBase)), name);

        /// <summary>A melee attack hits its target for 10, once a second; a bolt for 25, once in three.</summary>
        [UnityTest]
        public IEnumerator AC_5_A_melee_attack_and_a_bolt_hit_on_their_cooldowns([Values] Hub hub) => Live(async () =>
        {
            var hero = Fighter(hub, "Hero");
            var foe = Fighter(hub, "Foe");
            await Frames.Step();

            Assert.IsTrue(Cast(hero, starter.MeleeAttack, foe), "the attack starts");
            await Frames.Step(2);
            Assert.AreEqual(90f, ValueOf(foe, recipes.Health), 0.01f, "and hits for 10");
            Assert.IsFalse(Cast(hero, starter.MeleeAttack, foe), "on its cooldown, it waits");
            await Frames.Seconds(1.1f);
            Assert.IsTrue(Cast(hero, starter.MeleeAttack, foe), "a second on, it attacks again");

            Assert.IsTrue(Cast(hero, starter.Bolt, foe), "the bolt starts");
            await Frames.Step(2);
            Assert.AreEqual(100f - 10f - 10f - 25f, ValueOf(foe, recipes.Health), 0.01f, "and hits for 25");
            Assert.IsFalse(Cast(hero, starter.Bolt, foe), "on its cooldown, it waits");
            await Frames.Seconds(3.1f);
            Assert.IsTrue(Cast(hero, starter.Bolt, foe), "three seconds on, it bolts again");
        });

        /// <summary>A slow takes 30% of its target's Movement Speed for 3 seconds, then gives it back.</summary>
        [UnityTest]
        public IEnumerator AC_5_A_slow_takes_movement_speed_for_its_duration([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var slowed = Fighter(hub, "Slowed");
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, slowed, starter.Slow), "the slow lands");
            await Frames.Step(2);
            Assert.AreEqual(3.5f, ValueOf(slowed, starter.MovementSpeed), 0.01f, "70% of 5");
            await Frames.Seconds(3.1f);
            Assert.AreEqual(5f, ValueOf(slowed, starter.MovementSpeed), 0.01f, "and all of it back as it ends");
        });

        /// <summary>A shield of 50 takes its holder's hits first, the rest on Health, and fades as it ends.</summary>
        [UnityTest]
        public IEnumerator AC_5_A_shield_takes_hits_first_then_fades([Values] Hub hub) => Live(async () =>
        {
            var attacker = Fighter(hub, "Attacker");
            var shielded = Fighter(hub, "Shielded");
            await Frames.Step();

            Assert.IsTrue(World.Apply(shielded, shielded, starter.ShieldEffect), "the shield goes up");
            await Frames.Step(2);
            Assert.AreEqual(50f, ValueOf(shielded, starter.Shield), 0.01f, "50 of it");

            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = recipes.Health, Magnitude = -30f });
            World.Apply(attacker, shielded, blow);
            await Frames.Step(2);
            Assert.AreEqual(20f, ValueOf(shielded, starter.Shield), 0.01f, "a blow of 30 comes off the shield");
            Assert.AreEqual(100f, ValueOf(shielded, recipes.Health), 0.01f, "and none off Health");

            World.Apply(attacker, shielded, blow);
            await Frames.Step(2);
            Assert.AreEqual(0f, ValueOf(shielded, starter.Shield), 0.01f, "another empties it");
            Assert.AreEqual(90f, ValueOf(shielded, recipes.Health), 0.01f, "and its 10 left over comes off Health");

            await Frames.Seconds(5.1f);
            Assert.AreEqual(0f, ValueOf(shielded, starter.Shield), 0.01f, "the shield is gone as it ends");
            Assert.IsFalse(shielded.TryGetEffectContainer(starter.ShieldEffect, out _), "with its effect");
        });
    }
}
