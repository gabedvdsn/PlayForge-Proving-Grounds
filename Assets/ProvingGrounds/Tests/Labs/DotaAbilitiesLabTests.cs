using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Dota lab's second bot: abilities, items and an entity from Dota 2, authored as assets beside the first lab's
    /// effects. Lina's Laguna Blade, Sven's God's Strength and Storm Hammer, Axe's Counter Helix, Black King Bar,
    /// Desolator, Mekansm, and Sven himself. Where the lab needed what PlayForge doesn't have, a Lab Kit worker stands in,
    /// and Docs/TOOLING_WISHLIST.md names it.
    /// </summary>
    [Category("Lab")]
    public class DotaAbilitiesLabTests : LabTest
    {
        protected override string Root => "Assets/ProvingGrounds/Dota";

        private const string Radiant = "Team.Dota.Radiant";
        private const string Dire = "Team.Dota.Dire";

        private ForgeAttribute Health => Load<ForgeAttribute>("Attributes/Health");
        private ForgeAttribute Mana => Load<ForgeAttribute>("Attributes/Mana");
        private ForgeAttribute AttackDamage => Load<ForgeAttribute>("Attributes/Attack Damage");
        private ForgeAttribute Armor => Load<ForgeAttribute>("Attributes/Armor");
        private ForgeAttribute MagicResistance => Load<ForgeAttribute>("Attributes/Magic Resistance");

        private Ability AbilityNamed(string name) => Load<Ability>($"Abilities/{name}");
        private Item ItemNamed(string name) => Load<Item>($"Items/{name}");
        private GameplayEffect EffectAt(string path) => Load<GameplayEffect>($"Effects/{path}");

        // A hero of a team, with 1000 health and mana unless it says
        private IGameplayAbilitySystem Hero(Hub hub, string name, string team, float health = 1000f, float healthNow = -1f,
            float attackDamage = 100f, float armor = 0f, float magicResistance = 0.25f) =>
            Entity(hub, name, team,
                Pool(Health, healthNow < 0f ? health : healthNow, health),
                Pool(Mana, 1000f, 1000f),
                Stat(AttackDamage, attackDamage),
                Stat(Armor, armor),
                Stat(MagicResistance, magicResistance));

        /// <summary>
        /// Laguna Blade: a bolt a quarter of a second after the cast, for 450, 600 or 750 magical damage by its level, less
        /// the target's magic resistance; it costs 280, 420 or 680 mana. The numbers by level are tables, read at the
        /// ability's level.
        /// </summary>
        [UnityTest]
        public IEnumerator Laguna_Blade_strikes_a_moment_after_the_cast_by_its_level([Values] Hub hub, [Values(1, 3)] int level) => Live(async () =>
        {
            Clock(0.05f);
            var lina = Hero(hub, "Lina", Dire);
            var target = Hero(hub, "Target", Radiant, magicResistance: 0.25f);
            await Frames.Step();

            Assert.IsTrue(Cast(lina, AbilityNamed("Laguna Blade"), target, level), "Lina casts");
            await Frames.Step(2);
            Assert.AreEqual(1000f, Value(target, Health), 0.01f, "a tenth of a second in, the bolt hasn't struck");

            await Frames.Until(() => Value(target, Health) < 1000f, 30, "the bolt strikes");
            float damage = level == 1 ? 450f : 750f;
            Assert.AreEqual(1000f - damage * 0.75f, Value(target, Health), 0.01f, $"{damage} at level {level}, less 25% magic resistance");
            Assert.AreEqual(1000f - (level == 1 ? 280f : 680f), Value(lina, Mana), 0.01f, "and its cost by its level");
        });

        /// <summary>Laguna Blade can't be cast again while it cools down: one bolt, and its mana spent once.</summary>
        [UnityTest]
        public IEnumerator Laguna_Blade_waits_for_its_cooldown([Values] Hub hub) => Live(async () =>
        {
            Clock(0.05f);
            var lina = Hero(hub, "Lina", Dire);
            var target = Hero(hub, "Target", Radiant);
            await Frames.Step();

            Assert.That(lina.GetAbilitySystem().GiveAbility(AbilityNamed("Laguna Blade"), 1, out int laguna), "Lina has Laguna Blade");
            Assert.IsTrue(Cast(lina, laguna, target), "the first cast");
            await Frames.Until(() => Value(target, Health) < 1000f, 30, "the bolt strikes");

            Assert.IsFalse(Cast(lina, laguna, target), "a second, while it cools down, is refused");
            await Frames.Step(10);
            Assert.AreEqual(1000f - 337.5f, Value(target, Health), 0.01f, "one bolt");
            Assert.AreEqual(1000f - 280f, Value(lina, Mana), 0.01f, "its mana spent once");
        });

        /// <summary>
        /// God's Strength: 100% more Attack Damage for 25 seconds, a bonus that adds to the others in one pool, as Dota's
        /// bonuses do: with another 50%, 100 × (1 + 1 + 0.5) is 250, not 100 × 2 × 1.5. The pooling is the effect's
        /// choice, Additive Bonus; nothing on the attribute says how its bonuses accumulate (see the wishlist).
        /// </summary>
        [UnityTest]
        public IEnumerator Gods_Strength_adds_to_his_other_bonuses([Values] Hub hub) => Live(async () =>
        {
            Clock(0.5f);
            var sven = Hero(hub, "Sven", Radiant, attackDamage: 100f);
            var godsStrength = EffectAt("Sven/God's Strength");
            await Frames.Step();

            Assert.IsTrue(Cast(sven, AbilityNamed("God's Strength")), "Sven casts God's Strength on himself");
            await Frames.Until(() => Has(sven, godsStrength), 10, "it lands on him");
            Assert.AreEqual(200f, Value(sven, AttackDamage), 0.01f, "100% more at its first level");
            Assert.IsTrue(HasTag(sven, "State.Dota.GodsStrength"), "and he shows it");

            var aura = Make.Effect("Damage Aura", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = AttackDamage, Op = ECalculationOperation.Multiply,
                MultiplyBehaviour = EMultiplyBehaviour.AdditiveBonus, Magnitude = 1.5f,
            });
            Assert.IsTrue(World.Apply(sven, sven, aura), "another 50% bonus");
            await Frames.Step();
            Assert.AreEqual(250f, Value(sven, AttackDamage), 0.01f, "adds to it: 100 × (1 + 1 + 0.5)");

            await Frames.Until(() => !Has(sven, godsStrength), 60, "God's Strength runs out after 25 seconds");
            Assert.AreEqual(150f, Value(sven, AttackDamage), 0.01f, "and the other bonus stays");
        });

        /// <summary>
        /// Storm Hammer: 80 magical damage at its first level and a 2 second stun. A stunned hero casts nothing: each
        /// ability avoids the Stunned tag, as nothing makes a stun stop every ability at once (see the wishlist).
        /// </summary>
        [UnityTest]
        public IEnumerator Storm_Hammer_stuns_and_a_stunned_hero_casts_nothing([Values] Hub hub) => Live(async () =>
        {
            Clock(0.1f);
            var sven = Hero(hub, "Sven", Radiant);
            var lina = Hero(hub, "Lina", Dire, magicResistance: 0.25f);
            await Frames.Step();
            Assert.That(lina.GetAbilitySystem().GiveAbility(AbilityNamed("Laguna Blade"), 1, out int laguna), "Lina has Laguna Blade");

            Assert.IsTrue(Cast(sven, AbilityNamed("Storm Hammer"), lina), "Sven throws his hammer");
            await Frames.Until(() => HasTag(lina, "State.Dota.Stunned"), 10, "Lina is stunned");
            Assert.AreEqual(1000f - 60f, Value(lina, Health), 0.01f, "80 magical, less her 25%");
            Assert.IsFalse(Cast(lina, laguna, sven), "a stunned hero casts nothing");

            await Frames.Until(() => !HasTag(lina, "State.Dota.Stunned"), 30, "the stun wears off after 2 seconds");
            Assert.IsTrue(Cast(lina, laguna, sven), "then she can");
        });

        /// <summary>Sven, made from his asset: the Strength hero's attributes, a team, and his two abilities.</summary>
        [UnityTest]
        public IEnumerator Sven_comes_with_his_abilities([Values] Hub hub) => Live(async () =>
        {
            Clock(0.5f);
            var sven = FromIdentity(hub, Load<EntityIdentity>("Entities/Sven"));
            await Frames.Step();

            Assert.AreEqual(1000f, Value(sven, Health), 0.01f, "his health");
            Assert.AreEqual(100f, Value(sven, AttackDamage), 0.01f, "his Attack Damage");
            Assert.AreEqual(5f, Value(sven, Armor), 0.01f, "his armor");
            var abilities = sven.GetAbilitySystem();
            Assert.IsTrue(abilities.HasAbility(AbilityNamed("Storm Hammer")), "Storm Hammer");
            Assert.IsTrue(abilities.HasAbility(AbilityNamed("God's Strength")), "God's Strength");
            Assert.IsTrue(sven.GetAffiliation().Contains(Tag.Generate(Radiant)), "a hero of the Radiant");

            Assert.IsTrue(Use(sven, AbilityNamed("God's Strength")), "he casts God's Strength");
            await Frames.Until(() => Value(sven, AttackDamage) > 100f, 10, "and it lands");
            Assert.AreEqual(200f, Value(sven, AttackDamage), 0.01f, "100% more");
        });

        /// <summary>
        /// Black King Bar: 24 Attack Damage while held, and its Avatar: 9 seconds immune to magic. Laguna Blade and Storm
        /// Hammer, tagged magical, don't land while it holds.
        /// </summary>
        [UnityTest]
        public IEnumerator Black_King_Bar_makes_its_holder_immune_to_magic([Values] Hub hub) => Live(async () =>
        {
            Clock(0.05f);
            var lina = Hero(hub, "Lina", Dire);
            var holder = Hero(hub, "Holder", Radiant, attackDamage: 100f);
            await Frames.Step();
            Hold(holder, ItemNamed("Black King Bar"));
            await Frames.Step();
            Assert.AreEqual(124f, Value(holder, AttackDamage), 0.01f, "24 Attack Damage while it's held");

            Assert.IsTrue(Use(holder, AbilityNamed("Black King Bar (Activate)")), "Avatar");
            await Frames.Until(() => HasTag(holder, "State.Dota.MagicImmune"), 10, "the holder is immune to magic");

            Assert.IsTrue(Cast(lina, AbilityNamed("Laguna Blade"), holder), "Lina casts at it");
            await Frames.Step(15);
            Assert.AreEqual(1000f, Value(holder, Health), 0.01f, "the bolt doesn't land");
            Assert.IsFalse(Has(holder, EffectAt("Sven/Storm Hammer (Stun)")), "nor would a stun");
        });

        /// <summary>
        /// Desolator: 50 Attack Damage, and each attack takes 6 armor from its target for 7 seconds, refreshed, not stacked.
        /// An attack deals its Attack Damage less what armor takes off, 1/(1 + 0.06 × armor), through a Lab Kit curve: no
        /// number divides (see the wishlist). The armor comes off after the hit that brings it.
        /// </summary>
        [UnityTest]
        public IEnumerator Desolator_corrupts_what_its_holder_hits([Values] Hub hub) => Live(async () =>
        {
            Clock(0.5f);
            var attacker = Hero(hub, "Attacker", Radiant, attackDamage: 100f);
            var target = Hero(hub, "Target", Dire, health: 2000f, armor: 10f);
            var attack = EffectAt("Attack/Dota Attack");
            await Frames.Step();
            Hold(attacker, ItemNamed("Desolator"));
            await Frames.Step();
            Assert.AreEqual(150f, Value(attacker, AttackDamage), 0.01f, "50 Attack Damage while it's held");

            Assert.IsTrue(Hit(attacker, target, attack), "the first attack");
            await Frames.Step(2);
            Assert.AreEqual(2000f - 150f / 1.6f, Value(target, Health), 0.05f, "150, less what 10 armor takes off");
            Assert.AreEqual(4f, Value(target, Armor), 0.01f, "Corruption takes 6 armor");

            Assert.IsTrue(Hit(attacker, target, attack), "a second");
            await Frames.Step(2);
            Assert.IsTrue(Hit(attacker, target, attack), "a third");
            await Frames.Step(2);
            Assert.AreEqual(4f, Value(target, Armor), 0.01f, "Corruption doesn't stack");
            Assert.AreEqual(2000f - 150f / 1.6f - 2f * 150f / 1.24f, Value(target, Health), 0.05f, "the next attacks deal more");

            await Frames.Until(() => Value(target, Armor) > 4f, 20, "Corruption runs out 7 seconds after the last attack");
            Assert.AreEqual(10f, Value(target, Armor), 0.01f, "and the armor's back");
        });

        /// <summary>
        /// Mekansm: heals each ally 275, but none healed by a Mekansm in the last 21 seconds. The area search finds
        /// MonoBehaviour entities alone, and never its caster, so this is a Mono test, and Mekansm doesn't heal its holder
        /// (see the wishlist).
        /// </summary>
        [UnityTest]
        public IEnumerator Mekansm_heals_its_allies_once_in_a_while() => Live(async () =>
        {
            Clock(0.5f);
            var holder = Hero(Hub.Mono, "Holder", Radiant, healthNow: 500f);
            var ally = Hero(Hub.Mono, "Ally", Radiant, healthNow: 500f);
            var enemy = Hero(Hub.Mono, "Enemy", Dire, healthNow: 500f);
            var other = Hero(Hub.Mono, "Other", Radiant, healthNow: 500f);
            await Frames.Step();
            Hold(holder, ItemNamed("Mekansm"));
            Hold(other, ItemNamed("Mekansm"));
            await Frames.Step();

            Assert.IsTrue(Use(holder, AbilityNamed("Mekansm (Restore)")), "the holder's Mekansm");
            await Frames.Until(() => Value(ally, Health) > 500f, 10, "heals its ally");
            Assert.AreEqual(775f, Value(ally, Health), 0.01f, "275");
            Assert.AreEqual(775f, Value(other, Health), 0.01f, "each ally");
            Assert.AreEqual(500f, Value(enemy, Health), 0.01f, "no enemy");
            Assert.AreEqual(500f, Value(holder, Health), 0.01f, "nor the holder: the area search leaves out its caster");

            // The heal's lockout lands a frame after the heal: a delay of 0 between an ability's effects waits a frame
            await Frames.Step(2);
            Assert.IsTrue(Use(other, AbilityNamed("Mekansm (Restore)")), "another Mekansm, within 21 seconds");
            await Frames.Until(() => Value(holder, Health) > 500f, 10, "heals the one the first didn't");
            Assert.AreEqual(775f, Value(holder, Health), 0.01f, "the first's holder");
            Assert.AreEqual(775f, Value(ally, Health), 0.01f, "but not the one the first healed");
        });

        /// <summary>
        /// Counter Helix: as Axe is attacked, a 20% chance to spin, 60 pure damage to each enemy at its first level. The
        /// ability carries a Lab Kit worker that sends Axe an event as he's hit, which sets the ability off, and a Lab Kit
        /// rule rolls its chance: PlayForge sends no event of its own as an entity is hit (see the wishlist). Mono, as the
        /// area search finds MonoBehaviour entities alone.
        /// </summary>
        [UnityTest]
        public IEnumerator Counter_Helix_spins_on_a_fifth_of_the_hits_Axe_takes() => Live(async () =>
        {
            Clock(0.1f);
            var axe = Hero(Hub.Mono, "Axe", Dire, health: 2000f);
            var first = Hero(Hub.Mono, "First", Radiant);
            var second = Hero(Hub.Mono, "Second", Radiant);
            var attack = EffectAt("Attack/Dota Attack");
            await Frames.Step();
            Assert.That(axe.GetAbilitySystem().GiveAbility(AbilityNamed("Counter Helix"), 1, out _), "Axe has Counter Helix");

            var rolls = Rolls(0.5f, 0.1f, 0.9f, 0.15f);
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(Hit(first, axe, attack), $"attack {i + 1}");
                await Frames.Step(3);
            }

            Assert.AreEqual(2000f - 400f, Value(axe, Health), 0.01f, "Axe takes four attacks");
            Assert.AreEqual(4, rolls.Drawn, "a roll for each");
            Assert.AreEqual(1000f - 120f, Value(first, Health), 0.01f, "two of them spin, 60 each");
            Assert.AreEqual(1000f - 120f, Value(second, Health), 0.01f, "hitting each enemy around him");
        });
    }
}
