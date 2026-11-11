using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The bugs the content labs found (Decision 45): an impact worker told the one hit was its holder (GAS-72), an
    /// ability's second effect a frame behind its first (GAS-74), area searches blind to runtime entities (GAS-75), and
    /// two thresholds of one kind sharing one "has fired" (GAS-76).
    /// </summary>
    public class ContentLabFindingsTests : ProvingGroundsTest
    {
        // An impact worker that keeps the System each impact's context names
        private sealed class SystemProbeImpactWorker : AbstractImpactWorker
        {
            public ForgeAttribute Attribute;
            public readonly List<ISource> Seen = new();

            public override bool PreValidateWorkFor(ImpactData impactData) => impactData.Attribute.Equals(Attribute);
            public override bool ValidateWorkFor(ImpactData impactData) => true;

            public override IEnumerable<IRootAction> CreateDeferredActions(ImpactWorkerContext ctx)
            {
                Seen.Add(ctx.System);
                return Array.Empty<IRootAction>();
            }

            public override void SubscribeToCache(ImpactWorkerCache cache) => cache.ProvideWorker(Attribute, this);
            public override void UnsubscribeFromCache(ImpactWorkerCache cache) => cache.RemoveWorker(Attribute, this);
        }

        // A threshold that counts how often it fires
        private sealed class CountingThreshold : ThresholdAnalysisWorker
        {
            public int Fired;

            protected override IEnumerable<IRootAction> OnThresholdReached(IGameplayAbilitySystem system, AttributeValue value, FrameSummary frameSummary)
            {
                Fired++;
                yield break;
            }
        }

        private static bool HasTag(IGameplayAbilitySystem gas, Tag tag) => gas.GetTagCache().GetWeight(tag) > 0;

        private GameplayEffect Blow(ForgeAttribute health, float amount) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });

        private GameplayEffect Marking(string name, Tag tag) =>
            Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { tag } });

        // A runtime entity on a side, at a place
        private RuntimeGameplayAbilitySystem OnSide(string name, Tag side, Vector3 at = default)
        {
            var identity = Make.RuntimeIdentity(name, Make.UnusedSet());
            identity.Affiliation = new List<Tag> { side };
            var gas = new RuntimeGameplayAbilitySystem(identity);
            World.Register(gas);
            gas.GetTargetingPacket().position = at;
            return gas;
        }

        /// <summary>
        /// An impact worker runs for the dealer, from the dealer's workers, but its context named the one hit as its
        /// System, so a worker that took System for its holder applied its holder's effects to the target.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_72_An_impact_worker_runs_with_its_holder_as_its_System([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var probe = new SystemProbeImpactWorker { Attribute = health };
            var dealerSet = Make.Set(Make.Element(health, 100f, 100f));
            dealerSet.WorkerGroup.ImpactWorkers.Add(probe);
            var dealer = World.Entity(hub, dealerSet, "Dealer");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            await Frames.Step();

            Assert.IsTrue(World.Apply(dealer, target, Blow(health, 10f)), "the dealer hits");
            await Frames.Step(2);

            Assert.That(probe.Seen, Is.Not.Empty, "the dealer's worker ran");
            Assert.AreSame(dealer, probe.Seen[0], "with the dealer, whose worker it is, as its System");
        });

        /// <summary>
        /// Apply Effects waited its delay between effects, even a delay of 0, and a delay of 0 waits a frame, so an
        /// ability's second effect landed a frame after its first: a heal's lockout after the heal.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_74_An_abilitys_effects_with_no_delay_land_in_one_frame([Values] Hub hub) => Live(async () =>
        {
            var first = Make.NewTag("Test.GAS74.First");
            var second = Make.NewTag("Test.GAS74.Second");
            var ability = Make.Ability("Both", Make.Stage(new ApplyEffectsAbilityTask
            {
                Effects = new List<GameplayEffect> { Marking("First", first), Marking("Second", second) },
            }));
            var caster = World.Entity(hub, name: "Caster");
            await Frames.Step();

            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)caster);
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request)), "it activates");

            await Frames.Until(() => HasTag(caster, first), 10, "the first effect lands");
            Assert.IsTrue(HasTag(caster, second), "and the second in the same frame");
        });

        /// <summary>
        /// Auto Target by Affinity with no range searched the scene for MonoBehaviour entities, so it never found a
        /// runtime one; it goes through every live entity, on both hubs.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_75_An_area_search_with_no_range_finds_runtime_entities() => Live(async () =>
        {
            var ours = Make.NewTag("Test.GAS75.Ours");
            var theirs = Make.NewTag("Test.GAS75.Theirs");
            var mark = Make.NewTag("Test.GAS75.Mark");
            var caster = OnSide("Caster", ours);
            var ally = OnSide("Ally", ours);
            var first = OnSide("First", theirs);
            var second = OnSide("Second", theirs);
            var ability = Make.Ability("Area", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Marking("Mark", mark) } }));
            ability.Behaviour.Targeting = new AutoTargetByAffinityTask
            {
                AffinityFilter = EAffinityFilter.Enemies, Mode = EAutoTargetMode.Nearest, MaxTargets = 0, Range = 0f,
                Requirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
            };
            await Frames.Step();

            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "it activates");

            await Frames.Until(() => HasTag(first, mark), 10, "it finds a runtime enemy");
            Assert.IsTrue(HasTag(second, mark), "and the other");
            Assert.IsFalse(HasTag(ally, mark), "not an ally");
            Assert.IsFalse(HasTag(caster, mark), "nor its caster");
        });

        /// <summary>
        /// With a range, the search was physics' alone, which finds no runtime entity, as one has no collider: it finds a
        /// runtime entity by its targeting packet, within the range and no further.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_75_An_area_search_with_a_range_finds_runtime_entities_within_it() => Live(async () =>
        {
            var ours = Make.NewTag("Test.GAS75.Ours");
            var theirs = Make.NewTag("Test.GAS75.Theirs");
            var mark = Make.NewTag("Test.GAS75.Mark");
            var caster = OnSide("Caster", ours);
            var near = OnSide("Near", theirs, new Vector3(3f, 0f, 0f));
            var far = OnSide("Far", theirs, new Vector3(10f, 0f, 0f));
            var ability = Make.Ability("Area", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Marking("Mark", mark) } }));
            ability.Behaviour.Targeting = new AutoTargetByAffinityTask
            {
                AffinityFilter = EAffinityFilter.Enemies, Mode = EAutoTargetMode.Nearest, MaxTargets = 0, Range = 5f,
                Requirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
            };
            await Frames.Step();

            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "it activates");

            await Frames.Until(() => HasTag(near, mark), 10, "it finds the enemy 3 away");
            await Frames.Step(2);
            Assert.IsFalse(HasTag(far, mark), "not the one 10 away");
        });

        /// <summary>
        /// An analysis worker's "has fired" key was its type's name, so two thresholds of one kind on one entity shared
        /// it: the half fired, and the quarter never did.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_76_Two_thresholds_of_one_kind_fire_apart([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var half = new CountingThreshold { Target = health, Threshold = 50f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true };
            var quarter = new CountingThreshold { Target = health, Threshold = 25f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true };
            var set = Make.Set(Make.Element(health, 100f, 100f));
            set.WorkerGroup.AnalysisWorkers.Add(half);
            set.WorkerGroup.AnalysisWorkers.Add(quarter);
            var entity = World.Entity(hub, set, "Entity");
            var other = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Other");
            await Frames.Step();

            Assert.IsTrue(World.Apply(other, entity, Blow(health, 60f)), "to 40");
            await Frames.Step(2);
            Assert.AreEqual(1, half.Fired, "the half fires");
            Assert.AreEqual(0, quarter.Fired, "the quarter waits");

            Assert.IsTrue(World.Apply(other, entity, Blow(health, 20f)), "to 20");
            await Frames.Step(2);
            Assert.AreEqual(1, quarter.Fired, "the quarter fires, its own");
            Assert.AreEqual(1, half.Fired, "and the half once");
        });
    }
}
