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

namespace FarEmerald.PlayForge.Samples.Demo.Tests
{
    /// <summary>
    /// The demo, played in its own scene: the fight starts, every hero's abilities cast, the damage model's formulas hold,
    /// items and levels change what they should, Burning bursts at its sixth stack, and the AI fights back. Each test loads
    /// the scene and takes PlayForge down after.
    /// </summary>
    public class DemoTests
    {
        private DemoGame game;
        private DemoRoster roster;
        private string folder;

        // The panel settings the scene's UI documents draw on. As the HUD goes with its scene, Unity lets its panel go and
        // leaves UI Toolkit subscribed to its input provider, InputForUI, which then polls every frame, allocating, for
        // the rest of the run: every test after a Demo test measured it, the allocation tests among them (SP-28). A panel
        // disposed outright lets the provider go, so the tests open the panel again and dispose it, through the panel
        // settings' own members, as Unity has no public way; where a version has neither, nothing happens
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
            var rosterPath = AssetDatabase.FindAssets("t:DemoRoster").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            Assume.That(rosterPath, Is.Not.Null, "the demo is in the project");
            folder = Path.GetDirectoryName(rosterPath)?.Replace('\\', '/');
            roster = AssetDatabase.LoadAssetAtPath<DemoRoster>(rosterPath);

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode($"{folder}/Demo.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(5);
            panels.Clear();
            panels.AddRange(Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(document => document.panelSettings).Where(settings => settings != null).Distinct());
            game = DemoGame.Current;
            Assert.That(game, Is.Not.Null, "the scene has its game");
        }

        [UnityTearDown]
        public IEnumerator TakePlayForgeDown()
        {
            if (ProcessControl.Instance) ProcessControl.Instance.TerminateAllImmediately();
            yield return Frames(3);
            var empty = SceneManager.CreateScene($"After the demo {Time.frameCount}");
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
            // The HUD's panels opened again and disposed, and the input polling with them (SP-28)
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

        private static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float until = Time.time + seconds;
            while (!done() && Time.time < until) yield return null;
        }

        private T Asset<T>(string name) where T : Object =>
            AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>).FirstOrDefault(asset => asset != null && asset.name == name);

        private int HeroIndex(string name) => roster.Heroes.FindIndex(hero => hero.Identity.GetName() == name);

        /// <summary>The player as a hero, at the dummy, its level and items as given.</summary>
        private IEnumerator Face(string hero)
        {
            game.SetOpponent(DemoOpponent.TrainingDummy);
            game.SetHero(game.PlayerSeat, HeroIndex(hero));
            yield return Frames(3);
            game.Player.transform.position = game.Opponent.transform.position + Vector3.back * 2f;
            game.Player.Stop();
            yield return Frames(2);
        }

        private float DummyHealth => game.Opponent.Value(roster.Health);

        // ═══════════════════════════════════════════════════════════════════════════
        // Tests
        // ═══════════════════════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator The_scene_starts_a_fight()
        {
            Assert.That(ProcessControl.Instance, Is.Not.Null, "the Bootstrapper brought PlayForge up");
            Assert.That(game.Player != null && game.Player.Alive, "the player's hero stands");
            Assert.That(game.Opponent != null && game.Opponent.Alive, "and its opponent");
            Assert.That(game.Player.HealthShare, Is.EqualTo(1f).Within(0.01f), "at full health");
            Assert.That(game.Opponent.GetComponent<DemoAI>(), Is.Not.Null, "the opponent is an AI hero");
            Assert.That(DemoHud.Current, Is.Not.Null, "and the HUD is up");
            yield break;
        }

        /// <summary>Every ability of every hero casts, as the AI casts it, handed its target: its cooldown starts.</summary>
        [UnityTest]
        public IEnumerator Every_hero_casts_each_of_its_abilities()
        {
            var failed = new List<string>();
            foreach (var hero in roster.Heroes.Select(each => each.Identity.GetName()).ToList())
            {
                yield return Face(hero);
                var me = game.Player;
                var dummy = game.Opponent;
                for (int slot = 0; slot <= 3; slot++)
                {
                    var ability = me.AbilityAt(slot);
                    // Eviscerate is for a target below half its health
                    if (ability.GetName() == "Eviscerate")
                    {
                        dummy.Gas.TryModifyAttribute(roster.Health,
                            SourcedModifiedAttributeValue.GenerateSimple(dummy.Gas, roster.Health, -dummy.Value(roster.Health) * 0.7f, 0f));
                        yield return Frames(2);
                    }
                    me.transform.position = dummy.transform.position + Vector3.back * 2f;
                    bool cast = me.Cast(ability, me.DataAt(dummy));
                    yield return Until(() => me.CooldownLeft(ability) > 0f, 1.5f);
                    if (!cast || me.CooldownLeft(ability) <= 0f) failed.Add($"{hero}'s {ability.GetName()}");
                    yield return Until(() => !me.Casting(ability), 5f);
                    yield return Frames(2);
                }
            }
            Assert.That(failed, Is.Empty, "each ability started its cooldown");
        }

