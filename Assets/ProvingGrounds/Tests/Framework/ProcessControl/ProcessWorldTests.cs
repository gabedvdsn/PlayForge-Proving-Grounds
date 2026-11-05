using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A world of processes of its own (FEAT-10, the headless split): ProcessControl's scheduling as a plain class, which a
    /// scene's ProcessControl wraps and steps, or which code owns and steps itself. The scheduling was the MonoBehaviour
    /// itself, so no process could run without a ProcessControl in the scene, stepped by Unity's loop.
    /// </summary>
    public class ProcessWorldTests : ProvingGroundsTest
    {
        /// <summary>
        /// A world built with new, with no ProcessControl, takes a runtime process and steps it each time its owner steps the
        /// world, and only then. The scene's ProcessControl never holds it.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_10_A_world_of_its_own_steps_what_it_holds_as_its_owner_steps_it() => Live(async () =>
        {
            var world = new ProcessWorld();
            Assert.IsNull(world.Control, "a world of its own has no ProcessControl");

            var probe = World.NewProbe("Headless");
            var result = ProcessControl.Register(probe).In(world).Run();
            Assert.IsTrue(result, "the world takes the process");
            var relay = result.Relay;
            Assert.AreSame(world, relay.World, "its relay knows its world");
            Assert.IsFalse(World.Control.TryGetPCB(relay.CacheIndex, out var held) && held.Relay == relay, "and the scene's control doesn't hold it");

            await Frames.Step(3);
            Assert.AreEqual(0, probe.Recorder[ProbeHook.Update], "the scene's frames don't step it");

            world.Step();
            world.Step();
            int after = probe.Recorder[ProbeHook.Update];
            Assert.That(after, Is.GreaterThan(0), "its owner's steps do");
            for (int i = 0; i < 3; i++) world.Step();
            Assert.AreEqual(after + 3, probe.Recorder[ProbeHook.Update], "once each");

            await Frames.Step(2);
            Assert.AreEqual(after + 3, probe.Recorder[ProbeHook.Update], "and only then");

            Assert.IsTrue(relay.TerminateImmediate(), "its relay ends it in its world");
            world.Step();
            await Frames.Step(2);
            Assert.AreEqual(1, probe.Recorder[ProbeHook.Terminate], "and it ends");
        });

        /// <summary>
        /// A world of its own keeps its own time (FEAT-10, step 3): its owner steps it by a time it gives, and a sequence's
        /// delay in it runs out by those steps, within one frame. Its waits were Unity's next frame, so a world stepped in a
        /// loop got nowhere until frames passed.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_10_A_world_of_its_own_keeps_its_own_time() => Live(async () =>
        {
            var world = new ProcessWorld();
            bool done = false;
            var sequence = TaskSequenceBuilder.Create("FEAT-10 Delay")
                .Stage(stage => stage.Delay(1f))
                .Stage(stage => stage.Task(_ => done = true))
                .BuildSequence();
            var result = ProcessControl.Register(sequence).In(world).Run();
            Assert.IsTrue(result, "the world takes a sequence");

            int frame = UnityEngine.Time.frameCount;
            int steps = 0;
            while (!done && steps < 200)
            {
                world.Step(1f / 60f);
                steps++;
            }

            Assert.AreEqual(frame, UnityEngine.Time.frameCount, "all within one frame");
            Assert.IsTrue(done, "its delay runs out by the world's own steps");
            Assert.That(steps, Is.InRange(58, 75), "after the steps its second needs");
            Assert.AreEqual(steps / 60f, world.Elapsed, 1e-3f, "which its clock counts");
            result.Relay.TerminateImmediate();
            await Frames.Step();
        });
    }
}
