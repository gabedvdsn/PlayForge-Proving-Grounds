using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Stages that branch (TS-7), which the Duel lab needs. An ability's stage can run only while a tag is there, or only
    /// while it isn't, in the activation's data, on its caster or on its target, so an attack recovers one way on a hit
    /// and another on a miss; the compiler emits each as a branch. An Apply Effects task records a landing in the data.
    /// The claim follows the branch taken: a critical stage that runs holds it, and one skipped lets it go.
    /// </summary>
    public class BranchingStageTests : ProvingGroundsTest
    {
        // Sets a stage's condition, by reflection until the field is in
        private static void Condition(AbilityTaskBehaviourStage stage, string on, Tag tag, bool whenMissing)
        {
            var field = typeof(AbilityTaskBehaviourStage).GetField("Condition");
            Assert.IsNotNull(field, "a stage has a Condition");
            var condition = Activator.CreateInstance(field.FieldType);
            var onField = field.FieldType.GetField("On");
            onField.SetValue(condition, Enum.Parse(onField.FieldType, on));
            field.FieldType.GetField("Tag").SetValue(condition, tag);
            field.FieldType.GetField("WhenMissing").SetValue(condition, whenMissing);
            field.SetValue(stage, condition);
        }

        // An Apply Effects task that records a landing under the tag given
        private static ApplyEffectsAbilityTask Strike(GameplayEffect hit, Tag landed)
        {
            var task = new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { hit } };
            var field = typeof(ApplyEffectsAbilityTask).GetField("RecordLandedAs");
            Assert.IsNotNull(field, "the task can record a landing");
            field.SetValue(task, landed);
            return task;
        }

        // A caster and a target, which guards against the strike when it's to miss
        private (AbilitySystemComponent Abilities, IGameplayAbilitySystem Target) Duel(Hub hub, GameplayEffect hit, Tag strike, bool hits)
        {
            var caster = World.Entity(hub, name: "Caster");
            var health = hit.ImpactSpecification.AttributeTarget;
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            if (!hits)
            {
                var guard = Make.Effect("Guard", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite });
                guard.Tags.BlocksEffectsTagged = new List<Tag> { strike };
                Assert.IsTrue(World.Apply(target, target, guard), "the target guards, so the strike misses");
            }
            return (caster.GetAbilitySystem(), target);
        }

        private static bool Activate(AbilitySystemComponent abilities, Ability ability, IGameplayAbilitySystem target)
        {
            Assert.IsTrue(abilities.GiveAbility(ability, 1, out int index), $"the caster knows {ability.GetName()}");
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            return abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request));
        }

        private GameplayEffect Hit(out Tag strike)
        {
            strike = Make.NewTag("Test.Kind.Strike");
            var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Make.Attribute("Health"), Magnitude = -10f });
            hit.Tags.ContextTags = new List<Tag> { strike };
            return hit;
        }

        /// <summary>
        /// The compiler never emitted a branch, so there was no on-hit or on-miss stage (TS-7, C12). A stage that wants the
        /// landing runs on a hit, and one that wants it missing on a miss; the other is skipped.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_7_An_attack_recovers_one_way_on_a_hit_and_another_on_a_miss([Values] Hub hub, [Values] bool hits) => Live(async () =>
        {
            var hit = Hit(out var strike);
            var landed = Make.NewTag("Test.Data.Landed");
            var onHit = Make.Stage(new ProbeAbilityTask(World, "On Hit"));
            Condition(onHit, "Activation", landed, whenMissing: false);
            var onMiss = Make.Stage(new ProbeAbilityTask(World, "On Miss"));
            Condition(onMiss, "Activation", landed, whenMissing: true);
            var attack = Make.Ability("Attack", Make.Stage(Strike(hit, landed)), onHit, onMiss);
            var (abilities, target) = Duel(hub, hit, strike, hits);

            Assert.IsTrue(Activate(abilities, attack, target), "the attack starts");
            string taken = hits ? "On Hit" : "On Miss", other = hits ? "On Miss" : "On Hit";
            await Frames.Until(() => Ledger.Cleans(taken) == 1, 10, hits ? "it hits, and recovers as a hit does" : "it misses, and recovers as a miss does");
            await Frames.Step(2);
            Assert.AreEqual(0, Ledger.Prepares(other), "and never the other way");
        });

        /// <summary>
        /// A critical section's end was fixed as the run reached the last critical stage, so a critical stage that a branch
        /// skipped held the claim to the end of the run (TS-7). A miss's committed stage holds the claim while it runs; on
        /// a hit it's skipped, and the claim goes as the run passes it, for the hit's recovery to be cut short.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_7_The_claim_follows_the_branch_taken([Values] Hub hub, [Values] bool hits) => Live(async () =>
        {
            var hit = Hit(out var strike);
            var landed = Make.NewTag("Test.Data.Landed");
            var whiff = Make.Stage(new ProbeAbilityTask(World, "Whiff", frames: 6, critical: true));
            Condition(whiff, "Activation", landed, whenMissing: true);
            var recover = Make.Stage(new ProbeAbilityTask(World, "Recover", frames: 6));
            Condition(recover, "Activation", landed, whenMissing: false);
            var attack = Make.Ability("Attack", Make.Stage(Strike(hit, landed)), whiff, recover);
            var (abilities, target) = Duel(hub, hit, strike, hits);

            Assert.IsTrue(Activate(abilities, attack, target), "the attack starts");
            if (hits)
            {
                await Frames.Until(() => Ledger.Prepares("Recover") == 1, 10, "it hits and recovers");
                Assert.IsFalse(abilities.IsClaiming, "the miss's committed stage was skipped, so the claim is gone");
                Assert.AreEqual(0, Ledger.Prepares("Whiff"));
            }
            else
            {
                await Frames.Until(() => Ledger.Prepares("Whiff") == 1, 10, "it misses, and is committed to its whiff");
                Assert.IsTrue(abilities.IsClaiming, "which holds the claim");
                await Frames.Until(() => Ledger.Cleans("Whiff") == 1, 10, "until it's over");
                await Frames.Step();
                Assert.IsFalse(abilities.IsClaiming, "and then lets it go");
            }
            await Frames.Until(() => !abilities.IsExecuting, 12, "the attack ends");
        });

        /// <summary>A stage can ask its caster or its target for a tag too (TS-7): an empowered caster bursts, an unmarked target doesn't detonate.</summary>
        [UnityTest]
        public IEnumerator TS_7_A_stage_can_ask_its_caster_or_its_target([Values] Hub hub) => Live(async () =>
        {
            var empowered = Make.NewTag("Test.State.Empowered");
            var marked = Make.NewTag("Test.State.Marked");
            var burst = Make.Stage(new ProbeAbilityTask(World, "Burst"));
            Condition(burst, "Caster", empowered, whenMissing: false);
            var detonate = Make.Stage(new ProbeAbilityTask(World, "Detonate"));
            Condition(detonate, "Target", marked, whenMissing: false);
            var spell = Make.Ability("Spell", Make.Stage(new ProbeAbilityTask(World, "Cast")), burst, detonate);
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            caster.AddLooseTag(empowered);
            caster.CompileGrantedTags();

            Assert.IsTrue(Activate(caster.GetAbilitySystem(), spell, target), "the spell starts");
            await Frames.Until(() => Ledger.Cleans("Burst") == 1, 10, "the empowered caster bursts");
            await Frames.Step(2);
            Assert.AreEqual(0, Ledger.Prepares("Detonate"), "and the unmarked target doesn't detonate");
        });
    }
}
