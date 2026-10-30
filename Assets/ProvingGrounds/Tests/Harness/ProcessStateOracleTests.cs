using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ProcessStateOracleTests : ProvingGroundsTest
    {
        public static IEnumerable<TransitionCell> Cells => ProcessStateOracle.Cells;

        /// <summary>
        /// The driver gets through every cell and the world still tears down clean, whatever the control
        /// state did to GameRoot. Whether each cell agrees with the table is PC-7's test, not this one's.
        /// </summary>
        [UnityTest]
        public IEnumerator Every_cell_can_be_driven([ValueSource(nameof(Cells))] TransitionCell cell) => Live(async () =>
        {
            var observed = await ProcessStateOracle.Observe(World, cell);

            if (cell.From == EProcessState.Terminated) Assert.That(observed.Reachable, Is.False, observed.ToString());
        });

        [Test]
        public void The_table_has_96_cells() => Assert.That(ProcessStateOracle.Cells.Count(), Is.EqualTo(96));

        [Test]
        public void The_table_matches_the_framework() => Assert.That(ProcessStateOracle.DiffAgainstFramework(), Is.Empty);

        [Test]
        public void The_table_never_sends_a_process_back_to_Created() => Assert.That(
            ProcessStateOracle.Cells.Select(cell => cell.Expected).Distinct(),
            Is.EquivalentTo(new[] { EProcessState.Running, EProcessState.Waiting, EProcessState.Terminated }));

        [UnityTest]
        public IEnumerator A_cell_that_holds_is_observed_holding() => Live(async () =>
        {
            var cell = Cell(EProcessControlState.Ready, EProcessLifecycle.SelfTerminating, EProcessState.Running);

            var observed = await ProcessStateOracle.Observe(World, cell);

            Assert.That(observed.Reachable, observed.ToString());
            Assert.That(observed.Reached, Is.EqualTo(cell.Expected), observed.ToString());
        });

        [UnityTest]
        public IEnumerator A_registration_is_observed() => Live(async () =>
        {
            var cell = Cell(EProcessControlState.Ready, EProcessLifecycle.RequiresControl, EProcessState.Created);

            var observed = await ProcessStateOracle.Observe(World, cell);

            Assert.That(observed.Reached, Is.EqualTo(EProcessState.Waiting), observed.ToString());
        });

        [UnityTest]
        public IEnumerator A_closed_control_refuses_the_cell_outright() => Live(async () =>
        {
            var cell = Cell(EProcessControlState.Closed, EProcessLifecycle.SelfTerminating, EProcessState.Created);

            var observed = await ProcessStateOracle.Observe(World, cell);

            Assert.That(observed.Reachable, Is.False, observed.ToString());

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator A_terminated_block_cannot_be_placed() => Live(async () =>
        {
            var cell = Cell(EProcessControlState.Ready, EProcessLifecycle.SelfTerminating, EProcessState.Terminated);

            var observed = await ProcessStateOracle.Observe(World, cell);

            Assert.That(observed.Reachable, Is.False, observed.ToString());
        });

        [UnityTest]
        public IEnumerator A_process_that_breaks_its_cell_shows_as_a_mismatch() => Live(async () =>
        {
            // A pause mid-run sends a self-terminating process to Waiting instead of the table's Terminated
            var cell = Cell(EProcessControlState.Ready, EProcessLifecycle.SelfTerminating, EProcessState.Running);
            var probe = World.Probe("Rerouted");
            probe.Relay.Pause();
            probe.Recorder.Released = true;

            await Frames.Step(2);

            Assert.That(probe.Relay.State, Is.EqualTo(EProcessState.Waiting));
            Assert.That(probe.Relay.State, Is.Not.EqualTo(cell.Expected));
        });

        private static TransitionCell Cell(EProcessControlState control, EProcessLifecycle lifecycle, EProcessState from)
        {
            return new TransitionCell(control, lifecycle, from, ProcessStateOracle.Expected(control, lifecycle, from));
        }
    }
}
