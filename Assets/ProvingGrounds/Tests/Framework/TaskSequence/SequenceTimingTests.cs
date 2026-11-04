using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// When sequences and abilities run: an ability starts inside the call that activates it, and each ends in the frame
    /// its last task does, with no frame lost on the way.
    /// </summary>
    public class SequenceTimingTests : ProvingGroundsTest
    {
        /// <summary>How an ability is asked for again from inside its own activation.</summary>
        public enum Reentry
        {
            /// <summary>Its own end callback asks for it.</summary>
            End,
            /// <summary>Its own claim-released callback asks for it.</summary>
            ClaimReleased,
            /// <summary>Its end starts another ability, whose end asks for it.</summary>
            ThroughAnother
        }

        /// <summary>An instant sequence, for RunSequenceTask to nest, found through this method.</summary>
        public static TaskSequence InstantInner()
        {
            return new TaskSequence(TaskSequenceBuilder.Create("Inner").Do((SequenceDataPacket _) => { }).Build());
        }

        /// <summary>
        /// A task made from an action yielded a frame before running it, so a sequence of four ran them over four
        /// frames, where Do runs its action at once. Each now runs in the frame its stage starts.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_Z_A_task_made_from_an_action_runs_it_in_the_frame_its_stage_starts() => Live(async () =>
        {
            await Frames.Step();
            var ranAt = new List<int>();
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Actions")
                .Task((SequenceDataPacket _) => ranAt.Add(Time.frameCount))
                .Task((SequenceDataPacket _) => ranAt.Add(Time.frameCount))
                .Task((SequenceDataPacket _) => ranAt.Add(Time.frameCount))
                .Task((SequenceDataPacket _) => ranAt.Add(Time.frameCount))
                .Build());

            int started = Time.frameCount;
            Run(sequence);
            await Frames.Until(() => ranAt.Count == 4, 10, "all four run");

            Assert.That(ranAt, Is.All.EqualTo(started), "each in the frame the sequence started");
        });

        /// <summary>
        /// A run waited a frame at its end for its maintained stages, though it had none, so a sequence of instant
        /// stages ended a frame after it started. It now ends in the frame it starts.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_Z_An_instant_sequence_ends_in_the_frame_it_starts() => Live(async () =>
        {
            await Frames.Step();
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Instant").Do((SequenceDataPacket _) => { }).Build()));

            Assert.That(relay.State, Is.EqualTo(EProcessState.Terminated), "it ended as it started");
        });

        /// <summary>
        /// Waiting on a sequence's relay polled its state once a frame, so an instant ability ended two frames after it
        /// started, and a sequence nested in one, as RunSequenceTask runs it, added two more. Each now ends in the
        /// frame its last task does.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_Z_An_instant_ability_ends_in_the_frame_it_starts([Values] Hub hub, [Values] bool nested) => Live(async () =>
        {
            AbstractAbilityTask task = nested
                ? new RunSequenceTask { Sequence = new TaskSequenceReference(typeof(SequenceTimingTests).GetMethod(nameof(InstantInner))) }
                : new ProbeAbilityTask(World, "Instant");
            var abilities = World.Entity(hub, observed: false).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Instant", Make.Stage(task)), 1, out int index);
            await Frames.Step();

            int endedAt = -1;
            abilities.Callbacks.OnAbilityEnd += _ => endedAt = Time.frameCount;
            int started = Time.frameCount;
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Until(() => endedAt >= 0, 10, "it ends");

            Assert.That(endedAt, Is.EqualTo(started), "it ended in the frame it started");
        });

        /// <summary>
        /// Activation runs the ability before the call returns, as Unreal's does (Decision 25): an instant ability has
        /// ended by then, and one whose task waits has started it.
        /// </summary>
        [UnityTest]
        public IEnumerator An_ability_runs_inside_the_call_that_activates_it([Values] Hub hub, [Values] bool instant) => Live(async () =>
        {
            var ranAt = new List<int>();
            var abilities = World.Entity(hub, observed: false).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Called", Make.Stage(new FrameTask(ranAt, instant ? 0 : 3))), 1, out int index);
            await Frames.Step();

            bool ended = false;
            abilities.Callbacks.OnAbilityEnd += _ => ended = true;
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            Assert.That(ranAt.Count, Is.EqualTo(1), "its task started inside the call");
            Assert.That(ended, Is.EqualTo(instant), instant ? "and it has ended" : "and it's still running");
            await Frames.Until(() => ended, 10, "it ends");
        });

        /// <summary>
        /// An ability asked to activate again from inside its own activation started at once, nested in the call, so a
        /// loop of instant activations nested until the stack overflowed. Each round now starts the next frame.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_8_An_ability_asked_again_inside_its_own_activation_starts_next_frame([Values] Hub hub, [Values] Reentry reentry) => Live(async () =>
        {
            var ranAt = new List<int>();
            var looped = World.Entity(hub, name: "Looped", observed: false).GetAbilitySystem();
            looped.GiveAbility(Make.Ability("Looped", Make.Stage(new FrameTask(ranAt))), 1, out int index);
            var other = World.Entity(hub, name: "Other", observed: false).GetAbilitySystem();
            other.GiveAbility(Make.Ability("Other", Make.Stage(new FrameTask(new List<int>()))), 1, out int otherIndex);
            await Frames.Step();

            int asked = 0;
            void AskAgain()
            {
                if (asked++ < 2) looped.TryActivateAbility(looped.CreateActivationRequest(index));
            }

            switch (reentry)
            {
                case Reentry.End:
                    looped.Callbacks.OnAbilityEnd += _ => AskAgain();
                    break;
                case Reentry.ClaimReleased:
                    looped.Callbacks.OnAbilityClaimReleased += _ => AskAgain();
                    break;
                case Reentry.ThroughAnother:
                    looped.Callbacks.OnAbilityEnd += _ => other.TryActivateAbility(other.CreateActivationRequest(otherIndex));
                    other.Callbacks.OnAbilityEnd += _ => AskAgain();
                    break;
            }

            int started = Time.frameCount;
            looped.TryActivateAbility(looped.CreateActivationRequest(index));
            await Frames.Until(() => ranAt.Count == 3, 10, "three rounds");

            Assert.That(ranAt, Is.EqualTo(new[] { started, started + 1, started + 2 }), "one round a frame");
        });

        /// <summary>
        /// The control for the test above: an ability asked for outside its own activation starts at once, whether it's
        /// another ability that the activation starts, or itself asked again as it ends in a later frame.
        /// </summary>
        [UnityTest]
        public IEnumerator An_ability_asked_for_outside_its_own_activation_starts_at_once([Values] Hub hub, [Values] bool itself) => Live(async () =>
        {
            var firstRan = new List<int>();
            var secondRan = new List<int>();
            var first = World.Entity(hub, name: "First", observed: false).GetAbilitySystem();
            // Asked for itself, its task waits a frame, so that it ends after its activation has returned
            first.GiveAbility(Make.Ability("First", Make.Stage(new FrameTask(firstRan, itself ? 1 : 0))), 1, out int firstIndex);
            var second = World.Entity(hub, name: "Second", observed: false).GetAbilitySystem();
            second.GiveAbility(Make.Ability("Second", Make.Stage(new FrameTask(secondRan))), 1, out int secondIndex);
            await Frames.Step();

            int endedAt = -1;
            first.Callbacks.OnAbilityEnd += _ =>
            {
                if (endedAt >= 0) return;
                endedAt = Time.frameCount;
                if (itself) first.TryActivateAbility(first.CreateActivationRequest(firstIndex));
                else second.TryActivateAbility(second.CreateActivationRequest(secondIndex));
            };

            first.TryActivateAbility(first.CreateActivationRequest(firstIndex));
            await Frames.Until(() => itself ? firstRan.Count == 2 : secondRan.Count == 1, 10, "the second activation runs");

            Assert.That(itself ? firstRan[1] : secondRan[0], Is.EqualTo(endedAt), "in the frame the first ended");
        });

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            return relay;
        }

        /// <summary>An ability task that notes the frame it starts in, then waits its given number of frames.</summary>
        private sealed class FrameTask : AbstractAbilityTask
        {
            private readonly List<int> _ranAt;
            private readonly int _frames;

            public FrameTask(List<int> ranAt, int frames = 0)
            {
                _ranAt = ranAt;
                _frames = frames;
            }

            public override bool IsCriticalSection => false;

            public override async UniTask Activate(AbilityDataPacket data, CancellationToken token)
            {
                _ranAt.Add(Time.frameCount);
                for (int i = 0; i < _frames; i++) await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
    }
}
