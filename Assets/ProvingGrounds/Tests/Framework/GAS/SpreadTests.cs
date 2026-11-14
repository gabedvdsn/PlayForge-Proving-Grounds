using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FN-14, Decision 77: a Spread component spreads its effect, or another, from the one it's on to those near them at
    /// the moments it names: as it lands, each tick, as it ends, or as the one it's on dies. Each it takes is of the sides
    /// it takes from its caster's; a landing it made spreads on only so far as its Hops allow; and those who have the
    /// effect already are passed over.
    /// </summary>
    public class SpreadTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag red, blue, plagued, burst;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            red = Make.NewTag("Test.FN14.Spread.Red");
            blue = Make.NewTag("Test.FN14.Spread.Blue");
            plagued = Make.NewTag("Test.FN14.Spread.Plagued");
            burst = Make.NewTag("Test.FN14.Spread.Burst");
        }

        // A mortal entity of a side, at a point along x, on either hub, which stays as it dies
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, float x)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            var death = new EntityDeath { Policy = EDeathPolicy.Stay };
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                identity.Death = death;
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                mono.EntityData.Death = death;
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = new Vector3(x, 0f, 0f);
            return gas;
        }

        private bool Has(IGameplayAbilitySystem gas, Tag tag) => gas.GetTagCache().GetWeight(tag) > 0;

        // A plague: it lasts, marks the one it's on, and spreads as its Spread says
        private GameplayEffect Plague(SpreadEffectComponent spread, string name = "Plague")
        {
            var plague = Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { plagued } });
            plague.Components.Add(spread);
            return plague;
        }

        private async Cysharp.Threading.Tasks.UniTask Kill(IGameplayAbilitySystem killer, IGameplayAbilitySystem entity)
        {
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1000f });
            Assert.IsTrue(World.Apply(killer, entity, blow), "a mortal blow");
            await Frames.Until(() => entity.IsDead, 10, "it dies");
            await Frames.Step(2);
        }

        /// <summary>
        /// As the one it's on dies, a plague jumps to the caster's enemies near them, not to the caster's allies, nor to
        /// one out of its reach; and a landing it made, with no hops, spreads no further as its own holder dies.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_spread_jumps_to_those_near_as_its_holder_dies([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, -20f);
            var victim = At(hub, "Victim", blue, 0f);
            var near = At(hub, "Near", blue, 3f);
            var friend = At(hub, "Friend", red, 2f);
            var away = At(hub, "Away", blue, 10f);
            var beyond = At(hub, "Beyond", blue, 6.5f);
            var plague = Plague(new SpreadEffectComponent { When = ESpreadMoment.Death, Radius = 4f, Takes = EAffinityFilter.Enemies });
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, victim, plague), "the plague lands on its victim");
            await Frames.Step();
            Assert.IsFalse(Has(near, plagued), "and stays with it while it lives");

            await Kill(caster, victim);
            Assert.IsTrue(Has(near, plagued), "as it dies, an enemy near it takes the plague");
            Assert.IsFalse(Has(friend, plagued), "not the caster's own side");
            Assert.IsFalse(Has(away, plagued), "nor one out of its reach");
            Assert.IsFalse(Has(beyond, plagued), "nor yet one near the next");

            await Kill(caster, near);
            Assert.IsFalse(Has(beyond, plagued), "with no hops, a landing it made spreads no further");
        });

        /// <summary>
        /// A spread as it lands goes as far as its hops allow along a line of enemies, each spread a hop, and passes over
        /// those who have it already: with 1 hop, from its victim to the next, and from that one to the one after, but no
        /// further.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_spread_as_it_lands_goes_as_far_as_its_hops([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, -20f);
            var first = At(hub, "First", blue, 0f);
            var second = At(hub, "Second", blue, 3f);
            var third = At(hub, "Third", blue, 6f);
            var fourth = At(hub, "Fourth", blue, 9f);
            var plague = Plague(new SpreadEffectComponent { When = ESpreadMoment.Land, Radius = 4f, Hops = 1 });
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, first, plague), "the plague lands on the first");
            await Frames.Step(2);
            Assert.IsTrue(Has(second, plagued), "spreads to the second as it lands");
            Assert.IsTrue(Has(third, plagued), "and from the second, one hop, to the third");
            Assert.IsFalse(Has(fourth, plagued), "but no further");
            Assert.AreEqual(1, first.GetEffectSystem().Shelf.Count, "the first, passed over as it had it, holds it once");
        });

        /// <summary>A spread can spread another effect, as it comes off: a burst on those near as a curse ends.</summary>
        [UnityTest]
        public IEnumerator FN_14_A_spread_can_spread_another_effect_as_it_ends([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, -20f);
            var cursed = At(hub, "Cursed", blue, 0f);
            var near = At(hub, "Near", blue, 2f);
            var bursting = Make.Effect("Burst", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 5f, GrantedTags = new List<Tag> { burst } });
            var curse = Make.Effect("Curse", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 0.2f });
            curse.Components.Add(new SpreadEffectComponent { When = ESpreadMoment.End, Effect = bursting, Radius = 4f });
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, cursed, curse), "the curse lands");
            await Frames.Step();
            Assert.IsFalse(Has(near, burst), "nothing bursts while it lasts");
            await Frames.Until(() => cursed.GetEffectSystem().Shelf.Count == 0, 120, "the curse ends");
            await Frames.Step();
            Assert.IsTrue(Has(near, burst), "and as it ends, the burst lands on one near");
            Assert.IsFalse(Has(cursed, burst), "not on the cursed itself");
        });
    }
}
