using System.Collections;
using System.Collections.Generic;
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
        [UnityTest, Category("Red")]
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
        [UnityTest, Category("Red")]
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
        [UnityTest, Category("Red")]
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
