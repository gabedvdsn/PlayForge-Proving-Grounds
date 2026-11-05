using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The input buffer (FEAT-6, Decision 30), which the Duel lab needs: the Queue policy holds one slot per ability, which
    /// a new press refreshes. A buffered request waits out its window, in the caster's time, for the ability to be free to
    /// start; the highest buffer priority fires first, and the newest of equals. A request can name a window of its own,
    /// and one a running stage blocks waits too, for the block to lift.
    /// </summary>
    public class InputBufferTests : ProvingGroundsTest
    {
        private const EAbilityActivationPolicyExtended Queue = EAbilityActivationPolicyExtended.QueueActivationIfBusy;

        // A tenth of a second of game time each frame, so a window counts in frames
        private sealed class TenthSecondClock : IForgeClock
        {
            public float DeltaTime => 0.1f;
            public float UnscaledDeltaTime => 0.1f;
            public float FixedDeltaTime => 0.1f;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        // Sets an ability's buffer window or priority, by reflection until the fields are in
        private static void Definition(Ability ability, string field, object value)
        {
            var member = typeof(AbilityDefinition).GetField(field);
            Assert.IsNotNull(member, $"an ability has a {field}");
            member.SetValue(ability.Definition, value);
        }

        // A request under the Queue policy, with a window of its own when one is given
        private static AbilitySystemComponent.AbilityActivationRequest Press(AbilitySystemComponent abilities, AbilitySpecContainer container,
            float window = -1f)
        {
            var request = abilities.CreateActivationRequest(container.Index, null, Queue);
            if (window < 0f) return request;
            var member = typeof(AbilitySystemComponent.AbilityActivationRequest).GetField("BufferWindow");
            Assert.IsNotNull(member, "a request can name its buffer window");
            object boxed = request;
            member.SetValue(boxed, window);
            return (AbilitySystemComponent.AbilityActivationRequest)boxed;
        }

        private static AbilitySpecContainer Give(AbilitySystemComponent abilities, Ability ability)
        {
            Assert.IsTrue(abilities.GiveAbility(ability, 1, out int index), $"the entity knows {ability.GetName()}");
            abilities.TryGetAbilityContainer(index, out var container);
            return container;
        }

        // An ability that holds its claim for the frames given: a committed attack
        private Ability Hold(string name, int frames) =>
            Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name + " Task", frames: frames, critical: true)));

        private Ability Quick(string name, float window, int priority = 0)
        {
            var ability = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name + " Task", frames: 1)));
            Definition(ability, "BufferWindow", window);
            Definition(ability, "BufferPriority", priority);
            return ability;
        }

        // Runs a round on the tenth-second clock, and puts the game's back after it
        private IEnumerator Timed(Hub hub, System.Func<AbilitySystemComponent, Cysharp.Threading.Tasks.UniTask> round) => Live(async () =>
        {
            var previous = ForgeTime.Use(new TenthSecondClock());
            try
            {
                await round(World.Entity(hub).GetAbilitySystem());
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });

        /// <summary>
        /// A press while another ability holds the claim waited for good, however long ago it came (FEAT-6). It waits out
        /// its window, here 0.3 s, and is let go after that: once the claim goes, nothing fires.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_6_A_buffered_request_expires_after_its_window([Values] Hub hub) => Timed(hub, async abilities =>
        {
            var hold = Give(abilities, Hold("Hold", frames: 10));
            var jab = Give(abilities, Quick("Jab", window: 0.3f));
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, hold)), "the hold starts and claims");
            await Frames.Step();
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, jab)), "a jab pressed meanwhile is buffered");

            await Frames.Until(() => Ledger.Cleans("Hold Task") == 1, 15, "the hold ends, a second later");
            await Frames.Step(2);
            Assert.AreEqual(0, Ledger.Prepares("Jab Task"), "and the jab, whose 0.3 s ran out, never fires");
        });

        /// <summary>
        /// A press that comes in time fires as the claim goes. Pressed twice, it queued twice and fired twice; one slot per
        /// ability, which the second press refreshes, fires once (FEAT-6).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_6_A_new_press_refreshes_its_slot([Values] Hub hub) => Timed(hub, async abilities =>
        {
            var hold = Give(abilities, Hold("Hold", frames: 5));
            var jab = Give(abilities, Quick("Jab", window: 0.3f));
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, hold)), "the hold starts and claims");
            await Frames.Step();
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, jab)), "a jab is pressed");
            await Frames.Step(2);
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, jab)), "and pressed again, 0.2 s later");

            await Frames.Until(() => Ledger.Cleans("Jab Task") == 1, 12, "the jab fires once the hold lets go");
            await Frames.Step(3);
            Assert.AreEqual(1, Ledger.Prepares("Jab Task"), "once, as its second press refreshed its one slot");
        });

        /// <summary>
        /// Buffered requests fired in the order pressed (FEAT-6). The highest buffer priority fires first, and of equals, the
        /// newest: a dodge pressed before a parry, both above a jab, fires after it.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_6_The_highest_priority_fires_first_and_the_newest_of_equals([Values] Hub hub) => Timed(hub, async abilities =>
        {
            var hold = Give(abilities, Hold("Hold", frames: 5));
            var jab = Give(abilities, Quick("Jab", window: 1f, priority: 0));
            var dodge = Give(abilities, Quick("Dodge", window: 1f, priority: 2));
            var parry = Give(abilities, Quick("Parry", window: 1f, priority: 2));
            var fired = new List<string>();
            abilities.Callbacks.OnAbilityActivate += status => fired.Add(status.Ability.Base.GetName());

            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, hold)), "the hold starts and claims");
            await Frames.Step();
            fired.Clear();
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, jab)), "a jab is buffered");
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, dodge)), "then a dodge");
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, parry)), "then a parry");

            await Frames.Until(() => fired.Count > 0, 12, "something fires as the hold lets go");
            Assert.AreEqual("Parry", fired[0], "the parry first: the dodge's equal, and pressed after it");
        });

        /// <summary>A request can name a window of its own, longer than its ability's (FEAT-6, Decision 30).</summary>
        [UnityTest]
        public IEnumerator FEAT_6_A_request_names_its_own_window([Values] Hub hub) => Timed(hub, async abilities =>
        {
            var hold = Give(abilities, Hold("Hold", frames: 8));
            var jab = Give(abilities, Quick("Jab", window: 0.2f));
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, hold)), "the hold starts and claims");
            await Frames.Step();
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, jab, window: 2f)), "a jab is pressed with a window of 2 s");

            await Frames.Until(() => Ledger.Cleans("Jab Task") == 1, 15, "and fires once the hold lets go, past its own 0.2 s");
        });

        /// <summary>
        /// A request a running stage blocked was refused, whatever its policy (FEAT-5). Under the Queue policy it waits in
        /// the buffer for the block to lift, as a press during a recovery fires once the recovery is over (FEAT-6).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_6_A_blocked_request_waits_for_the_block_to_lift([Values] Hub hub) => Timed(hub, async abilities =>
        {
            var attack = Make.NewTag("Test.Kind.Attack");
            var recovery = Make.Stage(new ProbeAbilityTask(World, "Recovering", frames: 4));
            recovery.Blocks = new List<Tag> { attack };
            var heavy = Give(abilities, Make.Ability("Heavy", recovery));
            var jabAbility = Quick("Jab", window: 1f);
            jabAbility.Tags.ContextTags.Add(attack);
            var jab = Give(abilities, jabAbility);

            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, heavy)), "the heavy attack recovers");
            await Frames.Step();
            Assert.IsTrue(abilities.TryActivateAbility(Press(abilities, jab)), "a jab pressed in the recovery is buffered, not refused");
            Assert.AreEqual(0, Ledger.Prepares("Jab Task"), "and waits");

            await Frames.Until(() => Ledger.Cleans("Jab Task") == 1, 12, "it fires once the recovery's block lifts");
            Assert.AreEqual(1, Ledger.Cleans("Recovering"), "after the recovery");
        });
    }
}
