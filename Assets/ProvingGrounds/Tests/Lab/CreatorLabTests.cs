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
            if (pressing != null) ForgeInput.Use(previousInput);
            pressing = null;
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

        // An input a test presses: a pointer at a point of the screen, and buttons and keys held pressed until it lets go
        private sealed class PressingInput : IForgeInput
        {
            public Vector2? Pointer;
            public readonly HashSet<int> Buttons = new();
            public readonly HashSet<KeyCode> Keys = new();

            public bool PointerPressed(int button) => Buttons.Contains(button);

            public bool TryGetPointerRay(out Ray ray)
            {
                ray = default;
                return false;
            }

            public bool TryGetPointerPosition(out Vector2 position)
            {
                position = Pointer.GetValueOrDefault();
                return Pointer.HasValue;
            }

            public bool KeyPressed(KeyCode key) => Keys.Contains(key);

            public void LetGo()
            {
                Buttons.Clear();
                Keys.Clear();
            }
        }

        // A project's driver: it keeps each order it's given and passes none on, so its unit does nothing of itself
        private sealed class RecordingDriver : LabDriver
        {
            public readonly List<(LabUnit Unit, Vector3 Point)> Moves = new();
            public readonly List<(LabUnit Unit, LabUnit Target)> Attacks = new();
            public readonly List<(LabUnit Unit, Ability Ability)> Casts = new();
            public readonly List<LabUnit> Stops = new();

            public override void MoveTo(LabUnit unit, Vector3 point) => Moves.Add((unit, point));
            public override void Attack(LabUnit unit, LabUnit target) => Attacks.Add((unit, target));

            public override bool Cast(LabUnit unit, Ability ability, ProcessDataPacket data)
            {
                Casts.Add((unit, ability));
                return true;
            }

            public override void Stop(LabUnit unit) => Stops.Add(unit);
        }

        private PressingInput pressing;
        private IForgeInput previousInput;

        private PressingInput Press()
        {
            pressing = new PressingInput();
            previousInput = ForgeInput.Use(pressing);
            return pressing;
        }

        // A right-click, or keys, read by the controls over a frame
        private async Cysharp.Threading.Tasks.UniTask Click(PressingInput input, Vector3 world)
        {
            input.Pointer = lab.View.Camera.WorldToScreenPoint(world);
            input.Buttons.Add(1);
            await Frames.Step();
            input.LetGo();
        }

        private async Cysharp.Threading.Tasks.UniTask Key(PressingInput input, KeyCode key)
        {
            input.Keys.Add(key);
            await Frames.Step();
            input.LetGo();
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

        private EntityIdentity Mortal(string name, float hp, params Ability[] abilities)
        {
            var entity = Fighter(name, hp, 5f, abilities);
            entity.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker { Target = health, Threshold = 0f });
            return entity;
        }

        // The battle's bots fight; the lab's time is stepped here, not by Unity's
        private void Fight()
        {
            lab.Play();
            lab.Paused = true;
        }

        /// <summary>A bot casts what's ready at the nearest foe it can target, its target in the data, and otherwise attacks.</summary>
        [UnityTest]
        public IEnumerator LAB_10_A_bot_casts_at_the_nearest_foe() => Live(async () =>
        {
            var bolt = Make.Ability("Bolt", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Hit("Bolt Hit", 20f) } }));
            bolt.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            var caster = Fighter("Caster", abilities: new[] { Strike(Hit("Blow", 1f)), bolt });
            var target = Fighter("Target", hp: 1000f);
            Lab(false, caster, target);
            lab.SetMode(LabMode.Battle);
            var bot = lab.AddBot(caster, LabSides.Blue, Vector3.zero);
            var near = lab.AddBot(target, LabSides.Red, new Vector3(4f, 0f, 0f)).Unit;
            var far = lab.AddBot(target, LabSides.Red, new Vector3(-20f, 0f, 0f)).Unit;
            near.GetComponent<LabBot>().Fighting = false;
            far.GetComponent<LabBot>().Fighting = false;
            await Frames.Step();
            lab.Step(0.01f);
            Assert.AreSame(near, LabBot.Nearest(bot.Unit), "the nearest foe");

            lab.Play();
            lab.Paused = true;
            near.GetComponent<LabBot>().Fighting = false;
            far.GetComponent<LabBot>().Fighting = false;
            Run(0.6f);
            Assert.Less(near.Value(health), 1000f, "the bot cast at the nearest foe");
            Assert.AreEqual(1000f, far.Value(health), 0.01f, "and not at the far one");
        });

        /// <summary>A quick battle of two sides plays until one stands, and a reset puts every bot back, at full.</summary>
        [UnityTest]
        public IEnumerator LAB_9_A_quick_battle_plays_until_one_side_stands() => Live(async () =>
        {
            var soldier = Mortal("Soldier", 30f, Strike(Hit("Blow", 10f)));
            var veteran = Mortal("Veteran", 90f, Strike(Hit("Heavy Blow", 15f)));
            Lab(false, soldier, veteran);
            lab.QuickBattle(2, soldier);
            Assert.AreEqual(LabMode.Battle, lab.Mode, "it's a battle");
            Assert.AreEqual(4, lab.Seats.Count, "of two a side");
            Assert.AreEqual(2, lab.Seats.Count(seat => seat.Side.Name == "Blue"), "Blue");
            Assert.AreEqual(2, lab.Seats.Count(seat => seat.Side.Name == "Red"), "and Red");
            Assert.That(lab.Seats.All(seat => seat.Entity == soldier), Is.True, "of the entity given");

            // Blue's veterans against Red's soldiers, so the battle has a winner
            lab.Clear();
            lab.AddBot(veteran, LabSides.Blue);
            lab.AddBot(veteran, LabSides.Blue);
            lab.AddBot(soldier, LabSides.Red);
            lab.AddBot(soldier, LabSides.Red);
            await Frames.Step();
            lab.Step(0.01f);

            Fight();
            for (int i = 0; i < 1200 && !lab.Ended; i++) lab.Step(0.05f);
            Assert.IsTrue(lab.Ended, "the battle ends");
            Assert.IsFalse(lab.Fighting, "and its bots stop");
            Assert.AreEqual("Blue", lab.Winner?.Name, "with the stronger side standing");
            var standing = lab.Seats.Where(seat => seat.Unit && seat.Unit.Alive).Select(seat => seat.Side.Name).Distinct().ToList();
            Assert.That(standing, Is.EqualTo(new[] { "Blue" }), "its bots alone");

            var homes = lab.Seats.Select(seat => seat.Home).ToList();
            lab.ResetBattle();
            lab.Step(0.01f);
            Assert.IsFalse(lab.Ended, "a reset starts it again");
            Assert.That(lab.Seats.All(seat => seat.Unit && seat.Unit.Alive), Is.True, "every bot standing");
            Assert.That(lab.Seats.All(seat => Mathf.Approximately(seat.Unit.Value(health), seat.Entity == veteran ? 90f : 30f)), Is.True, "at full");
            Assert.That(lab.Seats.Select(seat => seat.Unit.transform.position), Is.EqualTo(homes), "where it started");
        });

        /// <summary>A bot made anew by a reset keeps what the author gave it: its items, the abilities taught and their levels.</summary>
        [UnityTest]
        public IEnumerator LAB_9_A_reset_bot_keeps_what_it_was_given() => Live(async () =>
        {
            var bolt = Make.Ability("Bolt", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Hit("Bolt Hit", 20f) } }));
            var trinket = Make.Item("Trinket");
            var soldier = Fighter("Soldier");
            Lab(false, soldier, bolt, trinket);
            lab.SetMode(LabMode.Battle);
            var bot = lab.AddBot(soldier, LabSides.Blue, Vector3.zero);
            await Frames.Step();
            lab.Step(0.01f);

            Assert.IsTrue(lab.GiveItem(trinket, bot.Unit), "an item given");
            Assert.IsTrue(lab.Teach(bolt, bot.Unit), "an ability taught");
            lab.SetAbilityLevel(bolt, bot.Unit, 3);
            lab.ResetBattle();
            lab.Step(0.01f);
            var again = lab.Seats[0].Unit;
            Assert.AreNotSame(bot.Unit, again, "the reset made the bot anew");
            Assert.That(again.Items(), Has.Member(trinket), "with the item");
            Assert.That(again.Known(), Has.Member(bolt), "and the ability");
            Assert.AreEqual(3, again.LevelOf(bolt), "at its level");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // The author's unit (LAB-11)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The author's unit takes the lab's controls: a right-click on the ground sends it there, on a foe sets it on the
        /// foe, and on an ally walks to it; S stops it.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_11_A_right_click_moves_the_authors_unit_or_sets_it_on_a_foe() => Live(async () =>
        {
            var hero = Fighter("Hero");
            var foe = Fighter("Foe");
            Lab(false, hero, foe);
            var input = Press();
            var mine = lab.PutDownOwnUnit(hero, LabSides.Blue, Vector3.zero);
            var theirs = lab.AddUnit(foe, LabSides.Red, new Vector3(4f, 0f, 2f));
            var friend = lab.AddUnit(foe, LabSides.Blue, new Vector3(-4f, 0f, 2f));
            await Frames.Step(2);
            lab.Step(0.01f);
            Assert.AreSame(mine, lab.ControlledUnit, "the unit put down is the author's");
            Assert.IsNotNull(lab.Controls, "and the lab's controls take it");

            await Click(input, new Vector3(3f, 0f, -3f));
            Assert.IsTrue(mine.Destination.HasValue, "a right-click on the ground sends it there");
            Assert.AreEqual(3f, mine.Destination.Value.x, 0.1f);
            Assert.AreEqual(-3f, mine.Destination.Value.z, 0.1f);

            await Click(input, theirs.transform.position + Vector3.up);
            Assert.AreSame(theirs, mine.Target, "one on a foe sets it on the foe");

            await Click(input, friend.transform.position + Vector3.up);
            Assert.IsNull(mine.Target, "one on an ally walks to it");
            Assert.IsTrue(mine.Destination.HasValue);

            await Key(input, KeyCode.S);
            Assert.IsNull(mine.Destination, "S stops it");

            lab.ReleaseControl();
            await Click(input, new Vector3(-2f, 0f, -2f));
            Assert.IsNull(mine.Destination, "let go, it takes no orders");
        });

        /// <summary>
        /// The author's orders go through a driver: a project's, named on its Lab Rules, takes them all, the keys casting
        /// the unit's abilities past its attack in order, with no data, so click targeting waits for a left-click.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_11_A_projects_driver_takes_the_authors_orders() => Live(async () =>
        {
            var dash = Make.Ability("Dash", Make.Stage());
            var leap = Make.Ability("Leap", Make.Stage());
            var hero = Fighter("Hero", abilities: new[] { Strike(Hit("Blow", 5f)), dash, leap });
            var foe = Fighter("Foe");
            var driver = ScriptableObject.CreateInstance<RecordingDriver>();
            var rules = ScriptableObject.CreateInstance<LabRules>();
            try
            {
                rules.Driver = driver;
                lab = CreatorLab.Create(new ListLibrary("Test", new ScriptableObject[] { hero, foe, health, speed }), rules, panels: false);
                lab.Paused = true;
                var input = Press();
                var mine = lab.PutDownOwnUnit(hero, LabSides.Blue, Vector3.zero);
                var theirs = lab.AddUnit(foe, LabSides.Red, new Vector3(4f, 0f, 2f));
                await Frames.Step(2);
                lab.Step(0.01f);

                await Click(input, new Vector3(3f, 0f, -3f));
                Assert.AreEqual(1, driver.Moves.Count, "a right-click on the ground is the driver's move");
                Assert.AreSame(mine, driver.Moves[0].Unit);
                Assert.AreEqual(3f, driver.Moves[0].Point.x, 0.1f);
                Assert.IsNull(mine.Destination, "and the driver's alone: the unit goes nowhere of itself");

                await Click(input, theirs.transform.position + Vector3.up);
                Assert.AreEqual(1, driver.Attacks.Count, "one on a foe, its attack");
                Assert.AreSame(theirs, driver.Attacks[0].Target);

                await Key(input, KeyCode.Q);
                await Key(input, KeyCode.W);
                Assert.That(driver.Casts.Select(cast => cast.Ability).ToList(), Is.EqualTo(new List<Ability> { dash, leap }),
                    "Q and W cast its abilities past its attack, in order");

                await Key(input, KeyCode.S);
                Assert.That(driver.Stops, Is.EqualTo(new List<LabUnit> { mine }), "S is its stop");

                var own = ScriptableObject.CreateInstance<RecordingDriver>();
                try
                {
                    lab.Controls.Driver = own;
                    await Key(input, KeyCode.S);
                    Assert.AreEqual(1, own.Stops.Count, "a driver on the controls comes before the rules'");
                    Assert.AreEqual(1, driver.Stops.Count);
                }
                finally
                {
                    lab.Controls.Driver = null;
                    Object.DestroyImmediate(own);
                }
            }
            finally
            {
                if (lab) Object.DestroyImmediate(lab.gameObject);
                lab = null;
                Object.DestroyImmediate(rules);
                Object.DestroyImmediate(driver);
            }
        });

        /// <summary>
        /// The author takes control of a bot: it stops fighting until it's let go, a new fight passes it over, a reset keeps
        /// the author on its seat's new bot, and a unit taken out is let go.
        /// </summary>
        [UnityTest]
        public IEnumerator LAB_11_Taking_control_of_a_bot_stops_it_fighting_until_its_let_go() => Live(async () =>
        {
            var fighter = Fighter("Fighter", abilities: Strike(Hit("Blow", 1f)));
            Lab(false, fighter);
            lab.SetMode(LabMode.Battle);
            var mine = lab.AddBot(fighter, LabSides.Blue, Vector3.zero);
            var theirs = lab.AddBot(fighter, LabSides.Red, new Vector3(6f, 0f, 0f));
            await Frames.Step();
            lab.Step(0.01f);
            lab.Play();
            lab.Paused = true;
            Assert.IsTrue(mine.Fighting, "the bots fight");

            lab.TakeControl(mine.Unit);
            Assert.AreSame(mine.Unit, lab.ControlledUnit, "the author has the bot");
            Assert.IsFalse(mine.Fighting, "which stops fighting");
            Assert.IsTrue(theirs.Fighting, "while the others fight on");

            var seat = lab.SeatOf(mine.Unit);
            lab.ResetBattle();
            Assert.IsNotNull(seat.Unit, "a reset makes the seat's bot again");
            Assert.AreSame(seat.Unit, lab.ControlledUnit, "and the author keeps it");
            var again = seat.Unit.GetComponent<LabBot>();
            lab.Play();
            lab.Paused = true;
            Assert.IsFalse(again.Fighting, "a new fight passes it over");

            lab.ReleaseControl();
            Assert.IsNull(lab.ControlledUnit, "let go");
            Assert.IsTrue(again.Fighting, "it fights again");

            var gone = theirs.Unit;
            lab.TakeControl(gone);
            Assert.IsNull(lab.ControlledUnit, "a bot the reset took out can't be taken over");

            var red = lab.Seats.First(each => each.Side.Tag.Equals(LabSides.Red.Tag)).Unit;
            lab.TakeControl(red);
            lab.Follow(red);
            Assert.AreSame(red.transform, lab.View.Following, "the camera follows it");
            lab.RemoveUnit(red);
            Assert.IsNull(lab.ControlledUnit, "a unit taken out is let go");
            Assert.IsNull(lab.View.Following, "and no longer followed");
        });

        /// <summary>The Lab panel's Your unit section: Put down, Take control, Release and Follow, and whom the author has.</summary>
        [UnityTest]
        public IEnumerator LAB_11_The_lab_panel_has_a_section_for_the_authors_unit() => Live(async () =>
        {
            var hero = Fighter("Hero");
            Lab(true, hero);
            await Frames.Step(2);
            var root = lab.Panels.Root;
            Assert.IsNotNull(root.Q(name: "UnitSection"), "the Lab panel has a Your unit section");
            foreach (var name in new[] { "PutDownOwn", "TakeControl", "ReleaseControl", "FollowUnit" })
                Assert.IsNotNull(root.Q<Button>(name: name), $"with {name}");
            var line = root.Q<Label>(name: "ControlLine");
            Assert.IsNotNull(line, "and a line of whom the author has");

            var mine = lab.PutDownOwnUnit(hero, LabSides.Blue, Vector3.zero);
            lab.Panels.Redraw();
            Assert.That(line.text, Does.Contain(mine.name), "naming the unit");
            lab.ReleaseControl();
            lab.Panels.Redraw();
            Assert.That(line.text, Does.Not.Contain(mine.name), "and no longer once it's let go");
        });
    }
}
