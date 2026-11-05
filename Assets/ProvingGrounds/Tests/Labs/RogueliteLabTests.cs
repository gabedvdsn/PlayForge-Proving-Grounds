using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Roguelite lab's bot. It builds the hero as a run would, under a live ProcessControl, and checks the might each
    /// step leaves; the harness checks its invariants as each round ends. Phase 5's gate is every lab green.
    /// </summary>
    [Category("Lab")]
    public class RogueliteLabTests : ProvingGroundsTest
    {
        private RogueliteLab Lab(Hub hub) => new(World, Make, hub);

        /// <summary>A whetstone stacks, and its one effect reads the count: +5 might, then +15 with three (FEAT-14).</summary>
        [UnityTest]
        public IEnumerator Lab_A_whetstone_sharpens_by_the_stack([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Equip(lab.Whetstone), "the hero equips a whetstone");
            await Frames.Step();
            Assert.AreEqual(RogueliteLab.WhetstoneMight, lab.Value, "+5 might");

            Assert.AreEqual(2, lab.Items.GiveItem(lab.Whetstone, 2), "two more stack on");
            await Frames.Step();
            Assert.AreEqual(3 * RogueliteLab.WhetstoneMight, lab.Value, "+15 with three");
            Assert.AreEqual(1, lab.Items.ItemCount, "in the one slot");
        });

        /// <summary>
        /// The Ember set's bonuses follow its count (FEAT-3): one piece grants nothing, two the pact, three the ascension
        /// too, and taking a piece off takes the ascension back.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_The_Ember_set_grants_its_bonuses([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Equip(lab.EmberRing), "the ring goes on");
            await Frames.Step(2);
            Assert.AreEqual(0f, lab.Value, "one piece grants nothing");

            Assert.IsTrue(lab.Equip(lab.EmberCrown), "the crown goes on");
            await Frames.Step(2);
            Assert.AreEqual(RogueliteLab.PactMight, lab.Value, "two make the pact");

            Assert.IsTrue(lab.Equip(lab.EmberBoots), "the boots go on");
            await Frames.Step(2);
            Assert.AreEqual(RogueliteLab.PactMight + RogueliteLab.AscensionMight, lab.Value, "three the ascension too");

            Assert.IsTrue(lab.Items.UnequipItem(lab.EmberRing), "the ring comes off");
            await Frames.Step(2);
            Assert.AreEqual(RogueliteLab.PactMight, lab.Value, "and the ascension goes with it");
        });

        /// <summary>
        /// Tomes stack, and each read takes one off and teaches +2 might for good; under the Library aura, a read teaches
        /// twice that (FEAT-14).
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_tome_under_the_library_teaches_twice([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.AreEqual(3, lab.Items.GiveItem(lab.Tome, 3), "three tomes");
            Assert.IsTrue(lab.Read(), "one is read");
            await Frames.Step();
            Assert.AreEqual(RogueliteLab.LessonMight, lab.BaseValue, "and teaches +2 for good");

            Assert.IsTrue(World.Apply(lab.Hero, lab.Hero, lab.Library), "the hero enters the library");
            await Frames.Step();
            Assert.IsTrue(lab.Read(), "another is read");
            await Frames.Step();
            Assert.AreEqual(3 * RogueliteLab.LessonMight, lab.BaseValue, "and teaches twice as much");
            Assert.IsTrue(lab.Items.TryGetItemContainer(lab.Tome, out var tomes) && tomes.ItemStacks == 1, "one tome is left");
        });

        /// <summary>The Crown of Ages grants +4 might a level of its own, and rescales as it levels up: +12 at its third.</summary>
        [UnityTest]
        public IEnumerator Lab_The_crown_rescales_as_it_levels([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            Assert.IsTrue(lab.Equip(lab.CrownOfAges), "the crown goes on");
            await Frames.Step();
            Assert.AreEqual(RogueliteLab.CrownMight[0], lab.Value, "+4 at its first level");

            Assert.IsTrue(lab.LevelCrown(3), "it levels up to its third");
            await Frames.Step(2);
            Assert.AreEqual(RogueliteLab.CrownMight[2], lab.Value, "and grants +12");
        });

        /// <summary>
        /// A run: six rooms, each with a find, and the hero's might at the end is the build's sum. Whetstones, the set, tomes
        /// read in and out of the library, and the crown, levelled twice.
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_A_run([Values] Hub hub) => Live(async () =>
        {
            var lab = Lab(hub);
            lab.Equip(lab.Whetstone);
            lab.Items.GiveItem(lab.Tome, 2);
            await Frames.Step();
            lab.Equip(lab.EmberRing);
            lab.Equip(lab.EmberCrown);
            lab.Read();
            await Frames.Step();
            lab.Items.GiveItem(lab.Whetstone, 3);
            lab.Equip(lab.CrownOfAges);
            await Frames.Step();
            World.Apply(lab.Hero, lab.Hero, lab.Library);
            await Frames.Step();
            lab.Read();
            lab.Equip(lab.EmberBoots);
            await Frames.Step();
            lab.LevelCrown(2);
            await Frames.Step();
            lab.LevelCrown(4);
            await Frames.Step(2);

            float expected = 4 * RogueliteLab.WhetstoneMight            // four whetstones
                             + RogueliteLab.PactMight + RogueliteLab.AscensionMight  // the whole set
                             + RogueliteLab.LessonMight + 2 * RogueliteLab.LessonMight  // a tome out of the library, one in it
                             + RogueliteLab.CrownMight[3];               // the crown at its fourth level
            Assert.AreEqual(expected, lab.Value, "the hero's might is the build's sum");
        });
    }
}
