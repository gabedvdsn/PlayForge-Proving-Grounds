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
    /// EP-37 to EP-39, the component API: the built-in components need no special case in the core, and a component of a
    /// project's own reaches as far as theirs. Each test gives an ability, an effect or an entity a component written here,
    /// on the public API alone, and finds it heard where a built-in's would be.
    /// </summary>
    public class ComponentApiTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag red, blue, marked, spared;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            red = Make.NewTag("Test.EP37.Red");
            blue = Make.NewTag("Test.EP37.Blue");
            marked = Make.NewTag("Test.EP37.Marked");
            spared = Make.NewTag("Test.EP37.Spared");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Components and rules of a project's own
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>A reach of a project's own, as a leash: a fixed range, measured along the ground.</summary>
        [Serializable]
        private sealed class Leash : AbstractAbilityComponent, IAbilityReach
        {
            public float Range = 5f;

            public bool TryReadReach(AbilitySpec spec, ProcessDataPacket data, out float range, out bool flat)
            {
                range = Range;
                flat = true;
                return range > 0f;
            }
        }

        /// <summary>A component of a project's own that refuses every activation, told as the kind it says.</summary>
        [Serializable]
        private sealed class Refusing : AbstractAbilityComponent
        {
            public EActivationRefusal As = EActivationRefusal.Range;

            public override string CanStart(AbilityComponentContext ctx) => "It refuses";

            public override EActivationRefusal RefusedAs => As;
        }

        /// <summary>A rule of a project's own that refuses every activation, told as the kind it says.</summary>
        [Serializable]
        private sealed class AmmunitionValidation : IAbilityValidationRule
        {
            public EActivationRefusal As = EActivationRefusal.Cooldown;

            public bool Validate(AbilityDataPacket data, Func<ITarget> getSource, out string error)
            {
                error = "It's out of ammunition";
                return false;
            }

            public string GetName() => "Ammunition";

            public EActivationRefusal RefusedAs => As;
        }

        /// <summary>A component of a project's own that refuses a target its targeting picked carrying a tag.</summary>
        [Serializable]
        private sealed class Untouchable : AbstractAbilityComponent
        {
            public Tag Spared;

            public override string CanTakeTarget(AbilityComponentContext ctx) =>
                ctx.Data is AbilityDataPacket data && data.TryGetFirst(Tags.TARGET, out AbstractTargetingPacket picked)
                && picked?.target is { } target && target.GetTagWeight(Spared) > 0
                    ? "Its target can't be touched"
                    : null;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The world
        // ═══════════════════════════════════════════════════════════════════════════

        // An entity of a side, at a point along x, on either hub, carrying the tags given
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, float x)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = new Vector3(x, 0f, 0f);
            return gas;
        }

        private bool Marked(IGameplayAbilitySystem gas) => gas.GetTagCache().GetWeight(marked) > 0;

        // An ability whose one stage marks each of its targets, with the targeting and the components given
        private Ability Marking(string name, AbstractTargetingAbilityTask targeting, params AbstractAbilityComponent[] components)
        {
            var mark = Make.Effect(name + " Mark", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 5f, GrantedTags = new List<Tag> { marked },
            });
            var ability = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name), new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { mark } }));
            if (targeting != null) ability.Behaviour.Targeting = targeting;
            ability.Components.AddRange(components);
            return ability;
        }

        private static ActivationRefusal Activate(IGameplayAbilitySystem caster, Ability ability, ProcessDataPacket data = null)
        {
            caster.GetAbilitySystem().TryActivateAbility(ability, out var refusal, data, EAbilityActivationPolicyExtended.AlwaysActivate);
            return refusal;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // EP-37: the lookup, a reach, refusal kinds and a refused target
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A reach of a project's own is found as an ability's reach, by the interface, never the class: the lookup finds
        /// it on the ability and its spec, the auto-targeting tasks search within it from the caster, and the Creator Lab's
        /// bots read it. They knew the Cast Range alone.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_37_A_reach_of_ones_own_is_asked_as_Cast_Range_is([Values] Hub hub, [Values] bool byAffinity) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, 0f);
            var inside = At(hub, "Inside", blue, 4f);
            var outside = At(hub, "Outside", blue, 8f);
            await Frames.Step();
            AbstractTargetingAbilityTask search = byAffinity
                ? new AutoTargetByAffinityTask { Range = 20f, Mode = EAutoTargetMode.Farthest, AffinityFilter = EAffinityFilter.Enemies }
                : new AutoTargetTask { Range = 20f, Mode = EAutoTargetMode.Farthest };
            var leash = new Leash { Range = 5f };
            var bolt = Marking("Bolt", search, leash);
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(bolt, 1, out _), "known");

            Assert.AreSame(leash, bolt.Find<IAbilityReach>(), "the ability's reach is its leash");
            Assert.IsTrue(caster.GetAbilitySystem().TryGetAbilityContainer(bolt, out var container));
            Assert.AreSame(leash, container.Spec.Find<IAbilityReach>(), "and its spec's");
            Assert.IsNull(bolt.Find<CastRangeAbilityComponent>(), "a lookup by the class finds only its own kind");
            Assert.IsTrue(ForgeReach.TryRead(caster, bolt, out float reach), "the lab's bots read it");
            Assert.AreEqual(5f, reach, 0.001f);

            Assert.AreEqual(EActivationRefusal.None, Activate(caster, bolt).Reason);
            await Frames.Until(() => Ledger.Cleans("Bolt") == 1, 30, "it runs");
            Assert.IsTrue(Marked(inside), "the farthest within its leash of 5");
            Assert.IsFalse(Marked(outside), "not the one at 8, within its own Range of 20");
        });

        /// <summary>
        /// A component that refuses an activation is told as the kind it says, as a Cast Range is told as Range; and so is a
        /// rule of a project's own, as Cooldown and Cost are told as theirs. Each was Rule, but for the built-ins, which
        /// the core named.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_37_A_refusal_is_told_as_its_component_or_rule_says([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, 0f);
            await Frames.Step();

            var walled = Marking("Walled", null, new Refusing { As = EActivationRefusal.Range });
            var empty = Marking("Empty", null);
            empty.SourceActivationRules.Insert(0, new AmmunitionValidation { As = EActivationRefusal.Cooldown });
            var plain = Marking("Plain", null);
            plain.SourceActivationRules.Insert(0, new AmmunitionValidation { As = EActivationRefusal.Rule });
            foreach (var ability in new[] { walled, empty, plain })
                Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(ability, 1, out _), $"{ability.name} known");

            var refusal = Activate(caster, walled);
            Assert.AreEqual(EActivationRefusal.Range, refusal.Reason, "a component's refusal, as the kind it says");
            Assert.AreEqual("It refuses", refusal.Rule, "named by its reason");

            refusal = Activate(caster, empty);
            Assert.AreEqual(EActivationRefusal.Cooldown, refusal.Reason, "a rule's, as the kind it says");
            Assert.IsNull(refusal.Rule, "and named by none, as a cooldown is");

            refusal = Activate(caster, plain);
            Assert.AreEqual(EActivationRefusal.Rule, refusal.Reason, "a rule that says Rule, as Rule");
            Assert.AreEqual("Ammunition", refusal.Rule, "named by its name");
            await Frames.Step(2);
            Assert.AreEqual(0, Ledger.Prepares("Walled") + Ledger.Prepares("Empty") + Ledger.Prepares("Plain"), "none ran");
        });

        /// <summary>
        /// A component may refuse the target its targeting picked, as a Cast Range refuses one beyond it: the activation
        /// ends there, before its stages. The core asked the Cast Range alone.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_37_A_component_may_refuse_the_target_its_targeting_picked([Values] Hub hub) => Live(async () =>
        {
            var picked = Make.NewTag("Test.EP37.Picked");
            var caster = At(hub, "Caster", red, 0f);
            var guarded = At(hub, "Guarded", blue, 2f);
            var open = At(hub, "Open", blue, 3f);
            guarded.AddLooseTag(spared);
            var touch = Marking("Touch", new AutoTargetFromDataTask { SourceKey = picked }, new Untouchable { Spared = spared });
            await Frames.Step();
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(touch, 1, out _), "known");

            var data = ProcessDataPacket.Default();
            data.SetPrimary(picked, (ITarget)guarded);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, touch, data).Reason, "it starts, its target not yet picked");
            await Frames.Step(5);
            Assert.AreEqual(0, Ledger.Prepares("Touch"), "picked the spared one, its stage never runs");
            Assert.IsFalse(Marked(guarded), "and nothing lands");

            data = ProcessDataPacket.Default();
            data.SetPrimary(picked, (ITarget)open);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, touch, data).Reason);
            await Frames.Until(() => Ledger.Cleans("Touch") == 1, 30, "picked the other, it runs");
            Assert.IsTrue(Marked(open), "at the one picked");
        });
    }
}
