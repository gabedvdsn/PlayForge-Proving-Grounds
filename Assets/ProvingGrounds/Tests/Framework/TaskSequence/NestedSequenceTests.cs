using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class NestedSequenceTests : ProvingGroundsTest
    {
        // RunSequenceTask finds its sequence through a static method, so the task for it is handed over here
        private static ISequenceTask _nested;

        /// <summary>A nested sequence that takes interrupts, as most do.</summary>
        public static TaskSequence Interruptible()
        {
            return new TaskSequence(TaskSequenceBuilder.Create("Nested").Task(_nested).Build());
        }

        /// <summary>A nested sequence whose critical section refuses an interrupt.</summary>
        public static TaskSequence Uninterruptible()
        {
            return new TaskSequence(TaskSequenceBuilder.Create("Nested")
                .WithCriticalFlag(true)
                .WithCriticalAllowedInjections(typeof(SkipStageInjection))
                .Task(_nested)
                .Build());
        }

        /// <summary>
        /// Cancelling the outer ability sent the nested sequence an interrupt, which it can refuse. The ability
        /// ended and left the nested sequence running as a process nothing owned any more.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_G_Interrupting_an_ability_ends_its_nested_sequence([Values] Hub hub) => Live(async () =>
        {
            var abilities = RunNested(hub, nameof(Uninterruptible), out int index);
            await Frames.Step(3);
            Assert.That(Ledger.Prepares("Nested"), Is.EqualTo(1), "the nested sequence is running");

            abilities.Inject(index, InterruptSequenceInjection.Instance);

            await Frames.Until(() => Ledger.Cleans("Nested") == 1, 5, "it ends with the ability");
        });

        /// <summary>The control for the test above: a nested sequence that takes the interrupt ended already.</summary>
        [UnityTest]
        public IEnumerator An_interruptible_nested_sequence_ends_with_its_ability([Values] Hub hub) => Live(async () =>
        {
            var abilities = RunNested(hub, nameof(Interruptible), out int index);
            await Frames.Step(3);

            abilities.Inject(index, InterruptSequenceInjection.Instance);

            await Frames.Until(() => Ledger.Cleans("Nested") == 1, 5, "it ends with the ability");
        });

        /// <summary>
        /// Run Sequence ran only a sequence a static method made, by name, so a sequence made in code, as an ability built
        /// as the game runs makes one, couldn't run in an ability (FEAT-21). Given a definition made in code, each
        /// activation runs a fresh sequence from it, with the activation's data.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_21_Run_Sequence_runs_a_sequence_made_in_code([Values] Hub hub) => Live(async () =>
        {
            var definitionField = typeof(RunSequenceTask).GetField("Definition");
            Assert.IsNotNull(definitionField, "Run Sequence takes a sequence made in code");
            int runs = 0;
            SequenceDataPacket seen = null;
            var definition = TaskSequenceBuilder.Create("Made").Task(data =>
            {
                runs++;
                seen = data;
            }).Build();
            var task = new RunSequenceTask();
            definitionField.SetValue(task, definition);

            var abilities = World.Entity(hub).GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(Make.Ability("Made", Make.Stage(task)), 1, out int index), "the entity knows it");
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "the ability activates");
                await Frames.Until(() => !abilities.IsExecuting, 10, "and its sequence runs to its end");
            }
            Assert.AreEqual(2, runs, "a fresh run of the sequence each activation");
            Assert.IsInstanceOf<AbilityDataPacket>(seen, "with the activation's own data");
        });

        private AbilitySystemComponent RunNested(Hub hub, string provider, out int index)
        {
            _nested = new ProbeSequenceTask(World, "Nested", frames: 1000);
            var task = new RunSequenceTask { Sequence = new TaskSequenceReference(typeof(NestedSequenceTests).GetMethod(provider)) };

            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Outer", Make.Stage(task)), 1, out index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            return abilities;
        }
    }
}
