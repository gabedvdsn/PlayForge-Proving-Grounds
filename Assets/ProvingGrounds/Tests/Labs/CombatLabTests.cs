using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Combat lab's bot. It plays the lab's rounds on a training dummy, under a live ProcessControl and the lab's clock
    /// of a quarter second a frame, and checks what each leaves, the health bar among it. Phase 5's gate is every lab
    /// green.
    /// </summary>
    [Category("Lab")]
    public class CombatLabTests : ProvingGroundsTest
    {
        // The lab's clock for the round, and the game's back after it
        private sealed class LabTime : IDisposable
        {
            private readonly IForgeClock previous = ForgeTime.Use(new CombatClock());
            public void Dispose() => ForgeTime.Use(previous);
        }

        private static int FramesFor(float seconds) => (int)Math.Round(seconds / CombatLab.SecondsPerFrame);

        // The times, from its application, at which an effect changed the dummy's Health
        private async UniTask<float[]> BitesOf(CombatLab lab, GameplayEffect effect, float over)
        {
            var bites = new List<float>();
            float last = lab.Value(lab.Health);
            Assert.IsTrue(lab.Apply(effect), $"{effect.GetName()} lands");
            if (lab.Value(lab.Health) != last)
            {
                bites.Add(0f);
                last = lab.Value(lab.Health);
            }
            for (int frame = 1; frame <= FramesFor(over); frame++)
            {
                await Frames.Step();
                if (lab.Value(lab.Health) == last) continue;
                bites.Add(frame * CombatLab.SecondsPerFrame);
                last = lab.Value(lab.Health);
            }
            return bites.ToArray();
        }

        /// <summary>
        /// Each delivery lands as EFFECTS.md says. A change while the effect lasts goes with it, and one on applying stays.
        /// Five ticks over ten seconds fall two seconds apart, the last as the effect ends, and Tick On Application adds one
        /// as it lands. An additional impact lowers Armor while the effect lasts, as its first burns Health on each tick.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_Each_delivery_lands_as_it_should([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new CombatLab(World, Make, hub);

            var vigor = lab.Effect("Vigor", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 4f, Target = lab.Health, Magnitude = 50f, Slot = EAttributeTarget.CurrentAndBase,
            }, lab.BonusGroup);
            Assert.IsTrue(lab.Apply(vigor), "Vigor lands");
            Assert.AreEqual(250f, lab.Value(lab.Health), "and adds 50 while it lasts");
            Assert.AreEqual(250f, lab.Max(lab.Health), "to the most the dummy can have too");
            Assert.AreEqual(50f, lab.Bar().Of(lab.BonusGroup), "as the bar's bonus");
            await Frames.Until(() => lab.Stacks(vigor) == 0, FramesFor(4f) + 2, "Vigor ends");
            Assert.AreEqual(200f, lab.Value(lab.Health), "and takes its 50 with it");
            Assert.AreEqual(0f, lab.Bar().Of(lab.BonusGroup), "and the bar its bonus");

            var hit = lab.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = lab.Health, Magnitude = -30f }, lab.DamageGroup);
            Assert.IsTrue(lab.Apply(hit), "a hit lands");
            Assert.AreEqual(170f, lab.Value(lab.Health), "once, and stays");
            Assert.AreEqual(-30f, lab.Bar().Of(lab.DamageGroup), "as the bar's damage");

            var burn = lab.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = lab.Health, Magnitude = -4f, PeriodicTicks = true, Ticks = 5,
            }, lab.DamageGroup);
            Assert.AreEqual(new[] { 2f, 4f, 6f, 8f, 10f }, await BitesOf(lab, burn, 11f), "five ticks over ten seconds, two apart, the last as it ends");
            Assert.AreEqual(150f, lab.Value(lab.Health), "for 4 each");

            var searing = lab.Effect("Searing", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = lab.Health, Magnitude = -4f, PeriodicTicks = true, Ticks = 5,
                TickOnApplication = true,
            }, lab.DamageGroup);
            Assert.AreEqual(new[] { 0f, 2f, 4f, 6f, 8f, 10f }, await BitesOf(lab, searing, 11f), "Tick On Application adds one as it lands");

            var corrode = lab.Effect("Corrode", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 6f, Target = lab.Health, Magnitude = -2f, PeriodicTicks = true, Ticks = 3,
            }, lab.DamageGroup);
            corrode.ImpactSpecification.AdditionalImpacts = new List<AttributeImpactEntry>
            {
                new()
                {
                    AttributeTarget = lab.Armor, Delivery = EImpactDelivery.Continuous,
                    Magnitude = new ScalerMagnitudeOperation { Magnitude = -10f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                },
            };
            float before = lab.Value(lab.Health);
            Assert.IsTrue(lab.Apply(corrode), "Corrode lands");
            Assert.AreEqual(CombatLab.StartingArmor - 10f, lab.Value(lab.Armor), "and lowers Armor while it lasts");
            await Frames.Until(() => lab.Stacks(corrode) == 0, FramesFor(6f) + 2, "Corrode ends");
            Assert.AreEqual(before - 6f, lab.Value(lab.Health), "having burnt 2 on each of its three ticks");
            Assert.AreEqual(CombatLab.StartingArmor, lab.Value(lab.Armor), "and Armor is whole again");
        });

        /// <summary>
        /// Each re-application policy, with what follows it, as EFFECTS.md says, for a second application two seconds into
        /// four. Add Another adds a container, and both apply; the rest keep one. Do Nothing leaves its time alone, Refresh
        /// restarts it and Extend adds the four seconds to it; Stack Existing adds a stack.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_Each_reapplication_policy([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new CombatLab(World, Make, hub);
            var cases = new (EEffectReApplicationPolicy Reapply, EEffectInteractionPolicy Then, int Containers, int Stacks, float Power, float Left)[]
            {
                (EEffectReApplicationPolicy.AppendNewContainer, EEffectInteractionPolicy.DoNothing, 2, 1, 40f, 4f),
                (EEffectReApplicationPolicy.DoNothing, EEffectInteractionPolicy.DoNothing, 1, 1, 20f, 2f),
                (EEffectReApplicationPolicy.DoNothing, EEffectInteractionPolicy.RefreshContainerDuration, 1, 1, 20f, 4f),
                (EEffectReApplicationPolicy.DoNothing, EEffectInteractionPolicy.ExtendContainerDuration, 1, 1, 20f, 6f),
                (EEffectReApplicationPolicy.ReplaceOldContainer, EEffectInteractionPolicy.DoNothing, 1, 1, 20f, 2f),
                (EEffectReApplicationPolicy.ReplaceOldContainer, EEffectInteractionPolicy.RefreshContainerDuration, 1, 1, 20f, 4f),
                (EEffectReApplicationPolicy.ReplaceOldContainer, EEffectInteractionPolicy.ExtendContainerDuration, 1, 1, 20f, 6f),
                (EEffectReApplicationPolicy.StackExistingContainers, EEffectInteractionPolicy.DoNothing, 1, 2, 20f, 2f),
                (EEffectReApplicationPolicy.StackExistingContainers, EEffectInteractionPolicy.RefreshContainerDuration, 1, 2, 20f, 4f),
                (EEffectReApplicationPolicy.StackExistingContainers, EEffectInteractionPolicy.ExtendContainerDuration, 1, 2, 20f, 6f),
            };
            foreach (var (reapply, then, containers, stacks, power, left) in cases)
            {
                string name = $"{reapply} then {then}";
                var surge = lab.Effect(name, new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 4f, Target = lab.Power, Magnitude = 20f, Reapply = reapply, Interaction = then,
                }, lab.BonusGroup);
                Assert.IsTrue(lab.Apply(surge), $"{name}: the first lands");
                await Frames.Step(FramesFor(2f));
                lab.Apply(surge);

                Assert.IsTrue(lab.Dummy.TryGetEffectContainers(surge, out var held), $"{name}: it's on");
                Assert.AreEqual(containers, held.Length, $"{name}: its containers");
                Assert.AreEqual(stacks, lab.Stacks(surge), $"{name}: its stacks");
                Assert.AreEqual(power, lab.Value(lab.Power), $"{name}: what it adds");
                Assert.AreEqual(left, lab.Left(surge), 0.001f, $"{name}: its time left");

                lab.Dummy.RemoveGameplayEffect(surge);
                await Frames.Until(() => lab.Stacks(surge) == 0, 3, $"{name}: it comes off");
                Assert.AreEqual(0f, lab.Value(lab.Power), $"{name}: and takes what it added");
            }
        });

        /// <summary>
        /// Each stacking policy, as EFFECTS.md says, for stacks added a second apart on a four-second effect. Sharing one
        /// duration, a new stack doesn't reset it, and all go together. With durations of their own, they go one at a
        /// time. With the duration taken from one stack, a new stack restarts its timer, and a stack goes each time it runs
        /// out.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_Each_stacking_policy([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new CombatLab(World, Make, hub);
            var cases = new (EEffectStackPolicy Policy, (float At, int Stacks)[] Expected)[]
            {
                (EEffectStackPolicy.StacksShareOneDuration, new[] { (3f, 3), (4.25f, 0) }),
                (EEffectStackPolicy.StacksHaveIndependentDurations, new[] { (3f, 3), (4.25f, 2), (5.25f, 1), (6.25f, 0) }),
                (EEffectStackPolicy.DurationTakenFromOneStack, new[] { (3f, 3), (5.75f, 3), (6.25f, 2), (10.25f, 1), (14.25f, 0) }),
            };
            foreach (var (policy, expected) in cases)
            {
                var bleed = lab.Effect($"Bleed {policy}", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 4f, Target = null,
                    Reapply = EEffectReApplicationPolicy.StackExistingContainers, StackPolicy = policy,
                }, lab.DamageGroup);
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(lab.Apply(bleed), $"{policy}: a stack lands");
                    if (i < 2) await Frames.Step(FramesFor(1f));
                }

                float now = 2f;
                foreach (var (at, stacks) in expected)
                {
                    await Frames.Step(FramesFor(at - now));
                    now = at;
                    Assert.AreEqual(stacks, lab.Stacks(bleed), $"{policy}: {stacks} stacks at {at} s");
                }
            }
        });

        /// <summary>
        /// A stack count feeds a change (FEAT-19). A sunder of −5 Armor a stack, read by the Effect Stacks scaler, takes 15
        /// at three stacks, and gives it back a stack at a time as each runs out. A shared-duration bleed's tick executes
        /// once a stack.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_stack_count_feeds_a_change([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new CombatLab(World, Make, hub);
            var sunder = lab.Effect("Sunder", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 4f, Target = lab.Armor, Magnitude = -5f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = new EffectStacksScaler(),
                Reapply = EEffectReApplicationPolicy.StackExistingContainers, StackPolicy = EEffectStackPolicy.StacksHaveIndependentDurations,
            }, lab.DamageGroup);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(lab.Apply(sunder), "a sunder lands");
                if (i < 2) await Frames.Step(FramesFor(1f));
            }
            Assert.AreEqual(CombatLab.StartingArmor - 15f, lab.Value(lab.Armor), "three stacks take 15");
            await Frames.Step(FramesFor(2.25f));
            Assert.AreEqual(CombatLab.StartingArmor - 10f, lab.Value(lab.Armor), "the first runs out and gives its 5 back");
            await Frames.Step(FramesFor(1f));
            Assert.AreEqual(CombatLab.StartingArmor - 5f, lab.Value(lab.Armor), "then the second");
            await Frames.Step(FramesFor(1f));
            Assert.AreEqual(CombatLab.StartingArmor, lab.Value(lab.Armor), "and the last");

            var bleed = lab.Effect("Bleed", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 4f, Target = lab.Health, Magnitude = -1f, PeriodicTicks = true, Ticks = 4,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers, StackPolicy = EEffectStackPolicy.StacksShareOneDuration,
            }, lab.DamageGroup);
            for (int i = 0; i < 3; i++) Assert.IsTrue(lab.Apply(bleed), "a bleed lands");
            Assert.AreEqual(3, lab.Stacks(bleed), "three stacked");
            await Frames.Until(() => lab.Stacks(bleed) == 0, FramesFor(4f) + 2, "the bleed runs its course");
            Assert.AreEqual(CombatLab.StartingHealth - 4 * 3, lab.Value(lab.Health), "each of its four ticks executes once a stack");
        });

        /// <summary>
        /// A bout: Vigor, a hit a second and two burns on the dummy over twelve seconds. On every frame the bar adds up to the
        /// dummy's Health, and its most to the Health's Base. As it ends, its segments are the dummy's own, no bonus, and
        /// every point of damage it took.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_bout([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new CombatLab(World, Make, hub);
            var vigor = lab.Effect("Vigor", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 6f, Target = lab.Health, Magnitude = 50f, Slot = EAttributeTarget.CurrentAndBase,
            }, lab.BonusGroup);
            var hit = lab.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = lab.Health, Magnitude = -12f }, lab.DamageGroup);
            var burn = lab.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 4f, Target = lab.Health, Magnitude = -2f, PeriodicTicks = true, Ticks = 4,
            }, lab.DamageGroup);

            for (int frame = 0; frame < FramesFor(12f); frame++)
            {
                float now = frame * CombatLab.SecondsPerFrame;
                if (now == 0f) Assert.IsTrue(lab.Apply(vigor), "Vigor lands");
                if (now % 1f == 0f) Assert.IsTrue(lab.Apply(hit), $"a hit at {now} s");
                if (now == 1f || now == 5f) Assert.IsTrue(lab.Apply(burn), $"a burn at {now} s");
                await Frames.Step();

                var bar = lab.Bar();
                Assert.AreEqual(lab.Value(lab.Health), bar.Current, 0.001f, $"at {now} s the bar adds up to the Health");
                Assert.AreEqual(lab.Max(lab.Health), bar.Max, 0.001f, "and its most to the Health's Base");
            }

            var end = lab.Bar();
            Assert.AreEqual(CombatLab.StartingHealth, end.Of(lab.BaseGroup), "as the bout ends, the bar has the dummy's own");
            Assert.AreEqual(0f, end.Of(lab.BonusGroup), "no bonus, as Vigor is over");
            Assert.AreEqual(-(12 * 12f + 2 * 4 * 2f), end.Of(lab.DamageGroup), "and every point of damage it took");
            Assert.AreEqual(CombatLab.StartingHealth - 160f, lab.Value(lab.Health));
        });
    }
}
