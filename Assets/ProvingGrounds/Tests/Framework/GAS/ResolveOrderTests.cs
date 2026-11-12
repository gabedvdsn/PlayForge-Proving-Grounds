using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-31, Decision 60: the project's resolve order. An area's effects land on those it takes in the order the Forge
    /// Project's settings give by the tags they carry, lower first and those with none at 0, the first entry an entity
    /// carries placing it; the area's own order breaks ties. An ability's targeting keeps its own order and its primary
    /// target, and its Apply Effects walk them in resolve order; an effect's Area resolves its target among those it takes.
    /// A death waits for its entity's end of frame, so one explosion's hits resolve together whatever the order.
    /// </summary>
    public class ResolveOrderTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag red, blue, totemTag, championTag, immortal;
        private readonly List<Object> made = new();
        private ForgeProject previous;
        private bool projectSet;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            red = Make.NewTag("Test.TW31.Red");
            blue = Make.NewTag("Test.TW31.Blue");
            totemTag = Make.NewTag("Test.TW31.Totem");
            championTag = Make.NewTag("Test.TW31.Champion");
            immortal = Make.NewTag("Test.TW31.Immortal");
        }

        [TearDown]
        public void PutBackTheProject()
        {
            if (projectSet) ForgeProject.Use(previous);
            projectSet = false;
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        // A project whose Health role is the test's, with the resolve order given, in use
        private ForgeProject UseOrder(params (Tag Tag, int Order)[] entries)
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            made.Add(project);
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            foreach (var (tag, order) in entries) project.Settings.ResolveOrder.Add(new ResolveOrderEntry { Tag = tag, Order = order });
            var was = ForgeProject.Use(project);
            if (!projectSet) previous = was;
            projectSet = true;
            return project;
        }

        // An entity of a side, at a point, on either hub, with any auras of its own
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, Vector3 at, params AuraEntry[] own)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                identity.Auras = new List<AuraEntry>(own);
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                mono.EntityData.Auras = new List<AuraEntry>(own);
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = at;
            return gas;
        }

        // Has the entity carry a tag, as an effect grants it while it lasts
        private GameplayEffect Carry(IGameplayAbilitySystem gas, Tag tag)
        {
            var carrying = Make.Effect($"Carries {tag}", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { tag } });
            Assert.IsTrue(World.Apply(gas, gas, carrying), $"it carries {tag}");
            return carrying;
        }

        // The names of the entities an effect lands on, in the order it lands
        private static List<string> Record(GameplayEffect effect, params (string Name, IGameplayAbilitySystem Gas)[] entities)
        {
            var order = new List<string>();
            foreach (var (name, gas) in entities)
                gas.GetCallbacks().OnEffectApplied += spec => { if (spec.Base == effect) order.Add(name); };
            return order;
        }

        private GameplayEffect Blast(float amount = -10f) =>
            Make.Effect("Blast", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = amount });

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private async UniTask Cast(IGameplayAbilitySystem caster, Ability ability)
        {
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out _), $"{ability.name} is known");
            Assert.IsTrue(abilities.TryActivateAbility(ability, out _), $"{ability.name} casts");
            await Frames.Step(4);
        }

        /// <summary>
        /// An ability's targeting picks the nearest first, as its Mode says, and its Apply Effects lands on them in the
        /// project's resolve order: the champion, carrying an entry at -5, first; the totem, at 10, last. Turned off, it
        /// lands in the targeting's order; Apply/Remove Effects follows the resolve order too.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_31_An_abilitys_area_lands_in_the_projects_resolve_order([Values] Hub hub) => Live(async () =>
        {
            UseOrder((totemTag, 10), (championTag, -5));
            var caster = At(hub, "Caster", blue, Vector3.zero);
            var totem = At(hub, "Totem", red, new Vector3(2f, 0f, 0f));
            var grunt = At(hub, "Grunt", red, new Vector3(4f, 0f, 0f));
            var champion = At(hub, "Champion", red, new Vector3(6f, 0f, 0f));
            await Frames.Step();
            Carry(totem, totemTag);
            Carry(champion, championTag);
            await Frames.Step();

            var blast = Blast();
            var order = Record(blast, ("Totem", totem), ("Grunt", grunt), ("Champion", champion));
            var ability = Make.Ability("Blast", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { blast } }));
            ability.Behaviour.Targeting = new AutoTargetTask { Mode = EAutoTargetMode.Nearest, MaxTargets = 0, Range = 10f };
            await Cast(caster, ability);
            Assert.That(order, Is.EqualTo(new[] { "Champion", "Grunt", "Totem" }), "the champion first and the totem last");

            order.Clear();
            var unordered = Make.Ability("Unordered Blast", Make.Stage(new ApplyEffectsAbilityTask
            {
                Effects = new List<GameplayEffect> { blast }, UseResolveOrder = false,
            }));
            unordered.Behaviour.Targeting = new AutoTargetTask { Mode = EAutoTargetMode.Nearest, MaxTargets = 0, Range = 10f };
            await Cast(caster, unordered);
            Assert.That(order, Is.EqualTo(new[] { "Totem", "Grunt", "Champion" }), "turned off, the targeting's own order: nearest first");

            order.Clear();
            var held = Make.Ability("Held Blast", Make.Stage(new ApplyRemoveEffectsAbilityTask { Effects = new List<GameplayEffect> { blast } }));
            held.Behaviour.Targeting = new AutoTargetTask { Mode = EAutoTargetMode.Nearest, MaxTargets = 0, Range = 10f };
            await Cast(caster, held);
            Assert.That(order, Is.EqualTo(new[] { "Champion", "Grunt", "Totem" }), "Apply/Remove Effects follows it too");
        });

        /// <summary>
        /// An effect's Area lands on its target among those it takes, in resolve order, so a totem it was cast at lands
        /// last; turned off, its target first and then those near.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_31_An_effects_area_resolves_its_target_among_those_it_takes([Values] Hub hub) => Live(async () =>
        {
            UseOrder((totemTag, 10), (championTag, -5));
            var caster = At(hub, "Caster", blue, new Vector3(-20f, 0f, 0f));
            var totem = At(hub, "Totem", red, Vector3.zero);
            var grunt = At(hub, "Grunt", red, new Vector3(2f, 0f, 0f));
            var champion = At(hub, "Champion", red, new Vector3(4f, 0f, 0f));
            await Frames.Step();
            Carry(totem, totemTag);
            Carry(champion, championTag);
            await Frames.Step();

            var blast = Blast();
            blast.Area = new EffectArea { Radius = 5f, Takes = EAffinityFilter.Enemies };
            var order = Record(blast, ("Totem", totem), ("Grunt", grunt), ("Champion", champion));
            Assert.IsTrue(World.Apply(caster, totem, blast), "it lands on the totem");
            await Frames.Step();
            Assert.That(order, Is.EqualTo(new[] { "Champion", "Grunt", "Totem" }), "its target last, as the totem resolves last");

            order.Clear();
            blast.Area.UseResolveOrder = false;
            Assert.IsTrue(World.Apply(caster, totem, blast));
            await Frames.Step();
            Assert.That(order, Is.EqualTo(new[] { "Totem", "Grunt", "Champion" }), "turned off, its target first, then those near");
        });

        /// <summary>
        /// An entity is placed by the first entry it carries, a tag under the entry's counting as it; one with none is at 0;
        /// and its place follows its tags as they change, as an aura's holder carries a tag only while it holds the aura.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_31_An_entity_is_placed_by_the_first_entry_it_carries([Values] Hub hub) => Live(async () =>
        {
            var ward = Tag.Generate(totemTag.Name + ".Ward");
            UseOrder((totemTag, 10), (championTag, -5));
            var both = At(hub, "Both", red, Vector3.zero);
            var warded = At(hub, "Warded", red, Vector3.right);
            var plain = At(hub, "Plain", red, Vector3.left);
            await Frames.Step();
            Carry(both, championTag);
            var totem = Carry(both, totemTag);
            Carry(warded, ward);
            await Frames.Step();

            Assert.AreEqual(10, ForgeResolveOrder.OrderOf(both), "the totem's entry, listed first, places one carrying both");
            Assert.AreEqual(10, ForgeResolveOrder.OrderOf(warded), "a tag under an entry's counts as it");
            Assert.AreEqual(0, ForgeResolveOrder.OrderOf(plain), "one with none is at 0");

            var targets = new List<ITarget> { both, plain, warded };
            ForgeResolveOrder.Sort(targets);
            Assert.That(targets, Is.EqualTo(new ITarget[] { plain, both, warded }), "lower first, ties as they were");

            Assert.IsTrue(both.RemoveGameplayEffect(totem.Tags.AssetTag), "it stops carrying the totem's tag");
            await Frames.Step(2);
            Assert.AreEqual(-5, ForgeResolveOrder.OrderOf(both), "and the champion's entry places it now");
        });

        /// <summary>
        /// The case the order was asked for: a totem whose aura keeps its allies from dying, and a hero beside it, both
        /// caught in one explosion, the totem resolving first. The hero survives at 1, as the totem's death, and its aura
        /// coming off, wait for the totem's end of frame; then the totem dies.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_31_One_explosions_hits_resolve_together_whatever_their_order([Values] Hub hub) => Live(async () =>
        {
            UseOrder((totemTag, -10));
            var undying = Make.Effect("Undying", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { immortal } });
            undying.WorkerGroup = new StandardWorkerGroup();
            undying.WorkerGroup.AttributeWorkers.Add(new CantFallBelowAttributeWorker { TargetAttribute = health, Floor = 1f });
            var aura = new AuraEntry { Effect = undying, Radius = 5f, Takes = EAffinityFilter.Allies, IncludeHolder = false, Interval = 0f };

            var bomber = At(hub, "Bomber", blue, new Vector3(0f, 0f, 20f));
            var hero = At(hub, "Hero", red, Vector3.zero);
            var totem = At(hub, "Totem", red, new Vector3(2f, 0f, 0f), aura);
            await Frames.Step();
            Carry(totem, totemTag);
            await Frames.Until(() => hero.GetTagCache().GetWeight(immortal) > 0, 30, "the totem's aura holds on the hero");

            var explosion = Blast(-1000f);
            explosion.Area = new EffectArea { Radius = 5f, Takes = EAffinityFilter.Enemies };
            var order = Record(explosion, ("Hero", hero), ("Totem", totem));
            Assert.IsTrue(World.Apply(bomber, hero, explosion), "the explosion lands on the hero");
            Assert.That(order, Is.EqualTo(new[] { "Totem", "Hero" }), "the totem first, by its entry, though the hero was its target");
            Assert.AreEqual(1f, Value(hero, health), 0.01f, "the hero, still under the aura, holds at 1");

            await Frames.Until(() => totem.IsDead, 30, "the totem dies at its frame's end");
            await Frames.Step(3);
            Assert.IsFalse(hero.IsDead, "and the hero lives");
            Assert.AreEqual(1f, Value(hero, health), 0.01f, "at 1");
        });
    }
}
