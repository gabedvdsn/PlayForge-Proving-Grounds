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
    /// A refusal carries its check's reason (EP-42, the user's word of 2026-11-17, after EP-35): each validation rule says
    /// why it refuses, but a refusal named the rule alone, its reason dropped, and the target checks after targeting ended
    /// the activation without a refusal at all, so nothing heard why. A refusal's Detail is its check's own words, and a
    /// target check that refuses after targeting is told as a refusal, Target, naming the check.
    /// </summary>
    public class RefusalReasonTests : ProvingGroundsTest
    {
        private ForgeAttribute health, mana;
        private Tag red;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            mana = Make.Attribute("Mana");
            red = Make.NewTag("Test.EP42.Red");
        }

        // An entity of the red side at a point along x, with health and 10 mana, on either hub
        private IGameplayAbilitySystem At(Hub hub, string name, float x)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase), Make.Element(mana, 10f, 10f));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { red };
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { red };
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = new Vector3(x, 0f, 0f);
            return gas;
        }

        private Ability Quick(string name) => Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name)));

        // Each refusal the ability system tells
        private static List<ActivationRefusal> Hear(IGameplayAbilitySystem gas)
        {
            var told = new List<ActivationRefusal>();
            gas.GetAbilitySystem().Callbacks.OnAbilityRefused += refusal => told.Add(refusal);
            return told;
        }

        // Asks for the ability, which must be refused, and gives the refusal
        private static ActivationRefusal Refused(IGameplayAbilitySystem gas, Ability ability, ProcessDataPacket data = null)
        {
            bool activated = gas.GetAbilitySystem().TryActivateAbility(ability, out var refusal, data, EAbilityActivationPolicyExtended.ActivateIfIdle);
            Assert.IsFalse(activated, $"{ability.GetName()} is refused");
            return refusal;
        }

        private static ProcessDataPacket Handing(ITarget target)
        {
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, target);
            return data;
        }

        /// <summary>
        /// A caster check's refusal says why in the check's own words: the Cooldown check's, the Cost check's, with what it
        /// can't pay, and another rule's, which it names; a component's in its reason's. The callbacks tell the same, and
        /// the refusal's text says it too (EP-42).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_42_A_refusal_says_why_in_its_checks_words([Values] Hub hub) => Live(async () =>
        {
            var gas = At(hub, "Caster", 0f);
            var abilities = gas.GetAbilitySystem();
            var blink = Quick("Blink");
            blink.Behaviour.Stages[0].ApplyUsageEffects = true;
            blink.Cooldown = Make.Effect("Blink Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 30f, Target = null });
            var nova = Quick("Nova");
            nova.Behaviour.Stages[0].ApplyUsageEffects = true;
            nova.Cost = Make.Effect("Nova Cost", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = mana, Magnitude = -50f });
            var surge = Quick("Surge");
            surge.SourceActivationRules.Add(new AbilityLevelValidation { Comparison = EComparisonOperator.GreaterOrEqual, Level = 2 });
            var bolt = Quick("Bolt");
            bolt.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            bolt.Components.Add(new CastRangeAbilityComponent { Range = 5f });
            foreach (var ability in new[] { blink, nova, surge, bolt }) Assert.IsTrue(abilities.GiveAbility(ability, 1, out _), $"{ability.GetName()} granted");
            var far = At(hub, "Far", 8f);

            Assert.IsTrue(abilities.TryActivateAbility(blink, out _, null, EAbilityActivationPolicyExtended.ActivateIfIdle), "Blink activates");
            await Frames.Until(() => !abilities.IsExecuting, 10, "and ends");
            var told = Hear(gas);

            var cooling = Refused(gas, blink);
            Assert.AreEqual(EActivationRefusal.Cooldown, cooling.Reason, "Blink is cooling down");
            StringAssert.Contains("cooling down", cooling.Detail ?? "", "in the Cooldown check's words");

            var cost = Refused(gas, nova);
            Assert.AreEqual(EActivationRefusal.Cost, cost.Reason, "Nova costs more mana than there is");
            StringAssert.Contains("50", cost.Detail ?? "", "the Cost check says what it can't pay");
            StringAssert.Contains("10", cost.Detail ?? "", "and what there is");

            var level = Refused(gas, surge);
            Assert.AreEqual(EActivationRefusal.Rule, level.Reason, "Surge wants level 2");
            Assert.AreEqual("Ability Level", level.Rule, "naming the check");
            StringAssert.Contains("1", level.Detail ?? "", "in its words, with the level it read");

            var range = Refused(gas, bolt, Handing(far));
            Assert.AreEqual(EActivationRefusal.Range, range.Reason, "a target 8 away is beyond Bolt's 5");
            Assert.AreEqual(CastRangeAbilityComponent.OutOfRange, range.Detail, "in its Cast Range's words");

            Assert.That(told.Select(each => each.Detail), Is.EqualTo(new[] { cooling.Detail, cost.Detail, level.Detail, range.Detail }),
                "the callbacks tell the same words");
            StringAssert.Contains(level.Detail, level.ToString(), "and the refusal's text says them");
        });

        /// <summary>
        /// A target check that refuses after targeting is told as a refusal, Target, naming the check and in its words, where
        /// the activation ended without one; its stage never runs and nothing is paid (EP-42).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_42_A_target_check_after_targeting_is_told_with_its_reason([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", 0f);
            var ally = At(hub, "Ally", 2f);
            var smite = Quick("Smite");
            smite.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            smite.TargetActivationRules.Add(new AffiliationValidation { Target = ETargetAffiliation.Enemy });
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(smite, 1, out int index), "Smite granted");
            var told = Hear(caster);

            Assert.IsTrue(abilities.TryActivateAbility(smite, out _, Handing(ally), EAbilityActivationPolicyExtended.ActivateIfIdle),
                "Smite starts, its target not yet checked");
            await Frames.Step(5);

            Assert.AreEqual(0, Ledger.Prepares("Smite"), "its stage never runs");
            Assert.That(told, Has.Count.EqualTo(1), $"the ability system tells one refusal: {string.Join(", ", told)}");
            Assert.AreEqual(EActivationRefusal.Target, told[0].Reason, "a target check's");
            Assert.AreEqual("Affiliation", told[0].Rule, "naming the check");
            StringAssert.Contains("enemy", told[0].Detail ?? "", "in its words");
            Assert.AreSame(smite, told[0].Ability, "for Smite");
            Assert.AreEqual(index, told[0].Index, "in its slot");
        });

        /// <summary>
        /// A target that passes the target checks isn't told as refused: the activation runs its stage, and nothing is
        /// told (EP-42).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_42_A_target_that_passes_is_told_nothing([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", 0f);
            var ally = At(hub, "Ally", 2f);
            var mend = Quick("Mend");
            mend.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            mend.TargetActivationRules.Add(new AffiliationValidation { Target = ETargetAffiliation.Ally });
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(mend, 1, out _), "Mend granted");
            var told = Hear(caster);

            Assert.IsTrue(abilities.TryActivateAbility(mend, out _, Handing(ally), EAbilityActivationPolicyExtended.ActivateIfIdle), "Mend starts");
            await Frames.Until(() => !abilities.IsExecuting, 10, "and ends");

            Assert.AreEqual(1, Ledger.Prepares("Mend"), "its stage runs on an ally");
            Assert.That(told, Is.Empty, "and nothing is told as refused");
        });
    }
}
