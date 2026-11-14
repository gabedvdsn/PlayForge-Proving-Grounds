using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-15, Decision 53: the common rules, built in. Healing received scales each heal, 0 blocking it; lifesteal heals an
    /// effect's caster, or a holder, for a share of the damage dealt; an execute takes the rest below a share; Can't Fall
    /// Below keeps an attribute at its floor; and damage returns to its dealer once, never returned again.
    /// </summary>
    public class CommonRulesTests : ProvingGroundsTest
    {
        private ForgeAttribute health, healing;
        private readonly List<Object> made = new();

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            healing = Make.Attribute("Healing Received");
        }

        [TearDown]
        public void Clean()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name, float current = 100f, float healingReceived = 1f,
            params AbstractAttributeWorker[] workers)
        {
            var set = Make.Set(
                Make.Element(health, current, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(healing, healingReceived, healingReceived));
            foreach (var worker in workers) set.WorkerGroup.AttributeWorkers.Add(worker);
            return World.Entity(hub, set, name);
        }

        private GameplayEffect Change(float amount) =>
            Make.Effect(amount < 0f ? "Blow" : "Heal", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = amount });

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>Each workable gain to Health is multiplied by Healing Received; 0 blocks it; a loss is left alone.</summary>
        [UnityTest]
        public IEnumerator TW_15_Healing_received_scales_each_heal([Values] Hub hub) => Live(async () =>
        {
            health.GainsScaledBy = healing;
            var halved = Fighter(hub, "Halved", 50f, 0.5f);
            var blocked = Fighter(hub, "Blocked", 50f, 0f);
            var healer = Fighter(hub, "Healer");
            await Frames.Step();

            Assert.IsTrue(World.Apply(healer, halved, Change(20f)));
            Assert.IsTrue(World.Apply(healer, blocked, Change(20f)));
            await Frames.Step(2);
            Assert.AreEqual(60f, Value(halved, health), 0.01f, "half of a 20 heal");
            Assert.AreEqual(50f, Value(blocked, health), 0.01f, "and none at 0, as Ice Blast's no-healing");

            Assert.IsTrue(World.Apply(healer, halved, Change(-10f)));
            await Frames.Step(2);
            Assert.AreEqual(50f, Value(halved, health), 0.01f, "a loss, all of it");
        });

        /// <summary>An effect's own lifesteal heals its caster a share of each loss it deals.</summary>
        [UnityTest]
        public IEnumerator TW_15_An_effects_lifesteal_heals_its_caster([Values] Hub hub) => Live(async () =>
        {
            // Through the Lifesteal worker its project gives every entity, healing the Health role's attribute (FN-15)
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            var was = ForgeProject.Use(project);
            try
            {
                var caster = Fighter(hub, "Caster", 50f);
                var target = Fighter(hub, "Target");
                var strike = Change(-20f);
                strike.Lifesteal = new EffectLifesteal { Share = 0.5f };
                await Frames.Step();

                Assert.IsTrue(World.Apply(caster, target, strike));
                await Frames.Step(3);
                Assert.AreEqual(80f, Value(target, health), 0.01f, "the strike's 20");
                Assert.AreEqual(60f, Value(caster, health), 0.01f, "and half of it heals its caster");
            }
            finally
            {
                ForgeProject.Use(was);
                Object.DestroyImmediate(project);
            }
        });

        /// <summary>
        /// A holder's lifesteal is its Lifesteal, which an item raises by an effect while it's equipped and the Lifesteal
        /// worker heals by; its Lifesteal list is gone (FN-15).
        /// </summary>
        [UnityTest]
        public IEnumerator TW_15_A_holders_lifesteal_heals_it_for_its_hits([Values] Hub hub) => Live(async () =>
        {
            var lifesteal = Make.Attribute("Lifesteal");
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_LIFESTEAL, Attribute = lifesteal });
            var was = ForgeProject.Use(project);
            try
            {
                var holder = World.Entity(hub, Make.Set(
                    Make.Element(health, 50f, 100f, EAttributeOverflowPolicy.ZeroToBase), Make.Element(lifesteal, 0f, 0f)), "Holder");
                var target = Fighter(hub, "Target");
                var mask = Make.Item("Vampiric Mask");
                mask.GrantedEffects.Add(Make.Effect("Vampirism", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = lifesteal, Magnitude = 0.25f }));
                await Frames.Step();
                Assert.That(holder.GetItemSystem().GiveAndEquipItem(mask, out _), "the mask's equipped");
                await Frames.Step();
                Assert.AreEqual(0.25f, Value(holder, lifesteal), 0.001f, "and raises its holder's Lifesteal");

                Assert.IsTrue(World.Apply(holder, target, Change(-40f)));
                await Frames.Step(3);
                Assert.AreEqual(60f, Value(holder, health), 0.01f, "a quarter of the 40 it dealt");
            }
            finally
            {
                ForgeProject.Use(was);
                Object.DestroyImmediate(project);
            }
        });

        /// <summary>An execute takes the rest of a hit that would leave less than its share of the base.</summary>
        [UnityTest]
        public IEnumerator TW_15_An_execute_takes_the_rest_below_its_share([Values] Hub hub) => Live(async () =>
        {
            var low = Fighter(hub, "Low", 30f);
            var high = Fighter(hub, "High", 50f);
            var caster = Fighter(hub, "Caster");
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            made.Add(pipeline);
            pipeline.Stages = new List<AbstractValueStage> { new ExecuteStage { Share = 0.2f, Attribute = health } };
            var cull = Change(-15f);
            cull.ImpactSpecification.Pipeline = pipeline;
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, low, cull));
            Assert.IsTrue(World.Apply(caster, high, cull));
            await Frames.Step(2);
            Assert.AreEqual(0f, Value(low, health), 0.01f, "15 would leave 15, under a fifth of 100: it takes the rest");
            Assert.AreEqual(35f, Value(high, health), 0.01f, "where it leaves 35, it's a hit of 15");
        });

        /// <summary>Can't Fall Below keeps its attribute at its floor, its own or a buff's while it lasts.</summary>
        [UnityTest]
        public IEnumerator TW_15_A_floor_keeps_its_attribute_from_falling_below_it([Values] Hub hub) => Live(async () =>
        {
            var steady = Fighter(hub, "Steady", 100f, 1f, new CantFallBelowAttributeWorker { TargetAttribute = health, Floor = 1f });
            var graved = Fighter(hub, "Graved");
            var foe = Fighter(hub, "Foe");
            var shallowGrave = Make.Effect("Shallow Grave", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            shallowGrave.WorkerGroup = new StandardWorkerGroup();
            shallowGrave.WorkerGroup.AttributeWorkers.Add(new CantFallBelowAttributeWorker { TargetAttribute = health, Floor = 1f });
            await Frames.Step();
            Assert.IsTrue(World.Apply(foe, graved, shallowGrave), "Shallow Grave's on");
            await Frames.Step();

            Assert.IsTrue(World.Apply(foe, steady, Change(-150f)));
            Assert.IsTrue(World.Apply(foe, graved, Change(-150f)));
            await Frames.Step(2);
            Assert.AreEqual(1f, Value(steady, health), 0.01f, "its own floor holds it at 1");
            Assert.AreEqual(1f, Value(graved, health), 0.01f, "and Shallow Grave's, while it lasts");
        });

        /// <summary>Damage returns to its dealer, typed so the dealer's own return doesn't send it back.</summary>
        [UnityTest]
        public IEnumerator TW_15_Damage_returns_to_its_dealer_once([Values] Hub hub) => Live(async () =>
        {
            var first = Fighter(hub, "First", 100f, 1f, new ReturnDamageAttributeWorker { TargetAttribute = health, Share = 1f });
            var second = Fighter(hub, "Second", 100f, 1f, new ReturnDamageAttributeWorker { TargetAttribute = health, Share = 1f });
            await Frames.Step();

            Assert.IsTrue(World.Apply(second, first, Change(-20f)));
            await Frames.Step(4);
            Assert.AreEqual(80f, Value(first, health), 0.01f, "the first takes its 20");
            Assert.AreEqual(80f, Value(second, health), 0.01f, "and returns it, all of it, to the second");
            await Frames.Step(4);
            Assert.AreEqual(80f, Value(first, health), 0.01f, "whose own return doesn't send it back");
        });
    }
}
