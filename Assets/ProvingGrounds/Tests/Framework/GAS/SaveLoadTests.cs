using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Save and load (FEAT-9, Decision 30), which the Tactics lab needs: an entity at rest, its permanent commits,
    /// effects with their time left, levels, granted abilities, items and cooldowns, in plain serializable types a game
    /// writes where it likes. An asset catalog finds what a save names by an ID that lasts across sessions. A restore
    /// fires nothing per effect and then one OnRestored; running abilities end on load.
    /// </summary>
    public class SaveLoadTests : ProvingGroundsTest
    {
        private static ForgeAssetCatalog Catalog(params (string Id, ScriptableObject Asset)[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            foreach (var (id, asset) in entries) Assert.IsTrue(catalog.Add(id, asset), $"the catalog names {id}");
            return catalog;
        }

        private static ForgeSnapshot Capture(IGameplayAbilitySystem gas, ForgeAssetCatalog catalog, IForgeEntityIds ids = null)
        {
            var snapshot = ForgeSave.Capture(gas, catalog, ids);
            Assert.IsNotNull(snapshot, "the entity can be saved");
            return snapshot;
        }

        private static bool Restore(IGameplayAbilitySystem gas, ForgeSnapshot snapshot, ForgeAssetCatalog catalog, IForgeEntityIds ids = null) =>
            ForgeSave.Restore(gas, snapshot, catalog, ids);

        // A clock that moves only when the round says, as a turn-based game's does
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

        // The game's own IDs for its entities, which last across sessions
        private sealed class EntityIds : IForgeEntityIds
        {
            private readonly Dictionary<string, IGameplayAbilitySystem> byId = new();

            public EntityIds(params (string Id, IGameplayAbilitySystem Entity)[] entities)
            {
                foreach (var (id, entity) in entities) byId[id] = entity;
            }

            public string IdOf(IGameplayAbilitySystem entity) => byId.FirstOrDefault(pair => ReferenceEquals(pair.Value, entity)).Key;

            public IGameplayAbilitySystem Find(string id) => id != null && byId.TryGetValue(id, out var entity) ? entity : null;
        }

        private float Value(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>Nothing captured an entity's state (FEAT-9). A restore puts its permanent commits back as they were saved.</summary>
        [UnityTest]
        public IEnumerator FEAT_9_A_restore_puts_the_commits_back([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f });
            var catalog = Catalog(("health", health), ("hit", hit));

            World.Apply(gas, gas, hit);
            await Frames.Step();
            var snapshot = Capture(gas, catalog);
            World.Apply(gas, gas, hit);
            World.Apply(gas, gas, hit);
            await Frames.Step();
            Assert.AreEqual(10f, Value(gas, health), "two more hits land after the save");

            Assert.IsTrue(Restore(gas, snapshot, catalog), "the save loads");
            await Frames.Step();
            Assert.AreEqual(70f, Value(gas, health), "and the health is as it was saved");
        });

        /// <summary>
        /// An effect comes back with the time it had left, and its modifier, without running its application again: its
        /// contained effect on application doesn't land twice (FEAT-9).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_9_An_effect_comes_back_with_its_time_left([Values] Hub hub) => Live(async () =>
        {
            var clock = new ManualClock();
            var previous = ForgeTime.Use(clock);
            try
            {
                var might = Make.Attribute("Might");
                var sparks = Make.Attribute("Sparks");
                var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f), Make.Element(sparks, 0f, 0f)));
                var spark = Make.Effect("Spark", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = sparks, Magnitude = 1f });
                var rally = Make.Effect("Rally", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = might, Magnitude = 20f });
                rally.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = spark } };
                var catalog = Catalog(("might", might), ("sparks", sparks), ("spark", spark), ("rally", rally));

                Assert.IsTrue(World.Apply(gas, gas, rally), "the rally lands");
                clock.Step = 1f;
                await Frames.Step(3);
                clock.Step = 0f;
                await Frames.Step();
                Assert.IsTrue(gas.TryGetEffectContainer(rally, out var before), "and runs");
                float left = before.DurationRemaining;
                Assert.That(left, Is.LessThan(10f), "for a while");
                var snapshot = Capture(gas, catalog);

                gas.RemoveGameplayEffect(rally);
                await Frames.Step(2);
                Assert.AreEqual(0f, Value(gas, might), "the rally is gone");

                Assert.IsTrue(Restore(gas, snapshot, catalog), "the save loads");
                await Frames.Step();
                Assert.IsTrue(gas.TryGetEffectContainer(rally, out var after), "the rally is back");
                Assert.AreEqual(left, after.DurationRemaining, 0.001f, "with the time it had left");
                Assert.AreEqual(20f, Value(gas, might), "and its +20");
                Assert.AreEqual(1f, Value(gas, sparks), "its application didn't run again");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>Stacks with durations of their own come back each with its own time left (FEAT-9).</summary>
        [UnityTest]
        public IEnumerator FEAT_9_Stacks_come_back_each_with_its_own_time([Values] Hub hub) => Live(async () =>
        {
            var clock = new ManualClock();
            var previous = ForgeTime.Use(clock);
            try
            {
                var might = Make.Attribute("Might");
                var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f)));
                var bleed = Make.Effect("Bleed", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = might, Magnitude = 1f,
                    Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                    StackPolicy = EEffectStackPolicy.StacksHaveIndependentDurations,
                });
                var catalog = Catalog(("might", might), ("bleed", bleed));

                Assert.IsTrue(World.Apply(gas, gas, bleed), "a bleed lands");
                clock.Step = 1f;
                await Frames.Step(3);
                clock.Step = 0f;
                Assert.IsTrue(World.Apply(gas, gas, bleed), "and another");
                await Frames.Step();
                Assert.IsTrue(gas.TryGetEffectContainer(bleed, out var before) && before is AbstractStackingEffectContainer { Stacks: 2 },
                    "two stacks, one older");
                float soonest = before.NextDurationRemaining, longest = before.DurationRemaining;
                Assert.That(soonest, Is.LessThan(longest), "the older has less left");
                var snapshot = Capture(gas, catalog);

                gas.RemoveGameplayEffect(bleed);
                await Frames.Step(2);
                Assert.IsTrue(Restore(gas, snapshot, catalog), "the save loads");
                await Frames.Step();

                Assert.IsTrue(gas.TryGetEffectContainer(bleed, out var after) && after is AbstractStackingEffectContainer { Stacks: 2 },
                    "both stacks are back");
                Assert.AreEqual(soonest, after.NextDurationRemaining, 0.001f, "the older with its time");
                Assert.AreEqual(longest, after.DurationRemaining, 0.001f, "and the newer with its own");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>
        /// Levels, granted abilities, items with their stacks, equipped or not, and cooldowns come back, onto a fresh
        /// entity: the item's effect is on once, from the save, and wearing the item again applies no second (FEAT-9).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_9_Levels_abilities_items_and_cooldowns_come_back([Values] Hub hub) => Live(async () =>
        {
            var might = Make.Attribute("Might");
            var set = Make.Set(Make.Element(might, 0f, 0f));
            var slash = Make.Ability("Slash");
            slash.Cooldown = Make.Effect("Slash Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 30f, Target = null });
            var sharpness = Make.Effect("Sharpness", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = might, Magnitude = 5f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = new ItemStacksScaler(),
            });
            var whetstone = Make.Item("Whetstone");
            whetstone.Definition.MaxStackSize = 5;
            whetstone.GrantedEffects = new List<GameplayEffect> { sharpness };
            var catalog = Catalog(("might", might), ("slash", slash), ("slash-cooldown", slash.Cooldown), ("sharpness", sharpness), ("whetstone", whetstone));

            var hero = World.Entity(hub, set, "Hero");
            Assert.IsTrue(hero.GetAbilitySystem().GiveAbility(slash, 3, out _), "the hero knows Slash at level 3");
            Assert.IsTrue(hero.GetItemSystem().GiveAndEquipItem(whetstone, out _), "and wears a whetstone");
            Assert.AreEqual(2, hero.GetItemSystem().GiveItem(whetstone, 2), "of three");
            World.Apply(hero, hero, slash.Cooldown);
            await Frames.Step();
            Assert.AreEqual(15f, Value(hero, might));
            var snapshot = Capture(hero, catalog);

            var twin = World.Entity(hub, set, "Twin");
            Assert.IsTrue(Restore(twin, snapshot, catalog), "the save loads onto a fresh entity");
            await Frames.Step();
            Assert.IsTrue(twin.GetAbilitySystem().TryGetAbilityContainer(slash, out var ability), "which knows Slash");
            Assert.AreEqual(3, ability.Spec.GetLevel().CurrentValue, "at level 3");
            Assert.IsTrue(twin.GetItemSystem().TryGetItemContainer(whetstone, out var stone) && stone.IsEquipped && stone.ItemStacks == 3,
                "wears the whetstone, three stacked");
            Assert.AreEqual(15f, Value(twin, might), "whose effect is on once");
            Assert.IsTrue(twin.TryGetEffectContainers(sharpness, out var sharp) && sharp.Length == 1);
            Assert.IsTrue(twin.TryGetEffectContainer(slash.Cooldown, out _), "and Slash is on cooldown still");
        });

        /// <summary>
        /// What an effect or a worn item lends comes back with it, once, and goes as it goes. Loose tags and the entity's
        /// time scale come back too (FEAT-9).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_9_Lent_abilities_loose_tags_and_time_scale_come_back([Values] Hub hub) => Live(async () =>
        {
            var might = Make.Attribute("Might");
            var set = Make.Set(Make.Element(might, 0f, 0f));
            var dash = Make.Ability("Dash");
            var parry = Make.Ability("Parry");
            var haste = Make.Effect("Haste", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            haste.GrantedAbilities = new List<Ability> { dash };
            var buckler = Make.Item("Buckler");
            buckler.ActiveAbility = parry;
            var focused = Make.NewTag("State.Focused");
            var catalog = Catalog(("might", might), ("dash", dash), ("parry", parry), ("haste", haste), ("buckler", buckler));

            var hero = World.Entity(hub, set, "Hero");
            Assert.IsTrue(World.Apply(hero, hero, haste), "the hero is hasted");
            Assert.IsTrue(hero.GetItemSystem().GiveAndEquipItem(buckler, out _), "and wears a buckler");
            hero.AddLooseTag(focused);
            hero.GetClock().TimeScale = 0.5f;
            await Frames.Step();
            var snapshot = Capture(hero, catalog);
            Assert.IsEmpty(snapshot.Abilities, "the hero knows no ability of its own");

            var twin = World.Entity(hub, set, "Twin");
            Assert.IsTrue(Restore(twin, snapshot, catalog), "the save loads onto a fresh entity");
            await Frames.Step();
            var abilities = twin.GetAbilitySystem();
            Assert.AreEqual(1, abilities.Containers.Count(c => c.Spec.Base == dash), "the haste lends Dash once");
            Assert.AreEqual(1, abilities.Containers.Count(c => c.Spec.Base == parry), "and the buckler Parry once");
            // By the tag's own equality: Does.Contain takes a tag as the string it converts to
            Assert.IsTrue(twin.GetLooseTags().Contains(focused), "the loose tag is back");
            Assert.AreEqual(1, twin.GetTagCache().GetWeight(focused), "and counted");
            Assert.AreEqual(0.5f, twin.GetClock().TimeScale, "and so is the time scale");

            twin.RemoveGameplayEffect(haste);
            Assert.IsTrue(twin.GetItemSystem().UnequipItem(buckler), "the buckler comes off");
            await Frames.Step(2);
            Assert.IsFalse(abilities.HasAbility(dash), "Dash goes with the haste");
            Assert.IsFalse(abilities.HasAbility(parry), "and Parry with the buckler");
        });

        /// <summary>
        /// An effect another entity applied comes back from that entity where the game's IDs name it, and as the entity's
        /// own where they can't (FEAT-9).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_9_An_effect_from_another_entity_comes_back_from_it([Values] Hub hub) => Live(async () =>
        {
            var might = Make.Attribute("Might");
            var set = Make.Set(Make.Element(might, 0f, 0f));
            var curse = Make.Effect("Curse", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = might, Magnitude = -2f });
            var catalog = Catalog(("might", might), ("curse", curse));
            var witch = World.Entity(hub, set, "Witch");
            var hero = World.Entity(hub, set, "Hero");
            var ids = new EntityIds(("witch", witch), ("hero", hero));

            Assert.IsTrue(World.Apply(witch, hero, curse), "the witch curses the hero");
            await Frames.Step();
            Assert.IsTrue(Restore(hero, Capture(hero, catalog, ids), catalog, ids), "a save with the game's IDs loads");
            await Frames.Step();
            Assert.IsTrue(hero.TryGetEffectContainer(curse, out var cursed) && ReferenceEquals(cursed.Spec.Source, witch),
                "and the curse is the witch's still");

            Assert.IsTrue(Restore(hero, Capture(hero, catalog), catalog), "a save without them loads");
            await Frames.Step();
            Assert.IsTrue(hero.TryGetEffectContainer(curse, out cursed) && ReferenceEquals(cursed.Spec.Source, hero),
                "and the curse comes back as the hero's own");
            Assert.AreEqual(-2f, Value(hero, might), "on once");
        });

        /// <summary>A save is plain serializable types: through JSON and back, it loads the same (FEAT-9).</summary>
        [UnityTest]
        public IEnumerator FEAT_9_A_save_goes_through_JSON([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -25f });
            var catalog = Catalog(("health", health), ("hit", hit));
            World.Apply(gas, gas, hit);
            await Frames.Step();

            string json = JsonUtility.ToJson(Capture(gas, catalog));
            Assert.That(json, Does.Contain("health"), "the save names the attribute by its ID");
            var loaded = JsonUtility.FromJson<ForgeSnapshot>(json);
            World.Apply(gas, gas, hit);
            await Frames.Step();

            Assert.IsTrue(Restore(gas, loaded, catalog), "the save read back loads");
            await Frames.Step();
            Assert.AreEqual(75f, Value(gas, health), "as it was saved");
        });

        /// <summary>Running abilities end on load, and the restore fires nothing per effect, then one OnRestored (FEAT-9).</summary>
        [UnityTest]
        public IEnumerator FEAT_9_A_load_ends_running_abilities_and_tells_once([Values] Hub hub) => Live(async () =>
        {
            var might = Make.Attribute("Might");
            var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f)));
            var focus = Make.Ability("Focus", Make.Stage(new ProbeAbilityTask(World, "Focusing", frames: 200)));
            var buff = Make.Effect("Buff", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = might, Magnitude = 3f });
            var catalog = Catalog(("might", might), ("focus", focus), ("buff", buff));
            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(focus, 1, out int index), "the entity knows Focus");
            World.Apply(gas, gas, buff);
            var snapshot = Capture(gas, catalog);

            Assert.IsTrue(gas.GetAbilitySystem().TryActivateAbility(gas.GetAbilitySystem().CreateActivationRequest(index)), "Focus runs");
            await Frames.Step(2);
            int applied = 0, removed = 0, restored = 0;
            gas.GetCallbacks().OnEffectApplied += _ => applied++;
            gas.GetCallbacks().OnEffectRemoved += _ => removed++;
            gas.GetCallbacks().OnRestored += () => restored++;

            Assert.IsTrue(Restore(gas, snapshot, catalog), "the save loads");
            await Frames.Until(() => Ledger.Cleans("Focusing") == 1, 5, "and Focus ends");
            Assert.AreEqual(0, applied, "no effect was told as applied");
            Assert.AreEqual(0, removed, "nor as removed");
            Assert.AreEqual(1, restored, "and the restore was told once");
            Assert.AreEqual(3f, Value(gas, might), "with the buff on");
        });

        /// <summary>
        /// A load from inside the entity's own step, as from one of its callbacks, is refused: it would empty the lists
        /// the step walks. Between steps, it loads (FEAT-9).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_9_A_load_inside_the_entitys_step_is_refused([Values] Hub hub) => Live(async () =>
        {
            var previous = ForgeTime.Use(new ManualClock { Step = 1f });
            try
            {
                var might = Make.Attribute("Might");
                var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f)));
                var pulse = Make.Effect("Pulse", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Infinite, Target = might, Magnitude = 1f, PeriodicTicks = true, Ticks = 1,
                });
                var catalog = Catalog(("might", might), ("pulse", pulse));
                Assert.IsTrue(World.Apply(gas, gas, pulse), "a pulse lands");
                var snapshot = Capture(gas, catalog);

                bool? inside = null;
                gas.GetCallbacks().OnEffectTick += (_, _) => inside ??= Restore(gas, snapshot, catalog);
                await Frames.Until(() => inside.HasValue, 5, "the pulse ticks");
                Assert.IsFalse(inside.Value, "a load inside the step is refused");
                Assert.IsTrue(Restore(gas, snapshot, catalog), "and between steps it loads");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });
    }
}
