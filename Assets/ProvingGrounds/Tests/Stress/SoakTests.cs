using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The stress suite's soaks (the review's Module 3): the same work again and again, for what each time leaves behind.
    /// Half an hour of bullet heaven is Explicit, as it runs for minutes: run it with -testCategory Soak.
    /// </summary>
    [Category("Stress")]
    public class SoakTests : ProvingGroundsTest
    {
        private const int Casters = 100, RunsEach = 100, Loads = 100, LevelSize = 10;

        /// <summary>
        /// 100 casters each run an ability 100 times, one run after another: 10,000 runs of its sequence. Each run counts
        /// once, ProcessControl ends with the processes it began with, and the last thousand runs take about as long as the
        /// second thousand did.
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_An_abilitys_sequence_run_10000_times_leaves_nothing_behind([Values] Hub hub) => Live(async () =>
        {
            var runs = Make.Attribute("Runs");
            var counted = Make.Effect("Counted", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = runs, Op = ECalculationOperation.Add, Magnitude = 1f,
            });
            var set = Make.Set(Make.Element(runs, 0f, 0f));

            var casters = new List<(IGameplayAbilitySystem gas, AbilitySystemComponent abilities, int index)>();
            for (int i = 0; i < Casters; i++)
            {
                // Two stages: the run counts itself on its caster, then holds a frame. Each caster's own, as the ledger
                // pairs a task's preparing and cleaning by its name
                var pulse = Make.Ability($"Pulse {i}",
                    Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { counted } }),
                    Make.Stage(new ProbeAbilityTask(World, $"Pulse Held {i}", frames: 1)));
                var gas = World.Entity(hub, set, $"Caster {i}", observed: false);
                var abilities = gas.GetAbilitySystem();
                Assert.IsTrue(abilities.GiveAbility(pulse, 1, out int index), "a caster knows Pulse");
                casters.Add((gas, abilities, index));
            }
            await Frames.Step(2);
            int before = World.Control.Active;

            const int all = Casters * RunsEach;
            int Done() => casters.Sum(caster => (int)ValueOf(caster.gas, runs));
            var watch = Stopwatch.StartNew();
            double secondStart = -1, secondEnd = -1, lastStart = -1;
            for (int frame = 0; Done() < all; frame++)
            {
                int done = Done();
                if (secondStart < 0 && done >= 1000) secondStart = watch.Elapsed.TotalMilliseconds;
                if (secondEnd < 0 && done >= 2000) secondEnd = watch.Elapsed.TotalMilliseconds;
                if (lastStart < 0 && done >= all - 1000) lastStart = watch.Elapsed.TotalMilliseconds;

                foreach (var caster in casters)
                {
                    if (ValueOf(caster.gas, runs) < RunsEach && !caster.abilities.IsExecuting)
                        caster.abilities.TryActivateAbility(caster.abilities.CreateActivationRequest(caster.index));
                }
                await Frames.Step();
                if (frame > RunsEach * 20) Assert.Fail($"only {Done()} of {all} runs in {frame} frames");
            }
            double last = watch.Elapsed.TotalMilliseconds - lastStart, second = secondEnd - secondStart;
            await Frames.Step(3);

            TestContext.WriteLine($"ms a thousand runs, the second thousand → the last: {second:F1} → {last:F1}");
            Assert.That(casters.All(caster => ValueOf(caster.gas, runs) == RunsEach), "each caster ran 100 times, each run counted once");
            Assert.AreEqual(all, Enumerable.Range(0, Casters).Sum(i => Ledger.Cleans($"Pulse Held {i}")), "and each run's last stage was cleaned up");
            Assert.AreEqual(before, World.Control.Active, "ProcessControl ends with the processes it began with");
            Assert.That(last, Is.LessThan(3 * second), "the last thousand runs took about as long as the second thousand");
        });

        /// <summary>
        /// A level of 10 entities, each burning and casting a long ability, loaded over 100 times as a game changes levels:
        /// each load takes the level's entities and every process of theirs with it, so ProcessControl ends each load with
        /// the processes it began with, and each cast's task was cleaned up as its entity went.
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_100_scene_loads_leave_nothing_behind() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 1e6f, 1e6f));
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Target = health, Op = ECalculationOperation.Add, Magnitude = -1f,
                Duration = 60f, PeriodicTicks = true, Ticks = 240,
            });
            // One channel for each place in a level, as the ledger pairs a task's preparing and cleaning by its name
            var channels = Enumerable.Range(0, LevelSize)
                .Select(i => Make.Ability($"Channel {i}", Make.Stage(new ProbeAbilityTask(World, $"Channelling {i}", frames: 600))))
                .ToList();
            await Frames.Step();
            int before = World.Control.Active;

            for (int load = 0; load < Loads; load++)
            {
                for (int i = 0; i < LevelSize; i++)
                {
                    var gas = World.MonoEntity(set, $"Level {load} entity {i}", observed: false);
                    Assert.IsTrue(World.Apply(gas, gas, burn), "the entity burns");
                    var abilities = gas.GetAbilitySystem();
                    Assert.IsTrue(abilities.GiveAbility(channels[i], 1, out int index), "it knows Channel");
                    Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "and channels");
                }
                await Frames.Step(3);
                Assert.That(World.Control.Active, Is.GreaterThan(before), $"load {load}: the level runs");

                await World.ChangeScene();
                await Frames.Step(3);
                Assert.AreEqual(before, World.Control.Active, $"load {load}: the level's processes went with it");
            }

            Assert.AreEqual(Loads * LevelSize, Enumerable.Range(0, LevelSize).Sum(i => Ledger.Cleans($"Channelling {i}")),
                "each cast's task was cleaned up as its entity went");
        });

        /// <summary>
        /// Half an hour of bullet heaven by the lab's clock: the wizard fires 20 bolts a frame into a horde of 500 too
        /// hardy to fall, and its aura burns them twice a second. Every bolt lands and goes back to the pool, which makes only
        /// as many as fly at once; ProcessControl ends with the processes it began with; the managed heap ends about where
        /// it was two minutes in, once warming up has filled what it fills; and a frame of the last minute costs about what one
        /// of the second did.
        /// </summary>
        [UnityTest, Explicit("Half an hour of the lab's time, minutes of a machine's: run it with -testCategory Soak"), Category("Soak")]
        [Timeout(30 * 60 * 1000)]
        public IEnumerator Soak_Half_an_hour_of_bullet_heaven() => Live(async () =>
        {
            const int minute = 64 * 60, minutes = 30, boltsAFrame = 20;
            var previous = ForgeTime.Use(new HeavenClock());
            var lab = new BulletHeavenLab(World, Make);
            try
            {
                lab.Horde(25, 20, new Vector3(4f, 0f, -10f), health: 1e9f);
                await Frames.Step();
                int before = World.Control.Active;
                GC.Collect();
                long heapBefore = Profiler.GetMonoUsedSizeLong();

                double second = 0, last = 0;
                long heapWarm = 0;
                var heaps = new List<string>();
                var watch = Stopwatch.StartNew();
                for (int m = 0; m < minutes; m++)
                {
                    using var timer = new StressMeasure.StepTimer(minute);
                    for (int frame = 0; frame < minute; frame++)
                    {
                        for (int i = 0; i < boltsAFrame; i++)
                        {
                            // Spread over the horde, the product kept in a long: two million bolts in, an int's would overflow
                            var at = lab.Enemies[(int)((long)lab.Fired * 7919 % lab.Enemies.Count)];
                            if (!lab.Fire(at, own: false)) Assert.Fail($"bolt {lab.Fired} was refused");
                        }
                        if (frame % 32 == 0) lab.Pulse(6f);
                        await Frames.Step();
                    }
                    if (m == 1) second = timer.PerFrame;
                    if (m == minutes - 1) last = timer.PerFrame;

                    // The heap now and then: two minutes in, past what warming up fills, and on, so a leak shows as a climb
                    if (m + 1 is 2 or 10 or 20 or minutes)
                    {
                        GC.Collect();
                        long heap = Profiler.GetMonoUsedSizeLong();
                        if (m + 1 == 2) heapWarm = heap;
                        heaps.Add($"{m + 1}: {heap / 1024} KB");
                    }

                    // Where it's got to, in the editor's log, as the run lasts minutes
                    UnityEngine.Debug.Log($"[Soak] minute {m + 1} of {minutes}: {lab.Fired} bolts, {watch.Elapsed.TotalSeconds:F0} s in, " +
                                          $"{timer.PerFrame / 1000:F1} µs a frame in the step loops");
                }
                await Frames.Until(() => lab.Pool.Out == 0, 200, "every bolt lands");
                await Frames.Step(2);
                watch.Stop();
                GC.Collect();
                long heapAfter = Profiler.GetMonoUsedSizeLong();

                TestContext.WriteLine($"{lab.Fired} bolts over {minutes} minutes of the lab's time in {watch.Elapsed.TotalMinutes:F1} minutes; " +
                                      $"the pool made {lab.Pool.Made}; µs a frame, the second minute → the last: {second / 1000:F1} → {last / 1000:F1}; " +
                                      $"heap {heapBefore / 1024} KB at the start, by minute {string.Join(", ", heaps)}, {heapAfter / 1024} KB at the end");
                Assert.AreEqual(minute * minutes * boltsAFrame, lab.Fired, "every bolt was fired");
                Assert.That(lab.Pool.Made, Is.LessThan(1000), "the pool made only as many as flew at once");
                Assert.AreEqual(lab.Pool.Made, lab.Pool.Idle, "and every bolt is back in it");
                Assert.AreEqual(before, World.Control.Active, "ProcessControl ends with the processes it began with");
                Assert.IsEmpty(lab.Deaths, "and the horde stood");
                Assert.That(heapAfter - heapBefore, Is.LessThan(8L * 1024 * 1024), "the heap ends within 8 MB of where it began: 4 bytes a bolt kept would be more");
                Assert.That(heapAfter - heapWarm, Is.LessThan(2L * 1024 * 1024), "and within 2 MB of where it was two minutes in: a byte a bolt kept since would be more");
                Assert.That(last, Is.LessThan(1.5 * second), "a frame of the last minute costs about what one of the second did");
            }
            finally
            {
                lab.Pool.DestroyAll();
                ForgeTime.Use(previous);
            }
        });

        private static float ValueOf(IGameplayAbilitySystem gas, ForgeAttribute attribute)
        {
            gas.TryGetAttributeValue(attribute, out var value);
            return value.CurrentValue;
        }
    }
}
