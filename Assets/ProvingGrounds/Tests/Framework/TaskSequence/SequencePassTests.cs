using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The task sequence framework's pass (EP-28), as an async sequence runs it: waits on the player's input through
    /// ForgeInput, and waits that hold while their sequence is paused; a stage run a set number of times, by its flag or
    /// by its policy; and what a sequence says, through ForgeLog (EP-27). EditMode's TaskSequenceFrameworkTests steps the synced
    /// side by hand.
    /// </summary>
    public class SequencePassTests : ProvingGroundsTest
    {
        /// <summary>Input put in for a test, and the project's own put back after.</summary>
        private sealed class Keys : IForgeInput, IDisposable
        {
            private readonly IForgeInput previous;
            public readonly HashSet<KeyCode> Pressed = new();
            public readonly HashSet<int> Pointers = new();

            public Keys() => previous = ForgeInput.Use(this);
            public void Dispose() => ForgeInput.Use(previous);

            public bool PointerPressed(int button) => Pointers.Contains(button);

            public bool TryGetPointerRay(out Ray ray)
            {
                ray = default;
                return false;
            }

            public bool KeyPressed(KeyCode key) => Pressed.Contains(key);
        }

        public enum Wait
        {
            Key,
            AnyKey,
            Pointer
        }

        /// <summary>
        /// An async sequence couldn't wait on the player but by a predicate of its own (EP-28): WaitForKey, WaitForAnyKey
        /// and WaitForPointer end on their own input alone, read through ForgeInput.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_28_An_input_wait_ends_on_its_own_input([Values] Wait wait) => Live(async () =>
        {
            using var keys = new Keys();
            var heard = KeyCode.None;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Input")
                .Stage(stage => stage.Task(async (_, token) =>
                {
                    switch (wait)
                    {
                        case Wait.Key:
                            await SequenceTaskLibrary.WaitForKey(KeyCode.Q, token);
                            break;
                        case Wait.AnyKey:
                            heard = await SequenceTaskLibrary.WaitForAnyKey(new[] { KeyCode.Alpha1, KeyCode.Q }, token);
                            break;
                        default:
                            await SequenceTaskLibrary.WaitForPointer(1, token);
                            break;
                    }
                }))
                .Build()));

            await Frames.Holds(5, () => relay.State != EProcessState.Terminated, "it waits");
            keys.Pressed.Add(KeyCode.E);
            keys.Pointers.Add(0);
            await Frames.Holds(3, () => relay.State != EProcessState.Terminated, "other input doesn't end it");
            keys.Pressed.Add(KeyCode.Q);
            keys.Pointers.Add(1);
            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "its own input ends it");
            if (wait == Wait.AnyKey) Assert.AreEqual(KeyCode.Q, heard, "and says which key");
        });

        /// <summary>
        /// A wait on the player holds with its sequence, as the library's delays do: a press while it's paused isn't
        /// heard, and one after the resume is.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_28_An_input_wait_holds_while_its_sequence_is_paused() => Live(async () =>
        {
            using var keys = new Keys();
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Paused Input")
                .Stage(stage => stage.Task((_, token) => SequenceTaskLibrary.WaitForKey(KeyCode.Q, token)))
                .Build()));

            await Frames.Step(2);
            Assert.That(relay.Pause(), "the pause is accepted");
            keys.Pressed.Add(KeyCode.Q);
            await Frames.Step(3);
            keys.Pressed.Clear();
            Assert.That(relay.Unpause(), "the resume is accepted");
            await Frames.Holds(3, () => relay.State != EProcessState.Terminated, "a press while paused wasn't heard");
            keys.Pressed.Add(KeyCode.Q);
            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "a press after the resume is");
        });

        /// <summary>
        /// The async library had no WaitUntil or WaitWhile of its own, so a task waited with UniTask's, which went on
        /// looking while its sequence was paused (EP-28). The library's look only while it runs.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_28_A_library_wait_holds_while_its_sequence_is_paused([Values] bool until) => Live(async () =>
        {
            bool open = false;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Gate")
                .Stage(stage => stage.Task((_, token) => until
                    ? SequenceTaskLibrary.WaitUntil(() => open, token)
                    : SequenceTaskLibrary.WaitWhile(() => !open, token)))
                .Build()));

            await Frames.Holds(3, () => relay.State != EProcessState.Terminated, "the gate is shut");
            Assert.That(relay.Pause(), "the pause is accepted");
            open = true;
            await Frames.Holds(3, () => relay.State != EProcessState.Terminated, "paused, it doesn't look");
            Assert.That(relay.Unpause(), "the resume is accepted");
            await Frames.Until(() => relay.State == EProcessState.Terminated, 5, "resumed, it sees the gate open");
        });

        public enum RepeatKind
        {
            Flag,
            Policy
        }

        /// <summary>
        /// A stage repeated until something broke it, so "three times" needed a predicate on its repeat count, and one
        /// repeated by its policy, with a delay between, never counted its runs or raised OnRepeat (EP-28). WithRepeat(3)
        /// runs it three times in all, by either; its OnRepeat hears the two repeats, each counted as it's heard.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_28_A_stage_runs_as_many_times_as_it_is_told([Values] RepeatKind kind) => Live(async () =>
        {
            int runs = 0;
            var counts = new List<int>();
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Thrice")
                .Stage(stage =>
                {
                    stage.WithRepeat(3)
                        .OnRepeat(context => counts.Add(context.Runtime.GetRepeatCount(context.Stage)))
                        .Do(_ => runs++);
                    if (kind == RepeatKind.Policy) stage.WithRepeatAndDelay(0.05f);
                })
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 60, "the sequence ends after its runs");
            Assert.AreEqual(3, runs, "three runs");
            Assert.That(counts, Is.EqualTo(new[] { 0, 1 }), "two repeats, counted as they're heard");
        });

        /// <summary>
        /// What a sequence says went through Unity's console alone (EP-27): a handler's throw is a Sequences error through
        /// ForgeLog, which the Creator Lab's log and a project's own listener hear, and a chain's progress, where its
        /// logging is on, is Sequences information.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_27_What_a_sequence_says_goes_through_ForgeLog() => Live(async () =>
        {
            var heard = new List<ForgeLogEntry>();
            void Hear(ForgeLogEntry entry) => heard.Add(entry);
            var level = ForgeLog.HasOwnLevel(ForgeLog.ESystem.Sequences) ? ForgeLog.LevelOf(ForgeLog.ESystem.Sequences) : (ForgeLog.ELevel?)null;
            ForgeLog.SetLevel(ForgeLog.ESystem.Sequences, ForgeLog.ELevel.Verbose);
            ForgeLog.Logged += Hear;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("OnComplete"));
                var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Throws")
                    .OnComplete((_, _) => throw new InvalidOperationException("thrown on purpose"))
                    .Stage(stage => stage.Do(_ => { }))
                    .Build()));
                await Frames.Until(() => relay.State == EProcessState.Terminated, 10, "it ends");
                Assert.That(heard, Has.Some.Matches<ForgeLogEntry>(entry =>
                    entry.System == ForgeLog.ESystem.Sequences && entry.Level == ForgeLog.ELevel.Error), "the throw is a Sequences error");

                heard.Clear();
                var chain = new TaskSequence(TaskSequenceBuilder.Create("First").Stage(stage => stage.Do(_ => { })).Build())
                    .Then(next => next.WithName("Second").Stage(stage => stage.Do(_ => { })))
                    .WithLogging(true);
                await chain.Run(SequenceDataPacket.Default());
                Assert.That(heard, Has.Some.Matches<ForgeLogEntry>(entry =>
                    entry.System == ForgeLog.ESystem.Sequences && entry.Level == ForgeLog.ELevel.Info), "a chain's progress is Sequences information");
            }
            finally
            {
                ForgeLog.Logged -= Hear;
                ForgeLog.SetLevel(ForgeLog.ESystem.Sequences, level);
            }
        });

        /// <summary>
        /// A stage repeated by its policy, as WithRepeatAndDelay sets it, ran its repeats in the policy, which never raised
        /// the stage's OnRepeat or counted its runs, so a handler heard nothing and GetRepeatCount read 0 (EP-28). The
        /// policy's repeats are heard and counted as a flag's are.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_28_A_stage_repeated_by_its_policy_hears_and_counts_its_repeats() => Live(async () =>
        {
            int runs = 0;
            var counts = new List<int>();
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Policy Repeat")
                .Stage(stage => stage
                    .WithRepeatAndDelay(0.05f)
                    .OnRepeat(context => counts.Add(context.Runtime.GetRepeatCount(context.Stage)))
                    .Do(data =>
                    {
                        if (++runs >= 3) data.BreakStageRepeat();
                    }))
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 60, "the break ends it");
            Assert.AreEqual(3, runs, "three runs");
            Assert.That(counts, Is.EqualTo(new[] { 0, 1 }), "its two repeats heard, each counted as it's heard");
        });

        // ─── What the examples' rewrite found (EP-31) ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A synced task in an async sequence was stepped by the frame's time whatever its sequence was doing, so a
        /// SyncDelay or a library's synced tween went on while the sequence was paused, which the library's own waits
        /// don't (EP-31, found by the Task Sequence Examples' rewrite). It holds with its run.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_31_A_synced_task_holds_while_its_async_sequence_is_paused() => Live(async () =>
        {
            float stepped = 0f;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Synced In Async")
                .Stage(stage => stage.SyncTask((_, deltaTime) =>
                {
                    stepped += deltaTime;
                    return false;
                }))
                .Build()));

            await Frames.Step(3);
            Assert.Greater(stepped, 0f, "it steps while the sequence runs");
            Assert.That(relay.Pause(), "the pause is accepted");
            float at = stepped;
            await Frames.Step(5);
            Assert.AreEqual(at, stepped, "paused, it isn't stepped");
            Assert.That(relay.Unpause(), "the resume is accepted");
            await Frames.Step(3);
            Assert.Greater(stepped, at, "resumed, it is again");
            relay.Terminate();
        });

        /// <summary>
        /// data.SetStageTimeout gave its timeout the injection it was given, null where it was given none, so a stage's
        /// time up applied nothing, where WithTimeout's skips the stage (EP-31). Its default is the skip, as WithTimeout's.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_31_A_stage_timeout_set_from_a_task_skips_the_stage() => Live(async () =>
        {
            bool reached = false;
            var relay = Run(new TaskSequence(TaskSequenceBuilder.Create("Timed From Inside")
                .Stage(stage => stage.Task(async (data, token) =>
                {
                    Assert.That(data.SetStageTimeout(0.1f), "the timeout is set");
                    await SequenceTaskLibrary.WaitUntil(() => false, token);
                }))
                .Stage(stage => stage.Do(_ => reached = true))
                .Build()));

            await Frames.Until(() => relay.State == EProcessState.Terminated, 30, "the time's up skips the stage, and the run ends");
            Assert.IsTrue(reached, "on through the next stage");
        });

        private static bool openGate;

        /// <summary>A chain whose condition ends it once the gate opens, its first sequence waiting for ever.</summary>
        public static TaskSequenceChain GatedChain() =>
            new TaskSequence(TaskSequenceBuilder.Create("Waits")
                    .Stage(stage => stage.Task((_, token) => SequenceTaskLibrary.WaitUntil(() => false, token)))
                    .Build())
                .Then(next => next.WithName("Never").Stage(stage => stage.Do(_ => { })))
                .InterruptWhen(_ => openGate);

        /// <summary>A sequence's definition, as a method the sequence dropdown offers may return.</summary>
        public static TaskSequenceDefinition MadeDefinition() =>
            TaskSequenceBuilder.Create("Defined").Stage(stage => stage.Do(_ => definitionRuns++)).Build();

        private static int definitionRuns;

        /// <summary>
        /// An ability's Sequence task awaited a chain in place, where nothing asked its conditions, so a chain an ability
        /// ran never heard them, its sequences' neither (EP-31). It runs the chain as a process, as a sequence's run is,
        /// which asks them each frame.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_31_A_chain_an_ability_runs_heeds_its_conditions([Values] Hub hub) => Live(async () =>
        {
            openGate = false;
            var task = new RunSequenceTask { Sequence = new TaskSequenceReference(typeof(SequencePassTests).GetMethod(nameof(GatedChain))) };
            var abilities = World.Entity(hub).GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(Make.Ability("Gated", Make.Stage(task)), 1, out int index), "the entity knows it");
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "the ability activates");

            await Frames.Holds(5, () => abilities.IsExecuting, "the chain waits");
            openGate = true;
            await Frames.Until(() => !abilities.IsExecuting, 10, "its condition ends it, and the ability with it");
        });

        /// <summary>
        /// The sequence dropdown offers a method that returns a sequence's definition, and the Sequence task found no
        /// sequence in it, so it did nothing at all (EP-31). A definition's method runs as a sequence's does.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_31_A_definition_s_method_runs_from_an_ability([Values] Hub hub) => Live(async () =>
        {
            definitionRuns = 0;
            var reference = new TaskSequenceReference(typeof(SequencePassTests).GetMethod(nameof(MadeDefinition)));
            Assert.IsInstanceOf<TaskSequence>(reference.GetActiveSequence(), "the reference finds a sequence in it");

            var abilities = World.Entity(hub).GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(Make.Ability("Defined", Make.Stage(new RunSequenceTask { Sequence = reference })), 1,
                out int index), "the entity knows it");
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "the ability activates");
            await Frames.Until(() => !abilities.IsExecuting, 10, "its sequence runs to its end");
            Assert.AreEqual(1, definitionRuns, "it ran");
        });

        private ProcessRelay Run(TaskSequence sequence)
        {
            ProcessControl.Register(sequence).Run(out var relay);
            World.Own(relay);
            return relay;
        }
    }
}
