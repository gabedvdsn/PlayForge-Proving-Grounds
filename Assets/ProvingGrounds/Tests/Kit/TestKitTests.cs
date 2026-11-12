using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using FarEmerald.PlayForge.Testing;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.KitTests
{
    /// <summary>
    /// TW-26, Decision 66: the package's test kit, used as a game's own tests would use it. This assembly references
    /// PlayForge and its kit alone, not Proving Grounds' harness. Each test is a <see cref="ForgeTest"/>, with a live world
    /// made for it and its invariants audited after it.
    /// </summary>
    public class TestKitTests : ForgeTest
    {
        /// <summary>An effect hit on an entity plays out frame by frame, on either hub: a burn's two ticks over its second.</summary>
        [UnityTest]
        public IEnumerator TW_26_A_test_plays_an_effect_on_an_entity_frame_by_frame([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            var source = World.Entity(hub, name: "Source");
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Target = health, Magnitude = -10f, Duration = 1f, PeriodicTicks = true, Ticks = 2,
            });

            Assert.IsTrue(World.Hit(source, target, burn), "the burn lands");
            Assert.IsTrue(World.Has(target, burn), "and lasts");
            await Frames.Until(() => World.Value(target, health) <= 80f, 90, "its two ticks land within its second");
            await Frames.Until(() => !World.Has(target, burn), 30, "and it ends");
            Assert.AreEqual(80f, World.Value(target, health), 0.001f, "having burned 20");
        });

        /// <summary>An entity made from an identity asset, on either hub, starts with its set's attributes.</summary>
        [UnityTest]
        public IEnumerator TW_26_An_entity_from_an_identity_on_either_hub([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var identity = Make.Identity("Hero", Make.Set(Make.Element(health, 75f, 100f)));
            var hero = World.FromIdentity(hub, identity);
            await Frames.Step();

            Assert.AreEqual(75f, World.Value(hero, health), 0.001f, "its current");
            Assert.AreEqual(100f, World.Base(hero, health), 0.001f, "and its base");
            Assert.AreEqual(hub == Hub.Mono, hero is GameplayAbilitySystem, "on the hub asked for");
        });

        /// <summary>Cast gives an ability and starts it, paying its cost; Use starts one the entity has; Hold equips an item.</summary>
        [UnityTest]
        public IEnumerator TW_26_Cast_Use_and_Hold_play_an_entitys_abilities_and_items() => Live(async () =>
        {
            var mana = Make.Attribute("Mana");
            var armor = Make.Attribute("Armor");
            var caster = World.Entity(Hub.Runtime, Make.Set(Make.Element(mana, 50f, 50f), Make.Element(armor, 0f, 0f)), "Caster");

            var spell = Make.Ability("Spell");
            spell.Cost = Make.Effect("Spell Cost", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = mana, Magnitude = -10f });
            Assert.IsTrue(World.Cast(caster, spell), "Cast gives the ability and starts it");
            await Frames.Step(2);
            Assert.AreEqual(40f, World.Value(caster, mana), 0.001f, "paying its cost");

            Assert.IsTrue(World.Use(caster, spell), "Use starts the ability it has");
            await Frames.Step(2);
            Assert.AreEqual(30f, World.Value(caster, mana), 0.001f, "paying again");

            var ring = Make.Item("Ring");
            ring.GrantedEffects.Add(Make.Effect("Ring Armor", new EffectConfig { Target = armor, Magnitude = 5f }));
            World.Hold(caster, ring);
            await Frames.Step();
            Assert.AreEqual(5f, World.Value(caster, armor), 0.001f, "Hold equips an item, its effects on");
        });

        /// <summary>The clock and the rolls are the test's: each frame takes its step, and each roll comes as written, counted.</summary>
        [UnityTest]
        public IEnumerator TW_26_The_clock_and_rolls_are_the_tests_own() => Live(async () =>
        {
            World.Clock(0.25f);
            await Frames.Step();
            Assert.AreEqual(0.25f, ForgeTime.DeltaTime, 0.0001f, "each frame takes the clock's step");

            var rolls = World.Rolls(0.1f, 0.9f);
            Assert.AreEqual(0.1f, ForgeRandom.Value, 0.0001f, "the first roll");
            Assert.AreEqual(0.9f, ForgeRandom.Value, 0.0001f, "the second");
            Assert.AreEqual(0.1f, ForgeRandom.Value, 0.0001f, "and round again");
            Assert.AreEqual(3, rolls.Drawn, "each counted");
        });

        /// <summary>The allocation probe counts what an operation allocates, and steps the frames it measures.</summary>
        [UnityTest]
        public IEnumerator TW_26_The_allocation_probe_counts_what_runs() => Live(async () =>
        {
            using var probe = new AllocationProbe();
            Assert.That(probe.Measure(() => GC.KeepAlive(new byte[4096])), Is.GreaterThanOrEqualTo(4096), "an array's bytes");
            Assert.AreEqual(0f, probe.MeasureEach(() => { }), "and nothing's none");
            var frames = await probe.MeasureFrames(Frames, 5);
            Assert.AreEqual(5, frames.Frames, "over the frames it stepped");
        });
    }

    /// <summary>
    /// TW-26: a world of one's own, without the base class, as a test that has a base class of its own would make one.
    /// </summary>
    public class TestKitWorldTests
    {
        private static IForgeClock CurrentClock()
        {
            var current = ForgeTime.Use(new SteppingClock(1f));
            ForgeTime.Use(current);
            return current;
        }

        private static IForgeRandom CurrentRandom()
        {
            var current = ForgeRandom.Use(new ScriptedRandom(0f));
            ForgeRandom.Use(current);
            return current;
        }

        /// <summary>
        /// It starts, audits as the test asks, finding a process it doesn't own still running, and ends as it's disposed,
        /// taking what it made and giving back the clock and the rolls.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_26_A_world_of_ones_own_audits_a_leak_and_ends() => UniTask.ToCoroutine(async () =>
        {
            var clock = CurrentClock();
            var random = CurrentRandom();
            Attribute strength;
            await using (var world = await ForgeTestWorld.Start())
            {
                Assert.AreSame(world, ForgeTestWorld.Current, "it's the current world");
                strength = world.Make.Attribute("Strength");
                world.Clock(0.5f);
                world.Rolls(0.5f);

                // Registered behind the world's back, so it isn't the world's to end before the audit
                var stray = new RuntimeGameplayAbilitySystem(world.Make.RuntimeIdentity("Stray", world.Make.UnusedSet()));
                Assert.IsTrue(ProcessControl.Register(stray, ProcessDataPacket.Default(), out _), "a stray registers");
                await world.Frames.Step();
                var violations = await world.Audit();
                Assert.That(violations, Has.Some.Contains("leaked"), $"the audit finds it: {string.Join(" | ", violations)}");
            }

            Assert.IsNull(ForgeTestWorld.Current, "ended, the world is gone");
            Assert.IsTrue(strength == null, "with what it made");
            Assert.IsNull(ProcessControl.Instance, "and its ProcessControl");
            Assert.AreSame(clock, CurrentClock(), "the clock is as it was");
            Assert.AreSame(random, CurrentRandom(), "and the rolls");
        });
    }
}
