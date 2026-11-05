using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The review's mirror clones, a stretch scenario: a GAS's full state copied into a new runtime GAS. With FEAT-9 a copy
    /// took a catalog naming every asset and IDs for every entity, which assets made as the game runs never have, and the
    /// entity's own level stayed behind on a clone of another kind (FEAT-20).
    /// </summary>
    [Category("Stretch")]
    public class MirrorCloneTests : ProvingGroundsTest
    {
        // A clock that moves only when the round says
        private sealed class ManualClock : IForgeClock
        {
            public float Step;
            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        private static float Value(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>
        /// A mirror clone takes its original's state onto a new runtime GAS, with no catalog: its health, its effects with
        /// their time left and their sources, its abilities and items, its loose tags and its level. Then the two go their
        /// own ways (FEAT-20).
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_A_mirror_clone_takes_its_originals_state([Values] Hub hub) => Live(async () =>
        {
            var clock = new ManualClock();
            var previous = ForgeTime.Use(clock);
            try
            {
                var health = Make.Attribute("Health");
                var speed = Make.Attribute("Speed");
                var power = Make.Attribute("Power");
                var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase), Make.Element(speed, 10f, 10f),
                    Make.Element(power, 0f, 0f));
                var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -35f });
                var haste = Make.Effect("Haste", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = speed, Magnitude = 5f });
                var curse = Make.Effect("Curse", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = power, Magnitude = -3f });
                var fireball = Make.Ability("Fireball");
                var focus = Make.Effect("Focus", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = power, Magnitude = 10f });
                var staff = Make.Item("Staff");
                staff.GrantedEffects = new List<GameplayEffect> { focus };
                var inspired = Make.NewTag("State.Inspired");

                var mage = World.Entity(hub, set, "Mage");
                var witch = World.Entity(hub, set, "Witch");
                Assert.IsTrue(World.Apply(mage, mage, hit), "the mage is hit");
                Assert.IsTrue(World.Apply(mage, mage, haste), "and hasted");
                Assert.IsTrue(World.Apply(witch, mage, curse), "and cursed by the witch");
                Assert.IsTrue(mage.GetAbilitySystem().GiveAbility(fireball, 2, out _), "and knows Fireball at level 2");
                Assert.IsTrue(mage.GetItemSystem().GiveAndEquipItem(staff, out _), "and holds a staff");
                mage.AddLooseTag(inspired);
                Assert.IsTrue(mage.FindLevelSystem(out var levels));
                levels.TrySetLevel(mage.GetAssetTag(), new IntValuePair(3, 10));
                Assert.AreEqual(3, mage.GetLevel().CurrentValue, "the mage is at level 3");
                clock.Step = 1f;
                await Frames.Step(2);
                clock.Step = 0f;
                await Frames.Step();
                Assert.IsTrue(mage.TryGetEffectContainer(haste, out var hasted));
                float left = hasted.DurationRemaining;

                // A runtime GAS of its own kind, which none of this was given
                var clone = World.Entity(Hub.Runtime, set, "Clone");
                Assert.IsTrue(ForgeSave.Copy(mage, clone), "the mage's state copies onto the clone");
                await Frames.Step();

                Assert.AreEqual(65f, Value(clone, health), "the clone is as hurt");
                Assert.IsTrue(clone.TryGetEffectContainer(haste, out var cloneHasted), "as hasted");
                Assert.AreEqual(left, cloneHasted.DurationRemaining, 0.001f, "for as long");
                Assert.AreEqual(15f, Value(clone, speed));
                Assert.IsTrue(clone.TryGetEffectContainer(curse, out var cursed) && ReferenceEquals(cursed.Spec.Source, witch),
                    "and cursed by the same witch");
                Assert.IsTrue(clone.GetAbilitySystem().TryGetAbilityContainer(fireball, out var known), "it knows Fireball");
                Assert.AreEqual(2, known.Spec.GetLevel().CurrentValue, "at level 2");
                Assert.IsTrue(clone.GetItemSystem().TryGetItemContainer(staff, out var held) && held.IsEquipped, "it holds the staff");
                Assert.AreEqual(7f, Value(clone, power), "whose focus is on once, with the curse");
                Assert.IsTrue(clone.GetTagCache().GetWeight(inspired) > 0, "it's inspired");
                Assert.AreEqual(3, clone.GetLevel().CurrentValue, "and at the mage's level, though another kind");

                Assert.IsTrue(World.Apply(clone, clone, hit), "the clone is hit again");
                await Frames.Step();
                Assert.AreEqual(30f, Value(clone, health), "and only the clone is hurt");
                Assert.AreEqual(65f, Value(mage, health), "the mage isn't");
                clone.RemoveGameplayEffect(haste);
                await Frames.Step(2);
                Assert.AreEqual(10f, Value(clone, speed), "the clone's haste goes alone");
                Assert.AreEqual(15f, Value(mage, speed), "and the mage's stays");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });
    }
}
