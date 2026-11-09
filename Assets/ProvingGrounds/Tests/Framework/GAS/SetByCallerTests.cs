using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// SetByCaller magnitudes (FEAT-2, PF-C01): an effect's amount came only from its asset and its scalers, so code that
    /// knew the amount as it applied the effect couldn't pass it in. A magnitude set by the caller under a tag, on the
    /// spec or on the activation's data, is what a Set By Caller scaler reads, and each effect made from that data takes it.
    /// </summary>
    public class SetByCallerTests : ProvingGroundsTest
    {
        private static AbstractScaler SetByCaller(Tag key, float fallback = 0f) => new SetByCallerScaler { Key = key, Default = fallback };

        private static float Value(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // A hit of 1 times what the caller sets under the damage tag
        private GameplayEffect Hit(Attribute health, Tag damage, EEffectDurationPolicy policy = EEffectDurationPolicy.Instant) =>
            Make.Effect("Hit", new EffectConfig
            {
                Policy = policy, Target = health, Magnitude = -1f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = SetByCaller(damage),
            });

        /// <summary>A magnitude set on the spec before it's applied is the effect's amount; with none set, the scaler reads its default (FEAT-2).</summary>
        [UnityTest]
        public IEnumerator FEAT_2_A_magnitude_set_by_the_caller_lands([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var damage = Make.NewTag("Data.Damage");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var hit = Hit(health, damage);

            var spec = gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(gas), hit);
            spec.SetByCaller(damage, 42f);
            Assert.IsTrue(gas.ApplyGameplayEffect(spec), "a hit of 42 lands");
            await Frames.Step();
            Assert.AreEqual(58f, Value(gas, health), "for 42");

            Assert.IsTrue(World.Apply(gas, gas, hit), "a hit with nothing set lands");
            await Frames.Step();
            Assert.AreEqual(58f, Value(gas, health), "for its default, 0");
        });

        /// <summary>
        /// A magnitude set on an activation's data reaches each effect the ability applies, as it's made: the hit, the
        /// burn it contains, and the cooldown, whose length the caller sets too (FEAT-2).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_2_An_ability_passes_the_callers_magnitude_to_its_effects([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var damage = Make.NewTag("Data.Damage");
            var rest = Make.NewTag("Data.Rest");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            var hit = Hit(health, damage);
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -0.5f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = SetByCaller(damage),
            });
            hit.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = burn } };
            var strike = Make.Ability("Strike", Make.Stage(new ApplyEffectsAbilityTask { Effects = new System.Collections.Generic.List<GameplayEffect> { hit } }));
            strike.Behaviour.Stages[0].ApplyUsageEffects = true;
            strike.Cooldown = Make.Effect("Strike Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 1f, Target = null });
            strike.Cooldown.DurationSpecification.DurationOperation = new ScalableFloat
            {
                Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = SetByCaller(rest, 1f),
            };
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(strike, 1, out int index), "the caster knows Strike");

            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            data.SetByCaller(damage, 30f);
            data.SetByCaller(rest, 8f);
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, data)), "Strike, for 30, activates");
            await Frames.Until(() => !abilities.IsExecuting, 10, "and ends");

            Assert.AreEqual(55f, Value(target, health), "the hit lands for 30, and the burn it contains for 15");
            Assert.IsTrue(caster.TryGetEffectContainer(strike.Cooldown, out var cooldown), "Strike is on cooldown");
            Assert.AreEqual(8f, cooldown.TotalDuration, 0.001f, "for the 8 s the caller set");
        });

        /// <summary>A save keeps an effect's set-by-caller magnitudes, so a modifier that reads one comes back with it (FEAT-2, FEAT-9).</summary>
        [UnityTest]
        public IEnumerator FEAT_2_A_save_keeps_a_magnitude_set_by_the_caller([Values] Hub hub) => Live(async () =>
        {
            var might = Make.Attribute("Might");
            var bonus = Make.NewTag("Data.Bonus");
            var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f)));
            var blessing = Make.Effect("Blessing", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = might, Magnitude = 1f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = SetByCaller(bonus),
            });
            var spec = gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(gas), blessing);
            spec.SetByCaller(bonus, 7f);
            Assert.IsTrue(gas.ApplyGameplayEffect(spec), "a blessing of 7 lands");
            await Frames.Step();
            Assert.AreEqual(7f, Value(gas, might), "for 7");

            var catalog = UnityEngine.ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            catalog.Add("might", might);
            catalog.Add("blessing", blessing);
            var snapshot = ForgeSave.Capture(gas, catalog);
            gas.RemoveGameplayEffect(blessing);
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(gas, might), "the blessing is gone");

            Assert.IsTrue(ForgeSave.Restore(gas, snapshot, catalog), "the save loads");
            await Frames.Step();
            Assert.AreEqual(7f, Value(gas, might), "and the blessing is back for its 7");
        });
    }
}
