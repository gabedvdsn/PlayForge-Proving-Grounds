using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Cleanse and immunity (FEAT-8), which the Status-effect lab needs: an effect that takes off the effects tagged as
    /// it names as it lands, and one that, while it's on, refuses them. An effect names another by the other's asset tag
    /// or a context tag, or a child of either.
    /// </summary>
    public class CleanseAndImmunityTests : ProvingGroundsTest
    {
        // Sets one of an effect's lists of tags that name other effects, which a cleanse removes and an immunity blocks
        private static void Name(GameplayEffect effect, string list, params Tag[] tags)
        {
            var field = typeof(GameplayEffectTags).GetField(list);
            Assert.IsNotNull(field, $"an effect has {list}");
            // The tags are a struct, so the field is set on a copy, which goes back
            object copy = effect.Tags;
            field.SetValue(copy, new List<Tag>(tags));
            effect.Tags = (GameplayEffectTags)copy;
        }

        // An effect that lasts, changes nothing, and is tagged as the kind given
        private GameplayEffect Status(string name, Tag kind)
        {
            var effect = Make.Effect(name, new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });
            effect.Tags.ContextTags = new List<Tag> { kind };
            return effect;
        }

        private static bool On(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.GetEffectSystem().Shelf.Any(container => container.Spec.Base == effect);

        /// <summary>
        /// Nothing took off a set of effects at once: a cleanse took a removal requirement on each effect it meant to
        /// clear (FEAT-8). A cleanse takes off each effect on its target tagged as one it names, or a child of one, and
        /// leaves the rest.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_A_cleanse_takes_off_the_effects_it_names([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var debuff = Make.NewTag("Test.Status.Debuff");
            var poison = Status("Poison", Tag.Generate(debuff.Name + ".Poison"));
            var burn = Status("Burn", debuff);
            var shield = Status("Shield", Make.NewTag("Test.Status.Buff"));
            Assert.That(World.Apply(gas, gas, poison) && World.Apply(gas, gas, burn) && World.Apply(gas, gas, shield), "the statuses land");
            await Frames.Step();

            var cleanse = Make.Effect("Cleanse", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Instant });
            Name(cleanse, "RemovesEffectsTagged", debuff);
            Assert.That(World.Apply(gas, gas, cleanse), "the cleanse lands");
            await Frames.Step(2);

            Assert.IsFalse(On(gas, poison), "it takes off an effect tagged as a child of the tag it names");
            Assert.IsFalse(On(gas, burn), "and one tagged as the tag itself");
            Assert.IsTrue(On(gas, shield), "and leaves the rest");
        });

        /// <summary>
        /// Nothing made an entity immune to a kind of effect but a requirement on each effect of the kind (FEAT-8). While
        /// an immunity is on its target, an effect tagged as one it names, or a child of one, can't land; once it's gone,
        /// it can.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_An_immunity_refuses_the_effects_it_names([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var debuff = Make.NewTag("Test.Status.Debuff");
            var poison = Status("Poison", Tag.Generate(debuff.Name + ".Poison"));
            var shield = Status("Shield", Make.NewTag("Test.Status.Buff"));
            var ward = Status("Ward", Make.NewTag("Test.Status.Buff"));
            Name(ward, "BlocksEffectsTagged", debuff);
            Assert.That(World.Apply(gas, gas, ward), "the ward lands");
            await Frames.Step();

            Assert.IsFalse(World.Apply(gas, gas, poison), "an effect the ward names is refused");
            Assert.IsFalse(On(gas, poison), "and isn't on the entity");
            Assert.IsTrue(World.Apply(gas, gas, shield), "one it doesn't name lands");

            gas.RemoveGameplayEffect(ward);
            await Frames.Step(2);
            Assert.IsTrue(World.Apply(gas, gas, poison), "with the ward gone, the poison lands");
        });

        /// <summary>
        /// A cleanse and an immunity named an effect by its asset tag or a context tag only, so "whatever makes you
        /// Burning" needed a context tag on every effect that burns (FEAT-8, Decision 30). They name an effect by a tag it
        /// grants too, as Unreal's do.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_A_cleanse_and_an_immunity_name_an_effect_by_a_tag_it_grants([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var burning = Make.NewTag("Test.State.Burning");
            var fire = Make.Effect("Fire", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });
            fire.Tags.GrantedTags = new List<Tag> { burning };
            var douse = Make.Effect("Douse", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Instant });
            Name(douse, "RemovesEffectsTagged", burning);
            var fireproof = Status("Fireproof", Make.NewTag("Test.Status.Buff"));
            Name(fireproof, "BlocksEffectsTagged", burning);

            Assert.IsTrue(World.Apply(gas, gas, fire), "the fire lands");
            await Frames.Step();
            Assert.IsTrue(World.Apply(gas, gas, douse), "the douse lands");
            await Frames.Step(2);
            Assert.IsFalse(On(gas, fire), "a cleanse takes off an effect by a tag it grants");

            Assert.IsTrue(World.Apply(gas, gas, fireproof), "the fireproofing lands");
            await Frames.Step();
            Assert.IsFalse(World.Apply(gas, gas, fire), "and an immunity refuses one by a tag it grants");
            Assert.IsFalse(On(gas, fire), "which isn't on the entity");
        });

        // A bleed that stacks to three, sharing one duration, whose bound overflows as set
        private (GameplayEffect Bleed, StackBoundsConfig Bound) Bleed(float seconds)
        {
            var bleed = Make.Effect("Bleed", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = seconds, FixedDeltaTime = 1f, Target = null,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = EEffectStackPolicy.StacksShareOneDuration,
                Interaction = EEffectInteractionPolicy.RefreshContainerDuration,
                StackAmount = 1,
            });
            var bound = new StackBoundsConfig { Min = 0, Max = 3 };
            bleed.DurationSpecification.StackingConfigs.Add(bound);
            return (bleed, bound);
        }

        private static void Overflow(StackBoundsConfig bound, string field, object value)
        {
            var member = typeof(StackBoundsConfig).GetField(field);
            Assert.IsNotNull(member, $"a stack bound has {field}");
            member.SetValue(bound, value);
        }

        /// <summary>
        /// A stack bound held an effect at its max and nothing more happened, so a bleed couldn't hemorrhage at its fourth
        /// stack (FEAT-8, Decision 30). As a stack would take an effect past its bound's max, the bound's overflow effects land
        /// from the same origin, and the effect's stacks clear when the bound says so.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_An_effect_at_its_max_stacks_overflows([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var hemorrhage = Make.Effect("Hemorrhage", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -15f });
            var (bleed, bound) = Bleed(10f);
            Overflow(bound, "OverflowEffects", new List<GameplayEffect> { hemorrhage });
            Overflow(bound, "ClearStacksOnOverflow", true);

            for (int i = 0; i < 3; i++) Assert.IsTrue(World.Apply(gas, gas, bleed), "the bleed lands");
            Assert.IsTrue(gas.TryGetEffectContainer(bleed, out var container) && ((AbstractStackingEffectContainer)container).Stacks == 3,
                "in three stacks, its max");
            Assert.IsTrue(gas.TryGetAttributeValue(health, out var value) && value.CurrentValue == 100f, "and nothing has overflowed");

            World.Apply(gas, gas, bleed);
            await Frames.Step(2);
            gas.TryGetAttributeValue(health, out value);
            Assert.AreEqual(85f, value.CurrentValue, "a fourth overflows, and the hemorrhage lands");
            Assert.IsFalse(gas.TryGetEffectContainer(bleed, out _), "and the bleed's stacks clear");
        });

        /// <summary>
        /// An application that overflows refreshed the effect's duration all the same (FEAT-8). With Deny Refresh, it leaves
        /// the duration as it was, and the stacks stay at the max.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_8_Deny_Refresh_keeps_an_overflowing_application_from_refreshing([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var (bleed, bound) = Bleed(10f);
            Overflow(bound, "DenyRefreshOnOverflow", true);

            for (int i = 0; i < 3; i++) Assert.IsTrue(World.Apply(gas, gas, bleed), "the bleed lands");
            await Frames.Step(3);
            Assert.IsTrue(gas.TryGetEffectContainer(bleed, out var container), "and lasts");
            float before = container.DurationRemaining;
            Assert.That(before, Is.LessThan(10f), "and has run a while");

            World.Apply(gas, gas, bleed);
            Assert.AreEqual(before, container.DurationRemaining, "an application past the max doesn't refresh it");
            Assert.AreEqual(3, ((AbstractStackingEffectContainer)container).Stacks, "and the stacks stay at the max");
        });
    }
}
