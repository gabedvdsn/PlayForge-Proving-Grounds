using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Register builder (FEAT-15, Decision 30): one fluent registration in place of ProcessControl's 28 static
    /// overloads. It says why it refuses a process, hands a process to its packet's source by the packet's run-time type,
    /// and its relay runs a process that waits. The overloads, obsolete wrappers of it since, went with the legacy code
    /// (FN-21).
    /// </summary>
    public class RegisterBuilderTests : ProvingGroundsTest
    {
        private static TaskSequence LongSequence(string name) => new(TaskSequenceBuilder.Create(name).Delay(100f).Build());

        // An ability's packet, from an ability the entity holds, so its source is the entity
        private AbilityDataPacket AbilityPacket(IGameplayAbilitySystem gas)
        {
            var abilities = gas.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(Make.Ability("Source", Make.Stage()), 1, out int index), "the entity has an ability");
            var container = abilities.GetAbilityContainers().Single(each => each.Index == index);
            return AbilityDataPacket.GenerateFrom(container, abilities);
        }

        /// <summary>The builder registers a process and starts it, as the overloads did, and says it did.</summary>
        [UnityTest]
        public IEnumerator FEAT_15_The_builder_registers_a_process_and_starts_it() => Live(async () =>
        {
            var probe = World.NewProbe("Built");
            var result = ProcessControl.Register(probe).Run();
            Assert.IsTrue(result, "it registers");
            Assert.AreEqual(EProcessRegistrationRefusal.None, result.Refusal, "with nothing refused");
            Assert.IsNotNull(result.Relay, "and a relay to hold");
            try
            {
                await Frames.Step();
                Assert.AreEqual(EProcessState.Running, result.Relay.State, "and starts it");
                Assert.AreSame(GameRoot.Instance, result.Relay.Handler, "under GameRoot, with no packet to say otherwise");
            }
            finally
            {
                result.Relay.TerminateImmediate();
            }
        });

        /// <summary>
        /// A refused call returned false and a null relay, and said nothing of why, so a caller that dropped the result
        /// never knew (FEAT-15). A refused registration says why.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_15_A_refused_registration_says_why() => Live(async () =>
        {
            Assert.AreEqual(EProcessRegistrationRefusal.NoProcess, ProcessControl.Register((AbstractRuntimeProcess)null).Run().Refusal,
                "no process");

            var probe = World.Probe("Taken");
            var again = ProcessControl.Register(probe).Run();
            Assert.IsFalse(again, "a process registered already is refused");
            Assert.AreEqual(EProcessRegistrationRefusal.AlreadyRegistered, again.Refusal, "as registered already");
            Assert.IsNull(again.Relay, "with no relay");

            Assert.AreEqual(EProcessRegistrationRefusal.NotASequencePacket,
                ProcessControl.Register(LongSequence("Wrong Packet")).With(ProcessDataPacket.Default()).Run().Refusal,
                "a sequence with a packet that isn't a sequence's");

            World.Control.SetControlState(EProcessControlState.Closed);
            try
            {
                Assert.AreEqual(EProcessRegistrationRefusal.ProcessControlClosed, ProcessControl.Register(World.NewProbe("Late")).Run().Refusal,
                    "ProcessControl closed");
            }
            finally
            {
                World.Control.SetControlState(EProcessControlState.Ready);
            }

            var control = ProcessControl.Instance;
            ProcessControl.Instance = null;
            try
            {
                Assert.AreEqual(EProcessRegistrationRefusal.NoProcessControl, ProcessControl.Register(World.NewProbe("Nowhere")).Run().Refusal,
                    "and no ProcessControl, which the overloads threw on");
            }
            finally
            {
                ProcessControl.Instance = control;
            }
            await Frames.Step();
        });

        /// <summary>
        /// The argument's static type picked the handler: an ability's packet passed as a sequence's filed its sequence
        /// under GameRoot, so the sequence didn't end with its entity (FEAT-15, GAS-52), as TaskSequence.RegisterAndRun did.
        /// The handler follows the packet's run-time type, through the builder and RegisterAndRun alike.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_15_The_handler_follows_the_packets_run_time_type([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, name: "Source");
            SequenceDataPacket packet = AbilityPacket(gas);
            await Frames.Step();

            var built = ProcessControl.Register(LongSequence("Built")).With(packet).Run();
            Assert.IsTrue(built, "the builder registers the sequence");
            Assert.AreSame(gas, built.Relay.Handler, "under the packet's source, the entity");

            var run = LongSequence("Run").RegisterAndRun(packet);
            Assert.AreSame(gas, run.Handler, "as TaskSequence.RegisterAndRun does");

            await Frames.Step(2);

            var relay = gas is AbstractMonoProcess mono ? mono.ProcessRelay : ((AbstractRuntimeProcess)gas).ProcessRelay;
            relay.TerminateImmediate();
            await Frames.Step(3);
            foreach (var each in new[] { built.Relay, run })
                Assert.IsFalse(World.Control.TryGetPCB(each.CacheIndex, out _), "and each ends with its entity (GAS-52)");
        });

        /// <summary>HandledBy and With set the handler and the packet; a handler given as null means none at all.</summary>
        [UnityTest]
        public IEnumerator FEAT_15_HandledBy_and_With_set_the_handler_and_packet([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, name: "Handler");
            var data = ProcessDataPacket.Default();
            var handled = ProcessControl.Register(World.NewProbe("Handled")).HandledBy(gas).With(data).Run();
            var unhandled = ProcessControl.Register(World.NewProbe("Unhandled")).HandledBy(null).Run();
            try
            {
                Assert.AreSame(gas, handled.Relay.Handler, "the handler given");
                Assert.AreSame(data, handled.Relay.Wrapper.Data, "and the packet");
                Assert.IsNull(unhandled.Relay.Handler, "and none, given none");
                await Frames.Step();
            }
            finally
            {
                handled.Relay?.TerminateImmediate();
                unhandled.Relay?.TerminateImmediate();
            }
        });

        /// <summary>
        /// ProcessControl could run a waiting process by its index, but the relay that holds it had no Run, as the review
        /// paired with a builder (FEAT-15). A process that waits to be told, as RequiresControl does, runs from its relay.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_15_A_waiting_process_runs_from_its_relay() => Live(async () =>
        {
            var probe = World.NewProbe("Told", EProcessLifecycle.RequiresControl);
            var result = ProcessControl.Register(probe).Run();
            try
            {
                await Frames.Step();
                Assert.AreEqual(EProcessState.Waiting, result.Relay.State, "a process that requires control waits");

                Assert.IsTrue(result.Relay.Run(), "its relay runs it");
                await Frames.Step();
                Assert.AreEqual(EProcessState.Running, result.Relay.State, "and it runs");
            }
            finally
            {
                result.Relay?.TerminateImmediate();
            }
        });
    }
}
