using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-7, Decision 47: effect modifiers. An item, an ability, an effect or an entity changes other effects as they're
    /// made, those its holder applies or those landing on it, picked by tag: a contained effect added, an impact's number
    /// through a Value Pipeline stage, an overflow, its time, its stacks, its chance and its granted tags. An entry is its
    /// own rule or names a shared Effect Modifier asset.
    /// </summary>
    public class EffectModifierTests : ProvingGroundsTest
    {
        private ForgeAttribute health, armor;
        private Tag chill, marked, frozen;

        [SetUp]
        public void Tags()
        {
            health = Make.Attribute("Health");
            armor = Make.Attribute("Armor");
            chill = Make.NewTag("Test.TW7.Status.Chill");
            marked = Make.NewTag("Test.TW7.Marked");
            frozen = Make.NewTag("Test.TW7.Frozen");
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name, float hp = 100f) => World.Entity(hub, Make.Set(
            Make.Element(health, hp, hp, EAttributeOverflowPolicy.ZeroToBase),
            Make.Element(armor, 100f, 100f)), name);

        private GameplayEffect Tagged(GameplayEffect effect, Tag tag)
        {
            effect.Tags.ContextTags.Add(tag);
            return effect;
        }

        private GameplayEffect Mark() =>
            Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { marked } });

        private GameplayEffect Chill(float seconds = 0f) => Tagged(Make.Effect("Chill", new EffectConfig
        {
            Policy = seconds > 0f ? EEffectDurationPolicy.Durational : EEffectDurationPolicy.Infinite, Duration = seconds,
        }), chill);

        private GameplayEffect Blow(float amount) =>
            Tagged(Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount }), chill);

        private static EffectModifierEntry Entry(EModifierDirection direction, Tag picks, params AbstractEffectChange[] changes) => new()
        {
            Rule = new EffectModifierRule
            {
                Direction = direction, EffectTags = new List<Tag> { picks }, Changes = new List<AbstractEffectChange>(changes),
            },
        };

        private Item Holding(IGameplayAbilitySystem holder, params EffectModifierEntry[] entries)
        {
            var item = Make.Item("Boon");
            item.Modifiers.AddRange(entries);
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(item, out _), "the boon is held");
            return item;
        }

        private static bool Has(IGameplayAbilitySystem gas, Tag tag) => gas.GetTagCache().GetWeight(tag) > 0;

        private float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>An item's modifier adds a contained effect to the effects its holder applies that it picks by tag, and no other.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifier_adds_a_contained_effect_to_what_it_picks([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            Holding(holder, Entry(EModifierDirection.Outgoing, chill, new AddContainedEffectChange { Effect = Mark() }));
            var plain = Make.Effect("Plain", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, first, Chill()), "a chill");
            Assert.IsTrue(World.Apply(holder, second, plain), "and a plain effect");
            await Frames.Step(2);
            Assert.IsTrue(Has(first, marked), "the chill brings the mark with it");
            Assert.IsFalse(Has(second, marked), "the plain effect doesn't");
        });

        /// <summary>A modifier's stage changes a hit's number after the effect's own, and the hit's breakdown keeps it.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifier_changes_a_hits_number_by_a_pipeline_stage([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            Holding(holder, Entry(EModifierDirection.Outgoing, chill,
                new ChangeImpactChange { Attribute = health, Stage = new MultiplyStage { Label = "Frost", By = 2f } }));
            await Frames.Step();

            var spec = target.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(holder), Blow(10f));
            Assert.IsTrue(target.ApplyGameplayEffect(spec), "a blow lands");
            await Frames.Step(2);
            Assert.AreEqual(80f, Value(target, health), 0.01f, "for twice its 10");
            Assert.IsNotNull(spec.Breakdown, "and its breakdown");
            Assert.AreEqual(1, spec.Breakdown.Steps.Count, "keeps the stage");
            Assert.AreEqual(-20f, spec.Breakdown.Steps[0].After, 0.01f, "with what it gave");
        });

        /// <summary>A lasting impact, which no pipeline runs on, takes the stage on its magnitude.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_lasting_impact_takes_the_stage_on_its_magnitude([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            Holding(holder, Entry(EModifierDirection.Outgoing, chill,
                new ChangeImpactChange { Attribute = armor, Stage = new MultiplyStage { By = 2f } }));
            var sunder = Tagged(Make.Effect("Sunder", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = armor, Magnitude = -10f }), chill);
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, target, sunder));
            await Frames.Step(2);
            Assert.AreEqual(80f, Value(target, armor), 0.01f, "its −10 armor doubled while it lasts");
        });

        /// <summary>A modifier adds an overflow to an effect that has a stack bound, and clears its stacks with it.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifier_adds_an_overflow([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            var burst = Make.Effect("Arctic Blast", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f });
            Holding(holder, Entry(EModifierDirection.Outgoing, chill, new AddOverflowChange { Effect = burst, ClearStacks = true }));
            var stacking = Tagged(Make.Effect("Chill", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 10f, FixedDeltaTime = 1f,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers, StackPolicy = EEffectStackPolicy.StacksShareOneDuration,
                Interaction = EEffectInteractionPolicy.RefreshContainerDuration, StackAmount = 1,
            }), chill);
            stacking.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig { Min = 0, Max = 3 });
            await Frames.Step();

            for (int i = 0; i < 3; i++) Assert.IsTrue(World.Apply(holder, target, stacking), "a chill lands");
            await Frames.Step();
            Assert.AreEqual(100f, Value(target, health), 0.01f, "three stacks, its most, and nothing more");
            World.Apply(holder, target, stacking);
            await Frames.Step(2);
            Assert.AreEqual(70f, Value(target, health), 0.01f, "a fourth bursts as Arctic Blast");
            Assert.IsFalse(target.TryGetEffectContainer(stacking, out _), "and the chill's stacks clear");
        });

        /// <summary>A modifier changes how long an effect lasts: its time multiplied, then more added.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifier_changes_how_long_an_effect_lasts([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            Holding(holder, Entry(EModifierDirection.Outgoing, chill, new DurationChange { Multiply = 2f, AddSeconds = 1f }));
            var effect = Chill(5f);
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, target, effect));
            Assert.IsTrue(target.TryGetEffectContainer(effect, out var container), "it lands");
            Assert.AreEqual(11f, container.TotalDuration, 0.01f, "for twice its 5 s, and one more");
            Assert.AreEqual(11f, container.DurationRemaining, 0.1f, "from the start");
        });

        /// <summary>A modifier changes an effect's stacks: more it can hold, and more a landing gives.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifier_changes_its_stacks([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            Holding(holder, Entry(EModifierDirection.Outgoing, chill, new StacksChange { MostStacks = 2, StacksPerLanding = 1 }));
            var stacking = Tagged(Make.Effect("Chill", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = EEffectStackPolicy.StacksShareOneDuration, StackAmount = 1,
            }), chill);
            stacking.DurationSpecification.StackingConfigs.Add(new StackBoundsConfig { Min = 0, Max = 3 });
            await Frames.Step();

            int Stacks() => target.TryGetEffectContainer(stacking, out var c) ? ((AbstractStackingEffectContainer)c).Stacks : 0;
            Assert.IsTrue(World.Apply(holder, target, stacking));
            Assert.AreEqual(2, Stacks(), "a landing gives two");
            World.Apply(holder, target, stacking);
            Assert.AreEqual(4, Stacks(), "and two more, past its own most of 3");
            World.Apply(holder, target, stacking);
            Assert.AreEqual(5, Stacks(), "up to its most and the two more");
        });

        /// <summary>A modifier changes an effect's chance to apply.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifier_changes_its_chance([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            var effect = Chill();
            effect.ImpactSpecification.ChanceToApply = new ScalableFloat { Magnitude = 0f, RealMagnitude = EMagnitudeOperation.UseMagnitude };
            await Frames.Step();
            Assert.IsFalse(World.Apply(holder, target, effect), "with no chance, it doesn't land");

            Holding(holder, Entry(EModifierDirection.Outgoing, chill, new ChanceChange { Add = 1f }));
            Assert.IsTrue(World.Apply(holder, target, effect), "a modifier's whole chance lands it");
        });

        /// <summary>A modifier adds tags an effect grants while it's on, which go as it does.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifier_adds_the_tags_it_grants([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            Holding(holder, Entry(EModifierDirection.Outgoing, chill, new GrantTagsChange { Tags = new List<Tag> { frozen } }));
            var effect = Chill();
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, target, effect));
            await Frames.Step(2);
            Assert.IsTrue(Has(target, frozen), "the chill grants Frozen while it's on");
            target.RemoveGameplayEffect(effect);
            await Frames.Step(2);
            Assert.IsFalse(Has(target, frozen), "and it goes with it");
        });

        /// <summary>An incoming modifier changes what lands on its holder, whoever applies it; an outgoing one doesn't.</summary>
        [UnityTest]
        public IEnumerator TW_7_An_incoming_modifier_changes_what_lands_on_its_holder([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var resistant = Fighter(hub, "Resistant");
            var plain = Fighter(hub, "Plain");
            Holding(resistant, Entry(EModifierDirection.Incoming, chill, new DurationChange { Multiply = 0.5f }));
            Holding(plain, Entry(EModifierDirection.Outgoing, chill, new DurationChange { Multiply = 0.5f }));
            var effect = Chill(10f);
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, resistant, effect));
            Assert.IsTrue(World.Apply(caster, plain, effect));
            Assert.IsTrue(resistant.TryGetEffectContainer(effect, out var onResistant));
            Assert.IsTrue(plain.TryGetEffectContainer(effect, out var onPlain));
            Assert.AreEqual(5f, onResistant.TotalDuration, 0.01f, "the chill lasts half as long on the resistant");
            Assert.AreEqual(10f, onPlain.TotalDuration, 0.01f, "and as long as ever on one whose modifier is its own effects'");
        });

        /// <summary>An entry that names a shared Effect Modifier uses its rule in place of its own.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_shared_modifier_stands_in_for_an_entrys_own([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            var shared = ScriptableObject.CreateInstance<EffectModifier>();
            shared.Rule = new EffectModifierRule
            {
                EffectTags = new List<Tag> { chill }, Changes = new List<AbstractEffectChange> { new AddContainedEffectChange { Effect = Mark() } },
            };
            try
            {
                Holding(holder, new EffectModifierEntry { Shared = shared });
                await Frames.Step();
                Assert.IsTrue(World.Apply(holder, target, Chill()));
                await Frames.Step(2);
                Assert.IsTrue(Has(target, marked), "the shared modifier's mark comes with the chill");
            }
            finally
            {
                Object.Destroy(shared);
            }
        });

        /// <summary>An effect a modifier adds, which it would pick again, isn't changed by it again.</summary>
        [UnityTest]
        public IEnumerator TW_7_A_modifiers_added_effect_isnt_changed_by_it_again([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var target = Fighter(hub, "Target");
            var echo = Tagged(Mark(), chill);
            Holding(holder, Entry(EModifierDirection.Outgoing, chill, new AddContainedEffectChange { Effect = echo }));
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, target, Chill()));
            await Frames.Step(2);
            Assert.AreEqual(1, target.GetTagCache().GetWeight(marked), "one echo, not one for each echo");
        });

        /// <summary>An effect's modifiers hold on the one it's on while it lasts; an entity's own always do.</summary>
        [UnityTest]
        public IEnumerator TW_7_An_effects_modifiers_hold_while_it_lasts_and_an_entitys_always([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var boon = Make.Effect("Frost Boon", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            boon.Modifiers.Add(Entry(EModifierDirection.Outgoing, chill, new AddContainedEffectChange { Effect = Mark() }));
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, holder, boon), "the boon is on its holder");
            Assert.IsTrue(World.Apply(holder, first, Chill()));
            await Frames.Step(2);
            Assert.IsTrue(Has(first, marked), "while it's on, its chills mark");
            holder.RemoveGameplayEffect(boon);
            await Frames.Step(2);
            Assert.IsTrue(World.Apply(holder, second, Chill()));
            await Frames.Step(2);
            Assert.IsFalse(Has(second, marked), "once it's gone, they don't");

            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            var own = Entry(EModifierDirection.Outgoing, chill, new GrantTagsChange { Tags = new List<Tag> { frozen } });
            IGameplayAbilitySystem mage;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity("Frost Mage", set);
                identity.Modifiers.Add(own);
                var gas = new RuntimeGameplayAbilitySystem(identity);
                World.Register(gas);
                World.Invariants.Observe(gas);
                mage = gas;
            }
            else
            {
                var mono = World.NewMonoEntity(set, "Frost Mage");
                mono.EntityData.Modifiers.Add(own);
                World.Register(mono);
                World.Invariants.Observe(mono);
                mage = mono;
            }
            var third = Fighter(hub, "Third");
            await Frames.Step();
            Assert.IsTrue(World.Apply(mage, third, Chill()));
            await Frames.Step(2);
            Assert.IsTrue(Has(third, frozen), "an entity's own modifiers hold as it lives");
        });
    }
}
