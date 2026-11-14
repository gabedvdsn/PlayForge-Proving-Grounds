using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FN-15, Decision 78: lifesteal is an attribute the Lifesteal worker reads. Every entity has the worker through its
    /// project's Workers, or through its own; it heals its holder its Lifesteal's share of each loss it deals another, of the
    /// impact types it names; an effect has no lifesteal of its own, its hits healing by its caster's (FN-22); and two of
    /// the worker heal a hit once.
    /// </summary>
    public class LifestealTests : ProvingGroundsTest
    {
        private ForgeAttribute health, lifesteal;
        private Tag attack, spell;
        private ForgeProject project, was;

        [SetUp]
        public void Project()
        {
            health = Make.Attribute("Health");
            lifesteal = Make.Attribute("Lifesteal");
            attack = Make.NewTag("Test.FN15.Attack");
            spell = Make.NewTag("Test.FN15.Spell");
            project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_LIFESTEAL, Attribute = lifesteal });
            was = ForgeProject.Use(project);
        }

        [TearDown]
        public void PutBack()
        {
            ForgeProject.Use(was);
            Object.DestroyImmediate(project);
        }

        // An entity at a share of its Health, with a Lifesteal of its own
        private IGameplayAbilitySystem Fighter(Hub hub, string name, float healthNow = 100f, float share = 0f, params AbstractImpactWorker[] own)
        {
            var set = Make.Set(
                Make.Element(health, healthNow, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(lifesteal, share, share));
            foreach (var worker in own) set.WorkerGroup.ImpactWorkers.Add(worker);
            return World.Entity(hub, set, name);
        }

        private float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        // A blow of an amount, of an impact type where it's given
        private GameplayEffect Blow(string name, float amount, Tag? type = null)
        {
            var blow = Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });
            if (type.HasValue) blow.ImpactSpecification.ImpactTypes.Add(type.Value);
            return blow;
        }

        /// <summary>A project's Workers hold the Lifesteal worker by default, which every entity in it has.</summary>
        [Test]
        public void FN_15_A_projects_workers_hold_the_lifesteal_worker()
        {
            Assert.That(new ForgeProjectSettings().Workers.ImpactWorkers, Has.Some.InstanceOf<LifestealImpactWorker>(),
                "a project's workers, which every entity has, hold the Lifesteal worker");
            Assert.That(project.Settings.Workers.ImpactWorkers, Has.Some.InstanceOf<LifestealImpactWorker>(), "this one's among them");
        }

        /// <summary>
        /// The worker heals its holder its Lifesteal's share of each loss it deals another, to the attribute playing the
        /// Health role; with the project's Workers empty, no entity has it, and nothing heals.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_15_The_worker_heals_by_its_holders_lifesteal([Values] Hub hub) => Live(async () =>
        {
            var leech = Fighter(hub, "Leech", healthNow: 50f, share: 0.25f);
            var target = Fighter(hub, "Target");
            await Frames.Step();

            Assert.IsTrue(World.Apply(leech, target, Blow("Bite", 40f)));
            await Frames.Step(3);
            Assert.AreEqual(60f, Value(target, health), 0.01f, "the bite's 40");
            Assert.AreEqual(60f, Value(leech, health), 0.01f, "and a quarter of it heals the leech, its Lifesteal's share");

            project.Settings.Workers.ImpactWorkers.Clear();
            var bare = Fighter(hub, "Bare", healthNow: 50f, share: 0.25f);
            await Frames.Step();
            Assert.IsTrue(World.Apply(bare, target, Blow("Bite", 40f)));
            await Frames.Step(3);
            Assert.AreEqual(50f, Value(bare, health), 0.01f, "with no worker in its project, its Lifesteal heals nothing");
        });

        /// <summary>
        /// An effect has no lifesteal of its own: its hits heal its caster by the caster's Lifesteal alone, which an effect on
        /// the caster raises while it should (FN-22). An effect's own Lifesteal raised its caster's for that effect's hits
        /// alone (Decision 78), which the user's notes took out.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_22_An_effects_hits_heal_by_its_casters_Lifesteal_alone([Values] Hub hub) => Live(async () =>
        {
            Assert.IsNull(typeof(GameplayEffect).GetField("Lifesteal"), "an effect has no Lifesteal of its own");
            Assert.IsNull(typeof(GameplayEffect).Assembly.GetType("FarEmerald.PlayForge.EffectLifesteal"), "nor the kind it held");

            var caster = Fighter(hub, "Caster", healthNow: 50f, share: 0.25f);
            var target = Fighter(hub, "Target");
            await Frames.Step();
            Assert.IsTrue(World.Apply(caster, target, Blow("Strike", 20f)));
            await Frames.Step(3);
            Assert.AreEqual(55f, Value(caster, health), 0.01f, "a quarter of the strike's 20, its Lifesteal's share");

            var frenzy = Make.Effect("Frenzy", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = lifesteal, Magnitude = 0.5f });
            Assert.IsTrue(World.Apply(caster, caster, frenzy));
            await Frames.Step();
            Assert.IsTrue(World.Apply(caster, target, Blow("Strike", 20f)));
            await Frames.Step(3);
            Assert.AreEqual(70f, Value(caster, health), 0.01f, "and three quarters while an effect raises it by 0.5");
        });

        /// <summary>The worker heals only the impact types it names, as attacks; with none named, any.</summary>
        [UnityTest]
        public IEnumerator FN_15_The_worker_heals_only_the_types_it_names([Values] Hub hub) => Live(async () =>
        {
            foreach (var worker in project.Settings.Workers.ImpactWorkers)
                if (worker is LifestealImpactWorker steal) steal.ImpactTypes = new List<Tag> { attack };
            var leech = Fighter(hub, "Leech", healthNow: 50f, share: 0.5f);
            var target = Fighter(hub, "Target");
            await Frames.Step();

            Assert.IsTrue(World.Apply(leech, target, Blow("Bolt", 20f, spell)));
            await Frames.Step(3);
            Assert.AreEqual(50f, Value(leech, health), 0.01f, "a spell heals nothing");
            Assert.IsTrue(World.Apply(leech, target, Blow("Swing", 20f, attack)));
            await Frames.Step(3);
            Assert.AreEqual(60f, Value(leech, health), 0.01f, "an attack heals half of its 20");
        });

        /// <summary>An entity with the worker of its own as well as its project's heals a hit once.</summary>
        [UnityTest]
        public IEnumerator FN_15_Two_lifesteal_workers_heal_a_hit_once([Values] Hub hub) => Live(async () =>
        {
            var leech = Fighter(hub, "Leech", healthNow: 50f, share: 0.25f, new LifestealImpactWorker());
            var target = Fighter(hub, "Target");
            await Frames.Step();

            Assert.IsTrue(World.Apply(leech, target, Blow("Bite", 40f)));
            await Frames.Step(3);
            Assert.AreEqual(60f, Value(leech, health), 0.01f, "a quarter of the 40, once");
        });

        /// <summary>
        /// A frame in which the worker runs allocates nothing, whether it heals or, with no Lifesteal, heals nothing, as the
        /// relative-operation worker's lifesteal allocates nothing (GAS-P9).
        /// </summary>
        [UnityTest]
        public IEnumerator FN_15_A_frame_in_which_the_lifesteal_worker_runs_allocates_nothing([Values] Hub hub, [Values] bool heals) => Live(async () =>
        {
            var set = Make.Set(Make.Element(health, 1f, 1e6f), Make.Element(lifesteal, heals ? 0.5f : 0f, heals ? 0.5f : 0f));
            var leech = World.Entity(hub, set, name: "Leech", observed: false);
            var bled = World.Entity(hub, Make.Set(Make.Element(health, 1e6f, 1e6f)), name: "Bled", observed: false);
            World.Apply(leech, bled, Make.Effect("Bleed", new EffectConfig { Target = health, Magnitude = -1f, PeriodicTicks = true, FixedDeltaTime = 1f }));
            await Frames.Step(20);

            leech.TryGetAttributeValue(health, out var before);
            using var probe = new AllocationProbe();
            var frames = await probe.MeasureFrames(Frames, 600);
            leech.TryGetAttributeValue(health, out var after);

            if (heals) Assert.That(after.CurrentValue - before.CurrentValue, Is.GreaterThanOrEqualTo(299f), "it healed half a point every frame");
            else Assert.AreEqual(before.CurrentValue, after.CurrentValue, 0.001f, "with no Lifesteal it heals nothing");
            Assert.That(frames.Total, Is.EqualTo(0), frames.ToString());
        });
    }
}
