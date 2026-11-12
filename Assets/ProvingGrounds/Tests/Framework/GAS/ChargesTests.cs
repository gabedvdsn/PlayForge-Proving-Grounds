using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-17, Decision 55: an ability's cost and cooldown apply as it starts, after targeting, where no stage marked Apply
    /// Usage Effects takes them; Laguna Blade paid neither. And charges, a number on the ability: each spent charge comes
    /// back on a cooldown of its own, or, in the other mode, all come back as one cooldown ends once all are spent; and a
    /// recharge of the author's own decides it as it says.
    /// </summary>
    public class ChargesTests : ProvingGroundsTest
    {
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

        // Spent charges never come back: so many uses, and no more
        private sealed class LimitedUses : AbstractChargeRecharge
        {
            public override int InHand(AbilityCharges charges) => charges.Max - charges.Spent;
            public override void Spend(AbilityCharges charges, ProcessDataPacket data) => charges.Spent++;
        }

        private Attribute mana;
        private IForgeClock previousClock;
        private bool clockSet;

        [SetUp]
        public void Attributes() => mana = Make.Attribute("Mana");

        [TearDown]
        public void PutBackTime()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            clockSet = false;
        }

        // Each frame a step of its own, so a cooldown's seconds are frames
        private void Stepping(float seconds)
        {
            var was = ForgeTime.Use(new SteppingClock { Step = seconds });
            if (!clockSet) previousClock = was;
            clockSet = true;
        }

        private IGameplayAbilitySystem Caster(Hub hub) => World.Entity(hub, Make.Set(Make.Element(mana, 100f, 100f)), "Caster");

        // Over as it's asked for, so several uses in a frame don't overlap
        private Ability Quick(string name) => Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name)));

        private GameplayEffect Cooldown(string name, float seconds) =>
            Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = seconds, Target = null });

        private GameplayEffect Cost(string name, float amount) =>
            Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = mana, Magnitude = -amount });

        private static void Give(IGameplayAbilitySystem gas, params Ability[] abilities)
        {
            foreach (var ability in abilities) Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(ability, 1, out _), $"{ability.name} is known");
        }

        // Activates at once, as an ability set to Activate Anyway, and gives the reason it was refused, or None
        private static EActivationRefusal Activate(IGameplayAbilitySystem gas, Ability ability)
        {
            gas.GetAbilitySystem().TryActivateAbility(ability, out var refusal, null, EAbilityActivationPolicyExtended.AlwaysActivate);
            return refusal.Reason;
        }

        private static float Value(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static int InHand(IGameplayAbilitySystem gas, Ability ability) =>
            gas.GetAbilitySystem().TryGetAbilityContainer(ability, out var container) ? container.Spec.Charges.InHand : -1;

        private static bool Carries(IGameplayAbilitySystem gas, GameplayEffect effect) => gas.GetTagCache().GetWeight(effect.Tags.AssetTag) > 0;

        /// <summary>Where no stage takes them, the cost and the cooldown apply as it starts; a stage that takes them still does.</summary>
        [UnityTest]
        public IEnumerator TW_17_Cost_and_cooldown_apply_as_it_starts_where_no_stage_takes_them([Values] Hub hub) => Live(async () =>
        {
            var gas = Caster(hub);
            var laguna = Quick("Laguna");
            laguna.Cost = Cost("Laguna Cost", 30f);
            laguna.Cooldown = Cooldown("Laguna Cooldown", 30f);

            // A channel that takes them at the end of its stage, which an interrupt never reaches
            var channel = Make.Ability("Channel", Make.Stage(new ProbeAbilityTask(World, "Channel", frames: 600)));
            channel.Cost = Cost("Channel Cost", 20f);
            channel.Behaviour.Stages[0].ApplyUsageEffects = true;
            channel.Behaviour.Stages[0].ApplyUsageAtStageStart = false;
            await Frames.Step();
            Give(gas, laguna, channel);

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, laguna), "Laguna casts");
            Assert.AreEqual(70f, Value(gas, mana), 0.01f, "and pays its cost as it starts, though no stage takes it");
            Assert.IsTrue(Carries(gas, laguna.Cooldown), "and its cooldown runs");
            await Frames.Until(() => !gas.GetAbilitySystem().IsExecuting, 10, "it ends");
            Assert.AreEqual(EActivationRefusal.Cooldown, Activate(gas, laguna), "and can't cast again");

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, channel), "the channel starts");
            await Frames.Step(2);
            Assert.IsTrue(gas.GetAbilitySystem().TryGetAbilityContainer(channel, out var running) && running.IsActive, "and runs");
            running.Inject(InterruptSequenceInjection.Instance);
            await Frames.Step(2);
            Assert.AreEqual(70f, Value(gas, mana), 0.01f, "its stage takes the cost at its end, which an interrupt never reaches");
        });

        /// <summary>By default, each spent charge comes back on a cooldown of its own, and it's refused only with none in hand.</summary>
        [UnityTest]
        public IEnumerator TW_17_Each_spent_charge_comes_back_on_its_own_cooldown([Values] Hub hub) => Live(async () =>
        {
            Stepping(0.1f);
            var gas = Caster(hub);
            var shrapnel = Quick("Shrapnel");
            shrapnel.Cooldown = Cooldown("Shrapnel Cooldown", 1f);
            shrapnel.Charges = 3;
            await Frames.Step();
            Give(gas, shrapnel);
            Assert.AreEqual(3, InHand(gas, shrapnel), "three in hand");

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, shrapnel), "one");
            Assert.AreEqual(2, InHand(gas, shrapnel), "two left");
            await Frames.Step(5);
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, shrapnel), "two, half a second on, with a charge in hand");
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, shrapnel), "three");
            Assert.AreEqual(0, InHand(gas, shrapnel), "none left");
            Assert.AreEqual(EActivationRefusal.Cooldown, Activate(gas, shrapnel), "so it's refused");

            await Frames.Step(7);
            Assert.AreEqual(1, InHand(gas, shrapnel), "the first charge's cooldown is over, and it's back alone");
            await Frames.Step(6);
            Assert.AreEqual(3, InHand(gas, shrapnel), "then the other two, each on its own");
        });

        /// <summary>All at once: no cooldown until the last charge is spent, and its end brings them all back.</summary>
        [UnityTest]
        public IEnumerator TW_17_All_at_once_comes_back_as_one_cooldown_ends([Values] Hub hub) => Live(async () =>
        {
            Stepping(0.1f);
            var gas = Caster(hub);
            var cast = Quick("Cast");
            cast.Cooldown = Cooldown("Cast Cooldown", 1f);
            cast.Charges = 2;
            cast.Recharge = new AllAtOnceRecharge();
            await Frames.Step();
            Give(gas, cast);

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, cast));
            Assert.AreEqual(1, InHand(gas, cast), "one left");
            Assert.IsFalse(Carries(gas, cast.Cooldown), "and no cooldown while one is");
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, cast));
            Assert.AreEqual(0, InHand(gas, cast), "none left");
            Assert.IsTrue(Carries(gas, cast.Cooldown), "so the cooldown runs");
            Assert.AreEqual(EActivationRefusal.Cooldown, Activate(gas, cast), "and it's refused");

            await Frames.Step(13);
            Assert.AreEqual(2, InHand(gas, cast), "its end brings both back");
        });

        /// <summary>A recharge of the author's own decides what's in hand and what a use spends: here, uses that never come back.</summary>
        [UnityTest]
        public IEnumerator TW_17_A_recharge_of_ones_own_decides([Values] Hub hub) => Live(async () =>
        {
            Stepping(0.1f);
            var gas = Caster(hub);
            var potion = Quick("Potion");
            potion.Charges = 2;
            potion.Recharge = new LimitedUses();
            await Frames.Step();
            Give(gas, potion);

            Assert.AreEqual(EActivationRefusal.None, Activate(gas, potion));
            Assert.AreEqual(EActivationRefusal.None, Activate(gas, potion));
            Assert.AreEqual(0, InHand(gas, potion), "two uses spent");
            Assert.AreEqual(EActivationRefusal.Cooldown, Activate(gas, potion), "refused with none in hand");
            await Frames.Step(30);
            Assert.AreEqual(0, InHand(gas, potion), "and they never come back");
        });
    }
}
