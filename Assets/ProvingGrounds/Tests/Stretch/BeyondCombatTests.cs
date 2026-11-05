using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The review's beyond combat, a stretch scenario: crop growth, a crafting timer and a dialogue gate, built from
    /// effects, tags and abilities, as PlayForge would serve a game with no fighting in it. A crop ripens through its
    /// stages, faster when watered, and blooms as it ripens (FEAT-4); a smelter makes an ingot each tick of its timer, and
    /// takes more orders; a gate in a conversation opens once the player has met the keeper and read the letter.
    /// </summary>
    [Category("Stretch")]
    public class BeyondCombatTests : ProvingGroundsTest
    {
        // A clock of a second a frame, so a stage of ten seconds is ten frames
        private sealed class SecondClock : IForgeClock
        {
            public float DeltaTime => 1f;
            public float UnscaledDeltaTime => 1f;
            public float FixedDeltaTime => 1f;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        private sealed class LabTime : IDisposable
        {
            private readonly IForgeClock previous = ForgeTime.Use(new SecondClock());
            public void Dispose() => ForgeTime.Use(previous);
        }

        private static float Value(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static void Then(GameplayEffect stage, GameplayEffect next) =>
            stage.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnRemove, ContainedEffect = next } };

        /// <summary>
        /// A crop ripens through its stages, a seedling then a sprout, ten seconds each, each stage's end bringing on the
        /// next; watered, its time runs twice as fast. As it ripens, it blooms: its Bloom ability, which gaining the Ripe tag
        /// triggers, makes it fragrant (FEAT-4, FEAT-7).
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_A_crop_ripens_through_its_stages_and_blooms([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var seedlingTag = Make.NewTag("Crop.Stage.Seedling");
            var sproutTag = Make.NewTag("Crop.Stage.Sprout");
            var ripeTag = Make.NewTag("Crop.Stage.Ripe");
            var fragrantTag = Make.NewTag("Crop.Fragrant");
            var ripe = Make.Effect("Ripe", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { ripeTag } });
            var sprout = Make.Effect("Sprout", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = null, GrantedTags = new List<Tag> { sproutTag } });
            var seedling = Make.Effect("Seedling", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = null, GrantedTags = new List<Tag> { seedlingTag } });
            Then(seedling, sprout);
            Then(sprout, ripe);
            var fragrant = Make.Effect("Fragrant", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { fragrantTag } });
            var bloom = Make.Ability("Bloom", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { fragrant } }));
            bloom.Behaviour.UseImplicitTargeting = true;
            bloom.Definition.Triggers = new List<AbilityTrigger> { new() { Source = EAbilityTriggerSource.TagAdded, Tag = ripeTag } };

            var wheat = World.Entity(hub, null, "Wheat");
            var watered = World.Entity(hub, null, "Watered Wheat");
            foreach (var crop in new[] { wheat, watered })
            {
                Assert.IsTrue(crop.GetAbilitySystem().GiveAbility(bloom, 1, out _), "a crop can bloom");
                Assert.IsTrue(World.Apply(crop, crop, seedling), "and is sown");
            }
            watered.GetClock().TimeScale = 2f;

            await Frames.Step(11);
            Assert.IsTrue(watered.GetTagCache().GetWeight(ripeTag) > 0, "after ten seconds the watered crop is ripe");
            Assert.IsTrue(wheat.GetTagCache().GetWeight(sproutTag) > 0, "and the other a sprout");
            await Frames.Until(() => watered.GetTagCache().GetWeight(fragrantTag) > 0, 4, "the ripe crop blooms");

            await Frames.Step(11);
            Assert.IsTrue(wheat.GetTagCache().GetWeight(ripeTag) > 0, "after twenty, the other is ripe");
            await Frames.Until(() => wheat.GetTagCache().GetWeight(fragrantTag) > 0, 4, "and blooms");
            Assert.IsTrue(wheat.GetTagCache().GetWeight(seedlingTag) < 1 && wheat.GetTagCache().GetWeight(sproutTag) < 1,
                "its earlier stages behind it");
        });

        /// <summary>
        /// A smelter makes an ingot each tick of its timer, three over fifteen seconds; an order taken while it works extends
        /// the timer, and makes two more.
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_A_smelter_makes_an_ingot_each_tick([Values] Hub hub) => Live(async () =>
        {
            using var time = new LabTime();
            var ingots = Make.Attribute("Ingots");
            var smelter = World.Entity(hub, Make.Set(Make.Element(ingots, 0f, 0f)), "Smelter");
            var smelting = Make.Effect("Smelting", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 15f, Target = ingots, Magnitude = 1f, PeriodicTicks = true, Ticks = 3,
                Reapply = EEffectReApplicationPolicy.DoNothing, Interaction = EEffectInteractionPolicy.ExtendContainerDuration,
            });

            Assert.IsTrue(World.Apply(smelter, smelter, smelting), "an order of three goes in");
            await Frames.Step(6);
            Assert.AreEqual(1f, Value(smelter, ingots), "after six seconds, one ingot");
            Assert.IsTrue(World.Apply(smelter, smelter, smelting), "another order goes in, extending the timer");
            await Frames.Until(() => !smelter.TryGetEffectContainer(smelting, out _), 30, "the smelter finishes");
            Assert.AreEqual(6f, Value(smelter, ingots), "having made an ingot every five seconds of thirty");
        });

        /// <summary>
        /// A gate in a conversation: the player greets the keeper, a gameplay event that sets off the keeper's greeting back,
        /// which marks the player as met (FEAT-4). Asking about the ruins needs both that and the letter read, and until
        /// then is refused for its requirements (FEAT-17).
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_A_dialogue_gate_opens_once_its_tags_are_met([Values] Hub hub) => Live(async () =>
        {
            var greet = Make.NewTag("Dialogue.Greet");
            var metTag = Make.NewTag("Story.Met.Keeper");
            var readTag = Make.NewTag("Story.Read.Letter");
            var met = Make.Effect("Met Keeper", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { metTag } });
            var greetBack = Make.Ability("Greet Back", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { met } }));
            greetBack.Definition.Triggers = new List<AbilityTrigger> { new() { Source = EAbilityTriggerSource.GameplayEvent, Tag = greet } };
            var ask = Make.Ability("Ask About The Ruins", Make.Stage(new ProbeAbilityTask(World, "Ruins", frames: 1)));
            ask.Tags.TagRequirements.SourceRequirements.RequireTags.Add(new TagQuery { Tag = metTag });
            ask.Tags.TagRequirements.SourceRequirements.RequireTags.Add(new TagQuery { Tag = readTag });

            var player = World.Entity(hub, null, "Player");
            var keeper = World.Entity(hub, null, "Keeper");
            Assert.IsTrue(keeper.GetAbilitySystem().GiveAbility(greetBack, 1, out _), "the keeper greets back");
            Assert.IsTrue(player.GetAbilitySystem().GiveAbility(ask, 1, out _), "the player can ask about the ruins");

            Assert.IsFalse(player.GetAbilitySystem().TryActivateAbility(ask, out var refusal), "a stranger can't ask yet");
            Assert.AreEqual(EActivationRefusal.Requirements, refusal.Reason, "for want of what it needs");

            var greeting = ProcessDataPacket.Default();
            greeting.SetPrimary(Tags.TARGET_REAL, (ITarget)player);
            Assert.AreEqual(1, keeper.GetAbilitySystem().SendGameplayEvent(greet, greeting), "the player greets the keeper, who greets back");
            await Frames.Until(() => player.GetTagCache().GetWeight(metTag) > 0, 10, "and the player has met the keeper");
            Assert.IsFalse(player.GetAbilitySystem().TryActivateAbility(ask, out refusal), "but hasn't read the letter");

            player.AddLooseTag(readTag);
            Assert.IsTrue(player.GetAbilitySystem().TryActivateAbility(ask, out refusal), $"with both, the player asks ({refusal})");
            await Frames.Until(() => Ledger.Cleans("Ruins") == 1, 10, "and hears about the ruins");
        });
    }
}