        /// <summary>A physical blow through the dummy's Armor: (30 + 1.2 × Strength) × 100 / (100 + Armor).</summary>
        [UnityTest]
        public IEnumerator Armor_takes_its_share_of_a_physical_blow()
        {
            yield return Face("Vanguard");
            var me = game.Player;
            float strength = me.Value(Asset<Attribute>("Strength"));
            float armor = game.Opponent.Value(Asset<Attribute>("Armor"));
            float before = DummyHealth;
            Assert.That(me.Cast(me.AbilityAt(0), me.DataAt(game.Opponent)), "Strike casts");
            yield return Until(() => DummyHealth < before, 2f);
            float expected = (30f + 1.2f * strength) * 100f / (100f + armor);
            Assert.That(before - DummyHealth, Is.EqualTo(expected).Within(0.5f), $"Strike at {strength} Strength through {armor} Armor");
        }

        /// <summary>A magic blow through the dummy's Magic Resistance, and a pure one through nothing.</summary>
        [UnityTest]
        public IEnumerator Magic_resistance_takes_its_share_and_pure_damage_goes_through()
        {
            yield return Face("Pyromancer");
            var me = game.Player;
            float intelligence = me.Value(Asset<Attribute>("Intelligence"));
            float resist = game.Opponent.Value(Asset<Attribute>("Magic Resistance"));

            float before = DummyHealth;
            Assert.That(me.Cast(me.AbilityAt(0), me.DataAt(game.Opponent)), "Ember casts");
            yield return Until(() => DummyHealth < before, 3f);
            Assert.That(before - DummyHealth, Is.EqualTo((20f + 0.9f * intelligence) * (1f - resist)).Within(0.5f), "Ember, magic, through Magic Resistance");

            yield return Seconds(0.2f);
            before = DummyHealth;
            var combustion = me.AbilityAt(3);
            Assert.That(me.Cast(combustion, me.DataAt(game.Opponent)), "Combustion casts");
            yield return Until(() => DummyHealth < before, 2f);
            Assert.That(before - DummyHealth, Is.EqualTo(50f + 0.5f * intelligence).Within(0.5f), "Combustion, pure, through nothing");
        }

        /// <summary>
        /// A blow's dealer's amplification makes it harder: Duelist's Edge's Attack Amplification on a physical blow,
        /// (30 + 1.2 × Strength) × (1 + Attack Amplification) × 100 / (100 + Armor), as the Physical Damage pipeline has it.
        /// </summary>
        [UnityTest]
        public IEnumerator Amplification_makes_a_blow_harder()
        {
            yield return Face("Vanguard");
            var me = game.Player;
            game.Give(game.PlayerSeat, Asset<Item>("Duelist's Edge"));
            yield return Frames(3);
            float amplification = me.Value(Asset<Attribute>("Attack Amplification"));
            Assert.That(amplification, Is.GreaterThan(0f), "Duelist's Edge gives Attack Amplification");
            float strength = me.Value(Asset<Attribute>("Strength"));
            float armor = game.Opponent.Value(Asset<Attribute>("Armor"));

            float before = DummyHealth;
            Assert.That(me.Cast(me.AbilityAt(0), me.DataAt(game.Opponent)), "Strike casts");
            yield return Until(() => DummyHealth < before, 2f);
            float expected = (30f + 1.2f * strength) * (1f + amplification) * 100f / (100f + armor);
            Assert.That(before - DummyHealth, Is.EqualTo(expected).Within(0.5f),
                $"Strike at {strength} Strength and {amplification} Attack Amplification through {armor} Armor");
        }

