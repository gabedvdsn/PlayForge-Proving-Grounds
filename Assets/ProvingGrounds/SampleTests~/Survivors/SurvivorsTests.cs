#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Lab;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FarEmerald.PlayForge.Samples.Survivors.Tests
{
    /// <summary>
    /// The Survivors demo, played in its own scene (FEAT-40): the run starts with its hero and its first wave, every
    /// ability of the pool hurts the horde, every kind of the horde fights the hero, a level-up's cards teach and level
    /// abilities, a boss wave's end offers passives that raise what they name, the hero falls and a run starts again,
    /// hundreds of enemies stand at once, a volley goes on as its targets fall, and the shapes the game makes take their
    /// fields. Each test loads the scene, calms the waves where it sets its own fight, and takes PlayForge down after.
    /// </summary>
    public class SurvivorsTests
    {
        private SurvivorsGame game;
        private SurvivorsRoster roster;

        // The panel settings the scene's UI document draws on, disposed after as the Demo's tests do (SP-28)
        private readonly List<PanelSettings> panels = new();
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly PropertyInfo PanelOf = typeof(PanelSettings).GetProperty("panel", Members);
        private static readonly MethodInfo DisposePanel = typeof(PanelSettings).GetMethod("DisposePanel", Members, null, System.Type.EmptyTypes, null);

        // ═══════════════════════════════════════════════════════════════════════════
        // The scene
        // ═══════════════════════════════════════════════════════════════════════════

        [UnitySetUp]
        public IEnumerator LoadTheScene()
        {
            var rosterPath = AssetDatabase.FindAssets("t:SurvivorsRoster").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            Assume.That(rosterPath, Is.Not.Null, "the Survivors demo is in the project");
            var folder = Path.GetDirectoryName(rosterPath)?.Replace('\\', '/');
            roster = AssetDatabase.LoadAssetAtPath<SurvivorsRoster>(rosterPath);

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode($"{folder}/Survivors.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(5);
            panels.Clear();
            panels.AddRange(Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(document => document.panelSettings).Where(settings => settings != null).Distinct());
            game = SurvivorsGame.Current;
            Assert.That(game, Is.Not.Null, "the scene has its game");
        }

        [UnityTearDown]
        public IEnumerator TakePlayForgeDown()
        {
            Time.timeScale = 1f;
            if (ProcessControl.Instance) ProcessControl.Instance.TerminateAllImmediately();
            yield return Frames(3);
            var empty = SceneManager.CreateScene($"After Survivors {Time.frameCount}");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
            if (GameRoot.Instance) Object.Destroy(GameRoot.Instance.gameObject);
            if (ProcessControl.Instance) Object.Destroy(ProcessControl.Instance.gameObject);
            yield return null;
            ProcessControl.Instance = null;
            GameRoot.Instance = null;
            TagRegistry.Reset();
            if (PanelOf != null && DisposePanel != null)
                foreach (var settings in panels)
                {
                    if (settings == null) continue;
                    PanelOf.GetValue(settings);
                    DisposePanel.Invoke(settings, null);
                }
            panels.Clear();
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// The waves calmed: a copy of the roster that sends no one, starts no wave and levels the hero only as a test
        /// gives it a level's experience, as a level-up's cards stop time; the horde cleared, the hero at the middle and
        /// at full health, so a test sets its own fight.
        /// </summary>
        private IEnumerator Calm()
        {
            var calm = Object.Instantiate(roster);
            calm.FirstWaveCount = 0;
            calm.WaveGrowth = 0;
            calm.WaveCurve = 0f;
            calm.WaveSeconds = 100000f;
            calm.FirstLevel = 100000f;
            calm.LevelGrowth = 0f;
            calm.LevelCurve = 0f;
            game.Roster = calm;
            game.Horde.Clear();
            game.Crystals.Clear();
            yield return Frames(2);
            game.Hero.transform.position = Vector3.zero;
            game.Hero.Stop();
            Heal(game.Hero);
            yield return Frames(2);
        }

        private float Health(LabUnit unit) => unit ? unit.Value(roster.HealthAttribute) : 0f;

        private void Heal(LabUnit unit) =>
            unit.Gas.TryModifyAttribute(roster.HealthAttribute,
                SourcedModifiedAttributeValue.GenerateSimple(unit.Gas, roster.HealthAttribute, unit.Base(roster.HealthAttribute) - Health(unit), 0f));

        private void Hurt(LabUnit unit, float amount) =>
            unit.Gas.TryModifyAttribute(roster.HealthAttribute,
                SourcedModifiedAttributeValue.GenerateSimple(unit.Gas, roster.HealthAttribute, -amount, 0f));

        private SurvivorsEnemyKind KindOf(string name) =>
            roster.Enemies.Concat(roster.BossWaves.SelectMany(wave => wave.Units).Select(entry => entry.Kind))
                .FirstOrDefault(kind => kind?.Identity && kind.Identity.GetName() == name);

        // ═══════════════════════════════════════════════════════════════════════════
        // Tests
        // ═══════════════════════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator FEAT_40_The_run_starts_with_its_hero_knowing_its_first_ability_and_the_first_wave_coming()
        {
            Assert.That(game.Hero, Is.Not.Null, "the hero stands");
            Assert.That(game.Hero.Alive, Is.True);
            var first = roster.Hero.StartingAbilities[0];
            Assert.That(game.Hero.Known(), Does.Contain(first), "it knows its first ability");
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.Level, Is.EqualTo(1));
            yield return Until(() => game.Horde.Count > 0, 6f);
            Assert.That(game.Horde.Count, Is.GreaterThan(0), "the first wave's enemies come in");
        }

        /// <summary>
        /// The arena's floor and the crystals are shapes the game makes as it plays. A shape made its primitive as it woke,
        /// which a component added to an active object does as it's added, before its fields were set, so the floor and
        /// each crystal came out a plain white sphere a unit wide.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_40_The_shapes_the_game_makes_take_their_fields()
        {
            var floor = Object.FindObjectsByType<SurvivorsShape>(FindObjectsSortMode.None).FirstOrDefault(shape => shape.name == "Floor");
            Assert.That(floor, Is.Not.Null, "the arena has its floor");
            Assert.That(floor.Made, Is.Not.Null);
            Assert.That(floor.Made.localScale.x, Is.EqualTo(game.ArenaSize).Within(0.01f), "the floor is the arena's size");
            Assert.That(floor.Made.GetComponent<MeshFilter>().sharedMesh.name, Does.StartWith("Cube"), "the floor is a slab");
            Assert.That(floor.Made.GetComponent<Renderer>().HasPropertyBlock(), Is.True, "the floor is tinted");
            var block = new MaterialPropertyBlock();
            floor.Made.GetComponent<Renderer>().GetPropertyBlock(block);
            Assert.That(block.GetColor("_Color"), Is.EqualTo(game.FloorColor), "in the floor's color");

            yield return Calm();
            game.Crystals.Drop(new Vector3(9f, 0f, 9f), 1f);
            yield return null;
            var crystal = Object.FindObjectsByType<SurvivorsShape>(FindObjectsSortMode.None).FirstOrDefault(shape => shape.name == "Crystal");
            Assert.That(crystal, Is.Not.Null, "a crystal lies");
            Assert.That(crystal.Made.GetComponent<MeshFilter>().sharedMesh.name, Does.StartWith("Cube"), "a crystal is a cube");
            Assert.That(crystal.Made.localScale.x, Is.LessThan(0.5f), "a crystal is small");
        }

        [UnityTest]
        public IEnumerator FEAT_40_Each_ability_of_the_pool_casts_itself_and_hurts_the_horde()
        {
            yield return Calm();
            var hero = game.Hero;
            var shambler = KindOf("Shambler");
            Assert.That(shambler, Is.Not.Null, "the roster sends Shamblers");
            var failed = new List<string>();
            foreach (var ability in roster.AbilityPool)
            {
                foreach (var known in hero.Known().ToList()) hero.Forget(known);
                game.Horde.Clear();
                hero.transform.position = Vector3.zero;
                hero.Stop();
                yield return Frames(2);
                Assert.That(hero.Teach(ability, 1), Is.True, $"the hero learns {ability.GetName()}");
                // A ring of Shamblers within its reach; the hero mends as they claw it, so it stands to cast
                var standing = new List<SurvivorsHorde.Enemy>();
                for (int i = 0; i < 6; i++)
                {
                    float angle = Mathf.PI * 2f * i / 6f;
                    var enemy = game.Horde.Spawn(shambler, 1, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.5f, false);
                    if (enemy != null) standing.Add(enemy);
                }
                yield return Frames(2);
                var before = standing.Select(enemy => Health(enemy.Unit)).ToArray();
                yield return Until(() =>
                {
                    Heal(hero);
                    return standing.Where((enemy, i) => !enemy.Unit || Health(enemy.Unit) < before[i]).Any();
                }, 6f);
                bool hurt = standing.Where((enemy, i) => !enemy.Unit || Health(enemy.Unit) < before[i]).Any();
                if (!hurt) failed.Add(ability.GetName());
                Heal(hero);
            }
            Assert.That(failed, Is.Empty, "each ability hurts the horde within its reach");
        }

        /// <summary>
        /// A volley spaces its shots, so a target can fall, and its body be taken out, between them: Arcane Bolt at its
        /// last level, its targets felled as its first bolt flies, threw as its next read the gone one's place.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_40_A_volley_goes_on_as_its_targets_fall_between_its_shots()
        {
            yield return Calm();
            var hero = game.Hero;
            foreach (var known in hero.Known().ToList()) hero.Forget(known);
            game.Horde.Clear();
            yield return Frames(2);
            var bolt = roster.AbilityPool.First(ability => ability.GetName() == "Arcane Bolt");
            var shambler = KindOf("Shambler");
            var standing = new List<SurvivorsHorde.Enemy>();
            for (int i = 0; i < 3; i++) standing.Add(game.Horde.Spawn(shambler, 1, new Vector3(-2f + 2f * i, 0f, 6f), false));
            Assert.That(hero.Teach(bolt, bolt.MaxLevel), Is.True);
            yield return Until(() => hero.CooldownLeft(bolt) > 0f, 3f);
            Assert.That(hero.CooldownLeft(bolt), Is.GreaterThan(0f), "the hero cast its volley");
            foreach (var enemy in standing.Where(enemy => enemy.Unit)) Hurt(enemy.Unit, 100000f);
            yield return Until(() => standing.All(enemy => !enemy.Unit), 2f);
            Assert.That(standing.All(enemy => !enemy.Unit), Is.True, "its targets fell and were taken out mid-volley");
            // The rest of the volley flies on, aimed ahead, with nothing logged
            yield return Frames(30);
            Heal(hero);
        }

        /// <summary>
        /// Each reach in Survivors is a Cast Range component, as the Demo's are since SP-37: the hero casts each ability
        /// as an enemy stands within it, its targeting picks within it, and each enemy attacks and casts within its own. The
        /// abilities kept their reach in Local Data under the roster's Reach key, and each enemy its attack range under its
        /// Attack Range key. The melee kinds shared one Claw, so its reach couldn't be one number: they attack by their
        /// size's Bite, Claw, Maul or Smash (SP-38).
        /// </summary>
        [UnityTest]
        public IEnumerator SP_38_Each_reach_in_Survivors_is_a_Cast_Range_component()
        {
            var expected = new Dictionary<string, float>
            {
                ["Arcane Bolt"] = 14f, ["Cleave"] = 4.2f, ["Holy Aura"] = 3.6f, ["Orbiting Blades"] = 8f,
                ["Lightning Strike"] = 12f, ["Fire Field"] = 12f, ["Hurled Axe"] = 12f, ["Frost Nova"] = 5f,
                ["Bite"] = 1.1f, ["Claw"] = 1.4f, ["Maul"] = 1.7f, ["Smash"] = 2.3f, ["Detonate"] = 1.6f,
                ["Arrow Shot"] = 9f, ["Fire Bolt"] = 10f, ["Death Bolt"] = 10f, ["Mend"] = 30f, ["Charge"] = 7f,
                ["Phase"] = 30f, ["Slam"] = 4.5f, ["Bolt Nova"] = 14f, ["Raise Dead"] = 30f, ["Brood"] = 30f, ["Acid Spray"] = 11f,
            };
            var attacks = new Dictionary<string, string>
            {
                ["Shambler"] = "Claw", ["Bat"] = "Bite", ["Archer"] = "Arrow Shot", ["Brute"] = "Maul", ["Bomber"] = "Detonate",
                ["Shaman"] = "Fire Bolt", ["Charger"] = "Claw", ["Splitter"] = "Claw", ["Splitling"] = "Bite", ["Wraith"] = "Claw",
                ["Guardian"] = "Maul", ["Ogre Warlord"] = "Smash", ["Lich"] = "Death Bolt", ["Hive Queen"] = "Smash",
            };
            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(roster))?.Replace('\\', '/');
            var abilities = AssetDatabase.FindAssets("t:Ability", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Ability>).Where(ability => ability != null).ToList();
            var entities = AssetDatabase.FindAssets("t:EntityIdentity", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<EntityIdentity>).Where(entity => entity != null).ToList();
            var wrong = new List<string>();
            foreach (var asset in abilities.Cast<BaseForgeAsset>().Concat(entities))
                foreach (var data in asset.LocalData ?? new List<DataWrapper>())
                    if (data != null && data.Key.Name is { } key && (key.EndsWith(".Reach") || key.EndsWith(".AttackRange")))
                        wrong.Add($"{asset.name} keeps {key} in Local Data");
            foreach (var ability in abilities)
            {
                var range = ability.Find<CastRangeAbilityComponent>();
                // The dash goes its own distance, whatever point it's handed, so it has no reach (FEAT-41)
                if (ability == roster.Dash)
                {
                    if (range != null) wrong.Add("the Dash has a Cast Range, though it goes its own distance");
                    continue;
                }
                if (!expected.TryGetValue(ability.GetName(), out float reach)) wrong.Add($"{ability.GetName()} isn't expected");
                else if (range == null) wrong.Add($"{ability.GetName()} has no Cast Range");
                else if (!range.Range.IsConstant || Mathf.Abs(range.Range.Constant - reach) > 0.001f)
                    wrong.Add($"{ability.GetName()}'s Cast Range is {range.Range.Constant}, not {reach}");
                // A targeting that finds its own takes the Cast Range, and holds the same number
                if (range != null && ability.Behaviour?.Targeting is AutoTargetTask auto
                    && (!auto.UseCastRange || Mathf.Abs(auto.Range.Constant - range.Range.Constant) > 0.001f))
                    wrong.Add($"{ability.GetName()}'s targeting finds within {auto.Range.Constant}, not its Cast Range");
            }
            foreach (var entity in entities.Where(entity => attacks.ContainsKey(entity.GetName())))
            {
                var attack = entity.StartingAbilities.FirstOrDefault();
                if (attack == null || attack.GetName() != attacks[entity.GetName()])
                    wrong.Add($"{entity.GetName()} attacks with {(attack ? attack.GetName() : "nothing")}, not {attacks[entity.GetName()]}");
            }
            var rules = (ILabRules)roster;
            if (rules.ReachKey.IsValid) wrong.Add($"the roster still names a Reach key, {rules.ReachKey.Name}");
            if (rules.AttackRangeKey.IsValid) wrong.Add($"the roster still names an Attack Range key, {rules.AttackRangeKey.Name}");
            Assert.That(wrong, Is.Empty, string.Join("; ", wrong));

            // In play, the hero reaches each ability by its Cast Range, and each kind attacks from its attack's
            yield return Calm();
            var hero = game.Hero;
            foreach (var ability in roster.AbilityPool)
            {
                if (!hero.Known().Contains(ability)) hero.Teach(ability, 1);
                Assert.That(hero.Reach(ability), Is.EqualTo(expected[ability.GetName()]).Within(0.001f), $"the hero's {ability.GetName()}");
            }
            foreach (var kind in roster.Enemies.Concat(roster.BossWaves.SelectMany(wave => wave.Units).Select(entry => entry.Kind)))
            {
                var enemy = game.Horde.Spawn(kind, 1, new Vector3(30f, 0f, 30f), false);
                Assert.That(enemy.Unit.AttackRange, Is.EqualTo(expected[attacks[kind.Identity.GetName()]]).Within(0.001f),
                    $"a {kind.Identity.GetName()} attacks from its attack's Cast Range");
                game.Horde.Clear();
            }
        }

        [UnityTest]
        public IEnumerator FEAT_40_Each_kind_of_the_horde_fights_the_hero()
        {
            yield return Calm();
            var hero = game.Hero;
            var player = hero.GetComponent<SurvivorsHero>();
            player.enabled = false;
            var names = roster.Enemies.Select(kind => kind.Identity.GetName())
                .Concat(new[] { "Ogre Warlord", "Lich", "Hive Queen" }).ToList();
            var failed = new List<string>();
            foreach (var name in names)
            {
                var kind = KindOf(name);
                if (kind == null)
                {
                    failed.Add($"{name}: no kind");
                    continue;
                }
                game.Horde.Clear();
                hero.transform.position = Vector3.zero;
                Heal(hero);
                yield return Frames(2);
                float before = Health(hero);
                var enemy = game.Horde.Spawn(kind, 1, new Vector3(0f, 0f, 3f), false);
                yield return Until(() => Health(hero) < before, 8f);
                if (Health(hero) >= before) failed.Add(name);
            }
            player.enabled = true;
            Assert.That(failed, Is.Empty, "each kind hurts the hero");
        }

        [UnityTest]
        public IEnumerator FEAT_40_A_level_up_offers_three_ability_cards_and_taking_one_teaches_or_levels_it()
        {
            yield return Calm();
            game.Gain(game.ExperienceNeeded);
            yield return Until(() => game.Draft != null, 2f);
            Assert.That(game.Draft, Is.Not.Null, "a level-up's cards come");
            Assert.That(game.Level, Is.EqualTo(2));
            Assert.That(game.Draft.Kind, Is.EqualTo(ESurvivorsDraftKind.Ability));
            Assert.That(game.Draft.Cards.Count, Is.EqualTo(roster.AbilityCards));
            Assert.That(Time.timeScale, Is.EqualTo(0f), "time stands while the cards are up");
            var card = game.Draft.Cards[0];
            game.Choose(0);
            yield return Frames(2);
            Assert.That(game.Draft, Is.Null);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(game.Hero.Known(), Does.Contain(card.Ability), "the card's ability is held");
            Assert.That(game.Hero.LevelOf(card.Ability), Is.EqualTo(card.To), "at the level the card brings");
        }

        [UnityTest]
        public IEnumerator FEAT_40_A_boss_wave_defeated_offers_five_passive_cards_and_a_passive_raises_what_it_names()
        {
            yield return Calm();
            var boss = game.Horde.Spawn(KindOf("Brute"), 1, new Vector3(0f, 0f, 8f), true);
            Assert.That(game.Horde.BossesLeft, Is.EqualTo(1));
            Hurt(boss.Unit, 100000f);
            yield return Until(() => game.Draft != null, 3f);
            Assert.That(game.Draft, Is.Not.Null, "the passives' cards come as the boss wave falls");
            Assert.That(game.Draft.Kind, Is.EqualTo(ESurvivorsDraftKind.Passive));
            Assert.That(game.Draft.Cards.Count, Is.EqualTo(roster.PassiveCards));

            // A passive whose bonus is more at level 2 than at level 1: Multishot's and Piercing's step up only every few
            // levels, so the test took the first card at random and failed when one of them came first
            int chosen = game.Draft.Cards.FindIndex(card => RisesAtLevelTwo(card.Passive));
            Assert.That(chosen, Is.GreaterThanOrEqualTo(0), "the draft offers a passive that rises at its second level");
            var passive = game.Draft.Cards[chosen].Passive;
            var bonus = passive.GrantedEffects[0].ImpactSpecification;
            bool byBase = bonus.TargetImpact == EAttributeTarget.Base;
            float Read() => byBase ? game.Hero.Base(bonus.AttributeTarget) : game.Hero.Value(bonus.AttributeTarget);
            float before = Read();
            game.Choose(chosen);
            yield return Frames(2);
            Assert.That(game.Hero.Items(), Does.Contain(passive), "the passive is held");
            float first = Read();
            Assert.That(first, Is.GreaterThan(before), $"{passive.GetName()} raises {bonus.AttributeTarget.GetName()}");

            // A level more of it, from a draft that offers it
            SurvivorsDraft again = null;
            for (int i = 0; i < 200 && again == null; i++)
            {
                var draft = SurvivorsDraft.For(ESurvivorsDraftKind.Passive, game);
                if (draft != null && draft.Cards.Any(card => card.Passive == passive && !card.IsNew)) again = draft;
            }
            Assert.That(again, Is.Not.Null, "a later draft offers its next level");
            again.Apply(again.Cards.FindIndex(card => card.Passive == passive), game);
            yield return Frames(2);
            Assert.That(game.Hero.LevelOf(passive), Is.EqualTo(2), "it's at level 2");
            Assert.That(Read(), Is.GreaterThan(first), "its second level raises it more");
        }

        // An input that presses one key once, as a player's tap, and holds nothing
        private sealed class Tap : IForgeInput
        {
            public KeyCode Key;
            public bool Pending = true;
            public bool PointerPressed(int button) => false;
            public bool TryGetPointerRay(out Ray ray) { ray = default; return false; }
            public bool KeyPressed(KeyCode key)
            {
                if (!Pending || key != Key) return false;
                Pending = false;
                return true;
            }
        }

        /// <summary>
        /// Space casts the hero's dash the way it last walked, a short way ahead, and the horde's blows pass it by as it
        /// goes: the user's word of 2026-11-19 (FEAT-41).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_41_Space_dashes_the_hero_ahead_past_the_hordes_blows()
        {
            yield return Calm();
            var hero = game.Hero;
            var dash = roster.Dash;
            Assert.That(dash, Is.Not.Null, "the roster names the hero's dash");
            Assert.That(hero.Known(), Does.Contain(dash), "the hero starts with it");
            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(roster))?.Replace('\\', '/');
            var blow = AssetDatabase.FindAssets("Claw Hit t:GameplayEffect", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameplayEffect>).FirstOrDefault(effect => effect && effect.GetName() == "Claw Hit");
            Assert.That(blow, Is.Not.Null, "the horde's blow");
            Assert.That(blow.TargetRequirements.ApplicationRequirements.AvoidTags.Select(query => query.Tag.Name),
                Does.Contain("Survivors.State.Dashing"), "lands on no dashing hero");
            var dashing = new List<Tag> { Tag.Generate("Survivors.State.Dashing") };
            var shambler = game.Horde.Spawn(KindOf("Shambler"), 1, new Vector3(30f, 0f, -30f), false);
            var start = hero.transform.position;
            var previous = ForgeInput.Use(new Tap { Key = KeyCode.Space });
            try
            {
                yield return Until(() => hero.CooldownLeft(dash) > 0f, 2f);
                Assert.That(hero.CooldownLeft(dash), Is.GreaterThan(0f), "Space cast it");
                Assert.That(hero.HasAny(dashing), Is.True, "and the hero is dashing");
                float before = Health(hero);
                Strike(blow, shambler.Unit, hero);
                yield return Frames(2);
                Assert.That(Health(hero), Is.GreaterThanOrEqualTo(before - 0.01f), "the blow passes the dashing hero by");
                yield return Until(() => hero.transform.position.z - start.z > 4f, 1f);
                Assert.That(hero.transform.position.z - start.z, Is.GreaterThan(4f), "it dashed ahead, the way it faces");
            }
            finally
            {
                ForgeInput.Use(previous);
            }
            yield return Until(() => !hero.HasAny(dashing), 1f);
            Assert.That(hero.HasAny(dashing), Is.False, "the dash ends");
            float after = Health(hero);
            Strike(blow, shambler.Unit, hero);
            yield return Frames(2);
            Assert.That(Health(hero), Is.LessThan(after - 0.5f), "once it ends, the blow lands");
        }

        // An effect of the horde's lands on a unit, from the enemy given, as its ability would land it
        private static void Strike(GameplayEffect effect, LabUnit from, LabUnit onto) =>
            onto.Gas.ApplyGameplayEffect(onto.Gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(from.Gas), effect));

        /// <summary>The dash waits for its key: it never casts on its own, and no card offers it (FEAT-41).</summary>
        [UnityTest]
        public IEnumerator FEAT_41_The_dash_never_casts_on_its_own_nor_comes_as_a_card()
        {
            yield return Calm();
            var hero = game.Hero;
            var dash = roster.Dash;
            Assert.That(dash, Is.Not.Null, "the roster names the hero's dash");
            Assert.That(roster.AbilityPool.Contains(dash), Is.False, "it isn't in the pool the cards draw from");
            for (int i = 0; i < 3; i++)
                game.Horde.Spawn(KindOf("Shambler"), 1, new Vector3(Mathf.Cos(i * 2.1f), 0f, Mathf.Sin(i * 2.1f)) * 3f, false);
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                Heal(hero);
                yield return null;
            }
            Assert.That(hero.CooldownLeft(dash), Is.EqualTo(0f), "with enemies near, it still waits for its key");
            for (int i = 0; i < 50; i++)
            {
                var draft = SurvivorsDraft.For(ESurvivorsDraftKind.Ability, game);
                Assert.That(draft == null || draft.Cards.All(card => card.Ability != dash), Is.True, "no card offers it");
            }
        }

        /// <summary>
        /// Waves last 25 seconds, each sending its enemies as fast as a 15-second wave did, and the first boss waves are a
        /// little gentler: the Brute Pack four Brutes at the wave's level, the Ogre Warlord with one Brute and less Health
        /// (FEAT-41).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_41_Waves_last_25_seconds_and_the_first_boss_waves_are_gentler()
        {
            Assert.That(roster.WaveSeconds, Is.EqualTo(25f));
            Assert.That(game.WaveLeft, Is.GreaterThan(20f).And.LessThanOrEqualTo(25f), "the first wave's time");
            Assert.That(roster.CountFor(1) / roster.WaveSeconds, Is.EqualTo(14f / 15f).Within(0.05f), "as fast as a 15-second wave sent them");
            var pack = roster.BossWaves[0];
            Assert.That(pack.Units.Sum(entry => entry.Count), Is.LessThanOrEqualTo(4), "the Brute Pack");
            Assert.That(pack.Units.All(entry => entry.ExtraLevels == 0), Is.True, "at the wave's own level");
            var ogreWave = roster.BossWaves[1];
            Assert.That(ogreWave.Units.Where(entry => entry.Kind.Identity.GetName() != "Ogre Warlord").Sum(entry => entry.Count),
                Is.LessThanOrEqualTo(1), "the Ogre Warlord's escort");
            yield return Calm();
            var ogre = game.Horde.Spawn(ogreWave.Units.First(entry => entry.Kind.Identity.GetName() == "Ogre Warlord").Kind, 1,
                new Vector3(30f, 0f, 30f), false);
            Assert.That(ogre.Unit.Base(roster.HealthAttribute), Is.EqualTo(1000f).Within(0.5f), "the Ogre Warlord's Health");
        }

        // Whether a passive's bonus, a table by its level, is more at level 2 than at level 1
        private static bool RisesAtLevelTwo(Item passive) =>
            passive && passive.GrantedEffects.Count > 0
            && passive.GrantedEffects[0].ImpactSpecification.MagnitudeOperation.Scaler is AbstractScaler table
            && table.LevelValues != null && table.LevelValues.Length > 1 && table.LevelValues[1] > table.LevelValues[0];

        [UnityTest]
        public IEnumerator FEAT_40_A_kill_drops_a_crystal_the_hero_picks_up()
        {
            yield return Calm();
            var enemy = game.Horde.Spawn(KindOf("Shambler"), 1, new Vector3(1.5f, 0f, 0f), false);
            Hurt(enemy.Unit, 100000f);
            yield return Until(() => game.Kills > 0, 2f);
            Assert.That(game.Kills, Is.EqualTo(1));
            yield return Until(() => game.Experience > 0f || game.Level > 1, 3f);
            Assert.That(game.Experience > 0f || game.Level > 1, Is.True, "its crystal flew to the hero and gave its worth");
            Assert.That(game.Crystals.Count, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator FEAT_40_The_hero_falls_and_a_new_run_starts()
        {
            yield return Calm();
            Hurt(game.Hero, 100000f);
            yield return Until(() => game.Over, 3f);
            Assert.That(game.Over, Is.True, "the run ends as the hero falls");
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            game.StartRun();
            yield return Frames(3);
            Assert.That(game.Over, Is.False);
            Assert.That(game.Hero.Alive, Is.True);
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.Level, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator FEAT_40_Hundreds_of_enemies_stand_at_once_as_the_waves_grow()
        {
            Assert.That(roster.CountFor(20), Is.GreaterThanOrEqualTo(300), "the twentieth wave sends hundreds");
            yield return Calm();
            var shambler = KindOf("Shambler");
            var random = new System.Random(40);
            for (int i = 0; i < 400; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f, distance = 18f + (float)random.NextDouble() * 30f;
                game.Horde.Spawn(shambler, 1, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance, false);
            }
            Assert.That(game.Horde.Count, Is.EqualTo(400));
            int frames = 0;
            float started = Time.realtimeSinceStartup;
            while (frames < 120)
            {
                frames++;
                yield return null;
            }
            float each = (Time.realtimeSinceStartup - started) / frames * 1000f;
            Debug.Log($"[Survivors] 400 enemies: {each:0.0} ms a frame over {frames} frames, {ProcessControl.Instance.Active} processes");
            Assert.That(game.Horde.Count, Is.GreaterThan(350), "they stand, the hero's bolts felling a few");
            Assert.That(game.Hero.Alive, Is.True);
        }
    }
}
#endif
