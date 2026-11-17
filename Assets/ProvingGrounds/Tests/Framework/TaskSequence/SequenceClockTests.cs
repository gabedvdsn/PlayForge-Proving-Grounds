using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A sequence's own clock (EP-32, the user's question of 2026-11-17): a sequence ran by the game's clock unless an
    /// ability's packet ran it, by its caster's, so one that wasn't an ability's couldn't run in unscaled time, at a pace of
    /// its own or on an entity's time. A run goes by its packet's clock, its sequence's, its packet's owner's or the game's,
    /// in that order: its waits, delays, timeouts and elapsed time, its tasks' tweens and its synced tasks, and, under the
    /// fixed tick, an unscaled run's waits go by Unity's frames.
    /// </summary>
    public class SequenceClockTests : ProvingGroundsTest
    {
        // The game's clock for a test, put in as it's made and the one before put back as it ends: each frame a step of
        // scaled time and one of unscaled, set apart, with Unity's frame
        private sealed class GameClock : IForgeClock, IDisposable
        {
            private readonly IForgeClock previous;
            public float Step, UnscaledStep;

            public GameClock(float step, float unscaledStep)
            {
                Step = step;
                UnscaledStep = unscaledStep;
                previous = ForgeTime.Use(this);
            }

            public void Dispose() => ForgeTime.Use(previous);

            public float DeltaTime => Step;
            public float UnscaledDeltaTime => UnscaledStep;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        /// <summary>
        /// A packet that names a clock runs its sequence by it: a delay of half a second takes four frames of the game's
        /// eighth of a second, and two of a clock twice as fast.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_32_A_sequence_runs_by_the_clock_its_packet_names() => Live(async () =>
        {
            using var game = new GameClock(0.125f, 0.125f);
            Assert.That(await HalfSecond(TaskSequenceBuilder.Create("Game's")), Is.InRange(4, 5),
                "by the game's eighth of a second a frame, four frames");

            var data = SequenceDataPacket.Default();
            data.Clock = new ScaledClock(2f);
            Assert.That(await HalfSecond(TaskSequenceBuilder.Create("Twice"), data), Is.InRange(2, 3),
                "by a clock twice the game's, two");
        });

        /// <summary>A sequence runs by its own clock, WithClock's, unless its packet names one, which comes first.</summary>
        [UnityTest]
        public IEnumerator EP_32_A_sequence_runs_by_its_own_clock_unless_its_packet_names_one() => Live(async () =>
        {
            using var game = new GameClock(0.125f, 0.125f);
            TaskSequenceBuilder Twice() => TaskSequenceBuilder.Create("Twice").WithClock(new ScaledClock(2f));
            Assert.That(await HalfSecond(Twice()), Is.InRange(2, 3), "its own clock, twice the game's: two frames");

            var data = SequenceDataPacket.Default();
            data.Clock = new ScaledClock(0.5f);
            Assert.That(await HalfSecond(Twice(), data), Is.InRange(8, 9), "its packet's, half the game's, before its own: eight");
        });

        /// <summary>
        /// An entity's clock is a clock, so a sequence that isn't an ability's runs on an entity's time, slowed with it.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_32_A_sequence_runs_on_an_entitys_time([Values] Hub hub) => Live(async () =>
        {
            using var game = new GameClock(0.125f, 0.125f);
            var gas = World.Entity(hub);
            IForgeClock clock = gas.GetClock();
            gas.GetClock().TimeScale = 0.5f;
            Assert.AreEqual(0.0625f, clock.DeltaTime, 1e-6f, "the entity's step, half the game's");
            Assert.AreEqual(0.125f, clock.UnscaledDeltaTime, 1e-6f, "and unscaled, the game's");

            var data = SequenceDataPacket.Default();
            data.Clock = clock;
            Assert.That(await HalfSecond(TaskSequenceBuilder.Create("Slowed"), data), Is.InRange(8, 9),
                "on the entity's time, half the game's: eight frames");
        });

        /// <summary>A sequence on the unscaled clock runs while the game's time stands, as a pause menu's does.</summary>
        [UnityTest]
        public IEnumerator EP_32_The_unscaled_clock_runs_while_the_games_time_stands() => Live(async () =>
        {
            using var game = new GameClock(0f, 0.125f);
            bool menu = false, played = false;
            Run(new TaskSequence(TaskSequenceBuilder.Create("Menu").WithClock(UnscaledClock.Instance)
                .Delay(0.5f).Do(_ => menu = true).Build()));
            Run(new TaskSequence(TaskSequenceBuilder.Create("Game's").Delay(0.5f).Do(_ => played = true).Build()));

            int taken = await Frames.Until(() => menu, 10, "the menu's half second ends");
            Assert.That(taken, Is.InRange(4, 5), "by the game's unscaled eighth of a second a frame");
            Assert.IsFalse(played, "while the game's stands with the game's time");
        });

        /// <summary>
        /// Under the fixed tick a sequence waits for the next tick, and the ticks stand with the game's time, so an unscaled
        /// sequence's waits go by Unity's frames: it ends as the game's time stands, and the game's sequence holds.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_32_Under_the_fixed_tick_an_unscaled_sequence_goes_by_frames() => Live(async () =>
        {
            int rateBefore = World.Control.FixedTickRate;
            World.Control.FixedTickRate = 60;
            try
            {
                using var game = new GameClock(1f / 20f, 0.125f);
                bool menu = false, played = false;
                Run(new TaskSequence(TaskSequenceBuilder.Create("Menu").WithClock(UnscaledClock.Instance)
                    .Delay(0.5f).Do(_ => menu = true).Build()));
                Run(new TaskSequence(TaskSequenceBuilder.Create("Game's").Delay(0.5f).Do(_ => played = true).Build()));

                // A frame of the game's time, three ticks, and then it stands
                await Frames.Step();
                game.Step = 0f;
                int taken = await Frames.Until(() => menu, 10, "the menu's half second ends with the game's time standing");
                Assert.That(taken, Is.InRange(3, 4), "an unscaled eighth of a second a frame, from the first");
                await Frames.Holds(6, () => !played, "the game's sequence stands with the ticks");
            }
            finally
            {
                World.Control.FixedTickRate = rateBefore;
            }
        });

        /// <summary>
        /// A sequence run inside another on the same packet goes by its own clock, and the outer by its own beside it: the
        /// packet's runtime is the inner's while it runs, so a task that asked the packet read the inner's for both.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_32_A_nested_sequence_keeps_its_own_clock() => Live(async () =>
        {
            using var game = new GameClock(0.125f, 0.125f);
            var innerSteps = new List<float>();
            var outerSteps = new List<float>();
            var inner = new TaskSequence(TaskSequenceBuilder.Create("Inner").WithClock(new ScaledClock(3f))
                .Stage(stage => stage.Task((_, token) => Steps(innerSteps, token)))
                .Build());
            bool done = false;
            Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage => stage
                    .Task((data, token) => inner.Run(data, token))
                    .Task((_, token) => Steps(outerSteps, token)))
                .Do(_ => done = true)
                .Build()));

            await Frames.Until(() => done, 15, "both end");
            Assert.That(innerSteps, Has.Count.EqualTo(3).And.All.EqualTo(0.375f).Within(1e-5f), "the inner by its own clock, three times the game's");
            Assert.That(outerSteps, Has.Count.EqualTo(3).And.All.EqualTo(0.125f).Within(1e-5f), "the outer by the game's, beside it");
        });

        /// <summary>A synced task in an async sequence steps by its run's clock.</summary>
        [UnityTest]
        public IEnumerator EP_32_A_synced_task_steps_by_its_runs_clock() => Live(async () =>
        {
            using var game = new GameClock(0.125f, 0.125f);
            var steps = new List<float>();
            Run(new TaskSequence(TaskSequenceBuilder.Create("Synced In Async").WithClock(new ScaledClock(2f))
                .Stage(stage => stage.SyncTask((_, deltaTime) =>
                {
                    steps.Add(deltaTime);
                    return steps.Count >= 3;
                }))
                .Build()));

            await Frames.Until(() => steps.Count >= 3, 10, "three steps");
            Assert.That(steps, Has.All.EqualTo(0.25f).Within(1e-5f), "each a step of its clock, twice the game's");
        });

        /// <summary>A synced sequence run as a process steps by its clock, as its runner takes it from its sequence.</summary>
        [UnityTest]
        public IEnumerator EP_32_A_synced_sequence_run_as_a_process_steps_by_its_clock() => Live(async () =>
        {
            using var game = new GameClock(0.125f, 0.125f);
            var steps = new List<float>();
            Run(new TaskSequence(TaskSequenceBuilder.Create("Synced")
                .WithLifecycle(EProcessLifecycle.Synchronous, EProcessStepTiming.Update)
                .WithClock(new ScaledClock(2f))
                .Stage(stage => stage.SyncTask((_, deltaTime) =>
                {
                    steps.Add(deltaTime);
                    return steps.Count >= 3;
                }))
                .Build()));

            await Frames.Until(() => steps.Count >= 3, 10, "three steps");
            Assert.That(steps, Has.All.EqualTo(0.25f).Within(1e-5f), "each a step of its clock, twice the game's");
        });

        /// <summary>A run's own timers go by its clock: a stage's timeout, and its elapsed time.</summary>
        [UnityTest]
        public IEnumerator EP_32_A_runs_timeouts_and_elapsed_time_go_by_its_clock() => Live(async () =>
        {
            using var game = new GameClock(0.125f, 0.125f);
            bool reached = false;
            float elapsed = 0f;
            Run(new TaskSequence(TaskSequenceBuilder.Create("Timed").WithClock(new ScaledClock(2f))
                .Stage(stage => stage.WithTimeout(0.5f).Task((_, token) => SequenceTaskLibrary.WaitUntil(() => false, token)))
                .Do(data =>
                {
                    reached = true;
                    elapsed = data.ElapsedTime;
                })
                .Build()));

            int taken = await Frames.Until(() => reached, 10, "the stage's timeout skips it");
            Assert.That(taken, Is.InRange(2, 3), "half a second of its clock, twice the game's: two frames");
            Assert.That(elapsed, Is.InRange(0.49f, 0.76f), "and its elapsed time counts by its clock too");
        });

        // Records three steps of a task's time, a frame apart
        private static async UniTask Steps(List<float> steps, System.Threading.CancellationToken token)
        {
            for (int i = 0; i < 3; i++)
            {
                await SequenceTaskLibrary.Yield(token);
                steps.Add(SequenceTaskLibrary.DeltaTime(token));
            }
        }

        // Runs a sequence that waits half a second, with a packet or with none, and gives the frames it took to end
        private async UniTask<int> HalfSecond(TaskSequenceBuilder builder, SequenceDataPacket data = null)
        {
            bool done = false;
            Run(new TaskSequence(builder.Delay(0.5f).Do(_ => done = true).Build()), data);
            return await Frames.Until(() => done, 20, "the half second's wait ends");
        }

        private ProcessRelay Run(TaskSequence sequence, SequenceDataPacket data = null)
        {
            var registration = ProcessControl.Register(sequence);
            if (data != null) registration = registration.With(data);
            registration.Run(out var relay);
            World.Own(relay);
            return relay;
        }
    }
}