        /// <summary>An item's bonus shows apart, by its retention group, and follows the item's level; Health's base follows Strength.</summary>
        [UnityTest]
        public IEnumerator An_items_bonus_follows_its_level_and_goes_with_it()
        {
            yield return Face("Vanguard");
            var strength = Asset<Attribute>("Strength");
            var gauntlets = Asset<Item>("Iron Gauntlets");
            float bare = game.Player.Value(strength), health = game.Player.Base(roster.Health);

            game.Give(game.PlayerSeat, gauntlets);
            yield return Frames(3);
            Assert.That(game.Player.Value(strength), Is.EqualTo(bare + 8f).Within(0.01f), "Iron Gauntlets give 8 Strength");
            Assert.That(Bonus(strength), Is.EqualTo(8f).Within(0.01f), "which the Bonus retention group holds apart");
            Assert.That(game.Player.Base(roster.Health), Is.EqualTo(health + 80f).Within(0.5f), "and Health's base takes 10 for each");

            game.SetItemLevel(game.PlayerSeat, gauntlets, 2);
            yield return Frames(3);
            Assert.That(game.Player.Value(strength), Is.EqualTo(bare + 12f).Within(0.01f), "at level 2, 12");

            game.Take(game.PlayerSeat, gauntlets);
            yield return Frames(3);
            Assert.That(game.Player.Value(strength), Is.EqualTo(bare).Within(0.01f), "and taken back, none");
        }

        private float Bonus(Attribute attribute) =>
            game.Player.Gas.GetAttributeSystem().TryGetCachedValue(attribute, out CachedAttributeValue cached)
                ? cached.GetGroupValue(Tags.BONUS).CurrentValue : 0f;

        /// <summary>A hero's level raises its primaries, and an ability's level and Cooldown Reduction set its cooldown.</summary>
        [UnityTest]
        public IEnumerator Levels_and_cooldown_reduction_change_what_they_should()
        {
            yield return Face("Vanguard");
            var strength = Asset<Attribute>("Strength");
            float atOne = game.Player.Value(strength);
            game.SetLevel(game.PlayerSeat, 3);
            yield return Frames(3);
            Assert.That(game.Player.Value(strength), Is.EqualTo(atOne + 6f).Within(0.01f), "the Vanguard gains 3 Strength a level");

            game.Player.transform.position = game.Opponent.transform.position + Vector3.back * 2f;
            var bash = game.Player.AbilityAt(1);
            game.SetAbilityLevel(game.PlayerSeat, bash, 3);
            game.Give(game.PlayerSeat, Asset<Item>("Hourglass"));
            yield return Frames(3);
            Assert.That(game.Player.LevelOf(bash), Is.EqualTo(3), "Shield Bash is at level 3");
            Assert.That(game.Player.Cast(bash, game.Player.DataAt(game.Opponent)), "and casts");
            yield return Until(() => game.Player.CooldownLeft(bash) > 0f, 1f);
            Assert.That(game.Player.CooldownLeft(bash), Is.EqualTo(7f * 0.85f).Within(0.1f), "for its level's 7 seconds, less the Hourglass's 15%");
        }

        /// <summary>Burning stacks to five, and a sixth bursts as Flashpoint, which takes the burning off.</summary>
        [UnityTest]
        public IEnumerator Burning_bursts_at_its_sixth_stack()
        {
            yield return Face("Pyromancer");
            var burning = Asset<GameplayEffect>("Burning");
            var dummy = game.Opponent.Gas;
            var origin = IEffectOrigin.GenerateSourceDerivation(game.Player.Gas);
            for (int i = 0; i < 5; i++) dummy.ApplyGameplayEffect(dummy.GenerateEffectSpec(origin, burning));
            yield return Frames(2);
            Assert.That(dummy.TryGetEffectContainer(burning, out var container) && container is AbstractStackingEffectContainer { Stacks: 5 },
                "five stacks of Burning");

            float before = DummyHealth;
            dummy.ApplyGameplayEffect(dummy.GenerateEffectSpec(origin, burning));
            yield return Frames(3);
            Assert.That(dummy.TryGetEffectContainer(burning, out _), Is.False, "the sixth burst, and the burning's gone");
            Assert.That(DummyHealth, Is.LessThan(before), "as Flashpoint, which hurt");
        }

