using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>The queue an entity's deferred actions wait in until its end of frame.</summary>
    public class ActionQueueTests : ProvingGroundsTest
    {
        /// <summary>
        /// The queue kept its buckets in a SortedDictionary and walked it to take each action off, and the walk allocated
        /// an enumerator every time, so every deferred action allocated as it ran. Taking one off now allocates nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_P9_Taking_an_action_off_the_queue_allocates_nothing() => Live(async () =>
        {
            await Frames.Step();
            var queue = new ActionQueue();
            var action = new LambdaAction(_ => { });

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() =>
            {
                queue.Enqueue(action);
                queue.ProcessAll(null);
            }, runs: 500, warmup: 20);

            Assert.That(bytes, Is.EqualTo(0f), "bytes per action queued and run");
        });

        /// <summary>
        /// The control for the queue's buckets: higher priorities run first, each in the order queued, a priority between
        /// the named ones takes its place among them, and an action queued as the queue runs takes its place by priority.
        /// </summary>
        [UnityTest]
        public IEnumerator Actions_run_by_priority_and_in_the_order_queued() => Live(async () =>
        {
            await Frames.Step();
            var queue = new ActionQueue();
            var ran = new List<string>();

            queue.Enqueue(new LambdaAction(_ => ran.Add("normal 1"), priority: ActionPriority.Normal));
            queue.Enqueue(new LambdaAction(_ => ran.Add("low"), priority: ActionPriority.Low));
            queue.Enqueue(new LambdaAction(_ => ran.Add("critical"), priority: ActionPriority.Critical));
            queue.Enqueue(new LambdaAction(_ => ran.Add("between"), priority: 25));
            queue.Enqueue(new LambdaAction(_ => ran.Add("normal 2"), priority: ActionPriority.Normal));
            queue.Enqueue(new LambdaAction(_ =>
            {
                ran.Add("high");
                queue.Enqueue(new LambdaAction(__ => ran.Add("queued by high"), priority: ActionPriority.Critical));
            }, priority: ActionPriority.High));

            int executed = queue.ProcessAll(null);

            Assert.That(ran, Is.EqualTo(new[] { "critical", "high", "queued by high", "between", "normal 1", "normal 2", "low" }));
            Assert.That(executed, Is.EqualTo(7));
            Assert.That(queue.HasActions, Is.False);
        });
    }
}
