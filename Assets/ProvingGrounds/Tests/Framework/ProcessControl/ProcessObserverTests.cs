using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ProcessObserverTests : ProvingGroundsTest
    {
        /// <summary>The report was built after the state had changed, so old and new were always the same.</summary>
        [UnityTest]
        public IEnumerator PC_13_A_state_change_reports_where_it_came_from() => Live(async () =>
        {
            var seen = new List<(EProcessState from, EProcessState to)>();
            var probe = World.NewProbe("Reported", EProcessLifecycle.RunThenWait, runFrames: 1);

            ProcessControlCallbacks.ProcessStateDelegate listener = (relay, from, to, _) =>
            {
                if (relay.Wrapper.ProcessName == "Reported") seen.Add((from, to));
            };

            World.Control.Callbacks.OnProcessStateChanged += listener;
            World.Register(probe);
            await Frames.Step(3);
            World.Control.Callbacks.OnProcessStateChanged -= listener;

            Assert.That(seen, Is.EqualTo(new[]
            {
                (EProcessState.Created, EProcessState.Running),
                (EProcessState.Running, EProcessState.Waiting),
            }));
        });

        /// <summary>A run that ends at once changes state twice, and each change was reported once, not one twice.</summary>
        [UnityTest]
        public IEnumerator PC_13_A_run_that_ends_at_once_reports_each_change_once() => Live(async () =>
        {
            var seen = new List<(EProcessState from, EProcessState to)>();
            var probe = World.NewProbe("Instant", EProcessLifecycle.SelfTerminating, runFrames: 0);

            ProcessControlCallbacks.ProcessStateDelegate listener = (relay, from, to, _) =>
            {
                if (relay.Wrapper.ProcessName == "Instant") seen.Add((from, to));
            };

            World.Control.Callbacks.OnProcessStateChanged += listener;
            World.Register(probe);
            await Frames.Step(2);
            World.Control.Callbacks.OnProcessStateChanged -= listener;

            Assert.That(seen, Is.EqualTo(new[]
            {
                (EProcessState.Created, EProcessState.Running),
                (EProcessState.Running, EProcessState.Terminated),
            }));
        });

        /// <summary>Terminating took the watchers off before they were told, so none ever saw Terminated.</summary>
        [UnityTest]
        public IEnumerator PC_13_A_watcher_sees_the_process_terminate() => Live(async () =>
        {
            var probe = World.Probe("Watched", EProcessLifecycle.Synchronous);
            var seen = new List<EProcessState>();
            World.Control.WatchProcess(probe.Relay, this, onStateChange: (_, to, _) => seen.Add(to));
            await Frames.Step();

            probe.Relay.TerminateImmediate();
            await Frames.Step();

            Assert.That(seen, Does.Contain(EProcessState.Terminated));
        });

        /// <summary>The event says it fires before the removal, and it fired after.</summary>
        [UnityTest]
        public IEnumerator PC_13_Unregistered_fires_before_the_process_goes() => Live(async () =>
        {
            var probe = World.Probe("Leaving", EProcessLifecycle.Synchronous);
            bool stillThere = false;

            ProcessControlCallbacks.ProcessDelegate listener = (relay, _) =>
            {
                if (relay == probe.Relay) stillThere = World.Control.IsRegistered(relay.CacheIndex);
            };

            World.Control.Callbacks.OnProcessUnregistered += listener;
            probe.Relay.TerminateImmediate();
            World.Control.Callbacks.OnProcessUnregistered -= listener;

            await Frames.Step();

            Assert.That(stillThere, "it could still be looked up");
            Assert.That(World.Control.IsRegistered(probe.Relay.CacheIndex), Is.False, "and then it was gone");
        });

        /// <summary>The first watcher returned before calling back, so only later ones heard they'd started.</summary>
        [UnityTest]
        public IEnumerator PC_14_The_first_watcher_is_told_it_has_started() => Live(async () =>
        {
            var probe = World.Probe("Watched", EProcessLifecycle.Synchronous);
            bool told = false;

            World.Control.WatchProcess(probe.Relay, this, onStartWatching: _ => told = true);

            await Frames.Step();
            Assert.That(told);
        });

        /// <summary>A second subscription edited a copy of the first, so it never took.</summary>
        [UnityTest]
        public IEnumerator PC_14_One_watcher_can_add_a_second_callback() => Live(async () =>
        {
            var probe = World.Probe("Watched", EProcessLifecycle.Synchronous);
            int calls = 0;

            World.Control.WatchProcess(probe.Relay, this, onUnregister: _ => calls++);
            World.Control.WatchProcess(probe.Relay, this, onUnregister: _ => calls++);
            await Frames.Step();

            probe.Relay.TerminateImmediate();
            await Frames.Step();

            Assert.That(calls, Is.EqualTo(2));
        });

        /// <summary>Unwatching from inside a callback changed the collection it was being walked from.</summary>
        [UnityTest]
        public IEnumerator PC_14_A_watcher_can_unwatch_from_its_own_callback() => Live(async () =>
        {
            var probe = World.Probe("Watched", EProcessLifecycle.Synchronous);
            var other = new object();
            int otherCalls = 0;

            World.Control.WatchProcess(probe.Relay, this, onStateChange: (_, _, _) => World.Control.StopWatchingProcess(probe.Relay, this));
            World.Control.WatchProcess(probe.Relay, other, onStateChange: (_, _, _) => otherCalls++);
            await Frames.Step();

            Assert.DoesNotThrow(() => probe.Relay.Pause());
            await Frames.Step();

            Assert.That(otherCalls, Is.EqualTo(1), "the other watcher still heard");
            probe.Relay.Unpause();
            await Frames.Step();
        });
    }
}
