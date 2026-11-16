using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FarEmerald.PlayForge.Testing;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.Samples.ProcessGroups.Tests
{
    /// <summary>
    /// The Process Group Examples sample does what its README says each key does (EP-30): a buff's group ends its oldest to
    /// make room, a summon's refuses one more, the hazards share one total and end their group with the last, PauseAll
    /// holds every member, a detached summon goes on alone, TerminateAllMembers leaves its group, ending a group ends its
    /// members, and a squad takes new places as one leaves. Its keys are pressed through ForgeInput, a frame a press.
    /// </summary>
    public class ProcessGroupExamplesTests : ForgeTest
    {
        // The groups outlive a test on purpose, some of them, and the world's end ends them
        protected override bool Audits => false;

        /// <summary>Keys pressed for a frame, through ForgeInput.</summary>
        private sealed class Keys : IForgeInput, IDisposable
        {
            private readonly IForgeInput previous;
            public readonly HashSet<KeyCode> Pressed = new();
            public Keys() => previous = ForgeInput.Use(this);
            public void Dispose() => ForgeInput.Use(previous);
            public bool PointerPressed(int button) => false;

            public bool TryGetPointerRay(out Ray ray)
            {
                ray = default;
                return false;
            }

            public bool KeyPressed(KeyCode key) => Pressed.Contains(key);
        }

        private Keys keys;
        private ProcessGroupExamples examples;
        private readonly List<GameObject> made = new();

        [SetUp]
        public void PutTheExamplesIn()
        {
            keys = new Keys();
            var host = new GameObject("Process Group Examples");
            made.Add(host);
            examples = host.AddComponent<ProcessGroupExamples>();
            examples.BuffDuration = 30f;
            examples.HazardInterval = 0.1f;
            examples.HazardPulses = 2;
        }

        [TearDown]
        public void TakeThemOut()
        {
            keys.Dispose();
            foreach (var each in made) if (each) Object.Destroy(each);
            made.Clear();
        }

        private async UniTask Press(KeyCode key)
        {
            keys.Pressed.Add(key);
            await Frames.Step();
            keys.Pressed.Clear();
            await Frames.Step();
        }

        private static GroupProcess Group(Tag tag)
        {
            Assert.IsTrue(ProcessControl.TryGetGroup(tag, out var group), $"{tag} has a group");
            return group;
        }

        private static List<string> Names(GroupProcess group)
        {
            var names = new List<string>();
            group.ForEach(relay => names.Add(relay.TryGetProcess<Summon>(out var summon) ? summon.Called : relay.Wrapper.ProcessName));
            return names;
        }

        [UnityTest]
        public IEnumerator EP_30_A_full_buff_group_ends_its_oldest_buff() => Live(async () =>
        {
            for (int i = 0; i < 4; i++) await Press(examples.AddBuffKey);
            var buffs = Group(ProcessGroupExamples.BUFFS);
            Assert.AreEqual(3, buffs.MemberCount, "three at most");
            Assert.That(Names(buffs), Is.EqualTo(new[] { "Buff 2", "Buff 3", "Buff 4" }), "the oldest made room for the fourth");
        });

        [UnityTest]
        public IEnumerator EP_30_A_full_summon_group_refuses_another() => Live(async () =>
        {
            for (int i = 0; i < 3; i++) await Press(examples.SummonKey);
            var summons = Group(ProcessGroupExamples.SUMMONS);
            Assert.That(Names(summons), Is.EqualTo(new[] { "Summon 1", "Summon 2" }), "the third was refused, the first two kept");
        });

        [UnityTest]
        public IEnumerator EP_30_Hazards_share_one_total_and_end_their_group_with_the_last() => Live(async () =>
        {
            await Press(examples.HazardKey);
            await Press(examples.HazardKey);
            var hazards = Group(ProcessGroupExamples.HAZARDS);
            Assert.AreEqual(2, hazards.MemberCount, "the first hazard made the group, the second joined it");
            var shared = hazards.Data;
            await Frames.Until(() => !ProcessControl.HasGroup(ProcessGroupExamples.HAZARDS), 90, "the group ends with its last hazard");
            Assert.AreEqual(2 * 2 * examples.HazardDamage, shared.GetPrimary(HazardPulse.TOTAL, 0f), 0.001f,
                "each hazard's two pulses went to the one total");
        });

        [UnityTest]
        public IEnumerator EP_30_PauseAll_holds_every_member_and_UnpauseAll_lets_them_go_on() => Live(async () =>
        {
            await Press(examples.AddBuffKey);
            Assert.IsTrue(Group(ProcessGroupExamples.BUFFS).TryGetFirst<TimedBuff>(out var buff, out var relay), "a buff");
            await Press(examples.PauseKey);
            Assert.IsTrue(relay.IsWaiting, "paused");
            float left = buff.Remaining;
            await Frames.Step(10);
            Assert.AreEqual(left, buff.Remaining, "its countdown holds");
            await Press(examples.PauseKey);
            Assert.IsTrue(relay.IsRunning, "unpaused");
            await Frames.Step(5);
            Assert.Less(buff.Remaining, left, "and it counts down again");
        });

        [UnityTest]
        public IEnumerator EP_30_A_detached_summon_goes_on_alone_and_outlives_its_group() => Live(async () =>
        {
            await Press(examples.SummonKey);
            await Press(examples.SummonKey);
            var summons = Group(ProcessGroupExamples.SUMMONS);
            var oldest = summons.GetOldest();
            await Press(examples.DetachKey);
            Assert.IsFalse(summons.HasMember(oldest), "it left the group");
            Assert.AreEqual(1, summons.MemberCount, "which keeps the other");
            await Press(examples.DismissKey);
            Assert.IsFalse(ProcessControl.HasGroup(ProcessGroupExamples.SUMMONS), "the group ends");
            Assert.IsTrue(oldest.ProcessActive, "and the detached summon goes on");
            oldest.Terminate();
        });

        [UnityTest]
        public IEnumerator EP_30_Clearing_the_buffs_ends_them_and_keeps_their_group() => Live(async () =>
        {
            await Press(examples.AddBuffKey);
            await Press(examples.AddBuffKey);
            await Press(examples.ClearBuffsKey);
            await Frames.Step(2);
            var buffs = Group(ProcessGroupExamples.BUFFS);
            Assert.IsTrue(buffs.IsEmpty, "every buff ended");
            await Press(examples.AddBuffKey);
            Assert.AreEqual(1, buffs.MemberCount, "and the group takes the next");
        });

        [UnityTest]
        public IEnumerator EP_30_A_squad_takes_new_places_as_one_leaves() => Live(async () =>
        {
            examples.SquadLeader = Made("Leader").transform;
            var left = Made("Left").transform;
            var right = Made("Right").transform;
            examples.SquadUnits = new List<Transform> { left, right };
            await Press(examples.SquadKey);
            var squad = Group(ProcessGroupExamples.SQUAD);
            Assert.AreEqual(2, squad.MemberCount, "both units joined");
            Assert.That(Offsets(squad), Is.EqualTo(new[] { -0.75f, 0.75f }).Within(0.001f), "a line centred on the leader");

            Object.Destroy(left.gameObject);
            await Frames.Until(() => squad.MemberCount == 1, 10, "the unit's end takes it out");
            Assert.That(Offsets(squad), Is.EqualTo(new[] { 0f }).Within(0.001f), "and the line closes up");

            await Press(examples.SquadKey);
            Assert.IsFalse(ProcessControl.HasGroup(ProcessGroupExamples.SQUAD), "pressed again, the squad's disbanded");
        });

        [UnityTest]
        public IEnumerator EP_30_Listing_says_each_group_s_members() => Live(async () =>
        {
            var heard = new List<string>();
            void Hear(ForgeLogEntry entry) => heard.Add(entry.Message);
            ForgeLog.Logged += Hear;
            try
            {
                await Press(examples.AddBuffKey);
                await Press(examples.SummonKey);
                await Press(examples.ListKey);
            }
            finally
            {
                ForgeLog.Logged -= Hear;
            }
            var list = heard.LastOrDefault(message => message.Contains("member(s)"));
            Assert.IsNotNull(list, "the list is said");
            StringAssert.Contains("Buff 1", list, "with the buff");
            StringAssert.Contains("Summon 1", list, "and the summon");
        });

        private GameObject Made(string name)
        {
            var each = new GameObject(name);
            made.Add(each);
            return each;
        }

        private static List<float> Offsets(GroupProcess squad)
        {
            var offsets = new List<float>();
            squad.ForEach<SquadMember>((member, _) => offsets.Add(member.Offset.x));
            return offsets;
        }
    }
}
