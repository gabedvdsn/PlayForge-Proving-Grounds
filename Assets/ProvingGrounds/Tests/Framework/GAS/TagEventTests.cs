using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Tag-change events (FEAT-3, Decision 30). A listener on an entity's tag hears it come and go as it happens, and a
    /// listener on a parent hears its children. A compile, which rebuilds the tags from their sources, reports only what
    /// it changed. Tag workers are evaluated when the tags change, where they were evaluated every frame.
    /// </summary>
    public class TagEventTests : ProvingGroundsTest
    {
        /// <summary>Nothing told anyone a tag came or went: a tag worker polled for it every frame (FEAT-3).</summary>
        [UnityTest]
        public IEnumerator FEAT_3_A_listener_hears_a_tag_come_and_go_as_it_happens([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var burning = Make.NewTag("Test.State.Burning");
            var fire = Make.Effect("Fire", new EffectConfig { Target = null, GrantedTags = new List<Tag> { burning } });
            var heard = new List<TagChange>();
            using var listener = gas.GetTagCache().Listen(burning, heard.Add);

            Assert.IsTrue(World.Apply(gas, gas, fire), "the fire lands");
            Assert.AreEqual(1, heard.Count, "the listener hears its tag come before Apply returns");
            Assert.IsTrue(heard[0].Added, "as added");
            Assert.AreEqual(burning, heard[0].Tag, "naming it");
            Assert.AreEqual((0, 1), (heard[0].Before, heard[0].After), "with its weight before and after");

            gas.RemoveGameplayEffect(fire);
            await Frames.Step(2);
            Assert.AreEqual(2, heard.Count, "and go, once: the compiles between report nothing");
            Assert.IsTrue(heard[1].Removed, "as removed");
        });

        /// <summary>A listener on a parent tag hears its children come and go, and nothing else (Decision 30).</summary>
        [UnityTest]
        public IEnumerator FEAT_3_A_listener_on_a_parent_hears_its_children([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var state = Make.NewTag("Test.State");
            var stunned = Tag.Generate(state.Name + ".Stunned");
            var heard = new List<TagChange>();
            using var listener = gas.GetTagCache().Listen(state, heard.Add);

            gas.AddLooseTag(stunned);
            Assert.AreEqual(1, heard.Count, "a listener on the parent hears the child come");
            Assert.AreEqual(stunned, heard[0].Tag, "naming the child");
            Assert.IsTrue(heard[0].Added, "as added");

            gas.AddLooseTag(Make.NewTag("Test.Elsewhere"));
            Assert.AreEqual(1, heard.Count, "and nothing outside it");
            await Frames.Step();
        });

        /// <summary>
        /// A compile rebuilds the tags from their sources, clearing them first, so an event for each step would report every
        /// tag going and coming. It reports only what it changed; AnyCount hears a weight change, which AddedOrRemoved doesn't.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_3_A_compile_reports_only_what_it_changed([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var held = Make.NewTag("Test.State.Held");
            var counted = Make.NewTag("Test.State.Counted");
            gas.AddLooseTag(held);
            gas.AddLooseTag(counted);
            var heard = new List<TagChange>();
            var counts = new List<TagChange>();
            using var comes = gas.GetTagCache().Listen(held, heard.Add);
            using var every = gas.GetTagCache().Listen(counted, counts.Add, ETagChangeKind.AnyCount);

            gas.CompileGrantedTags();
            Assert.IsEmpty(heard, "a compile that changes nothing reports nothing");
            Assert.IsEmpty(counts, "to either kind of listener");

            gas.AddLooseTag(counted);
            Assert.AreEqual(1, counts.Count, "AnyCount hears a weight change");
            Assert.AreEqual((1, 2), (counts[0].Before, counts[0].After), "from 1 to 2");
            gas.AddLooseTag(held);
            Assert.IsEmpty(heard, "which AddedOrRemoved doesn't");
            await Frames.Step();
        });

        /// <summary>A listener that's disposed hears no more.</summary>
        [UnityTest]
        public IEnumerator FEAT_3_A_disposed_listener_hears_no_more([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var tag = Make.NewTag("Test.State.Once");
            var heard = new List<TagChange>();
            var listener = gas.GetTagCache().Listen(tag, heard.Add);

            gas.AddLooseTag(tag);
            listener.Dispose();
            Assert.IsFalse(listener.Listening, "a disposed listener stops");
            gas.RemoveLooseTag(tag);
            Assert.AreEqual(1, heard.Count, "and hears the tag come, but not go");
            await Frames.Step();
        });

        /// <summary>A listener can stop itself, or change the tags, while it hears a change.</summary>
        [UnityTest]
        public IEnumerator FEAT_3_A_listener_can_change_tags_as_it_hears([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var trigger = Make.NewTag("Test.State.Trigger");
            var answer = Make.NewTag("Test.State.Answer");
            TagListener listener = null;
            int heard = 0;
            listener = gas.GetTagCache().Listen(trigger, change =>
            {
                heard++;
                listener.Dispose();
                gas.AddLooseTag(answer);
            });

            gas.AddLooseTag(trigger);
            Assert.AreEqual(1, heard, "it hears the change once");
            Assert.That(gas.GetTagWeight(answer), Is.GreaterThan(0), "and its own change lands");
            gas.RemoveLooseTag(trigger);
            Assert.AreEqual(1, heard, "and, stopped, it hears no more");
            await Frames.Step();
        });

        // A tag worker that counts how often it's evaluated
        private sealed class CountingWorker : AbstractTagWorker
        {
            public int Evaluated;
            public bool EveryFrame;

            public override bool EvaluateEveryFrame => EveryFrame;

            public override bool ValidateWorkFor(ITagHandler handler)
            {
                Evaluated++;
                return false;
            }
        }

        /// <summary>
        /// The tag workers were evaluated at the end of every frame, whatever had changed, as a poll for the tags they
        /// name (FEAT-3). They're evaluated at the end of a frame in which the tags changed.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_3_Tag_workers_are_evaluated_when_the_tags_change([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var worker = new CountingWorker();
            gas.GetTagCache().ProvideWorker(worker);
            await Frames.Step(2);
            Assert.That(worker.Evaluated, Is.GreaterThan(0), "a worker added is evaluated");

            int settled = worker.Evaluated;
            await Frames.Step(5);
            Assert.AreEqual(settled, worker.Evaluated, "and, with no tag changing, isn't again");

            gas.AddLooseTag(Make.NewTag("Test.State.Changed"));
            await Frames.Step(2);
            Assert.That(worker.Evaluated, Is.GreaterThan(settled), "until a tag changes");
        });

        /// <summary>A worker whose validity reads more than the tags asks to be evaluated every frame, as all were.</summary>
        [UnityTest]
        public IEnumerator FEAT_3_A_tag_worker_can_ask_to_be_evaluated_every_frame([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var worker = new CountingWorker { EveryFrame = true };
            gas.GetTagCache().ProvideWorker(worker);
            await Frames.Step(2);

            int before = worker.Evaluated;
            await Frames.Step(5);
            Assert.That(worker.Evaluated - before, Is.InRange(4, 6), "it's evaluated each frame");
        });
    }
}
