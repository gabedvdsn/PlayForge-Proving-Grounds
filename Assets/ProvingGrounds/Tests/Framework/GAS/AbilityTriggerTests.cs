using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Ability triggers (FEAT-4): an ability activated only when asked for. An ability's trigger sets it off on its own:
    /// a gameplay event sent to the entity under the trigger's tag or one under it, or the entity gaining the tag.
    /// </summary>
    public class AbilityTriggerTests : ProvingGroundsTest
    {
        private static void Trigger(Ability ability, EAbilityTriggerSource source, Tag tag) =>
            ability.Definition.Triggers.Add(new AbilityTrigger { Source = source, Tag = tag });

        private static int Send(IGameplayAbilitySystem gas, Tag tag, ProcessDataPacket data = null) => gas.SendGameplayEvent(tag, data);

        /// <summary>
        /// A gameplay event activates each ability the entity knows whose trigger is the event's tag or one above it, and
        /// no other: a hit under Event.Hit sets off a parry, and a heal doesn't (FEAT-4).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_4_A_gameplay_event_activates_the_abilities_it_triggers([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var hit = Make.NewTag("Event.Hit");
            var parry = Make.Ability("Parry", Make.Stage(new ProbeAbilityTask(World, "Parry", frames: 1)));
            Trigger(parry, EAbilityTriggerSource.GameplayEvent, hit);
            var idle = Make.Ability("Idle", Make.Stage(new ProbeAbilityTask(World, "Idle", frames: 1)));
            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(parry, 1, out _), "the entity knows Parry");
            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(idle, 1, out _), "and an ability no event triggers");

            Assert.AreEqual(1, Send(gas, hit.Child("Critical")), "a critical hit sets off one ability");
            await Frames.Until(() => !gas.GetAbilitySystem().IsExecuting, 10, "which ends");
            Assert.AreEqual(1, Ledger.Prepares("Parry"), "the parry");
            Assert.AreEqual(0, Ledger.Prepares("Idle"), "and not the other");

            Assert.AreEqual(0, Send(gas, Make.NewTag("Event.Heal")), "a heal sets off none");
            await Frames.Step();
            Assert.AreEqual(1, Ledger.Prepares("Parry"));
        });

        /// <summary>
        /// The entity gaining a tag activates each ability whose trigger is the tag or one above it, once as the tag comes:
        /// more of it, while it's held, sets off nothing, and gaining it again after it went does (FEAT-4).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_4_A_tag_added_activates_the_abilities_it_triggers([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var enraged = Make.NewTag("State.Enraged");
            var rage = Make.Ability("Rage", Make.Stage(new ProbeAbilityTask(World, "Rage", frames: 1)));
            Trigger(rage, EAbilityTriggerSource.TagAdded, enraged);
            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(rage, 1, out _), "the entity knows Rage");

            gas.AddLooseTag(enraged);
            await Frames.Until(() => Ledger.Cleans("Rage") == 1, 10, "enraged, it rages");
            gas.AddLooseTag(enraged);
            await Frames.Step(3);
            Assert.AreEqual(1, Ledger.Prepares("Rage"), "more rage, still held, sets off nothing");

            gas.RemoveLooseTag(enraged);
            gas.RemoveLooseTag(enraged);
            await Frames.Step();
            gas.AddLooseTag(enraged);
            await Frames.Until(() => Ledger.Prepares("Rage") == 2, 10, "enraged again, it rages again");
        });
    }
}
