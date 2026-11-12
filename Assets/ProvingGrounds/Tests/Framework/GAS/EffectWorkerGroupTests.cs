using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-13: rules that come with an effect. An effect's own worker group goes to the one it's on as it lands and comes
    /// back as it goes, as Ice Blast's debuff shatters its target below 13% only while it lasts; two of it give the group
    /// once, and the last to go takes it back.
    /// </summary>
    public class EffectWorkerGroupTests : ProvingGroundsTest
    {
        private ForgeAttribute health, shatters;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            shatters = Make.Attribute("Shatters");
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) => World.Entity(hub, Make.Set(
            Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
            Make.Element(shatters, 0f, 999f)), name);

        private GameplayEffect Change(float amount) =>
            Make.Effect(amount < 0f ? "Blow" : "Heal", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = amount });

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // Ice Blast's debuff: while it lasts, its target shatters each time its Health falls below 13%
        private GameplayEffect IceBlast()
        {
            var debuff = Make.Effect("Ice Blast", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            debuff.WorkerGroup = new StandardWorkerGroup();
            debuff.WorkerGroup.AnalysisWorkers.Add(new ThresholdAnalysisWorker
            {
                Target = health, What = EAttributeTargetBinary.Share, Threshold = 0.13f, Crossing = EThresholdCrossing.Falling,
                TriggerOnce = false,
                Effects = new List<GameplayEffect>
                {
                    Make.Effect("Shatter", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = shatters, Magnitude = 1f }),
                },
            });
            return debuff;
        }

        private async Cysharp.Threading.Tasks.UniTask Crossing(IGameplayAbilitySystem from, IGameplayAbilitySystem target)
        {
            // Down past 13% and back up, a crossing each way
            Assert.IsTrue(World.Apply(from, target, Change(-90f)));
            await Frames.Step(2);
            Assert.IsTrue(World.Apply(from, target, Change(90f)));
            await Frames.Step(2);
        }

        /// <summary>Its group watches the one it's on while it lasts, and not after.</summary>
        [UnityTest]
        public IEnumerator TW_13_An_effects_worker_group_holds_while_it_lasts([Values] Hub hub) => Live(async () =>
        {
            var target = Fighter(hub, "Target");
            var caster = Fighter(hub, "Caster");
            var iceBlast = IceBlast();
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, iceBlast), "Ice Blast's debuff lands");
            await Frames.Step();
            await Crossing(caster, target);
            Assert.AreEqual(1f, Value(target, shatters), "while it lasts, falling below 13% shatters");

            Assert.IsTrue(target.GetEffectSystem().RemoveGameplayEffect(iceBlast), "the debuff ends");
            await Frames.Step(2);
            await Crossing(caster, target);
            Assert.AreEqual(1f, Value(target, shatters), "and after, its group is gone with it");
        });

        /// <summary>Two of an effect give its group once, and the last of them to go takes it back.</summary>
        [UnityTest]
        public IEnumerator TW_13_Two_of_an_effect_give_its_group_once([Values] Hub hub) => Live(async () =>
        {
            var target = Fighter(hub, "Target");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var iceBlast = IceBlast();
            await Frames.Step();

            var firstOrigin = IEffectOrigin.GenerateSourceDerivation(first);
            var secondOrigin = IEffectOrigin.GenerateSourceDerivation(second);
            Assert.IsTrue(target.ApplyGameplayEffect(target.GenerateEffectSpec(firstOrigin, iceBlast)), "one Ice Blast");
            Assert.IsTrue(target.ApplyGameplayEffect(target.GenerateEffectSpec(secondOrigin, iceBlast)), "and another");
            await Frames.Step();
            await Crossing(first, target);
            Assert.AreEqual(1f, Value(target, shatters), "a crossing shatters once, though two of it last");

            Assert.IsTrue(target.GetEffectSystem().RemoveGameplayEffect(iceBlast, firstOrigin), "the first ends");
            await Frames.Step(2);
            await Crossing(first, target);
            Assert.AreEqual(2f, Value(target, shatters), "the other still lasts, and its group with it");

            Assert.IsTrue(target.GetEffectSystem().RemoveGameplayEffect(iceBlast, secondOrigin), "the last ends");
            await Frames.Step(2);
            await Crossing(first, target);
            Assert.AreEqual(2f, Value(target, shatters), "and takes the group back");
        });
    }
}
