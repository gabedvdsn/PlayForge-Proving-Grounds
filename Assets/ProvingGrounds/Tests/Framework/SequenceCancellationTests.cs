using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class SequenceCancellationTests : ProvingGroundsTest
    {
        /// <summary>
        /// A sequence waits on its stage through AttachExternalCancellation, which throws the moment the
        /// token cancels, so CleanStage runs while the stage's tasks are still unwinding. A task that
        /// releases something in Clean and uses it in Execute's finally sees it gone.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_AA_A_cancelled_sequence_cleans_its_tasks_after_they_stop() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Cancelled", frames: 1000);
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Cancelled Sequence").Task(task).Build());
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Step(2);

            relay.TerminateImmediate();
            await Frames.Step(2);

            Assert.That(Ledger.Violations(), Is.Empty);
        });
    }
}
