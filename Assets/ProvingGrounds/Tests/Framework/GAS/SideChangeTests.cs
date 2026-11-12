using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-18, Decision 56: changing sides as the game runs. The hubs set an entity's sides, its tags following; and an
    /// effect changes its target's sides while it lasts, to its caster's (mind control) or to those it names (a pacify, to
    /// Neutral), the highest Priority holding, ties to the latest, and the target's own sides back as the last ends. An
    /// effect that interrupts breaks what its target runs as the side changes.
    /// </summary>
    public class SideChangeTests : ProvingGroundsTest
    {
        private Tag red, blue, green, neutral;

        [SetUp]
        public void Sides()
        {
            red = Make.NewTag("Test.TW18.Red");
            blue = Make.NewTag("Test.TW18.Blue");
            green = Make.NewTag("Test.TW18.Green");
            neutral = Make.NewTag("Test.TW18.Neutral");
        }

        // An entity of a side, on either hub
        private IGameplayAbilitySystem Of(Hub hub, string name, Tag side)
        {
            var gas = World.Entity(hub, Make.UnusedSet(), name);
            gas.SetAffiliation(new List<Tag> { side });
            return gas;
        }

        private static bool On(IGameplayAbilitySystem gas, Tag side) =>
            gas.GetAffiliation()?.Contains(side) == true && gas.GetTagCache().GetWeight(side) > 0;

        private GameplayEffect Turning(string name, float seconds, ESideSource source = ESideSource.Caster, int priority = 0,
            bool interrupts = false, params Tag[] sides)
        {
            var effect = Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = seconds, Target = null });
            effect.SideChange = new SideChange
            {
                Changes = true, Source = source, Sides = new List<Tag>(sides), Priority = priority, Interrupts = interrupts,
            };
            return effect;
        }

        /// <summary>The hubs set an entity's sides, which its tags follow.</summary>
        [UnityTest]
        public IEnumerator TW_18_The_hubs_set_an_entitys_sides([Values] Hub hub) => Live(async () =>
        {
            var gas = Of(hub, "Turncoat", red);
            await Frames.Step();
            Assert.IsTrue(On(gas, red), "red at first");

            gas.SetAffiliation(new List<Tag> { blue });
            await Frames.Step();
            Assert.IsTrue(On(gas, blue), "then blue");
            Assert.IsFalse(gas.GetTagCache().GetWeight(red) > 0, "and red no more");
        });

        /// <summary>A mind control gives its target its caster's sides while it lasts, and its own come back as it ends.</summary>
        [UnityTest]
        public IEnumerator TW_18_A_mind_control_gives_the_casters_sides_while_it_lasts([Values] Hub hub) => Live(async () =>
        {
            var caster = Of(hub, "Caster", red);
            var target = Of(hub, "Target", blue);
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, Turning("Dominate", 0.3f)));
            await Frames.Step();
            Assert.IsTrue(On(target, red), "red while dominated");
            Assert.IsFalse(target.GetTagCache().GetWeight(blue) > 0, "not blue");

            await Frames.Until(() => On(target, blue), 60, "blue again as it ends");
            Assert.IsFalse(target.GetTagCache().GetWeight(red) > 0, "and red no more");
        });

        /// <summary>An effect can give the sides it names, as a pacify makes its target Neutral.</summary>
        [UnityTest]
        public IEnumerator TW_18_An_effect_gives_the_sides_it_names([Values] Hub hub) => Live(async () =>
        {
            var caster = Of(hub, "Caster", red);
            var target = Of(hub, "Target", blue);
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, Turning("Pacify", 30f, ESideSource.Named, 0, false, neutral)));
            await Frames.Step();
            Assert.IsTrue(On(target, neutral), "neutral while pacified");
            Assert.IsFalse(On(target, red) || On(target, blue), "neither red nor blue");
        });

        /// <summary>The highest priority holds, ties to the latest; as one ends, the next holds, and with none, its own.</summary>
        [UnityTest]
        public IEnumerator TW_18_The_highest_priority_holds([Values] Hub hub) => Live(async () =>
        {
            var redCaster = Of(hub, "Red", red);
            var greenCaster = Of(hub, "Green", green);
            var target = Of(hub, "Target", blue);
            await Frames.Step();

            var strong = Turning("Strong", 30f, priority: 2);
            var weak = Turning("Weak", 30f, priority: 1);
            Assert.IsTrue(World.Apply(greenCaster, target, strong));
            Assert.IsTrue(World.Apply(redCaster, target, weak));
            await Frames.Step();
            Assert.IsTrue(On(target, green), "the stronger holds, though the weaker came later");

            var tie = Turning("Tie", 30f, ESideSource.Named, 2, false, neutral);
            Assert.IsTrue(World.Apply(redCaster, target, tie));
            await Frames.Step();
            Assert.IsTrue(On(target, neutral), "a tie goes to the latest");

            target.GetEffectSystem().RemoveGameplayEffect(tie.Tags.AssetTag);
            await Frames.Step(2);
            Assert.IsTrue(On(target, green), "as it ends, the strong one holds again");
            target.GetEffectSystem().RemoveGameplayEffect(strong.Tags.AssetTag);
            await Frames.Step(2);
            Assert.IsTrue(On(target, red), "then the weak one");
            target.GetEffectSystem().RemoveGameplayEffect(weak.Tags.AssetTag);
            await Frames.Step(2);
            Assert.IsTrue(On(target, blue), "and with none, its own");
        });

        /// <summary>An effect that interrupts breaks what its target runs as the side changes; one that doesn't, doesn't.</summary>
        [UnityTest]
        public IEnumerator TW_18_An_effect_that_interrupts_breaks_what_runs([Values] Hub hub) => Live(async () =>
        {
            var caster = Of(hub, "Caster", red);
            var target = Of(hub, "Target", blue);
            var channel = Make.Ability("Channel", Make.Stage(new ProbeAbilityTask(World, "Channel", frames: 600)));
            await Frames.Step();
            Assert.IsTrue(target.GetAbilitySystem().GiveAbility(channel, 1, out _));

            bool Running() => target.GetAbilitySystem().TryGetAbilityContainer(channel, out var container) && container.IsActive;

            Assert.IsTrue(target.GetAbilitySystem().TryActivateAbility(channel, out _));
            await Frames.Step(2);
            Assert.IsTrue(World.Apply(caster, target, Turning("Charm", 30f)));
            await Frames.Step(2);
            Assert.IsTrue(Running(), "a side change that doesn't interrupt leaves the channel running");

            Assert.IsTrue(World.Apply(caster, target, Turning("Dominate", 30f, ESideSource.Named, 5, true, green)));
            await Frames.Step(2);
            Assert.IsFalse(Running(), "one that interrupts breaks it");
        });
    }
}
