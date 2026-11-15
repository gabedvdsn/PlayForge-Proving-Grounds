using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Creator Lab showing its working (phase 5, LAB-12 to LAB-16, Decision 88): its effect journal over its units, a
    /// unit's combat log with each hit's steps, its stops, which pause its world as one fires and say which, Step Frame
    /// and Step Tick, its watches over the last minute of its time, and ForgeLog's messages by system, in its bottom panel.
    /// </summary>
    public class LabShowingTests : ProvingGroundsTest
    {
        private ForgeAttribute health, speed;
        private CreatorLab lab;
        private readonly List<Object> made = new();
        private ForgeProject previous;
        private bool projectSet;

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            speed = Make.Attribute("Movement Speed");
        }

        [TearDown]
        public void TakeTheLabDown()
        {
            if (lab) Object.DestroyImmediate(lab.gameObject);
            lab = null;
            if (projectSet) ForgeProject.Use(previous);
            projectSet = false;
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
            ForgeLog.ClearLevels();
        }

        private EntityIdentity Fighter(string name, float hp = 100f, params Ability[] abilities)
        {
            var entity = Make.Identity(name, Make.Set(
                Make.Element(health, hp, hp, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(speed, 5f, 5f)));
            entity.StartingAbilities.AddRange(abilities);
            return entity;
        }

        private CreatorLab Lab(bool panels, params ScriptableObject[] assets)
        {
            lab = CreatorLab.Create(new ListLibrary("Test", assets.Append(health).Append(speed)), panels: panels);
            lab.Paused = true;
            return lab;
        }

        private GameplayEffect Hit(string name, float amount) =>
            Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -amount });

        private GameplayEffect Burn(string name, float amount, int ticks, float duration) => Make.Effect(name, new EffectConfig
        {
            Policy = EEffectDurationPolicy.Durational, Duration = duration, PeriodicTicks = true, Ticks = ticks, Target = health,
            Magnitude = -amount,
        });

        private void Run(float seconds, float step = 0.05f)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += step) lab.Step(step);
        }

        // The bottom panel open on a page, drawn as it is now
        private VisualElement Page(string page)
        {
            lab.Panels.ShowLog(page);
            return lab.Panels.Root.Q(name: $"{page}Page");
        }

        private static List<string> Lines(VisualElement list) => list.Query<Label>().ToList().Select(label => label.text).ToList();

        /// <summary>
        /// The lab's bottom panel said its journals were to come (LAB-12). The lab keeps an effect journal over every unit it
        /// holds, and its Journal page lists each entry on the selected unit, newest first.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_12_The_lab_journals_its_units_effects() => Live(async () =>
        {
            var target = Fighter("Target");
            var burn = Burn("Burn", 5f, 2, 1f);
            Lab(true, target, burn);
            var b = lab.AddUnit(target, LabSides.Red, Vector3.zero);
            await Frames.Step();
            lab.Step(0.01f);

            Assert.IsTrue(lab.ApplyEffect(burn, b), "the burn lands");
            Run(1.5f);
            var entries = lab.Showing.Journal.On(b.Gas).Where(entry => entry.Effect == burn).ToList();
            Assert.That(entries.Select(entry => entry.Kind), Is.SupersetOf(new[]
            {
                EEffectJournalKind.Applied, EEffectJournalKind.Ticked, EEffectJournalKind.Removed,
            }), "the lab's journal heard its unit's burn land, tick and come off");

            lab.Select(b);
            var lines = Lines(Page("Journal").Q(name: "JournalList"));
            Assert.That(lines, Has.Some.Contains("Burn landed on"), "and its Journal page lists it");
            Assert.That(lines.First(), Does.Contain("Burn came off"), "newest first");
        });

        /// <summary>A unit's combat log, on the Combat page, carries each hit's steps, as made and as it landed (LAB-13).</summary>
        [UnityTest]
        public IEnumerator LAB_13_The_combat_page_shows_a_hits_steps() => Live(async () =>
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            made.Add(project);
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            project.Settings.CombatLog = new CombatLogSettings { Keep = 100, Records = ECombatLogRecords.HitsTaken | ECombatLogRecords.HitsDealt };
            previous = ForgeProject.Use(project);
            projectSet = true;

            var target = Fighter("Target");
            var blow = Hit("Blow", 12f);
            Lab(true, target, blow);
            var a = lab.AddUnit(target, LabSides.Blue, Vector3.left * 2f);
            var b = lab.AddUnit(target, LabSides.Red, Vector3.right * 2f);
            await Frames.Step();
            lab.Step(0.01f);

            Assert.IsTrue(lab.ApplyEffect(blow, b, a), "a blow from one unit to the other");
            lab.Step(0.01f);
            lab.Select(b);
            var lines = Lines(Page("Combat").Q(name: "CombatList"));
            Assert.That(lines, Has.Some.Contains("Took -12 Health"), "the Combat page lists the hit it took");
            Assert.That(lines, Has.Some.Contains("-12 as made").And.Some.Contains("landed -12"), "with its steps");
        });

        /// <summary>
        /// Nothing stopped the lab's world on an event (LAB-14). A stop pauses it as what it names happens, on the selected
        /// unit or any, and says which: an effect landing, an attribute crossing a value, a tag coming, an ability refused.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_14_A_stop_pauses_the_lab_and_says_which() => Live(async () =>
        {
            var strike = Make.Ability("Strike", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect>() }));
            var target = Fighter("Target", 100f, strike);
            var blow = Hit("Blow", 10f);
            var marked = Make.NewTag("Test.Lab.Marked");
            var mark = Make.Effect("Mark", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { marked },
            });
            Lab(false, target, blow, mark);
            var b = lab.AddUnit(target, LabSides.Red, Vector3.zero);
            await Frames.Step();
            lab.Step(0.01f);
            lab.Select(b);

            var lands = lab.Showing.Add(new LabStop { Kind = ELabStopKind.EffectLands, Effect = blow, OnSelected = false });
            lab.Paused = false;
            Assert.IsTrue(lab.ApplyEffect(blow, b));
            Assert.IsTrue(lab.Paused, "the blow landing paused the lab");
            Assert.AreSame(lands, lab.Showing.StoppedBy, "its stop");
            StringAssert.Contains("Blow landed on", lab.Showing.StoppedFor, "and it says what fired it");
            lab.Showing.Remove(lands);
            lab.Showing.Resume();

            var crosses = lab.Showing.Add(new LabStop { Kind = ELabStopKind.AttributeCrosses, Attribute = health, Value = 85f });
            lab.Paused = false;
            Assert.IsTrue(lab.ApplyEffect(blow, b), "a second blow, 90 to 80");
            lab.Step(0.01f);
            Assert.IsTrue(lab.Paused, "Health crossing 85 paused it");
            Assert.AreSame(crosses, lab.Showing.StoppedBy);
            StringAssert.Contains("crossed 85", lab.Showing.StoppedFor);
            lab.Showing.Remove(crosses);
            lab.Showing.Resume();

            var comes = lab.Showing.Add(new LabStop { Kind = ELabStopKind.TagComes, Tag = marked });
            lab.Paused = false;
            Assert.IsTrue(lab.ApplyEffect(mark, b));
            lab.Step(0.01f);
            Assert.IsTrue(lab.Paused, "the mark's tag coming paused it");
            Assert.AreSame(comes, lab.Showing.StoppedBy);
            lab.Showing.Remove(comes);
            lab.Showing.Resume();

            var refused = lab.Showing.Add(new LabStop { Kind = ELabStopKind.AbilityRefused, Ability = strike });
            lab.Paused = false;
            var abilities = b.Gas.GetAbilitySystem();
            abilities.Enabled = false;
            Assert.IsFalse(abilities.TryActivateAbility(strike, out _, null, EAbilityActivationPolicyExtended.ActivateIfIdle),
                "with its ability system off, the strike is refused");
            abilities.Enabled = true;
            Assert.IsTrue(lab.Paused, "and that paused it");
            Assert.AreSame(refused, lab.Showing.StoppedBy);
            StringAssert.Contains("Strike refused on Target", lab.Showing.StoppedFor);
        });

        /// <summary>
        /// The lab's time went a frame at a time, never to the next tick (LAB-14). Step Tick goes on until an effect ticks on
        /// the selected unit, and stops there.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_14_Step_Tick_goes_until_an_effect_ticks() => Live(async () =>
        {
            var target = Fighter("Target");
            var burn = Burn("Burn", 5f, 4, 4f);
            Lab(false, target, burn);
            var b = lab.AddUnit(target, LabSides.Red, Vector3.zero);
            await Frames.Step();
            lab.Step(0.01f);
            lab.Select(b);
            Assert.IsTrue(lab.ApplyEffect(burn, b));
            lab.Step(0.01f);
            float health0 = b.Value(health);

            float before = lab.Elapsed;
            Assert.IsTrue(lab.Showing.StepTick(), "it found a tick");
            Assert.AreEqual(health0 - 5f, b.Value(health), 0.01f, "and stopped as it landed");
            Assert.That(lab.Elapsed - before, Is.InRange(0.5f, 1.1f), "about a second on, as the burn ticks each second");

            before = lab.Elapsed;
            Assert.IsTrue(lab.Showing.StepTick());
            Assert.AreEqual(health0 - 10f, b.Value(health), 0.01f, "the next tick");
            Assert.That(lab.Elapsed - before, Is.InRange(0.9f, 1.1f), "a second later");
        });

        /// <summary>
        /// Nothing drew a unit's numbers over time (LAB-15). A watch pinned on a unit takes its value each tenth of a second
        /// of the lab's time, and keeps the last minute of them.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_15_A_watch_keeps_the_last_minute_of_a_units_value() => Live(async () =>
        {
            var target = Fighter("Target");
            var burn = Burn("Burn", 1f, 10, 10f);
            Lab(true, target, burn);
            var b = lab.AddUnit(target, LabSides.Red, Vector3.zero);
            await Frames.Step();
            lab.Step(0.01f);
            var watch = lab.Showing.Pin(new LabWatch { Kind = ELabWatchKind.Attribute, Unit = b, Attribute = health });
            var stacks = lab.Showing.Pin(new LabWatch { Kind = ELabWatchKind.EffectTime, Unit = b, Effect = burn });

            Assert.IsTrue(lab.ApplyEffect(burn, b));
            Run(5f);
            Assert.That(watch.Count, Is.InRange(50, 52), "a value each tenth of a second, five seconds of them");
            Assert.AreEqual(b.Value(health), watch.Latest, 0.01f, "its newest is the unit's Health now");
            Assert.That(watch[0].value, Is.GreaterThan(watch.Latest), "and its line falls as the burn ticks");
            Assert.That(stacks.Latest, Is.InRange(4.5f, 5.5f), "the burn's time left, five seconds of ten");

            Run(65f, 0.1f);
            Assert.That(watch.Count, Is.EqualTo(601), "the last minute of them, no more");
            Assert.That(watch[0].time, Is.GreaterThanOrEqualTo(lab.Elapsed - 60.01f), "its oldest a minute ago");

            lab.Select(b);
            var page = Page("Watches");
            Assert.That(Lines(page.Q(name: "WatchList")), Has.Some.Contains($"{b.Name}'s Health"), "the Watches page lists it");
            Assert.IsNotNull(page.Q(name: "WatchChart"), "and draws its lines");
        });

        /// <summary>
        /// ForgeLog had one level for everything, and no one could hear what it logged (LAB-16). Each system takes a level of
        /// its own, and Logged hears each message let through, its system and whom it's about.
        /// </summary>
        [Test]
        public void LAB_16_ForgeLog_keeps_a_level_for_each_system()
        {
            var heard = new List<ForgeLogEntry>();
            void Hear(ForgeLogEntry entry) => heard.Add(entry);
            ForgeLog.Logged += Hear;
            try
            {
                ForgeLog.SetLevel(ForgeLog.ESystem.Effects, ForgeLog.ELevel.Verbose);
                ForgeLog.Verbose(ForgeLog.ESystem.Effects, "An effect's trace", null, "a subject");
                ForgeLog.Verbose(ForgeLog.ESystem.Abilities, "An ability's trace");
                Assert.That(heard.Select(entry => entry.Message), Is.EqualTo(new[] { "An effect's trace" }),
                    "Effects' own Verbose lets its trace through, and Abilities, at the default Info, doesn't");
                Assert.AreEqual(ForgeLog.ESystem.Effects, heard[0].System);
                Assert.AreEqual("a subject", heard[0].Subject, "with whom it's about");
                Assert.IsTrue(ForgeLog.HasOwnLevel(ForgeLog.ESystem.Effects));
                ForgeLog.SetLevel(ForgeLog.ESystem.Effects, null);
                Assert.IsFalse(ForgeLog.HasOwnLevel(ForgeLog.ESystem.Effects), "and gives it back");
                ForgeLog.Verbose(ForgeLog.ESystem.Effects, "Dropped");
                Assert.AreEqual(1, heard.Count, "to the default's Info, which drops Verbose");
            }
            finally
            {
                ForgeLog.Logged -= Hear;
                ForgeLog.ClearLevels();
            }
        }

        /// <summary>The lab's Log page lists ForgeLog's messages, those about the selected unit where asked (LAB-16).</summary>
        [UnityTest]
        public IEnumerator LAB_16_The_labs_log_shows_a_units_messages() => Live(async () =>
        {
            var target = Fighter("Target");
            Lab(true, target);
            var a = lab.AddUnit(target, LabSides.Blue, Vector3.left * 2f);
            var b = lab.AddUnit(target, LabSides.Red, Vector3.right * 2f);
            await Frames.Step();
            lab.Step(0.01f);

            ForgeLog.Warning(ForgeLog.ESystem.Effects, "About the red one", null, b.Gas);
            ForgeLog.Warning(ForgeLog.ESystem.Abilities, "About the blue one", null, a.Gas);
            Assert.That(lab.Showing.Log.Select(entry => entry.Message), Is.SupersetOf(new[] { "About the red one", "About the blue one" }),
                "the lab's log heard both");

            lab.Select(b);
            var page = Page("Log");
            page.Q<Toggle>(name: "LogSelected").value = true;
            lab.Panels.RefreshLog();
            var lines = Lines(page.Q(name: "LogList"));
            Assert.That(lines, Has.Some.Contains("Effects, warning: About the red one"), "filtered to the red one, its message");
            Assert.That(lines, Has.None.Contains("About the blue one"), "and not the other's");
        });
    }
}
