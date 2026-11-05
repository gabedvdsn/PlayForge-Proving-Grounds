using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Cancel and block abilities by tag (FEAT-5, Decision 30), which the MOBA hero kit needs: a stage lists the
    /// abilities that cancel it as they activate and those it keeps from activating, and a stage that lists none takes
    /// the ability's. A critical stage can't be cancelled. A While active caster group interrupts the ability when it
    /// fails, so a stun ends a channel.
    /// </summary>
    public class CancelAndBlockTests : ProvingGroundsTest
    {
        // Sets one of a stage's lists of ability tags, by reflection until the field is in
        private static void StageList(AbilityTaskBehaviourStage stage, string list, params Tag[] tags)
        {
            var field = typeof(AbilityTaskBehaviourStage).GetField(list);
            Assert.IsNotNull(field, $"a stage has {list}");
            field.SetValue(stage, new List<Tag>(tags));
        }

        // Sets one of an ability's lists, which each stage that lists none takes
        private static void DefaultList(Ability ability, string list, params Tag[] tags)
        {
            var field = typeof(AbilityBehaviour).GetField(list);
            Assert.IsNotNull(field, $"an ability has {list} for its stages");
            field.SetValue(ability.Behaviour, new List<Tag>(tags));
        }

        // Sets the caster group that must hold while the ability runs
        private static void WhileActive(Ability ability, Tag avoided)
        {
            var field = typeof(AbilityTagRequirements).GetField("WhileActiveRequirements");
            Assert.IsNotNull(field, "an ability has a While active group");
            field.SetValue(ability.Tags.TagRequirements, new AvoidRequireTagGroup
            {
                AvoidTags = new List<TagQuery> { new TagQuery { Tag = avoided } },
                RequireTags = new List<TagQuery>(),
            });
        }

        // A one-stage ability tagged as the kind given, which runs a frame
        private Ability Quick(string name, Tag kind)
        {
            var ability = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name + " Task", frames: 1)));
            ability.Tags.ContextTags.Add(kind);
            return ability;
        }

        private static AbilitySpecContainer Give(AbilitySystemComponent abilities, Ability ability)
        {
            Assert.IsTrue(abilities.GiveAbility(ability, 1, out int index), $"the entity knows {ability.GetName()}");
            abilities.TryGetAbilityContainer(index, out var container);
            return container;
        }

        private static bool Activate(AbilitySystemComponent abilities, AbilitySpecContainer container,
            EAbilityActivationPolicyExtended policy = EAbilityActivationPolicyExtended.UseLocalPolicy) =>
            abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, null, policy));

        /// <summary>
        /// Nothing cancelled an ability but an interrupt by slot, so a special couldn't cut a heavy attack's recovery
        /// short (FEAT-5). An ability that a running stage names as it activates cancels that ability; a stage that names
        /// nothing, the windup here, goes on.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_5_A_stage_is_cancelled_by_an_ability_it_names([Values] Hub hub) => Live(async () =>
        {
            var special = Make.NewTag("Test.Kind.Special");
            var recovery = Make.Stage(new ProbeAbilityTask(World, "Recovery", frames: 100));
            StageList(recovery, "CancelledBy", special);
            var heavy = Make.Ability("Heavy", Make.Stage(new ProbeAbilityTask(World, "Windup", frames: 4)), recovery);
            var abilities = World.Entity(hub).GetAbilitySystem();
            var heavyContainer = Give(abilities, heavy);
            var flash = Give(abilities, Quick("Flash", special));

            Assert.IsTrue(Activate(abilities, heavyContainer), "the heavy attack starts");
            await Frames.Step();
            Assert.IsTrue(Activate(abilities, flash), "a special activates in the windup");
            await Frames.Until(() => Ledger.Cleans("Flash Task") == 1, 5, "and runs");
            Assert.IsTrue(heavyContainer.IsActive, "while the windup, which names nothing, goes on");

            await Frames.Until(() => Ledger.Prepares("Recovery") == 1, 10, "the recovery starts");
            Assert.IsTrue(Activate(abilities, flash), "a special activates in the recovery");
            await Frames.Until(() => Ledger.Cleans("Recovery") == 1, 5, "and cancels it");
            await Frames.Step();
            Assert.IsFalse(heavyContainer.IsActive, "so the heavy attack is over");
        });

        /// <summary>A stage that names nothing takes the ability's list, so one list serves every stage (FEAT-5).</summary>
        [UnityTest]
        public IEnumerator FEAT_5_A_stage_that_names_none_takes_the_abilitys_list([Values] Hub hub) => Live(async () =>
        {
            var special = Make.NewTag("Test.Kind.Special");
            var channel = Make.Ability("Channel", Make.Stage(new ProbeAbilityTask(World, "Channeling", frames: 100)));
            DefaultList(channel, "CancelledBy", special);
            var abilities = World.Entity(hub).GetAbilitySystem();
            var channelContainer = Give(abilities, channel);
            var flash = Give(abilities, Quick("Flash", special));

            Assert.IsTrue(Activate(abilities, channelContainer), "the channel starts");
            await Frames.Step(2);
            Assert.IsTrue(Activate(abilities, flash), "a special activates");
            await Frames.Until(() => Ledger.Cleans("Channeling") == 1, 5, "and cancels the channel");
            await Frames.Step();
            Assert.IsFalse(channelContainer.IsActive, "which is over");
        });

        /// <summary>
        /// Blocking was the claim alone, per activation policy, so nothing kept one kind of ability from activating while
        /// another ran (FEAT-5). While a stage that blocks a kind runs, an ability of that kind, or a child of it, can't
        /// activate; once the stage is over, it can.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_5_A_stage_blocks_the_abilities_it_names([Values] Hub hub) => Live(async () =>
        {
            var attack = Make.NewTag("Test.Kind.Attack");
            var focus = Make.Stage(new ProbeAbilityTask(World, "Focusing", frames: 6));
            StageList(focus, "Blocks", attack);
            var abilities = World.Entity(hub).GetAbilitySystem();
            var meditate = Give(abilities, Make.Ability("Meditate", focus));
            var jab = Give(abilities, Quick("Jab", Tag.Generate(attack.Name + ".Light")));

            Assert.IsTrue(Activate(abilities, meditate), "the meditation starts");
            await Frames.Step();
            Assert.IsFalse(abilities.CanActivateAbility(jab.Index), "a light attack can't activate while the focus blocks attacks");
            Assert.IsFalse(Activate(abilities, jab), "and is refused");
            Assert.AreEqual(0, Ledger.Prepares("Jab Task"), "so it never ran");

            await Frames.Until(() => !meditate.IsActive, 12, "the meditation ends");
            Assert.IsTrue(Activate(abilities, jab), "and the jab activates");
            await Frames.Until(() => Ledger.Cleans("Jab Task") == 1, 5, "and runs");
        });

        /// <summary>
        /// A critical stage is committed, so nothing it names cancels it (FEAT-5, Decision 30): the lunge runs to its end,
        /// though the special that would cancel it activates anyway.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_5_A_critical_stage_cant_be_cancelled([Values] Hub hub) => Live(async () =>
        {
            var special = Make.NewTag("Test.Kind.Special");
            var committed = Make.Stage(new ProbeAbilityTask(World, "Committed", frames: 6, critical: true));
            StageList(committed, "CancelledBy", special);
            var abilities = World.Entity(hub).GetAbilitySystem();
            var lunge = Give(abilities, Make.Ability("Lunge", committed));
            var flash = Give(abilities, Quick("Flash", special));

            Assert.IsTrue(Activate(abilities, lunge), "the lunge starts");
            await Frames.Step();
            Activate(abilities, flash, EAbilityActivationPolicyExtended.AlwaysActivate);
            await Frames.Step();
            Assert.IsTrue(lunge.IsActive, "the committed stage goes on");
            Assert.AreEqual(0, Ledger.Cleans("Committed"), "uncancelled");
            await Frames.Until(() => !lunge.IsActive, 12, "until it's done");
        });

        /// <summary>
        /// Requirements weren't checked again while an ability ran, so a stun didn't end a channel (FEAT-5). An ability's
        /// While active group holds for as long as it runs: when it fails, the ability is interrupted, and while it fails,
        /// the ability can't start.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_5_A_stun_interrupts_a_channel_through_its_While_active_group([Values] Hub hub) => Live(async () =>
        {
            var stunned = Make.NewTag("Test.State.Stunned");
            var channel = Make.Ability("Channel", Make.Stage(new ProbeAbilityTask(World, "Channeling", frames: 100)));
            WhileActive(channel, stunned);
            var stun = Make.Effect("Stun", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });
            stun.Tags.GrantedTags = new List<Tag> { stunned };
            var gas = World.Entity(hub);
            var abilities = gas.GetAbilitySystem();
            var container = Give(abilities, channel);

            Assert.IsTrue(Activate(abilities, container), "the channel starts");
            await Frames.Step(2);
            Assert.IsTrue(container.IsActive, "and runs");

            Assert.IsTrue(World.Apply(gas, gas, stun), "a stun lands");
            await Frames.Until(() => Ledger.Cleans("Channeling") == 1, 5, "and interrupts the channel");
            await Frames.Step();
            Assert.IsFalse(container.IsActive, "which is over");
            Assert.IsFalse(Activate(abilities, container), "and can't start again while the stun lasts");

            gas.RemoveGameplayEffect(stun);
            await Frames.Step(2);
            Assert.IsTrue(Activate(abilities, container), "but starts once it's gone");
            await Frames.Step();
            abilities.Inject(container.Index, InterruptSequenceInjection.Instance);
            await Frames.Until(() => Ledger.Cleans("Channeling") == 2, 5, "the second channel ends as the test does");
        });
    }
}
