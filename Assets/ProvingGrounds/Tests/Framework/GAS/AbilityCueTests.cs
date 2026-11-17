using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// An ability's cues (EP-49): its Cues component plays at its moments, on its caster, as an activation starts, pays its
    /// cost, lasts, ends and is refused; and a Play Cue task plays at its stage's exact moment, on the caster, on the
    /// target or at the point.
    /// </summary>
    public class AbilityCueTests : ProvingGroundsTest
    {
        private readonly List<(string Line, CueContext Cue)> heard = new();

        // A cue that writes down each time it plays, by its own name, how, and what raised it
        private sealed class Noting : AbstractCueHandler
        {
            private readonly List<(string, CueContext)> heard;
            private readonly string name;

            public Noting(List<(string, CueContext)> heard, string name)
            {
                this.heard = heard;
                this.name = name;
            }

            public override void Burst(CueContext cue) => heard.Add(($"{name} burst {cue.Moment}", cue));
            public override void Begin(CueContext cue) => heard.Add(($"{name} begin {cue.Moment}", cue));
            public override void End(CueContext cue) => heard.Add(($"{name} end {cue.Moment}", cue));
        }

        [SetUp]
        public void Clear() => heard.Clear();

        private List<AbstractCueHandler> Noted(string name) => new() { new Noting(heard, name) };

        /// <summary>
        /// An ability's Cues component plays at its moments, on its caster, in order: its activation's burst and its
        /// lasting cue's beginning as it starts, its commit's burst, then its lasting cue's end and its end's burst (EP-49).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_49_An_abilitys_cues_play_at_its_moments([Values] Hub hub) => Live(async () =>
        {
            var ability = Make.Ability("Cued", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.Components.Add(new CuesAbilityComponent
            {
                OnActivate = Noted("activate"), OnCommit = Noted("commit"), WhileActive = Noted("active"), OnEnd = Noted("end"),
            });
            var caster = World.Entity(hub, name: "Caster");
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");
            await Frames.Step(2);

            Assert.That(heard.Select(each => each.Line), Is.EqualTo(new[]
            {
                "activate burst Activated", "active begin Activated", "commit burst Committed", "active end Ended", "end burst Ended",
            }), "each moment, in order");
            Assert.That(heard.Select(each => each.Cue.Target), Has.All.SameAs(caster), "each on its caster");
            Assert.IsNotNull(heard[1].Cue.Key, "its lasting cue has a key");
            Assert.AreSame(heard[1].Cue.Key, heard[3].Cue.Key, "which its beginning and its end share");
        });

        /// <summary>A refused activation plays the Cues component's refusal, on its caster (EP-49).</summary>
        [UnityTest]
        public IEnumerator EP_49_A_refused_activation_plays_its_cues([Values] Hub hub) => Live(async () =>
        {
            var ability = Make.Ability("Ranged", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.Components.Add(new CastRangeAbilityComponent { Range = 5f });
            ability.Components.Add(new CuesAbilityComponent { OnRefused = Noted("refused"), OnActivate = Noted("activate") });
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            target.GetTargetingPacket().position = new Vector3(10f, 0f, 0f);
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request));
            await Frames.Step(3);

            Assert.That(heard.Select(each => each.Line), Is.EqualTo(new[] { "refused burst Refused" }), "out of range, it plays its refusal alone");
            Assert.AreSame(caster, heard[0].Cue.Target, "on its caster");
        });

        /// <summary>
        /// A Play Cue task plays its cues at its stage's moment: on the activation's target, or at its point, with the
        /// activation's data and the magnitude it reads (EP-49).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_49_A_Play_Cue_task_plays_on_the_target_or_at_the_point([Values] Hub hub) => Live(async () =>
        {
            var onTarget = new PlayCueTask { Cues = Noted("on target"), At = EAreaCentre.Target, Magnitude = 7f };
            var atPoint = new PlayCueTask { Cues = Noted("at point"), At = EAreaCentre.Point };
            var ability = Make.Ability("Slash", Make.Stage(onTarget, atPoint, new ProbeAbilityTask(World, "Stage Task")));
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            request.SetPrimary(Tags.POSITION, new Vector3(2f, 0f, 3f));

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request));
            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");

            var hit = heard.Single(each => each.Line == "on target burst Stage").Cue;
            Assert.AreSame(target, hit.Target, "one plays on the target");
            Assert.AreSame(caster, hit.Source, "from its caster");
            Assert.IsNotNull(hit.Data, "with the activation's data");
            Assert.AreEqual(7f, hit.Magnitude, 0.001f, "and its magnitude");
            var placed = heard.Single(each => each.Line == "at point burst Stage").Cue;
            Assert.AreEqual(new Vector3(2f, 0f, 3f), placed.Position, "the other at the activation's point");
        });
    }
}
