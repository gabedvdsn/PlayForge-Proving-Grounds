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
    /// The Creator Lab's shell (FEAT-35, phase 2): its space, its units, its camera, its dummies, its panels, and what's
    /// dropped on its units. Each test makes a lab of its own assets, paused, and steps its time itself, as its world is
    /// its own.
    /// </summary>
    public class CreatorLabTests : ProvingGroundsTest
    {
        private ForgeAttribute health, speed;
        private Tag physical;
        private CreatorLab lab;

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

        [SetUp]
        public void Attributes()
        {
            health = Make.Attribute("Health");
            speed = Make.Attribute("Movement Speed");
            physical = Make.NewTag("Test.Lab.Physical");
        }

        [TearDown]
        public void TakeTheLabDown()
        {
            if (lab) Object.DestroyImmediate(lab.gameObject);
            lab = null;
        }

        private EntityIdentity Fighter(string name, float hp = 100f, float walk = 5f, params Ability[] abilities)
        {
            var entity = Make.Identity(name, Make.Set(
                Make.Element(health, hp, hp, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(speed, walk, walk)));
            entity.StartingAbilities.AddRange(abilities);
            return entity;
        }

        private CreatorLab Lab(bool panels, params ScriptableObject[] assets)
        {
            lab = CreatorLab.Create(new ListLibrary("Test", assets.Append(health).Append(speed)), panels: panels);
            lab.Paused = true;
            return lab;
        }

        private GameplayEffect Hit(string name, float amount, EEffectDurationPolicy policy = EEffectDurationPolicy.Instant, float duration = 0f)
        {
            var effect = Make.Effect(name, new EffectConfig { Policy = policy, Target = health, Magnitude = -amount, Duration = duration });
            effect.ImpactSpecification.ImpactTypes.Add(physical);
            return effect;
        }

        private Ability Strike(GameplayEffect effect)
        {
            var ability = Make.Ability("Strike", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { effect } }));
            ability.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            return ability;
        }

        private void Run(float seconds, float step = 0.05f)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += step) lab.Step(step);
        }

        /// <summary>The lab builds its space: the ground, its camera, a world of its own and its own caster, on no side.</summary>
        [UnityTest]
        public IEnumerator LAB_1_The_lab_builds_its_space_and_finds_its_rules() => Live(async () =>
        {
            Lab(false, Fighter("Brute"));
            await Frames.Step();
            Assert.IsNotNull(lab.View, "a camera");
            Assert.IsNotNull(lab.View.Camera, "that looks");
            Assert.IsNotNull(GameObject.Find("Creator Lab Ground"), "the ground");
            Assert.IsNotNull(lab.World, "a world of its own");
            Assert.IsNotNull(lab.Caster, "its own caster");
            Assert.IsTrue(lab.Caster.Alive, "standing");
            Assert.AreEqual("Neutral", LabSides.Of(lab.Caster.Gas).Name, "on no side");
            Assert.AreSame(health, lab.ActiveRules.Health, "Health found by its name");
            Assert.AreSame(speed, lab.ActiveRules.MovementSpeed, "and Movement Speed");
        });

        /// <summary>A unit stands on its side and walks where it's sent at its speed, in the lab's time.</summary>
        [UnityTest]
        public IEnumerator LAB_2_A_unit_stands_on_its_side_and_walks_at_its_speed() => Live(async () =>
        {
            var brute = Fighter("Brute", walk: 5f);
            Lab(false, brute);
            var unit = lab.AddUnit(brute, LabSides.Blue, Vector3.zero);
            await Frames.Step();
            lab.Step(0.01f);
            Assert.IsTrue(unit.Alive, "it stands");
            Assert.AreEqual("Blue", LabSides.Of(unit.Gas).Name, "on its side");

            unit.MoveTo(new Vector3(5f, 0f, 0f));
            Run(0.6f, 0.1f);
            Assert.AreEqual(3f, unit.transform.position.x, 0.06f, "at 5 a second, 3 in 0.6 s of the lab's time");
            Run(1f, 0.1f);
            Assert.AreEqual(5f, unit.transform.position.x, 0.06f, "and it stops where it was sent");
            Assert.IsNull(unit.Destination, "its order done");
        });

        /// <summary>A unit set on another walks into range and attacks it with its first ability.</summary>
        [UnityTest]
        public IEnumerator LAB_2_A_unit_attacks_what_it_is_set_on() => Live(async () =>
        {
            var fighter = Fighter("Fighter", abilities: Strike(Hit("Blow", 10f)));
            var target = Fighter("Target");
            Lab(false, fighter, target);
            var a = lab.AddUnit(fighter, LabSides.Blue, Vector3.zero);
            var b = lab.AddUnit(target, LabSides.Red, new Vector3(6f, 0f, 0f));
            await Frames.Step();
            lab.Step(0.01f);

            a.AttackUnit(b);
            for (int i = 0; i < 80 && b.Value(health) >= 100f; i++) lab.Step(0.05f);
            Assert.Less(b.Value(health), 100f, "it walked up and struck");
            Assert.LessOrEqual(a.DistanceTo(b), a.AttackRange + 0.3f, "from within its range");
        });

        /// <summary>The camera pulls back, pans, follows and resets.</summary>
        [UnityTest]
        public IEnumerator LAB_3_The_camera_pulls_back_pans_follows_and_resets() => Live(async () =>
        {
            var brute = Fighter("Brute");
            Lab(false, brute);
            await Frames.Step();
            var view = lab.View;
            float start = view.Distance;

            view.Zoom(-2f);
            Assert.Greater(view.Distance, start, "two notches away pull it back");
            view.Zoom(4f);
            Assert.Less(view.Distance, start, "and four toward bring it in");

            view.Pan(new Vector2(200f, 150f), new Vector2(260f, 150f));
            Assert.Greater(Vector3.Distance(view.Focus, Vector3.zero), 0.1f, "a drag pans it");

            view.ResetView();
            Assert.AreEqual(start, view.Distance, 0.001f, "reset puts its distance back");
            Assert.AreEqual(Vector3.zero, view.Focus, "and its focus");

            var unit = lab.AddUnit(brute, LabSides.Blue, new Vector3(10f, 0f, 0f));
            view.Following = unit.transform;
            var previous = ForgeTime.Use(new SteppingClock { Step = 0.1f });
            try
            {
                await Frames.Step(12);
            }
            finally
            {
                ForgeTime.Use(previous);
            }
            Assert.Greater(view.Focus.x, 9f, "it follows a unit when given one");
        });

        /// <summary>A dummy counts what it takes, by type and by effect, and mends after a rest.</summary>
        [UnityTest]
        public IEnumerator LAB_4_A_dummy_counts_what_it_takes_and_mends() => Live(async () =>
        {
            var brute = Fighter("Brute");
            var hit = Hit("Hit", 30f);
            Lab(false, brute, hit);
            var dummy = lab.AddDummy(brute, LabSides.Red, new Vector3(0f, 0f, 3f));
            await Frames.Step();
            lab.Step(0.01f);

            Assert.IsTrue(lab.ApplyEffect(hit, dummy.Unit), "a hit lands, from the lab");
            lab.Step(0.05f);
            Assert.AreEqual(70f, dummy.Unit.Value(health), 0.01f, "for 30");
            Assert.AreEqual(1, dummy.History.Count, "the dummy counts it");
            Assert.AreEqual(30f, dummy.LastBlow, 0.01f, "as 30");
            Assert.IsTrue(dummy.Last.Value.Types.Contains(physical), "of its type");
            Assert.AreEqual("Hit", dummy.Last.Value.From, "from its effect");
            Assert.Greater(dummy.DamagePerSecond, 0f, "with its damage a second");

            Run(4f, 0.1f);
            Assert.AreEqual(100f, dummy.Unit.Value(health), 0.01f, "it mends to full after a rest");
        });

        /// <summary>A dummy that falls is revived where it stands, at full; one asked to die stays down.</summary>
        [UnityTest]
        public IEnumerator LAB_4_A_dummy_that_falls_is_revived_where_it_stands() => Live(async () =>
        {
            var mortal = Fighter("Mortal");
            mortal.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker { Target = health, Threshold = 0f });
            var doom = Hit("Doom", 500f);
            Lab(false, mortal, doom);
            var dummy = lab.AddDummy(mortal, LabSides.Red, new Vector3(0f, 0f, 3f));
            var dies = lab.AddDummy(mortal, LabSides.Red, new Vector3(3f, 0f, 3f));
            dies.Dies = true;
            await Frames.Step();
            lab.Step(0.01f);

            Assert.IsTrue(lab.ApplyEffect(doom, dummy.Unit));
            Assert.IsTrue(lab.ApplyEffect(doom, dies.Unit));
            Run(0.3f);
            Assert.IsTrue(dummy.Unit.Alive, "the dummy is revived");
            Assert.AreEqual(100f, dummy.Unit.Value(health), 0.01f, "at full");
            Assert.AreEqual(new Vector3(0f, 0f, 3f), dummy.transform.position, "where it fell");
            Assert.IsFalse(dies.Unit.Alive, "and one asked to die stays down");
            Assert.IsTrue(mortal.Death.Policy == EDeathPolicy.End, "the project's entity is as it was");
        });

        /// <summary>
        /// The panels: the library by kind with a search, the mode's controls, the log collapsed, and the middle dock
        /// hidden until something is selected, then showing it level by level.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_5_The_panels_list_the_library_and_show_the_selected() => Live(async () =>
        {
            var brute = Fighter("Brute");
            var blow = Hit("Blow", 10f);
            var strike = Strike(blow);
            Lab(true, brute, blow, strike);
            await Frames.Step(3);
            var panels = lab.Panels;
            Assert.IsNotNull(panels, "it builds its panels");
            var root = panels.Root;
            foreach (var name in new[] { "Library", "Selected", "Mode", "Log" })
                Assert.IsNotNull(root.Q(name: name), $"a {name} panel");
            Assert.IsNotNull(root.Q(name: "GroupEffects"), "the library lists effects");
            Assert.IsNotNull(root.Q(name: "Asset Blow"), "Blow among them");
            Assert.IsNotNull(root.Q(name: "Asset Strike"), "and abilities, Strike among them");
            Assert.AreEqual(DisplayStyle.None, root.Q(name: "LogContent").style.display.value, "the log starts collapsed");
            Assert.AreEqual(DisplayStyle.None, root.Q(name: "Selected").style.display.value, "the middle dock is hidden");

            lab.Select(blow);
            await Frames.Step(2);
            Assert.AreEqual(DisplayStyle.Flex, root.Q(name: "Selected").style.display.value, "until something is selected");
            var lines = root.Q(name: "SelectedBody").Query<Label>().ToList().Select(label => label.text).ToList();
            Assert.That(lines.Any(line => line.Contains("-10") && line.Contains("Health")), Is.True,
                $"its numbers: {string.Join(" | ", lines)}");

            var search = root.Q<TextField>(name: "Search");
            search.value = "stri";
            await Frames.Step();
            Assert.IsNull(root.Q(name: "Asset Blow"), "a search hides what it doesn't match");
            Assert.IsNotNull(root.Q(name: "Asset Strike"), "and keeps what it does");

            panels.Collapse(root.Q(name: "Library"), true);
            Assert.AreEqual(DisplayStyle.None, root.Q(name: "LibraryContent").style.display.value, "a panel collapses to its tab");
        });

        /// <summary>
        /// What's dropped: an effect lands from the selected unit; an ability with nothing selected is cast by the lab's
        /// caster, which learns it; an item is given; an entity on the ground is put down as a dummy.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_6_Dropped_assets_land_cast_give_and_put_down() => Live(async () =>
        {
            var fighter = Fighter("Fighter");
            var target = Fighter("Target");
            var mark = Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            var strike = Strike(Hit("Blow", 10f));
            var trinket = Make.Item("Trinket");
            Lab(false, fighter, target, mark, strike, trinket);
            var a = lab.AddUnit(fighter, LabSides.Blue, Vector3.zero);
            var b = lab.AddUnit(target, LabSides.Red, new Vector3(2f, 0f, 0f));
            await Frames.Step();
            lab.Step(0.01f);

            lab.Select(a);
            Assert.IsTrue(lab.DropAt(mark, b, b.transform.position), "an effect dropped on a unit");
            Assert.IsTrue(b.Gas.TryGetEffectContainer(mark, out var container), "lands on it");
            Assert.AreSame(a.Gas, container.Spec.Source, "from the selected unit");

            lab.Select((LabUnit)null);
            Assert.IsTrue(lab.DropAt(strike, b, b.transform.position), "an ability dropped on a unit");
            Run(0.3f);
            Assert.AreEqual(90f, b.Value(health), 0.01f, "is cast at it by the lab's caster");
            Assert.That(lab.Caster.Known(), Has.Member(strike), "which learned it");

            Assert.IsTrue(lab.DropAt(trinket, b, b.transform.position), "an item dropped on a unit");
            Assert.That(b.Items(), Has.Member(trinket), "is given to it");

            Assert.IsTrue(lab.DropAt(target, null, new Vector3(6f, 0f, 6f), LabSides.Green), "an entity dropped on the ground");
            var dummy = lab.Units.Last();
            Assert.IsTrue(dummy.TryGetComponent<LabDummy>(out _), "is put down as a dummy");
            Assert.AreEqual("Green", dummy.Side.Name, "on the side asked");
        });

        /// <summary>A drop on a point of the screen finds the unit under it.</summary>
        [UnityTest]
        public IEnumerator LAB_6_A_drop_on_the_screen_finds_the_unit_under_it() => Live(async () =>
        {
            var target = Fighter("Target");
            var mark = Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            Lab(false, target, mark);
            var b = lab.AddUnit(target, LabSides.Red, new Vector3(2f, 0f, 1f));
            await Frames.Step(2);
            lab.Step(0.01f);

            var screen = lab.View.Camera.WorldToScreenPoint(b.transform.position + Vector3.up);
            Assert.IsTrue(lab.TryPick(screen, out var picked, out _), "the screen point picks");
            Assert.AreSame(b, picked, "the unit there");
            Assert.IsTrue(lab.Drop(mark, screen), "and an effect dropped there");
            Assert.IsTrue(b.Gas.TryGetEffectContainer(mark, out _), "lands on it");
        });

        /// <summary>The lab's time stands while it's paused, goes a frame at a step, and runs at its speed.</summary>
        [UnityTest]
        public IEnumerator LAB_8_The_labs_time_pauses_steps_and_runs_at_its_speed() => Live(async () =>
        {
            var target = Fighter("Target");
            var burn = Hit("Burn", 1f, EEffectDurationPolicy.Durational, 10f);
            Lab(false, target, burn);
            var b = lab.AddUnit(target, LabSides.Red, Vector3.zero);
            await Frames.Step();
            lab.Step(0.01f);
            Assert.IsTrue(lab.ApplyEffect(burn, b));
            lab.Step(0.01f);
            Assert.IsTrue(b.Gas.TryGetEffectContainer(burn, out var container));
            float left = container.DurationRemaining;

            await Frames.Step(10);
            Assert.AreEqual(left, container.DurationRemaining, 0.0001f, "paused, its time stands");

            lab.StepFrame();
            await Frames.Step();
            Assert.AreEqual(left - lab.StepSeconds, container.DurationRemaining, 0.001f, "a step goes a frame");

            var clock = new SteppingClock { Step = 0.1f };
            var previous = ForgeTime.Use(clock);
            try
            {
                left = container.DurationRemaining;
                lab.Speed = 2f;
                lab.Paused = false;
                await Frames.Step();
                lab.Paused = true;
                Assert.AreEqual(left - 0.2f, container.DurationRemaining, 0.02f, "at twice the speed, a frame of 0.1 s is 0.2");
            }
            finally
            {
                ForgeTime.Use(previous);
            }
        });
    }
}