        /// <summary>
        /// An item's ability comes with it: Thunderclap stuns the dummy beside its holder, and a Healing Salve heals and uses
        /// one of its stack up.
        /// </summary>
        [UnityTest]
        public IEnumerator An_items_ability_comes_with_it()
        {
            yield return Face("Vanguard");
            game.Give(game.PlayerSeat, Asset<Item>("Thunder Hammer"));
            yield return Frames(3);
            var thunderclap = game.Player.ItemAbilityAt(0);
            Assert.That(thunderclap, Is.Not.Null, "the hammer's ability is its holder's");
            Assert.That(game.Player.Cast(thunderclap), "Thunderclap casts");
            yield return Until(() => game.Opponent.Has(roster.Stunned), 1f);
            Assert.That(game.Opponent.Has(roster.Stunned), "and stuns the dummy beside it");

            var salve = Asset<Item>("Healing Salve");
            game.Give(game.PlayerSeat, salve);
            game.Give(game.PlayerSeat, salve);
            yield return Frames(3);
            var held = game.PlayerSeat.Items.Find(each => each.Item == salve);
            Assert.That(held.Count, Is.EqualTo(2), "two salves, stacked");
            game.Player.Gas.TryModifyAttribute(roster.Health,
                SourcedModifiedAttributeValue.GenerateSimple(game.Player.Gas, roster.Health, -150f, 0f));
            yield return Frames(2);
            float hurt = game.Player.Value(roster.Health);
            Assert.That(game.Player.Cast(game.Player.ItemAbilityAt(game.PlayerSeat.Items.IndexOf(held))), "a salve's ability casts");
            yield return Seconds(1.5f);
            Assert.That(game.Player.Value(roster.Health), Is.GreaterThanOrEqualTo(hurt + 25f), "and heals");
            Assert.That(held.Count, Is.EqualTo(1), "using one up");
        }

        /// <summary>The AI hero fights: in a few seconds it reaches the player's hero, casts and hurts it.</summary>
        [UnityTest]
        public IEnumerator The_AI_hero_fights_back()
        {
            game.SetOpponent(DemoOpponent.AIHero);
            game.SetHero(game.OpponentSeat, HeroIndex("Ranger"));
            game.SetHero(game.PlayerSeat, HeroIndex("Vanguard"));
            yield return Frames(3);
            var ai = game.Opponent;
            yield return Seconds(8f);
            Assert.That(game.Player.HealthShare, Is.LessThan(1f), "the AI hurt the player's hero");
            Assert.That(Enumerable.Range(1, 3).Any(slot => ai.CooldownLeft(ai.AbilityAt(slot)) > 0f), "and cast an ability");
        }

        /// <summary>
        /// Each reach in the Demo is a Cast Range component, which the reach ring, the targeting that picks in range, the
        /// Check Range and the AI all read: every ability that reaches so far, and each hero's attack. The abilities kept
        /// their reach in Local Data under the roster's Reach key, and the heroes their attack range under its Attack Range
        /// key, which only the AI and the ring read, so a click cast a Fireball from across the arena (SP-37).
        /// </summary>
        [UnityTest]
        public IEnumerator SP_37_Each_reach_in_the_Demo_is_a_Cast_Range_component()
        {
            var expected = new Dictionary<string, float>
            {
                ["Strike"] = 2.4f, ["Shield Bash"] = 3.5f, ["Rallying Cry"] = 6f,
                ["Arrow"] = 9f, ["Hunter's Mark"] = 10f, ["Volley"] = 9f,
                ["Ember"] = 8f, ["Fireball"] = 10f, ["Flame Wall"] = 9f, ["Combustion"] = 9f,
                ["Cut"] = 2.2f, ["Shadow Step"] = 8f, ["Eviscerate"] = 3.5f,
                ["Spark"] = 8f, ["Drain Life"] = 8.5f, ["Hex"] = 9f,
                ["Thunderclap"] = 4f,
            };
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
                if (expected.TryGetValue(ability.GetName(), out float reach))
                {
                    if (range == null) wrong.Add($"{ability.GetName()} has no Cast Range");
                    else if (!range.Range.IsConstant || Mathf.Abs(range.Range.Constant - reach) > 0.001f)
                        wrong.Add($"{ability.GetName()}'s Cast Range is {range.Range.Constant}, not {reach}");
                }
                else if (range != null) wrong.Add($"{ability.GetName()} has a Cast Range, though it reaches anywhere");
                // A targeting that picks within a range takes the Cast Range, and holds the same number
                if (range != null && ability.Behaviour?.Targeting is SelectGasTargetInRangeTask inRange
                    && (!inRange.UseCastRange || Mathf.Abs(inRange.MaxTargetRange.Constant - range.Range.Constant) > 0.001f))
                    wrong.Add($"{ability.GetName()}'s targeting picks to {inRange.MaxTargetRange.Constant}, not its Cast Range");
                if (range != null && ability.Behaviour?.Targeting is AutoTargetByAffinityTask around
                    && (!around.UseCastRange || Mathf.Abs(around.Range.Constant - range.Range.Constant) > 0.001f))
                    wrong.Add($"{ability.GetName()}'s targeting reaches {around.Range.Constant}, not its Cast Range");
            }
            var rules = (ILabRules)roster;
            if (rules.ReachKey.IsValid) wrong.Add($"the roster still names a Reach key, {rules.ReachKey.Name}");
            if (rules.AttackRangeKey.IsValid) wrong.Add($"the roster still names an Attack Range key, {rules.AttackRangeKey.Name}");
            Assert.That(wrong, Is.Empty, string.Join("; ", wrong));

