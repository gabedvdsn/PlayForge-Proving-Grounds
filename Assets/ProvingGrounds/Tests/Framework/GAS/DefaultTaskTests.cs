using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The tasks a framework should have (FN-24, Decision 83): waits for an event, a tag or an attribute, giving up after
    /// their timeout; sending an event, granting tags while a stage runs and cancelling abilities by tag; and the Demo's
    /// Dash, Blink and Consume Item, in the framework now. Area Effect and Apply To Caster are AreaTests'.
    /// </summary>
    public class DefaultTaskTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag marked;

        [SetUp]
        public void Names()
        {
            health = Make.Attribute("Health");
            marked = Make.NewTag("Test.FN24.Marked");
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name) =>
            World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        // A lasting effect that marks the one it's on
        private GameplayEffect Mark() =>
            Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { marked } });

        private bool Marked(IGameplayAbilitySystem gas) => gas.GetTagCache().GetWeight(marked) > 0;

        private static AbilitySpecContainer Give(IGameplayAbilitySystem caster, Ability ability)
        {
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(ability, 1, out int index), $"{ability.GetName()} is known");
            abilities.TryGetAbilityContainer(index, out var container);
            return container;
        }

        private static void Cast(IGameplayAbilitySystem caster, AbilitySpecContainer container, ProcessDataPacket data = null)
        {
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, data)), $"{container.Spec.Base.GetName()} activates");
        }

        // A stage that waits, then one that marks its caster, implicit targeting making the caster its target: the mark says
        // the wait is over
        private Ability WaitsThenMarks(string name, AbstractAbilityTask wait) =>
            Make.Ability(name, Make.Stage(wait), Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Mark() } }));

        [UnityTest]
        public IEnumerator FN_24_A_wait_for_an_event_holds_its_stage_until_it_comes([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var hit = Make.NewTag("Test.FN24.Hit");
            var combo = Give(caster, WaitsThenMarks("Combo", new WaitForEventTask { Event = hit }));
            await Frames.Step();
            Assert.IsFalse(caster.GetAbilitySystem().ListensFor(hit), "nothing listens for the event");

            Cast(caster, combo);
            await Frames.Step(3);
            Assert.IsFalse(Marked(caster), "it waits for the event");
            Assert.IsTrue(caster.GetAbilitySystem().ListensFor(hit), "and its entity listens for it while it waits, as the core sends only what's listened for");
            caster.SendGameplayEvent(hit);
            await Frames.Until(() => Marked(caster), 10, "the event ends the wait");
            await Frames.Step();
            Assert.IsFalse(caster.GetAbilitySystem().ListensFor(hit), "and its entity listens no more");
        });

        [UnityTest]
        public IEnumerator FN_24_A_wait_gives_up_after_its_timeout([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var patience = Give(caster, WaitsThenMarks("Patience", new WaitForEventTask { Event = Make.NewTag("Test.FN24.Never"), Timeout = 0.05f }));
            await Frames.Step();
            Cast(caster, patience);
            await Frames.Until(() => Marked(caster), 60, "after its timeout, the next stage runs");
        });

        [UnityTest]
        public IEnumerator FN_24_A_wait_for_a_tag_holds_until_its_caster_gains_it([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var ready = Make.NewTag("Test.FN24.Ready");
            var readying = Give(caster, WaitsThenMarks("Ready Up", new WaitForTagTask { Tag = ready, Until = ETagWait.Gained }));
            await Frames.Step();
            Cast(caster, readying);
            await Frames.Step(3);
            Assert.IsFalse(Marked(caster), "it waits for the tag");
            caster.AddLooseTag(ready);
            await Frames.Until(() => Marked(caster), 10, "gained, the wait is over");
        });

        [UnityTest]
        public IEnumerator FN_24_A_wait_for_an_attribute_holds_until_it_crosses_its_value([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var berserk = Give(caster, WaitsThenMarks("Berserk",
                new WaitForAttributeTask { Attribute = health, Comparison = EComparisonOperator.LessOrEqual, Value = 50f }));
            await Frames.Step();
            Cast(caster, berserk);
            await Frames.Step(3);
            Assert.IsFalse(Marked(caster), "at 100 Health it waits");
            Assert.IsTrue(World.Apply(caster, caster, Make.Effect("Wound", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -60f })));
            await Frames.Until(() => Marked(caster), 10, "at 40, under its 50, the wait is over");
        });

        [UnityTest]
        public IEnumerator FN_24_A_sent_event_sets_off_an_abilitys_trigger([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var signal = Make.NewTag("Test.FN24.Signal");
            var answer = Make.Ability("Answer", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Mark() } }));
            answer.Definition.Triggers.Add(new AbilityTrigger { Source = EAbilityTriggerSource.GameplayEvent, Tag = signal });
            answer.Definition.ActivationPolicy = EAbilityActivationPolicyExtended.AlwaysActivate;
            Give(caster, answer);
            var call = Give(caster, Make.Ability("Call", Make.Stage(new SendEventTask { Event = signal, To = EEventRecipient.Caster })));
            await Frames.Step();
            Cast(caster, call);
            await Frames.Until(() => Marked(caster), 10, "its event sets off the answering ability");
        });

        [UnityTest]
        public IEnumerator FN_24_Granted_tags_last_while_their_stage_runs([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var charging = Make.NewTag("Test.FN24.Charging");
            var charge = Give(caster, Make.Ability("Charge", Make.Stage(
                new GrantTagsTask { Granted = new List<Tag> { charging } },
                new ProbeAbilityTask(World, "Charge Hold", frames: 6))));
            await Frames.Step();
            Cast(caster, charge);
            await Frames.Step(2);
            Assert.That(caster.GetTagCache().GetWeight(charging), Is.GreaterThan(0), "while its stage runs, its caster has the tag");
            await Frames.Until(() => !charge.IsActive, 30, "the stage ends");
            await Frames.Step();
            Assert.That(caster.GetTagCache().GetWeight(charging), Is.LessThanOrEqualTo(0), "and the tag goes with it");
        });

        [UnityTest]
        public IEnumerator FN_24_A_cancel_interrupts_running_abilities_by_tag([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var casting = Make.NewTag("Test.FN24.Cast");
            var spell = Make.Ability("Spell", Make.Stage(new ProbeAbilityTask(World, "Spell Hold", frames: 60)));
            spell.Tags.ContextTags.Add(casting);
            var spellRun = Give(caster, spell);
            var dodge = Make.Ability("Dodge", Make.Stage(new CancelAbilitiesTask { Cancels = new List<Tag> { casting } }));
            dodge.Definition.ActivationPolicy = EAbilityActivationPolicyExtended.AlwaysActivate;
            var dodgeRun = Give(caster, dodge);
            await Frames.Step();
            Cast(caster, spellRun);
            await Frames.Step(2);
            Assert.IsTrue(spellRun.IsActive, "the spell runs");
            Cast(caster, dodgeRun);
            await Frames.Until(() => !spellRun.IsActive, 10, "the dodge cancels it, by its tag");
        });

        [UnityTest]
        public IEnumerator FN_24_Consume_item_uses_its_items_up_and_says_so([Values] Hub hub) => Live(async () =>
        {
            var caster = Fighter(hub, "Caster");
            var salve = Make.Item("Salve");
            salve.Definition.MaxStackSize = 2;
            var items = caster.GetItemSystem();
            Assert.AreEqual(2, items.GiveItem(salve, 2), "two salves, in one stack");
            var consumed = new List<(Item, int)>();
            items.Callbacks.OnItemConsumed += (item, count) => consumed.Add((item, count));
            var use = Give(caster, Make.Ability("Use Salve", Make.Stage(new ConsumeItemTask { Item = salve })));
            await Frames.Step();

            Cast(caster, use);
            await Frames.Until(() => !use.IsActive, 10, "the use ends");
            Assert.That(consumed, Is.EqualTo(new[] { (salve, 1) }), "one used up, which its item system says");
            Assert.IsTrue(items.HasItem(salve), "one left");
            Cast(caster, use);
            await Frames.Until(() => !use.IsActive, 10, "the use ends again");
            Assert.IsFalse(items.HasItem(salve), "and the last used up, the item goes");
        });

        // A mover of a game's own on the caster's object, which hears a Dash hold it and a Blink stop it
        private sealed class Mover : MonoBehaviour, IForgeMover
        {
            public int Holds, Releases, Stops;

            public void Hold(bool held)
            {
                if (held) Holds++;
                else Releases++;
            }

            public void Stop() => Stops++;
        }

        // An ability whose targeting takes the target its activation's data names, and the data that names it
        private static ProcessDataPacket Aiming(Ability ability, ITarget target)
        {
            ability.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, target);
            return data;
        }

        [UnityTest]
        public IEnumerator FN_24_A_dash_moves_its_caster_toward_its_target() => Live(async () =>
        {
            var caster = World.MonoEntity(name: "Caster");
            caster.transform.position = Vector3.zero;
            var target = World.MonoEntity(name: "Target");
            target.transform.position = new Vector3(0f, 0f, 10f);
            var mover = caster.gameObject.AddComponent<Mover>();
            var tumble = Make.Ability("Tumble", Make.Stage(new DashTask { Distance = 4f, Duration = 0.1f }));
            var data = Aiming(tumble, target);
            var run = Give(caster, tumble);
            await Frames.Step();

            Cast(caster, run, data);
            await Frames.Until(() => !run.IsActive, 60, "the dash ends");
            Assert.AreEqual(4f, caster.transform.position.z, 0.05f, "four toward its target, its distance");
            Assert.AreEqual(1, mover.Holds, "holding its mover while it moved");
            Assert.AreEqual(1, mover.Releases, "and letting it go after");
        });

        [UnityTest]
        public IEnumerator FN_24_A_blink_puts_its_caster_at_its_target_within_its_reach() => Live(async () =>
        {
            var caster = World.MonoEntity(name: "Caster");
            caster.transform.position = Vector3.zero;
            var target = World.MonoEntity(name: "Target");
            target.transform.position = new Vector3(0f, 0f, 10f);
            var mover = caster.gameObject.AddComponent<Mover>();
            var blink = Make.Ability("Blink", Make.Stage(new BlinkTask { Reach = 6f }));
            var data = Aiming(blink, target);
            var run = Give(caster, blink);
            await Frames.Step();

            Cast(caster, run, data);
            await Frames.Until(() => !run.IsActive, 10, "the blink ends");
            Assert.AreEqual(6f, caster.transform.position.z, 0.05f, "as far as its reach toward its target");
            Assert.AreEqual(1, mover.Stops, "and its mover stopped after");
        });
    }
}
