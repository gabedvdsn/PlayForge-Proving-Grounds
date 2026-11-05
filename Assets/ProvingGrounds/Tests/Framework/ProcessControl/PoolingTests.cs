using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Pooled spawns, which the Bullet heaven lab needs. A pool extends <see cref="AbstractMonoProcessInstantiator"/>
    /// (Decision 23): it makes or hands out an object as a process starts, and takes it back as the process ends.
    /// </summary>
    public class PoolingTests : ProvingGroundsTest
    {
        // A pool as a game writes one: an idle object if it has one, a new clone if not, and each taken back as its
        // process ends
        private sealed class Pool : AbstractMonoProcessInstantiator
        {
            public readonly Stack<AbstractMonoProcess> Idle = new();
            public int Made, Cleaned;

            protected override AbstractMonoProcess PrepareNew(AbstractMonoProcess template, ProcessDataPacket data)
            {
                if (Idle.Count > 0)
                {
                    var reused = Idle.Pop();
                    reused.gameObject.SetActive(true);
                    return reused;
                }
                Made++;
                return Object.Instantiate(template);
            }

            protected override AbstractMonoProcess PrepareExisting(AbstractMonoProcess process, ProcessDataPacket data) => process;

            public override void CleanProcess(AbstractMonoProcess process)
            {
                Cleaned++;
                process.gameObject.SetActive(false);
                Idle.Push(process);
            }
        }

        // Spawns a bolt through the pool: as the template's own instantiator, or as the handler's
        private ProbeMonoProcess Spawn(ProbeMonoProcess template, Pool pool, bool throughHandler)
        {
            if (!throughHandler) return World.Spawn(template);

            var handler = ProcessDataPacket.Default();
            handler.CustomInstantiator = pool;
            var registration = ProcessControl.Register(template).HandledBy(handler).With(ProcessDataPacket.Default()).Run();
            Assert.IsTrue(registration, "the bolt registers");
            World.Own(registration.Relay);
            Assert.IsTrue(registration.Relay.TryGetProcess<ProbeMonoProcess>(out var bolt), "as a bolt");
            return bolt;
        }

        /// <summary>
        /// The wrapper handed a finished object back through the object's own instantiator, which a clone never has, as
        /// it isn't serialized: so a pool's object was destroyed as its process ended, and the pool never saw it again
        /// (PC-33). It goes back to the instantiator that made it, and the next spawn reuses it.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_33_A_pooled_object_goes_back_to_its_pool([Values] bool throughHandler) => Live(async () =>
        {
            var pool = new Pool();
            var template = World.MonoProbeTemplate("Bolt", runFrames: 2);
            if (throughHandler) template.UseHandlerInstantiator = true;
            else template.Instantiator = pool;

            var first = Spawn(template, pool, throughHandler);
            Assert.AreEqual(1, pool.Made, "the pool makes the first bolt");
            await Frames.Until(() => pool.Cleaned == 1, 10, "and takes it back as its process ends");
            await Frames.Step();
            Assert.IsTrue(first != null, "rather than its being destroyed");

            var second = Spawn(template, pool, throughHandler);
            Assert.AreSame(first, second, "the next bolt is the same object");
            Assert.AreEqual(1, pool.Made, "so the pool made only the one");
            await Frames.Until(() => pool.Cleaned == 2, 10, "and it goes back again");
        });
    }
}
