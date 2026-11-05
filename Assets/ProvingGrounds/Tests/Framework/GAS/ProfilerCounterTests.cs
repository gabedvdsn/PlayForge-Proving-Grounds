using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// PlayForge's counters in Unity's Profiler (FEAT-18, Decision 32): what ProcessControl, the GAS and TaskSequence do
    /// each frame, and what they hold, each a counter in the PlayForge category, which a Profiler module charts. The
    /// Profiler could time PlayForge's work by its markers, but nothing said how much of it there was.
    /// </summary>
    public class ProfilerCounterTests : ProvingGroundsTest
    {
        private static readonly ProfilerCategory PlayForge = new("PlayForge");

        // A frame's counts, which start from 0 each frame
        private static readonly string[] PerFrame =
        {
            "Registrations", "Terminations", "Steps", "State Changes", "Step Time", "Effects Applied", "Effects Removed",
            "Impacts", "Activations", "Refused Activations", "Tag Compiles", "Recomputes", "Stages Started",
        };

        // What's held, which holds until it changes
        private static readonly string[] Levels =
        {
            "Processes Running", "Processes Waiting", "Processes Created", "Mono Entities", "Runtime Entities",
            "Effects Active", "Sequences Running",
        };

        // A frame's count added up over the frames the recorder kept
        private static long Sum(ProfilerRecorder recorder)
        {
            long sum = 0;
            for (int i = 0; i < recorder.Count; i++) sum += recorder.GetSample(i).Value;
            return sum;
        }

        // The most a level reached in those frames
        private static long Most(ProfilerRecorder recorder)
        {
            long most = 0;
            for (int i = 0; i < recorder.Count; i++) most = Math.Max(most, recorder.GetSample(i).Value);
            return most;
        }

        /// <summary>
        /// An entity takes a shield and a hit, activates an ability, has one refused and loses its shield, and each counter
        /// counts its part: two effects applied, one removed, one activation and one refusal, with the impacts, tag
        /// compiles, recomputes, stages, registrations, steps, state changes and the step loop's time, and the levels the
        /// shield, the ability's sequence, the scene's processes and the entity by its hub reach.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_18_PlayForges_counters_count_its_work([Values] Hub hub) => Live(async () =>
        {
            var recorders = PerFrame.Concat(Levels).ToDictionary(name => name, name => ProfilerRecorder.StartNew(PlayForge, name, 256));
            try
            {
                var health = Make.Attribute("Health");
                var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
                var shield = Make.Effect("Shield", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = health, Magnitude = 5f });
                var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
                var focus = Make.Ability("Focus", Make.Stage(new ProbeAbilityTask(World, "Focusing", frames: 3)));
                var abilities = gas.GetAbilitySystem();
                Assert.IsTrue(abilities.GiveAbility(focus, 1, out int index), "the entity knows Focus");
                await Frames.Step();

                Assert.IsTrue(World.Apply(gas, gas, shield), "a shield lands");
                Assert.IsTrue(World.Apply(gas, gas, hit), "and a hit");
                Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "an ability activates");
                Assert.IsFalse(abilities.TryActivateAbility(abilities.CreateActivationRequest(index + 99)), "and one it doesn't know is refused");
                await Frames.Step(2);
                gas.RemoveGameplayEffect(shield);
                await Frames.Step(6);

                long Total(string name) => Sum(recorders[name]);
                long Peak(string name) => Most(recorders[name]);
                Assert.AreEqual(2, Total("Effects Applied"), "the two effects that landed");
                Assert.AreEqual(1, Total("Effects Removed"), "the one taken off");
                Assert.AreEqual(1, Total("Activations"), "the ability that activated");
                Assert.AreEqual(1, Total("Refused Activations"), "and the one refused");
                Assert.That(Total("Impacts"), Is.GreaterThanOrEqualTo(1), "the hit's impact");
                Assert.That(Total("Tag Compiles"), Is.GreaterThanOrEqualTo(1), "a tag compile");
                Assert.That(Total("Recomputes"), Is.GreaterThanOrEqualTo(1), "a recompute");
                Assert.That(Total("Stages Started"), Is.GreaterThanOrEqualTo(1), "the ability's stage");
                Assert.That(Total("Registrations"), Is.GreaterThanOrEqualTo(2), "the entity and the ability's sequence");
                Assert.That(Total("Terminations"), Is.GreaterThanOrEqualTo(1), "the sequence as it ended");
                Assert.That(Total("Steps"), Is.GreaterThan(0), "the steps");
                Assert.That(Total("State Changes"), Is.GreaterThan(0), "the state changes");
                Assert.That(Total("Step Time"), Is.GreaterThan(0), "and the step loop's time");
                Assert.That(Peak("Effects Active"), Is.GreaterThanOrEqualTo(1), "the shield while it held");
                Assert.That(Peak("Sequences Running"), Is.GreaterThanOrEqualTo(1), "the ability's sequence while it ran");
                Assert.That(Peak("Processes Running"), Is.GreaterThanOrEqualTo(1), "the scene's processes");
                Assert.That(Peak("Processes Created"), Is.GreaterThanOrEqualTo(2), "and those it has taken");
                Assert.That(Peak(hub == Hub.Mono ? "Mono Entities" : "Runtime Entities"), Is.GreaterThanOrEqualTo(1), "the entity, by its hub");
            }
            finally
            {
                foreach (var recorder in recorders.Values) recorder.Dispose();
            }
        });
    }
}
