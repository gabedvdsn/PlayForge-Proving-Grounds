using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Possession, a stretch scenario (the review's): abilities and effects moved between entities mid-cast. A ghost
    /// possesses a knight as the knight prays: the prayer, cut off, goes to the ghost, and the ghost's Haunt and its
    /// lasting effects go into the knight, keeping their time (FEAT-23); the possessed knight haunts; and as the ghost
    /// leaves, each goes back. An effect could only be taken off one entity and applied to another, which started its
    /// time again and ran its application twice.
    /// </summary>
    [Category("Stretch")]
    public class PossessionTests : ProvingGroundsTest
    {
        private static bool Holds(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.GetEffectSystem().Shelf.Any(container => container.Spec.Base == effect && container.Ongoing);

        private static float Left(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.GetEffectSystem().Shelf.Where(container => container.Spec.Base == effect).Select(container => container.DurationRemaining)
                .DefaultIfEmpty(float.NaN).Max();

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static bool Has(IGameplayAbilitySystem gas, Tag tag) => gas.GetTagCache().GetWeight(tag) > 0;

        private static bool Knows(IGameplayAbilitySystem gas, Ability ability) =>
            gas.GetAbilitySystem().GetAbilityContainers().Any(container => container.Spec.Base == ability);

        /// <summary>
        /// An effect moves from one entity to another with its time left, its lasting modifier and the tag it grants going
        /// with it, both of its containers, and nothing of its application runs there again (FEAT-23).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_23_An_effect_moves_to_another_entity_with_its_time_left([Values] Hub hub) => Live(async () =>
        {
            var armor = Make.Attribute("Armor");
            var blessed = Make.NewTag("Status.Blessed");
            var from = World.Entity(hub, Make.Set(Make.Element(armor, 10f, 10f)), "From");
            var onto = World.Entity(hub, Make.Set(Make.Element(armor, 10f, 10f)), "Onto");
            var blessing = Make.Effect("Blessing", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = armor, Magnitude = 5f, GrantedTags = new List<Tag> { blessed },
            });
            Assert.IsTrue(World.Apply(from, from, blessing), "a blessing lands");
            Assert.IsTrue(World.Apply(from, from, blessing), "and another");
            await Frames.Seconds(3f);
            float left = Left(from, blessing);
            Assert.That(left, Is.InRange(6.5f, 7.5f), "after 3 seconds they have 7 left");
            Assert.AreEqual(20f, Value(from, armor), 1e-3f, "each adds its 5");
            Assert.IsTrue(Has(from, blessed), "and the entity is blessed");

            int applied = 0;
            void Heard(GameplayEffectSpec spec) => applied++;
            onto.GetCallbacks().OnEffectApplied += Heard;
            try
            {
                Assert.AreEqual(2, from.GetEffectSystem().MoveTo(blessing, onto), "both move");
                await Frames.Step();
                Assert.IsFalse(Holds(from, blessing), "the first entity holds them no more");
                Assert.AreEqual(10f, Value(from, armor), 1e-3f, "nor their armor");
                Assert.IsFalse(Has(from, blessed), "nor their tag");
                Assert.IsTrue(Holds(onto, blessing), "the other does");
                Assert.AreEqual(20f, Value(onto, armor), 1e-3f, "with their armor");
                Assert.IsTrue(Has(onto, blessed), "and their tag");
                Assert.AreEqual(left, Left(onto, blessing), 0.2f, "with the time they had left");
                Assert.AreEqual(0, applied, "and they aren't applied there again");

                await Frames.Seconds(left + 0.5f);
                Assert.IsFalse(Holds(onto, blessing), "they end as that time runs out");
                Assert.AreEqual(10f, Value(onto, armor), 1e-3f, "their armor with them");
            }
            finally
            {
                onto.GetCallbacks().OnEffectApplied -= Heard;
            }
        });

        // What a possession moves one way: abilities, cut off where they run, and lasting effects, with their time left
        private static void Move(IGameplayAbilitySystem from, IGameplayAbilitySystem onto, IEnumerable<Ability> abilities, IEnumerable<GameplayEffect> effects)
        {
            foreach (var ability in abilities)
            {
                Assert.IsTrue(from.GetAbilitySystem().RemoveAbility(ability), $"{ability.name} leaves {from.GetName()}");
                Assert.IsTrue(onto.GetAbilitySystem().GiveAbility(ability, 1, out _), $"for {onto.GetName()}");
            }
            foreach (var effect in effects)
                Assert.That(from.GetEffectSystem().MoveTo(effect, onto), Is.GreaterThan(0), $"{effect.name} moves from {from.GetName()} to {onto.GetName()}");
        }

        /// <summary>
        /// A ghost possesses a knight as the knight prays. The prayer, cut off mid-cast, ends and goes to the ghost; the
        /// ghost's Haunt, its Ethereal and its Curse go into the knight, the Curse with the time it had left; the possessed
        /// knight haunts. As the ghost leaves, each goes back, and the knight prays again.
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_Possession_moves_abilities_and_effects_mid_cast() => Live(async () =>
        {
            var will = Make.Attribute("Will");
            var etherealTag = Make.NewTag("Status.Ethereal");
            var ghost = World.Entity(Hub.Runtime, Make.Set(Make.Element(will, 50f, 50f)), "Ghost");
            var knight = World.Entity(Hub.Mono, Make.Set(Make.Element(will, 20f, 20f)), "Knight");

            var prayer = Make.Ability("Prayer", Make.Stage(new ProbeAbilityTask(World, "Praying", frames: 600)));
            var haunt = Make.Ability("Haunt", Make.Stage(new ProbeAbilityTask(World, "Haunting", frames: 2)));
            var ethereal = Make.Effect("Ethereal", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = will, Magnitude = 10f, GrantedTags = new List<Tag> { etherealTag },
            });
            var curse = Make.Effect("Curse", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 8f, Target = will, Magnitude = -5f });

            var knightAbilities = knight.GetAbilitySystem();
            Assert.IsTrue(knightAbilities.GiveAbility(prayer, 1, out int prayerSlot), "the knight knows Prayer");
            Assert.IsTrue(ghost.GetAbilitySystem().GiveAbility(haunt, 1, out _), "the ghost knows Haunt");
            Assert.IsTrue(World.Apply(ghost, ghost, ethereal), "the ghost is ethereal");
            Assert.IsTrue(World.Apply(knight, ghost, curse), "and the knight has cursed it");
            await Frames.Step();
            Assert.AreEqual(55f, Value(ghost, will), 1e-3f, "its will 50, 10 up and 5 down");

            Assert.IsTrue(knightAbilities.TryActivateAbility(knightAbilities.CreateActivationRequest(prayerSlot)), "the knight starts to pray");
            await Frames.Seconds(1f);
            var praying = knightAbilities.GetAbilityContainers().Single(container => container.Spec.Base == prayer);
            Assert.IsTrue(praying.IsActive, "and prays on");
            float curseLeft = Left(ghost, curse);

            // The ghost possesses the knight, mid-prayer
            Move(knight, ghost, new[] { prayer }, new GameplayEffect[0]);
            Move(ghost, knight, new[] { haunt }, new[] { ethereal, curse });
            await Frames.Step(2);
            Assert.IsFalse(praying.IsActive, "the prayer, cut off, ends");
            Assert.IsFalse(Knows(knight, prayer), "and leaves the knight");
            Assert.IsTrue(Knows(ghost, prayer), "for the ghost");
            Assert.IsTrue(Knows(knight, haunt), "the knight knows Haunt");
            Assert.IsTrue(Has(knight, etherealTag) && !Has(ghost, etherealTag), "the knight is ethereal, and the ghost no more");
            Assert.AreEqual(25f, Value(knight, will), 1e-3f, "the knight's will 20, 10 up and 5 down");
            Assert.AreEqual(50f, Value(ghost, will), 1e-3f, "and the ghost's its own");
            Assert.AreEqual(curseLeft, Left(knight, curse), 0.2f, "the curse holds on, with the time it had left");

            var hauntSlot = knightAbilities.GetAbilityContainers().Single(container => container.Spec.Base == haunt).Index;
            Assert.IsTrue(knightAbilities.TryActivateAbility(knightAbilities.CreateActivationRequest(hauntSlot)), "the possessed knight haunts");
            await Frames.Step(4);
            Assert.IsFalse(knightAbilities.GetAbilityContainers().Single(container => container.Spec.Base == haunt).IsActive, "and its haunt runs its course");

            // The ghost leaves, and each goes back
            await Frames.Seconds(2f);
            curseLeft = Left(knight, curse);
            Move(knight, ghost, new[] { haunt }, new[] { ethereal, curse });
            Move(ghost, knight, new[] { prayer }, new GameplayEffect[0]);
            await Frames.Step(2);
            Assert.IsTrue(Knows(ghost, haunt) && Knows(knight, prayer), "the ghost has Haunt again, and the knight Prayer");
            Assert.IsTrue(Has(ghost, etherealTag) && !Has(knight, etherealTag), "the ghost is ethereal again");
            Assert.AreEqual(55f, Value(ghost, will), 1e-3f, "its will as it was");
            Assert.AreEqual(20f, Value(knight, will), 1e-3f, "and the knight's its own");
            Assert.AreEqual(curseLeft, Left(ghost, curse), 0.2f, "what's left of the curse with it");

            prayerSlot = knightAbilities.GetAbilityContainers().Single(container => container.Spec.Base == prayer).Index;
            Assert.IsTrue(knightAbilities.TryActivateAbility(knightAbilities.CreateActivationRequest(prayerSlot)), "and the knight prays again");
            await Frames.Step(2);
            Assert.IsTrue(knightAbilities.GetAbilityContainers().Single(container => container.Spec.Base == prayer).IsActive, "on its own");
        });
    }
}
