using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The review's ability crafting, a stretch scenario: abilities built as the game runs from runes, their casts made
    /// with TaskSequenceBuilder, then cached and run. One spell effect serves every crafted spell, its damage set by the
    /// caster from the spell's runes (FEAT-2), and an ability runs a sequence made in code (FEAT-21).
    /// </summary>
    [Category("Stretch")]
    public class AbilityCraftingTests : ProvingGroundsTest
    {
        private enum Rune { Fire, Frost, Echo }

        private const float FireDamage = 10f, FrostDamage = 4f, EchoDelay = 0.25f;

        // A spellbook crafts a spell from its runes, once for each recipe, whatever their order
        private sealed class Spellbook
        {
            public readonly Tag Damage;
            public readonly GameplayEffect Spell;
            public int Crafted { get; private set; }

            private readonly ForgeAssets make;
            private readonly Dictionary<string, (Ability Ability, float Damage)> known = new();

            public Spellbook(ForgeAssets make, Attribute health)
            {
                this.make = make;
                Damage = make.NewTag("Data.Damage");

                // The one effect every crafted spell lands: 1 times the damage its caster sets
                Spell = make.Effect("Spell", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f,
                    MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = new SetByCallerScaler { Key = Damage },
                });
            }

            public (Ability Ability, float Damage) Craft(params Rune[] runes)
            {
                string recipe = string.Join("+", runes.OrderBy(rune => rune));
                if (known.TryGetValue(recipe, out var spell)) return spell;
                Crafted++;

                float damage = FireDamage * runes.Count(rune => rune == Rune.Fire) + FrostDamage * runes.Count(rune => rune == Rune.Frost);
                int strikes = 1 + runes.Count(rune => rune == Rune.Echo);

                // The cast, built from the runes: a strike for each echo, a quarter second apart
                var cast = TaskSequenceBuilder.Create(recipe);
                for (int i = 0; i < strikes; i++)
                {
                    if (i > 0) cast.Delay(EchoDelay);
                    cast.Task(Strike);
                }
                var ability = make.Ability($"Spell {recipe}", make.Stage(new RunSequenceTask { Definition = cast.Build() }));
                known[recipe] = spell = (ability, damage);
                return spell;
            }

            // A strike of the spell on the cast's target, with the damage its caster set
            private void Strike(SequenceDataPacket data)
            {
                if (data is not AbilityDataPacket cast || !cast.TryGetFirstTarget(out var target)) return;
                var strike = target.GenerateEffectSpec(cast.EffectOrigin, Spell);
                strike.TakeSetByCaller(cast.SetByCallerMagnitudes);
                target.ApplyCarrier(strike);
            }

            /// <summary>The caster casts a spell at a target, with the damage its runes give.</summary>
            public bool Cast(IGameplayAbilitySystem caster, (Ability Ability, float Damage) spell, IGameplayAbilitySystem target)
            {
                var abilities = caster.GetAbilitySystem();
                if (!abilities.HasAbility(spell.Ability)) abilities.GiveAbility(spell.Ability, 1, out _);
                var data = ProcessDataPacket.Default();
                data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
                data.SetByCaller(Damage, spell.Damage);
                return abilities.TryActivateAbility(spell.Ability, out _, data, EAbilityActivationPolicyExtended.AlwaysActivate);
            }
        }

        private static float Value(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>A crafted spell casts with its runes' damage: two fire runes strike once for 20, and two echoes strike three times (FEAT-2, FEAT-21).</summary>
        [UnityTest]
        public IEnumerator Stretch_A_crafted_spell_casts_with_its_runes([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f));
            var mage = World.Entity(hub, set, "Mage");
            var dummy = World.Entity(hub, set, "Dummy");
            var book = new Spellbook(Make, health);

            Assert.IsTrue(book.Cast(mage, book.Craft(Rune.Fire, Rune.Fire), dummy), "a fireball of two fire runes casts");
            await Frames.Until(() => !mage.GetAbilitySystem().IsExecuting, 10, "and ends");
            Assert.AreEqual(80f, Value(dummy, health), "and strikes once for 20");

            Assert.IsTrue(book.Cast(mage, book.Craft(Rune.Fire, Rune.Echo, Rune.Echo), dummy), "an echoing bolt casts");
            await Frames.Until(() => !mage.GetAbilitySystem().IsExecuting, 60, "and ends");
            Assert.AreEqual(50f, Value(dummy, health), "and strikes three times for 10");
        });

        /// <summary>
        /// A recipe is crafted once, whatever its runes' order, and the cached spell runs afresh on each cast: one spell
        /// effect serves every crafted spell, each with its own damage (FEAT-2).
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_A_recipe_is_crafted_once_and_cast_again([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 200f, 200f));
            var mage = World.Entity(hub, set, "Mage");
            var dummy = World.Entity(hub, set, "Dummy");
            var book = new Spellbook(Make, health);

            var frostfire = book.Craft(Rune.Fire, Rune.Frost);
            Assert.AreSame(frostfire.Ability, book.Craft(Rune.Frost, Rune.Fire).Ability, "the same runes in another order give the same spell");
            Assert.AreEqual(1, book.Crafted, "crafted once");

            for (int cast = 1; cast <= 3; cast++)
            {
                Assert.IsTrue(book.Cast(mage, frostfire, dummy), $"frostfire casts, time {cast}");
                await Frames.Until(() => !mage.GetAbilitySystem().IsExecuting, 10, "and ends");
                Assert.AreEqual(200f - cast * (FireDamage + FrostDamage), Value(dummy, health), "and strikes for 14 each time");
            }

            var frost = book.Craft(Rune.Frost);
            Assert.IsTrue(book.Cast(mage, frost, dummy), "a frost spell casts through the same effect");
            await Frames.Until(() => !mage.GetAbilitySystem().IsExecuting, 10, "and ends");
            Assert.AreEqual(200f - 3 * 14f - FrostDamage, Value(dummy, health), "for its own 4");
            Assert.AreEqual(2, book.Crafted, "two recipes crafted in all");
        });
    }
}