            // In play, each hero attacks from its attack's Cast Range, and reaches each of its casts by its own
            foreach (var hero in roster.Heroes.Select(each => each.Identity.GetName()).ToList())
            {
                yield return Face(hero);
                var me = game.Player;
                Assert.That(me.AttackRange, Is.EqualTo(expected[me.AttackAbility.GetName()]).Within(0.001f), $"{hero} attacks from its Cast Range");
                for (int slot = 1; slot <= 3; slot++)
                {
                    var ability = me.AbilityAt(slot);
                    float reach = expected.TryGetValue(ability.GetName(), out var given) ? given : 0f;
                    Assert.That(me.Reach(ability), Is.EqualTo(reach).Within(0.001f), $"{hero}'s {ability.GetName()} reaches as its Cast Range says");
                }
            }
        }

        // Whether an ability waits for the player's click to choose its target
        private static bool WaitsForAClick(Ability ability) => ability != null && ability.Behaviour.Targeting is SelectGasTargetTask
            or SelectGasTargetInRangeTask or SelectPositionTargetTask or SelectDirectionTargetTask or SelectGasOrPositionTargetTask;

        /// <summary>
        /// The HUD is short and centred under the arena, so the arena stays the player's to click, the hero's attributes in
        /// the panel; above it, what a cast waits for, and under it the keys. It stood a third of the screen tall, the
        /// attributes in it, and a cast waiting for its click said "left-click its target" whether it waited or ran (SP-35).
        /// </summary>
        [UnityTest]
        public IEnumerator SP_35_The_HUD_leaves_the_arena_clear_and_says_what_a_cast_waits_for()
        {
            Ability aimed = null;
            foreach (var hero in roster.Heroes.Select(each => each.Identity.GetName()).ToList())
            {
                yield return Face(hero);
                aimed = Enumerable.Range(1, 3).Select(game.Player.AbilityAt).FirstOrDefault(WaitsForAClick);
                if (aimed != null) break;
            }
            Assume.That(aimed, Is.Not.Null, "a hero's ability waits for a click");
            yield return Frames(3);

            var root = DemoHud.Current.GetComponent<UIDocument>().rootVisualElement;
            var hud = root.Q(name: "Hud");
            Assert.IsNull(hud.Q(name: "Attributes"), "the hero's attributes aren't in the HUD");
            Assert.IsNotNull(root.Q(name: "Panel").Q(name: "Attributes"), "but in the panel");
            Assert.That(hud.worldBound.height, Is.GreaterThan(0f).And.LessThan(root.worldBound.height * 0.2f),
                $"the HUD is short: {hud.worldBound.height} of {root.worldBound.height}");
            Assert.That(root.Q<Label>(name: "Keys")?.text ?? "", Does.Contain("Right-click: move or attack"), "the keys under it");

            var me = game.Player;
            Assert.IsTrue(me.Cast(aimed), $"{aimed.GetName()} is cast, and waits for its click");
            yield return Frames(3);
            var waiting = root.Q<Label>(name: "Waiting");
            Assert.That(waiting?.text ?? "", Does.Contain($"{aimed.GetName()}: Click").And.Contain("cancels"),
                "the HUD says what the cast waits for, and how to let it go");
            me.CancelCasts();
            yield return Frames(3);
            Assert.AreEqual(DisplayStyle.None, waiting.style.display.value, "and nothing once it's let go");
        }
    }
}
#endif
