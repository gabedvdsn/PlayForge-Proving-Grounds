using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// PlayForge in Unity's Profiler (FEAT-18): Runtime had no ProfilerMarker, so the cost of a process's step, an
    /// effect's application or an ability's activation showed only as its caller's. Each now has a marker of its own.
    /// </summary>
    public class ProfilerMarkerTests : ProvingGroundsTest
    {
        private static readonly string[] Markers =
        {
            "PlayForge.ProcessStep", "PlayForge.GASStep", "PlayForge.EndOfFrame", "PlayForge.ApplyEffect", "PlayForge.ActivateAbility",
        };

        // How many times the marker was sampled over the frames the recorder kept
        private static long Calls(ProfilerRecorder recorder)
        {
            long calls = 0;
            for (int i = 0; i < recorder.Count; i++) calls += recorder.GetSample(i).Count;
            return calls;
        }

        /// <summary>A process's step, a GAS's step and its end of frame, an effect's application and an ability's activation each show under their own marker.</summary>
        [UnityTest]
        public IEnumerator FEAT_18_PlayForge_shows_in_the_Profiler([Values] Hub hub) => Live(async () =>
        {
            var recorders = Markers.Select(name => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, 64)).ToArray();
            try
            {
                var might = Make.Attribute("Might");
                var gas = World.Entity(hub, Make.Set(Make.Element(might, 0f, 0f)));
                var buff = Make.Effect("Buff", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = might, Magnitude = 3f });
                var focus = Make.Ability("Focus", Make.Stage(new ProbeAbilityTask(World, "Focusing", frames: 2)));
                Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(focus, 1, out int index), "the entity knows Focus");

                Assert.IsTrue(World.Apply(gas, gas, buff), "an effect lands");
                Assert.IsTrue(gas.GetAbilitySystem().TryActivateAbility(gas.GetAbilitySystem().CreateActivationRequest(index)), "an ability activates");
                await Frames.Step(4);

                for (int i = 0; i < Markers.Length; i++)
                    Assert.That(Calls(recorders[i]), Is.GreaterThan(0), $"{Markers[i]} shows in the Profiler");
            }
            finally
            {
                foreach (var recorder in recorders) recorder.Dispose();
            }
        });
    }
}
