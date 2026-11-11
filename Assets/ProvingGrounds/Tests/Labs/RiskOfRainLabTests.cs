using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Risk of Rain lab's bot: items that stack, from Risk of Rain 2, authored as assets under
    /// Assets/ProvingGrounds/RiskOfRain. Soldier's Syringe stacks linearly, Tougher Times hyperbolically, Crowbar and
    /// Lens-Maker's Glasses change every hit, Tri-Tip Dagger and Topaz Brooch act on a hit and on a kill; Commando fires
    /// Double Tap. Rolls are the test's, so a chance comes out as it says.
    /// </summary>
    [Category("Lab")]
    public class RiskOfRainLabTests : LabTest
    {
        protected override string Root => "Assets/ProvingGrounds/RiskOfRain";

        private const string Survivors = "Team.RoR.Survivors";
        private const string Monsters = "Team.RoR.Monsters";

        private ForgeAttribute Health => Load<ForgeAttribute>("Attributes/Health");
        private ForgeAttribute BaseDamage => Load<ForgeAttribute>("Attributes/Base Damage");
        private ForgeAttribute AttackSpeed => Load<ForgeAttribute>("Attributes/Attack Speed");
        private ForgeAttribute Barrier => Load<ForgeAttribute>("Attributes/Barrier");
        private ForgeAttribute CritChance => Load<ForgeAttribute>("Attributes/Crit Chance");

        private Item ItemNamed(string name) => Load<Item>($"Items/{name}");
        private GameplayEffect EffectNamed(string name) => Load<GameplayEffect>($"Effects/{name}");
        private GameplayEffect Bullet => EffectNamed("Bullet");

        // A survivor with 12 base damage and no crit chance, unless it says, so a hit draws no roll of its own
        private IGameplayAbilitySystem Survivor(Hub hub, string name, float health = 110f, float crit = 0f) =>
            Entity(hub, name, Survivors, Pool(Health, health, health), Stat(BaseDamage, 12f), Stat(AttackSpeed, 1f),
                Pool(Barrier, 0f, health), Stat(CritChance, crit));

        private IGameplayAbilitySystem Monster(Hub hub, string name, float health) => Entity(hub, name, Monsters, Pool(Health, health, health));

        private GameplayEffect Blow(float amount) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -amount });

        /// <summary>Soldier's Syringe: 15% attack speed a stack, linearly, in the pool of attack speed bonuses.</summary>
        [UnityTest]
        public IEnumerator Soldiers_Syringe_stacks_linearly([Values] Hub hub) => Live(async () =>
        {
            var commando = Survivor(hub, "Commando");
            await Frames.Step();

            Hold(commando, ItemNamed("Soldier's Syringe"));
            await Frames.Step();
            Assert.AreEqual(1.15f, Value(commando, AttackSpeed), 0.001f, "one: 15%");

            Assert.AreEqual(2, commando.GetItemSystem().GiveItem(ItemNamed("Soldier's Syringe"), 2), "two more");
            await Frames.Step();
            Assert.AreEqual(1.45f, Value(commando, AttackSpeed), 0.001f, "three: 45%");

            var bonus = Make.Effect("Warbanner", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = AttackSpeed, Op = ECalculationOperation.Multiply,
                MultiplyBehaviour = EMultiplyBehaviour.AdditiveBonus, Magnitude = 1.5f,
            });
            Assert.IsTrue(World.Apply(commando, commando, bonus), "a 50% bonus besides");
            await Frames.Step();
            Assert.AreEqual(1.95f, Value(commando, AttackSpeed), 0.001f, "adds to them: 1 + 0.45 + 0.5");
        });

        /// <summary>
        /// Tougher Times: a chance to block a hit outright, 1 − 1/(1 + 0.15 × stacks): 13% with one, 43% with five. A Lab
        /// Kit worker blocks, and a Lab Kit curve reads the stacks: PlayForge has neither a chance to block nor a
        /// hyperbolic number (see the wishlist).
        /// </summary>
        [UnityTest]
        public IEnumerator Tougher_Times_blocks_by_a_hyperbolic_chance([Values] Hub hub) => Live(async () =>
        {
            var commando = Survivor(hub, "Commando");
            var beetle = Monster(hub, "Beetle", 100f);
            await Frames.Step();
            Hold(commando, ItemNamed("Tougher Times"));
            await Frames.Step();

            var rolls = Rolls(0.10f, 0.20f, 0.40f, 0.45f);
            Assert.IsTrue(Hit(beetle, commando, Blow(20f)));
            await Frames.Step();
            Assert.AreEqual(110f, Value(commando, Health), 0.01f, "a roll of 0.10 is under 13%: blocked");
            Assert.IsTrue(Hit(beetle, commando, Blow(20f)));
            await Frames.Step();
            Assert.AreEqual(90f, Value(commando, Health), 0.01f, "0.20 isn't: it lands");

            Assert.AreEqual(4, commando.GetItemSystem().GiveItem(ItemNamed("Tougher Times"), 4), "four more");
            await Frames.Step();
            Assert.IsTrue(Hit(beetle, commando, Blow(20f)));
            await Frames.Step();
            Assert.AreEqual(90f, Value(commando, Health), 0.01f, "with five, 0.40 is under 43%: blocked");
            Assert.IsTrue(Hit(beetle, commando, Blow(20f)));
            await Frames.Step();
            Assert.AreEqual(70f, Value(commando, Health), 0.01f, "0.45 isn't");
            Assert.AreEqual(4, rolls.Drawn, "a roll for each hit");
        });

        /// <summary>A blocked hit lands as nothing, so the attacker's on-hit items don't go off.</summary>
        [UnityTest]
        public IEnumerator A_blocked_hit_sets_off_no_item([Values] Hub hub) => Live(async () =>
        {
            var attacker = Survivor(hub, "Attacker");
            var defender = Survivor(hub, "Defender");
            var bleed = EffectNamed("Bleed");
            await Frames.Step();
            Hold(attacker, ItemNamed("Tri-Tip Dagger"), stacks: 10);
            Hold(defender, ItemNamed("Tougher Times"));
            await Frames.Step();

            Rolls(0f, 0.99f);
            Assert.IsTrue(Hit(attacker, defender, Bullet));
            await Frames.Step(2);
            Assert.AreEqual(110f, Value(defender, Health), 0.01f, "blocked");
            Assert.IsFalse(Has(defender, bleed), "so ten daggers, a sure bleed, don't bleed it");

            Assert.IsTrue(Hit(attacker, defender, Bullet));
            await Frames.Step(2);
            Assert.IsTrue(Has(defender, bleed), "a hit that lands bleeds");
        });

        /// <summary>
        /// Crowbar: 75% more damage a stack to an enemy above 90% of its health. The bullet carries Crowbar's term: no item
        /// adds to another asset's hits, so every attack must read each item that changes a hit (see the wishlist).
        /// </summary>
        [UnityTest]
        public IEnumerator Crowbar_hits_harder_above_nine_tenths_health([Values] Hub hub) => Live(async () =>
        {
            var commando = Survivor(hub, "Commando");
            var lemurian = Monster(hub, "Lemurian", 100f);
            await Frames.Step();
            Hold(commando, ItemNamed("Crowbar"));
            await Frames.Step();

            Assert.IsTrue(Hit(commando, lemurian, Bullet));
            await Frames.Step();
            Assert.AreEqual(100f - 21f, Value(lemurian, Health), 0.01f, "at full health, 12 × 1.75");
            Assert.IsTrue(Hit(commando, lemurian, Bullet));
            await Frames.Step();
            Assert.AreEqual(100f - 21f - 12f, Value(lemurian, Health), 0.01f, "below 90%, 12");
        });

        /// <summary>
        /// Tri-Tip Dagger: a 10% chance a stack on hit to bleed for 240% base damage over 3 seconds. The bleed's ticks are
        /// typed apart from attacks, so they set off no item and draw no roll: their proc coefficient is 0.
        /// </summary>
        [UnityTest]
        public IEnumerator Tri_Tip_Dagger_bleeds_and_a_bleed_sets_off_nothing([Values] Hub hub) => Live(async () =>
        {
            Clock(0.25f);
            var commando = Survivor(hub, "Commando");
            var lemurian = Monster(hub, "Lemurian", 1000f);
            var bleed = EffectNamed("Bleed");
            await Frames.Step();
            Hold(commando, ItemNamed("Tri-Tip Dagger"));
            await Frames.Step();

            var rolls = Rolls(0.05f, 0.5f);
            Assert.IsTrue(Hit(commando, lemurian, Bullet));
            await Frames.Step(2);
            Assert.IsTrue(Has(lemurian, bleed), "0.05 is under 10%: it bleeds");
            Assert.IsTrue(Hit(commando, lemurian, Bullet));
            await Frames.Until(() => !Has(lemurian, bleed), 30, "the bleed runs its 3 seconds");

            Assert.AreEqual(1000f - 24f - 28.8f, Value(lemurian, Health), 0.05f, "two hits of 12, and one bleed of 240% of 12");
            Assert.AreEqual(2, rolls.Drawn, "a roll for each hit, none for the bleed's twelve ticks");
        });

        /// <summary>
        /// Topaz Brooch: on a kill, 15 barrier a stack, which takes a hit before health and fades over 30 seconds. A Lab
        /// Kit worker acts on the kill, and another soaks the hit: PlayForge has neither (see the wishlist).
        /// </summary>
        [UnityTest]
        public IEnumerator Topaz_Brooch_gives_a_barrier_on_a_kill([Values] Hub hub) => Live(async () =>
        {
            Clock(0.1f);
            var commando = FromIdentity(hub, Load<EntityIdentity>("Entities/Commando"));
            var lemurian = Monster(hub, "Lemurian", 10f);
            var beetle = Monster(hub, "Beetle", 100f);
            await Frames.Step();
            Hold(commando, ItemNamed("Topaz Brooch"));
            await Frames.Step();

            Rolls(0.99f);
            Assert.IsTrue(Hit(commando, lemurian, Bullet), "a bullet of 12 at a Lemurian of 10");
            await Frames.Until(() => Value(commando, Barrier) > 0f, 10, "the kill gives a barrier");
            float barrier = Value(commando, Barrier);
            Assert.That(barrier, Is.InRange(15f - 110f / 30f - 0.01f, 15.01f), "15, less what's faded");

            Assert.IsTrue(Hit(beetle, commando, Blow(20f)), "a blow of 20");
            await Frames.Step();
            Assert.AreEqual(0f, Value(commando, Barrier), 0.01f, "the barrier takes it first");
            Assert.AreEqual(110f - (20f - barrier), Value(commando, Health), 0.05f, "and health the rest");
        });

        /// <summary>
        /// Lens-Maker's Glasses: 10% crit chance a stack, a critical hit dealing double. The bullet rolls through a Lab
        /// Kit condition: PlayForge has no chance in a number (see the wishlist). The roll is drawn once for each hit.
        /// </summary>
        [UnityTest]
        public IEnumerator Lens_Makers_Glasses_make_hits_critical([Values] Hub hub) => Live(async () =>
        {
            var commando = Survivor(hub, "Commando");
            var lemurian = Monster(hub, "Lemurian", 1000f);
            await Frames.Step();
            Hold(commando, ItemNamed("Lens-Maker's Glasses"));
            await Frames.Step();
            Assert.AreEqual(0.1f, Value(commando, CritChance), 0.001f, "10% crit chance");

            var rolls = Rolls(0.05f, 0.5f);
            Assert.IsTrue(Hit(commando, lemurian, Bullet));
            await Frames.Step();
            Assert.AreEqual(1000f - 24f, Value(lemurian, Health), 0.01f, "0.05 is under 10%: a critical hit, double");
            Assert.IsTrue(Hit(commando, lemurian, Bullet));
            await Frames.Step();
            Assert.AreEqual(1000f - 36f, Value(lemurian, Health), 0.01f, "0.5 isn't: 12");
            Assert.AreEqual(2, rolls.Drawn, "a roll for each hit");
        });

        /// <summary>Commando, made from his asset: a survivor, and Double Tap's two bullets.</summary>
        [UnityTest]
        public IEnumerator Commando_double_taps([Values] Hub hub) => Live(async () =>
        {
            Clock(0.05f);
            var commando = FromIdentity(hub, Load<EntityIdentity>("Entities/Commando"));
            var lemurian = Monster(hub, "Lemurian", 500f);
            await Frames.Step();
            Assert.AreEqual(110f, Value(commando, Health), 0.01f, "a survivor's health");
            Assert.AreEqual(12f, Value(commando, BaseDamage), 0.01f, "and base damage");

            Rolls(0.99f);
            Assert.IsTrue(Use(commando, Load<Ability>("Abilities/Double Tap"), lemurian), "Double Tap");
            await Frames.Until(() => Value(lemurian, Health) < 500f - 12f - 0.01f, 20, "both bullets land");
            Assert.AreEqual(500f - 24f, Value(lemurian, Health), 0.01f, "12 each");
        });
    }
}
