using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Bullet heaven's bot. It plays the lab's rounds under a live ProcessControl and the lab's clock of 1/64 s a frame,
    /// and checks what each leaves; the harness checks its invariants as each ends. Phase 5's gate is every lab green.
    /// </summary>
    [Category("Lab")]
    public class BulletHeavenLabTests : ProvingGroundsTest
    {
        // The lab's clock for the round, and the game's back after it
        private sealed class LabTime : IDisposable
        {
            private readonly IForgeClock previous = ForgeTime.Use(new HeavenClock());
            public void Dispose() => ForgeTime.Use(previous);
        }

        private float Damage(BulletHeavenLab lab, float health) => lab.Enemies.Sum(enemy => health - lab.Value(enemy));

        /// <summary>
        /// Ten thousand bolts, a hundred a frame at a horde of 500, each the framework's projectile process drawn from a
        /// pool. Every bolt lands, every one goes back to the pool, so the pool makes only as many as fly at once, and
        /// ProcessControl ends with the processes it began with.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_Ten_thousand_bolts_come_from_a_pool() => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new BulletHeavenLab(World, Make);
            try
            {
                lab.Horde(25, 20, new Vector3(4f, 0f, -10f), health: 1000f);
                await Frames.Step();
                int before = World.Control.Active;

                for (int frame = 0; frame < 100; frame++)
                {
                    for (int i = 0; i < 100; i++)
                        Assert.IsTrue(lab.Fire(lab.Enemies[(lab.Fired * 7919) % lab.Enemies.Count]), "the bolt is fired");
                    await Frames.Step();
                }
                await Frames.Until(() => lab.Pool.Out == 0, 200, "every bolt lands");
                await Frames.Step(2);

                Assert.AreEqual(10000, lab.Fired, "ten thousand bolts");
                Assert.AreEqual(10000f * BulletHeavenLab.BoltDamage, Damage(lab, 1000f), 0.01f, "each dealt its damage once");
                Assert.That(lab.Pool.Made, Is.LessThan(1000), "the pool made only as many as flew at once");
                Assert.AreEqual(lab.Pool.Made, lab.Pool.Idle, "and every bolt is back in it");
                Assert.AreEqual(before, World.Control.Active, "ProcessControl holds the processes it began with");
                Assert.IsEmpty(lab.Deaths, "and no enemy died of a thousand cuts");
            }
            finally
            {
                lab.Pool.DestroyAll();
            }
        });

        /// <summary>
        /// The wizard's aura burns every living enemy within its reach, and none beyond it: 500 enemies asked a pulse at a
        /// time, a pulse every quarter second for two seconds.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_The_aura_burns_what_it_reaches() => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new BulletHeavenLab(World, Make);
            try
            {
                lab.Horde(25, 20, new Vector3(-12f, 0f, -10f), health: 1000f);
                const float reach = 6f;
                int inReach = lab.Enemies.Count(enemy => enemy.GetTargetingPacket().position.sqrMagnitude <= reach * reach);
                Assume.That(inReach, Is.InRange(1, lab.Enemies.Count - 1), "the aura reaches some of the horde");

                for (int pulse = 0; pulse < 8; pulse++)
                {
                    Assert.AreEqual(inReach, lab.Pulse(reach), "each pulse burns every enemy within reach");
                    await Frames.Step(16);
                }
                foreach (var enemy in lab.Enemies)
                {
                    bool within = enemy.GetTargetingPacket().position.sqrMagnitude <= reach * reach;
                    Assert.AreEqual(within, lab.Burns(enemy), within ? "an enemy within reach burns" : "and none beyond it");
                }
            }
            finally
            {
                lab.Pool.DestroyAll();
            }
        });

        /// <summary>
        /// A death cascades: an enemy that dies blasts its neighbours, which die and blast theirs, through a cluster of
        /// 200, each dying once. A second cluster, out of reach across a gap, is untouched, and the fallen's processes end.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_death_cascades_through_its_cluster() => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new BulletHeavenLab(World, Make);
            try
            {
                var near = lab.Horde(20, 10, new Vector3(4f, 0f, 0f), health: 20f);
                var far = lab.Horde(20, 10, new Vector3(4f, 0f, 20f), health: 20f);
                await Frames.Step();
                int before = World.Control.Active;

                Assert.IsTrue(lab.Fire(near[0]), "a bolt starts it");
                Assert.IsTrue(World.Apply(lab.Wizard, near[0], lab.Blast), "and a blast finishes the first");
                await Frames.Until(() => near.All(lab.Deaths.ContainsKey), 120, "the cascade runs through the cluster");
                await Frames.Step(4);

                Assert.That(near.Select(enemy => lab.Deaths[enemy]), Is.All.EqualTo(1), "each enemy in it died once");
                Assert.That(far.Any(lab.Deaths.ContainsKey), Is.False, "and none across the gap");
                Assert.That(far.Select(lab.Value), Is.All.EqualTo(20f), "untouched");
                Assert.AreEqual(before - near.Count, World.Control.Active, "the fallen's processes ended, and only theirs");
            }
            finally
            {
                lab.Pool.DestroyAll();
            }
        });

        /// <summary>
        /// A storm: the wizard fires at the horde, its aura pulses, and the dying blast their neighbours, all at once, for
        /// four seconds. The horde falls, each enemy once, and the pool and ProcessControl end clean.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_storm() => Live(async () =>
        {
            using var time = new LabTime();
            var lab = new BulletHeavenLab(World, Make);
            try
            {
                lab.Horde(25, 20, new Vector3(4f, 0f, -10f), health: 60f);
                await Frames.Step();
                int before = World.Control.Active;

                for (int frame = 0; frame < 256; frame++)
                {
                    var living = lab.Enemies.Where(enemy => !lab.Deaths.ContainsKey(enemy)).ToList();
                    if (living.Count == 0) break;
                    // Focus fire on the first few still standing, as a player would
                    for (int i = 0; i < 40; i++) lab.Fire(living[i % Math.Min(4, living.Count)]);
                    if (frame % 16 == 0) lab.Pulse(8f);
                    await Frames.Step();
                }
                await Frames.Until(() => lab.Pool.Out == 0, 200, "the last bolts land");
                await Frames.Step(4);

                Assert.That(lab.Deaths.Count, Is.GreaterThan(lab.Enemies.Count / 2), "the storm took most of the horde");
                Assert.That(lab.Deaths.Values, Is.All.EqualTo(1), "each enemy dying once");
                Assert.AreEqual(lab.Pool.Made, lab.Pool.Idle, "every bolt is back in the pool");
                Assert.AreEqual(before - lab.Deaths.Count, World.Control.Active, "and ProcessControl holds the living, and no bolt");
            }
            finally
            {
                lab.Pool.DestroyAll();
            }
        });
    }
}
