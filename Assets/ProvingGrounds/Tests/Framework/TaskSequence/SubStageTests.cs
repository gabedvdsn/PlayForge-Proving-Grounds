using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A sub-stage ran its tasks and nothing else: its callbacks, repeat, timeouts and conditions were read
    /// only for top-level stages, so setting them did nothing.
    /// </summary>
    public class SubStageTests : ProvingGroundsTest
    {
        [UnityTest, Category("Red")]
        public IEnumerator TS_N_A_sub_stage_runs_its_callbacks() => Live(async () =>
        {
            int started = 0, ended = 0;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage => stage.SubStage(sub =>
                {
                    sub.OnInit(_ => started++);
                    sub.OnTerminate(() => ended++);
                    sub.Task(new ProbeSequenceTask(World, "Inner", frames: 1));
                }))
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "the sequence ends");

            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));
        });

        [UnityTest, Category("Red")]
        public IEnumerator TS_N_A_sub_stage_repeats() => Live(async () =>
        {
            int iterations = 0;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage => stage.SubStage(sub =>
                {
                    sub.WithRepeat(true);
                    sub.OnRepeat(context => { if (++iterations == 3) context.Data.BreakStageRepeat(context.Stage); });
                    sub.Task(new ProbeSequenceTask(World, "Inner", frames: 1));
                }))
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 12, "the sequence ends");

            Assert.That(iterations, Is.EqualTo(3));
        });

        /// <summary>
        /// A sub-stage's own timeout stops it and leaves the rest of its parent running. A timer applied its
        /// injection to the current top-level stage, so here it stopped the parent.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_K_A_sub_stages_timeout_stops_just_it() => Live(async () =>
        {
            Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage =>
                {
                    stage.Task(new ProbeSequenceTask(World, "Sibling", frames: 30));
                    stage.SubStage(sub =>
                    {
                        sub.Task(new ProbeSequenceTask(World, "Inner", frames: 1000));
                        sub.WithTimeout(0.1f);
                    });
                })
                .Task(new ProbeSequenceTask(World, "Next"))
                .Build()));

            await Frames.Seconds(0.2f);

            Assert.That(Ledger.Prepares("Next"), Is.EqualTo(0), "the parent is still waiting on its other task");

            await Frames.Until(() => Ledger.Cleans("Next") == 1, 30, "then the sequence goes on, with the sub-stage long stopped");
        });

        /// <summary>A sub-stage's own condition skips it, and leaves the rest of its parent running, as its timeout does.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_K_A_sub_stages_condition_skips_just_it() => Live(async () =>
        {
            bool skip = false;
            Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage =>
                {
                    stage.Task(new ProbeSequenceTask(World, "Sibling", frames: 30));
                    stage.SubStage(sub =>
                    {
                        sub.SkipWhen(_ => skip);
                        sub.Task(new ProbeSequenceTask(World, "Inner", frames: 1000));
                    });
                })
                .Task(new ProbeSequenceTask(World, "Next"))
                .Build()));
            await Frames.Step(2);

            skip = true;
            await Frames.Step(3);
            Assert.That(Ledger.Prepares("Next"), Is.EqualTo(0), "the parent is still waiting on its other task");

            await Frames.Until(() => Ledger.Cleans("Next") == 1, 35, "then the sequence goes on, with the sub-stage long stopped");
        });

        /// <summary>
        /// Breaking "the current stage's" repeat reached only the top-level stage, so a repeating sub-stage couldn't
        /// be broken from inside it.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_K_BreakStageRepeat_reaches_a_repeating_sub_stage() => Live(async () =>
        {
            int iterations = 0;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage => stage.SubStage(sub =>
                {
                    sub.WithRepeat(true);
                    sub.Do(data => { if (++iterations == 3) data.BreakStageRepeat(); });
                }))
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 12, "the sequence ends");
            Assert.That(iterations, Is.EqualTo(3));
        });

        /// <summary>A sub-stage's own handler can take its error, as a top-level stage's can.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_N_A_sub_stage_handles_its_own_error() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Inner", frames: 1);
            Faults.Arm(task.Site(TaskHook.Execute), loggedAs: null);

            Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage => stage.SubStage(sub =>
                {
                    sub.OnException((_, _) => true);
                    sub.Task(task);
                }))
                .Task(new ProbeSequenceTask(World, "Next"))
                .Build()));

            await Frames.Until(() => Ledger.Cleans("Next") == 1, 5, "the sequence goes on");
        });

        /// <summary>A critical sub-stage was never on the active list, so it held no critical section.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_N_A_critical_sub_stage_holds_the_critical_section() => Live(async () =>
        {
            int exits = 0;
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage => stage.SubStage(sub =>
                {
                    sub.WithCriticalFlag(true);
                    sub.Task(new ProbeSequenceTask(World, "Critical", frames: 3));
                }))
                .Task(new ProbeSequenceTask(World, "After", frames: 30))
                .Build()) { OnCriticalSectionExited = () => exits++ };
            Run(sequence);
            await Frames.Step();

            Assert.That(sequence.IsCriticalSection, "critical while the sub-stage runs");

            await Frames.Until(() => Ledger.Prepares("After") == 1, 6, "the stage ends");
            Assert.That(sequence.IsCriticalSection, Is.False, "and the critical section with it");
            Assert.That(exits, Is.EqualTo(1), "which is reported once");
        });

        /// <summary>The control: a throw in a sub-stage still reaches its parent, as it always has.</summary>
        [UnityTest]
        public IEnumerator A_throw_in_a_sub_stage_fails_its_parent() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Inner", frames: 1);
            Faults.Arm(task.Site(TaskHook.Execute), loggedAs: UnityEngine.LogType.Error);

            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Outer")
                .Stage(stage => stage.SubStage(sub => sub.Task(task)))
                .Task(new ProbeSequenceTask(World, "Next"))
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "the sequence ends");

            Assert.That(Ledger.Prepares("Next"), Is.EqualTo(0), "without going on");
        });

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            return relay;
        }
    }
}
