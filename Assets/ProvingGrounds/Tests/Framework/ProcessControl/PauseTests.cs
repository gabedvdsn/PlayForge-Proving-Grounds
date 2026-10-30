using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// What pausing means (Decision 5): a process holds in place. It stops at once, a process that can hold
    /// its run carries on from where it stopped, and one that can't is cancelled and runs again on resume.
    /// </summary>
    public class PauseTests : ProvingGroundsTest
    {
        private const int WorkFrames = 8;

        /// <summary>A pause was queued for the end of the run, so a running process wasn't paused at all.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_pause_takes_effect_at_once(
            [Values(EProcessLifecycle.SelfTerminating, EProcessLifecycle.RunThenWait, EProcessLifecycle.Synchronous)] EProcessLifecycle lifecycle) => Live(async () =>
        {
            var probe = World.Probe("Paused", lifecycle);
            await Frames.Step();

            Assert.That(probe.Relay.Pause(), "the pause is accepted");
            Assert.That(probe.Relay.IsWaiting, "it's paused already");

            int updates = probe.Recorder[ProbeHook.Update];
            await Frames.Step(3);

            Assert.That(probe.Recorder[ProbeHook.Update], Is.EqualTo(updates), "and it stays stopped");
            Assert.That(probe.Recorder[ProbeHook.Wait], Is.EqualTo(1));
        });

        /// <summary>A pause overwrote the queued state, so a process told to end went to Waiting instead.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_requested_terminate_outranks_a_pause() => Live(async () =>
        {
            var probe = World.Probe("Ending");
            await Frames.Step();

            probe.Relay.Terminate();

            Assert.That(probe.Relay.Pause(), Is.False, "the pause is refused");
            await Frames.Until(() => probe.Relay.State == EProcessState.Terminated, 5, "it ends as it was told to");
        });

        /// <summary>A process that says it can hold keeps its run, and carries on with what was left of it.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_run_that_can_hold_carries_on([Values] bool mono) => Live(async () =>
        {
            var work = Holding(mono, out var relay);
            await Frames.Step(3);
            int worked = work.Worked;

            relay.Pause();
            await Frames.Step(5);
            Assert.That(work.Worked, Is.EqualTo(worked), "it held");

            relay.Unpause();

            await Frames.Until(() => relay.State == EProcessState.Terminated, WorkFrames, "it carries on and ends");
            Assert.That(work.Worked, Is.EqualTo(WorkFrames), "with all its work done");
            Assert.That(work.Runs, Is.EqualTo(1), "in one run");
        });

        /// <summary>A process that can't hold is cancelled, and a new run starts on resume.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_run_that_cant_hold_starts_again() => Live(async () =>
        {
            var probe = World.Probe("Restarted", runFrames: 100);
            await Frames.Step(2);

            probe.Relay.Pause();
            await Frames.Step(2);
            Assert.That(probe.Recorder.RunsCancelled, Is.EqualTo(1), "its run was cancelled");

            probe.Relay.Unpause();
            await Frames.Step(2);

            Assert.That(probe.Recorder[ProbeHook.Run], Is.EqualTo(2), "and it runs again");
            Assert.That(probe.Relay.IsRunning);
        });

        /// <summary>A held run has nothing else to end it, so ending the process has to.</summary>
        [UnityTest]
        public IEnumerator PC_11_Terminating_a_paused_process_ends_its_held_run() => Live(async () =>
        {
            var work = Holding(false, out var relay);
            await Frames.Step(2);

            relay.Pause();
            await Frames.Step();

            relay.Terminate();
            await Frames.Step(2);

            Assert.That(relay.State, Is.EqualTo(EProcessState.Terminated));
            Assert.That(work.Cancelled, "the held run was stopped");
        });

        /// <summary>A run that finishes while its process is paused is over: resuming moves on, not back.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_run_that_finished_during_a_pause_doesnt_run_again() => Live(async () =>
        {
            var work = Holding(false, out var relay, holds: false, frames: 3);
            await Frames.Step();

            relay.Pause();
            await Frames.Step(WorkFrames);
            Assert.That(work.Worked, Is.EqualTo(3), "it finished regardless");

            relay.Unpause();

            await Frames.Until(() => relay.State == EProcessState.Terminated, 3, "it ends");
            Assert.That(work.Runs, Is.EqualTo(1), "without running again");
        });

        /// <summary>Asked to pause while ProcessControl has it stopped, the pause becomes its own and outlasts that.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_pause_during_a_control_pause_outlasts_it() => Live(async () =>
        {
            var probe = World.Probe("Paused Twice", EProcessLifecycle.Synchronous);
            await Frames.Step();

            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step();

            Assert.That(probe.Relay.Pause(), "the pause is accepted");

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();

            Assert.That(probe.Relay.IsWaiting, "ProcessControl's resume wasn't for this");
        });

        /// <summary>
        /// A pause and resume in one frame starts a new run while the old one unwinds, and they shared a token
        /// source, so the old run cancelled the new one and then applied its queued state.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_12_A_pause_and_resume_in_one_frame_leaves_the_new_run_alive([Values] bool control) => Live(async () =>
        {
            var probe = World.Probe("Flicker");
            await Frames.Step(2);

            if (control)
            {
                World.Control.SetControlState(EProcessControlState.Waiting);
                World.Control.SetControlState(EProcessControlState.Ready);
            }
            else
            {
                probe.Relay.Pause();
                probe.Relay.Unpause();
            }

            await Frames.Step(3);

            Assert.That(probe.Relay.IsRunning, "the new run goes on");
            Assert.That(probe.Recorder[ProbeHook.Run], Is.EqualTo(2));
            Assert.That(probe.Recorder.RunsCancelled, Is.EqualTo(1), "only the old run was cancelled");
        });

        /// <summary>ProcessControl's resume sends back what its pause stopped, not everything that's Waiting.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_process_paused_on_its_own_stays_paused([Values] EProcessLifecycle lifecycle) => Live(async () =>
        {
            var probe = World.Probe("Paused Alone", lifecycle);
            if (lifecycle == EProcessLifecycle.RequiresControl) probe.Relay.Unpause();
            await Frames.Step();

            probe.Relay.Pause();
            await Frames.Step();

            await PauseAndResume();

            Assert.That(probe.Relay.IsWaiting, "it wasn't ProcessControl that stopped it");
        });

        /// <summary>A process registered during a pause waits for it, then starts as it would have.</summary>
        [UnityTest]
        public IEnumerator PC_07_A_process_registered_during_a_pause_starts_when_it_ends([Values] EProcessLifecycle lifecycle) => Live(async () =>
        {
            World.Control.SetControlState(EProcessControlState.Waiting);
            var probe = World.Probe("Late", lifecycle);
            await Frames.Step();
            Assert.That(probe.Relay.IsWaiting, "it waits for ProcessControl");

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step(2);

            var expected = ProcessStateOracle.Expected(EProcessControlState.Ready, lifecycle, EProcessState.Created);
            Assert.That(probe.Relay.State, Is.EqualTo(expected));
        });

        /// <summary>ProcessControl's pause holds a run that can hold, as a pause of its own does.</summary>
        [UnityTest]
        public IEnumerator A_control_pause_leaves_a_held_run_where_it_was([Values] bool mono) => Live(async () =>
        {
            var work = Holding(mono, out var relay);
            await Frames.Step(3);
            int worked = work.Worked;

            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step(5);
            Assert.That(work.Worked, Is.EqualTo(worked), "it held");

            World.Control.SetControlState(EProcessControlState.Ready);

            await Frames.Until(() => relay.State == EProcessState.Terminated, WorkFrames, "it carries on and ends");
            Assert.That(work.Runs, Is.EqualTo(1), "in one run");
        });

        /// <summary>A Terminate that was on its way when ProcessControl paused still lands.</summary>
        [UnityTest]
        public IEnumerator PC_11_A_requested_terminate_survives_a_control_pause() => Live(async () =>
        {
            var probe = World.Probe("Ending", EProcessLifecycle.RunThenWait);
            await Frames.Step();

            probe.Relay.Terminate();
            await PauseAndResume();

            Assert.That(probe.Relay.State, Is.EqualTo(EProcessState.Terminated));
            Assert.That(probe.Recorder[ProbeHook.Run], Is.EqualTo(1), "it didn't run again");
        });

        /// <summary>The table says a paused ProcessControl holds what it paused, and Unpause has to say so.</summary>
        [UnityTest]
        public IEnumerator PC_27_Unpause_is_refused_while_ProcessControl_is_paused() => Live(async () =>
        {
            var probe = World.Probe("Held");
            await Frames.Step();

            World.Control.SetControlState(EProcessControlState.Waiting);

            Assert.That(probe.Relay.Unpause(), Is.False);

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();
        });

        private async UniTask PauseAndResume()
        {
            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step();
            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();
        }

        private HeldWork Holding(bool mono, out ProcessRelay relay, bool holds = true, int frames = WorkFrames)
        {
            var work = new HeldWork(frames, holds);

            if (mono)
            {
                var process = new GameObject("Holding Mono").AddComponent<HoldingMonoProcess>();
                process.Work = work;
                process.ProcessLifecycle = EProcessLifecycle.SelfTerminating;
                process.ProcessTiming = EProcessStepTiming.Update;
                relay = World.Register(process);
            }
            else
            {
                relay = World.Register(new HoldingRuntimeProcess(work));
            }

            return work;
        }

        /// <summary>
        /// A run doing a frame of work at a time, which holds the way any process can: it says it will, and
        /// its run looks at its relay.
        /// </summary>
        private sealed class HeldWork
        {
            private readonly int _frames;
            private readonly bool _holds;

            public int Worked { get; private set; }
            public int Runs { get; private set; }
            public bool Cancelled { get; private set; }

            public HeldWork(int frames, bool holds)
            {
                _frames = frames;
                _holds = holds;
            }

            public async UniTask Run(ProcessRelay relay, CancellationToken token)
            {
                Runs++;

                try
                {
                    while (Worked < _frames)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, token);
                        if (!_holds || !relay.IsWaiting) Worked++;
                    }
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }
        }

        private sealed class HoldingRuntimeProcess : AbstractRuntimeProcess
        {
            private readonly HeldWork _work;

            public HoldingRuntimeProcess(HeldWork work)
                : base("Holding", EProcessStepPriorityMethod.Manual, 0, EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating)
            {
                _work = work;
            }

            public override bool TryHandlePause() => true;

            public override bool TryHandleResume() => true;

            public override UniTask RunProcess(CancellationToken token) => _work.Run(Relay, token);
        }

        private sealed class HoldingMonoProcess : AbstractMonoProcess
        {
            public HeldWork Work;

            public override bool TryHandlePause() => true;

            public override bool TryHandleResume() => true;

            public override UniTask RunProcess(CancellationToken token) => Work.Run(Relay, token);
        }
    }
}
