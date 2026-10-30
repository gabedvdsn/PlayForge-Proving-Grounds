using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class SequenceRunTests : ProvingGroundsTest
    {
        /// <summary>
        /// A TaskSequence holds one Runtime, and each run overwrites it, so running one twice at once leaves
        /// the first run unreachable: interrupting it stops the other one instead.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_I_An_interrupt_reaches_the_run_it_was_sent_to() => Live(async () =>
        {
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Shared Sequence").Delay(100f).Build());

            ProcessControl.Register(sequence, out var first);
            World.Own(first);
            await Frames.Step();

            ProcessControl.Register(sequence, out var second);
            World.Own(second);
            await Frames.Step();

            first.TryGetProcess<TaskSequenceProcess>(out var firstProcess);
            Assert.That(firstProcess.Interrupt(), "the interrupt is accepted");
            await Frames.Step(3);

            Assert.That(second.IsRunning, "the other run goes on");
            Assert.That(first.IsRunning, Is.False, "the interrupted run stops");
        });

        /// <summary>
        /// A run's stage timeout goes into the shared definition, so every later run of it inherits a timeout
        /// that one run asked for.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_I_A_run_leaves_the_definition_as_it_found_it() => Live(async () =>
        {
            var definition = TaskSequenceBuilder.Create("Shared Definition").Delay(100f).Build();
            var sequence = new TaskSequence(definition);

            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Step(2);

            sequence.Runtime.RequestStartStageTimeout(new SequenceTimeout { Seconds = 100f });

            Assert.That(definition.Stages[0].Timeouts, Is.Empty, "a run's timeout stays with the run");
        });

        /// <summary>
        /// A sequence waits on its stage through AttachExternalCancellation, which throws the moment the
        /// token cancels, so CleanStage runs while the stage's tasks are still unwinding. A task that releases
        /// something in Clean and uses it in Execute's finally sees it gone.
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
