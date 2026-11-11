using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Hades lab's bot: boons from Hades, authored as items under Assets/ProvingGrounds/Hades, each changing
    /// Zagreus's Strike: Heartbreak Strike and Frost Strike add to it in one pool and inflict Weak and Chill, Curse of
    /// Agony Dooms, Drunken Strike gives a Hangover, Pressure Points makes hits critical. God Mode resists every blow,
    /// and Megaera rages from half her health. How a hit's bonuses accumulate is the point: Attack bonuses pool, Weak
    /// multiplies on its own.
    /// </summary>
    [Category("Lab")]
    public class HadesLabTests : LabTest
    {
        protected override string Root => "Assets/ProvingGrounds/Hades";

        private const string Underworld = "Team.Hades.Underworld";

        private ForgeAttribute Health => Load<ForgeAttribute>("Attributes/Health");
        private ForgeAttribute StrikeDamage => Load<ForgeAttribute>("Attributes/Strike Damage");
        private ForgeAttribute DamageDealt => Load<ForgeAttribute>("Attributes/Damage Dealt");
        private ForgeAttribute DamageResistance => Load<ForgeAttribute>("Attributes/Damage Resistance");
        private ForgeAttribute MoveSpeed => Load<ForgeAttribute>("Attributes/Move Speed");

        private Item Boon(string name) => Load<Item>($"Boons/{name}");
        private GameplayEffect Status(string name) => Load<GameplayEffect>($"Effects/Statuses/{name}");
        private GameplayEffect StrikeHit => Load<GameplayEffect>("Effects/Strike (Hit)");

        private IGameplayAbilitySystem Zagreus(Hub hub) => FromIdentity(hub, Load<EntityIdentity>("Entities/Zagreus"));

        private IGameplayAbilitySystem Foe(Hub hub, string name, float health = 1000f) =>
            Entity(hub, name, Underworld, Pool(Health, health, health), Stat(DamageDealt, 1f), Stat(MoveSpeed, 1f));

        // A foe's blow: 10, times what its Damage Dealt makes it
        private GameplayEffect Club() => Make.Effect("Club", new EffectConfig
        {
            Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -10f,
            MagnitudeScaler = new AttributeBackedScaler { CaptureAttribute = DamageDealt, CaptureFrom = ESourceTarget.Source },
            MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler,
        });

        private GameplayEffect Blow(float amount) =>
            Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -amount });

        /// <summary>
        /// Zagreus, made from his asset, strikes with two Attack boons: Heartbreak Strike's 50% and Frost Strike's 40% add
        /// in one pool, 20 × (1 + 0.5 + 0.4), as Hades adds its boons' bonuses.
        /// </summary>
        [UnityTest]
        public IEnumerator Strike_takes_its_Attack_bonuses_in_one_pool([Values] Hub hub) => Live(async () =>
        {
            var zagreus = Zagreus(hub);
            var wretch = Foe(hub, "Wretch");
            await Frames.Step();
            Hold(zagreus, Boon("Heartbreak Strike"));
            Hold(zagreus, Boon("Frost Strike"));
            await Frames.Step();
            Assert.AreEqual(38f, Value(zagreus, StrikeDamage), 0.01f, "20 × (1 + 0.5 + 0.4)");

            Assert.IsTrue(Use(zagreus, Load<Ability>("Abilities/Strike"), wretch), "he strikes");
            await Frames.Until(() => Value(wretch, Health) < 1000f, 10, "it lands");
            Assert.AreEqual(1000f - 38f, Value(wretch, Health), 0.01f, "for 38");
        });

        /// <summary>
        /// Heartbreak Strike: each Attack hit makes the foe Weak for 4 seconds: it deals 30% less, a multiplier apart from
        /// its bonuses.
        /// </summary>
        [UnityTest]
        public IEnumerator Heartbreak_Strike_weakens_what_it_hits([Values] Hub hub) => Live(async () =>
        {
            Clock(0.5f);
            var zagreus = Zagreus(hub);
            var wretch = Foe(hub, "Wretch");
            var weak = Status("Weak");
            await Frames.Step();
            Hold(zagreus, Boon("Heartbreak Strike"));
            await Frames.Step();

            Assert.IsTrue(Hit(zagreus, wretch, StrikeHit), "a Strike");
            await Frames.Step(2);
            Assert.IsTrue(Has(wretch, weak), "makes it Weak");
            Assert.AreEqual(0.7f, Value(wretch, DamageDealt), 0.001f, "30% less");

            Assert.IsTrue(Hit(wretch, zagreus, Club()), "it hits back");
            await Frames.Step();
            Assert.AreEqual(50f - 7f, Value(zagreus, Health), 0.01f, "for 7, not 10");

            await Frames.Until(() => !Has(wretch, weak), 12, "Weak wears off after 4 seconds");
            Assert.AreEqual(1f, Value(wretch, DamageDealt), 0.001f, "and it deals its all again");
        });

        /// <summary>
        /// Curse of Agony: each Attack hit Dooms the foe: 50 damage after 1.1 seconds, from an effect its Doom holds for
        /// when it leaves. A hit while it's on changes nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator Doom_bursts_after_a_moment_and_wont_stack([Values] Hub hub) => Live(async () =>
        {
            Clock(0.1f);
            var zagreus = Zagreus(hub);
            var wretch = Foe(hub, "Wretch");
            var doom = Status("Doom");
            await Frames.Step();
            Hold(zagreus, Boon("Curse of Agony"));
            await Frames.Step();

            Assert.IsTrue(Hit(zagreus, wretch, StrikeHit), "a Strike");
            await Frames.Step(2);
            Assert.IsTrue(Has(wretch, doom), "Dooms it");
            await Frames.Step(3);
            Assert.IsTrue(Hit(zagreus, wretch, StrikeHit), "another, while it's Doomed");
            await Frames.Step(2);
            Assert.AreEqual(1, Stacks(wretch, doom), "adds nothing to it");

            await Frames.Until(() => !Has(wretch, doom), 20, "Doom bursts");
            await Frames.Step(2);
            Assert.AreEqual(1000f - 40f - 50f, Value(wretch, Health), 0.01f, "two Strikes of 20, and one burst of 50");
            await Frames.Step(15);
            Assert.AreEqual(1000f - 40f - 50f, Value(wretch, Health), 0.01f, "and only one");
        });

        /// <summary>Drunken Strike: each Attack hit adds a Hangover stack, up to 5: 4 damage a stack every half second.</summary>
        [UnityTest]
        public IEnumerator Hangover_stacks_to_five([Values] Hub hub) => Live(async () =>
        {
            Clock(0.05f);
            var zagreus = Zagreus(hub);
            var wretch = Foe(hub, "Wretch");
            var hangover = Status("Hangover");
            await Frames.Step();
            Hold(zagreus, Boon("Drunken Strike"));
            await Frames.Step();

            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(Hit(zagreus, wretch, StrikeHit), $"Strike {i + 1}");
                await Frames.Step();
            }
            await Frames.Step();
            Assert.AreEqual(5, Stacks(wretch, hangover), "six hits, five stacks");

            float before = Value(wretch, Health);
            await Frames.Until(() => Value(wretch, Health) < before, 15, "a tick");
            Assert.AreEqual(before - 20f, Value(wretch, Health), 0.01f, "4 for each of five stacks");
        });

        /// <summary>
        /// Frost Strike: each Attack hit Chills the foe, 4% slower a stack; the tenth stack bursts in an Arctic Blast of 80
        /// and clears it, through Chill's own bound and its overflow.
        /// </summary>
        [UnityTest]
        public IEnumerator Chill_slows_by_the_stack_and_bursts_at_ten([Values] Hub hub) => Live(async () =>
        {
            Clock(0.05f);
            var zagreus = Zagreus(hub);
            var wretch = Foe(hub, "Wretch");
            var chill = Status("Chill");
            await Frames.Step();
            Hold(zagreus, Boon("Frost Strike"));
            await Frames.Step();

            for (int i = 0; i < 9; i++)
            {
                Assert.IsTrue(Hit(zagreus, wretch, StrikeHit), $"Strike {i + 1}");
                await Frames.Step();
            }
            await Frames.Step();
            Assert.AreEqual(9, Stacks(wretch, chill), "nine stacks");
            Assert.AreEqual(1f - 0.36f, Value(wretch, MoveSpeed), 0.001f, "36% slower");

            Assert.IsTrue(Hit(zagreus, wretch, StrikeHit), "the tenth");
            await Frames.Step(3);
            Assert.IsFalse(Has(wretch, chill), "bursts and clears Chill");
            Assert.AreEqual(1f, Value(wretch, MoveSpeed), 0.001f, "its speed back");
            Assert.AreEqual(1000f - 10f * 28f - 80f, Value(wretch, Health), 0.01f, "ten Strikes of 20 × 1.4, and a blast of 80");
        });

        /// <summary>
        /// Pressure Points: 2% more chance of a critical hit, for triple. Strike rolls through a Lab Kit condition: only
        /// hits that read Crit Chance can be critical, so Doom and Hangover can't, where Hades makes any damage (see the
        /// wishlist).
        /// </summary>
        [UnityTest]
        public IEnumerator Pressure_Points_makes_strikes_critical([Values] Hub hub) => Live(async () =>
        {
            var zagreus = Zagreus(hub);
            var wretch = Foe(hub, "Wretch");
            await Frames.Step();
            Hold(zagreus, Boon("Pressure Points"));
            await Frames.Step();

            var rolls = Rolls(0.01f, 0.5f);
            Assert.IsTrue(Hit(zagreus, wretch, StrikeHit));
            await Frames.Step();
            Assert.AreEqual(1000f - 60f, Value(wretch, Health), 0.01f, "0.01 is under 2%: triple");
            Assert.IsTrue(Hit(zagreus, wretch, StrikeHit));
            await Frames.Step();
            Assert.AreEqual(1000f - 80f, Value(wretch, Health), 0.01f, "0.5 isn't: 20");
            Assert.AreEqual(2, rolls.Drawn, "a roll for each hit");
        });

        /// <summary>God Mode: 20% Damage Resistance, which Zagreus's own worker takes off every blow. No code.</summary>
        [UnityTest]
        public IEnumerator God_Mode_takes_a_fifth_off_every_blow([Values] Hub hub) => Live(async () =>
        {
            var zagreus = Zagreus(hub);
            var wretch = Foe(hub, "Wretch");
            await Frames.Step();
            Assert.IsTrue(World.Apply(zagreus, zagreus, Load<GameplayEffect>("Effects/God Mode")), "God Mode");
            await Frames.Step();
            Assert.AreEqual(0.2f, Value(zagreus, DamageResistance), 0.001f, "20%");

            Assert.IsTrue(Hit(wretch, zagreus, Club()));
            await Frames.Step();
            Assert.AreEqual(50f - 8f, Value(zagreus, Health), 0.01f, "a blow of 10 takes 8");
        });

        /// <summary>
        /// Megaera, made from her asset, rages as her health falls past half: 50% more damage, once. A Lab Kit worker
        /// watches the line: PlayForge's thresholds only log (see the wishlist). Weak multiplies apart from her rage, so
        /// she deals 1 × 1.5 × 0.7.
        /// </summary>
        [UnityTest]
        public IEnumerator Megaera_rages_from_half_her_health([Values] Hub hub) => Live(async () =>
        {
            var megaera = FromIdentity(hub, Load<EntityIdentity>("Entities/Megaera"));
            var zagreus = Zagreus(hub);
            var fury = Load<GameplayEffect>("Effects/Megaera (Fury)");
            await Frames.Step();

            Assert.IsTrue(Hit(zagreus, megaera, Blow(2000f)));
            await Frames.Step(2);
            Assert.IsFalse(Has(megaera, fury), "at 2400 of 4400, calm");
            Assert.IsTrue(Hit(zagreus, megaera, Blow(300f)));
            await Frames.Step(2);
            Assert.IsTrue(Has(megaera, fury), "past 2200, she rages");
            Assert.AreEqual(1.5f, Value(megaera, DamageDealt), 0.001f, "50% more");

            var heal = Make.Effect("Heal", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = 500f });
            Assert.IsTrue(World.Apply(megaera, megaera, heal));
            await Frames.Step();
            Assert.IsTrue(Hit(zagreus, megaera, Blow(500f)));
            await Frames.Step(2);
            Assert.AreEqual(1, Stacks(megaera, fury), "falling past it again adds nothing");
            Assert.AreEqual(1.5f, Value(megaera, DamageDealt), 0.001f, "still 50% more");

            Hold(zagreus, Boon("Heartbreak Strike"));
            await Frames.Step();
            Assert.IsTrue(Hit(zagreus, megaera, StrikeHit), "a Heartbreak Strike");
            await Frames.Step(2);
            Assert.AreEqual(1.05f, Value(megaera, DamageDealt), 0.001f, "Weak multiplies apart from her rage: 1.5 × 0.7");
        });
    }
}
