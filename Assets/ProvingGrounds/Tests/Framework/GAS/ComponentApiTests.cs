using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// EP-37 to EP-39 and EP-48, the component API: the built-in components need no special case in the core, and a component of a
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
                ? new AutoTargetByAffinityTask { Range = 20f, Mode = EAutoTargetMode.Farthest, Takes = EAffiliationFilter.Enemies }
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

        // ═══════════════════════════════════════════════════════════════════════════
        // EP-48: a refused pick is told
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A target its targeting picked that a component refuses is told as a refusal, as the kind the component says and
        /// in its words: a Cast Range's pick beyond it as Range, and a component of a project's own as it says, a Rule
        /// unless it says otherwise. The activation ended there untold, where a target handed beyond the range, or a Target
        /// Check after targeting, was told. Its stage never runs and nothing of it is paid.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_48_A_refused_pick_is_told_as_its_component_says([Values] Hub hub) => Live(async () =>
        {
            var picked = Make.NewTag("Test.EP48.Picked");
            var caster = At(hub, "Caster", red, 0f);
            var far = At(hub, "Far", blue, 8f);
            var guarded = At(hub, "Guarded", blue, 2f);
            guarded.AddLooseTag(spared);
            var bolt = Marking("Bolt", new AutoTargetFromDataTask { SourceKey = picked }, new CastRangeAbilityComponent { Range = 5f });
            bolt.Cost = Make.Effect("Bolt Cost", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
            var touch = Marking("Touch", new AutoTargetFromDataTask { SourceKey = picked }, new Untouchable { Spared = spared });
            await Frames.Step();
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(bolt, 1, out int boltIndex), "Bolt known");
            Assert.IsTrue(abilities.GiveAbility(touch, 1, out _), "Touch known");
            var told = new List<ActivationRefusal>();
            abilities.Callbacks.OnAbilityRefused += refusal => told.Add(refusal);

            // The built-in: a Cast Range, its targeting's pick beyond it
            var data = ProcessDataPacket.Default();
            data.SetPrimary(picked, (ITarget)far);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, bolt, data).Reason, "Bolt starts, its target not yet picked");
            await Frames.Step(5);
            Assert.AreEqual(0, Ledger.Prepares("Bolt"), "it picked one 8 away, beyond its 5: its stage never runs");
            Assert.IsFalse(Marked(far), "nothing lands");
            Assert.AreEqual(100f, caster.TryGetAttributeValue(health, out var value) ? value.CurrentValue : float.NaN, "and nothing is paid");
            Assert.That(told, Has.Count.EqualTo(1), $"one refusal is told: {string.Join(", ", told)}");
            Assert.AreEqual(EActivationRefusal.Range, told[0].Reason, "as Range, so an AI can walk closer and ask again");
            Assert.AreEqual(CastRangeAbilityComponent.OutOfRange, told[0].Detail, "in its Cast Range's words");
            Assert.AreSame(bolt, told[0].Ability, "for Bolt");
            Assert.AreEqual(boltIndex, told[0].Index, "in its slot");

            // A component of one's own, told as it says
            told.Clear();
            data = ProcessDataPacket.Default();
            data.SetPrimary(picked, (ITarget)guarded);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, touch, data).Reason, "Touch starts");
            await Frames.Step(5);
            Assert.AreEqual(0, Ledger.Prepares("Touch"), "it picked the spared one: its stage never runs");
            Assert.That(told, Has.Count.EqualTo(1), $"one refusal is told: {string.Join(", ", told)}");
            Assert.AreEqual(EActivationRefusal.Rule, told[0].Reason, "as a Rule, as its component says by default");
            Assert.AreEqual("Its target can't be touched", told[0].Detail, "in its words");
            Assert.AreSame(touch, told[0].Ability, "for Touch");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // EP-38: a cooldown model, a usage taken over, checks waived, effects claimed
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>A cooldown model of a project's own: so many rounds, which never come back.</summary>
        [Serializable]
        private sealed class Ammo : AbstractAbilityComponent, ICooldownModel
        {
            public int Rounds = 2;

            private sealed class Fired
            {
                public int Count;
            }

            public int Max(AbilitySpec spec) => Rounds;
            public int InHand(AbilitySpec spec) => Rounds - spec.ComponentStates.Of<Fired>(this).Count;
            public void Spend(AbilitySpec spec, ProcessDataPacket data) => spec.ComponentStates.Of<Fired>(this).Count++;
            public string NoneInHand(AbilitySpec spec) => "It's out of rounds";
        }

        /// <summary>
        /// While its caster holds a tag, it waives its ability's cost and cooldown and takes over its usage, paying nothing
        /// and starting no cooldown, as a toggle turning off does; it counts the commits it hears.
        /// </summary>
        [Serializable]
        private sealed class Overdrive : AbstractAbilityComponent
        {
            public Tag While;

            private sealed class Commits
            {
                public int Count;
            }

            private bool On(AbilityComponentContext ctx) => ctx.Source != null && ctx.Source.GetTagWeight(While) > 0;

            public override EUsageCheck Waives(AbilityComponentContext ctx) => On(ctx) ? EUsageCheck.Cost | EUsageCheck.Cooldown : EUsageCheck.None;

            public override bool ApplyUsage(AbilityComponentContext ctx) => On(ctx);

            public override void Commit(AbilityComponentContext ctx) => ctx.GetState<Commits>(this).Count++;

            public int CommitsOf(AbilitySpec spec) => spec.ComponentStates.Of<Commits>(this).Count;
        }

        /// <summary>A stance of a project's own: its effect on its caster as it starts, which it claims as a toggle's.</summary>
        [Serializable]
        private sealed class Stance : AbstractAbilityComponent
        {
            public GameplayEffect Effect;

            public override void Start(AbilityComponentContext ctx) => ctx.Source?.ApplyCarrier(ctx.Spec.GenerateUsageEffect(Effect, ctx.Data));

            public override EEffectCategory CategoryOf(AbilityComponentContext ctx, GameplayEffectSpec effect) =>
                Effect != null && ReferenceEquals(effect?.Base, Effect) ? EEffectCategory.Toggle : EEffectCategory.None;
        }

        // A caster of a side with Mana, on either hub
        private IGameplayAbilitySystem Caster(Hub hub, ForgeAttribute mana, float amount)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase), Make.Element(mana, amount, 100f));
            if (hub == Hub.Runtime)
            {
                var runtime = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity("Caster", set));
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, "Caster");
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        // Over as it's asked for, so several uses in a frame don't overlap
        private Ability Quick(string name, params AbstractAbilityComponent[] components)
        {
            var ability = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name)));
            ability.Components.AddRange(components);
            return ability;
        }

        private GameplayEffect Lasting(string name, float seconds) =>
            Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = seconds });

        private static AbilitySpec SpecOf(IGameplayAbilitySystem gas, Ability ability) =>
            gas.GetAbilitySystem().TryGetAbilityContainer(ability, out var container) ? container.Spec : null;

        private static bool Carries(IGameplayAbilitySystem gas, GameplayEffect effect) => gas.GetTagCache().GetWeight(effect.Tags.AssetTag) > 0;

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>
        /// A cooldown model of a project's own is its ability's, as Charges is: the Cooldown check asks it what's in hand,
        /// and a use spends through it, the ability's own Cooldown never started. The core knew the charges alone.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_38_A_cooldown_model_of_ones_own_says_what_is_in_hand([Values] Hub hub) => Live(async () =>
        {
            var mana = Make.Attribute("Mana");
            var caster = Caster(hub, mana, 100f);
            var ammo = new Ammo { Rounds = 2 };
            var shot = Quick("Shot", ammo);
            shot.Cooldown = Lasting("Shot Cooldown", 30f);
            await Frames.Step();
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(shot, 1, out _), "known");
            var spec = SpecOf(caster, shot);

            Assert.AreSame(ammo, spec.CooldownModel, "its cooldown model is its ammunition");
            Assert.AreEqual(2, spec.CooldownModel.InHand(spec), "two rounds in hand");
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, shot).Reason, "one");
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, shot).Reason, "two");
            Assert.IsFalse(Carries(caster, shot.Cooldown), "its own Cooldown never started: the model spends");
            Assert.AreEqual(0, spec.CooldownModel.InHand(spec), "none left");
            Assert.AreEqual(EActivationRefusal.Cooldown, Activate(caster, shot).Reason, "so the Cooldown check refuses it");

            var plain = Quick("Plain");
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(plain, 1, out _));
            Assert.IsInstanceOf<SingleCooldown>(SpecOf(caster, plain).CooldownModel, "with none, the single cooldown");
        });

        /// <summary>
        /// A component may take over its ability's usage and waive its cost and its cooldown, as a Toggle does while it's on:
        /// overdriven, the ability is used again and again with Mana it can't pay and nothing paid, its commits still heard;
        /// out of it, it pays and cools down. The core knew the Toggle alone.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_38_A_component_may_take_over_the_usage_and_waive_its_checks([Values] Hub hub) => Live(async () =>
        {
            var mana = Make.Attribute("Mana");
            var overdriven = Make.NewTag("Test.EP38.Overdriven");
            var caster = Caster(hub, mana, 5f);
            var overdrive = new Overdrive { While = overdriven };
            var nova = Quick("Nova", overdrive);
            nova.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = 10f });
            nova.Cooldown = Lasting("Nova Cooldown", 30f);
            await Frames.Step();
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(nova, 1, out _), "known");
            var spec = SpecOf(caster, nova);

            Assert.AreEqual(EActivationRefusal.Cost, Activate(caster, nova).Reason, "with 5 Mana it can't pay its 10");
            caster.AddLooseTag(overdriven);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, nova).Reason, "overdriven, its cost is waived");
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, nova).Reason, "and used again");
            Assert.AreEqual(5f, Value(caster, mana), 0.01f, "paying nothing");
            Assert.IsFalse(Carries(caster, nova.Cooldown), "and starting no cooldown");
            Assert.AreEqual(2, overdrive.CommitsOf(spec), "each commit heard");

            caster.RemoveLooseTag(overdriven);
            caster.TryModifyAttribute(mana, SourcedModifiedAttributeValue.GenerateSimple(caster, mana, 20f, 0f));
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, nova).Reason, "out of it, with Mana, it's used");
            Assert.AreEqual(15f, Value(caster, mana), 0.01f, "paying");
            Assert.IsTrue(Carries(caster, nova.Cooldown), "and cooling down");
            Assert.AreEqual(EActivationRefusal.Cooldown, Activate(caster, nova).Reason, "so it's refused");
            Assert.AreEqual(3, overdrive.CommitsOf(spec), "its third commit heard");
        });

        /// <summary>
        /// A component claims the effects its ability puts on, for a purge: a stance of a project's own claims its effect
        /// as a Toggle's, as the Toggle does its own. The core compared the Toggle's effect alone.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_38_A_component_claims_the_effects_its_ability_puts_on([Values] Hub hub) => Live(async () =>
        {
            var mana = Make.Attribute("Mana");
            var caster = Caster(hub, mana, 100f);
            var guard = Make.Effect("Guard", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            var shield = Make.Effect("Shield", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            var stance = Quick("Stance", new Stance { Effect = guard });
            var toggle = Quick("Toggle", new ToggleAbilityComponent { Effect = shield });
            await Frames.Step();
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(stance, 1, out _) && caster.GetAbilitySystem().GiveAbility(toggle, 1, out _), "known");

            Assert.AreEqual(EActivationRefusal.None, Activate(caster, stance).Reason);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, toggle).Reason);
            await Frames.Step(2);
            Assert.IsTrue(caster.TryGetEffectContainer(guard, out var guarding), "the stance's effect is on");
            Assert.AreEqual(EEffectCategory.Toggle, ForgePurge.CategoriesOf(guarding, caster), "claimed as a toggle's");
            Assert.IsTrue(caster.TryGetEffectContainer(shield, out var shielding), "the toggle's effect is on");
            Assert.AreEqual(EEffectCategory.Toggle, ForgePurge.CategoriesOf(shielding, caster), "claimed as its own");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // EP-39: entity services, and what an effect lends while it lasts
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>A service of a project's own: heat that cools by its entity's own time, told of each revive and its end.</summary>
        private sealed class Heat : AbstractEntityService
        {
            public float Value;
            public int Revives;
            public bool Ended;

            public override void Step(float deltaTime) => Value = Mathf.Max(0f, Value - deltaTime);
            public override void Revive() => Revives++;
            public override void End() => Ended = true;
        }

        /// <summary>An entity component of a project's own that heats its entity as it registers.</summary>
        [Serializable]
        private sealed class Furnace : AbstractEntityComponent
        {
            public float Amount = 10f;

            public override void Register(EntityComponentContext ctx)
            {
                if (ctx.System?.GetService<Heat>() is { } heat) heat.Value += Amount;
            }
        }

        /// <summary>An effect component of a project's own that counts what it hears while its effect lasts, and as it lands.</summary>
        [Serializable]
        private sealed class Banner : AbstractEffectComponent
        {
            public int Lends, TakeBacks, Lands;

            public override void Lend(EffectComponentContext ctx) => Lends++;
            public override void TakeBack(EffectComponentContext ctx) => TakeBacks++;
            public override void Land(EffectComponentContext ctx) => Lands++;
        }

        private sealed class SteppingClock : IForgeClock
        {
            public float Step;
            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        private IForgeClock previousClock;
        private bool clockSet;

        // The game's own IDs for its entities, which a save names them by
        private sealed class EntityIds : IForgeEntityIds
        {
            private readonly Dictionary<string, IGameplayAbilitySystem> byId = new();

            public EntityIds(params (string Id, IGameplayAbilitySystem Entity)[] entities)
            {
                foreach (var (id, entity) in entities) byId[id] = entity;
            }

            public string IdOf(IGameplayAbilitySystem entity) => byId.FirstOrDefault(pair => ReferenceEquals(pair.Value, entity)).Key;

            public IGameplayAbilitySystem Find(string id) => id != null && byId.TryGetValue(id, out var entity) ? entity : null;
        }

        [TearDown]
        public void PutBackTime()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            clockSet = false;
        }

        // Each frame a step of its own
        private void Stepping(float seconds)
        {
            var was = ForgeTime.Use(new SteppingClock { Step = seconds });
            if (!clockSet) previousClock = was;
            clockSet = true;
        }

        // A mortal entity of a side, on either hub, which stays as it dies or ends, holding the components given
        private IGameplayAbilitySystem Mortal(Hub hub, string name, Tag side, bool stays, params AbstractEntityComponent[] components)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            var death = stays
                ? new EntityDeath
                {
                    Policy = EDeathPolicy.Stay,
                    OnRevive = new List<GameplayEffect> { Make.Effect("Mend", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = 1000f }) },
                }
                : new EntityDeath();
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                identity.Death = death;
                identity.Components = new List<AbstractEntityComponent>(components);
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Affiliation = new List<Tag> { side };
            mono.EntityData.Death = death;
            mono.EntityData.Components = new List<AbstractEntityComponent>(components);
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private async Cysharp.Threading.Tasks.UniTask Kill(IGameplayAbilitySystem killer, IGameplayAbilitySystem entity)
        {
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1000f });
            Assert.IsTrue(World.Apply(killer, entity, blow), "a mortal blow");
            await Frames.Until(() => entity.IsDead, 10, "it dies");
        }

        /// <summary>
        /// A service of a project's own is its entity's, as the auras it runs are: made the first time it's asked for and
        /// found again, stepped each frame by the entity's own time, told as the entity revives, and ended with it. The
        /// core stepped its auras and its Respawn's wait alone.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_39_A_service_of_ones_own_steps_by_its_entitys_time([Values] Hub hub) => Live(async () =>
        {
            Stepping(0.1f);
            var killer = Mortal(hub, "Killer", red, stays: false);
            var hero = Mortal(hub, "Hero", blue, stays: true, new Furnace { Amount = 10f });
            await Frames.Step();

            var heat = hero.FindService<Heat>();
            Assert.IsNotNull(heat, "its furnace made it as it registered");
            Assert.AreSame(heat, hero.GetService<Heat>(), "and asking again finds it");
            Assert.IsNull(killer.FindService<Heat>(), "finding makes none");
            hero.GetClock().TimeScale = 0.5f;
            float before = heat.Value;
            await Frames.Step(10);
            Assert.AreEqual(before - 0.5f, heat.Value, 0.06f, "it cools by its entity's own time, half the game's");

            await Kill(killer, hero);
            Assert.IsTrue(hero.Revive(), "it revives");
            Assert.AreEqual(1, heat.Revives, "and its service hears it");

            var other = Mortal(hub, "Other", blue, stays: false, new Furnace());
            await Frames.Step();
            var its = other.FindService<Heat>();
            Assert.IsNotNull(its);
            await Kill(killer, other);
            await Frames.Step(2);
            Assert.IsTrue(its.Ended, "an entity that ends as it dies ends its services");
        });

        /// <summary>
        /// A Respawn's wait is a service of its entity, which a HUD can read as it counts down. The core kept the wait.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_39_A_respawns_wait_is_its_entitys_service([Values] Hub hub) => Live(async () =>
        {
            var killer = Mortal(hub, "Killer", red, stays: false);
            var hero = Mortal(hub, "Hero", blue, stays: true, new RespawnEntityComponent { Delay = 1f });
            await Frames.Step();
            Assert.IsNull(hero.FindService<RespawnTimer>(), "no wait before it dies");

            await Kill(killer, hero);
            await Frames.Step();
            var timer = hero.FindService<RespawnTimer>();
            Assert.IsNotNull(timer, "its death started the wait");
            Assert.IsTrue(timer.Waiting, "which counts down");
            Assert.That(timer.TimeLeft, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            await Frames.Until(() => !hero.IsDead, 240, "then it revives");
            Assert.IsFalse(timer.Waiting, "the wait over");
        });

        /// <summary>
        /// An effect's component hears what its effect lends while it lasts: lent as it lands, taken back as it ends, and
        /// lent again as a load puts it back, which it doesn't land again, as an effect's aura is. An instant effect lends
        /// nothing. The core lent an effect's auras alone.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_39_An_effects_component_hears_what_it_lends([Values] Hub hub) => Live(async () =>
        {
            var killer = Mortal(hub, "Killer", red, stays: false);
            var hero = Mortal(hub, "Hero", blue, stays: false);
            var banner = new Banner();
            var rally = Make.Effect("Rally", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            rally.Components.Add(banner);
            var flash = new Banner();
            var spark = Make.Effect("Spark", new EffectConfig { Policy = EEffectDurationPolicy.Instant });
            spark.Components.Add(flash);
            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            Assert.IsTrue(catalog.Add("health", health) && catalog.Add("rally", rally), "the catalog names them");
            var ids = new EntityIds(("hero", hero), ("killer", killer));
            await Frames.Step();

            Assert.IsTrue(World.Apply(hero, hero, rally), "the rally's on");
            Assert.AreEqual((1, 1, 0), (banner.Lands, banner.Lends, banner.TakeBacks), "it lands and lends");
            Assert.IsTrue(ForgeSave.Restore(hero, ForgeSave.Capture(hero, catalog, ids), catalog, ids), "a load puts it back");
            Assert.AreEqual((1, 2, 1), (banner.Lands, banner.Lends, banner.TakeBacks), "taken back and lent again, not landed");
            Assert.IsTrue(hero.RemoveGameplayEffect(rally.Tags.AssetTag), "it ends");
            await Frames.Step(2);
            Assert.AreEqual(2, banner.TakeBacks, "and takes back what it lent");

            Assert.IsTrue(World.Apply(hero, hero, spark), "an instant effect lands");
            Assert.AreEqual((1, 0), (flash.Lands, flash.Lends), "and lends nothing");
            UnityEngine.Object.DestroyImmediate(catalog);
        });
    }
}
