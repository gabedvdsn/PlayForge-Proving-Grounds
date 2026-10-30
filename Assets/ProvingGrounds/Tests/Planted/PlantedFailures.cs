using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Every test here must FAIL. The harness's checks run in teardown and through the log, where a test
    /// can't catch their failures itself, so the proof that they fail a test is a run of tests that fail.
    /// A planted test that passes means a check has gone quiet.
    ///
    /// They're Explicit so a normal run skips them. Tools/check-planted.sh runs them and checks each one
    /// failed for its own reason, which is the text in its Description.
    /// </summary>
    [Explicit, Category("Planted")]
    public class PlantedFailures : ProvingGroundsTest
    {
        [UnityTest, Description("control block #")]
        public IEnumerator A_leaked_control_block() => Live(async () =>
        {
            ProcessControl.Register(World.NewProbe("Leaked"), ProcessDataPacket.Default(), out _);
            await Frames.Step();
        });

        [UnityTest, Description("Forgotten was prepared and never cleaned")]
        public IEnumerator A_task_never_cleaned() => Live(async () =>
        {
            Ledger.Prepared("Forgotten");
            await Frames.Step();
        });

        [UnityTest, Description("Health slices sum to")]
        public IEnumerator Slices_that_miss_ActiveValue() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 100f, 100f)));
            World.Apply(gas, gas, Make.Effect("Buff", new EffectConfig { Target = health, Magnitude = 10f, Slot = EAttributeTarget.Current }));
            await Frames.Step();

            gas.GetAttributeSystem().TryGetCachedValue(health, out var cached);
            var field = typeof(CachedAttributeValue).GetField("attribution",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var attribution = (System.Collections.Generic.Dictionary<Tag, AttributeValue>)field.GetValue(cached);
            foreach (var group in new System.Collections.Generic.List<Tag>(attribution.Keys)) attribution[group] += new AttributeValue(5f, 0f);
        });

        [UnityTest, Description("but its targets received")]
        public IEnumerator Damage_nobody_received() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 100f, 100f)));
            ImpactData landed = default;
            gas.Callbacks.OnImpactReceived += impact => landed = impact;

            World.Apply(gas, gas, Make.Effect("Hit", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f, Slot = EAttributeTarget.Current,
            }));
            gas.RecordFrameImpactDealt(landed);
            await Frames.Step();
        });

        [UnityTest, Description("Expected log did not appear")]
        public IEnumerator A_swallowed_fault() => Live(async () =>
        {
            Faults.Arm("Swallowed");
            try
            {
                Faults.Trip("Swallowed");
            }
            catch (InjectedFault)
            {
                // Nothing reports it, as a bare catch wouldn't
            }

            await Frames.Step();
        });

        [UnityTest, Description("Unhandled log message")]
        public IEnumerator An_error_nobody_expected() => Live(async () =>
        {
            Debug.LogError("Nobody expected this");
            await Frames.Step();
        });

        [UnityTest, Description("not true within 2 frames")]
        public IEnumerator A_wait_that_runs_out() => Live(async () =>
        {
            await Frames.Until(() => false, 2, "never");
        });
    }
}
