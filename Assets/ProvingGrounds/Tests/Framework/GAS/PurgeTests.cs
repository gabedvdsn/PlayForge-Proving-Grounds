using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FN-16, Decisions 79 and 80: purging. A purge takes the effects at its strength's tier of the project's Purge Levels or
    /// under it, an effect at no tier never; only the kinds it names, by their tags; only those applied by whom it says,
    /// against the one purged; and only those its filters take. An effect's Purges purge its target as it lands. A dead
    /// entity takes no one else's effects, only those of its own, as its death's and its revive's.
    /// </summary>
    public class PurgeTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag marked, basic, strong, buff, debuff, stun, red, blue;
        private ForgeProject project, was;

        [SetUp]
        public void Tags()
        {
            health = Make.Attribute("Health");
            marked = Make.NewTag("Test.FN16.Marked");
            basic = Make.NewTag("Test.FN16.Purge.Basic");
            strong = Make.NewTag("Test.FN16.Purge.Strong");
            buff = Make.NewTag("Test.FN16.Status.Buff");
            debuff = Make.NewTag("Test.FN16.Status.Debuff");
            stun = Make.NewTag("Test.FN16.Status.Debuff.Stun");
            red = Make.NewTag("Test.FN16.Red");
            blue = Make.NewTag("Test.FN16.Blue");
            project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.PurgeLevels.Tiers.Add(new PurgeTier { Tag = basic });
            project.Settings.PurgeLevels.Tiers.Add(new PurgeTier { Tag = strong });
            was = ForgeProject.Use(project);
        }

        [TearDown]
        public void PutBack()
        {
            ForgeProject.Use(was);
            Object.DestroyImmediate(project);
        }

        // An entity of a side, on either hub
        private IGameplayAbilitySystem Sided(Hub hub, string name, Tag side)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Affiliation = new List<Tag> { side };
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        // A lasting effect carrying the context tags given
        private GameplayEffect Tagged(string name, params Tag[] tags)
        {
            var effect = Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f });
            effect.Tags.ContextTags = new List<Tag>(tags);
            return effect;
        }

        private static bool Holds(IGameplayAbilitySystem gas, GameplayEffect effect)
        {
            foreach (var container in gas.GetEffectSystem().Shelf)
                if (container.Spec.Base == effect) return true;
            return false;
        }

        /// <summary>
        /// A purge takes the effects at its strength's tier or under it: a Basic purge the Basic, a Strong one the Strong as
        /// well; an effect at no tier stays through both (Decision 80).
        /// </summary>
        [UnityTest]
        public IEnumerator FN_16_A_purge_takes_the_effects_at_or_under_its_strength([Values] Hub hub) => Live(async () =>
        {
            var hero = Sided(hub, "Hero", blue);
            var foe = Sided(hub, "Foe", red);
            var weak = Tagged("Weak", basic);
            var hard = Tagged("Hard", strong);
            var plain = Tagged("Plain");
            await Frames.Step();
            foreach (var effect in new[] { weak, hard, plain }) Assert.IsTrue(World.Apply(foe, hero, effect), $"{effect.name} lands");
            await Frames.Step();

            Assert.AreEqual(1, ForgePurge.Purge(hero, basic), "a Basic purge takes one");
            await Frames.Step(2);
            Assert.IsFalse(Holds(hero, weak), "the Basic effect");
            Assert.IsTrue(Holds(hero, hard), "not the Strong");

            Assert.AreEqual(1, ForgePurge.Purge(hero, strong), "a Strong purge takes the Strong");
            await Frames.Step(2);
            Assert.IsFalse(Holds(hero, hard), "gone");
            Assert.IsTrue(Holds(hero, plain), "and an effect at no tier is never purged");
        });

        /// <summary>A purge of kinds takes only the effects carrying one of them, as a cleanse of debuffs leaves a buff.</summary>
        [UnityTest]
        public IEnumerator FN_16_A_purge_takes_only_the_kinds_it_names([Values] Hub hub) => Live(async () =>
        {
            var hero = Sided(hub, "Hero", blue);
            var foe = Sided(hub, "Foe", red);
            var curse = Tagged("Curse", basic, debuff);
            var boon = Tagged("Boon", basic, buff);
            await Frames.Step();
            Assert.IsTrue(World.Apply(foe, hero, curse) && World.Apply(foe, hero, boon));
            await Frames.Step();

            ForgePurge.Purge(hero, new EffectPurge { Strength = basic, Kinds = new List<Tag> { debuff } });
            await Frames.Step(2);
            Assert.IsFalse(Holds(hero, curse), "the debuff goes");
            Assert.IsTrue(Holds(hero, boon), "the buff stays");
        });

        /// <summary>
        /// A purge takes by who applied each effect, against the one purged: its enemies' effects, its allies', or its own.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_16_A_purge_takes_by_who_applied_each([Values] Hub hub) => Live(async () =>
        {
            var hero = Sided(hub, "Hero", blue);
            var friend = Sided(hub, "Friend", blue);
            var foe = Sided(hub, "Foe", red);
            var curse = Tagged("Curse", basic);
            var blessing = Tagged("Blessing", basic);
            var focus = Tagged("Focus", basic);
            await Frames.Step();
            Assert.IsTrue(World.Apply(foe, hero, curse) && World.Apply(friend, hero, blessing) && World.Apply(hero, hero, focus));
            await Frames.Step();

            ForgePurge.Purge(hero, new EffectPurge { Strength = basic, AppliedBy = EPurgeRelation.Enemies });
            await Frames.Step(2);
            Assert.IsFalse(Holds(hero, curse), "its enemy's goes");
            Assert.IsTrue(Holds(hero, blessing) && Holds(hero, focus), "its ally's and its own stay");

            ForgePurge.Purge(hero, new EffectPurge { Strength = basic, AppliedBy = EPurgeRelation.Allies });
            await Frames.Step(2);
            Assert.IsFalse(Holds(hero, blessing), "then its ally's");
            Assert.IsTrue(Holds(hero, focus), "and its own stays");
        });

        /// <summary>A purge's filter of its own spares what it says: Except Tagged leaves a stun a debuff purge would take.</summary>
        [UnityTest]
        public IEnumerator FN_16_A_purges_filters_spare_what_they_say([Values] Hub hub) => Live(async () =>
        {
            var hero = Sided(hub, "Hero", blue);
            var foe = Sided(hub, "Foe", red);
            var slow = Tagged("Slow", basic, debuff);
            var stunned = Tagged("Stunned", basic, stun);
            await Frames.Step();
            Assert.IsTrue(World.Apply(foe, hero, slow) && World.Apply(foe, hero, stunned));
            await Frames.Step();

            ForgePurge.Purge(hero, new EffectPurge
            {
                Strength = basic, Kinds = new List<Tag> { debuff },
                Filters = new List<AbstractPurgeFilter> { new ExceptTaggedPurgeFilter { Tags = new List<Tag> { stun } } },
            });
            await Frames.Step(2);
            Assert.IsFalse(Holds(hero, slow), "the slow, a debuff, goes");
            Assert.IsTrue(Holds(hero, stunned), "the stun, a debuff too, spared");
        });

        /// <summary>An effect's Purges purge its target as it lands: a friend's dispel of debuffs, Strong, takes both curses.</summary>
        [UnityTest]
        public IEnumerator FN_16_An_effects_purges_purge_its_target_as_it_lands([Values] Hub hub) => Live(async () =>
        {
            var hero = Sided(hub, "Hero", blue);
            var friend = Sided(hub, "Friend", blue);
            var foe = Sided(hub, "Foe", red);
            var weakCurse = Tagged("Weak Curse", basic, debuff);
            var hardCurse = Tagged("Hard Curse", strong, debuff);
            var boon = Tagged("Boon", basic, buff);
            var dispel = Make.Effect("Dispel", new EffectConfig { Policy = EEffectDurationPolicy.Instant });
            dispel.Purges = new EffectPurge { Strength = strong, Kinds = new List<Tag> { debuff } };
            await Frames.Step();
            Assert.IsTrue(World.Apply(foe, hero, weakCurse) && World.Apply(foe, hero, hardCurse) && World.Apply(friend, hero, boon));
            await Frames.Step();

            Assert.IsTrue(World.Apply(friend, hero, dispel), "the dispel lands");
            await Frames.Step(2);
            Assert.IsFalse(Holds(hero, weakCurse) || Holds(hero, hardCurse), "and both curses go");
            Assert.IsTrue(Holds(hero, boon), "the boon stays");
        });

        // An entity on either hub that dies at 0 Health and stays, its revive mending it whole
        private IGameplayAbilitySystem Mortal(Hub hub, string name)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            var death = new EntityDeath
            {
                Policy = EDeathPolicy.Stay,
                OnRevive = new List<GameplayEffect> { Make.Effect("Mend", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = 1000f }) },
            };
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Death = death;
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Death = death;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private async Cysharp.Threading.Tasks.UniTask Kill(IGameplayAbilitySystem killer, IGameplayAbilitySystem entity)
        {
            var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1000f });
            Assert.IsTrue(World.Apply(killer, entity, blow), "a mortal blow");
            await Frames.Until(() => entity.IsDead, 10, "it dies");
            await Frames.Step(2);
        }

        private GameplayEffect Mark(string name) => Make.Effect(name, new EffectConfig
        {
            Policy = EEffectDurationPolicy.Durational, Duration = 10f, GrantedTags = new List<Tag> { marked },
        });

        // A staying entity of a side, its death as given, mortal and mended as it revives
        private IGameplayAbilitySystem Dying(Hub hub, string name, Tag side, EntityDeath death)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            death.Policy = EDeathPolicy.Stay;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                identity.Death = death;
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                return runtime;
            }
            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Affiliation = new List<Tag> { side };
            mono.EntityData.Death = death;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        // A hero holding each kind of effect a death reads: its ability's cooldown, an item's lent effect, a foe's Basic
        // curse, a foe's Strong hex, a foe's mark at no tier, and a focus of its own at no tier
        private async Cysharp.Threading.Tasks.UniTask<(GameplayEffect Cooldown, GameplayEffect Lent, GameplayEffect Curse, GameplayEffect Hex,
            GameplayEffect Mark, GameplayEffect Focus)> Holding(IGameplayAbilitySystem hero, IGameplayAbilitySystem foe)
        {
            var cooldown = Make.Effect("Strike Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 60f });
            var strike = Make.Ability("Strike", Make.Stage());
            strike.Cooldown = cooldown;
            var lent = Make.Effect("Ward", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            var amulet = Make.Item("Amulet");
            amulet.GrantedEffects.Add(lent);
            var curse = Tagged("Curse", basic);
            var hex = Tagged("Hex", strong);
            var mark = Tagged("Mark");
            var focus = Tagged("Focus");
            await Frames.Step();

            var abilities = hero.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(strike, 1, out int index), "it knows Strike");
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "and uses it");
            Assert.IsTrue(hero.GetItemSystem().GiveAndEquipItem(amulet, out _), "it wears the amulet");
            Assert.IsTrue(World.Apply(foe, hero, curse) && World.Apply(foe, hero, hex) && World.Apply(foe, hero, mark), "a foe's curse, hex and mark");
            Assert.IsTrue(World.Apply(hero, hero, focus), "and a focus of its own");
            await Frames.Until(() => Holds(hero, cooldown), 20, "Strike's on its cooldown");
            return (cooldown, lent, curse, hex, mark, focus);
        }

        /// <summary>
        /// A death purges all but what it keeps: the categories its project keeps, a cooldown and a lent effect, whatever
        /// their tiers; the tiers its project keeps, as a Strong hex; and nothing at no tier, as a foe's mark and its own focus.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_16_A_death_purges_all_but_what_it_keeps([Values] Hub hub) => Live(async () =>
        {
            project.Settings.PurgeLevels.Tiers[1].KeptOnDeath = true;
            var hero = Dying(hub, "Hero", blue, new EntityDeath());
            var foe = Sided(hub, "Foe", red);
            var held = await Holding(hero, foe);

            await Kill(foe, hero);
            Assert.IsTrue(Holds(hero, held.Cooldown), "its cooldown, a category its project keeps, stays");
            Assert.IsTrue(Holds(hero, held.Lent), "and its amulet's ward, lent");
            Assert.IsTrue(Holds(hero, held.Hex), "and the Strong hex, at a tier its project keeps");
            Assert.IsFalse(Holds(hero, held.Curse), "the Basic curse goes");
            Assert.IsFalse(Holds(hero, held.Mark), "and the mark, at no tier");
            Assert.IsFalse(Holds(hero, held.Focus), "and its own focus, its own not kept");
        });

        /// <summary>
        /// A death keeps what it says of its own: here not its project's, but its own effects and the Basic tier; and a death
        /// that doesn't purge keeps all.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_16_A_death_keeps_what_it_says([Values] Hub hub) => Live(async () =>
        {
            var hero = Dying(hub, "Hero", blue, new EntityDeath
            {
                KeepsAsProject = false, KeepsCategories = EEffectCategory.Own, KeepsTiers = new List<Tag> { basic },
            });
            var foe = Sided(hub, "Foe", red);
            var held = await Holding(hero, foe);

            await Kill(foe, hero);
            Assert.IsTrue(Holds(hero, held.Focus), "its own focus, a category it keeps");
            Assert.IsTrue(Holds(hero, held.Curse), "the Basic curse, a tier it keeps");
            Assert.IsFalse(Holds(hero, held.Cooldown), "its cooldown, not kept, goes");
            Assert.IsFalse(Holds(hero, held.Hex), "and the Strong hex");

            var keeper = Dying(hub, "Keeper", blue, new EntityDeath { Purges = false });
            var kept = await Holding(keeper, foe);
            await Kill(foe, keeper);
            Assert.IsTrue(Holds(keeper, kept.Mark) && Holds(keeper, kept.Curse) && Holds(keeper, kept.Focus), "a death that doesn't purge keeps all");
        });

        /// <summary>
        /// A dead entity takes no one else's effects, as the dead in the games are untargetable; its own still land, as its
        /// death's respawn timer and its revive's mend do; and revived, it takes others' again.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_16_The_dead_take_no_one_elses_effects([Values] Hub hub) => Live(async () =>
        {
            var hero = Mortal(hub, "Hero");
            var foe = Mortal(hub, "Foe");
            await Frames.Step();

            await Kill(foe, hero);
            Assert.IsFalse(World.Apply(foe, hero, Mark("Curse")), "dead, it takes no one else's effect");
            Assert.IsTrue(World.Apply(hero, hero, Mark("Own")), "but its own");

            Assert.IsTrue(hero.Revive(), "it revives");
            await Frames.Step(2);
            Assert.AreEqual(100f, hero.TryGetAttributeValue(health, out var now) ? now.CurrentValue : -1f, 0.01f, "its revive's mend landing");
            Assert.IsTrue(World.Apply(foe, hero, Mark("Curse")), "and alive, it takes others' again");
        });
    }
}
