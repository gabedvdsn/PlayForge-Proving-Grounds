using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ProcessLifecycleTests : ProvingGroundsTest
    {
        public enum LazyKind
        {
            RuntimeGas,
            PlainRuntime,
            ActionProcess,
            Mono,
            Sequence
        }

        /// <summary>
        /// A throw in an async RunProcess is logged, but the state queued for the end of the run is skipped,
        /// so the process stays Running for good and anything waiting on it hangs (B10).
        /// </summary>
        [UnityTest]
        public IEnumerator PC_02_A_run_that_throws_still_ends(
            [Values(EProcessLifecycle.SelfTerminating, EProcessLifecycle.RunThenWait)] EProcessLifecycle lifecycle) => Live(async () =>
        {
            var probe = World.NewProbe("Broken Run", lifecycle);
            Faults.Arm(probe.Recorder.Site(ProbeHook.Run));
            World.Register(probe);

            await Frames.Step(3);

            var expected = ProcessStateOracle.Expected(EProcessControlState.Ready, lifecycle, EProcessState.Running);
            Assert.That(probe.Relay.State, Is.EqualTo(expected), "the run is over, so the process moves on as any finished run does");
        });

        /// <summary>
        /// The hierarchy is keyed by the object handed to Register, which for a prefab is the prefab, not the
        /// clone. Every clone of one prefab shares a node and so counts as a sibling of the rest, and ending
        /// one cascades to them all.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_03_PrefabCascade_KillsOnlyOne() => Live(async () =>
        {
            var template = World.MonoProbeTemplate("Enemy", EProcessLifecycle.Synchronous);
            var doomed = World.Spawn(template);
            var survivor = World.Spawn(template);
            var bystander = World.Spawn(template);
            await Frames.Step();

            doomed.ProcessRelay.TerminateImmediate();
            await Frames.Step();

            Assert.That(World.Control.IsRegistered(survivor.ProcessRelay.CacheIndex), "the second clone survives");
            Assert.That(World.Control.IsRegistered(bystander.ProcessRelay.CacheIndex), "the third clone survives");
        });

        /// <summary>The same cascade as a game meets it: one enemy of a prefab dies and takes the rest with it.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_03_One_GAS_death_leaves_the_rest_of_its_prefab() => Live(async () =>
        {
            var template = World.Template<GameplayAbilitySystem>("Enemy");
            template.EntityData = Make.Identity("Enemy", Make.UnusedSet());
            template.ProcessLifecycle = EProcessLifecycle.Synchronous;
            template.ProcessTiming = EProcessStepTiming.UpdateAndLate;

            var doomed = World.Spawn(template);
            var survivor = World.Spawn(template);
            await Frames.Step();

            doomed.MarkDead();
            await Frames.Step(2);

            Assert.That(World.Control.IsRegistered(survivor.ProcessRelay.CacheIndex), "the other enemy survives");
        });

        /// <summary>
        /// The hierarchy follows the clone, so what hangs off a clone is that clone's to end, and no other's.
        /// Keyed by the prefab, a clone was never anyone's parent.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_03_A_clone_ends_its_own_children_and_no_others() => Live(async () =>
        {
            var template = World.MonoProbeTemplate("Enemy", EProcessLifecycle.Synchronous);
            var doomed = World.Spawn(template);
            var survivor = World.Spawn(template);
            var doomedChild = ChildProbe(doomed);
            var survivorChild = ChildProbe(survivor);
            await Frames.Step();

            doomed.ProcessRelay.TerminateImmediate();
            await Frames.Step();

            Assert.That(World.Control.IsRegistered(doomedChild.ProcessRelay.CacheIndex), Is.False, "its child goes with it");
            Assert.That(World.Control.IsRegistered(survivorChild.ProcessRelay.CacheIndex), "the other clone's child stays");
        });

        private ProbeMonoProcess ChildProbe(Component parent)
        {
            var host = new GameObject($"{parent.name} Child");
            host.transform.SetParent(parent.transform);

            var probe = host.AddComponent<ProbeMonoProcess>();
            probe.ProcessLifecycle = EProcessLifecycle.Synchronous;
            probe.ProcessTiming = EProcessStepTiming.Update;
            World.Register(probe);
            return probe;
        }

        /// <summary>The control for PC-3: separate objects with no prefab in common end alone already.</summary>
        [UnityTest]
        public IEnumerator Separate_scene_objects_end_alone() => Live(async () =>
        {
            var doomed = World.MonoProbe("Doomed", EProcessLifecycle.Synchronous);
            var survivor = World.MonoProbe("Survivor", EProcessLifecycle.Synchronous);
            await Frames.Step();

            doomed.ProcessRelay.TerminateImmediate();
            await Frames.Step();

            Assert.That(World.Control.IsRegistered(survivor.ProcessRelay.CacheIndex));
        });

        /// <summary>
        /// A forced state change moves on without waiting for the run, and when the cancelled run unwound it
        /// applied that state again: a second WhenTerminate, with its Destroy, unregister and events.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_04_A_forced_terminate_ends_a_process_once() => Live(async () =>
        {
            var probe = World.Probe("Forced");
            await Frames.Step();

            probe.Relay.TerminateImmediate();
            await Frames.Step(3);

            Assert.That(probe.Recorder[ProbeHook.Terminate], Is.EqualTo(1));
        });

        /// <summary>Forced Waiting goes the same way, with a second WhenWait.</summary>
        [UnityTest]
        public IEnumerator PC_04_A_forced_wait_waits_once() => Live(async () =>
        {
            var probe = World.Probe("Forced");
            await Frames.Step();

            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step(3);

            Assert.That(probe.Recorder[ProbeHook.Wait], Is.EqualTo(1));
        });

        /// <summary>
        /// A queued state applies when the run ends, and a Lazy process's run only ends when something sets
        /// processActive false, which WhenTerminate does. So WhenTerminate waits for the run and the run waits
        /// for WhenTerminate, and the process never leaves Running. That includes every runtime GAS, and a
        /// runtime GAS's death is a queued Terminate.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_05_A_queued_terminate_ends_a_running_Lazy_process([Values] LazyKind kind) => Live(async () =>
        {
            var relay = SpawnLazy(kind);
            await Frames.Step();
            Assert.That(relay.IsRunning, "it's running");

            relay.Terminate();

            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "the queued terminate completes");
            Assert.That(World.Control.IsRegistered(relay.CacheIndex), Is.False, "its block is gone");
        });

        /// <summary>A cascade queues the same Terminate on a child, so a Lazy child has to hear of it too.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_05_A_cascaded_terminate_ends_a_running_Lazy_child() => Live(async () =>
        {
            var parent = SpawnLazy(LazyKind.Mono);
            parent.TryGetProcess<LazyMonoProcess>(out var parentProcess);

            var host = new GameObject("Lazy Child");
            host.transform.SetParent(parentProcess.transform);
            var child = host.AddComponent<LazyMonoProcess>();
            child.ProcessLifecycle = EProcessLifecycle.SelfTerminating;
            child.ProcessTiming = EProcessStepTiming.Update;
            var childRelay = World.Register(child);
            await Frames.Step();

            parent.Terminate();

            await Frames.Until(() => !World.Control.IsRegistered(childRelay.CacheIndex), 5, "the child ends with its parent");
        });

        /// <summary>
        /// A queued Terminate is graceful: only a run with nothing left to do ends early. The control for
        /// PC-5, so that a fix can't cut every run short.
        /// </summary>
        [UnityTest]
        public IEnumerator A_queued_terminate_lets_a_run_with_work_finish() => Live(async () =>
        {
            var probe = new SteadyRun(World, "Steady", frames: 4);
            var relay = World.Register(probe);
            await Frames.Step();

            relay.Terminate();

            await Frames.Until(() => relay.State == EProcessState.Terminated, 8, "the run finishes, then the process ends");
            Assert.That(probe.Recorder.RunsCompleted, Is.EqualTo(1), "it ran to its end");
            Assert.That(probe.Recorder.RunsCancelled, Is.EqualTo(0), "nothing cut it short");
        });

        /// <summary>A run with work of its own, which doesn't wait on processActive as a Lazy process does.</summary>
        private sealed class SteadyRun : AbstractRuntimeProcess
        {
            public readonly ProbeRecorder Recorder;

            public SteadyRun(World world, string name, int frames)
                : base(name, EProcessStepPriorityMethod.Manual, 0, EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating)
            {
                Recorder = new ProbeRecorder(name, world.Faults, frames);
            }

            public override UniTask RunProcess(CancellationToken token) => Recorder.Run(() => true, token);
        }

        private ProcessRelay SpawnLazy(LazyKind kind)
        {
            switch (kind)
            {
                case LazyKind.RuntimeGas:
                    return World.RuntimeEntity().Relay;

                case LazyKind.PlainRuntime:
                    return World.Register(new LazyRuntimeProcess("Lazy", EProcessStepPriorityMethod.Manual, 0,
                        EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating));

                case LazyKind.ActionProcess:
                    return World.Register(new ActionProcess("Action", EProcessStepPriorityMethod.Manual, 0,
                        EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating, () => { }));

                case LazyKind.Mono:
                    var host = new GameObject("Lazy Mono");
                    var mono = host.AddComponent<LazyMonoProcess>();
                    mono.ProcessLifecycle = EProcessLifecycle.SelfTerminating;
                    mono.ProcessTiming = EProcessStepTiming.Update;
                    return World.Register(mono);

                default:
                    var sequence = new TaskSequence(TaskSequenceBuilder.Create("Long Sequence").Delay(100f).Build());
                    ProcessControl.Register(sequence, out var relay);
                    World.Own(relay);
                    return relay;
            }
        }

        /// <summary>
        /// The Register overloads with no data packet pass null on, and registration calls through it. The
        /// exception comes after the cache index is spent and the handler has subscribed.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_06_Registering_with_no_data_works([Values] bool withHandler) => Live(async () =>
        {
            var probe = World.NewProbe("No Data", EProcessLifecycle.Synchronous);
            bool registered = false;
            ProcessRelay relay = null;

            Assert.DoesNotThrow(() => registered = withHandler
                ? ProcessControl.Register(probe, (IGameplayProcessHandler)World.Root, out relay)
                : ProcessControl.Register(probe, out relay));

            Assert.That(registered, "it registers");
            World.Own(relay);

            await Frames.Step(2);
            Assert.That(probe.Recorder[ProbeHook.Update], Is.EqualTo(2), "and it runs");
        });

        /// <summary>RunProcessTask registers with the handler alone, so it hits PC-6 on every activation.</summary>
        [UnityTest]
        public IEnumerator PC_06_An_ability_can_run_a_process([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var probe = World.NewProbe("Run By Ability", runFrames: 2);
            var task = new RunProcessTask { Processes = new List<AbstractRuntimeProcess> { probe } };

            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Runner", Make.Stage(task)), 1, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => probe.Recorder[ProbeHook.Terminate] == 1, 10, "the process runs and ends");
        });

        /// <summary>
        /// A control state change ends by queuing every process's default transition. For a synchronous
        /// process that's Running to Waiting, so a pause and resume parks it for good.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_07_A_pause_and_resume_leaves_a_synchronous_process_running() => Live(async () =>
        {
            var probe = World.Probe("Steady", EProcessLifecycle.Synchronous);
            await Frames.Step();

            await PauseAndResume();
            await Frames.Step(2);

            Assert.That(probe.Relay.State, Is.EqualTo(EProcessState.Running), "it's where it was");
        });

        /// <summary>The same default transition starts a process that only runs when told to.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_07_A_pause_and_resume_doesnt_start_a_RequiresControl_process() => Live(async () =>
        {
            var probe = World.Probe("Held", EProcessLifecycle.RequiresControl);
            await Frames.Step();

            await PauseAndResume();
            await Frames.Step(2);

            Assert.That(probe.Recorder[ProbeHook.Run], Is.EqualTo(0), "nobody told it to run");
            Assert.That(probe.Relay.IsWaiting, "it's where it was");
        });

        /// <summary>And it runs again a process that already ran once and was done.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_07_A_pause_and_resume_doesnt_rerun_a_finished_RunThenWait_process() => Live(async () =>
        {
            var probe = World.Probe("Once", EProcessLifecycle.RunThenWait, runFrames: 1);
            await Frames.Step(3);
            Assert.That(probe.Relay.IsWaiting, "its run is over");

            await PauseAndResume();
            await Frames.Step(2);

            Assert.That(probe.Recorder[ProbeHook.Run], Is.EqualTo(1), "it runs once");
        });

        private async UniTask PauseAndResume()
        {
            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step();
            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();
        }
    }
}
