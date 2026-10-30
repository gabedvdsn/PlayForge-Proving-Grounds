using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// What the golden scenarios share. They're ported from the custom runner in the PlayForge package
    /// (Tests/Editor/PlayForgeGoldenTests.cs), with the same scenarios and the same expected values, but
    /// under a live ProcessControl, where the originals stepped a GAS by hand. Each runs on both hubs,
    /// where the originals ran on the runtime GAS alone.
    /// </summary>
    public abstract class GoldenTest : ProvingGroundsTest
    {
        private const float Tolerance = 0.001f;

        protected IGameplayAbilitySystem Entity(Hub hub, params AttributeSetElement[] elements)
        {
            return World.Entity(hub, Make.Set(elements));
        }

        protected bool Apply(IGameplayAbilitySystem gas, GameplayEffect effect) => World.Apply(gas, gas, effect);

        /// <summary>Removal is deferred through the finished effects to the next frame's tick.</summary>
        protected async UniTask Remove(IGameplayAbilitySystem gas, GameplayEffect effect)
        {
            gas.RemoveGameplayEffect(effect);
            await Frames.Step();
        }

        protected static void AssertValue(IGameplayAbilitySystem gas, IAttribute attribute, string what, float current, float? @base = null)
        {
            Assert.That(gas.TryGetAttributeValue(attribute, out var value), $"{what}: attribute defined");
            Assert.That(value.CurrentValue, Is.EqualTo(current).Within(Tolerance), $"{what} (Current)");
            if (@base.HasValue) Assert.That(value.BaseValue, Is.EqualTo(@base.Value).Within(Tolerance), $"{what} (Base)");
        }

        /// <summary>
        /// Asserts a value now and again a frame on: right after an application, as the originals did,
        /// and once ProcessControl has stepped the entity past it, which they couldn't.
        /// </summary>
        protected async UniTask ValueHolds(IGameplayAbilitySystem gas, IAttribute attribute, string what, float current, float? @base = null)
        {
            AssertValue(gas, attribute, what, current, @base);
            await Frames.Step();
            AssertValue(gas, attribute, $"{what}, a frame later", current, @base);
        }
    }
}
