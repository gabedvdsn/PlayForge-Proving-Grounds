using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>The path a packet carries: the runs and stages it's in, a breadcrumb for when one fails.</summary>
    public class SequencePathTests : ProvingGroundsTest
    {
        /// <summary>
        /// Each part a run or a stage added to its packet's path stayed there, so a sequence that repeated added its
        /// stages' parts again every time round, and its path grew for as long as it ran. Each part now comes off as
        /// what added it ends, so every repeat finds the path as the first did.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_5_A_repeating_sequence_keeps_its_path_the_same() => Live(async () =>
        {
            var paths = new List<string>();
            Run(new TaskSequence(TaskSequenceBuilder.Create("Looping")
                .WithRepeat(true)
                .Stage(stage => stage.WithName("Pulse").Do(data =>
                {
                    paths.Add(data.Path);
                    if (paths.Count == 20) data.Interrupt();
                }))
                .Build()));

            await Frames.Until(() => paths.Count >= 20, 100, "twenty runs");

            Assert.That(paths[0], Does.Contain("Stage[Pulse]"), "the path names the stage");
            Assert.That(paths[19], Is.EqualTo(paths[0]), "the twentieth run's path is the first's");
        });

        /// <summary>
        /// The control for the parts coming off: a run that fails still logs the path to where it failed, its stage
        /// included, though the stage has ended by the time the run logs it.
        /// </summary>
        [UnityTest]
        public IEnumerator A_failed_run_logs_the_path_where_it_failed() => Live(async () =>
        {
            LogAssert.Expect(LogType.Error, new Regex(@"Seq\[Failing\]>Stage\[Breaks\]\) failed"));
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Failing")
                .Stage(stage => stage.WithName("Breaks").Do(_ => throw new InvalidOperationException("Planned")))
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 10, "the run ends");
        });

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            return relay;
        }
    }
}
