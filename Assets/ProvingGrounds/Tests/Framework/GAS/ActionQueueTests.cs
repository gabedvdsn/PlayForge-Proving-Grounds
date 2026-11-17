using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
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

        // An action that throws as its queue asks whether it's still valid
        private sealed class BreaksWhenAsked : IRootAction
        {
            public int Priority => ActionPriority.Normal;
            public bool IsValid => throw new InvalidOperationException("asked and broke");
            public string Description => "Breaks when asked";
            public void Execute(ISource system) { }
        }

        // An effect worker that queues, as its effect lands, an action that throws as it runs
        private sealed class QueuesABreak : AbstractEffectWorker
        {
            public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx) =>
                new IRootAction[] { new LambdaAction(_ => throw new InvalidOperationException("ran and broke"), "Breaks as it runs") };
        }

        /// <summary>
        /// An action that threw as it ran was written to the Console and heard by no one: no listener heard it end, and the
        /// effect journal showed it queued for good (EP-43). It's heard as failed, with what it threw, and the rest run.
        /// </summary>
        [Test]
        public void EP_43_An_action_that_throws_is_heard_as_failed_and_the_rest_run()
        {
            var queue = new ActionQueue();
            var ran = new List<string>();
            var failed = new List<(IRootAction action, Exception error)>();
            queue.OnActionFailed += (action, error) => failed.Add((action, error));
            var breaks = new LambdaAction(_ => throw new InvalidOperationException("ran and broke"), "Breaks as it runs");
            queue.Enqueue(new LambdaAction(_ => ran.Add("first"), "First"));
            queue.Enqueue(breaks);
            queue.Enqueue(new LambdaAction(_ => ran.Add("last"), "Last"));

            LogAssert.Expect(LogType.Error, new Regex("Breaks as it runs"));
            int executed = queue.ProcessAll(null);

            Assert.That(ran, Is.EqualTo(new[] { "first", "last" }), "the rest run");
            Assert.AreEqual(2, executed, "two ran");
            Assert.That(failed.Select(each => each.action), Is.EqualTo(new IRootAction[] { breaks }), "the one that threw is heard as failed");
            Assert.That(failed[0].error, Is.InstanceOf<InvalidOperationException>(), "with what it threw");
        }

        /// <summary>
        /// An action whose IsValid threw took the queue down with it, and with the queue its entity's end of frame (EP-43).
        /// It's heard as failed, and the rest run.
        /// </summary>
        [Test]
        public void EP_43_An_action_whose_validity_throws_is_heard_as_failed_and_the_rest_run()
        {
            var queue = new ActionQueue();
            var ran = new List<string>();
            var failed = new List<IRootAction>();
            queue.OnActionFailed += (action, _) => failed.Add(action);
            var breaks = new BreaksWhenAsked();
            queue.Enqueue(breaks);
            queue.Enqueue(new LambdaAction(_ => ran.Add("after"), "After"));

            LogAssert.Expect(LogType.Error, new Regex("Breaks when asked"));
            int executed = 0;
            Assert.DoesNotThrow(() => executed = queue.ProcessAll(null), "the queue runs on");

            Assert.That(ran, Is.EqualTo(new[] { "after" }), "the rest run");
            Assert.AreEqual(1, executed, "one ran");
            Assert.That(failed, Is.EqualTo(new IRootAction[] { breaks }), "the one that threw is heard as failed");
            Assert.IsFalse(queue.HasActions, "nothing is left");
        }

        /// <summary>
        /// The queue's guard against an action that queues another for good said it had stopped one when a pass ran exactly
        /// its limit and left nothing (EP-43). It speaks only while actions are left, and those wait for the next pass.
        /// </summary>
        [Test]
        public void EP_43_The_loop_guard_speaks_only_while_actions_are_left()
        {
            var queue = new ActionQueue();
            int ran = 0;
            for (int i = 0; i < 3; i++) queue.Enqueue(new LambdaAction(_ => ran++));
            Assert.AreEqual(3, queue.ProcessAll(null, maxIterations: 3), "three of three ran");
            LogAssert.NoUnexpectedReceived();

            for (int i = 0; i < 4; i++) queue.Enqueue(new LambdaAction(_ => ran++));
            LogAssert.Expect(LogType.Error, new Regex("Max iterations"));
            Assert.AreEqual(3, queue.ProcessAll(null, maxIterations: 3), "three of four ran");
            Assert.AreEqual(1, queue.TotalCount, "and one waits for the next pass");
            Assert.AreEqual(6, ran);
        }

        /// <summary>
        /// An entity's action that fails is heard through its callbacks and its frame's summary, its effect journal settles
        /// it as failed, and its frame ends as ever: an action whose IsValid threw ended the entity's end of frame there,
        /// and the actions after it never ran (EP-43).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_43_An_entitys_failed_actions_are_heard_and_its_frame_ends([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), "Target");
            var effect = Make.Effect("Breaking", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 5f, Target = health, Magnitude = 0f,
                Workers = new List<AbstractEffectWorker> { new QueuesABreak() },
            });
            var journal = new EffectJournal();
            journal.Listen(target);
            var failed = new List<string>();
            int failedInSummaries = 0;
            bool afterRan = false;
            target.GetCallbacks().OnActionFailed += (action, _) => failed.Add(action.Description);
            target.GetCallbacks().OnFrameComplete += summary => failedInSummaries += summary.Failed;
            await Frames.Step();

            LogAssert.Expect(LogType.Error, new Regex("Breaks as it runs"));
            LogAssert.Expect(LogType.Error, new Regex("Breaks when asked"));
            Assert.IsTrue(World.Apply(target, target, effect), "the effect lands");
            target.GetActionQueue().Enqueue(new BreaksWhenAsked());
            target.GetActionQueue().Enqueue(new LambdaAction(_ => afterRan = true, "After", ActionPriority.Low));
            await Frames.Step();
            await Frames.Step();

            Assert.That(failed, Is.EquivalentTo(new[] { "Breaks as it runs", "Breaks when asked" }), "its callbacks hear both fail");
            Assert.AreEqual(2, failedInSummaries, "and its frame's summary counts them");
            Assert.IsTrue(afterRan, "the action after them runs, its frame ending as ever");
            var queued = journal.Of(effect).Where(entry => entry.Kind == EEffectJournalKind.Worker).SelectMany(run => run.Actions).Single();
            Assert.AreEqual(EJournalActionState.Failed, queued.State, "and the journal settles its worker's as failed");
            journal.Dispose();
        });

        /// <summary>
        /// Clearing the queue dropped its actions unheard, so a listener waiting on one, as the effect journal, waited for
        /// good (EP-44). Each is heard as dropped.
        /// </summary>
        [Test]
        public void EP_44_Clearing_the_queue_drops_each_action_heard()
        {
            var queue = new ActionQueue();
            var dropped = new List<string>();
            bool ran = false;
            queue.OnActionInvalidated += action => dropped.Add(action.Description);
            queue.Enqueue(new LambdaAction(_ => ran = true, "One"));
            queue.Enqueue(new LambdaAction(_ => ran = true, "Two", ActionPriority.High));

            queue.Clear();

            Assert.That(dropped, Is.EquivalentTo(new[] { "One", "Two" }), "each is heard as dropped");
            Assert.IsFalse(queue.HasActions, "none is left");
            Assert.AreEqual(0, queue.ProcessAll(null));
            Assert.IsFalse(ran, "and none runs");
        }

        /// <summary>
        /// A queued action could be stopped only by its own IsValid (EP-44). Remove takes one off and RemoveWhere every one
        /// it picks, each heard as dropped and never run, the rest running in their order.
        /// </summary>
        [Test]
        public void EP_44_A_queued_action_can_be_taken_off()
        {
            var queue = new ActionQueue();
            var ran = new List<string>();
            var dropped = new List<string>();
            queue.OnActionInvalidated += action => dropped.Add(action.Description);
            var second = new LambdaAction(_ => ran.Add("second"), "Second");
            queue.Enqueue(new LambdaAction(_ => ran.Add("first"), "First"));
            queue.Enqueue(second);
            queue.Enqueue(new LambdaAction(_ => ran.Add("third"), "Third"));
            queue.Enqueue(new LambdaAction(_ => ran.Add("fourth"), "Fourth", ActionPriority.Low));
            queue.Enqueue(new LambdaAction(_ => ran.Add("respawn"), "Respawn", ActionPriority.Low));

            Assert.IsTrue(queue.Remove(second), "one is taken off");
            Assert.IsFalse(queue.Remove(second), "and not twice");
            Assert.AreEqual(1, queue.RemoveWhere(action => action.Description == "Respawn"), "one more is picked off");
            Assert.AreEqual(3, queue.TotalCount, "three are left");
            queue.ProcessAll(null);

            Assert.That(ran, Is.EqualTo(new[] { "first", "third", "fourth" }), "the rest run in their order");
            Assert.That(dropped, Is.EqualTo(new[] { "Second", "Respawn" }), "the two taken off are heard as dropped");
        }

        /// <summary>
        /// An entity that ended kept what it had queued, took more, and ran none of it, as its queue never ran again, so a
        /// listener waiting on one of them waited for good (EP-44). As it ends, each is dropped and heard, and one queued
        /// after is dropped at once.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_44_An_ended_entity_drops_its_actions_and_any_queued_after([Values] Hub hub) => Live(async () =>
        {
            IGameplayAbilitySystem entity;
            ProcessRelay relay;
            if (hub == Hub.Runtime)
            {
                var runtime = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity("Ending", Make.UnusedSet()));
                relay = World.Register(runtime);
                entity = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(name: "Ending");
                relay = World.Register(mono);
                entity = mono;
            }
            await Frames.Step();

            var queue = entity.GetActionQueue();
            var dropped = new List<string>();
            bool ran = false;
            entity.GetCallbacks().OnActionInvalidated += action => dropped.Add(action.Description);
            queue.Enqueue(new LambdaAction(_ => ran = true, "Pending"));
            relay.TerminateImmediate();
            await Frames.Step();

            Assert.That(dropped, Is.EqualTo(new[] { "Pending" }), "what it had queued is heard as dropped");
            Assert.IsTrue(queue.IsClosed, "its queue is closed");
            queue.Enqueue(new LambdaAction(_ => ran = true, "Late"));
            Assert.That(dropped, Is.EqualTo(new[] { "Pending", "Late" }), "and one queued after is dropped at once");
            Assert.IsFalse(queue.HasActions, "nothing waits");
            await Frames.Step();
            Assert.IsFalse(ran, "and none runs");
        });
    }
}
