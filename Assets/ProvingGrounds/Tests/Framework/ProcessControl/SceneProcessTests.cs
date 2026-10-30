using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class SceneProcessTests : ProvingGroundsTest
    {
        /// <summary>
        /// Anything outside a scene was taken for a prefab, so a live object with HideAndDontSave was cloned,
        /// and what the caller had set on it went nowhere.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_09_A_live_object_outside_any_scene_isnt_cloned() => Live(async () =>
        {
            var host = new GameObject("Hidden") { hideFlags = HideFlags.HideAndDontSave };
            var probe = host.AddComponent<ProbeMonoProcess>();
            probe.ProcessLifecycle = EProcessLifecycle.Synchronous;
            probe.ProcessTiming = EProcessStepTiming.Update;

            var relay = World.Register(probe);
            await Frames.Step();

            Assert.That(relay.TryGetProcess<ProbeMonoProcess>(out var running) && running == probe, "it runs as itself");
            Assert.That(probe.IsInitialized, "and is initialized");

            relay.TerminateImmediate();
            await Frames.Step();
            Assert.That(host == null, "and is destroyed with its process");
        });

        /// <summary>The control for the test above: a prefab is still cloned, and left as it was.</summary>
        [UnityTest]
        public IEnumerator A_prefab_is_cloned_and_left_alone() => Live(async () =>
        {
            var template = World.MonoProbeTemplate("Prefab", EProcessLifecycle.Synchronous);

            var clone = World.Spawn(template);
            await Frames.Step();

            Assert.That(clone, Is.Not.SameAs(template), "what runs is a clone");
            Assert.That(clone.gameObject.activeInHierarchy, "and it's active");
            Assert.That(template.IsInitialized, Is.False, "and the prefab is untouched");
        });

        /// <summary>
        /// Nothing told ProcessControl when a process's object went, so an external Destroy or a scene unload
        /// left a block stepping a dead object.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_10_Destroying_a_process_object_ends_the_process() => Live(async () =>
        {
            var probe = World.MonoProbe("Destroyed", EProcessLifecycle.Synchronous);
            int index = probe.ProcessRelay.CacheIndex;
            await Frames.Step();

            Object.Destroy(probe.gameObject);
            await Frames.Step(2);

            Assert.That(World.Control.IsRegistered(index), Is.False, "its block is gone");
            Assert.That(probe.Recorder[ProbeHook.Terminate], Is.EqualTo(1), "and it was told once");
        });

        [UnityTest]
        public IEnumerator PC_10_Unloading_a_scene_ends_its_processes() => Live(async () =>
        {
            var scene = World.AddScene("Level");
            var probe = World.MonoProbe("In The Level", EProcessLifecycle.Synchronous);
            SceneManager.MoveGameObjectToScene(probe.gameObject, scene);
            int index = probe.ProcessRelay.CacheIndex;
            await Frames.Step();

            await World.UnloadScene(scene);
            await Frames.Step();

            Assert.That(World.Control.IsRegistered(index), Is.False);
        });

        /// <summary>
        /// Processes sharing an object go together. One opted out of sibling cascades, which is the case that
        /// used to leave a block behind when the other destroyed their object.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_10_Processes_on_one_object_go_together() => Live(async () =>
        {
            var first = World.MonoProbe("Shared Object", EProcessLifecycle.Synchronous);

            var second = first.gameObject.AddComponent<ProbeMonoProcess>();
            second.ProcessLifecycle = EProcessLifecycle.Synchronous;
            second.ProcessTiming = EProcessStepTiming.Update;
            second.ParticipateInSiblingCascade = false;
            var secondRelay = World.Register(second);
            await Frames.Step();

            first.ProcessRelay.TerminateImmediate();
            await Frames.Step(2);

            Assert.That(World.Control.IsRegistered(secondRelay.CacheIndex), Is.False);
        });

        /// <summary>A normal end goes through OnDestroy too, and that mustn't end the process a second time.</summary>
        [UnityTest]
        public IEnumerator A_process_that_ended_normally_ends_once() => Live(async () =>
        {
            var probe = World.MonoProbe("Ended", EProcessLifecycle.Synchronous);
            await Frames.Step();

            probe.ProcessRelay.TerminateImmediate();
            await Frames.Step(2);

            Assert.That(probe.Recorder[ProbeHook.Terminate], Is.EqualTo(1));
        });

        /// <summary>
        /// Collecting the scene's processes gave each one GameRoot's packet, which reparents whatever it's
        /// handed, so a scene's hierarchy was flattened under GameRoot at startup.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_20_A_scene_process_stays_where_the_scene_put_it() => Live(async () =>
        {
            var parent = new GameObject("Parent");
            var host = new GameObject("Child");
            host.transform.SetParent(parent.transform);

            var probe = host.AddComponent<ProbeMonoProcess>();
            probe.ProcessLifecycle = EProcessLifecycle.Synchronous;
            probe.ProcessTiming = EProcessStepTiming.Update;

            World.Control.CollectSceneProcesses();
            World.Own(probe.ProcessRelay);
            await Frames.Step();

            Assert.That(probe.IsInitialized, "it was collected");
            Assert.That(host.transform.parent, Is.EqualTo(parent.transform), "and it's still where it was");

            Object.Destroy(parent);
            await Frames.Step();
        });

        /// <summary>GameRoot went with the first scene, and every overload with no handler uses it.</summary>
        [UnityTest]
        public IEnumerator PC_21_GameRoot_survives_a_scene_change() => Live(async () =>
        {
            await World.ChangeScene();
            await Frames.Step();

            Assert.That(GameRoot.Instance != null, "it's still there");

            var probe = World.NewProbe("After The Load", EProcessLifecycle.Synchronous);
            Assert.That(ProcessControl.Register(probe, out var relay), "and the default overloads work");
            World.Own(relay);
            await Frames.Step();

            Assert.That(probe.Recorder[ProbeHook.Update], Is.GreaterThan(0));
        });
    }
}
