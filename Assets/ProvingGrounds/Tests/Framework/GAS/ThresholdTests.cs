using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-11, Decision 51: thresholds that act. A Threshold reads a part of its attribute, as its share of its base, and at
    /// the frame's end fires as that crosses its threshold, falling or rising, once or each time: it applies its effects to
    /// the entity, from the entity or from the one whose hit crossed it, and sends its event, as Megaera rages at half her
    /// Health and Ice Blast shatters below 13%.
    /// </summary>
    public class ThresholdTests : ProvingGroundsTest
    {
        private ForgeAttribute health, rages;
        private Tag enraged, shattered;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            rages = Make.Attribute("Rages");
            enraged = Make.NewTag("Test.TW11.Enraged");
            shattered = Make.NewTag("Test.TW11.Shattered");
        }

        // A fighter whose set watches its health with the thresholds given
        private IGameplayAbilitySystem Fighter(Hub hub, string name, params ThresholdAnalysisWorker[] thresholds)
        {
            var set = Make.Set(
                Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(rages, 0f, 999f));
            foreach (var threshold in thresholds) set.WorkerGroup.AnalysisWorkers.Add(threshold);
            return World.Entity(hub, set, name);
        }

        private GameplayEffect Change(float amount) =>
            Make.Effect(amount < 0f ? "Blow" : "Heal", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = amount });

        private GameplayEffect Rage() =>
            Make.Effect("Rage", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = rages, Magnitude = 1f });

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private ThresholdAnalysisWorker AtShare(float share, EThresholdCrossing crossing, bool once, params GameplayEffect[] effects) => new()
        {
            Target = health, What = EAttributeTargetBinary.Share, Threshold = share, Crossing = crossing, TriggerOnce = once,
            Effects = new List<GameplayEffect>(effects),
        };

        private async Cysharp.Threading.Tasks.UniTask Hit(IGameplayAbilitySystem from, IGameplayAbilitySystem on, float amount)
        {
            Assert.IsTrue(World.Apply(from, on, Change(amount)), "the change lands");
            await Frames.Step(2);
        }

        /// <summary>It fires as its share falls past half, each time, and only as it crosses.</summary>
        [UnityTest]
        public IEnumerator TW_11_A_threshold_fires_each_time_its_share_falls_past_it([Values] Hub hub) => Live(async () =>
        {
            var megaera = Fighter(hub, "Megaera", AtShare(0.5f, EThresholdCrossing.Falling, once: false, Rage()));
            var hero = Fighter(hub, "Hero");
            await Frames.Step();

            await Hit(hero, megaera, -40f);
            Assert.AreEqual(0f, Value(megaera, rages), "at 60%, above half, nothing");
            await Hit(hero, megaera, -20f);
            Assert.AreEqual(1f, Value(megaera, rages), "falling past half, she rages");
            await Hit(hero, megaera, -10f);
            Assert.AreEqual(1f, Value(megaera, rages), "further below, no more: it fires as it crosses");
            await Hit(hero, megaera, 40f);
            Assert.AreEqual(1f, Value(megaera, rages), "rising back above it, nothing, as it fires falling");
            await Hit(hero, megaera, -30f);
            Assert.AreEqual(2f, Value(megaera, rages), "and falling past it again, she rages again");
        });

        /// <summary>One that fires once fires at its first crossing alone; one rising fires as its share climbs above it.</summary>
        [UnityTest]
        public IEnumerator TW_11_A_threshold_fires_once_or_rising_as_it_says([Values] Hub hub) => Live(async () =>
        {
            var once = Fighter(hub, "Once", AtShare(0.5f, EThresholdCrossing.Falling, once: true, Rage()));
            var rising = Fighter(hub, "Rising", AtShare(0.5f, EThresholdCrossing.Rising, once: false, Rage()));
            var hero = Fighter(hub, "Hero");
            await Frames.Step();

            await Hit(hero, once, -60f);
            await Hit(hero, once, 40f);
            await Hit(hero, once, -40f);
            Assert.AreEqual(1f, Value(once, rages), "once: its first crossing alone");

            await Hit(hero, rising, -80f);
            Assert.AreEqual(0f, Value(rising, rages), "falling past it, one that fires rising doesn't");
            await Hit(hero, rising, 40f);
            Assert.AreEqual(1f, Value(rising, rages), "and rising above it, it does: a second wind");
        });

        /// <summary>Its effects come from the entity itself, or, where it says, from the one whose hit crossed it.</summary>
        [UnityTest]
        public IEnumerator TW_11_A_thresholds_effects_come_from_whom_it_says([Values] Hub hub) => Live(async () =>
        {
            GameplayEffect Shatter() => Make.Effect("Shatter", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 5f, GrantedTags = new List<Tag> { shattered },
            });
            var ownShatter = Shatter();
            var castersShatter = Shatter();
            var itself = AtShare(0.13f, EThresholdCrossing.Falling, once: true, ownShatter);
            var crosser = AtShare(0.13f, EThresholdCrossing.Falling, once: true, castersShatter);
            crosser.AppliedBy = EThresholdSource.Crosser;
            var first = Fighter(hub, "First", itself);
            var second = Fighter(hub, "Second", crosser);
            var caster = Fighter(hub, "Caster");
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, first, Change(-90f)));
            Assert.IsTrue(World.Apply(caster, second, Change(-90f)));
            await Frames.Step(2);
            Assert.IsTrue(first.TryGetEffectContainer(ownShatter, out var own), "below 13%, it shatters");
            Assert.AreSame(first, own.Spec.Origin.GetSource(), "from itself, as it says by default");
            Assert.IsTrue(second.TryGetEffectContainer(castersShatter, out var theirs), "the other shatters too");
            Assert.AreSame(caster, theirs.Spec.Origin.GetSource(), "from the one whose hit crossed it");
        });

        /// <summary>It sends its event to the entity as it fires, its source the one whose change crossed it.</summary>
        [UnityTest]
        public IEnumerator TW_11_A_threshold_sends_its_event([Values] Hub hub) => Live(async () =>
        {
            var watching = AtShare(0.5f, EThresholdCrossing.Falling, once: false);
            watching.Event = enraged;
            var megaera = Fighter(hub, "Megaera", watching);
            var hero = Fighter(hub, "Hero");
            var fury = Make.Ability("Fury", Make.Stage(new ProbeAbilityTask(World, "Fury")));
            fury.Definition.Triggers = new List<AbilityTrigger> { new() { Source = EAbilityTriggerSource.GameplayEvent, Tag = enraged } };
            await Frames.Step();
            Assert.That(megaera.GetAbilitySystem().GiveAbility(fury, 1, out _), "her fury's known");
            int heard = 0;
            ITarget source = null;
            megaera.GetAbilitySystem().Callbacks.OnGameplayEvent += (tag, data) =>
            {
                if (!tag.Equals(enraged)) return;
                heard++;
                data?.TryGetFirst(Tags.SOURCE, out source);
            };

            await Hit(hero, megaera, -60f);
            await Frames.Step(3);
            Assert.AreEqual(1, heard, "the event, once, as it crosses");
            Assert.AreSame(hero, source, "its source the one whose hit crossed it");
            Assert.AreEqual(1, Ledger.Cleans("Fury"), "and her fury, which listens for it, runs");
        });
    }
}
