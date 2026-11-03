using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ProcessHierarchyTests : ProvingGroundsTest
    {
        /// <summary>
        /// The hierarchy filed a process by its transform's parent when it registered, before WhenInitialize applied
        /// the parent its data asked for, so a process parented through its data was no one's child, and pausing its
        /// parent passed it by.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_28_A_process_parented_through_its_data_is_its_parents_child() => Live(async () =>
        {
            var parent = World.MonoProbe("Parent");
            var child = World.Spawn(World.MonoProbeTemplate("Child"), ProcessDataPacket.SceneLocal(parent.transform));
            await Frames.Step();

            parent.ProcessRelay.Pause();
            int updates = child.Recorder[ProbeHook.Update];
            await Frames.Step(2);

            Assert.That(child.Recorder[ProbeHook.Update], Is.EqualTo(updates), "pausing the parent paused the child");
        });

        /// <summary>The hierarchy never heard of a later move either, so a process moved under another stayed where it was.</summary>
        [UnityTest]
        public IEnumerator PC_28_A_moved_process_follows_its_transform() => Live(async () =>
        {
            var first = World.MonoProbe("First");
            var second = World.MonoProbe("Second");
            var moved = World.MonoProbe("Moved");

            moved.transform.SetParent(first.transform);
            await Frames.Step();
            Assert.That(Children(first), Has.Member(moved.ProcessRelay.CacheIndex), "the first's child");

            moved.transform.SetParent(second.transform);
            await Frames.Step();
            Assert.That(Children(second), Has.Member(moved.ProcessRelay.CacheIndex), "then the second's");
            Assert.That(Children(first), Has.No.Member(moved.ProcessRelay.CacheIndex), "and the first's no longer");
        });

        /// <summary>
        /// Decision 11: the hierarchy follows transforms everywhere, GameRoot included, so a process spawned at
        /// SceneRoot is GameRoot's child, and ending or pausing GameRoot reaches it.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_28_A_SceneRoot_process_is_GameRoots_child() => Live(async () =>
        {
            var spawned = World.Spawn(World.MonoProbeTemplate("Spawned"), ProcessDataPacket.SceneRoot());
            await Frames.Step();

            Assert.That(World.Control.GetChildProcessIds(World.Root.ProcessRelay.CacheIndex), Has.Member(spawned.ProcessRelay.CacheIndex));
        });

        private List<int> Children(ProbeMonoProcess probe) => World.Control.GetChildProcessIds(probe.ProcessRelay.CacheIndex);
    }
}
