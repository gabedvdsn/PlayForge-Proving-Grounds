using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-6: On Hit on items, abilities and effects. The effects its holder's hits apply, or a kill's: only losses to its
    /// attribute that the holder deals another, of its impact types where it names any, landing on the one hit or the
    /// holder; an item's while it's equipped, an ability's while it's known, an effect's while it lasts.
    /// </summary>
    public class OnHitTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag attack, bleed, mark;

        private sealed class SteppingClock : IForgeClock
        {
            public float Step;
            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        private IForgeClock previousClock;
        private bool clockSet;

        [SetUp]
        public void Tags()
        {
            health = Make.Attribute("Health");
            attack = Make.NewTag("Test.TW6.Attack");
            bleed = Make.NewTag("Test.TW6.Bleed");
            mark = Make.NewTag("Test.TW6.Marked");
        }

        [TearDown]
        public void PutBackTime()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            clockSet = false;
        }

        private IGameplayAbilitySystem Fighter(Hub hub, string name, float health = 100f) =>
            World.Entity(hub, Make.Set(Make.Element(this.health, health, health, EAttributeOverflowPolicy.ZeroToBase)), name);

        private GameplayEffect Blow(float amount, Tag type)
        {
            var fx = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });
            fx.ImpactSpecification.ImpactTypes.Add(type);
            return fx;
        }

        private GameplayEffect Marking() =>
            Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { mark } });

        private OnHitEntry Marks(EOnHitTo to = EOnHitTo.Target, bool onlyOnKill = false) => new()
        {
            Attribute = health, Effects = new List<GameplayEffect> { Marking() }, To = to, OnlyOnKill = onlyOnKill,
            ImpactTypes = new List<Tag> { attack },
        };

        private bool Marked(IGameplayAbilitySystem gas) => gas.GetTagCache().GetWeight(mark) > 0;

        /// <summary>An item's On Hit marks the one its holder's attack hits, and not the one a bleed hits.</summary>
        [UnityTest]
        public IEnumerator TW_6_An_items_on_hit_marks_the_one_its_attack_hits([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var dagger = Make.Item("Dagger");
            dagger.OnHit.Add(Marks());
            await Frames.Step();
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(dagger, out _), "the dagger is held");

            Assert.IsTrue(World.Apply(holder, first, Blow(10f, attack)), "an attack");
            Assert.IsTrue(World.Apply(holder, second, Blow(10f, bleed)), "a bleed");
            await Frames.Step(3);
            Assert.IsTrue(Marked(first), "the attack's target is marked");
            Assert.IsFalse(Marked(second), "the bleed's isn't");
            Assert.IsFalse(Marked(holder), "nor the holder");
        });

        /// <summary>An On Kill lands on the holder, on the blow that kills alone.</summary>
        [UnityTest]
        public IEnumerator TW_6_An_on_kill_lands_on_the_holder([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var victim = Fighter(hub, "Victim", health: 30f);
            var brooch = Make.Item("Brooch");
            brooch.OnHit.Add(Marks(EOnHitTo.Holder, onlyOnKill: true));
            await Frames.Step();
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(brooch, out _));

            Assert.IsTrue(World.Apply(holder, victim, Blow(10f, attack)));
            await Frames.Step(3);
            Assert.IsFalse(Marked(holder), "a blow that doesn't kill gives nothing");
            Assert.IsTrue(World.Apply(holder, victim, Blow(50f, attack)));
            await Frames.Step(3);
            Assert.IsTrue(Marked(holder), "the one that kills marks the holder");
        });

        /// <summary>An ability's On Hit holds while it's known, and goes with it.</summary>
        [UnityTest]
        public IEnumerator TW_6_An_abilitys_on_hit_holds_while_it_is_known([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var passive = Make.Ability("Passive", Make.Stage());
            passive.OnHit.Add(Marks());
            await Frames.Step();
            var abilities = holder.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(passive, 1, out int slot), "the passive is known");

            Assert.IsTrue(World.Apply(holder, first, Blow(10f, attack)));
            await Frames.Step(3);
            Assert.IsTrue(Marked(first), "its hits mark");

            Assert.IsTrue(abilities.RemoveAbilityAt(slot, passive), "forgotten");
            Assert.IsTrue(World.Apply(holder, second, Blow(10f, attack)));
            await Frames.Step(3);
            Assert.IsFalse(Marked(second), "its hits mark no more");
        });

        /// <summary>An effect's On Hit holds on the one it's on while it lasts, and goes as it ends.</summary>
        [UnityTest]
        public IEnumerator TW_6_An_effects_on_hit_holds_while_it_lasts([Values] Hub hub) => Live(async () =>
        {
            previousClock = ForgeTime.Use(new SteppingClock { Step = 0.5f });
            clockSet = true;
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var poison = Make.Effect("Poisoned Blade", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 2f });
            poison.OnHit.Add(Marks());
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, holder, poison), "the blade is poisoned");
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, first, Blow(10f, attack)));
            await Frames.Step(2);
            Assert.IsTrue(Marked(first), "its hits mark while it lasts");

            await Frames.Until(() => !holder.TryGetEffectContainer(poison, out _), 12, "the poison wears off");
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, second, Blow(10f, attack)));
            await Frames.Step(2);
            Assert.IsFalse(Marked(second), "and its hits mark no more");
        });

        /// <summary>
        /// An effect another under its tag replaces takes its On Hit with it. The replacement gives the container the new
        /// effect's spec before what the old one lent is taken back, so what it gave is kept apart.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_6_An_effect_replaced_by_another_takes_its_on_hit_with_it([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var blade = Make.NewTag("Test.TW6.Blade");
            var marking = Make.Effect("Marking Blade", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Reapply = EEffectReApplicationPolicy.ReplaceOldContainer });
            var plain = Make.Effect("Plain Blade", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Reapply = EEffectReApplicationPolicy.ReplaceOldContainer });
            marking.Tags.AssetTag = blade;
            plain.Tags.AssetTag = blade;
            marking.OnHit.Add(Marks());
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, holder, marking), "the marking blade");
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, first, Blow(10f, attack)));
            await Frames.Step(2);
            Assert.IsTrue(Marked(first), "its hits mark");

            Assert.IsTrue(World.Apply(holder, holder, plain), "the plain blade, in its place");
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, second, Blow(10f, attack)));
            await Frames.Step(2);
            Assert.IsFalse(Marked(second), "whose hits mark not");
        });

        /// <summary>
        /// A load puts back an effect's On Hit once, and a save from before the effect takes it off: a load clears the
        /// shelf with nothing taken back, and the On Hits it gave were the ability system's to keep.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_6_A_load_puts_back_an_effects_on_hit_once([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var first = Fighter(hub, "First");
            var second = Fighter(hub, "Second");
            var poison = Make.Effect("Poisoned Blade", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            poison.OnHit.Add(Marks());
            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            catalog.Add("health", health);
            catalog.Add("poison", poison);
            await Frames.Step();
            var before = ForgeSave.Capture(holder, catalog);

            Assert.IsTrue(World.Apply(holder, holder, poison), "the blade is poisoned");
            await Frames.Step();
            Assert.IsTrue(ForgeSave.Restore(holder, ForgeSave.Capture(holder, catalog), catalog), "a save with it on loads");
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, first, Blow(10f, attack)));
            await Frames.Step(2);
            Assert.AreEqual(1, first.GetTagCache().GetWeight(mark), "a hit marks once");

            Assert.IsTrue(ForgeSave.Restore(holder, before, catalog), "a save from before it loads");
            await Frames.Step();
            Assert.IsTrue(World.Apply(holder, second, Blow(10f, attack)));
            await Frames.Step(2);
            Assert.IsFalse(Marked(second), "and its hits mark no more");
        });

        /// <summary>An entry's own effects set it off no more: a shock on any hit shocks once, not on and on.</summary>
        [UnityTest]
        public IEnumerator TW_6_An_on_hits_own_effects_set_it_off_no_more([Values] Hub hub) => Live(async () =>
        {
            var holder = Fighter(hub, "Holder");
            var victim = Fighter(hub, "Victim");
            var shock = Make.Effect("Shock", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -5f });
            var rod = Make.Item("Rod");
            rod.OnHit.Add(new OnHitEntry { Attribute = health, Effects = new List<GameplayEffect> { shock } });
            await Frames.Step();
            Assert.That(holder.GetItemSystem().GiveAndEquipItem(rod, out _));

            Assert.IsTrue(World.Apply(holder, victim, Blow(10f, attack)));
            await Frames.Step(4);
            Assert.AreEqual(85f, victim.TryGetAttributeValue(health, out var left) ? left.CurrentValue : float.NaN, 0.001f, "the blow's 10 and one shock's 5");
        });
    }
}
