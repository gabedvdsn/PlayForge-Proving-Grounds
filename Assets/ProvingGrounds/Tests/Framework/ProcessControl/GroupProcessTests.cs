using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class GroupProcessTests : ProvingGroundsTest
    {
        /// <summary>
        /// A group is Synchronous with no step timing, so nothing ever applied its queued Terminate. Any process
        /// set up that way had the same problem, and the group is where it showed.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_17_A_group_ends_with_its_last_member() => Live(async () =>
        {
            var tag = Make.NewTag("Test.Group");
            Assert.That(ProcessControl.RegisterWithGroup(tag, World.NewProbe("Member"), out var member, false), "joined");
            World.Own(member);
            Assert.That(ProcessControl.TryGetGroup(tag, out var group), "the group exists");
            await Frames.Step();

            member.TerminateImmediate();
            await Frames.Step(2);

            Assert.That(group.Relay.State, Is.EqualTo(EProcessState.Terminated));
            Assert.That(ProcessControl.HasGroup(tag), Is.False, "and is gone from the registry");
        });

        /// <summary>The general case: a queued Terminate on a process nothing steps.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_17_A_process_nothing_steps_can_still_be_terminated() => Live(async () =>
        {
            var probe = World.Probe("Unstepped", EProcessLifecycle.Synchronous, EProcessStepTiming.None);
            await Frames.Step();

            probe.Relay.Terminate();
            await Frames.Step();

            Assert.That(probe.Relay.State, Is.EqualTo(EProcessState.Terminated));
        });

        /// <summary>Detaching removed every watcher on the process, not only the group's.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_17_Detaching_a_member_leaves_its_other_watchers() => Live(async () =>
        {
            var tag = Make.NewTag("Test.Group");
            ProcessControl.RegisterWithGroup(tag, World.NewProbe("Member"), out var member, false);
            World.Own(member);
            ProcessControl.TryGetGroup(tag, out var group);
            group.TerminateWhenEmpty = false;

            int heard = 0;
            World.Control.WatchProcess(member, this, onUnregister: _ => heard++);
            await Frames.Step();

            Assert.That(group.DetachMember(member), "detached");

            member.TerminateImmediate();
            await Frames.Step();

            Assert.That(heard, Is.EqualTo(1), "the other watcher still heard");
        });
    }
}
