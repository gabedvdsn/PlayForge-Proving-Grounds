using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-20, Decision 59: a transfer moves an amount from one attribute to another, each side its own, as Essence Shift
    /// steals the target's stats and Attribute Shift moves Agility to Strength; only what the giver has moves; a Continuous
    /// one gives back as the effect ends, as its modifiers do, and an OnApply or Periodic one stays. And an ability that
    /// toggles an effect: on as it's used, paying its cost, off as it's used again, starting its cooldown.
    /// </summary>
    public class TransferToggleTests : ProvingGroundsTest
    {
        private ForgeAttribute strength, agility, mana;
        private Tag shielded;

        [SetUp]
        public void Attributes()
        {
            strength = Make.Attribute("Strength");
            agility = Make.Attribute("Agility");
            mana = Make.Attribute("Mana");
            shielded = Make.NewTag("Test.TW20.Shielded");
        }

        private IGameplayAbilitySystem Hero(Hub hub, string name, float strengthValue = 20f, float agilityValue = 20f, float manaValue = 50f) =>
            World.Entity(hub, Make.Set(
                Make.Element(strength, strengthValue, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(agility, agilityValue, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(mana, manaValue, 100f, EAttributeOverflowPolicy.ZeroToBase)), name);

        private static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static float Base(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.BaseValue : float.NaN;

        private GameplayEffect Moving(string name, EEffectDurationPolicy policy, float duration, AttributeTransfer transfer, int ticks = 0)
        {
            var effect = Make.Effect(name, new EffectConfig { Policy = policy, Duration = duration, PeriodicTicks = ticks > 0, Ticks = ticks });
            effect.Transfers.Add(transfer);
            return effect;
        }

        private AttributeTransfer Steal(EImpactDelivery delivery, ForgeAttribute attribute, float amount) => new()
        {
            Delivery = delivery, From = NumberSide.Target, FromAttribute = attribute, To = NumberSide.Caster, ToAttribute = attribute, Amount = amount,
        };

        /// <summary>A steal as it lands moves what the target has of the amount to the caster, and no more.</summary>
        [UnityTest]
        public IEnumerator TW_20_A_transfer_moves_what_the_giver_has([Values] Hub hub) => Live(async () =>
        {
            var caster = Hero(hub, "Caster", strengthValue: 10f);
            var target = Hero(hub, "Target");
            var poor = Hero(hub, "Poor", strengthValue: 3f);
            var steal = Moving("Steal", EEffectDurationPolicy.Instant, 0f, Steal(EImpactDelivery.OnApply, strength, 5f));
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, steal));
            await Frames.Step();
            Assert.AreEqual(15f, Value(target, strength), 0.01f, "the target gives 5");
            Assert.AreEqual(15f, Value(caster, strength), 0.01f, "and the caster gets 5");

            Assert.IsTrue(World.Apply(caster, poor, steal));
            await Frames.Step();
            Assert.AreEqual(0f, Value(poor, strength), 0.01f, "one with 3 gives its 3");
            Assert.AreEqual(18f, Value(caster, strength), 0.01f, "and the caster gets 3, not 5");
        });

        /// <summary>
        /// A shift between one entity's attributes, each tick, as Attribute Shift moves Agility to Strength: both values of
        /// each, as a stat's, for good.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_20_A_shift_moves_between_one_entitys_attributes([Values] Hub hub) => Live(async () =>
        {
            var hero = Hero(hub, "Hero");
            var shift = Moving("Shift", EEffectDurationPolicy.Durational, 5f, new AttributeTransfer
            {
                Delivery = EImpactDelivery.Periodic, From = NumberSide.Target, FromAttribute = agility,
                To = NumberSide.Target, ToAttribute = strength, Slot = EAttributeTarget.CurrentAndBase, Amount = 1f,
            }, ticks: 5);
            await Frames.Step();

            Assert.IsTrue(World.Apply(hero, hero, shift));
            await Frames.Until(() => Value(hero, agility) <= 15.01f, 600, "five ticks move five");
            Assert.AreEqual(25f, Value(hero, strength), 0.01f, "its Strength has what its Agility gave");
            Assert.AreEqual(40f, Value(hero, strength) + Value(hero, agility), 0.01f, "and nothing was made");
            Assert.AreEqual(95f, Base(hero, agility), 0.01f, "its Agility's base gave as much");
            Assert.AreEqual(105f, Base(hero, strength), 0.01f, "and its Strength's base got it");

            await Frames.Until(() => hero.GetEffectSystem().Shelf.Count == 0, 120, "the shift ends");
            await Frames.Step(2);
            Assert.AreEqual(25f, Value(hero, strength), 0.01f, "and what moved stays");
        });

        /// <summary>A Continuous steal holds while its effect lasts and gives back as it ends.</summary>
        [UnityTest]
        public IEnumerator TW_20_A_continuous_transfer_gives_back_as_it_ends([Values] Hub hub) => Live(async () =>
        {
            var caster = Hero(hub, "Caster");
            var target = Hero(hub, "Target");
            var essence = Moving("Essence Shift", EEffectDurationPolicy.Durational, 0.5f, Steal(EImpactDelivery.Continuous, agility, 4f));
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, essence));
            await Frames.Step(2);
            Assert.AreEqual(16f, Value(target, agility), 0.01f, "the target is 4 down while it lasts");
            Assert.AreEqual(24f, Value(caster, agility), 0.01f, "and the caster 4 up");

            await Frames.Until(() => Value(target, agility) >= 19.99f, 120, "as it ends, the target has its 4 back");
            Assert.AreEqual(20f, Value(caster, agility), 0.01f, "and the caster gives them up");
        });

        // The game's own IDs for its entities, as a save names an effect's caster by
        private sealed class EntityIds : IForgeEntityIds
        {
            private readonly Dictionary<string, IGameplayAbilitySystem> byId = new();

            public EntityIds(params (string Id, IGameplayAbilitySystem Entity)[] entities)
            {
                foreach (var (id, entity) in entities) byId[id] = entity;
            }

            public string IdOf(IGameplayAbilitySystem entity)
            {
                foreach (var pair in byId)
                    if (ReferenceEquals(pair.Value, entity)) return pair.Key;
                return null;
            }

            public IGameplayAbilitySystem Find(string id) => id != null && byId.TryGetValue(id, out var entity) ? entity : null;
        }

        /// <summary>A load puts a lasting steal back as it was saved: each side down or up by it once, and back as it ends.</summary>
        [UnityTest]
        public IEnumerator TW_20_A_load_puts_a_lasting_transfer_back([Values] Hub hub) => Live(async () =>
        {
            var caster = Hero(hub, "Caster");
            var target = Hero(hub, "Target");
            var essence = Moving("Essence Shift", EEffectDurationPolicy.Infinite, 0f, Steal(EImpactDelivery.Continuous, agility, 4f));
            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            Assert.IsTrue(catalog.Add("strength", strength) && catalog.Add("agility", agility) && catalog.Add("mana", mana)
                          && catalog.Add("essence", essence), "the catalog names them");
            var ids = new EntityIds(("caster", caster), ("target", target));
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, essence));
            await Frames.Step(2);
            Assert.IsTrue(ForgeSave.Restore(target, ForgeSave.Capture(target, catalog, ids), catalog, ids), "the target's save loads");
            await Frames.Step(2);
            Assert.AreEqual(16f, Value(target, agility), 0.01f, "the target is 4 down, once");
            Assert.AreEqual(24f, Value(caster, agility), 0.01f, "and the caster 4 up, once");

            Assert.IsTrue(target.RemoveGameplayEffect(essence.Tags.AssetTag));
            await Frames.Step(2);
            Assert.AreEqual(20f, Value(target, agility), 0.01f, "as it ends, the target has its own back");
            Assert.AreEqual(20f, Value(caster, agility), 0.01f, "and the caster gives them up");
        });

        /// <summary>A target that goes while a Continuous steal holds on it gives its caster's back, as an aura's holder does.</summary>
        [UnityTest]
        public IEnumerator TW_20_A_lasting_transfer_gives_back_as_its_target_goes() => Live(async () =>
        {
            var caster = Hero(Hub.Mono, "Caster");
            var target = Hero(Hub.Mono, "Target");
            var essence = Moving("Essence Shift", EEffectDurationPolicy.Infinite, 0f, Steal(EImpactDelivery.Continuous, agility, 4f));
            await Frames.Step();

            Assert.IsTrue(World.Apply(caster, target, essence));
            await Frames.Step(2);
            Assert.AreEqual(24f, Value(caster, agility), 0.01f, "the caster holds 4");

            Object.Destroy(((Component)target).gameObject);
            await Frames.Step(3);
            Assert.AreEqual(20f, Value(caster, agility), 0.01f, "and gives them up as its target goes");
        });

        /// <summary>
        /// A toggle puts its effect on as it's used, paying its cost, and takes it off as it's used again, paying nothing and
        /// asking no cost, and starting its cooldown.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_20_A_toggle_turns_its_effect_on_and_off([Values] Hub hub) => Live(async () =>
        {
            var hero = Hero(hub, "Hero", manaValue: 15f);
            var shield = Make.Effect("Mana Shield", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { shielded } });
            var toggle = Make.Ability("Mana Shield", Make.Stage(new ProbeAbilityTask(World, "Mana Shield")));
            toggle.Toggles = shield;
            toggle.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = 10f });
            toggle.Cooldown = Make.Effect("Mana Shield Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 0.3f });
            await Frames.Step();
            var abilities = hero.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(toggle, 1, out _));

            bool On() => hero.GetTagCache().GetWeight(shielded) > 0;
            bool Cooling() => hero.GetTagCache().GetWeight(toggle.Cooldown.Tags.AssetTag) > 0;

            Assert.IsTrue(abilities.TryActivateAbility(toggle, out _), "it turns on");
            await Frames.Step(2);
            Assert.IsTrue(On(), "its effect is on");
            Assert.AreEqual(5f, Value(hero, mana), 0.01f, "paying its cost");
            Assert.IsFalse(Cooling(), "and no cooldown yet");

            Assert.IsTrue(abilities.TryActivateAbility(toggle, out _), "it turns off, though it couldn't pay its cost again");
            await Frames.Step(2);
            Assert.IsFalse(On(), "its effect is off");
            Assert.AreEqual(5f, Value(hero, mana), 0.01f, "paying nothing");
            Assert.IsTrue(Cooling(), "and its cooldown runs");
            Assert.IsFalse(abilities.TryActivateAbility(toggle, out var refusal), "so it can't turn on at once");
            Assert.AreEqual(EActivationRefusal.Cooldown, refusal.Reason);

            await Frames.Until(() => !Cooling(), 120, "the cooldown ends");
            Assert.IsFalse(abilities.TryActivateAbility(toggle, out refusal), "and with 5 Mana it can't pay to turn on");
            Assert.AreEqual(EActivationRefusal.Cost, refusal.Reason);

            hero.TryModifyAttribute(mana, SourcedModifiedAttributeValue.GenerateSimple(hero, mana, 20f, 0f));
            Assert.IsTrue(abilities.TryActivateAbility(toggle, out _), "with Mana again it turns on");
            await Frames.Step(2);
            Assert.IsTrue(On());
            Assert.AreEqual(15f, Value(hero, mana), 0.01f, "paying again");
        });
    }
}
