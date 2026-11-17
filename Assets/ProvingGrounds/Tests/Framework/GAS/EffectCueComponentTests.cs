using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// An effect's Cues component (EP-53): its cues play at the effect's moments, each list its own, as an ability's, an
    /// entity's and an item's do; and at the moments its components newly hear: an application taken in, one refused, and
    /// its stacks changing.
    /// </summary>
    public class EffectCueComponentTests : ProvingGroundsTest
    {
        // A cue that writes down each time it plays: what raised it, a tick's count or a stack's, and a lasting cue's key
        private sealed class Probe : AbstractCueHandler
        {
            public readonly List<string> Heard = new();
            public readonly List<object> Keys = new();

            public override void Burst(CueContext cue) => Heard.Add(cue.Moment switch
            {
                ECueMoment.Ticked => $"Ticked {cue.Ticks}",
                ECueMoment.Stacked => $"Stacked {cue.Magnitude}",
                _ => cue.Moment.ToString(),
            });

            public override void Begin(CueContext cue)
            {
                Heard.Add("Begin");
                Keys.Add(cue.Key);
            }

            public override void End(CueContext cue)
            {
                Heard.Add("End");
                Keys.Add(cue.Key);
            }
        }

        // A second of game time each frame
        private sealed class SecondClock : IForgeClock
        {
            public float DeltaTime => 1f;
            public float UnscaledDeltaTime => 1f;
            public float FixedDeltaTime => 1f;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        // The effect's Cues component, with the probe in each list named
        private static Probe Cue(GameplayEffect effect, params string[] lists)
        {
            var probe = new Probe();
            var cues = new CuesEffectComponent();
            foreach (var list in lists)
            {
                var cuesOf = list switch
                {
                    "Apply" => cues.OnApply,
                    "Reapply" => cues.OnReapply,
                    "Tick" => cues.OnTick,
                    "Remove" => cues.OnRemove,
                    "WhileActive" => cues.WhileActive,
                    "Refused" => cues.OnRefused,
                    "StackChange" => cues.OnStackChange,
                    _ => null,
                };
                Assert.IsNotNull(cuesOf, $"a list named {list}");
                cuesOf.Add(probe);
            }
            effect.Components.Add(cues);
            return probe;
        }

        /// <summary>
        /// A lasting effect's cues play at its moments (EP-53): its lasting cue begins and its burst plays as it lands, a
        /// burst as each tick executes, and its burst and its lasting cue's end as it comes off.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_53_An_effects_cues_play_at_its_moments([Values] Hub hub) => Live(async () =>
        {
            var previous = ForgeTime.Use(new SecondClock());
            try
            {
                var health = Make.Attribute("Health");
                var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
                var burn = Make.Effect("Burn", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 3f, Target = health, Magnitude = -1f, PeriodicTicks = true, Ticks = 3,
                });
                var probe = Cue(burn, "Apply", "Tick", "Remove", "WhileActive");

                Assert.IsTrue(World.Apply(gas, gas, burn), "a burn lands");
                await Frames.Until(() => !gas.TryGetEffectContainer(burn, out _), 8, "and runs its course");

                CollectionAssert.AreEqual(new[] { "Begin", "Applied", "Ticked 1", "Ticked 1", "Ticked 1" }, probe.Heard.Take(5).ToList(),
                    $"it begins and lands, then ticks: {string.Join(", ", probe.Heard)}");
                CollectionAssert.AreEquivalent(new[] { "Removed", "End" }, probe.Heard.Skip(5).ToList(), "and comes off");
                Assert.AreSame(probe.Keys[0], probe.Keys[1], "its lasting cue's beginning and end share a key");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>An instant effect's cues play as it lands and no more: it's over as it lands, so nothing comes off (EP-53).</summary>
        [UnityTest]
        public IEnumerator EP_53_An_instant_effect_plays_its_apply_cues_alone([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var strike = Make.Effect("Strike", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -5f });
            var probe = Cue(strike, "Apply", "Remove", "WhileActive");

            Assert.IsTrue(World.Apply(gas, gas, strike), "a strike lands");
            await Frames.Step();
            CollectionAssert.AreEqual(new[] { "Applied" }, probe.Heard, "its apply cue, and nothing comes off");
        });

        /// <summary>
        /// An application the effect on already takes in plays its reapply cues, and the stack it adds its stack change
        /// cues, told the stacks it has (EP-53).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_53_An_application_taken_in_plays_reapply_and_stack_change([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var rage = Make.Effect("Rage", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = null, Reapply = EEffectReApplicationPolicy.StackExistingContainers,
            });
            var probe = Cue(rage, "Apply", "Reapply", "StackChange");

            Assert.IsTrue(World.Apply(gas, gas, rage), "rage lands");
            Assert.IsTrue(World.Apply(gas, gas, rage), "and again, a stack");
            await Frames.Step();
            CollectionAssert.AreEqual(new[] { "Applied", "Stacked 2", "Reapplied" }, probe.Heard, "it lands, stacks and is reapplied");
        });

        /// <summary>An application that's refused plays the effect's refused cues, and no other (EP-53).</summary>
        [UnityTest]
        public IEnumerator EP_53_A_refused_application_plays_refused_cues([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var hex = Make.Effect("Hex", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            hex.ImpactSpecification.ChanceToApply = new ScalableFloat(0f);
            var probe = Cue(hex, "Apply", "Refused", "WhileActive");

            Assert.IsFalse(World.Apply(gas, gas, hex), "a hex with no chance misses");
            await Frames.Step();
            CollectionAssert.AreEqual(new[] { "Refused" }, probe.Heard, "its refused cue, and nothing else");
        });

        /// <summary>
        /// A lasting cue of an effect another application replaces ends as it began, under its key, as the effect comes
        /// off (EP-53): the replacement swapped the effect's spec, so an end keyed by the spec left the cue playing.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_53_A_replaced_effects_lasting_cue_ends_as_it_began([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var ward = Make.Effect("Ward", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = null, Reapply = EEffectReApplicationPolicy.ReplaceOldContainer,
            });
            var probe = Cue(ward, "WhileActive");

            Assert.IsTrue(World.Apply(gas, gas, ward), "a ward lands");
            Assert.IsTrue(World.Apply(gas, gas, ward), "and another replaces it");
            gas.RemoveGameplayEffect(ward);
            await Frames.Step(2);

            CollectionAssert.AreEqual(new[] { "Begin", "End" }, probe.Heard, "its lasting cue begins once and ends once");
            Assert.AreSame(probe.Keys[0], probe.Keys[1], "under the key it began with");
        });

        /// <summary>
        /// A tick that doesn't execute, as the effect's ongoing condition fails, plays no tick cue (EP-53): its impacts
        /// don't land, and its components heard it as if they had.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_53_A_tick_that_doesnt_execute_plays_no_cue([Values] Hub hub) => Live(async () =>
        {
            var previous = ForgeTime.Use(new SecondClock());
            try
            {
                var health = Make.Attribute("Health");
                var mana = Make.Attribute("Mana");
                var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f), Make.Element(mana, 0f, 100f)));
                var burn = Make.Effect("Mana Burn", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = health, Magnitude = -1f, PeriodicTicks = true, Ticks = 10,
                });
                burn.Conditions.Ongoing.Add(new AttributeCondition
                {
                    On = NumberSide.Target, Attribute = mana, Comparison = EComparisonOperator.GreaterThan, Value = 0f,
                });
                var probe = Cue(burn, "Apply", "Tick");

                Assert.IsTrue(World.Apply(gas, gas, burn), "it lands, its ongoing condition aside");
                await Frames.Step(4);
                CollectionAssert.AreEqual(new[] { "Applied" }, probe.Heard, "and paused, with no mana, it plays no tick cue");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });
    }
}
