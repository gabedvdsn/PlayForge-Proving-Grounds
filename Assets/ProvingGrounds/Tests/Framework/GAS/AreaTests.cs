using System.Reflection;
using System.Linq;
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
    /// TW-10, Decision 50: areas without physics. An area search finds each entity on either hub by its position, with no
    /// collider, flat along the ground unless it says, around the caster, the activation's target or its point; physics is
    /// a search's to ask for. An effect's own Area lands it on each the area takes near its target, and spreads no further.
    /// </summary>
    public class AreaTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag red, blue, marked;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            red = Make.NewTag("Test.TW10.Red");
            blue = Make.NewTag("Test.TW10.Blue");
            marked = Make.NewTag("Test.TW10.Marked");
        }

        // An entity of a side, at a point, on either hub
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, Vector3 at)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = at;
            return gas;
        }

        private GameplayEffect Mark(string name = "Mark") => Make.Effect(name, new EffectConfig
        {
            Policy = EEffectDurationPolicy.Durational, Duration = 5f, GrantedTags = new List<Tag> { marked },
        });

        private bool Marked(IGameplayAbilitySystem gas) => gas.GetTagCache().GetWeight(marked) > 0;

        // An ability whose targeting is the task given, and whose one stage marks each target it finds
        private Ability Searching(string name, AbstractTargetingAbilityTask search)
        {
            var ability = Make.Ability(name, Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Mark() } }));
            ability.Behaviour.Targeting = search;
            return ability;
        }

        private async UniTask Cast(IGameplayAbilitySystem caster, Ability ability, ProcessDataPacket data = null)
        {
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out _), $"{ability.name} is known");
            Assert.IsTrue(abilities.TryActivateAbility(ability, out _, data), $"{ability.name} casts");
            await Frames.Step(4);
        }

        /// <summary>The search finds each entity by its position, on either hub, with no collider; flat unless it says.</summary>
        [UnityTest]
        public IEnumerator TW_10_An_area_search_finds_each_entity_by_its_position([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var near = At(hub, "Near", blue, new Vector3(3f, 0f, 0f));
            var far = At(hub, "Far", blue, new Vector3(7f, 0f, 0f));
            var high = At(hub, "High", blue, new Vector3(1f, 10f, 0f));
            await Frames.Step();

            var found = new List<ITarget>();
            ForgeEntities.Within(Vector3.zero, 5f, true, found);
            CollectionAssert.Contains(found, near, "flat: the near one");
            CollectionAssert.Contains(found, high, "and the one high above it");
            CollectionAssert.DoesNotContain(found, far, "but not the far one");
            if (GameRoot.Instance != null) CollectionAssert.DoesNotContain(found, GameRoot.Instance, "nor GameRoot, the game's manager");
            found.Clear();
            ForgeEntities.Within(Vector3.zero, 5f, false, found);
            CollectionAssert.Contains(found, near, "a sphere: the near one");
            CollectionAssert.DoesNotContain(found, high, "but not the one high above");

            await Cast(caster, Searching("Shout", new AutoTargetTask { Range = 5f, MaxTargets = 0 }));
            Assert.IsTrue(Marked(near), "the search finds the near one, with no collider");
            Assert.IsTrue(Marked(high), "and, flat, the one above it");
            Assert.IsFalse(Marked(far), "not the far one");
            Assert.IsFalse(Marked(caster), "nor its caster, which it didn't ask for");
        });

        /// <summary>Physics is a search's to ask for: then a MonoBehaviour entity needs a collider, as before.</summary>
        [UnityTest]
        public IEnumerator TW_10_An_area_search_uses_physics_where_it_asks([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var near = At(hub, "Near", blue, new Vector3(3f, 0f, 0f));
            await Frames.Step();

            await Cast(caster, Searching("Shout", new AutoTargetTask { Range = 5f, MaxTargets = 0, UsePhysics = true }));
            if (hub == Hub.Mono) Assert.IsFalse(Marked(near), "with physics, a MonoBehaviour entity with no collider isn't found");
            else Assert.IsTrue(Marked(near), "a runtime entity is found by its position, physics or not");
        });

        /// <summary>A search around the activation's point or target, of the sides it takes, its caster with it where it asks.</summary>
        [UnityTest]
        public IEnumerator TW_10_An_area_search_is_measured_from_where_it_says([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var ally = At(hub, "Ally", red, new Vector3(2f, 0f, 0f));
            var enemy = At(hub, "Enemy", blue, new Vector3(1f, 0f, 0f));
            var yonder = At(hub, "Yonder", blue, new Vector3(20f, 0f, 0f));
            await Frames.Step();

            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.POSITION, new Vector3(21f, 0f, 0f));
            await Cast(caster, Searching("Meteor", new AutoTargetTask { Range = 3f, MaxTargets = 0, Around = EAreaCentre.Point }), data);
            Assert.IsTrue(Marked(yonder), "around its point");
            Assert.IsFalse(Marked(enemy), "not around its caster");

            await Cast(caster, Searching("Rally", new AutoTargetByAffinityTask
            {
                AffinityFilter = EAffinityFilter.Allies, Range = 5f, MaxTargets = 0, IncludeSelf = true,
            }));
            Assert.IsTrue(Marked(ally), "its allies");
            Assert.IsTrue(Marked(caster), "and its caster, which it asked for");
            Assert.IsFalse(Marked(enemy), "not an enemy");
        });

        /// <summary>
        /// Area Effect, the Demo's own task in the framework now (FN-24): its effects land on each of the sides it takes
        /// within its radius of its caster, found by position on either hub, its caster left out.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_24_An_area_effect_lands_on_those_near_its_caster([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var near = At(hub, "Near", blue, new Vector3(2f, 0f, 0f));
            var friend = At(hub, "Friend", red, new Vector3(1f, 0f, 0f));
            var far = At(hub, "Far", blue, new Vector3(8f, 0f, 0f));
            await Frames.Step();

            var shout = Make.Ability("Shout", Make.Stage(new AreaEffectTask
            {
                Radius = 3f, Around = EAreaCentre.Caster, Takes = EAffinityFilter.Enemies, Effects = new List<GameplayEffect> { Mark() },
            }));
            await Cast(caster, shout);
            Assert.IsTrue(Marked(near), "an enemy within its radius");
            Assert.IsFalse(Marked(friend), "not an ally");
            Assert.IsFalse(Marked(far), "nor one beyond it");
            Assert.IsFalse(Marked(caster), "nor its caster");
        });

        /// <summary>
        /// Apply To Caster, the Demo's own task in the framework now (FN-24): its effects land on its caster, whatever its
        /// ability targets.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_24_Apply_to_caster_lands_on_its_caster_whatever_it_targets([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var enemy = At(hub, "Enemy", blue, new Vector3(2f, 0f, 0f));
            await Frames.Step();

            var empower = Make.Ability("Empower", Make.Stage(new ApplyToCasterTask { Effects = new List<GameplayEffect> { Mark() } }));
            empower.Behaviour.Targeting = new AutoTargetByAffinityTask { AffinityFilter = EAffinityFilter.Enemies, Range = 5f, MaxTargets = 1, UseCastRange = false };
            await Cast(caster, empower);
            Assert.IsTrue(Marked(caster), "its caster");
            Assert.IsFalse(Marked(enemy), "not the enemy it targets");
        });

        /// <summary>
        /// The targeting tasks drew headers between their fields, which the user's notes took out (FN-20): none has one, and
        /// each number of theirs is Scalable, but a pointer's button, which picks one of three (FN-19's rule).
        /// </summary>
        [Test]
        public void FN_20_The_targeting_tasks_have_no_headers_and_Scalable_numbers()
        {
            var tasks = typeof(AbstractTargetingAbilityTask).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(AbstractTargetingAbilityTask).IsAssignableFrom(type)).ToList();
            Assert.That(tasks, Is.Not.Empty, "there are targeting tasks");
            foreach (var task in tasks)
            {
                foreach (var field in task.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    Assert.IsNull(field.GetCustomAttribute<HeaderAttribute>(), $"{task.Name}.{field.Name} has no header");
                    if (field.Name == "MouseButton") continue;
                    Assert.That(field.FieldType != typeof(float) && field.FieldType != typeof(int), $"{task.Name}.{field.Name} is a Scalable number");
                }
            }
            Assert.AreEqual(typeof(ScalableFloat), typeof(TargetAttributeCondition).GetField(nameof(TargetAttributeCondition.CompareValue))?.FieldType,
                "and an attribute condition's value");
        }

        /// <summary>
        /// A search's Range is a Scalable number, read for its activation at its ability's level (FN-20): a Range of 5 at level
        /// 1 and 10 after reaches the far one only at level 2.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_20_A_searchs_range_reads_at_its_abilitys_level([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var near = At(hub, "Near", blue, new Vector3(3f, 0f, 0f));
            var far = At(hub, "Far", blue, new Vector3(7f, 0f, 0f));
            await Frames.Step();

            AutoTargetByAffinityTask Search()
            {
                var search = new AutoTargetByAffinityTask { AffinityFilter = EAffinityFilter.Enemies, MaxTargets = 0, UseCastRange = false };
                // Its Range by its level, set by name, as a number of any kind
                var range = typeof(AutoTargetByAffinityTask).GetField(nameof(AutoTargetByAffinityTask.Range));
                range.SetValue(search, new ScalableFloat(new SimpleScaler { LevelValues = new[] { 5f, 10f, 10f, 10f }, MaxLevel = 4, StretchOverLevels = false }, 1f));
                return search;
            }

            var abilities = caster.GetAbilitySystem();
            var low = Searching("Shout", Search());
            Assert.That(abilities.GiveAbility(low, 1, out _), "Shout is known at level 1");
            Assert.IsTrue(abilities.TryActivateAbility(low, out _, null), "and cast");
            await Frames.Step(4);
            Assert.IsTrue(Marked(near), "at level 1, its Range of 5 reaches the near one");
            Assert.IsFalse(Marked(far), "and not the far one");

            var high = Searching("Bellow", Search());
            Assert.That(abilities.GiveAbility(high, 2, out _), "Bellow is known at level 2");
            Assert.IsTrue(abilities.TryActivateAbility(high, out _, null), "and cast");
            await Frames.Step(4);
            Assert.IsTrue(Marked(far), "at level 2, its Range of 10 reaches the far one");
        });

        /// <summary>
        /// An area's Max Targets became a Scalable whole number with no value of its own, so an area made in code had none,
        /// and landing one threw (FN-19a). It reads 0, every one it reaches, as a saved area's does.
        /// </summary>
        [Test]
        public void FN_19a_An_area_made_in_code_takes_every_one_it_reaches()
        {
            var area = new EffectArea { Radius = 3f };
            Assert.IsNotNull(area.MaxTargets, "an area made in code has a Max Targets");
            Assert.IsTrue(area.MaxTargets.IsConstant, "a plain number");
            Assert.AreEqual(0, area.MaxTargets.Constant, "of 0, every one it reaches");
        }

        /// <summary>
        /// An effect's Area lands it on each it takes near its target, as Storm Hammer stuns those near the one it hits: of
        /// the sides it takes, at most its most, the nearest first; and an effect that lands by an area spreads no further.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_10_An_effect_with_an_area_lands_on_those_near_its_target([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, Vector3.zero);
            var target = At(hub, "Target", blue, new Vector3(10f, 0f, 0f));
            var beside = At(hub, "Beside", blue, new Vector3(12f, 0f, 0f));
            var chained = At(hub, "Chained", blue, new Vector3(14.5f, 0f, 0f));
            var friend = At(hub, "Friend", red, new Vector3(11f, 0f, 0f));
            var away = At(hub, "Away", blue, new Vector3(16f, 0f, 0f));
            await Frames.Step();

            var hammer = Mark("Storm Hammer");
            hammer.Components.Add(new AreaEffectComponent { Area = new EffectArea { Radius = 3f, Takes = EAffinityFilter.Enemies } });
            Assert.IsTrue(World.Apply(caster, target, hammer), "it lands on its target");
            await Frames.Step(2);
            Assert.IsTrue(Marked(target), "its target");
            Assert.IsTrue(Marked(beside), "an enemy near it");
            Assert.IsFalse(Marked(friend), "not the caster's own side");
            Assert.IsFalse(Marked(away), "nor one out of reach");
            Assert.IsFalse(Marked(chained), "nor one near those the area took: it spreads no further");

            var splash = Mark("Splash");
            splash.Components.Add(new AreaEffectComponent { Area = new EffectArea { Radius = 3f, Takes = EAffinityFilter.AlliesAndEnemies, MaxTargets = 1 } });
            var second = At(hub, "Second", blue, new Vector3(30f, 0f, 0f));
            var closest = At(hub, "Closest", red, new Vector3(31f, 0f, 0f));
            var next = At(hub, "Next", blue, new Vector3(32f, 0f, 0f));
            await Frames.Step();
            Assert.IsTrue(World.Apply(caster, second, splash));
            await Frames.Step(2);
            Assert.IsTrue(Marked(closest), "at most one besides its target, the nearest");
            Assert.IsFalse(Marked(next), "so not the next");
        });
    }
}
