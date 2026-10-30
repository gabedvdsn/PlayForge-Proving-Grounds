using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>One entry of the transition table: what a process in this state should move to, under this control state.</summary>
    public readonly struct TransitionCell
    {
        public readonly EProcessControlState Control;
        public readonly EProcessLifecycle Lifecycle;
        public readonly EProcessState From;
        public readonly EProcessState Expected;

        public TransitionCell(EProcessControlState control, EProcessLifecycle lifecycle, EProcessState from, EProcessState expected)
        {
            Control = control;
            Lifecycle = lifecycle;
            From = from;
            Expected = expected;
        }

        public override string ToString() => $"{Control} {Lifecycle} {From} to {Expected}";
    }

    /// <summary>What really happened to a process placed in a cell.</summary>
    public readonly struct TransitionObservation
    {
        /// <summary>False when nothing can put a process in this cell, or nothing can trigger it.</summary>
        public readonly bool Reachable;

        public readonly EProcessState Reached;
        public readonly string Note;

        private TransitionObservation(bool reachable, EProcessState reached, string note)
        {
            Reachable = reachable;
            Reached = reached;
            Note = note;
        }

        public static TransitionObservation Moved(EProcessState reached, string note = "") => new(true, reached, note);

        public static TransitionObservation Unreachable(string note) => new(false, default, note);

        public override string ToString() => Reachable ? $"reached {Reached} {Note}".TrimEnd() : $"unreachable: {Note}";
    }

    /// <summary>
    /// The 96 transitions ProcessControl means to make, written out here on purpose rather than read from
    /// the framework, so they can disagree with it. <see cref="DiffAgainstFramework"/> catches the table
    /// changing, and <see cref="Observe"/> checks a cell against what a live process really does.
    /// </summary>
    public static class ProcessStateOracle
    {
        private const EProcessState R = EProcessState.Running;
        private const EProcessState W = EProcessState.Waiting;
        private const EProcessState T = EProcessState.Terminated;

        // A row per lifecycle: SelfTerminating, RunThenWait, RequiresControl, Synchronous.
        // A column per state moved from: Created, Running, Waiting, Terminated.
        private static readonly Dictionary<EProcessControlState, EProcessState[,]> Table = new()
        {
            [EProcessControlState.Ready] = new[,]
            {
                { R, T, R, T },
                { R, W, R, T },
                { W, W, R, T },
                { R, W, R, T },
            },
            [EProcessControlState.Waiting] = new[,]
            {
                { W, W, W, T },
                { W, W, W, T },
                { W, W, W, T },
                { W, W, W, T },
            },
            [EProcessControlState.Closed] = new[,]
            {
                { T, T, R, T },
                { T, W, R, T },
                { W, W, W, T },
                { T, T, R, T },
            },
            [EProcessControlState.ClosedWaiting] = new[,]
            {
                { T, W, W, T },
                { T, W, W, T },
                { T, W, W, T },
                { T, W, W, T },
            },
            [EProcessControlState.Terminated] = new[,]
            {
                { T, T, T, T },
                { T, T, T, T },
                { T, T, T, T },
                { T, T, T, T },
            },
            [EProcessControlState.TerminatedImmediately] = new[,]
            {
                { T, T, T, T },
                { T, T, T, T },
                { T, T, T, T },
                { T, T, T, T },
            },
        };

        public static EProcessState Expected(EProcessControlState control, EProcessLifecycle lifecycle, EProcessState from)
        {
            return Table[control][(int)lifecycle, (int)from];
        }

        /// <summary>Every cell of the table.</summary>
        public static IEnumerable<TransitionCell> Cells
        {
            get
            {
                foreach (EProcessControlState control in Enum.GetValues(typeof(EProcessControlState)))
                {
                    foreach (EProcessLifecycle lifecycle in Enum.GetValues(typeof(EProcessLifecycle)))
                    {
                        foreach (EProcessState from in Enum.GetValues(typeof(EProcessState)))
                        {
                            yield return new TransitionCell(control, lifecycle, from, Expected(control, lifecycle, from));
                        }
                    }
                }
            }
        }

        /// <summary>Where this table and ProcessControl's own differ. Empty unless one of them changed.</summary>
        public static List<string> DiffAgainstFramework()
        {
            var field = typeof(ProcessControl).GetField("DefaultTransitions", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) return new List<string> { "ProcessControl has no DefaultTransitions table any more" };

            var framework = (Dictionary<(EProcessControlState, EProcessLifecycle, EProcessState), EProcessState>)field.GetValue(null);
            var differences = new List<string>();

            foreach (var cell in Cells)
            {
                if (!framework.TryGetValue((cell.Control, cell.Lifecycle, cell.From), out var actual))
                {
                    differences.Add($"{cell}: missing from the framework's table");
                }
                else if (actual != cell.Expected)
                {
                    differences.Add($"{cell}: the framework's table says {actual}");
                }
            }

            int cells = Table.Count * 16;
            if (framework.Count != cells) differences.Add($"the framework's table has {framework.Count} entries, not {cells}");

            return differences;
        }

        /// <summary>
        /// Puts a live process in <paramref name="cell"/> and triggers its transition, which is what makes
        /// the table's claim mean something, and reports where the process ended up:
        ///   Created    registering it under the control state.
        ///   Running    its run ending under the control state.
        ///   Waiting    asking it to resume under the control state.
        ///   Terminated nothing: a terminated block is unregistered, so the cell can't be reached.
        /// A control state change can itself move a process, and then that move is what's reported. A
        /// synchronous process has no run to end, so its Running cells can't be reached either.
        /// </summary>
        public static async UniTask<TransitionObservation> Observe(World world, TransitionCell cell)
        {
            switch (cell.From)
            {
                case EProcessState.Created: return await ObserveRegistration(world, cell);
                case EProcessState.Running: return await ObserveRunEnding(world, cell);
                case EProcessState.Waiting: return await ObserveResume(world, cell);
                default: return TransitionObservation.Unreachable("a terminated block is unregistered");
            }
        }

        private static async UniTask<TransitionObservation> ObserveRegistration(World world, TransitionCell cell)
        {
            world.Control.SetControlState(cell.Control);

            var probe = world.NewProbe("Oracle Probe", cell.Lifecycle);
            if (!ProcessControl.Register(probe, ProcessDataPacket.Default(), out var relay))
            {
                return TransitionObservation.Unreachable($"ProcessControl takes no new processes while {cell.Control}");
            }

            // Owned once it's in: the world ends what the cell leaves running
            world.Own(relay);
            await world.Frames.Step();
            return TransitionObservation.Moved(relay.State);
        }

        private static async UniTask<TransitionObservation> ObserveRunEnding(World world, TransitionCell cell)
        {
            var probe = world.Probe("Oracle Probe", cell.Lifecycle);
            if (cell.Lifecycle == EProcessLifecycle.RequiresControl) probe.Relay.Unpause();

            world.Control.SetControlState(cell.Control);
            await world.Frames.Step();

            if (!probe.Relay.IsRunning) return TransitionObservation.Moved(probe.Relay.State, "(moved by the control change)");
            if (cell.Lifecycle == EProcessLifecycle.Synchronous) return TransitionObservation.Unreachable("a synchronous process has no run to end");

            probe.Recorder.Released = true;
            await world.Frames.Step(2);
            return TransitionObservation.Moved(probe.Relay.State);
        }

        private static async UniTask<TransitionObservation> ObserveResume(World world, TransitionCell cell)
        {
            var probe = world.Probe("Oracle Probe", cell.Lifecycle);

            // RequiresControl waits from the start; anything else is sent there through a run ending
            if (!probe.Relay.IsWaiting)
            {
                probe.Relay.Pause();
                probe.Recorder.Released = true;
                await world.Frames.Step(2);
                probe.Recorder.Released = false;
            }

            if (!probe.Relay.IsWaiting) return TransitionObservation.Moved(probe.Relay.State, "(would not pause)");

            world.Control.SetControlState(cell.Control);
            await world.Frames.Step();

            if (!probe.Relay.IsWaiting) return TransitionObservation.Moved(probe.Relay.State, "(moved by the control change)");

            probe.Relay.Unpause();
            await world.Frames.Step(2);
            return TransitionObservation.Moved(probe.Relay.State);
        }
    }
}
