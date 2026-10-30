using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ProcessRegistrationTests : ProvingGroundsTest
    {
        /// <summary>
        /// WhenInitialize runs with the block already in place, so a throw there left a process half registered,
        /// and the exception went to whoever called Register. It's refused now, and nothing of it stays.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_15_A_throw_in_WhenInitialize_refuses_the_process() => Live(async () =>
        {
            var probe = World.NewProbe("Broken Start", EProcessLifecycle.Synchronous);
            Faults.Arm(probe.Recorder.Site(ProbeHook.Initialize));
            int active = World.Control.Active;

            bool registered = true;
            ProcessRelay relay = null;
            Assert.DoesNotThrow(() => registered = ProcessControl.Register(probe, ProcessDataPacket.Default(), out relay));

            Assert.That(registered, Is.False, "it's refused");
            Assert.That(relay, Is.Null, "with no relay to hold");
            Assert.That(World.Control.Active, Is.EqualTo(active), "and nothing of it stays");

            await Frames.Step(2);

            Assert.That(probe.Recorder[ProbeHook.Update], Is.EqualTo(0), "nothing steps it");
        });

        /// <summary>A packet heard about every process it served and never let go of one.</summary>
        [UnityTest]
        public IEnumerator PC_16_A_packet_lets_go_of_a_process_that_ends() => Live(async () =>
        {
            var data = ProcessDataPacket.Default();
            var relay = World.Register(World.NewProbe("Served"), data);
            await Frames.Step();

            Assert.That(data.GetRelays(), Has.Length.EqualTo(1), "it holds the running process");

            relay.TerminateImmediate();
            await Frames.Step();

            Assert.That(data.GetRelays(), Is.Empty, "and lets it go");
        });

        /// <summary>A Lazy process never marked itself initialized, so one instance could be running twice over.</summary>
        [UnityTest]
        public IEnumerator PC_18_One_instance_cant_be_registered_twice_at_once() => Live(async () =>
        {
            var process = new LazyRuntimeProcess("Lazy", EProcessStepPriorityMethod.Manual, 0, EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating);
            World.Register(process);
            await Frames.Step();

            Assert.That(ProcessControl.Register(process, ProcessDataPacket.Default(), out _), Is.False, "it's already running");
        });

        /// <summary>Initialized means registered, so a process that's ended can be run again, as RunProcessTask does.</summary>
        [UnityTest]
        public IEnumerator A_process_that_has_ended_can_be_registered_again([Values] bool lazy) => Live(async () =>
        {
            AbstractRuntimeProcess process = lazy
                ? new LazyRuntimeProcess("Lazy", EProcessStepPriorityMethod.Manual, 0, EProcessStepTiming.Update, EProcessLifecycle.SelfTerminating)
                : World.NewProbe("Again");

            var first = World.Register(process);
            await Frames.Step();

            first.TerminateImmediate();
            await Frames.Step();

            var second = World.Register(process);
            await Frames.Step();

            Assert.That(second.IsRunning, "it's running again");
        });

        /// <summary>
        /// The short constructors left the step timing at None, so the action never ran, and the long generic
        /// one had nowhere to take a source from.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_19_An_action_process_runs_its_action() => Live(async () =>
        {
            int plain = 0;
            var counter = new Counter();
            World.Register(new ActionProcess(() => plain++));
            World.Register(new ActionProcess<Counter>(c => c.Count++, counter));

            await Frames.Step(3);

            Assert.That(plain, Is.GreaterThan(0), "the plain one ran");
            Assert.That(counter.Count, Is.GreaterThan(0), "and so did the one with a source");
        });

        private sealed class Counter
        {
            public int Count;
        }

        /// <summary>Priorities were worked out and then ignored: processes stepped in the order they arrived.</summary>
        [UnityTest]
        public IEnumerator PC_22_Processes_step_in_priority_order() => Live(async () =>
        {
            var order = new List<string>();

            Noted(World.Probe("Third", EProcessLifecycle.Synchronous, priority: 20), order);
            Noted(World.Probe("First", EProcessLifecycle.Synchronous, priority: -5), order);
            Noted(World.Probe("Second", EProcessLifecycle.Synchronous, priority: 10), order);

            await Frames.Step();

            Assert.That(order, Is.EqualTo(new[] { "First", "Second", "Third" }));
        });

        /// <summary>First and Last are priorities as well, and a process keeps its place through a pause.</summary>
        [UnityTest]
        public IEnumerator PC_22_First_and_Last_go_first_and_last() => Live(async () =>
        {
            var order = new List<string>();

            World.Register(new ActionProcess("Last", EProcessStepPriorityMethod.Last, 0, EProcessStepTiming.Update,
                EProcessLifecycle.Synchronous, () => order.Add("Last")));
            var middle = World.Probe("Middle", EProcessLifecycle.Synchronous, priority: 10);
            Noted(middle, order);
            World.Register(new ActionProcess("First", EProcessStepPriorityMethod.First, 0, EProcessStepTiming.Update,
                EProcessLifecycle.Synchronous, () => order.Add("First")));

            middle.Relay.Pause();
            await Frames.Step();
            middle.Relay.Unpause();
            order.Clear();

            await Frames.Step();

            Assert.That(order, Is.EqualTo(new[] { "First", "Middle", "Last" }));
        });

        /// <summary>Two of one priority step in the order they arrived.</summary>
        [UnityTest]
        public IEnumerator Equal_priorities_step_in_order_of_arrival() => Live(async () =>
        {
            var order = new List<string>();

            Noted(World.Probe("Earlier", EProcessLifecycle.Synchronous, priority: 5), order);
            Noted(World.Probe("Later", EProcessLifecycle.Synchronous, priority: 5), order);

            await Frames.Step();

            Assert.That(order, Is.EqualTo(new[] { "Earlier", "Later" }));
        });

        private static void Noted(ProbeProcess probe, List<string> order)
        {
            probe.Recorder.OnHit = hook =>
            {
                if (hook == ProbeHook.Update) order.Add(probe.Recorder.Name);
            };
        }
    }
}
