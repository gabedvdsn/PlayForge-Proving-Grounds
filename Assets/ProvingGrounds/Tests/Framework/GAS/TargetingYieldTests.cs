using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A cast waiting for its target gives way to another (FEAT-39). A player who starts one ability's targeting and then
    /// presses another's key means the other; but the one waiting held its claim, so the second waited in the input buffer
    /// behind a click that never came, and was let go. An ability still choosing its target is cancelled as another of its
    /// entity's activates, nothing of it paid, unless it, or its entity, holds; and a trigger's activation, which the
    /// player didn't ask for, doesn't cut their choice short. A game shows the wait: whether an ability is choosing its
    /// target, and the words its targeting asks with.
    /// </summary>
    public class TargetingYieldTests : ProvingGroundsTest
    {
        // No press ever, so a click-targeting task waits
        private sealed class NoPress : IForgeInput
        {
            public bool PointerPressed(int button) => false;

            public bool TryGetPointerRay(out Ray ray)
            {
                ray = default;
                return false;
            }
        }

        // Puts the input that never presses in, and the one before back as the round ends
        private sealed class Quiet : IDisposable
        {
            private readonly IForgeInput previous = ForgeInput.Use(new NoPress());

            public void Dispose() => ForgeInput.Use(previous);
        }

        // An ability that waits for a click on the ground, then runs its task
        private Ability Aimed()
        {
            var ability = Make.Ability("Aimed", Make.Stage(new ProbeAbilityTask(World, "Aimed Task", frames: 1)));
            ability.Behaviour.Targeting = new SelectPositionTargetTask();
            return ability;
        }

        private Ability Other() => Make.Ability("Other", Make.Stage(new ProbeAbilityTask(World, "Other Task", frames: 1)));

        private static AbilitySpecContainer Give(AbilitySystemComponent abilities, Ability ability)
        {
            Assert.IsTrue(abilities.GiveAbility(ability, 1, out int index), $"the entity knows {ability.GetName()}");
            abilities.TryGetAbilityContainer(index, out var container);
            return container;
        }

        private static ActivationResult Activate(AbilitySystemComponent abilities, AbilitySpecContainer container) =>
            abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index));

        // A choice of an enum field by its name, as the inspector sets it
        private static void Choose(object owner, string field, string choice)
        {
            var member = owner.GetType().GetField(field);
            Assert.IsNotNull(member, $"{owner.GetType().Name} has {field}");
            member.SetValue(owner, Enum.Parse(member.FieldType, choice));
        }

        private static bool IsTargeting(AbilitySpecContainer container)
        {
            var property = typeof(AbilitySpecContainer).GetProperty("IsTargeting");
            Assert.IsNotNull(property, "an ability's slot says whether it's choosing its target");
            return (bool)property.GetValue(container);
        }

        // An aimed cast started and waiting, then another asked for: what came of the other
        private async Cysharp.Threading.Tasks.UniTask<ActivationResult> AimThenOther(AbilitySystemComponent abilities,
            AbilitySpecContainer aimed, AbilitySpecContainer other)
        {
            Assert.IsTrue(Activate(abilities, aimed).Started, "the aimed cast starts");
            await Frames.Step(2);
            Assert.IsTrue(aimed.IsActive, "and waits for its click");
            return Activate(abilities, other);
        }

        /// <summary>
        /// Another ability activated while one waits for its target starts at once, and the one waiting is cancelled, its
        /// task never run (FEAT-39). It waited in the buffer behind the click, and was let go.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_39_A_cast_waiting_for_its_target_gives_way_to_another([Values] Hub hub) => Live(async () =>
        {
            using var quiet = new Quiet();
            var abilities = World.Entity(hub, name: "Caster").GetAbilitySystem();
            var aimed = Give(abilities, Aimed());
            var other = Give(abilities, Other());

            var result = await AimThenOther(abilities, aimed, other);
            Assert.IsTrue(result.Started, $"another, activated meanwhile, starts at once: it was {result.Status}");
            await Frames.Until(() => !aimed.IsActive, 3, "and the cast waiting for its target is cancelled");
            await Frames.Until(() => Ledger.Cleans("Other Task") == 1, 5, "the other runs");
            Assert.AreEqual(0, Ledger.Prepares("Aimed Task"), "and nothing of the cancelled cast ran");
        });

        /// <summary>An ability set to hold keeps its targeting, and another waits as its When Busy says (FEAT-39).</summary>
        [UnityTest]
        public IEnumerator FEAT_39_An_ability_set_to_hold_keeps_its_targeting([Values] Hub hub) => Live(async () =>
        {
            using var quiet = new Quiet();
            var abilities = World.Entity(hub, name: "Caster").GetAbilitySystem();
            var held = Aimed();
            Choose(held.Definition, "WhileTargeting", "Holds");
            var aimed = Give(abilities, held);
            var other = Give(abilities, Other());

            var result = await AimThenOther(abilities, aimed, other);
            Assert.IsFalse(result.Started, "the other waits behind it, as Queue does");
            await Frames.Step(2);
            Assert.IsTrue(aimed.IsActive, "and the aimed cast still waits for its click");
        });

        /// <summary>An entity that holds holds for each of its abilities left to its rule (FEAT-39).</summary>
        [UnityTest]
        public IEnumerator FEAT_39_An_entity_that_holds_keeps_its_abilities_targeting([Values] Hub hub) => Live(async () =>
        {
            using var quiet = new Quiet();
            var rule = typeof(EntityIdentity).GetField("TargetingYields");
            Assert.IsNotNull(rule, "an entity says whether its abilities' targeting yields");
            IGameplayAbilitySystem gas;
            if (hub == Hub.Mono)
            {
                var mono = World.NewMonoEntity(name: "Caster");
                rule.SetValue(mono.EntityData, false);
                World.Register(mono);
                gas = mono;
            }
            else
            {
                var identity = Make.Identity("Caster", Make.UnusedSet());
                rule.SetValue(identity, false);
                var runtime = new RuntimeGameplayAbilitySystem(new RuntimeEntityIdentity(identity));
                World.Register(runtime);
                gas = runtime;
            }
            var abilities = gas.GetAbilitySystem();
            var aimed = Give(abilities, Aimed());
            var other = Give(abilities, Other());

            var result = await AimThenOther(abilities, aimed, other);
            Assert.IsFalse(result.Started, "the other waits behind it");
            await Frames.Step(2);
            Assert.IsTrue(aimed.IsActive, "and the aimed cast still waits for its click");
        });

        /// <summary>
        /// An ability a gameplay event sets off doesn't cancel a cast waiting for its target: the player didn't ask for it
        /// (FEAT-39). A guard, which holds before the feature as after.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_39_A_trigger_doesnt_cut_a_choice_short([Values] Hub hub) => Live(async () =>
        {
            using var quiet = new Quiet();
            var abilities = World.Entity(hub, name: "Caster").GetAbilitySystem();
            var ping = Tag.Generate("Test.Ping");
            var triggered = Other();
            triggered.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.GameplayEvent, Tag = ping });
            var aimed = Give(abilities, Aimed());
            Give(abilities, triggered);

            Assert.IsTrue(Activate(abilities, aimed).Started, "the aimed cast starts");
            await Frames.Step(2);
            abilities.SendGameplayEvent(ping);
            await Frames.Step(2);
            Assert.IsTrue(aimed.IsActive, "the aimed cast still waits for its click");
        });

        /// <summary>
        /// What a game shows of the wait: an ability's slot says it's choosing its target while it is, and each targeting
        /// task that waits for the player asks in words of its own (FEAT-39).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_39_A_game_reads_the_wait_and_its_words([Values] Hub hub) => Live(async () =>
        {
            using var quiet = new Quiet();
            var abilities = World.Entity(hub, name: "Caster").GetAbilitySystem();
            var aimed = Give(abilities, Aimed());
            Assert.IsFalse(IsTargeting(aimed), "not before it's cast");
            Assert.IsTrue(Activate(abilities, aimed).Started, "the aimed cast starts");
            await Frames.Step(2);
            Assert.IsTrue(IsTargeting(aimed), "while it waits for its click");
            abilities.Inject(aimed.Index, InterruptSequenceInjection.Instance);
            await Frames.Until(() => !aimed.IsActive, 3, "cancelled");
            Assert.IsFalse(IsTargeting(aimed), "and not once it's over");

            var prompt = typeof(AbstractTargetingAbilityTask).GetProperty("Prompt");
            Assert.IsNotNull(prompt, "a targeting task has words to ask with");
            foreach (var task in new AbstractTargetingAbilityTask[]
                     {
                         new SelectPositionTargetTask(), new SelectDirectionTargetTask(), new SelectGasTargetTask(),
                         new SelectGasOrPositionTargetTask(), new SelectGasTargetInRangeTask(),
                     })
                Assert.That(prompt.GetValue(task) as string, Is.Not.Null.And.Not.Empty, $"{task.GetType().Name} asks in words");
            Assert.IsNull(prompt.GetValue(new AutoTargetSelfTask()), "one that doesn't wait asks nothing");
        });
    }
}
