using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// ProcessControl without the ability system (EP-84), as the core edition runs it. It counted a process under an
    /// IGameplayAbilitySystem, its handler or an ability packet's caster, took its clock and its place from one, and handed
    /// a process naming no handler to GameRoot, so it couldn't compile without the GAS. It counts a process under any
    /// <see cref="IProcessOwner"/>, its handler or its packet's <see cref="ProcessDataPacket.Source"/>, and hands one naming
    /// no handler to ProcessControl's default, the GameRoot where there is one, and none where there isn't.
    /// </summary>
    public class OwnedProcessTests : ProvingGroundsTest
    {
        // A handler that's no owner
        private class Handler : IGameplayProcessHandler
        {
            private readonly List<ProcessRelay> relays = new();

            public ProcessRelay[] GetRelays() => relays.ToArray();
            public bool HandlerValidateAgainst(IGameplayProcessHandler handler) => ReferenceEquals(handler, this);
            public bool HandlerProcessIsSubscribed(ProcessRelay relay) => relays.Contains(relay);
            public void HandlerSubscribeProcess(ProcessRelay relay) => relays.Add(relay);
            public bool HandlerVoidProcess(ProcessRelay relay) => relays.Remove(relay);
            public AbstractMonoProcessInstantiator GetInstantiator(AbstractMonoProcess mono) => null;
            public string GetName() => GetType().Name;
            public string GetDescription() => "";
            public Texture2D GetDefaultIcon() => null;
        }

        // An owner that's no entity, with a place, a heading and a clock of its own
        private sealed class Owner : Handler, IProcessOwner
        {
            public IForgeClock Clock;
            public Vector3 At;
            public Quaternion Facing = Quaternion.identity;

            public IForgeClock ProcessClock => Clock;
            public Vector3 ProcessPosition => At;
            public Quaternion ProcessRotation => Facing;
        }

        // A packet whose processes come from a source, as an ability's come from its caster
        private sealed class SourcedPacket : ProcessDataPacket
        {
            private readonly IGameplayProcessHandler source;

            public SourcedPacket(IGameplayProcessHandler source) => this.source = source;

            public override IGameplayProcessHandler Source => source;
        }

        /// <summary>
        /// An owner that's no entity owns the processes it handles: ProcessControl counts them under it, ends them for it,
        /// and its callbacks name it as each comes and goes.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_84_An_owner_that_is_no_entity_owns_what_it_handles() => Live(async () =>
        {
            var owner = new Owner();
            IProcessOwner registered = null, unregistered = null;
            void Registered(IProcessOwner of, ProcessRelay relay, ProcessDataPacket data) => registered = of;
            void Unregistered(IProcessOwner of, ProcessRelay relay, ProcessDataPacket data) => unregistered = of;
            World.Control.Callbacks.OnOwnedProcessRegistered += Registered;
            World.Control.Callbacks.OnOwnedProcessUnregistered += Unregistered;

            var result = ProcessControl.Register(World.NewProbe("Owned")).HandledBy(owner).Run();
            try
            {
                Assert.IsTrue(result, "it registers");
                Assert.AreSame(owner, registered, "its callback names its owner");
                Assert.That(ProcessControl.GetProcessesFor(owner), Is.EquivalentTo(new[] { result.Relay }), "which owns it");
                Assert.That(ProcessControl.GetProcessIdsFor(owner), Is.EquivalentTo(new[] { result.Relay.CacheIndex }), "by its cache index");
                Assert.AreEqual(1, ProcessControl.GetProcessCountFor(owner), "and it alone");

                Assert.AreEqual(1, ProcessControl.TerminateAllFor(owner, immediate: true), "ending what the owner owns ends it");
                await Frames.Step();
                Assert.AreSame(owner, unregistered, "and its callback names its owner as it goes");
                Assert.AreEqual(0, ProcessControl.GetProcessCountFor(owner), "so the owner owns nothing");
            }
            finally
            {
                World.Control.Callbacks.OnOwnedProcessRegistered -= Registered;
                World.Control.Callbacks.OnOwnedProcessUnregistered -= Unregistered;
                if (result.Relay is { ProcessActive: true }) result.Relay.TerminateImmediate();
            }
        });

        /// <summary>
        /// A packet's source takes a process registered with it and naming no handler, owns it even under a handler that's
        /// no owner, and the work done with the packet runs by its clock.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_84_A_packets_source_owns_its_processes_and_gives_its_clock() => Live(async () =>
        {
            var owner = new Owner { Clock = new ScaledClock(0.5f) };
            var packet = new SourcedPacket(owner);
            Assert.AreSame(owner.Clock, ForgeTime.ClockFor(packet), "the work done with the packet runs by its source's clock");

            var sourced = ProcessControl.Register(World.NewProbe("Sourced")).With(packet).Run();
            var handled = ProcessControl.Register(World.NewProbe("Handled")).HandledBy(new Handler()).With(new SourcedPacket(owner)).Run();
            try
            {
                Assert.IsTrue(sourced, "a process naming no handler registers");
                Assert.IsTrue(handled, "and one under a handler");
                Assert.AreSame(owner, sourced.Relay.Handler, "naming no handler, a process goes to its packet's source");
                Assert.That(ProcessControl.GetProcessesFor(owner), Is.EquivalentTo(new[] { sourced.Relay, handled.Relay }),
                    "which owns it, and the one under a handler that's no owner");
                await Frames.Step();
            }
            finally
            {
                sourced.Relay?.TerminateImmediate();
                handled.Relay?.TerminateImmediate();
            }
        });

        /// <summary>
        /// The GameRoot is ProcessControl's default handler and scene root while it's the GameRoot. Without one, a
        /// registration naming no handler has none, and a scene root's packet parents its process nowhere; it threw.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_84_Without_a_GameRoot_a_process_naming_no_handler_has_none() => Live(async () =>
        {
            Assert.AreSame(World.Root, ProcessControl.DefaultHandler, "the GameRoot is the default handler");
            Assert.AreSame(World.Root.transform, ProcessControl.DefaultParent, "and the scene root");
            Assert.IsTrue(ProcessDataPacket.SceneRoot().TryGetFirst<Transform>(Tags.TRANSFORM, out var parent), "a scene root's packet names a parent");
            Assert.AreSame(World.Root.transform, parent, "the GameRoot");

            ProcessRelay unhandled = null;
            try
            {
                GameRoot.Instance = null;
                Assert.IsNull(ProcessControl.DefaultHandler, "with no GameRoot, no default handler");
                Assert.IsNull(ProcessControl.DefaultParent, "and no scene root");
                Assert.DoesNotThrow(() => ProcessDataPacket.SceneRoot(), "a scene root's packet is made all the same");
                Assert.DoesNotThrow(() => SequenceDataPacket.SceneRoot(), "a sequence's too");

                Assert.IsTrue(ProcessControl.Register(World.NewProbe("Unhandled")).Run(out unhandled), "a process registers");
                Assert.IsNull(unhandled.Handler, "with no handler");
                await Frames.Step();
            }
            finally
            {
                unhandled?.TerminateImmediate();
                GameRoot.Instance = World.Root;
            }

            Assert.AreSame(World.Root, ProcessControl.DefaultHandler, "the GameRoot is the default again as it's the GameRoot again");
        });

        /// <summary>
        /// An owner in a packet's position or rotation places a MonoBehaviour process where it is and turns it its way, as
        /// an entity does.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_84_An_owner_in_a_position_payload_places_a_process_where_it_is() => Live(async () =>
        {
            var owner = new Owner { At = new Vector3(3f, 0f, 4f), Facing = Quaternion.Euler(0f, 90f, 0f) };
            var data = ProcessDataPacket.Default();
            data.AddPayload<IProcessOwner>(Tags.POSITION, owner);
            data.AddPayload<IProcessOwner>(Tags.ROTATION, owner);

            var placed = World.Spawn(World.MonoProbeTemplate("Placed"), data);
            await Frames.Step();

            Assert.That(Vector3.Distance(placed.transform.position, owner.At), Is.LessThan(1e-4f), "where the owner is");
            Assert.That(Quaternion.Angle(placed.transform.rotation, owner.Facing), Is.LessThan(0.01f), "turned its way");
        });

        /// <summary>An entity owns what it handles, and its processes run by its own clock.</summary>
        [UnityTest]
        public IEnumerator EP_84_An_entity_owns_what_it_handles_and_gives_its_clock() => Live(async () =>
        {
            var gas = World.RuntimeEntity(name: "Owner");
            Assert.AreSame(gas.GetClock(), ForgeTime.ClockOf(gas), "an entity's processes run by its clock");

            var result = ProcessControl.Register(World.NewProbe("The entity's")).HandledBy(gas).Run();
            try
            {
                Assert.IsTrue(result, "it registers");
                Assert.That(ProcessControl.GetProcessesFor(gas), Does.Contain(result.Relay), "and the entity owns it");
                await Frames.Step();
            }
            finally
            {
                result.Relay?.TerminateImmediate();
            }
        });

        /// <summary>
        /// The Process Bootstrapper brings ProcessControl up alone, for a scene of processes and task sequences with no
        /// entities, and goes: processes and sequences run with no GameRoot and no handler.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_84_The_Process_Bootstrapper_brings_up_ProcessControl_alone() => Live(async () =>
        {
            Object.Destroy(World.Control.gameObject);
            Object.Destroy(World.Root.gameObject);
            await Frames.Step(2);

            ProcessControl control = null;
            try
            {
                var host = new GameObject("Process Bootstrapper");
                host.SetActive(false);
                host.AddComponent<ProcessBootstrapper>();
                host.SetActive(true);

                control = ProcessControl.Instance;
                Assert.That((bool)control, "a live ProcessControl");
                Assert.AreEqual("ProcessControl", control.name, "named as the Bootstrapper names it");
                Assert.That(!GameRoot.Instance, "and no GameRoot");
                Assert.IsNull(ProcessControl.DefaultHandler, "so no default handler");

                var probe = World.NewProbe("Alone");
                Assert.IsTrue(ProcessControl.Register(probe).Run(out var relay), "it takes processes");
                Assert.IsNull(relay.Handler, "with no handler");

                bool ran = false;
                var sequence = new TaskSequence(TaskSequenceBuilder.Create("Alone").Do(_ => ran = true).Build());
                Assert.IsTrue(ProcessControl.Register(sequence).Run(), "and sequences");

                await Frames.Until(() => ran && probe.Recorder[ProbeHook.Update] > 0, 30, "and steps them");
                Assert.That(!host, "and the Process Bootstrapper went");
                relay.TerminateImmediate();
            }
            finally
            {
                if (control) Object.Destroy(control.gameObject);
            }
        });

        /// <summary>
        /// A packet as a handler validates against itself alone. It cast the handler to an ability's packet, so it threw for
        /// any other handler, a plain packet itself too.
        /// </summary>
        [Test]
        public void EP_84_A_packet_validates_against_itself_alone()
        {
            var packet = ProcessDataPacket.Default();
            Assert.IsTrue(packet.HandlerValidateAgainst(packet), "a packet is itself");
            Assert.IsFalse(packet.HandlerValidateAgainst(ProcessDataPacket.Default()), "and no other packet");
            Assert.IsFalse(packet.HandlerValidateAgainst(new Handler()), "nor another handler");
        }
    }
}
