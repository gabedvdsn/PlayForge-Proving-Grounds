using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-16, Decision 54: crowd control. An entity names its Crowd Control Rules, or follows the project's: while it carries
    /// a rule's tag, an ability the rule stops is refused, and one running is interrupted, as a stun breaks a channel;
    /// Silenced stops only spells; and an ability ignores the rules it names, as Avatar ignores Silenced, not a stun.
    /// </summary>
    public class CrowdControlTests : ProvingGroundsTest
    {
        private Tag stunned, silenced, spell, attack;
        private readonly List<Object> made = new();
        private ForgeProject previous;
        private bool projectSet;

        [SetUp]
        public void Tags()
        {
            stunned = Make.NewTag("Test.TW16.Stunned");
            silenced = Make.NewTag("Test.TW16.Silenced");
            spell = Make.NewTag("Test.TW16.Spell");
            attack = Make.NewTag("Test.TW16.Attack");
        }

        [TearDown]
        public void Clean()
        {
            if (projectSet) ForgeProject.Use(previous);
            projectSet = false;
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private CrowdControlRules Rules(params CrowdControlRule[] rules)
        {
            var asset = ScriptableObject.CreateInstance<CrowdControlRules>();
            asset.Rules = new List<CrowdControlRule>(rules);
            made.Add(asset);
            return asset;
        }

        private static CrowdControlRule Stops(Tag tag, params Tag[] abilityTags) => new()
        {
            Tag = tag,
            Stops = abilityTags.Length == 0 ? ECrowdControlStops.EveryAbility : ECrowdControlStops.AbilitiesTagged,
            AbilityTags = new List<Tag>(abilityTags),
        };

        // An entity on either hub that names its rules, or none
        private IGameplayAbilitySystem Entity(Hub hub, string name, CrowdControlRules rules)
        {
            var set = Make.UnusedSet();
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.CrowdControl = rules;
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.CrowdControl = rules;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private GameplayEffect Granting(string name, Tag tag) => Make.Effect(name, new EffectConfig
        {
            Policy = EEffectDurationPolicy.Durational, Duration = 30f, GrantedTags = new List<Tag> { tag },
        });

        // An ability that runs long, a channel, with the context tags given
        private Ability Channel(string name, params Tag[] tags)
        {
            var ability = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name, frames: 600)));
            ability.Tags.ContextTags.AddRange(tags);
            return ability;
        }

        private static bool Running(IGameplayAbilitySystem gas, Ability ability) =>
            gas.GetAbilitySystem().TryGetAbilityContainer(ability, out var container) && container.IsActive;

        private static void Give(IGameplayAbilitySystem gas, params Ability[] abilities)
        {
            foreach (var ability in abilities) Assert.That(gas.GetAbilitySystem().GiveAbility(ability, 1, out _), $"{ability.name} is known");
        }

        /// <summary>A stun stops every ability and breaks the one that runs.</summary>
        [UnityTest]
        public IEnumerator TW_16_A_stun_stops_every_ability_and_breaks_what_runs([Values] Hub hub) => Live(async () =>
        {
            var hero = Entity(hub, "Hero", Rules(Stops(stunned)));
            var channel = Channel("Channel");
            await Frames.Step();
            Give(hero, channel);
            var abilities = hero.GetAbilitySystem();

            Assert.IsTrue(abilities.TryActivateAbility(channel, out _), "it channels");
            await Frames.Step(2);
            Assert.IsTrue(Running(hero, channel), "and runs");

            Assert.IsTrue(World.Apply(hero, hero, Granting("Stun", stunned)));
            await Frames.Step(2);
            Assert.IsFalse(Running(hero, channel), "a stun breaks the channel");
            Assert.IsFalse(abilities.TryActivateAbility(channel, out var refusal), "and stops it starting again");
            Assert.AreEqual(EActivationRefusal.CrowdControl, refusal.Reason, "refused by crowd control");
            Assert.AreEqual(stunned.ToString(), refusal.Rule, "which names the rule's tag");
        });

        /// <summary>Silenced stops spells alone, and an ability ignores the rules it names, not the others.</summary>
        [UnityTest]
        public IEnumerator TW_16_Silence_stops_spells_and_an_ability_ignores_what_it_names([Values] Hub hub) => Live(async () =>
        {
            var hero = Entity(hub, "Hero", Rules(Stops(stunned), Stops(silenced, spell)));
            var bolt = Channel("Bolt", spell);
            var swing = Channel("Swing", attack);
            var avatar = Channel("Avatar", spell);
            avatar.Definition.IgnoresCrowdControl.Add(silenced);
            await Frames.Step();
            Give(hero, bolt, swing, avatar);
            var abilities = hero.GetAbilitySystem();

            Assert.IsTrue(World.Apply(hero, hero, Granting("Silence", silenced)));
            await Frames.Step();
            Assert.IsFalse(abilities.TryActivateAbility(bolt, out var refusal), "a spell, silenced");
            Assert.AreEqual(EActivationRefusal.CrowdControl, refusal.Reason);
            Assert.IsTrue(abilities.TryActivateAbility(swing, out _), "an attack isn't a spell");
            Assert.IsTrue(abilities.TryActivateAbility(avatar, out _), "Avatar ignores Silenced");
            await Frames.Step(2);
            Assert.IsTrue(Running(hero, avatar), "and runs");

            Assert.IsTrue(World.Apply(hero, hero, Granting("Stun", stunned)));
            await Frames.Step(2);
            Assert.IsFalse(Running(hero, avatar), "but not a stun, which breaks it");
        });

        /// <summary>An entity that names no rules follows the project's; one that names its own follows those.</summary>
        [UnityTest]
        public IEnumerator TW_16_An_entity_follows_its_own_rules_or_the_projects([Values] Hub hub) => Live(async () =>
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            made.Add(project);
            project.Settings.CrowdControl = Rules(Stops(stunned));
            previous = ForgeProject.Use(project);
            projectSet = true;

            var follower = Entity(hub, "Follower", null);
            var own = Entity(hub, "Own", Rules());
            var cast = Channel("Cast");
            var other = Channel("Other");
            await Frames.Step();
            Give(follower, cast);
            Give(own, other);
            Assert.IsTrue(World.Apply(follower, follower, Granting("Stun", stunned)));
            Assert.IsTrue(World.Apply(own, own, Granting("Stun", stunned)));
            await Frames.Step();

            Assert.IsFalse(follower.GetAbilitySystem().TryActivateAbility(cast, out var refusal), "with none of its own, the project's stun holds");
            Assert.AreEqual(EActivationRefusal.CrowdControl, refusal.Reason);
            Assert.IsTrue(own.GetAbilitySystem().TryActivateAbility(other, out _), "its own rules, which stop nothing, in place of the project's");
        });
    }
}
