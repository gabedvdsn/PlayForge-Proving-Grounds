using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-14: effects that land on the caster. Each contained effect, an overflow's effects and each effect a modifier adds
    /// says whom it lands on: the target, as it always did, or the caster, as a lifesteal's heal, a frenzy on overflowing
    /// a bleed, or a rebound as a curse ends.
    /// </summary>
    public class RecipientTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag healed, wounded, frenzied, rebounded;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            healed = Make.NewTag("Test.TW14.Healed");
            wounded = Make.NewTag("Test.TW14.Wounded");
            frenzied = Make.NewTag("Test.TW14.Frenzied");
            rebounded = Make.NewTag("Test.TW14.Rebounded");
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) =>
            World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        private GameplayEffect Granting(string name, Tag tag) => Make.Effect(name, new EffectConfig
        {
            Policy = EEffectDurationPolicy.Durational, Duration = 5f, GrantedTags = new List<Tag> { tag },
        });

        private GameplayEffect Strike() =>
            Make.Effect("Strike", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });

        private static bool Has(IGameplayAbilitySystem gas, Tag tag) => gas.GetTagCache().GetWeight(tag) > 0;

        /// <summary>A contained effect lands on the caster where it says, and on the target where it doesn't.</summary>
        [UnityTest]
        public IEnumerator TW_14_A_contained_effect_lands_on_whom_it_says([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            var strike = Strike();
            strike.ImpactSpecification.Packets = new[]
            {
                new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = Granting("Lifesteal", healed), LandsOn = EEffectRecipient.Caster },
                new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = Granting("Wound", wounded) },
            };
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, strike));
            await Frames.Step(2);
            Assert.IsTrue(Has(caster, healed), "the lifesteal lands on its caster");
            Assert.IsFalse(Has(target, healed), "not on the one it struck");
            Assert.IsTrue(Has(target, wounded), "and the wound, which says nothing, on the target, as it always did");
            Assert.IsFalse(Has(caster, wounded));
        });

        /// <summary>One that lands as its effect ends lands on the caster where it says.</summary>
        [UnityTest]
        public IEnumerator TW_14_One_as_its_effect_ends_lands_on_the_caster([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            var curse = Make.Effect("Curse", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            curse.ImpactSpecification.Packets = new[]
            {
                new ContainedEffectPacket { Policy = EApplyTickRemove.OnRemove, ContainedEffect = Granting("Rebound", rebounded), LandsOn = EEffectRecipient.Caster },
            };
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, curse));
            await Frames.Step(2);
            Assert.IsFalse(Has(caster, rebounded), "nothing yet");
            Assert.IsTrue(target.GetEffectSystem().RemoveGameplayEffect(curse), "the curse ends");
            await Frames.Step(3);
            Assert.IsTrue(Has(caster, rebounded), "and its rebound lands on its caster");
            Assert.IsFalse(Has(target, rebounded), "not on the one it cursed");
        });

        /// <summary>An overflow's effects land on the caster where its bound says.</summary>
        [UnityTest]
        public IEnumerator TW_14_An_overflow_lands_on_the_caster([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            var bleed = Make.Effect("Bleed", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 10f, FixedDeltaTime = 1f,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = EEffectStackPolicy.StacksShareOneDuration,
                Interaction = EEffectInteractionPolicy.RefreshContainerDuration,
                StackAmount = 1,
            });
            bleed.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig
            {
                Min = 0, Max = 2, OverflowEffects = new List<GameplayEffect> { Granting("Frenzy", frenzied) },
                OverflowLandsOn = EEffectRecipient.Caster,
            });
            await Frames.Step();

            for (int i = 0; i < 3; i++) World.Apply(caster, target, bleed);
            await Frames.Step(2);
            Assert.IsTrue(Has(caster, frenzied), "a third stack overflows, and the frenzy lands on the one who bled it");
            Assert.IsFalse(Has(target, frenzied), "not on the one bleeding");
        });

        /// <summary>An effect a modifier adds lands on the caster where its change says.</summary>
        [UnityTest]
        public IEnumerator TW_14_A_modifiers_added_effect_lands_on_the_caster([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var target = Fighter(hub, "Target");
            var fang = Make.Item("Vampire Fang");
            fang.Modifiers.Add(new EffectModifierEntry
            {
                Rule = new EffectModifierRule
                {
                    Direction = EModifierDirection.Outgoing,
                    Changes = new List<AbstractEffectChange>
                    {
                        new AddContainedEffectChange { Effect = Granting("Siphon", healed), When = EApplyTickRemove.OnApply, LandsOn = EEffectRecipient.Caster },
                    },
                },
            });
            await Frames.Step();
            Assert.That(caster.GetItemSystem().GiveAndEquipItem(fang, out _), "the fang's equipped");

            Assert.IsTrue(World.Apply(caster, target, Strike()));
            await Frames.Step(2);
            Assert.IsTrue(Has(caster, healed), "the siphon its modifier adds lands on its holder, the caster");
            Assert.IsFalse(Has(target, healed), "not on the one it struck");
        });
    }
}
