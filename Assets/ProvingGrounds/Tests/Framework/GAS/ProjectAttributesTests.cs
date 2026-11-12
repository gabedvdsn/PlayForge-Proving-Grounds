using System.Collections;
using System.Collections.Generic;
using FarEmerald.PlayForge.Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-28, Decision 49: the project's attributes, set once under Project Settings → PlayForge. Its Health is what an
    /// entity dies of at 0, unless a death watcher of its own or its Death says otherwise, what On Hit counts where an entry
    /// names no attribute, and whose loss says who killed it; its Mana is what an ability's Mana Cost spends, where it has
    /// no Cost effect; its Movement Speed is what the Creator Lab's units walk at. Each is optional, and an empty one leaves
    /// things as they were.
    /// </summary>
    public class ProjectAttributesTests : ProvingGroundsTest
    {
        private ForgeAttribute health, mana;
        private Tag marked;

        private readonly List<Object> made = new();
        private ProjectAttributes previous;
        private bool projectSet;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            mana = Make.Attribute("Mana");
            marked = Make.NewTag("Test.TW28.Marked");
        }

        [TearDown]
        public void PutBackTheProject()
        {
            if (projectSet) ForgeAttributes.Use(previous);
            projectSet = false;
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        // The project's attributes, as a test uses them
        private ProjectAttributes Project(ForgeAttribute health = null, ForgeAttribute mana = null, ForgeAttribute speed = null)
        {
            var project = ScriptableObject.CreateInstance<ProjectAttributes>();
            project.Health = health;
            project.Mana = mana;
            project.MovementSpeed = speed;
            made.Add(project);
            return project;
        }

        private void Use(ProjectAttributes project)
        {
            var was = ForgeAttributes.Use(project);
            if (!projectSet) previous = was;
            projectSet = true;
        }

        private AttributeSet Pools(float manaValue = 50f) => Make.Set(
            Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
            Make.Element(mana, manaValue, 100f, EAttributeOverflowPolicy.ZeroToBase));

        private IGameplayAbilitySystem Fighter(Hub hub, string name) => World.Entity(hub, Pools(), name);

        // An entity on either hub whose identity's death is as given
        private IGameplayAbilitySystem Entity(Hub hub, EntityDeath death, string name)
        {
            var set = Pools();
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Death = death;
                var gas = new RuntimeGameplayAbilitySystem(identity);
                World.Register(gas);
                World.Invariants.Observe(gas);
                return gas;
            }

            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Death = death;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private GameplayEffect Blow(float amount, ForgeAttribute to = null) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = to ?? health, Magnitude = -amount });

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // An ability whose one stage applies its usage effects, at a Mana Cost
        private Ability Spell(string name, float cost)
        {
            var stage = Make.Stage(new ProbeAbilityTask(World, name));
            stage.ApplyUsageEffects = true;
            var spell = Make.Ability(name, stage);
            spell.ManaCost = cost;
            return spell;
        }

        /// <summary>With the project's Health, an entity with no death watcher dies as it reaches 0; with none, it doesn't.</summary>
        [UnityTest]
        public IEnumerator TW_28_An_entity_dies_as_the_projects_Health_reaches_0([Values] Hub hub) => Live(async () =>
        {
            var dealer = Fighter(hub, "Dealer");
            var victim = Fighter(hub, "Victim");
            Use(Project());
            await Frames.Step();

            Assert.IsTrue(World.Apply(dealer, victim, Blow(100f)));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(victim, health), 0.01f, "it's at 0");
            Assert.IsFalse(victim.IsDead, "with no Health of the project's, nothing watches it, as before");

            Use(Project(health: health));
            await Frames.Step(2);
            Assert.IsTrue(victim.IsDead, "the project's Health: at 0, it dies");
        });

        /// <summary>A death watcher of its own says when an entity dies, and its Death can say the project's Health doesn't kill it.</summary>
        [UnityTest]
        public IEnumerator TW_28_An_entitys_own_death_watcher_or_Death_wins([Values] Hub hub) => Live(async () =>
        {
            var shield = Make.Attribute("Shield");
            var set = Make.Set(
                Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(shield, 50f, 50f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = shield, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            var dealer = Fighter(hub, "Dealer");
            var guarded = World.Entity(hub, set, "Guarded");
            var dummy = Entity(hub, new EntityDeath { DiesAtZeroHealth = false }, "Dummy");
            Use(Project(health: health));
            await Frames.Step();

            Assert.IsTrue(World.Apply(dealer, guarded, Blow(100f)));
            Assert.IsTrue(World.Apply(dealer, dummy, Blow(100f)));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(guarded, health), 0.01f);
            Assert.IsFalse(guarded.IsDead, "its own death watcher, on its Shield, says when it dies");
            Assert.AreEqual(0f, Value(dummy, health), 0.01f);
            Assert.IsFalse(dummy.IsDead, "its Death says the project's Health doesn't kill it");

            Assert.IsTrue(World.Apply(dealer, guarded, Blow(50f, shield)));
            await Frames.Step(2);
            Assert.IsTrue(guarded.IsDead, "and its Shield at 0 does");
        });

        /// <summary>A Mana Cost spends the project's Mana as its stage applies its usage effects; one it can't pay is refused with Cost.</summary>
        [UnityTest]
        public IEnumerator TW_28_A_Mana_Cost_spends_the_projects_Mana([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var bolt = Spell("Bolt", 30f);
            Use(Project(mana: mana));
            await Frames.Step();
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(bolt, 1, out _), "it's known");

            Assert.IsTrue(abilities.TryActivateAbility(bolt, out _), "50 Mana pays 30");
            await Frames.Until(() => Ledger.Cleans("Bolt") == 1, 10, "the bolt runs");
            Assert.AreEqual(20f, Value(caster, mana), 0.01f, "and pays it as its stage applies its usage effects");

            Assert.IsFalse(abilities.TryActivateAbility(bolt, out var refusal), "20 doesn't pay 30");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason, "refused with Cost");
            Assert.AreEqual(20f, Value(caster, mana), 0.01f, "and spends nothing");
        });

        /// <summary>An ability's own Cost effect is paid in place of its Mana Cost.</summary>
        [UnityTest]
        public IEnumerator TW_28_A_Cost_effect_is_paid_in_place_of_a_Mana_Cost([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var bolt = Spell("Priced", 30f);
            bolt.Cost = Make.Effect("Cost", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = mana, Op = ECalculationOperation.Add, Magnitude = -5f,
            });
            Use(Project(mana: mana));
            await Frames.Step();
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(bolt, 1, out _));

            Assert.IsTrue(abilities.TryActivateAbility(bolt, out _));
            await Frames.Until(() => Ledger.Cleans("Priced") == 1, 10, "it runs");
            Assert.AreEqual(45f, Value(caster, mana), 0.01f, "its Cost effect's 5, not its Mana Cost's 30");
        });

        /// <summary>A Mana Cost with no Mana to pay it, the project's or the caster's, is refused with Cost; a free ability isn't.</summary>
        [UnityTest]
        public IEnumerator TW_28_A_Mana_Cost_with_no_Mana_to_pay_it_is_refused([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var poor = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), "Poor");
            var bolt = Spell("Bolt", 10f);
            var free = Spell("Free", 0f);
            await Frames.Step();
            Assert.That(caster.GetAbilitySystem().GiveAbility(bolt, 1, out _));
            Assert.That(poor.GetAbilitySystem().GiveAbility(bolt, 1, out _));
            Assert.That(poor.GetAbilitySystem().GiveAbility(free, 1, out _));

            Use(Project());
            Assert.IsFalse(caster.GetAbilitySystem().TryActivateAbility(bolt, out var refusal), "the project has no Mana to pay it");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason);

            Use(Project(mana: mana));
            Assert.IsFalse(poor.GetAbilitySystem().TryActivateAbility(bolt, out refusal), "its caster has no Mana to pay it");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason);
            Assert.IsTrue(poor.GetAbilitySystem().TryActivateAbility(free, out _), "a Mana Cost of 0 asks nothing");
            await Frames.Step(3);
        });

        /// <summary>An On Hit entry that names no attribute counts the project's Health.</summary>
        [UnityTest]
        public IEnumerator TW_28_On_Hit_with_no_attribute_counts_the_projects_Health([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var victim = Fighter(hub, "Victim");
            var dagger = Make.Item("Dagger");
            dagger.OnHit.Add(new OnHitEntry
            {
                Effects = new List<GameplayEffect>
                {
                    Make.Effect("Mark", new EffectConfig
                    {
                        Policy = EEffectDurationPolicy.Durational, Duration = 4f, GrantedTags = new List<Tag> { marked },
                    }),
                },
            });
            Use(Project(health: health));
            await Frames.Step();
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(dagger, out _));

            Assert.IsTrue(World.Apply(holder, victim, Blow(10f)));
            await Frames.Step(2);
            Assert.IsTrue(victim.GetTagCache().GetWeight(marked) > 0, "the hit to its Health sets it off");
        });

        /// <summary>The Kill goes to whoever's loss took the last of its Health, not to a loss after it, to another attribute.</summary>
        [UnityTest]
        public IEnumerator TW_28_The_Kill_goes_to_whoever_took_the_last_of_its_Health([Values] Hub hub) => Live(async () =>
        {
            var victim = Fighter(hub, "Victim");
            var slayer = Fighter(hub, "Slayer");
            var drainer = Fighter(hub, "Drainer");
            // An entity hears an event only where an ability of its listens for it
            foreach (var (gas, name) in new[] { (slayer, "Slayer's Trophy"), (drainer, "Drainer's Trophy") })
            {
                var trophy = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name)));
                trophy.Definition.Triggers = new List<AbilityTrigger> { new() { Source = EAbilityTriggerSource.GameplayEvent, Tag = Tags.KILL } };
                Assert.That(gas.GetAbilitySystem().GiveAbility(trophy, 1, out _));
            }
            int slayerKills = 0, drainerKills = 0;
            slayer.GetAbilitySystem().Callbacks.OnGameplayEvent += (tag, _) => { if (tag.Equals(Tags.KILL)) slayerKills++; };
            drainer.GetAbilitySystem().Callbacks.OnGameplayEvent += (tag, _) => { if (tag.Equals(Tags.KILL)) drainerKills++; };
            Use(Project(health: health));
            await Frames.Step();

            Assert.IsTrue(World.Apply(slayer, victim, Blow(100f)));
            Assert.IsTrue(World.Apply(drainer, victim, Blow(5f, mana)));
            await Frames.Step(2);
            Assert.IsTrue(victim.IsDead, "it dies of its Health");
            Assert.AreEqual(1, slayerKills, "the Kill is the slayer's, whose hit took the last of its Health");
            Assert.AreEqual(0, drainerKills, "not the drainer's, whose loss came after, to its Mana");
        });

        /// <summary>The Creator Lab's rules take the project's Health and Movement Speed where they name none; their own win.</summary>
        [Test]
        public void TW_28_The_labs_rules_take_the_projects_Health_and_Movement_Speed()
        {
            var speed = Make.Attribute("Movement Speed");
            var other = Make.Attribute("Vigor");
            var rules = ScriptableObject.CreateInstance<LabRules>();
            made.Add(rules);
            ILabRules lab = rules;
            Use(Project(health: health, speed: speed));

            Assert.AreSame(health, lab.Health, "the project's Health");
            Assert.AreSame(speed, lab.MovementSpeed, "and its Movement Speed");
            rules.Health = other;
            Assert.AreSame(other, lab.Health, "its own wins");
        }
    }
}
