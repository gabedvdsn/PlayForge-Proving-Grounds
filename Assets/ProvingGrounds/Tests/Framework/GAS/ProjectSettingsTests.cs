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
    /// TW-29, Decision 57: a game's settings on its Forge Project, which a running game finds through its GameRoot, or
    /// code sets. Its attributes' roles are tags: PlayForge's Health role is what an entity dies of at 0, what On Hit
    /// counts where an entry names no attribute, and whose loss says who killed it; a role of one's own is read by its
    /// tag. An ability's attribute costs spend the attributes they name, or those playing the roles they name, beside its
    /// Cost effect. TW-28 held fixed Health, Mana, Luck and Movement Speed slots, and an ability's Mana Cost.
    /// </summary>
    public class ProjectSettingsTests : ProvingGroundsTest
    {
        private ForgeAttribute health, mana;
        private Tag marked, energy;

        private readonly List<Object> made = new();
        private ForgeProject previous;
        private bool projectSet;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            mana = Make.Attribute("Mana");
            marked = Make.NewTag("Test.TW29.Marked");
            energy = Make.NewTag("Test.TW29.Energy");
        }

        [TearDown]
        public void PutBackTheProject()
        {
            if (projectSet) ForgeProject.Use(previous);
            projectSet = false;
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        // A project with the roles given, as a test uses one
        private ForgeProject Project(params (Tag Role, ForgeAttribute Attribute)[] roles)
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            foreach (var (role, attribute) in roles) project.Settings.Roles.Add(new AttributeRole { Role = role, Attribute = attribute });
            made.Add(project);
            return project;
        }

        private void Use(ForgeProject project)
        {
            var was = ForgeProject.Use(project);
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

        // An ability over as it's asked for, with the attribute costs given
        private Ability Spell(string name, params AttributeCost[] costs)
        {
            var spell = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name)));
            spell.AttributeCosts.AddRange(costs);
            return spell;
        }

        private static AttributeCost Spends(ForgeAttribute attribute, float amount) => new() { Attribute = attribute, Amount = amount };
        private static AttributeCost SpendsRole(Tag role, float amount) => new() { Role = role, Amount = amount };

        /// <summary>With its project's Health role filled, an entity with no death watcher dies as it reaches 0; with none, it doesn't.</summary>
        [UnityTest]
        public IEnumerator TW_29_An_entity_dies_as_its_projects_Health_role_reaches_0([Values] Hub hub) => Live(async () =>
        {
            var dealer = Fighter(hub, "Dealer");
            var victim = Fighter(hub, "Victim");
            Use(Project());
            await Frames.Step();

            Assert.IsTrue(World.Apply(dealer, victim, Blow(100f)));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(victim, health), 0.01f, "it's at 0");
            Assert.IsFalse(victim.IsDead, "with no attribute in the Health role, nothing watches it");

            Use(Project((Tags.ROLE_HEALTH, health)));
            await Frames.Step(2);
            Assert.IsTrue(victim.IsDead, "its Health role filled: at 0, it dies");
        });

        /// <summary>A running game reads its GameRoot's project where code sets none; a project code sets wins.</summary>
        [UnityTest]
        public IEnumerator TW_29_A_running_game_reads_its_GameRoots_project([Values] Hub hub) => Live(async () =>
        {
            var dealer = Fighter(hub, "Dealer");
            var victim = Fighter(hub, "Victim");
            var game = Project((Tags.ROLE_HEALTH, health));
            var rootHad = World.Root.Project;
            World.Root.Project = game;
            try
            {
                Assert.AreSame(game, ForgeProject.Current, "the GameRoot's project, where code sets none");
                Assert.AreSame(health, ForgeRoles.AttributeFor(Tags.ROLE_HEALTH), "whose roles PlayForge reads");

                var codes = Project();
                Use(codes);
                Assert.AreSame(codes, ForgeProject.Current, "one code sets wins");
                Assert.IsNull(ForgeRoles.AttributeFor(Tags.ROLE_HEALTH), "with its own roles");
                ForgeProject.Use(null);
                Assert.AreSame(game, ForgeProject.Current, "and with none set, the GameRoot's again");

                await Frames.Step();
                Assert.IsTrue(World.Apply(dealer, victim, Blow(100f)));
                await Frames.Step(2);
                Assert.IsTrue(victim.IsDead, "its Health role kills at 0");
            }
            finally
            {
                World.Root.Project = rootHad;
            }
        });

        /// <summary>A death watcher of its own says when an entity dies, and its Death can say the Health role doesn't kill it.</summary>
        [UnityTest]
        public IEnumerator TW_29_An_entitys_own_death_watcher_or_Death_wins([Values] Hub hub) => Live(async () =>
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
            Use(Project((Tags.ROLE_HEALTH, health)));
            await Frames.Step();

            Assert.IsTrue(World.Apply(dealer, guarded, Blow(100f)));
            Assert.IsTrue(World.Apply(dealer, dummy, Blow(100f)));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(guarded, health), 0.01f);
            Assert.IsFalse(guarded.IsDead, "its own death watcher, on its Shield, says when it dies");
            Assert.AreEqual(0f, Value(dummy, health), 0.01f);
            Assert.IsFalse(dummy.IsDead, "its Death says the Health role doesn't kill it");

            Assert.IsTrue(World.Apply(dealer, guarded, Blow(50f, shield)));
            await Frames.Step(2);
            Assert.IsTrue(guarded.IsDead, "and its Shield at 0 does");
        });

        /// <summary>
        /// An attribute cost spends the attribute it names, or the one playing the role it names; one its caster can't pay
        /// is refused with Cost and spends nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_29_Attribute_costs_spend_what_they_name([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var bolt = Spell("Bolt", Spends(mana, 30f));
            var surge = Spell("Surge", SpendsRole(energy, 10f));
            Use(Project((energy, mana)));
            await Frames.Step();
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(bolt, 1, out _) && abilities.GiveAbility(surge, 1, out _), "they're known");

            Assert.IsTrue(abilities.TryActivateAbility(bolt, out _), "50 Mana pays 30");
            Assert.AreEqual(20f, Value(caster, mana), 0.01f, "and pays it as it starts");
            Assert.IsFalse(abilities.TryActivateAbility(bolt, out var refusal), "20 doesn't pay 30");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason, "refused with Cost");
            Assert.AreEqual(20f, Value(caster, mana), 0.01f, "and spends nothing");

            Assert.AreSame(mana, ForgeRoles.AttributeFor(energy), "a role of one's own, read by its tag");
            Assert.IsTrue(abilities.TryActivateAbility(surge, out _), "Surge spends the attribute playing its role");
            Assert.AreEqual(10f, Value(caster, mana), 0.01f, "its Energy, the project's Mana");
        });

        /// <summary>An ability with a Cost effect and attribute costs pays both; it's refused where either can't be paid.</summary>
        [UnityTest]
        public IEnumerator TW_29_A_Cost_effect_and_attribute_costs_are_both_paid([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var blood = Spell("Blood Magic", Spends(health, 10f));
            blood.Cost = Make.Effect("Blood Cost", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = mana, Op = ECalculationOperation.Add, Magnitude = -5f,
            });
            var dear = Spell("Dear", Spends(health, 500f));
            dear.Cost = blood.Cost;
            Use(Project());
            await Frames.Step();
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(blood, 1, out _) && abilities.GiveAbility(dear, 1, out _));

            Assert.IsTrue(abilities.TryActivateAbility(blood, out _));
            Assert.AreEqual(45f, Value(caster, mana), 0.01f, "its Cost effect's 5 Mana");
            Assert.AreEqual(90f, Value(caster, health), 0.01f, "and its attribute cost's 10 Health");

            Assert.IsFalse(abilities.TryActivateAbility(dear, out var refusal), "500 Health can't be paid, though its Cost effect can");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason);
        });

        /// <summary>A cost with nothing to spend is refused with Cost: a role its project doesn't fill, or an attribute its caster lacks.</summary>
        [UnityTest]
        public IEnumerator TW_29_A_cost_with_nothing_to_spend_is_refused([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var poor = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), "Poor");
            var unfilled = Spell("Unfilled", SpendsRole(energy, 10f));
            var bolt = Spell("Bolt", Spends(mana, 10f));
            var free = Spell("Free", Spends(mana, 0f));
            Use(Project());
            await Frames.Step();
            Assert.That(caster.GetAbilitySystem().GiveAbility(unfilled, 1, out _));
            Assert.That(poor.GetAbilitySystem().GiveAbility(bolt, 1, out _));
            Assert.That(poor.GetAbilitySystem().GiveAbility(free, 1, out _));

            Assert.IsFalse(caster.GetAbilitySystem().TryActivateAbility(unfilled, out var refusal), "its project fills no Energy role");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason);
            Assert.IsFalse(poor.GetAbilitySystem().TryActivateAbility(bolt, out refusal), "its caster has no Mana");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason);
            Assert.IsTrue(poor.GetAbilitySystem().TryActivateAbility(free, out _), "a cost of 0 asks nothing");
        });

        /// <summary>An On Hit entry that names no attribute counts the attribute playing the Health role.</summary>
        [UnityTest]
        public IEnumerator TW_29_On_Hit_with_no_attribute_counts_the_Health_role([Values] Hub hub) => Live(async () =>
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
            Use(Project((Tags.ROLE_HEALTH, health)));
            await Frames.Step();
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(dagger, out _));

            Assert.IsTrue(World.Apply(holder, victim, Blow(10f)));
            await Frames.Step(2);
            Assert.IsTrue(victim.GetTagCache().GetWeight(marked) > 0, "the hit to its Health sets it off");
        });

        /// <summary>The Kill goes to whoever's loss took the last of its Health, not to a loss after it, to another attribute.</summary>
        [UnityTest]
        public IEnumerator TW_29_The_Kill_goes_to_whoever_took_the_last_of_its_Health([Values] Hub hub) => Live(async () =>
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
            Use(Project((Tags.ROLE_HEALTH, health)));
            await Frames.Step();

            Assert.IsTrue(World.Apply(slayer, victim, Blow(100f)));
            Assert.IsTrue(World.Apply(drainer, victim, Blow(5f, mana)));
            await Frames.Step(2);
            Assert.IsTrue(victim.IsDead, "it dies of its Health");
            Assert.AreEqual(1, slayerKills, "the Kill is the slayer's, whose hit took the last of its Health");
            Assert.AreEqual(0, drainerKills, "not the drainer's, whose loss came after, to its Mana");
        });

        /// <summary>The Creator Lab's rules take the Health and Movement Speed roles where they name none; their own win.</summary>
        [Test]
        public void TW_29_The_labs_rules_take_the_projects_roles()
        {
            var speed = Make.Attribute("Movement Speed");
            var other = Make.Attribute("Vigor");
            var rules = ScriptableObject.CreateInstance<LabRules>();
            made.Add(rules);
            ILabRules lab = rules;
            Use(Project((Tags.ROLE_HEALTH, health), (Tags.ROLE_MOVEMENT_SPEED, speed)));

            Assert.AreSame(health, lab.Health, "the Health role's attribute");
            Assert.AreSame(speed, lab.MovementSpeed, "and the Movement Speed role's");
            rules.Health = other;
            Assert.AreSame(other, lab.Health, "its own wins");
        }
    }
}
