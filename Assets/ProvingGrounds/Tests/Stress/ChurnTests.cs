using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The stress suite's churn (the review's Module 3): processes registered and ended by the thousand, as a game's
    /// projectiles are. ProcessControl's work for each should stay the same however many it holds, the review's budget
    /// being linear up to 10,000. A cost is compared per process with the same run at a tenth of the size.
    /// </summary>
    [Category("Stress")]
    public class ChurnTests : ProvingGroundsTest
    {
        private const int Many = 10000, Few = Many / 10;

        // A process that only steps, so what's measured is ProcessControl's own work
        private sealed class Idle : AbstractRuntimeProcess
        {
            public Idle() : base("Idle", EProcessStepPriorityMethod.Manual, 0, EProcessStepTiming.Update, EProcessLifecycle.Synchronous) { }

            public override UniTask RunProcess(CancellationToken token) => UniTask.CompletedTask;
        }

        /// <summary>
        /// Registering a process and ending it each cost about as much among 10,000 as among 1,000: within three times, as
        /// the cost tests allow. They're ended first to last, as projectiles that fly as long are, or last to first.
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_Registering_and_ending_10000_processes_costs_each_as_much_as_1000([Values] bool lastFirst) => Live(async () =>
        {
            var few = await Churn(Few, lastFirst);
            var many = await Churn(Many, lastFirst);

            TestContext.WriteLine($"µs a process, {Few} → {Many}: to register {few.register:F2} → {many.register:F2}, to end {few.end:F2} → {many.end:F2}");
            Assert.That(many.register, Is.LessThan(3 * few.register), "to register one");
            Assert.That(many.end, Is.LessThan(3 * few.end), "to end one");
        });

        /// <summary>A process's step costs about as much among 10,000 as among 1,000, in PlayForge's step loops.</summary>
        [UnityTest]
        public IEnumerator Stress_A_process_step_costs_as_much_among_10000_as_among_1000() => Live(async () =>
        {
            var relays = new List<ProcessRelay>(Many);
            try
            {
                Register(relays, Few);
                await Frames.Step(3);
                double few = await StressMeasure.StepNanoseconds(Frames, 30) / Few;

                Register(relays, Many - Few);
                await Frames.Step(3);
                double many = await StressMeasure.StepNanoseconds(Frames, 30) / Many;

                TestContext.WriteLine($"ns a process's step, {Few} → {Many}: {few:F0} → {many:F0}");
                Assert.That(many, Is.LessThan(3 * few), "a process's step, in PlayForge's step loops");
            }
            finally
            {
                foreach (var relay in relays)
                {
                    if (World.Control.IsRegistered(relay.CacheIndex)) World.Control.TerminateImmediate(relay.CacheIndex, false);
                }
            }
        });

        private static void Register(List<ProcessRelay> relays, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!ProcessControl.Register(new Idle(), ProcessDataPacket.Default(), out var relay)) Assert.Fail($"ProcessControl refused process {relays.Count}");
                relays.Add(relay);
            }
        }

        // Microseconds to register a process, and to end one, with `count` registered at once: the fastest of three rounds
        private async UniTask<(double register, double end)> Churn(int count, bool lastFirst)
        {
            double register = double.MaxValue, end = double.MaxValue;
            var relays = new List<ProcessRelay>(count);
            var watch = new Stopwatch();
            for (int round = 0; round < 3; round++)
            {
                relays.Clear();
                watch.Restart();
                Register(relays, count);
                watch.Stop();
                register = Math.Min(register, watch.Elapsed.TotalMilliseconds * 1000 / count);

                // They step a frame, as a game's do before they end
                await Frames.Step();

                watch.Restart();
                for (int i = 0; i < count; i++) World.Control.TerminateImmediate(relays[lastFirst ? count - 1 - i : i].CacheIndex, false);
                watch.Stop();
                end = Math.Min(end, watch.Elapsed.TotalMilliseconds * 1000 / count);

                await Frames.Step(2);
                Assert.That(World.Control.IsRegistered(relays[0].CacheIndex) || World.Control.IsRegistered(relays[count - 1].CacheIndex), Is.False, "they ended");
            }
            return (register, end);
        }
    }
}
