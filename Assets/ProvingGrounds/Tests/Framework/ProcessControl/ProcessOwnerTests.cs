using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A process belongs to the control that holds it (FEAT-10, the headless split's first step). Its relay, its block's
    /// state changes, a watch on it and the processes it handles reached ProcessControl.Instance, whichever control held
    /// it, so once another control was Instance, as a new scene's is while the old one goes, they reached the wrong one.
    /// </summary>
    public class ProcessOwnerTests : ProvingGroundsTest
    {
        // Another ProcessControl, Instance for the length of a check, as a new scene's would be. It's inactive, so it
        // steps nothing
        private sealed class OtherControl : IDisposable
        {
            private readonly ProcessControl held = ProcessControl.Instance;
            private readonly GameObject host;

            public OtherControl()
            {
                host = new GameObject("FEAT-10 Other Control");
                host.SetActive(false);
                ProcessControl.Instance = host.AddComponent<ProcessControl>();
            }

            public void Dispose()
            {
                ProcessControl.Instance = held;
                Object.Destroy(host);
            }
        }

        /// <summary>A relay finds, pauses, unpauses and terminates its process where it's held, whichever control is Instance.</summary>
        [UnityTest]
        public IEnumerator FEAT_10_A_relay_reaches_the_control_that_holds_its_process() => Live(async () =>
        {
            var relay = World.Probe("Held").Relay;
            await Frames.Step();
            var held = World.Control;
            Assert.IsTrue(held.IsRegistered(relay.CacheIndex), "the scene's control holds the process");

            using (new OtherControl())
            {
                Assert.IsTrue(relay.ProcessActive, "its relay finds it there");
                Assert.IsTrue(relay.Pause(), "pauses it there");
                Assert.IsTrue(relay.Unpause(), "unpauses it there");
                Assert.IsTrue(relay.Terminate(), "and terminates it there");
            }

            await Frames.Until(() => !held.IsRegistered(relay.CacheIndex), 10, "and it leaves the control that held it");
        });

        /// <summary>A watch on a process sits with the control that holds it, so the process's state changes reach it.</summary>
        [UnityTest]
        public IEnumerator FEAT_10_A_watch_sits_with_the_control_that_holds_the_process() => Live(async () =>
        {
            var relay = World.Probe("Watched").Relay;
            await Frames.Step();
            var held = World.Control;
            int changes = 0;

            using (new OtherControl())
            {
                held.WatchProcess(relay, this, onStateChange: (_, _, _) => changes++);
            }

            Assert.IsTrue(relay.Pause(), "the process pauses");
            await Frames.Step(2);
            Assert.That(changes, Is.GreaterThan(0), "and its watcher hears it");
            held.StopWatchingProcess(relay, this);
        });

        /// <summary>
        /// A runtime entity that ends while another control is Instance ends its abilities where they're held, as it does
        /// whatever control is Instance (GAS-52).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_10_An_ending_runtime_entity_ends_its_abilities_where_they_are_held() => Live(async () =>
        {
            var ability = Make.Ability("Held Work", Make.Stage(new ProbeAbilityTask(World, "Long Work", frames: 100000)));
            var gas = World.Entity(Hub.Runtime, name: "Ending");
            var abilities = gas.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(ability, 1, out int index), "granted");
            await Frames.Step();

            int ended = 0;
            abilities.Callbacks.OnAbilityEnd += _ => ended++;
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "activated");
            await Frames.Step(2);
            Assert.AreEqual(0, ended, "it's running");

            var relay = ((AbstractRuntimeProcess)gas).Relay;
            using (new OtherControl())
            {
                Assert.IsTrue(relay.TerminateImmediate(), "the entity ends where it's held");
            }

            await Frames.Step(3);
            Assert.AreEqual(1, ended, "and its ability ends with it");
        });
    }
}
