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
    /// FN-14, Decision 76: components. Each effect, ability, entity and item holds a list of components, each a class whose
    /// hooks hear its asset's moments: an effect land, tick, hit and end; an ability granted, asked whether it may start,
    /// started, committed, ended by how, and removed; an entity register, die, revive and end; an item given, equipped,
    /// unequipped and removed. What a component keeps for one holder it keeps through its context, apart from another's.
    /// </summary>
    public class ComponentTests : ProvingGroundsTest
    {
        // What the components heard, in order
        private static readonly List<string> Heard = new();

        private ForgeAttribute health;

        [SetUp]
        public void Clear() => Heard.Clear();

        private sealed class HearingEffect : AbstractEffectComponent
        {
            public override void Land(EffectComponentContext ctx) => Heard.Add("Land");
            public override void Tick(EffectComponentContext ctx) => Heard.Add("Tick");
            public override void Hit(EffectComponentContext ctx) => Heard.Add("Hit");
            public override void End(EffectComponentContext ctx) => Heard.Add("End");
        }

        private sealed class HearingAbility : AbstractAbilityComponent
        {
            public string Refuses;

            public override void Granted(AbilityComponentContext ctx) => Heard.Add("Granted");
            public override void Removed(AbilityComponentContext ctx) => Heard.Add("Removed");

            public override string CanStart(AbilityComponentContext ctx)
            {
                Heard.Add("CanStart");
                return Refuses;
            }

            public override void Start(AbilityComponentContext ctx) => Heard.Add("Start");
            public override void Commit(AbilityComponentContext ctx) => Heard.Add("Commit");
            public override void End(AbilityComponentContext ctx, EAbilityEnd how) => Heard.Add("End " + how);
        }

        // Counts its holder's activations, each holder's apart
        private sealed class CountingAbility : AbstractAbilityComponent
        {
            public sealed class Count
            {
                public int Starts;
            }

            public override void Start(AbilityComponentContext ctx) => ctx.GetState<Count>(this).Starts++;
        }

        // What its own entity's moments are, apart from another entity's
        private sealed class HearingEntity : AbstractEntityComponent
        {
            public readonly List<string> Heard = new();

            public override void Register(EntityComponentContext ctx) => Heard.Add("Register");
            public override void Death(EntityComponentContext ctx) => Heard.Add("Death");
            public override void Revive(EntityComponentContext ctx) => Heard.Add("Revive");
            public override void End(EntityComponentContext ctx) => Heard.Add("End");
        }

        private sealed class HearingItem : AbstractItemComponent
        {
            public override void Given(ItemComponentContext ctx) => Heard.Add("Given");
            public override void Equipped(ItemComponentContext ctx) => Heard.Add("Equipped");
            public override void Unequipped(ItemComponentContext ctx) => Heard.Add("Unequipped");
            public override void Removed(ItemComponentContext ctx) => Heard.Add("Removed");
        }

        // A set of 100 health whose death watcher sees it reach 0, its attribute made anew: one kept from a test before
        // is destroyed with that test's assets, though C# still holds it
        private AttributeSet Mortal()
        {
            health = Make.Attribute("Health");
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            return set;
        }

        // An entity on either hub whose identity dies as given and holds the components given
        private IGameplayAbilitySystem Entity(Hub hub, EntityDeath death, params AbstractEntityComponent[] components)
        {
            var set = Mortal();
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity("Hero", set);
                identity.Death = death;
                identity.Components = new List<AbstractEntityComponent>(components);
                var gas = new RuntimeGameplayAbilitySystem(identity);
                World.Register(gas);
                World.Invariants.Observe(gas);
                return gas;
            }

            var mono = World.NewMonoEntity(set, "Hero");
            mono.EntityData.Death = death;
            mono.EntityData.Components = new List<AbstractEntityComponent>(components);
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private async Cysharp.Threading.Tasks.UniTask Kill(IGameplayAbilitySystem entity)
        {
            var killer = World.Entity(Hub.Runtime, Make.Set(Make.Element(health, 1f, 1f)), "Killer");
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1000f });
            Assert.IsTrue(World.Apply(killer, entity, blow), "a mortal blow");
            await Frames.Until(() => entity.IsDead, 10, "it dies");
            await Frames.Step(2);
        }

        private static EActivationRefusal Activate(IGameplayAbilitySystem gas, Ability ability, out string rule)
        {
            gas.GetAbilitySystem().TryActivateAbility(ability, out var refusal, null, EAbilityActivationPolicyExtended.AlwaysActivate);
            rule = refusal.Rule;
            return refusal.Reason;
        }

        [UnityTest]
        public IEnumerator FN_14_An_effects_components_hear_it_land_tick_hit_and_end([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 0.4f, PeriodicTicks = true, Ticks = 2, Target = health, Magnitude = -5f,
            });
            burn.Components.Add(new HearingEffect());
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, burn), "the burn lands");
            await Frames.Until(() => Heard.Contains("End"), 120, "and ends");
            Assert.AreEqual("Land", Heard.First(), "its component hears it land first");
            Assert.That(Heard, Has.Member("Tick"), "then tick");
            Assert.That(Heard, Has.Member("Hit"), "and hit, as its damage lands");
            Assert.AreEqual("End", Heard.Last(), "and end last");

            Heard.Clear();
            var strike = Make.Effect("Strike", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -5f });
            strike.Components.Add(new HearingEffect());
            Assert.IsTrue(World.Apply(caster, target, strike), "a strike lands");
            await Frames.Step(2);
            Assert.AreEqual("Land", Heard.First(), "an instant effect's component hears it land");
            Assert.That(Heard, Has.Member("End"), "and end at once");
            Assert.That(Heard, Has.No.Member("Tick"), "with no tick");
        });

        [UnityTest]
        public IEnumerator FN_14_An_abilitys_components_hear_it_granted_started_committed_ended_and_removed([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, Make.Set(), "Caster");
            var strike = Make.Ability("Strike", Make.Stage(new ProbeAbilityTask(World, "Strike")));
            strike.Components.Add(new HearingAbility());
            await Frames.Step();

            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(strike, 1, out _), "it's known");
            Assert.That(Heard, Is.EqualTo(new[] { "Granted" }), "its component hears it granted");
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, strike, out _), "it starts");
            await Frames.Until(() => !gas.GetAbilitySystem().IsExecuting, 20, "and ends");
            Assert.That(Heard, Has.Member("CanStart"), "its component was asked whether it may start");
            Assert.That(Heard.Where(each => each != "CanStart"), Is.EqualTo(new[] { "Granted", "Start", "Commit", "End Done" }),
                "and heard it start, commit its cost and cooldown, and end done");

            Assert.IsTrue(gas.GetAbilitySystem().RemoveAbility(strike), "it's forgotten");
            Assert.AreEqual("Removed", Heard.Last(), "and its component hears it removed");
        });

        [UnityTest]
        public IEnumerator FN_14_An_abilitys_component_may_refuse_it_and_hears_it_cancelled([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, Make.Set(), "Caster");
            var refused = Make.Ability("Refused", Make.Stage(new ProbeAbilityTask(World, "Refused")));
            refused.Components.Add(new HearingAbility { Refuses = "Not yet" });
            var channel = Make.Ability("Channel", Make.Stage(new ProbeAbilityTask(World, "Channel", frames: 600)));
            channel.Components.Add(new HearingAbility());
            await Frames.Step();
            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(refused, 1, out _) && gas.GetAbilitySystem().GiveAbility(channel, 1, out _));

            Assert.AreEqual(EActivationRefusal.Rule, Activate(gas, refused, out var rule), "a component's reason refuses it");
            Assert.AreEqual("Not yet", rule, "named by the reason");
            Assert.That(Heard, Has.No.Member("Start"), "and it never starts");

            Heard.Clear();
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, channel, out _), "the channel starts");
            await Frames.Step(2);
            Assert.IsTrue(gas.GetAbilitySystem().TryGetAbilityContainer(channel, out var running) && running.IsActive, "and runs");
            running.Inject(InterruptSequenceInjection.Instance);
            await Frames.Until(() => Heard.Any(each => each.StartsWith("End")), 20, "it's interrupted");
            Assert.AreEqual("End Cancelled", Heard.Last(), "and its component hears it end cancelled");
        });

        [UnityTest]
        public IEnumerator FN_14_A_component_keeps_each_holders_state_apart([Values] Hub hub) => Live(async () =>
        {
            var first = World.Entity(hub, Make.Set(), "First");
            var second = World.Entity(hub, Make.Set(), "Second");
            var counting = new CountingAbility();
            var strike = Make.Ability("Strike", Make.Stage(new ProbeAbilityTask(World, "Strike")));
            strike.Components.Add(counting);
            await Frames.Step();
            Assert.IsTrue(first.GetAbilitySystem().GiveAbility(strike, 1, out _) && second.GetAbilitySystem().GiveAbility(strike, 1, out _));

            int Starts(IGameplayAbilitySystem gas) => gas.GetAbilitySystem().TryGetAbilityContainer(strike, out var container)
                ? container.Spec.ComponentStates.Of<CountingAbility.Count>(counting).Starts : -1;

            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(EActivationRefusal.None, Activate(second, strike, out _), "the second casts");
                await Frames.Until(() => !second.GetAbilitySystem().IsExecuting, 20, "and ends");
            }
            Assert.AreEqual(EActivationRefusal.None, Activate(first, strike, out _), "the first casts once");
            await Frames.Until(() => !first.GetAbilitySystem().IsExecuting, 20, "and ends");

            Assert.AreEqual(1, Starts(first), "one component, the first's count its own");
            Assert.AreEqual(2, Starts(second), "and the second's its own");
        });

        [UnityTest]
        public IEnumerator FN_14_An_entitys_components_hear_it_register_die_revive_and_end([Values] Hub hub) => Live(async () =>
        {
            var staying = new HearingEntity();
            var stays = Entity(hub, new EntityDeath { Policy = EDeathPolicy.Stay }, staying);
            Assert.That(staying.Heard, Is.EqualTo(new[] { "Register" }), "its component hears it register");
            await Frames.Step();
            await Kill(stays);
            Assert.AreEqual("Death", staying.Heard.Last(), "and die");
            Assert.IsTrue(stays.Revive(), "it revives");
            Assert.AreEqual("Revive", staying.Heard.Last(), "and its component hears it");

            var ending = new HearingEntity();
            var ends = Entity(hub, new EntityDeath { Policy = EDeathPolicy.End }, ending);
            await Frames.Step();
            await Kill(ends);
            Assert.That(ending.Heard, Is.EqualTo(new[] { "Register", "Death", "End" }), "one that doesn't stay is heard to die, then end");
        });

        [UnityTest]
        public IEnumerator FN_14_An_items_components_hear_it_given_equipped_unequipped_and_removed([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub, Make.Set(), "Holder");
            var banner = Make.Item("Banner");
            banner.Components.Add(new HearingItem());
            await Frames.Step();

            Assert.IsTrue(gas.GetItemSystem().GiveAndEquipItem(banner, out _), "the banner's given and equipped");
            Assert.That(Heard, Is.EqualTo(new[] { "Given", "Equipped" }), "its component hears both");
            Assert.IsTrue(gas.GetItemSystem().RemoveItem(banner), "it's taken away");
            Assert.That(Heard, Is.EqualTo(new[] { "Given", "Equipped", "Unequipped", "Removed" }), "unequipped, then removed");
        });
    }
}
