using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The review's chain reactions, a stretch scenario: effects that apply effects that apply effects, on one entity and
    /// across several. An effect asked for again inside its own application is refused there (GAS-28), so a loop is cut
    /// where it comes back, and a chain without one runs to its end.
    /// </summary>
    [Category("Stretch")]
    public class ChainReactionTests : ProvingGroundsTest
    {
        // The effects told as applied on an entity, by name, in order
        private static List<string> Landed(IGameplayAbilitySystem gas)
        {
            var landed = new List<string>();
            gas.GetCallbacks().OnEffectApplied += spec => landed.Add(spec.Base.GetName());
            return landed;
        }

        private GameplayEffect Instant(string name) => Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = null });

        private static void Contain(GameplayEffect effect, GameplayEffect next) =>
            effect.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = next } };

        /// <summary>A chain on one entity, each effect applying the next as it lands, runs five deep, and each lands once, in order.</summary>
        [UnityTest]
        public IEnumerator Stretch_A_chain_on_one_entity_runs_to_its_end([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var landed = Landed(gas);
            var names = new[] { "Ignite", "Burn", "Smoke", "Ash", "Ember" };
            var chain = new List<GameplayEffect>();
            foreach (var name in names) chain.Add(Instant(name));
            for (int i = 0; i < chain.Count - 1; i++) Contain(chain[i], chain[i + 1]);

            Assert.IsTrue(World.Apply(gas, gas, chain[0]), "Ignite lands");
            await Frames.Step();
            CollectionAssert.AreEqual(names, landed, "and each after it lands once, in order");
        });

        /// <summary>
        /// A loop on one entity, Fire applying Spark and Spark applying Fire, is cut as Fire comes back inside its own
        /// application: each lands once. Applied again, the loop is cut again (GAS-28).
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_A_loop_is_cut_where_it_comes_back([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var landed = Landed(gas);
            var fire = Instant("Fire");
            var spark = Instant("Spark");
            Contain(fire, spark);
            Contain(spark, fire);

            Assert.IsTrue(World.Apply(gas, gas, fire), "Fire lands");
            await Frames.Step();
            CollectionAssert.AreEqual(new[] { "Fire", "Spark" }, landed, "Spark lands, and Fire, coming back, is cut");
            Assert.IsTrue(World.Apply(gas, gas, fire), "Fire lands again");
            await Frames.Step();
            CollectionAssert.AreEqual(new[] { "Fire", "Spark", "Fire", "Spark" }, landed, "and the loop is cut again");
        });

        // As a shock lands, it arcs at once to the next entity in its ring, inside its own application
        private sealed class Arc : AbstractEffectWorker
        {
            public List<IGameplayAbilitySystem> Ring;
            public GameplayEffect Shock;

            public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx)
            {
                int at = Ring.IndexOf(ctx.System);
                var next = Ring[(at + 1) % Ring.Count];
                next.ApplyGameplayEffect(next.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(ctx.System), Shock));
                yield break;
            }
        }

        /// <summary>
        /// A shock that arcs from each entity of a ring to the next, inside its own application, goes round the ring once:
        /// each entity's guard refuses the shock coming back to it while its own still applies (GAS-28).
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_A_ring_of_arcs_goes_round_once([Values] Hub hub) => Live(async () =>
        {
            var ring = new List<IGameplayAbilitySystem>();
            var landed = new List<List<string>>();
            for (int i = 0; i < 4; i++)
            {
                // Unobserved: what each deals goes to the next, which the invariants would pair only on a whole ring
                var entity = World.Entity(hub, null, $"Link {i}", observed: false);
                ring.Add(entity);
                landed.Add(Landed(entity));
            }
            var arc = new Arc { Ring = ring };
            var shock = Make.Effect("Shock", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 2f, Target = null, Workers = new List<AbstractEffectWorker> { arc },
            });
            arc.Shock = shock;

            Assert.IsTrue(World.Apply(ring[0], ring[0], shock), "the first link is shocked");
            await Frames.Step();
            for (int i = 0; i < ring.Count; i++)
            {
                CollectionAssert.AreEqual(new[] { "Shock" }, landed[i], $"link {i} is shocked once");
                Assert.IsTrue(ring[i].TryGetEffectContainers(shock, out var held) && held.Length == 1, $"and holds one shock");
            }
        });
    }
}
