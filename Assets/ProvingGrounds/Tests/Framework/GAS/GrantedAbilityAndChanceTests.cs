using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Abilities granted by effects, and an effect's chance to apply (FEAT-8): a buff that lends its target an ability
    /// while it's on, and a strike whose bleed lands on a roll.
    /// </summary>
    public class GrantedAbilityAndChanceTests : ProvingGroundsTest
    {
        // Sets the abilities an effect grants, by reflection until the field is in
        private static void Grant(GameplayEffect effect, params Ability[] abilities)
        {
            var field = typeof(GameplayEffect).GetField("GrantedAbilities");
            Assert.IsNotNull(field, "an effect has Granted Abilities");
            field.SetValue(effect, new List<Ability>(abilities));
        }

        // Sets an effect's chance to apply, by reflection until the field is in
        private static void Chance(GameplayEffect effect, float chance)
        {
            var field = typeof(GameplayEffectImpact).GetField("ChanceToApply");
            Assert.IsNotNull(field, "an effect's impact has a Chance to Apply");
            field.SetValue(effect.ImpactSpecification,
                new ScalableFloat { Magnitude = chance, RealMagnitude = EMagnitudeOperation.UseMagnitude });
        }

        // An effect that lasts until it's removed, and changes nothing
        private GameplayEffect Buff(string name) =>
            Make.Effect(name, new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });

        // Applies an effect from the entity itself, at a level of its own
        private static bool ApplyAt(IGameplayAbilitySystem gas, GameplayEffect effect, int level)
        {
            var origin = IEffectOrigin.GenerateLevelerDerivation(gas, new IntValuePairClamped(level, 1, 10));
            return gas.ApplyGameplayEffect(gas.GenerateEffectSpec(origin, effect));
        }

        /// <summary>
        /// An effect couldn't give its target an ability, so a buff that lets you cast Fireball while it's on needed an
        /// item or a worker (FEAT-8). A lasting effect gives its target each ability it grants as it lands, at the level of
        /// what applied it, and takes each back as it comes off.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_A_lasting_effect_grants_its_abilities_while_it_is_on([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var abilities = gas.GetAbilitySystem();
            var fireball = Make.Ability("Fireball");
            var empower = Buff("Empower");
            Grant(empower, fireball);

            Assert.IsTrue(ApplyAt(gas, empower, 3), "the buff lands");
            Assert.IsTrue(abilities.TryGetAbilityContainer(fireball, out var granted), "and the entity has the ability it grants");
            Assert.AreEqual(3, granted.Spec.GetLevel().CurrentValue, "at the level of what applied the buff");
            await Frames.Step();

            gas.RemoveGameplayEffect(empower);
            await Frames.Step(2);
            Assert.IsFalse(abilities.HasAbility(fireball), "with the buff gone, so is the ability");
        });

        /// <summary>
        /// An ability an effect lent is interrupted as the effect comes off, as an item's is as it's taken off (FEAT-8).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_A_granted_ability_running_as_its_effect_leaves_is_interrupted([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var abilities = gas.GetAbilitySystem();
            var channel = Make.Ability("Channel", Make.Stage(new ProbeAbilityTask(World, "Channeling", frames: 200)));
            var trance = Buff("Trance");
            Grant(trance, channel);
            Assert.IsTrue(World.Apply(gas, gas, trance), "the trance lands");
            Assert.IsTrue(abilities.TryGetAbilityContainer(channel, out var container), "and lends the channel");
            await Frames.Step();

            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index)), "the channel starts");
            await Frames.Step(2);
            Assert.IsTrue(container.IsActive, "and runs");

            gas.RemoveGameplayEffect(trance);
            await Frames.Until(() => Ledger.Cleans("Channeling") == 1, 10, "the trance's end interrupts the channel");
            Assert.IsFalse(abilities.HasAbility(channel), "and takes it back");
        });

        /// <summary>
        /// An effect takes back only the abilities it gave, as an item does (GAS-48): one the entity knew already stays as
        /// the effect comes off (FEAT-8).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_An_effect_takes_back_only_the_abilities_it_gave([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var abilities = gas.GetAbilitySystem();
            var slash = Make.Ability("Slash");
            Assert.IsTrue(abilities.GiveAbility(slash, 1, out _), "the entity knows Slash already");
            var fury = Buff("Fury");
            Grant(fury, slash);
            Assert.IsTrue(World.Apply(gas, gas, fury), "the fury lands, though it can't lend a second Slash");
            await Frames.Step();

            gas.RemoveGameplayEffect(fury);
            await Frames.Step(2);
            Assert.IsTrue(abilities.HasAbility(slash), "and the entity keeps its own as the fury comes off");
        });

        // A source that draws the number it's set to, and counts its draws
        private sealed class FixedRandom : IForgeRandom
        {
            public float Next;
            public int Draws;

            public float Value
            {
                get
                {
                    Draws++;
                    return Next;
                }
            }

            public Vector2 InsideUnitCircle => Vector2.zero;
            public Vector3 InsideUnitSphere => Vector3.zero;
        }

        /// <summary>
        /// An effect always landed, so a strike couldn't bleed one time in three but through a worker (FEAT-8). An effect
        /// lands by its Chance to Apply, drawn once each time it's applied, from PlayForge's random source; a miss is
        /// refused, as an immunity refuses. A chance of 1, every effect's unless it's set, or of 0, draws nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_An_effect_lands_by_its_chance_to_apply([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var random = new FixedRandom { Next = 0.6f };
            var previous = ForgeRandom.Use(random);
            try
            {
                Assert.IsTrue(World.Apply(gas, gas, Buff("Sure")), "an effect lands unless its chance is set");
                var never = Buff("Never");
                Chance(never, 0f);
                Assert.IsFalse(World.Apply(gas, gas, never), "a chance of 0 never lands");
                Assert.AreEqual(0, random.Draws, "and neither draws a number");

                var even = Buff("Even");
                Chance(even, 0.5f);
                Assert.IsFalse(World.Apply(gas, gas, even), "a draw of 0.6 misses a chance of 0.5");
                Assert.IsFalse(gas.TryGetEffectContainer(even, out _), "which isn't on the entity");
                var likely = Buff("Likely");
                Chance(likely, 0.7f);
                Assert.IsTrue(World.Apply(gas, gas, likely), "and makes a chance of 0.7");
                Assert.AreEqual(2, random.Draws, "each application drawing once");
                await Frames.Step();
            }
            finally
            {
                ForgeRandom.Use(previous);
            }
        });

        /// <summary>
        /// A contained effect rolls its own chance, so a strike lands every time and its bleed only on a good roll (FEAT-8).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_A_contained_effect_rolls_its_own_chance([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var bleed = Make.Effect("Bleed", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });
            Chance(bleed, 0.3f);
            var strike = Make.Effect("Strike", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
            strike.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = bleed } };

            var random = new FixedRandom { Next = 0.5f };
            var previous = ForgeRandom.Use(random);
            try
            {
                Assert.IsTrue(World.Apply(gas, gas, strike), "the strike lands");
                Assert.IsFalse(gas.TryGetEffectContainer(bleed, out _), "and a draw of 0.5 misses the bleed's 0.3");
                random.Next = 0.2f;
                Assert.IsTrue(World.Apply(gas, gas, strike), "the next strike lands");
                Assert.IsTrue(gas.TryGetEffectContainer(bleed, out _), "and a draw of 0.2 makes its bleed");
                await Frames.Step(2);
                Assert.IsTrue(gas.TryGetAttributeValue(health, out var value) && value.CurrentValue == 80f, "each strike dealt its 10");
                Assert.AreEqual(2, random.Draws, "only the bleed drawing, once a strike");
            }
            finally
            {
                ForgeRandom.Use(previous);
            }
        });
    }
}
