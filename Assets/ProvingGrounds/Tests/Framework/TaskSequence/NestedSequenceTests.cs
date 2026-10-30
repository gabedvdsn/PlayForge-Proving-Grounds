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
        [UnityTest, Category("Red")]
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
