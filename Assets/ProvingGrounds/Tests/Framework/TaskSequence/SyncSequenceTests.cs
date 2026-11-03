using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A synchronous sequence is stepped by its process, frame by frame, through a runner built from its
    /// definition. It has no runtime, so whatever a runtime does for an async sequence either has to be done here
    /// or has to be said not to be.
    /// </summary>
    public class SyncSequenceTests : ProvingGroundsTest
    {
        public enum Unsupported
        {
            SubStage,
            Branch,
            StageCondition,
            SequenceCondition,
            SequenceTimeout,
            SequenceMaxDuration,
            InterruptingTimer,
            AsyncTask
        }

        /// <summary>Only Update stepped the runner, so a sequence set to step anywhere else never ran.</summary>
        [UnityTest]
        public IEnumerator TS_H_A_sync_sequence_steps_at_its_timing(
            [Values(EProcessStepTiming.FixedUpdate, EProcessStepTiming.LateUpdate, EProcessStepTiming.LateAndFixed)]
            EProcessStepTiming timing) => Live(async () =>
        {
            var relay = Run(Sync("Timed", timing).Stage(stage => stage.SyncDelay(0.25f)));

            int taken = await Frames.Until(() => relay.State == EProcessState.Terminated, 30, "it steps and finishes");
            Assert.That(taken, Is.InRange(13, 18), "in a quarter of a second");
        });

        /// <summary>
        /// The control: a composite timing steps the process at each of its timings, and the sequence still
        /// moves on once a frame.
        /// </summary>
        [UnityTest]
        public IEnumerator A_composite_timing_keeps_a_sync_sequence_at_its_speed(
            [Values(EProcessStepTiming.UpdateAndLate, EProcessStepTiming.UpdateAndFixed, EProcessStepTiming.UpdateFixedAndLate)]
            EProcessStepTiming timing) => Live(async () =>
        {
            var relay = Run(Sync("Composite", timing).Stage(stage => stage.SyncDelay(0.25f)));

            int taken = await Frames.Until(() => relay.State == EProcessState.Terminated, 30, "it finishes");
            Assert.That(taken, Is.InRange(13, 18), "in a quarter of a second, not a fraction of one");
        });

        /// <summary>Conditions were checked from Update alone too, whatever timing they were given.</summary>
        [UnityTest]
        public IEnumerator TS_H_Conditions_are_checked_at_their_timing(
            [Values(EProcessStepTiming.FixedUpdate, EProcessStepTiming.LateUpdate)] EProcessStepTiming timing,
            [Values] bool chain) => Live(async () =>
        {
            bool tripped = false;
            var task = new ProbeSequenceTask(World, "Long", frames: 1000);

            ProcessRelay relay;
            if (chain)
            {
                var sequence = new TaskSequence(TaskSequenceBuilder.Create("Chained").Task(task).Build());
                ProcessControl.Register(new TaskSequenceChain(sequence)
                    .WithConditionCheckTiming(timing)
                    .InterruptWhen(_ => tripped), out relay);
            }
            else
            {
                ProcessControl.Register(new TaskSequence(TaskSequenceBuilder.Create("Conditional")
                    .WithConditionCheckTiming(timing)
                    .InterruptWhen(_ => tripped)
                    .Task(task)
                    .Build()), out relay);
            }
            World.Own(relay);

            await Frames.Step(2);
            tripped = true;

            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "the condition interrupts it");
        });

        /// <summary>OnStep callbacks were kept by timing and never called.</summary>
        [UnityTest]
        public IEnumerator TS_H_OnStep_fires_at_each_timing_of_a_composite() => Live(async () =>
        {
            int updates = 0, lates = 0;
            Run(Sync("Stepped Twice", EProcessStepTiming.UpdateAndLate)
                .OnStep(EProcessStepTiming.Update, (_, _) => updates++)
                .OnStep(EProcessStepTiming.LateUpdate, (_, _) => lates++)
                .SyncTask(_ => false));

            await Frames.Step(5);

            Assert.That(updates, Is.InRange(4, 6), "once a frame at Update");
            Assert.That(lates, Is.InRange(4, 6), "and once at LateUpdate");
        });

        /// <summary>With no sequence packet to step with, a sync sequence made a new one for every step.</summary>
        [UnityTest]
        public IEnumerator TS_P_A_sync_sequence_keeps_one_packet() => Live(async () =>
        {
            var seen = new HashSet<SequenceDataPacket>();
            int steps = 0;
            var runner = TaskSequenceBuilder.Create("One Packet")
                .SyncTask((data, _) =>
                {
                    seen.Add(data);
                    return ++steps >= 3;
                })
                .BuildSyncRunner();

            var relay = World.Register(new TaskSequenceProcess(runner));
            await Frames.Until(() => relay.State == EProcessState.Terminated, 6, "it runs");

            Assert.That(seen, Has.Count.EqualTo(1));
        });

        /// <summary>
        /// A plain packet made an async run throw, and was swapped for an empty one on a sync run, so what it
        /// carried never reached the tasks.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_P_A_plain_packet_reaches_the_tasks([Values] bool synchronous) => Live(async () =>
        {
            var carried = Make.NewTag("Test.Data.Carried");
            var packet = ProcessDataPacket.Default();
            packet.SetPrimary(carried, 7);

            int found = 0;
            var builder = TaskSequenceBuilder.Create("Carrying");
            if (synchronous) builder.WithLifecycle(EProcessLifecycle.Synchronous, EProcessStepTiming.Update);
            builder.Do(data => data.TryGetFirst(carried, out found));

            var relay = World.Register(new TaskSequenceProcess(new TaskSequence(builder.Build())), packet);
            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "it runs");

            Assert.That(found, Is.EqualTo(7));
        });

        /// <summary>A sync sequence ended part way through left the stage it was in uncleaned.</summary>
        [UnityTest]
        public IEnumerator TS_P_Ending_a_sync_sequence_part_way_cleans_it([Values] bool immediately) => Live(async () =>
        {
            var relay = Run(Sync("Ended Early").Task(new ProbeSequenceTask(World, "Long", frames: 100)));
            await Frames.Step(3);

            if (immediately) relay.TerminateImmediate();
            else relay.Terminate();
            await Frames.Until(() => relay.State == EProcessState.Terminated, 3, "it ends");

            Assert.That(Ledger.Cleans("Long"), Is.EqualTo(1));
        });

        /// <summary>Interrupt went to a runtime that a sync sequence doesn't have, and did nothing.</summary>
        [UnityTest]
        public IEnumerator TS_P_A_sync_sequence_can_be_interrupted([Values] bool fromRunner) => Live(async () =>
        {
            var builder = Sync("Interrupted").Task(new ProbeSequenceTask(World, "Long", frames: 100));
            var process = fromRunner
                ? new TaskSequenceProcess(builder.BuildSyncRunner())
                : new TaskSequenceProcess(new TaskSequence(builder.Build()));
            var relay = World.Register(process);
            await Frames.Step(3);

            Assert.That(process.Interrupt(), "the interrupt is accepted");

            await Frames.Until(() => relay.State == EProcessState.Terminated, 3, "and it ends");
            Assert.That(Ledger.Cleans("Long"), Is.EqualTo(1), "cleaned");
        });

        /// <summary>A stage's callbacks were left behind when the runner was built.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_Q_A_sync_stage_runs_its_callbacks() => Live(async () =>
        {
            int inits = 0, repeats = 0;
            bool? ended = null;
            var relay = Run(Sync("Called Back").Stage(stage =>
            {
                stage.OnInit(_ => inits++);
                stage.WithRepeat(true);
                stage.OnRepeat(context => { if (++repeats == 3) context.Data.BreakStageRepeat(); });
                stage.OnTerminate((_, completed) => ended = completed);
                stage.Task(new ProbeSequenceTask(World, "Stepped", frames: 1));
            }));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 10, "it repeats three times and ends");

            Assert.That(inits, Is.EqualTo(1), "started once");
            Assert.That(repeats, Is.EqualTo(3), "told of each repeat");
            Assert.That(ended, Is.True, "and of its end");
        });

        /// <summary>WhenN came across as WhenAll.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_Q_A_sync_WhenN_stage_ends_at_N() => Live(async () =>
        {
            var relay = Run(Sync("Two Of Three").Stage(stage =>
            {
                stage.WhenN(2);
                stage.Task(new ProbeSequenceTask(World, "Quick", frames: 1));
                stage.Task(new ProbeSequenceTask(World, "Slower", frames: 3));
                stage.Task(new ProbeSequenceTask(World, "Slowest", frames: 100));
            }));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 6, "it ends with the second task");
            Assert.That(Ledger.Cleans("Slowest"), Is.EqualTo(1), "and the last is cleaned unfinished");
        });

        /// <summary>A stage that repeats by its policy ran once.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_Q_RepeatUntilSkipped_repeats_a_sync_stage() => Live(async () =>
        {
            int iterations = 0;
            var relay = Run(Sync("Repeated By Policy").Stage(stage =>
            {
                stage.WithRepeatAndDelay();
                stage.Do(data => { if (++iterations == 3) data.BreakStageRepeat(); });
            }));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 10, "it repeats and ends");
            Assert.That(iterations, Is.EqualTo(3));
        });

        /// <summary>Stage timeouts were dropped, so a stage waited out its slowest task.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_Q_A_timeout_ends_a_sync_stage() => Live(async () =>
        {
            Run(Sync("Timed Out")
                .Stage(stage => stage.WithTimeout(0.1f).Task(new ProbeSequenceTask(World, "Slow", frames: 100)))
                .Task(new ProbeSequenceTask(World, "Next", frames: 1)));

            await Frames.Until(() => Ledger.Cleans("Next") == 1, 12, "the stage times out and the next one runs");
            Assert.That(Ledger.Cleans("Slow"), Is.EqualTo(1));
        });

        /// <summary>The sequence's OnComplete never fired.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_Q_OnComplete_fires_for_a_sync_sequence() => Live(async () =>
        {
            bool? completed = null;
            var relay = Run(Sync("Completed")
                .OnComplete((_, success) => completed = success)
                .Task(new ProbeSequenceTask(World, "Stepped", frames: 2)));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "it ends");
            Assert.That(completed, Is.True);
        });

        /// <summary>
        /// A sequence that's critical as a whole came across with no critical stage, so its critical section
        /// never ended. An ability waits on that to let the next activation in.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_Q_A_critical_sync_sequence_reports_its_exit() => Live(async () =>
        {
            int exits = 0;
            var sequence = new TaskSequence(Sync("Critical")
                .WithCriticalFlag(true)
                .Task(new ProbeSequenceTask(World, "Stepped", frames: 2))
                .Build()) { OnCriticalSectionExited = () => exits++ };

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);

            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "it ends");
            Assert.That(exits, Is.EqualTo(1));
        });

        /// <summary>What a runner can't do was left out without a word. Each such thing is named as the runner is built.</summary>
        [Test, Category("Red")]
        public void TS_Q_What_a_sync_sequence_cant_run_is_named([Values] Unsupported feature)
        {
            var builder = WithUnsupported(TaskSequenceBuilder.Create("Unsupported"), feature);
            Assert.That(WarningsWhile(() => builder.BuildSyncRunner()), Is.EqualTo(1));
        }

        /// <summary>The control: what a runner can do builds without a warning.</summary>
        [Test]
        public void A_sync_sequence_with_nothing_unsupported_builds_quietly()
        {
            var builder = TaskSequenceBuilder.Create("Supported")
                .OnComplete((_, _) => { })
                .Stage(stage =>
                {
                    stage.OnInit(_ => { });
                    stage.OnRepeat(_ => { });
                    stage.OnTerminate(() => { });
                    stage.WithRepeat(true);
                    stage.WithTimeout(1f);
                    stage.WithMaxDuration(2f);
                    stage.WhenN(1);
                    stage.SyncDelay(0.5f);
                    stage.Do(_ => { });
                })
                .Stage(stage => stage.WithRepeatAndDelay().Do(_ => { }));

            Assert.That(WarningsWhile(() => builder.BuildSyncRunner()), Is.EqualTo(0));
        }

        private static TaskSequenceBuilder Sync(string name, EProcessStepTiming timing = EProcessStepTiming.Update)
        {
            return TaskSequenceBuilder.Create(name).WithLifecycle(EProcessLifecycle.Synchronous, timing);
        }

        private ProcessRelay Run(TaskSequenceBuilder builder)
        {
            ProcessControl.Register(new TaskSequence(builder.Build()), out var relay);
            World.Own(relay);
            return relay;
        }

        private static TaskSequenceBuilder WithUnsupported(TaskSequenceBuilder builder, Unsupported feature)
        {
            switch (feature)
            {
                case Unsupported.SubStage:
                    return builder.Stage(stage => stage.SubStage(sub => sub.Do(_ => { })));
                case Unsupported.Branch:
                    return builder.Branch(branch => branch.Default(stage => stage.Do(_ => { })));
                case Unsupported.StageCondition:
                    return builder.Stage(stage => stage.SkipWhen(_ => false).Do(_ => { }));
                case Unsupported.SequenceCondition:
                    return builder.InterruptWhen(_ => false).Do(_ => { });
                case Unsupported.SequenceTimeout:
                    return builder.WithTimeout(1f).Do(_ => { });
                case Unsupported.SequenceMaxDuration:
                    return builder.WithMaxDuration(1f).Do(_ => { });
                case Unsupported.InterruptingTimer:
                    return builder.Stage(stage => stage.WithTimeout(1f, InterruptSequenceInjection.Instance).Do(_ => { }));
                case Unsupported.AsyncTask:
                    return builder.Delay(1f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
            }
        }

        // Counts the warnings PlayForge logs while the action runs
        private static int WarningsWhile(Action action)
        {
            int warnings = 0;
            Application.LogCallback count = (message, _, type) =>
            {
                if (type == LogType.Warning && message.StartsWith("[PlayForge]")) warnings++;
            };

            Application.logMessageReceived += count;
            try
            {
                action();
            }
            finally
            {
                Application.logMessageReceived -= count;
            }

            return warnings;
        }
    }
}
