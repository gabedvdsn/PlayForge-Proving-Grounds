using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Activation by asset or tag, with a reason (FEAT-17): an ability activated only by its slot, and a refused activation
    /// said only false. An ability activates by its asset or by a tag it carries, and a refusal says why, as the ability
    /// system tells it.
    /// </summary>
    public class ActivationRefusalTests : ProvingGroundsTest
    {
        // Activates by an asset, and the reason it gives: None where it activated
        private static EActivationRefusal Activate(IGameplayAbilitySystem gas, Ability ability,
            EAbilityActivationPolicyExtended? policy = EAbilityActivationPolicyExtended.ActivateIfIdle)
        {
            bool activated = gas.GetAbilitySystem().TryActivateAbility(ability, out var refusal, null, policy);
            Assert.AreEqual(activated, !refusal.Refused, $"it activated just where it gave no reason ({refusal})");
            return refusal.Reason;
        }

        // Activates by a tag, and the reason it gives
        private static EActivationRefusal Activate(IGameplayAbilitySystem gas, Tag tag)
        {
            bool activated = gas.GetAbilitySystem().TryActivateAbility(tag, out var refusal, null, EAbilityActivationPolicyExtended.ActivateIfIdle);
            Assert.AreEqual(activated, !refusal.Refused, $"it activated just where it gave no reason ({refusal})");
            return refusal.Reason;
        }

        // Each refusal the ability system tells, by its reason
        private static List<EActivationRefusal> HearRefusals(IGameplayAbilitySystem gas)
        {
            var heard = new List<EActivationRefusal>();
            gas.GetAbilitySystem().Callbacks.OnAbilityRefused += refusal => heard.Add(refusal.Reason);
            return heard;
        }

        private Ability Quick(string name, int frames = 1) => Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name, frames: frames)));

        /// <summary>An ability activates by its asset, or by a tag it carries; one the entity doesn't know is refused as not known (FEAT-17).</summary>
        [UnityTest]
        public IEnumerator FEAT_17_An_ability_activates_by_its_asset_or_its_tag([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var strike = Quick("Strike");
            var kind = Make.NewTag("Kind.Strike");
            strike.Tags.ContextTags.Add(kind);
            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(strike, 1, out _), "the entity knows Strike");

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, strike), "Strike activates by its asset");
            await Frames.Until(() => !gas.GetAbilitySystem().IsExecuting, 10, "and ends");
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, kind), "and by a tag it carries");
            await Frames.Until(() => !gas.GetAbilitySystem().IsExecuting, 10, "and ends");
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, strike.Tags.AssetTag), "and by its asset tag");
            await Frames.Until(() => !gas.GetAbilitySystem().IsExecuting, 10, "and ends");
            Assert.AreEqual(3, Ledger.Prepares("Strike"), "three times");

            Assert.AreEqual(EActivationRefusal.NotKnown, Activate(gas, Quick("Unknown")), "an ability the entity doesn't know is refused");
            Assert.AreEqual(EActivationRefusal.NotKnown, Activate(gas, Make.NewTag("Kind.Unknown")), "and so is a tag none of its abilities carries");
        });

        /// <summary>
        /// A refused activation says why, and the ability system tells it: its cooldown, its cost, the caster's tag
        /// requirements, a running ability's claim, a running stage's block, or the system off (FEAT-17).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_17_A_refused_activation_says_why([Values] Hub hub) => Live(async () =>
        {
            var mana = Make.Attribute("Mana");
            var gas = World.Entity(hub, Make.Set(Make.Element(mana, 10f, 10f)));
            var abilities = gas.GetAbilitySystem();
            var heard = HearRefusals(gas);

            var stunned = Make.NewTag("State.Stunned");
            // Critical, so it holds its claim as it runs
            var slow = Make.Ability("Slow", Make.Stage(new ProbeAbilityTask(World, "Slow", frames: 30, critical: true)));
            var guard = Make.Ability("Guard", Make.Stage(new ProbeAbilityTask(World, "Guard", frames: 30)));
            var jab = Quick("Jab");
            jab.Tags.TagRequirements.SourceRequirements.AvoidTags.Add(new TagQuery { Tag = stunned });
            var blink = Quick("Blink");
            blink.Behaviour.Stages[0].ApplyUsageEffects = true;
            blink.Cooldown = Make.Effect("Blink Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 30f, Target = null });
            var nova = Quick("Nova");
            nova.Behaviour.Stages[0].ApplyUsageEffects = true;
            nova.Cost = Make.Effect("Nova Cost", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = mana, Magnitude = -50f });
            guard.Behaviour.Stages[0].Blocks = new List<Tag> { jab.Tags.AssetTag };
            foreach (var ability in new[] { slow, guard, jab, blink, nova }) Assert.IsTrue(abilities.GiveAbility(ability, 1, out _));

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, blink), "Blink activates");
            await Frames.Until(() => !abilities.IsExecuting, 10, "and ends");
            Assert.AreEqual(EActivationRefusal.Cooldown, Activate(gas, blink), "and is on cooldown");
            Assert.AreEqual(EActivationRefusal.Cost, Activate(gas, nova), "Nova costs more mana than there is");

            gas.AddLooseTag(stunned);
            Assert.AreEqual(EActivationRefusal.Requirements, Activate(gas, jab), "a stunned caster can't jab");
            gas.RemoveLooseTag(stunned);

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, slow), "Slow runs");
            Assert.AreEqual(EActivationRefusal.Busy, Activate(gas, jab), "and holds the claim a refused jab asks for");
            await Frames.Until(() => !abilities.IsExecuting, 40, "Slow ends");

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, guard, EAbilityActivationPolicyExtended.AlwaysActivate), "Guard runs");
            Assert.AreEqual(EActivationRefusal.Blocked, Activate(gas, jab, EAbilityActivationPolicyExtended.AlwaysActivate), "and its stage blocks the jab");
            await Frames.Until(() => !abilities.IsExecuting, 40, "Guard ends");

            abilities.Enabled = false;
            Assert.AreEqual(EActivationRefusal.Disabled, Activate(gas, jab), "with the ability system off, nothing activates");
            abilities.Enabled = true;

            CollectionAssert.AreEqual(new[]
                {
                    EActivationRefusal.Cooldown, EActivationRefusal.Cost, EActivationRefusal.Requirements, EActivationRefusal.Busy,
                    EActivationRefusal.Blocked, EActivationRefusal.Disabled,
                }, heard, "and the ability system told each refusal, as it said it");
        });
    }
}
