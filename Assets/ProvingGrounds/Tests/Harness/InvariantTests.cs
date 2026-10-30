using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Each check finds a fault planted for it, and stays quiet on honest work. Planted/PlantedFailures
    /// shows the other half, that a broken invariant really fails a test.
    /// </summary>
    public class InvariantTests : ProvingGroundsTest
    {
        [UnityTest]
        public IEnumerator A_block_nobody_ended_is_a_leak() => Live(async () =>
        {
            var baseline = new HashSet<int>(World.Control.FetchActiveProcesses().Keys);
            var stray = World.NewProbe("Stray");
            ProcessControl.Register(stray, ProcessDataPacket.Default(), out var relay);

            var violations = World.Invariants.CheckBlocks(World.Control, baseline);

            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0], Does.Contain("Stray").And.Contain("Running"));

            relay.TerminateImmediate();
            await Frames.Step(2);
            Assert.That(World.Invariants.CheckBlocks(World.Control, baseline), Is.Empty, "ended properly, it's gone");
        });

        [UnityTest]
        public IEnumerator A_task_never_cleaned_is_reported() => Live(async () =>
        {
            Ledger.Prepared("Forgotten");
            await Frames.Step();

            Assert.That(Ledger.Violations(), Has.Exactly(1).Contains("Forgotten was prepared and never cleaned"));

            Ledger.Cleaned("Forgotten");
            Assert.That(Ledger.Violations(), Is.Empty);
        });

        [UnityTest]
        public IEnumerator A_task_cleaned_twice_is_reported() => Live(async () =>
        {
            Ledger.Prepared("Twice");
            Ledger.Cleaned("Twice");
            Ledger.Cleaned("Twice");
            await Frames.Step();

            Assert.That(ExpectAndClearLedger(), Has.Exactly(1).Contains("Twice was cleaned without a Prepare"));
        });

        [UnityTest]
        public IEnumerator A_task_cleaned_while_running_is_reported() => Live(async () =>
        {
            Ledger.Prepared("Busy");
            Ledger.Started("Busy");
            Ledger.Cleaned("Busy");
            Ledger.Finished("Busy");
            await Frames.Step();

            Assert.That(ExpectAndClearLedger(), Has.Exactly(1).Contains("Busy was cleaned while it ran"));
        });

        [UnityTest]
        public IEnumerator A_task_prepared_twice_is_reported() => Live(async () =>
        {
            Ledger.Prepared("Again");
            Ledger.Prepared("Again");
            Ledger.Cleaned("Again");
            await Frames.Step();

            Assert.That(ExpectAndClearLedger(), Has.Exactly(1).Contains("Again was prepared again before it was cleaned"));
        });

        [UnityTest]
        public IEnumerator A_real_ability_pairs_its_prepares_and_cleans() => Live(async () =>
        {
            var task = new ProbeAbilityTask(World, "Honest", frames: 1);
            var gas = World.MonoEntity();
            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Honest", Make.Stage(task)), 1, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Honest") == 1, 30, "the task is cleaned");

            Assert.That(Ledger.Violations(), Is.Empty);
        });

        [UnityTest]
        public IEnumerator Slices_that_miss_ActiveValue_are_reported() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 100f, 100f)));
            var buff = Make.Effect("Buff", new EffectConfig { Target = health, Magnitude = 10f, Slot = EAttributeTarget.Current });
            World.Apply(gas, gas, buff);
            await Frames.Step();

            Assert.That(World.Invariants.CheckAttributes(), Is.Empty, "honest slices sum");

            var attribution = Attribution(gas, health);
            var group = attribution.Keys.First();
            var honest = attribution[group];
            attribution[group] = honest + new AttributeValue(5f, 0f);

            Assert.That(World.Invariants.CheckAttributes(), Has.Exactly(1).Contains("Health slices sum to"));

            attribution[group] = honest;
        });

        [UnityTest]
        public IEnumerator Dealt_and_received_agree_between_two_entities() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var attacker = World.RuntimeEntity(Make.Set(Make.Element(health, 100f, 100f)), "Attacker");
            var defender = World.MonoEntity(Make.Set(Make.Element(health, 100f, 100f)), "Defender");
            var hit = Make.Effect("Hit", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f, Slot = EAttributeTarget.Current,
            });

            World.Apply(attacker, defender, hit);
            await Frames.Step(2);

            Assert.That(World.Invariants.CheckAttributes(), Is.Empty);
        });

        [UnityTest]
        public IEnumerator Damage_nobody_received_is_reported() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 100f, 100f)));
            var hit = Make.Effect("Hit", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f, Slot = EAttributeTarget.Current,
            });

            ImpactData landed = default;
            gas.Callbacks.OnImpactReceived += impact => landed = impact;
            World.Apply(gas, gas, hit);
            gas.RecordFrameImpactDealt(landed);
            await Frames.Step();

            var violations = World.Invariants.CheckAttributes();
            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0], Does.Contain("dealt -60/0").And.Contain("received -30/0"));

            gas.RecordFrameImpactReceived(landed);
            await Frames.Step();
            Assert.That(World.Invariants.CheckAttributes(), Is.Empty, "evened up, it's quiet");
        });

        private List<string> ExpectAndClearLedger()
        {
            var violations = Ledger.Violations();
            ClearLedger();
            return violations;
        }

        // A planted ledger fault has to go before teardown, which would otherwise fail this test for it
        private void ClearLedger()
        {
            var field = typeof(TaskLedger).GetField("_violations", BindingFlags.NonPublic | BindingFlags.Instance);
            ((List<string>)field.GetValue(Ledger)).Clear();
        }

        private static Dictionary<Tag, AttributeValue> Attribution(RuntimeGameplayAbilitySystem gas, IAttribute attribute)
        {
            gas.GetAttributeSystem().TryGetCachedValue(attribute, out var cached);
            var field = typeof(CachedAttributeValue).GetField("attribution", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Dictionary<Tag, AttributeValue>)field.GetValue(cached);
        }
    }
}
