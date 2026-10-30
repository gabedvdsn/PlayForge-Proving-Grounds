using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Every cell of ProcessControl's transition table against a live process (see
    /// <see cref="ProcessStateOracle.Observe"/>). The cells a process used to break are split out by the bug
    /// that broke them, so each keeps its name. A cell nothing can reach has nothing to compare.
    /// </summary>
    public class TransitionTableTests : ProvingGroundsTest
    {
        public static IEnumerable<TransitionCell> Pc07Cells => ProcessStateOracle.Cells.Where(IsPc07);
        public static IEnumerable<TransitionCell> Pc27Cells => ProcessStateOracle.Cells.Where(IsPc27);
        public static IEnumerable<TransitionCell> OtherCells => ProcessStateOracle.Cells.Where(cell => !IsPc07(cell) && !IsPc27(cell));

        // Closing ProcessControl force-queued the table's own transition, which for GameRoot, a synchronous
        // process, was Terminated, and that ended everything it handled
        private static bool IsPc07(TransitionCell cell)
        {
            if (cell.Control != EProcessControlState.Closed) return false;

            return (cell.Lifecycle, cell.From) switch
            {
                (EProcessLifecycle.SelfTerminating, EProcessState.Waiting) => true,
                (EProcessLifecycle.RunThenWait, EProcessState.Running) => true,
                (EProcessLifecycle.RunThenWait, EProcessState.Waiting) => true,
                (EProcessLifecycle.RequiresControl, EProcessState.Running) => true,
                (EProcessLifecycle.RequiresControl, EProcessState.Waiting) => true,
                (EProcessLifecycle.Synchronous, EProcessState.Waiting) => true,
                _ => false
            };
        }

        // Unpause never read the control state, so a paused ProcessControl didn't hold what it paused
        private static bool IsPc27(TransitionCell cell)
        {
            return cell.From == EProcessState.Waiting && cell.Control is EProcessControlState.Waiting or EProcessControlState.ClosedWaiting;
        }

        [UnityTest]
        public IEnumerator Every_other_cell_keeps_to_the_table([ValueSource(nameof(OtherCells))] TransitionCell cell) => Check(cell);

        [UnityTest]
        public IEnumerator PC_07_A_closing_ProcessControl_keeps_to_the_table([ValueSource(nameof(Pc07Cells))] TransitionCell cell) => Check(cell);

        [UnityTest]
        public IEnumerator PC_27_Unpause_waits_for_a_paused_ProcessControl([ValueSource(nameof(Pc27Cells))] TransitionCell cell) => Check(cell);

        private IEnumerator Check(TransitionCell cell) => Live(async () =>
        {
            var observed = await ProcessStateOracle.Observe(World, cell);

            if (observed.Reachable) Assert.That(observed.Reached, Is.EqualTo(cell.Expected), observed.ToString());
        });
    }
}
