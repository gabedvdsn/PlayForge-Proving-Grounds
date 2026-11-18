using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A Sequence cue (EP-64): it runs a task sequence on a packet loaded for the cue, a burst's for its course and a
    /// lasting cue's until the cue ends, in the world of the one it plays on, by the clock it picks, else the sequence's
    /// own, else its owner's (Decision 91).
    /// </summary>
    public class SequenceCueTests : ProvingGroundsTest
    {
        private (IGameplayAbilitySystem Target, GameplayEffect Hit) Struck(Hub hub, params AbstractCueHandler[] cues)
        {
            var health = Make.Attribute("Health");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), name: "Target");
            var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -5f });
            var component = new CuesEffectComponent();
            component.OnApply.AddRange(cues);
            hit.Components.Add(component);
            return (target, hit);
        }

        // Holds its run until it's ended, saying as it starts and as it ends
        private static async UniTask Hold(CancellationToken token, Action began, Action ended)
        {
            began();
            using (token.Register(ended)) await UniTask.WaitUntilCanceled(token);
        }

        /// <summary>
        /// A Sequence cue in a burst list runs its sequence on a packet loaded for the cue, with the cue's own Data
        /// (EP-64).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_64_A_Sequence_cue_runs_its_sequence_on_the_cues_data([Values] Hub hub) => Live(async () =>
        {
            var word = Make.NewTag("Test.Data.Word");
            ProcessDataPacket seen = null;
            var cue = new SequenceCue { Definition = TaskSequenceBuilder.Create("EP-64 Flash").Do(data => seen = data).Build() };
            cue.Data.Add(new DataWrapper { Key = word, Value = new LocalString { Value = "flash" } });
            var (target, hit) = Struck(hub, cue);

            Assert.IsTrue(World.Apply(target, target, hit), "the hit lands");
            await Frames.Until(() => seen != null, 8, "its sequence runs");
            Assert.AreSame(hit, seen.Get<GameplayEffect>(Tags.ASSET, EDataTarget.Primary), "on the cue's packet");
            Assert.AreSame(target, seen.Get<ITarget>(Tags.TARGET_REAL, EDataTarget.Primary), "for the one it's on");
            Assert.AreEqual("flash", seen.Get<string>(word, EDataTarget.Primary), "with the cue's own Data");
        });

        /// <summary>
        /// A Sequence cue in a list of lasting cues runs its sequence while its cue lasts, and ends it as the cue ends
        /// (EP-64).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_64_A_lasting_Sequence_cue_runs_until_its_cue_ends([Values] Hub hub) => Live(async () =>
        {
            bool began = false, ended = false;
            var cue = new SequenceCue
            {
                Definition = TaskSequenceBuilder.Create("EP-64 Flicker")
                    .Stage(stage => stage.Task((_, token) => Hold(token, () => began = true, () => ended = true))).Build(),
            };
            var target = World.Entity(hub, name: "Target");
            var shield = Make.Effect("Shield", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            shield.Components.Add(new CuesEffectComponent { WhileActive = { cue } });

            Assert.IsTrue(World.Apply(target, target, shield), "the shield goes up");
            await Frames.Until(() => began, 8, "its sequence runs while the shield lasts");
            await Frames.Step(2);
            Assert.IsFalse(ended, "and goes on");

            target.RemoveGameplayEffect(shield);
            await Frames.Until(() => ended, 8, "it ends as its cue does");
        });

        /// <summary>
        /// A Sequence cue's run goes by the clock it picks, else by the sequence's own, else by its owner's, the one the
        /// cue plays on (EP-64).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_64_A_Sequence_cue_runs_by_its_pick_else_the_sequences_own_else_its_owners([Values] Hub hub) => Live(async () =>
        {
            var seen = new Dictionary<string, IForgeClock>();
            SequenceCue Noting(string name, AbstractClockChoice pick, IForgeClock own)
            {
                var builder = TaskSequenceBuilder.Create(name);
                if (own != null) builder = builder.WithClock(own);
                return new SequenceCue { Clock = pick, Definition = builder.Do(data => seen[name] = data.Runtime?.Clock).Build() };
            }
            var (target, hit) = Struck(hub,
                Noting("Owner's", null, null), Noting("Own", null, UnscaledClock.Instance), Noting("Picked", new GameClockChoice(), UnscaledClock.Instance));

            Assert.IsTrue(World.Apply(target, target, hit), "the hit lands");
            await Frames.Until(() => seen.Count == 3, 8, "each sequence runs");
            Assert.AreSame(target.GetClock(), seen["Owner's"], "with no pick and no clock of its own, its owner's");
            Assert.AreSame(UnscaledClock.Instance, seen["Own"], "the sequence's own goes ahead of its owner's");
            Assert.AreSame(GameClock.Instance, seen["Picked"], "and the cue's pick goes ahead of both");
        });
    }
}
