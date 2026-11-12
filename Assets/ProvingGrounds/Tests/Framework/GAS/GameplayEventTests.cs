using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-5: the gameplay events the core sends, where an ability listens: Impact Dealt and Impact Taken as an impact lands
    /// between two entities, Kill and Died as one dies, Revived as a staying one comes back. A trigger takes only some
    /// impacts, rolls a chance once for each event, and targets the event's instigator or its subject; the instigator has
    /// a place of its own in the event's data, apart from the ability's targets.
    /// </summary>
    public class GameplayEventTests : ProvingGroundsTest
    {
        private ForgeAttribute health;

        private sealed class ScriptedRandom : IForgeRandom
        {
            private readonly float[] rolls;
            public ScriptedRandom(params float[] rolls) => this.rolls = rolls;
            public int Drawn { get; private set; }
            public float Value => rolls[Drawn++ % rolls.Length];
            public Vector2 InsideUnitCircle => Vector2.zero;
            public Vector3 InsideUnitSphere => Vector3.zero;
        }

        private IForgeRandom previousRandom;
        private bool randomSet;

        [TearDown]
        public void PutBackChance()
        {
            if (randomSet) ForgeRandom.Use(previousRandom);
            randomSet = false;
        }

        private ScriptedRandom Rolls(params float[] rolls)
        {
            var scripted = new ScriptedRandom(rolls);
            previousRandom = ForgeRandom.Use(scripted);
            randomSet = true;
            return scripted;
        }

        // An ability an event sets off: a probe that counts its runs, or the tasks given
        private Ability Listening(string name, Tag tag, ETriggerTarget target = ETriggerTarget.Own, params AbstractAbilityTask[] tasks)
        {
            var stage = tasks.Length > 0 ? Make.Stage(tasks) : Make.Stage(new ProbeAbilityTask(World, name));
            var ability = Make.Ability(name, stage);
            ability.Definition.Triggers = new List<AbilityTrigger>
            {
                new() { Source = EAbilityTriggerSource.GameplayEvent, Tag = tag, Target = target },
            };
            return ability;
        }

        private AttributeSet Mortal()
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            return set;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) => World.Entity(hub, Mortal(), name);

        // An entity that stays when it dies (TW-4)
        private IGameplayAbilitySystem Stayer(Hub hub, string name)
        {
            var death = new EntityDeath { Policy = EDeathPolicy.Stay };
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, Mortal());
                identity.Death = death;
                var gas = new RuntimeGameplayAbilitySystem(identity);
                World.Register(gas);
                World.Invariants.Observe(gas);
                return gas;
            }
            var mono = World.NewMonoEntity(Mortal(), name);
            mono.EntityData.Death = death;
            World.Register(mono);
            World.Invariants.Observe(mono);
            return mono;
        }

        private GameplayEffect Blow(float amount, params Tag[] types)
        {
            var fx = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });
            fx.ImpactSpecification.ImpactTypes.AddRange(types);
            return fx;
        }

        private GameplayEffect Heal(float amount) =>
            Make.Effect("Heal", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = amount });

        private GameplayEffect Marking(Tag tag) =>
            Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { tag } });

        private static bool HasTag(IGameplayAbilitySystem gas, Tag tag) => gas.GetTagCache().GetWeight(tag) > 0;

        private static void Give(IGameplayAbilitySystem gas, Ability ability) =>
            Assert.That(gas.GetAbilitySystem().GiveAbility(ability, 1, out _), $"{ability.name} is given");

        /// <summary>An ability that listens for Impact Taken, losses to health alone, runs for each blow, not for a heal.</summary>
        [UnityTest]
        public IEnumerator TW_5_An_entity_hears_the_hits_it_takes([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var axe = Fighter(hub, "Axe");
            var foe = Fighter(hub, "Foe");
            var helix = Listening("Helix", Tags.IMPACT_TAKEN);
            helix.Definition.Triggers[0].Attribute = health;
            helix.Definition.Triggers[0].Sign = ETriggerImpactSign.Loss;
            await Frames.Step();
            Give(axe, helix);

            Assert.IsTrue(World.Apply(foe, axe, Blow(10f)));
            await Frames.Step(3);
            Assert.IsTrue(World.Apply(foe, axe, Blow(10f)));
            await Frames.Step(3);
            Assert.IsTrue(World.Apply(foe, axe, Heal(5f)));
            await Frames.Step(3);
            Assert.AreEqual(2, Ledger.Cleans("Helix"), "two blows, two runs; the heal, none");
        });

        /// <summary>An ability that listens for Impact Dealt and targets the event's subject marks the one its holder hits.</summary>
        [UnityTest]
        public IEnumerator TW_5_A_dealer_hears_its_hits_and_targets_the_one_hit([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var mark = Make.NewTag("Test.TW5.Corrupted");
            var dealer = Fighter(hub, "Dealer");
            var foe = Fighter(hub, "Foe");
            await Frames.Step();
            Give(dealer, Listening("Corrupt", Tags.IMPACT_DEALT, ETriggerTarget.Subject,
                new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Marking(mark) } }));

            Assert.IsTrue(World.Apply(dealer, foe, Blow(10f)));
            await Frames.Until(() => HasTag(foe, mark), 10, "the one hit is marked");
            Assert.IsFalse(HasTag(dealer, mark), "not the dealer");
        });

        /// <summary>
        /// A death is heard: Kill by the killer, Died by the one who died, whose ability targets its instigator, the killer.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_5_A_kill_and_a_death_are_heard([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var cursed = Make.NewTag("Test.TW5.Cursed");
            var killer = Fighter(hub, "Killer");
            var victim = Stayer(hub, "Victim");
            await Frames.Step();
            Give(killer, Listening("Trophy", Tags.KILL));
            // A dead entity's ability takes no alive rule, so its last words can run
            var lastWords = Listening("Last Words", Tags.DIED, ETriggerTarget.Instigator,
                new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Marking(cursed) } });
            lastWords.SourceActivationRules = new List<IAbilityValidationRule>();
            lastWords.TargetActivationRules = new List<IAbilityValidationRule>();
            Give(victim, lastWords);

            Assert.IsTrue(World.Apply(killer, victim, Blow(1000f)), "a mortal blow");
            await Frames.Until(() => victim.IsDead, 10, "it dies");
            await Frames.Until(() => HasTag(killer, cursed), 10, "its last words curse its killer, the event's instigator");
            await Frames.Step(2);
            Assert.AreEqual(1, Ledger.Cleans("Trophy"), "and the killer hears its kill");
        });

        /// <summary>A trigger's chance rolls once for each event: three blows under rolls of 0.1, 0.9 and 0.2 at 50%, two runs.</summary>
        [UnityTest]
        public IEnumerator TW_5_A_triggers_chance_rolls_once_for_each_event([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var axe = Fighter(hub, "Axe");
            var foe = Fighter(hub, "Foe");
            var helix = Listening("Helix", Tags.IMPACT_TAKEN);
            helix.Definition.Triggers[0].Chance = 0.5f;
            await Frames.Step();
            Give(axe, helix);

            var rolls = Rolls(0.1f, 0.9f, 0.2f);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(World.Apply(foe, axe, Blow(5f)));
                await Frames.Step(3);
            }
            Assert.AreEqual(2, Ledger.Cleans("Helix"), "two of three under 50%");
            Assert.AreEqual(3, rolls.Drawn, "a roll for each event, however the ability's rules are asked");
        });

        /// <summary>A trigger of impact types takes only those: an attack sets it off, a bleed doesn't.</summary>
        [UnityTest]
        public IEnumerator TW_5_A_trigger_takes_only_its_impact_types([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var attack = Make.NewTag("Test.TW5.Attack");
            var bleed = Make.NewTag("Test.TW5.Bleed");
            var axe = Fighter(hub, "Axe");
            var foe = Fighter(hub, "Foe");
            var helix = Listening("Helix", Tags.IMPACT_TAKEN);
            helix.Definition.Triggers[0].ImpactTypes = new List<Tag> { attack };
            await Frames.Step();
            Give(axe, helix);

            Assert.IsTrue(World.Apply(foe, axe, Blow(5f, bleed)));
            await Frames.Step(3);
            Assert.AreEqual(0, Ledger.Cleans("Helix"), "a bleed doesn't");
            Assert.IsTrue(World.Apply(foe, axe, Blow(5f, attack)));
            await Frames.Step(3);
            Assert.AreEqual(1, Ledger.Cleans("Helix"), "an attack does");
        });

        /// <summary>
        /// The core sends an event only where an ability listens for it: an entity that listens for none hears none, and
        /// one that listens for Impact Taken hears that alone.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_5_Nothing_is_sent_where_nothing_listens([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var deaf = Fighter(hub, "Deaf");
            var listener = Fighter(hub, "Listener");
            await Frames.Step();
            Give(listener, Listening("Helix", Tags.IMPACT_TAKEN));
            int deafHeard = 0;
            var listenerHeard = new List<Tag>();
            deaf.GetAbilitySystem().Callbacks.OnGameplayEvent += (tag, _) => deafHeard++;
            listener.GetAbilitySystem().Callbacks.OnGameplayEvent += (tag, _) => listenerHeard.Add(tag);

            Assert.IsTrue(World.Apply(listener, deaf, Blow(5f)), "the listener hits the deaf one");
            Assert.IsTrue(World.Apply(deaf, listener, Blow(5f)), "and the deaf one hits back");
            await Frames.Step(3);
            Assert.AreEqual(0, deafHeard, "the deaf one hears nothing");
            Assert.That(listenerHeard, Is.EqualTo(new List<Tag> { Tags.IMPACT_TAKEN }), "the listener, the hit it took alone");
        });

        /// <summary>A staying entity's revival is heard.</summary>
        [UnityTest]
        public IEnumerator TW_5_A_revival_is_heard([Values] Hub hub) => Live(async () =>
        {
            health = Make.Attribute("Health");
            var victim = Stayer(hub, "Victim");
            var killer = Fighter(hub, "Killer");
            await Frames.Step();
            Give(victim, Listening("Rise", Tags.REVIVED));
            Assert.IsTrue(World.Apply(killer, victim, Blow(1000f)));
            await Frames.Until(() => victim.IsDead, 10, "it dies");
            Assert.IsTrue(victim.Revive(), "and revives");
            // Its health back before its frame's end, or its death watcher would see it dead again
            Assert.IsTrue(World.Apply(killer, victim, Heal(100f)));
            await Frames.Step(3);
            Assert.AreEqual(1, Ledger.Cleans("Rise"), "and hears it");
        });
    }
}
