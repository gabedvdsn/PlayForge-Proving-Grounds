using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Every authored number is Scalable (FN-19, Decision 83), read at the level of what holds it: an effect's stack bound
    /// at its source's, as the effect carries it, and an item's stack size at the item's own.
    /// </summary>
    public class ScalableNumberTests : ProvingGroundsTest
    {
        // A whole number of 2 at level 1 and 4 after, set by name, as a number of any kind
        private static void ByLevel(object owner, string name)
        {
            var field = owner.GetType().GetField(name);
            Assert.AreEqual(typeof(ScalableInt), field?.FieldType, $"{owner.GetType().Name}.{name} is a Scalable whole number");
            field.SetValue(owner, new ScalableInt(new SimpleScaler { LevelValues = new[] { 2f, 4f, 4f, 4f }, MaxLevel = 4, StretchOverLevels = false }, 1));
        }

        // An entity at a level of its own, which the effects it applies carry
        private IGameplayAbilitySystem Source(string name, int level)
        {
            var identity = Make.RuntimeIdentity(name, Make.UnusedSet());
            identity.StartingLevel = level;
            identity.MaxLevel = 4;
            var gas = new RuntimeGameplayAbilitySystem(identity);
            World.Register(gas);
            return gas;
        }

        private static int Stacks(IGameplayAbilitySystem gas, GameplayEffect effect)
        {
            Assert.That(gas.TryGetEffectContainer(effect, out var container), "the effect is on the entity");
            return ((AbstractStackingEffectContainer)container).Stacks;
        }

        /// <summary>
        /// A stack bound's Max was a plain number (FN-19): as a Scalable one it reads at the level its effect carries, so a
        /// bleed holds 2 stacks from a novice and 4 from a master.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_19_A_stack_bound_reads_at_its_sources_level([Values] Hub hub) => Live(async () =>
        {
            var novice = Source("Novice", 1);
            var master = Source("Master", 2);
            var first = World.Entity(hub);
            var second = World.Entity(hub);
            var bleed = Make.Effect("Bleed", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 5f, Target = null,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = EEffectStackPolicy.StacksShareOneDuration, StackAmount = 1,
            });
            var bound = new StackBoundsConfig();
            ByLevel(bound, nameof(StackBoundsConfig.Max));
            bleed.DurationSpecification.StackingConfigs.Add(bound);

            for (int i = 0; i < 6; i++) World.Apply(novice, first, bleed);
            Assert.AreEqual(2, Stacks(first, bleed), "from a level 1 source, its Max is 2");
            for (int i = 0; i < 6; i++) World.Apply(master, second, bleed);
            Assert.AreEqual(4, Stacks(second, bleed), "from a level 2 source, 4");
            await Frames.Step();
        });

        /// <summary>
        /// An item's Max Stack Size was a plain number (FN-19): as a Scalable one it reads at the item's level, so a slot of
        /// a level 1 shard holds 2, and of a level 2 crystal, 4.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_19_An_items_stack_size_reads_at_its_level([Values] Hub hub) => Live(async () =>
        {
            var items = World.Entity(hub).GetItemSystem();
            var shard = Make.Item("Shard");
            ByLevel(shard.Definition, nameof(ItemDefinition.MaxStackSize));
            var crystal = Make.Item("Crystal");
            crystal.StartingLevel = 2;
            ByLevel(crystal.Definition, nameof(ItemDefinition.MaxStackSize));

            Assert.AreEqual(2, items.GiveItem(shard, 5), "a level 1 shard's slot holds 2");
            Assert.AreEqual(4, items.GiveItem(crystal, 5), "a level 2 crystal's, 4");
            await Frames.Step();
        });
    }
}
