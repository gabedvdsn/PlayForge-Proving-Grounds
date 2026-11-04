using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>When sequences and abilities end: in the frame their last task does, with no frame lost on the way.</summary>
    public class SequenceTimingTests : ProvingGroundsTest
    {
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

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            return relay;
        }
    }
}
