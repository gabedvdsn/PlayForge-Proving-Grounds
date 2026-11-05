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
    }
}
