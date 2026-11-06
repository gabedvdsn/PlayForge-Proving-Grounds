using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The Debugger, one window for the GAS, Process and Tag views (Q4), drawn as the design canvas's boards have it
    /// (version 21). It lists both kinds of hub from ProcessControl's registry (Q28) and follows their values in place while
    /// the game plays; outside Play mode it offers Play Mode. These tests run it in edit mode over a ProcessControl
    /// bootstrapped there, as the golden scenarios run, and tell it the game is live.
    /// </summary>
    public class RuntimeDebuggerTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Debugger = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.RuntimeDebugger");

        private ProcessControl previous;
        private GameObject host;
        private ProcessControl control;
        private object liveBefore;
        private Object[] selectionBefore;
        private readonly List<Object> made = new();

        [SetUp]
        public void Begin()
        {
            previous = ProcessControl.Instance;
            host = new GameObject("Debugger Process Control") { hideFlags = HideFlags.HideAndDontSave };
            control = host.AddComponent<ProcessControl>();
            var options = new SerializedObject(control);
            options.FindProperty("DontDestroyOnLoad").boolValue = false;
            options.FindProperty("CollectOnAwake").boolValue = false;
            options.ApplyModifiedPropertiesWithoutUndo();
            ProcessControl.Instance = null;
            control.Bootstrap();
            liveBefore = Debugger?.GetField("Live", AnyStatic)?.GetValue(null);
            selectionBefore = Selection.objects;
        }

        [TearDown]
        public void End()
        {
            if (Debugger != null)
            {
                foreach (var window in Resources.FindObjectsOfTypeAll(Debugger).Cast<EditorWindow>().ToList()) window.Close();
                if (liveBefore != null) Debugger.GetField("Live", AnyStatic).SetValue(null, liveBefore);
            }
            Selection.objects = selectionBefore;
            control.TerminateAllImmediately();
            Object.DestroyImmediate(host);
            ProcessControl.Instance = previous;
            foreach (var each in made)
                if (each != null) Object.DestroyImmediate(each);
            made.Clear();
        }

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens, as the smoke
        // suite's windows do. Every other error logged while the window is open is its own
        private sealed class WindowLog : IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public List<string> Errors { get; } = new();

            public WindowLog()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(NoDevice))
                    Errors.Add($"{type}: {message}");
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // Runs a test that opens the window, guarding the batch mode's "No graphic device" log, and checks no other error
        // was logged
        private static IEnumerator Guarded(IEnumerator test)
        {
            var log = new WindowLog();
            try
            {
                while (test.MoveNext()) yield return test.Current;
            }
            finally
            {
                // Closing a window logs the batch mode's error too
                if (Debugger != null)
                    foreach (var window in Resources.FindObjectsOfTypeAll(Debugger).Cast<EditorWindow>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the debugger was open");
        }

        private static IEnumerator Frames(int count = 4)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Entities built in code, as the harness builds them
        // ═══════════════════════════════════════════════════════════════════════════

        private T Own<T>(T asset) where T : Object
        {
            made.Add(asset);
            return asset;
        }

        private ForgeAttribute Attribute(string name)
        {
            var attribute = Own(ScriptableObject.CreateInstance<ForgeAttribute>());
            attribute.name = attribute.Name = name;
            attribute.Textures = new List<TextureItem>();
            return attribute;
        }

        private AttributeSet Set(params (ForgeAttribute attribute, float current, float @base)[] values)
        {
            var set = Own(ScriptableObject.CreateInstance<AttributeSet>());
            set.name = set.Name = "Debugger Set";
            set.AssetTag = Tag.Generate("Test.Debugger.Set");
            set.Attributes = values.Select(value => new AttributeSetElement
            {
                Attribute = value.attribute,
                Current = new AttributeMagnitudeSpec { Magnitude = value.current, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Base = new AttributeMagnitudeSpec { Magnitude = value.@base, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Overflow = new AttributeOverflowData { Policy = EAttributeOverflowPolicy.Unlimited },
                RetentionGroup = Tags.DEFAULT,
                Constraints = new AttributeConstraints { AutoClamp = true, RoundingMode = EAttributeRoundingPolicy.None },
            }).ToList();
            set.SubSets = new List<AttributeSet>();
            set.WorkerGroup = new StandardWorkerGroup();
            return set;
        }

        private static RuntimeEntityIdentity Identity(string name, AttributeSet set) => new()
        {
            Name = name,
            AssetTag = Tag.Generate($"Test.Debugger.{name.Replace(" ", "")}"),
            GrantedTags = new List<Tag>(),
            ContextTags = new List<Tag>(),
            Affiliation = new List<Tag>(),
            AttributeSet = set,
            MaxAbilitiesOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
            MaxItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
            MaxEquippedItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
        };

        // A runtime hub, registered as a game registers one
        private RuntimeGameplayAbilitySystem RuntimeHub(string name, AttributeSet set, out ProcessRelay relay)
        {
            var gas = new RuntimeGameplayAbilitySystem(Identity(name, set));
            Assert.IsTrue(ProcessControl.Register(gas, control, ProcessDataPacket.Default(), out relay), $"{name} registers");
            return gas;
        }

        // A MonoBehaviour hub in the scene, as ProcessControl collects one
        private GameplayAbilitySystem MonoHub(string name, AttributeSet set)
        {
            var identity = Own(ScriptableObject.CreateInstance<EntityIdentity>());
            identity.name = identity.Name = name;
            identity.Description = "";
            identity.Textures = new List<TextureItem>();
            identity.AssetTag = Tag.Generate($"Test.Debugger.{name.Replace(" ", "")}");
            identity.Affiliation = new List<Tag>();
            identity.GrantedTags = new List<Tag>();
            identity.ContextTags = new List<Tag>();
            identity.MaxLevel = 1;
            identity.MaxAbilitiesOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 };
            identity.MaxItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 };
            identity.MaxEquippedItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 };
            identity.StartingAbilities = new List<Ability>();
            identity.StartingItems = new List<StartingItemContainer>();
            identity.AttributeSet = set;
            identity.WorkerGroup = new StandardWorkerGroup();
            var body = Own(new GameObject(name) { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor });
            var gas = GameplayAbilitySystem.AddToGameObject(body, identity);
            gas.ProcessLifecycle = EProcessLifecycle.Synchronous;
            gas.ProcessTiming = EProcessStepTiming.UpdateAndLate;
            Assert.IsTrue(ProcessControl.Register(gas, control, ProcessDataPacket.Default(), out _), $"{name} registers");
            return gas;
        }

        private static EffectTagRequirements NoRequirements() => new()
        {
            ApplicationRequirements = new AvoidRequireTagGroup(),
            OngoingRequirements = new AvoidRequireTagGroup(),
            RemovalRequirements = new AvoidRequireTagGroup(),
        };

        // An effect that lasts, changes nothing, and grants the tags given
        private GameplayEffect Lasting(string name, float seconds, params Tag[] grants)
        {
            var effect = Own(ScriptableObject.CreateInstance<GameplayEffect>());
            effect.name = name;
            effect.Definition = new GameplayEffectDefinition { Name = name, Textures = new List<TextureItem>() };
            effect.Tags = new GameplayEffectTags
            {
                AssetTag = Tag.Generate($"Test.Debugger.{name.Replace(" ", "")}"),
                ContextTags = new List<Tag>(),
                GrantedTags = grants.ToList(),
            };
            effect.Workers = new List<AbstractEffectWorker>();
            effect.SourceRequirements = NoRequirements();
            effect.TargetRequirements = NoRequirements();
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AffiliationPolicy = EAffiliationPolicy.AlwaysAllow,
                ImpactTypes = new List<Tag>(),
                MagnitudeOperation = new ScalerMagnitudeOperation { RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Packets = Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Durational,
                DurationOperation = new ScalerMagnitudeOperation { Magnitude = seconds, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                TicksOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 0 },
                TickIntervalOperation = new ScalerMagnitudeOperation { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                AdditionalExecuteTicksOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 0 },
                StackAmountOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 1 },
                StackingConfigs = new List<AbstractStackingConfig>(),
                RealDeltaTime = EMagnitudeOperation.AddScaler,
            };
            return effect;
        }

        private Ability AbilityWithCooldown(string name, GameplayEffect cooldown)
        {
            var ability = Own(ScriptableObject.CreateInstance<Ability>());
            ability.name = name;
            ability.Definition = new AbilityDefinition { Name = name, Textures = new List<TextureItem>() };
            ability.Tags = new AbilityTags
            {
                AssetTag = Tag.Generate($"Test.Debugger.{name}"),
                ContextTags = new List<Tag>(),
                PassiveGrantedTags = new List<Tag>(),
                ActiveGrantedTags = new List<Tag>(),
                TagRequirements = new AbilityTagRequirements
                {
                    SourceRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
                    TargetRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
                },
            };
            ability.Behaviour = new AbilityBehaviour { Stages = new List<AbilityTaskBehaviourStage>() };
            ability.WorkerGroup = new StandardWorkerGroup();
            ability.Cooldown = cooldown;
            return ability;
        }

        private static bool Apply(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.ApplyGameplayEffect(gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(gas), effect));

        // ═══════════════════════════════════════════════════════════════════════════
        // Driving the window
        // ═══════════════════════════════════════════════════════════════════════════

        private static EditorWindow Open(bool live = true)
        {
            Assert.IsNotNull(Debugger, "one Debugger merges the GAS, Process and Tag views (Q4)");
            Debugger.GetField("Live", AnyStatic).SetValue(null, (Func<bool>)(() => live));
            return (EditorWindow)Debugger.GetMethod("Open", AnyStatic).Invoke(null, null);
        }

        // What the window does at each sample while the game plays
        private static void Sample(EditorWindow window) => Debugger.GetMethod("SampleNow", AnyInstance).Invoke(window, null);

        private static MultiColumnListView EntityList(EditorWindow window) => window.rootVisualElement.Q<MultiColumnListView>("Entities");

        private static List<object> Listed(MultiColumnListView list) => list.itemsSource.Cast<object>().ToList();

        private static IEnumerator Select(EditorWindow window, IGameplayAbilitySystem gas)
        {
            var list = EntityList(window);
            list.SetSelection(Listed(list).IndexOf(gas));
            yield return Frames();
            Sample(window);
        }

        private static VisualElement Detail(EditorWindow window) => window.rootVisualElement.Q("EntityDetail");

        private static void Click(Button button)
        {
            Assert.IsNotNull(button, "the button is there");
            using var click = NavigationSubmitEvent.GetPooled();
            click.target = button;
            button.SendEvent(click);
        }

        // A mouse press on a row or a cell: one click selects a row, and two set a value in place
        private static void Press(VisualElement element, int clicks = 1)
        {
            Assert.IsNotNull(element, "the element is there");
            using var down = MouseDownEvent.GetPooled(Vector2.zero, 0, clicks, Vector2.zero, EventModifiers.None);
            down.target = element;
            element.SendEvent(down);
        }

        private static IEnumerator ShowView(EditorWindow window, string toggleName)
        {
            var toggle = window.rootVisualElement.Q<ToolbarToggle>(toggleName);
            Assert.IsNotNull(toggle, $"the toolbar has {toggleName}");
            toggle.value = true;
            yield return Frames();
            Sample(window);
        }

        // A fact's value in a detail, as "Lifecycle" beside "SelfTerminating"
        private static Label Fact(VisualElement detail, string name) => detail?.Q(name)?.Q<Label>("Value");

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-4: the Entities view
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The GAS Debugger scanned the scene for MonoBehaviour hubs every 0.25 s, so a runtime hub never showed, and a
        /// hub that died while another spawned left the list stale (ED-4). The debugger lists each hub ProcessControl
        /// registers, of either kind, and drops one as it leaves.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_The_debugger_lists_each_hub_from_the_registry() => Guarded(ListsEachHub());

        private IEnumerator ListsEachHub()
        {
            var runtime = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out var relay);
            var mono = MonoHub("Mono Hero", Set((Attribute("Health"), 100f, 100f)));
            var window = Open();
            yield return Frames();
            Sample(window);

            var list = EntityList(window);
            Assert.IsNotNull(list, "the debugger lists the entities");
            Assert.That(list.columns.Select(column => column.title), Is.EqualTo(new[] { "Name", "Hub", "Lvl", "Effects" }),
                "by name, hub, level and effects, as the board has them");
            Assert.That(Listed(list), Has.Member(runtime), "a runtime hub, which the scan never found");
            Assert.That(Listed(list), Has.Member(mono), "and a MonoBehaviour hub");

            control.TerminateImmediate(relay.CacheIndex);
            Sample(window);
            Assert.That(Listed(EntityList(window)), Has.No.Member(runtime), "a hub that leaves the registry leaves the list");
            Assert.That(Listed(EntityList(window)), Has.Member(mono), "and the others stay");
        }

        /// <summary>
        /// The GAS Debugger cleared its details and built them again every 0.25 s, so a field was replaced while it was
        /// typed in (ED-4). A sample changes the values the detail shows, in place, under a header that says what the
        /// entity is.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_A_sample_changes_the_detail_in_place() => Guarded(ChangesInPlace());

        private IEnumerator ChangesInPlace()
        {
            var health = Attribute("Health");
            var foe = RuntimeHub("Runtime Foe", Set((health, 100f, 100f)), out _);
            var window = Open();
            yield return Frames();
            Sample(window);
            yield return Select(window, foe);

            var header = Detail(window)?.Q("EntityHeader");
            Assert.IsNotNull(header, "the detail has a header");
            Assert.AreEqual("Runtime Foe", header.Q<Label>("HeaderName").text, "with the entity's name");
            Assert.AreEqual("Entity · Runtime Foe · Runtime hub · Level 1", header.Q<Label>("HeaderDetail").text, "and what it is");
            Assert.IsFalse(header.Q<Button>("SelectInScene").enabledSelf, "a runtime hub has nothing in the scene to select");

            var row = Detail(window).Q("Attribute Health");
            Assert.IsNotNull(row, "the detail lists the entity's attributes");
            Assert.AreEqual("100", row.Q<Label>("Current").text, "with its current value");
            Assert.AreEqual("100", row.Q<Label>("Base").text, "and its base");

            foe.TryModifyAttribute(health, SourcedModifiedAttributeValue.GenerateUnworkable(foe, health, -30f, 0f));
            Sample(window);
            Assert.AreSame(row, Detail(window).Q("Attribute Health"), "a sample keeps the row");
            Assert.AreEqual("70", row.Q<Label>("Current").text, "and shows the new value");
        }

        /// <summary>
        /// The GAS Debugger showed an ability's cooldown as its effect's raw magnitude, before its scaler, and nowhere how
        /// long it had left; a badge printed a value as "current/base" or "min/current/max" with no label (ED-4). An
        /// ability on cooldown says how long it has left, an effect how long it has, and each column says what it holds.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_A_cooldown_shows_the_time_it_has_left() => Guarded(ShowsCooldown());

        private IEnumerator ShowsCooldown()
        {
            var cooldown = Lasting("Fireball Cooldown", 3f);
            var fireball = AbilityWithCooldown("Fireball", cooldown);
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            Assert.IsTrue(foe.GetAbilitySystem().GiveAbility(fireball, 1, out _), "the foe has Fireball");
            Assert.IsTrue(Apply(foe, cooldown), "and Fireball is cooling down");
            var window = Open();
            yield return Frames();
            Sample(window);
            yield return Select(window, foe);

            var ability = Detail(window)?.Q("Ability Fireball");
            Assert.IsNotNull(ability, "the detail lists the entity's abilities");
            Assert.AreEqual("Cooldown", ability.Q<Label>("State").text, "with its state");
            Assert.AreEqual("3.0 s", ability.Q<Label>("Cooldown").text, "and the time its cooldown has left");
            Assert.AreEqual("1", ability.Q<Label>("Level").text, "and its level");
            var titles = Detail(window).Q("Abilities_CONTAINER").Q(className: "pf-debugger__table-head").Query<Label>().ToList()
                .Select(label => label.text);
            Assert.That(titles, Is.EqualTo(new[] { "Ability", "Lvl", "State", "Cooldown" }), "under titles that say what each holds");

            var effect = Detail(window).Q("Effect Fireball Cooldown");
            Assert.IsNotNull(effect, "the detail lists the entity's effects");
            Assert.AreEqual("3.0 s", effect.Q<Label>("Left").text, "each with the time it has left");
            Assert.AreEqual("3.0 s of 3.0 s", effect.Q<Label>("Left").tooltip, "of its duration");
            Assert.AreEqual("1", effect.Q<Label>("Stacks").text, "its stacks");
            Assert.AreEqual("Self", effect.Q<Label>("From").text, "and where it came from");
        }

        /// <summary>
        /// The debugger named an entity through <c>ToGASObject</c>, which a runtime GAS answers with null, having no
        /// MonoBehaviour, so every runtime entity showed as "unknown" (GAS-S1).
        /// </summary>
        [Test]
        public void GAS_S1_The_debugger_names_a_runtime_entity()
        {
            Assert.IsNotNull(Debugger, "one Debugger merges the GAS, Process and Tag views (Q4)");
            var gas = new RuntimeGameplayAbilitySystem(Identity("Runtime Foe", Set((Attribute("Health"), 1f, 1f))));
            var nameOf = Debugger.GetMethod("NameOf", AnyStatic, null, new[] { typeof(IGameplayAbilitySystem) }, null);
            Assert.That(nameOf.Invoke(null, new object[] { gas }), Is.EqualTo("Runtime Foe"));
        }

        /// <summary>
        /// The debugger hooked three callbacks on each entity and never removed them, so a window closed in play mode
        /// kept sampling every entity, and reopening it added another set (ED-26). It listens to the entity it shows, and
        /// stops when it closes.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_26_Closing_the_debugger_removes_its_hooks() => Guarded(RemovesHooks());

        private IEnumerator RemovesHooks()
        {
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var hero = RuntimeHub("Runtime Hero", Set((Attribute("Health"), 100f, 100f)), out _);
            var window = Open();
            yield return Frames();
            Sample(window);
            Assert.IsFalse(foe.GetCallbacks().HasFrameCompleteListeners, "nothing listens before an entity is shown");

            yield return Select(window, foe);
            Assert.IsTrue(foe.GetCallbacks().HasFrameCompleteListeners, "the debugger listens to the entity it shows");
            Assert.IsFalse(hero.GetCallbacks().HasFrameCompleteListeners, "and to no other");

            window.Close();
            yield return null;
            Assert.IsFalse(foe.GetCallbacks().HasFrameCompleteListeners, "the closed debugger doesn't");
        }

        /// <summary>
        /// Outside play mode the GAS Debugger showed an empty window marked "EDITOR", while the Process and Tag
        /// debuggers offered Play Mode (Q28). The debugger says it's not in Play mode and offers it, as the board for
        /// outside Play mode draws it: the view toggles still switch, the search waits, and there's no status bar.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_In_edit_mode_the_debugger_offers_Play_Mode() => Guarded(OffersPlayMode());

        private IEnumerator OffersPlayMode()
        {
            var window = Open(live: false);
            yield return Frames();
            var root = window.rootVisualElement;
            var prompt = root.Q("Prompt");
            Assert.IsNotNull(prompt, "the debugger has a prompt");
            // The prompt and the views take turns in one place, each laid out while the other shows (ED-45)
            Assert.IsFalse(prompt.ClassListContains("pf-stack__hidden"), "which shows in edit mode");
            Assert.That(prompt.Query<Label>().ToList().Select(label => label.text), Has.Member("Not in Play mode."), "and says why");
            Assert.IsNotNull(prompt.Q<Button>("EnterPlayMode"), "with Enter Play Mode");
            Assert.IsTrue(root.Q("Views").ClassListContains("pf-stack__hidden"), "and the views wait");
            Assert.IsTrue(root.Q("DebuggerStatus").ClassListContains("pf-debugger__hidden"), "with no status bar");
            Assert.IsFalse(root.Q<ToolbarSearchField>("Search").enabledSelf, "and no search");

            var processes = root.Q<ToolbarToggle>("ShowProcesses");
            processes.value = true;
            yield return Frames();
            Assert.IsTrue(processes.value, "the view toggles still switch");
            Assert.IsFalse(root.Q<ToolbarToggle>("ShowEntities").value, "one view at a time");
        }

        /// <summary>
        /// The board's status bar counts what the game holds: ProcessControl's state, its processes by state, and its
        /// entities by hub (ED-4, ED-5). In a full window: a narrow one counts without the parts (ED-43), and a batch
        /// editor on Linux opens its windows narrow, where Windows' gives them no width at all.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_The_status_bar_counts_the_processes_and_entities() => Guarded(CountsInTheStatusBar());

        private IEnumerator CountsInTheStatusBar()
        {
            RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            RuntimeHub("Runtime Ally", Set((Attribute("Health"), 100f, 100f)), out _);
            MonoHub("Mono Hero", Set((Attribute("Health"), 100f, 100f)));
            var window = Open();
            yield return Frames();
            Debugger.GetMethod("Fit", AnyInstance, null, new[] { typeof(float) }, null).Invoke(window, new object[] { 1280f });
            Sample(window);

            var root = window.rootVisualElement;
            Assert.IsFalse(root.Q("DebuggerStatus").ClassListContains("pf-debugger__hidden"), "the status bar shows while the game plays");
            Assert.AreEqual($"ProcessControl: {control.State}", root.Q<Label>("ControlStatus").text, "with ProcessControl's state");
            int running = control.FetchActiveProcesses().Values.Count(pcb => pcb.State == EProcessState.Running);
            int waiting = control.FetchActiveProcesses().Values.Count(pcb => pcb.State == EProcessState.Waiting);
            StringAssert.StartsWith($"3 processes: {running} running, {waiting} waiting", root.Q<Label>("ProcessStatus").text,
                "its processes by state");
            Assert.AreEqual("3 entities: 1 Mono, 2 runtime", root.Q<Label>("EntityStatus").text, "and its entities by hub");
            StringAssert.StartsWith("Frame ", root.Q<Label>("Frame").text, "and the toolbar shows the frame");
        }

        /// <summary>
        /// The board's Follow Selection shows the entity whose GameObject is selected in the scene, and turned off, leaves
        /// the shown entity alone.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_Follow_Selection_shows_the_selected_hub() => Guarded(FollowsSelection());

        private IEnumerator FollowsSelection()
        {
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var hero = MonoHub("Mono Hero", Set((Attribute("Health"), 100f, 100f)));
            var window = Open();
            yield return Frames();
            Sample(window);
            yield return Select(window, foe);

            var follow = window.rootVisualElement.Q<ToolbarToggle>("FollowSelection");
            Assert.IsNotNull(follow, "the Entities view has Follow Selection");
            Assert.IsTrue(follow.value, "which is on at first");
            Selection.activeGameObject = hero.gameObject;
            yield return Frames();
            Sample(window);
            Assert.AreSame(hero, EntityList(window).selectedItem, "a hub's GameObject selected in the scene is the entity shown");
            Assert.AreEqual("Mono Hero", Detail(window).Q<Label>("HeaderName").text, "in the detail");

            follow.value = false;
            yield return Select(window, foe);
            Selection.activeGameObject = null;
            yield return Frames();
            Selection.activeGameObject = hero.gameObject;
            yield return Frames();
            Sample(window);
            Assert.AreSame(foe, EntityList(window).selectedItem, "turned off, it leaves the shown entity alone");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-40: the debugger's edits (Q27), in the sections' foot rows (canvas version 22)
        // ═══════════════════════════════════════════════════════════════════════════

        // What the last action did, on the status bar's right
        private static string ActionStatus(EditorWindow window) => window.rootVisualElement.Q<Label>("ActionStatus").text;

        // An ability whose one stage waits ten minutes, so it runs until something stops it
        private Ability Channel(string name)
        {
            var ability = AbilityWithCooldown(name, null);
            ability.Behaviour = new AbilityBehaviour
            {
                Stages = new List<AbilityTaskBehaviourStage>
                {
                    new() { Tasks = new List<AbstractAbilityTask> { new DelayTask { DelayMilliseconds = 600000 } } },
                },
            };
            return ability;
        }

        /// <summary>
        /// The debugger only watched, so an attribute could be changed only by code (ED-40, Q27). The Attributes foot row
        /// sets the selected attribute's base and current values, and the entity takes them as they are; a double-click on
        /// a value goes to its field.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_40_An_attribute_takes_the_value_typed_into_the_debugger() => Guarded(TakesAttributeValue());

        private IEnumerator TakesAttributeValue()
        {
            var health = Attribute("Health");
            var foe = RuntimeHub("Runtime Foe", Set((health, 100f, 100f)), out _);
            var window = Open();
            yield return Frames();
            Sample(window);
            yield return Select(window, foe);

            var detail = Detail(window);
            var foot = detail.Q("AttributeFoot");
            Assert.IsNotNull(foot, "the Attributes section has a foot row");
            var currentField = foot.Q<FloatField>("SelectedCurrent");
            var baseField = foot.Q<FloatField>("SelectedBase");
            Assert.AreEqual("None", foot.Q<Label>("SelectedAttribute").text, "which waits for a row");
            Assert.IsFalse(currentField.enabledSelf, "with its fields in place, disabled");

            var row = detail.Q("Attribute Health");
            Press(row);
            Assert.IsTrue(row.ClassListContains("pf-debugger__table-row--selected"), "a click selects the row");
            Assert.AreEqual("Health", foot.Q<Label>("SelectedAttribute").text, "and the foot row names it");
            Assert.IsTrue(currentField.enabledSelf, "with its fields on");
            Assert.AreEqual(100f, currentField.value, "showing its current value");
            Assert.AreEqual(100f, baseField.value, "and its base");
            Assert.IsTrue(currentField.isDelayed, "which take a value on Enter");

            currentField.value = 42f;
            Sample(window);
            Assert.IsTrue(foe.TryGetAttributeValue(health, out var value));
            Assert.AreEqual(42f, value.CurrentValue, "the entity takes it");
            Assert.AreEqual(100f, value.BaseValue, "and keeps its base");
            Assert.AreEqual("42", row.Q<Label>("Current").text, "which its row shows");
            Assert.AreEqual("Set Health's current value to 42", ActionStatus(window), "and the status bar says");

            baseField.value = 120f;
            Sample(window);
            foe.TryGetAttributeValue(health, out value);
            Assert.AreEqual(120f, value.BaseValue, "the base takes one too");
            Assert.AreEqual(42f, value.CurrentValue, "and leaves the current");

            Press(row.Q<Label>("Base"), clicks: 2);
            var focused = baseField.panel?.focusController?.focusedElement as VisualElement;
            Assert.IsTrue(focused == baseField || (focused != null && baseField.Contains(focused)), "a double-click on a value goes to its field");
        }

        /// <summary>
        /// The debugger couldn't add a tag, and one added straight to an entity's tag cache went at its next compile
        /// (ED-40, Q27). A tag added in the Tags foot row is a loose tag (FEAT-16), which lasts until it's removed, and
        /// Remove takes back the selected one, where it's loose.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_40_A_tag_added_in_the_debugger_lasts_until_its_removed() => Guarded(AddsATag());

        private IEnumerator AddsATag()
        {
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var window = Open();
            yield return Frames();
            Sample(window);
            yield return Select(window, foe);

            var detail = Detail(window);
            var foot = detail.Q("TagFoot");
            Assert.IsNotNull(foot, "the Tags section has a foot row");
            var name = foot.Q<TextField>("NewTag");
            var add = foot.Q<Button>("AddTag");
            var remove = foot.Q<Button>("RemoveTag");
            Assert.IsNotNull(name, "which takes a tag's name");
            Assert.IsNotNull(foot.Q<Button>("PickTag"), "or one of the project's tags");
            Assert.IsFalse(add.enabledSelf, "Add Tag waits for one");
            Assert.IsFalse(remove.enabledSelf, "and Remove for a row");

            name.value = "Status.Debugged";
            Assert.IsTrue(add.enabledSelf, "a name turns Add Tag on");
            Click(add);
            Sample(window);
            var debugged = Tag.Generate("Status.Debugged");
            foe.CompileGrantedTags();
            Assert.AreEqual(1, foe.GetTagWeight(debugged), "the entity has it, through a compile");
            Assert.AreEqual("Added Status.Debugged", ActionStatus(window), "and the status bar says so");
            Assert.AreEqual("", name.value, "and the field empties");

            Sample(window);
            var row = detail.Q("Tag Status.Debugged");
            Assert.IsNotNull(row, "its row shows");
            Assert.AreEqual("Status.", row.Q<Label>("Parents").text, "its parents");
            Assert.AreEqual("Debugged", row.Q<Label>("Name").text, "then its name");
            Assert.AreEqual("Loose", row.Q<Label>("From").text, "and what grants it");

            var granted = detail.Q("Tag Test.Debugger.RuntimeFoe");
            Assert.IsNotNull(granted, "the entity's asset tag shows too");
            Press(granted);
            Assert.IsFalse(remove.enabledSelf, "which Remove can't take back");
            Press(row);
            Assert.IsTrue(remove.enabledSelf, "but a loose tag it can");
            Assert.AreEqual("Remove", remove.text, "as plain Remove");
            Click(remove);
            Sample(window);
            Assert.That(foe.GetTagWeight(debugged), Is.LessThanOrEqualTo(0), "Remove takes it away");
            Assert.IsNull(detail.Q("Tag Status.Debugged"), "with its row");
            Assert.IsFalse(remove.enabledSelf, "and waits for another");
        }

        /// <summary>
        /// The debugger couldn't apply an effect or take one off (ED-40, Q27). An effect picked in the Effects foot row
        /// lands on the entity, from the entity itself, and Remove takes the selected one off.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_40_An_effect_applied_in_the_debugger_lands_and_its_row_removes_it() => Guarded(AppliesAnEffect());

        private IEnumerator AppliesAnEffect()
        {
            var burning = Lasting("Burning", 5f);
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var window = Open();
            yield return Frames();
            Sample(window);
            yield return Select(window, foe);

            var detail = Detail(window);
            var foot = detail.Q("EffectFoot");
            Assert.IsNotNull(foot, "the Effects section has a foot row");
            var pick = foot.Q<ObjectField>("NewEffect");
            var apply = foot.Q<Button>("ApplyEffect");
            var remove = foot.Q<Button>("RemoveEffect");
            Assert.IsNotNull(pick, "which takes an effect");
            Assert.IsFalse(apply.enabledSelf, "Apply waits for one");
            Assert.IsFalse(remove.enabledSelf, "and Remove for a row");
            pick.value = burning;
            Assert.IsTrue(apply.enabledSelf, "an effect turns Apply on");
            Click(apply);
            Sample(window);
            Assert.That(foe.GetEffectSystem().Shelf.Select(container => container.Spec.Base), Has.Member(burning), "it lands on the entity");
            Assert.AreEqual("Applied Burning", ActionStatus(window), "and the status bar says so");
            var row = detail.Q("Effect Burning");
            Assert.IsNotNull(row, "its row shows");

            Press(row);
            Assert.IsTrue(row.ClassListContains("pf-debugger__table-row--selected"), "a click selects the row");
            Assert.IsTrue(remove.enabledSelf, "which Remove takes");
            Assert.AreEqual("Remove", remove.text, "as plain Remove");
            Click(remove);
            // The entity clears a removed effect off its shelf at its next tick
            foe.GetEffectSystem().Tick();
            Sample(window);
            Assert.That(foe.GetEffectSystem().Shelf.Select(container => container.Spec.Base), Has.No.Member(burning), "and takes off");
            Assert.IsNull(detail.Q("Effect Burning"), "with its row");
            Assert.IsFalse(remove.enabledSelf, "and Remove waits for another");
        }

        /// <summary>
        /// The debugger couldn't grant an ability, set one off or stop it (ED-40, Q27). An ability picked in the Abilities
        /// foot row is granted at the level given, Activate sets the selected one off, and Interrupt, in place but off
        /// until it runs, stops it.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_40_An_ability_granted_in_the_debugger_activates_and_is_interrupted() => Guarded(GrantsAnAbility());

        private IEnumerator GrantsAnAbility()
        {
            var channel = Channel("Channel");
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var window = Open();
            yield return Frames();
            Sample(window);
            yield return Select(window, foe);

            var detail = Detail(window);
            var foot = detail.Q("AbilityFoot");
            Assert.IsNotNull(foot, "the Abilities section has a foot row");
            var pick = foot.Q<ObjectField>("NewAbility");
            var grant = foot.Q<Button>("GrantAbility");
            var activate = foot.Q<Button>("ActivateAbility");
            var interrupt = foot.Q<Button>("InterruptAbility");
            Assert.IsNotNull(pick, "which takes an ability");
            Assert.IsFalse(grant.enabledSelf, "Grant waits for one");
            pick.value = channel;
            foot.Q<IntegerField>("NewAbilityLevel").value = 1;
            Click(grant);
            Sample(window);
            Assert.That(foe.GetAbilitySystem().GetAbilityContainers().Select(container => container.Spec.Base), Has.Member(channel), "the entity has it");
            Assert.AreEqual("Granted Channel", ActionStatus(window), "and the status bar says so");
            var row = detail.Q("Ability Channel");
            Assert.IsNotNull(row, "its row shows");
            Assert.AreEqual("Ready", row.Q<Label>("State").text, "ready");

            Assert.IsFalse(activate.enabledSelf, "Activate waits for a row");
            Press(row);
            Assert.IsTrue(activate.enabledSelf, "and takes the selected one");
            Assert.AreEqual("Activate", activate.text, "as plain Activate");
            Assert.IsFalse(interrupt.enabledSelf, "while Interrupt stays in place, off, as it isn't running");
            Click(activate);
            Sample(window);
            Assert.AreEqual("Activated Channel", ActionStatus(window), "Activate sets it off");
            Assert.AreEqual("Stage 1/1", row.Q<Label>("State").text, "and its row says the stage it's in");
            Assert.IsTrue(interrupt.enabledSelf, "while Interrupt is on");

            Click(interrupt);
            yield return Frames();
            Sample(window);
            var container = foe.GetAbilitySystem().GetAbilityContainers().Single(each => each.Spec.Base == channel);
            Assert.IsFalse(container.IsActive, "Interrupt stops it");
            Assert.AreEqual("Interrupted Channel", ActionStatus(window), "as the status bar says");
            Assert.AreEqual("Ready", row.Q<Label>("State").text, "and its row is ready again");
            Assert.IsFalse(interrupt.enabledSelf, "with Interrupt off");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-4: the Tags view
        // ═══════════════════════════════════════════════════════════════════════════

        private static MultiColumnTreeView TagTree(EditorWindow window) => window.rootVisualElement.Q<MultiColumnTreeView>("Tags");

        // A tag path's row in the tree: its id, which the window keeps, and the counts its data holds
        private static int TagId(EditorWindow window, string path)
        {
            var ids = (Dictionary<string, int>)Debugger.GetField("tagIds", AnyInstance).GetValue(window);
            Assert.IsTrue(ids.TryGetValue(path, out int id), $"the tree has {path}");
            return id;
        }

        // The tree's data is the window's own node type, which its controller asks for by name
        private static (int Entities, int Weight) TagCounts(EditorWindow window, string path)
        {
            var nodeType = Debugger.GetNestedType("TagNode", BindingFlags.NonPublic);
            var dataFor = typeof(BaseTreeView).GetMethod(nameof(BaseTreeView.GetItemDataForId)).MakeGenericMethod(nodeType);
            var node = dataFor.Invoke(TagTree(window), new object[] { TagId(window, path) });
            Assert.IsNotNull(node, $"{path} is in the tree");
            var type = node.GetType();
            return ((int)type.GetField("Entities").GetValue(node), (int)type.GetField("Weight").GetValue(node));
        }

        /// <summary>
        /// The Tag Debugger listed one entity's tags in a window of its own (Q4). The Tags view holds every tag the
        /// entities hold, as a tree by its path, where a parent counts the entities that hold any tag under it and adds up
        /// their weights; the tag selected shows who holds it, and what grants it them.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_The_Tags_view_shows_each_tag_and_who_holds_it() => Guarded(ShowsTags());

        private IEnumerator ShowsTags()
        {
            var chilled = Tag.Generate("Status.Chilled");
            var frost = Lasting("Frost", 5f, chilled);
            var ice = Lasting("Ice", 5f, chilled);
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var ally = RuntimeHub("Runtime Ally", Set((Attribute("Health"), 100f, 100f)), out _);
            Assert.IsTrue(Apply(foe, frost) && Apply(foe, ice), "the foe is chilled twice over");
            Assert.IsTrue(Apply(ally, frost), "and the ally once");
            var window = Open();
            yield return Frames();
            yield return ShowView(window, "ShowTags");

            var tree = TagTree(window);
            Assert.IsNotNull(tree, "the Tags view is a tree");
            Assert.That(tree.columns.Select(column => column.title), Is.EqualTo(new[] { "Tag", "Entities", "Weight" }));
            Assert.AreEqual((2, 3), TagCounts(window, "Status.Chilled"), "a tag counts the entities that hold it, and their weights");
            Assert.AreEqual((2, 3), TagCounts(window, "Status"), "and its parent takes in its children's");
            Assert.IsTrue(tree.IsExpanded(TagId(window, "Status")), "which it shows, open");

            tree.SetSelectionById(TagId(window, "Status.Chilled"));
            yield return Frames();
            Sample(window);
            var detail = window.rootVisualElement.Q("TagDetail");
            Assert.AreEqual("Status.", detail.Q<Label>("HeaderParents").text, "the detail names the tag's parents");
            Assert.AreEqual("Chilled", detail.Q<Label>("HeaderName").text, "then the tag");
            Assert.AreEqual("Tag · 2 entities · weight 3", detail.Q<Label>("HeaderDetail").text, "and how widely it's held");

            var foeRow = detail.Q("Holder Runtime Foe");
            Assert.IsNotNull(foeRow, "Held By lists each entity that holds it");
            Assert.IsNotNull(detail.Q("Holder Runtime Ally"), "both of them");
            Assert.AreEqual("2", foeRow.Q<Label>("Weight").text, "with its weight");
            Assert.AreEqual("Frost, Ice", foeRow.Q("GrantedBy").Q<Label>("Name").text, "what grants it");
            Assert.AreEqual("Self", foeRow.Q<Label>("Source").text, "and where that came from");

            // A tag let go leaves the tree
            foreach (var container in foe.GetEffectSystem().Shelf.ToList()) foe.RemoveGameplayEffect(container.Spec.Base);
            foreach (var container in ally.GetEffectSystem().Shelf.ToList()) ally.RemoveGameplayEffect(container.Spec.Base);
            foe.GetEffectSystem().Tick();
            ally.GetEffectSystem().Tick();
            Sample(window);
            var nodes = (IDictionary)Debugger.GetField("tagNodes", AnyInstance).GetValue(window);
            Assert.IsFalse(nodes.Contains("Status.Chilled"), "a tag no entity holds leaves the tree");
            Assert.IsNotNull(window.rootVisualElement.Q("TagDetail").Q(className: "pf-note"), "and the detail");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-43: a narrow column
        // ═══════════════════════════════════════════════════════════════════════════

        private static readonly string[] Splits = { "EntitySplit", "ProcessSplit", "TagSplit" };

        /// <summary>
        /// The Debugger kept its wide layout in a docked column, so at 360 px its lists and details squeezed side by side
        /// and its tables clipped (ED-43, the user's rule in EDITOR_STYLE's Debugger section). Below 720 px each view's list
        /// sits above its detail, the toolbar's views take a row of their own, the frame count moves to the status bar, the
        /// Processes list keeps ID, Process, State and Step, and Held By drops Source. In a full window again, it's as it was.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_43_The_Debugger_fits_a_narrow_column() => Guarded(FitsANarrowColumn());

        private IEnumerator FitsANarrowColumn()
        {
            var chilled = Tag.Generate("Status.Chilled");
            var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            Assert.IsTrue(Apply(foe, Lasting("Frost", 5f, chilled)), "the foe is chilled");
            var window = Open();
            yield return Frames();
            yield return ShowView(window, "ShowTags");
            TagTree(window).SetSelectionById(TagId(window, "Status.Chilled"));
            yield return Frames();
            Sample(window);

            var fit = Debugger.GetMethod("Fit", AnyInstance, null, new[] { typeof(float) }, null);
            Assert.IsNotNull(fit, "the window fits its layout to its width");
            var root = window.rootVisualElement;
            var columns = ProcessList(window).columns;

            fit.Invoke(window, new object[] { 360f });
            Assert.IsTrue(root.ClassListContains("pf-debugger--narrow"), "a 360 px column is narrow");
            foreach (var name in Splits)
                Assert.AreEqual(TwoPaneSplitViewOrientation.Vertical, root.Q<TwoPaneSplitView>(name).orientation, $"{name}'s list sits above its detail");
            Assert.That(columns.Where(column => column.visible).Select(column => column.name), Is.EqualTo(new[] { "ID", "Process", "State", "Step" }),
                "the Processes list keeps ID, Process, State and Step");
            Assert.IsTrue(root.Q<Label>("Frame").ClassListContains(HiddenClass), "the frame count leaves the toolbar");
            var frame = root.Q<Label>("FrameStatus");
            Assert.IsNotNull(frame, "for the status bar");
            Assert.IsFalse(frame.ClassListContains(HiddenClass), "where it shows");
            Assert.That(frame.text, Does.StartWith("Frame "), "as the frame count");
            var sources = root.Q("TagDetail").Query(className: "pf-debugger__col-source").ToList();
            Assert.That(sources, Has.Count.EqualTo(2), "Held By's Source column, its title and its row");
            Assert.That(sources.All(cell => cell.ClassListContains(HiddenClass)), "drops out");

            fit.Invoke(window, new object[] { 1280f });
            Assert.IsFalse(root.ClassListContains("pf-debugger--narrow"), "a full window isn't narrow");
            foreach (var name in Splits)
                Assert.AreEqual(TwoPaneSplitViewOrientation.Horizontal, root.Q<TwoPaneSplitView>(name).orientation, $"{name}'s list sits beside its detail again");
            Assert.That(columns.All(column => column.visible), "the Processes list shows every column");
            Assert.IsFalse(root.Q<Label>("Frame").ClassListContains(HiddenClass), "the frame count is back on the toolbar");
            Assert.IsTrue(frame.ClassListContains(HiddenClass), "and off the status bar");
            Assert.That(root.Q("TagDetail").Query(className: "pf-debugger__col-source").ToList().All(cell => !cell.ClassListContains(HiddenClass)),
                "and Held By shows Source");
        }

        private const string HiddenClass = "pf-debugger__hidden";

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-45 and ED-48: a view's split and columns, through the other views
        // ═══════════════════════════════════════════════════════════════════════════

        // Each view's toggle and split
        private static readonly (string Toggle, string Split)[] SplitViews =
        {
            ("ShowEntities", "EntitySplit"), ("ShowProcesses", "ProcessSplit"), ("ShowTags", "TagSplit"), ("ShowTimeline", "TimelineSplit"),
        };

        // The window's content, moved into a runtime panel of a set size, where it's laid out: batch mode gives an editor
        // window no size on Windows, and a narrow one on Linux. It takes the window root's style sheets and classes, and
        // Unity's own editor style sheet, which styles the splits and the lists
        private sealed class LaidOut : IDisposable
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("Laid Out") { hideFlags = HideFlags.HideAndDontSave };
            private readonly EditorWindow window;
            public VisualElement Root { get; } = new() { name = "LaidOut" };

            public LaidOut(EditorWindow window, float width, float height)
            {
                this.window = window;
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var theme = EditorGUIUtility.isProSkin ? "Dark" : "Light";
                var unity = EditorGUIUtility.Load($"StyleSheets/Generated/DefaultCommon{theme}_inter.uss.asset") as StyleSheet;
                Assert.IsNotNull(unity, "Unity's editor style sheet loads");
                Root.styleSheets.Add(unity);
                var from = window.rootVisualElement;
                for (int i = 0; i < from.styleSheets.count; i++) Root.styleSheets.Add(from.styleSheets[i]);
                Size(width, height);
                foreach (var child in from.Children().ToList()) Root.Add(child);
                Mirror();
                document.rootVisualElement.Add(Root);
            }

            public void Size(float width, float height)
            {
                Root.style.width = width;
                Root.style.height = height;
            }

            // The window root's classes, as the narrow class the window sets on it to fit its width
            public void Mirror()
            {
                Root.ClearClassList();
                foreach (var name in window.rootVisualElement.GetClasses()) Root.AddToClassList(name);
            }

            // The content goes back to its window, which closes it as it closes
            public void Dispose()
            {
                if (window != null)
                    foreach (var child in Root.Children().ToList()) window.rootVisualElement.Add(child);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static void Fit(EditorWindow window, LaidOut laid, float width)
        {
            laid.Size(width, 720f);
            Debugger.GetMethod("Fit", AnyInstance, null, new[] { typeof(float) }, null).Invoke(window, new object[] { width });
            laid.Mirror();
        }

        private static IEnumerator Switch(EditorWindow window, LaidOut laid, string toggleName)
        {
            var toggle = laid.Root.Q<ToolbarToggle>(toggleName);
            Assert.IsNotNull(toggle, $"the toolbar has {toggleName}");
            toggle.value = true;
            yield return Frames();
            Sample(window);
            yield return Frames();
        }

        // The size of a split's fixed pane: the width of the list or the detail that keeps its width beside the other, or
        // in a narrow window, the height of the list above the detail
        private static float FixedPane(LaidOut laid, string split, bool narrow)
        {
            var pane = laid.Root.Q<TwoPaneSplitView>(split).fixedPane;
            Assert.IsNotNull(pane, $"{split} has its fixed pane");
            return narrow ? pane.layout.height : pane.layout.width;
        }

        // What a split's layout was, for a failure's message
        private static string Described(LaidOut laid, string split)
        {
            var view = laid.Root.Q<TwoPaneSplitView>(split);
            var pane = view?.fixedPane;
            var holder = view?.parent;
            return $" [{split} {view?.layout}, its pane {pane?.name} {pane?.layout} style {pane?.style.width.value.value}x{pane?.style.height.value.value} " +
                   $"basis {pane?.resolvedStyle.flexBasis}, its view {holder?.name} {holder?.layout} {string.Join(" ", holder?.GetClasses() ?? Array.Empty<string>())}]";
        }

        /// <summary>
        /// The Entities view lost its list: back from another view, the list's column was collapsed against the window's
        /// left edge, and only dragging the splitter brought it back (ED-45, the user's report). Each view's split keeps
        /// its size through the other views, in a full window and in a narrow column, and through Play mode's end and
        /// start, when the prompt takes the views' place.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_45_Each_view_keeps_its_split_through_the_other_views() => Guarded(KeepsItsSplit());

        private IEnumerator KeepsItsSplit()
        {
            RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var window = Open();
            bool live = true;
            Debugger.GetField("Live", AnyStatic).SetValue(null, (Func<bool>)(() => live));
            var showState = Debugger.GetMethod("ShowState", AnyInstance);
            yield return Frames();
            using var laid = new LaidOut(window, 1280f, 720f);
            yield return Frames();
            Assume.That(laid.Root.panel, Is.Not.Null, "the window's content is in a panel");

            foreach (var (width, narrow) in new[] { (1280f, false), (360f, true) })
            {
                Fit(window, laid, width);
                yield return Frames();
                foreach (var (toggle, split) in SplitViews)
                {
                    yield return Switch(window, laid, toggle);
                    float size = FixedPane(laid, split, narrow);
                    Assert.That(size, Is.GreaterThan(100f), $"{split}'s fixed pane is laid out at {width} px" + Described(laid, split));

                    foreach (var (other, _) in SplitViews)
                        if (other != toggle) yield return Switch(window, laid, other);
                    yield return Switch(window, laid, toggle);
                    Assert.That(FixedPane(laid, split, narrow), Is.EqualTo(size).Within(1f),
                        $"{split} keeps its size through the other views at {width} px" + Described(laid, split));

                    live = false;
                    showState.Invoke(window, null);
                    yield return Frames();
                    live = true;
                    showState.Invoke(window, null);
                    yield return Frames();
                    Assert.That(FixedPane(laid, split, narrow), Is.EqualTo(size).Within(1f),
                        $"and through Play mode's end and start at {width} px" + Described(laid, split));
                }
            }

            // A view hidden while the window changes width has its splitter where its panes meet once it shows again
            Fit(window, laid, 1280f);
            yield return Switch(window, laid, "ShowEntities");
            Fit(window, laid, 1400f);
            yield return Frames();
            yield return Switch(window, laid, "ShowProcesses");
            var processes = laid.Root.Q<TwoPaneSplitView>("ProcessSplit");
            var anchor = processes.Q("unity-dragline-anchor");
            Assert.IsNotNull(anchor, "the split has its splitter");
            // The panel runs what's scheduled on it by real time, not by frames
            var giveUp = EditorApplication.timeSinceStartup + 1.0;
            while (Mathf.Abs(anchor.worldBound.xMin - processes.fixedPane.worldBound.xMin) > 4f && EditorApplication.timeSinceStartup < giveUp)
                yield return null;
            Assert.That(anchor.worldBound.xMin, Is.EqualTo(processes.fixedPane.worldBound.xMin).Within(4f),
                "the Processes view's splitter sits where its list and detail meet, after the window widened while it was hidden");
        }

        // A column's width as its list lays it out
        private static float Laid(Column column) => (float)typeof(Column).GetProperty("desiredWidth", AnyInstance).GetValue(column);

        // Drags a column's right edge, as its header's handle does, once the header has laid its columns out
        private static IEnumerator Drag(VisualElement list, Column column, float by)
        {
            var header = list.Q(className: "unity-multi-column-header");
            Assert.IsNotNull(header, "the list has its header");
            var layout = header.GetType().GetProperty("columnLayout", AnyInstance)?.GetValue(header);
            Assert.IsNotNull(layout, "whose layout sizes the columns");
            var type = layout.GetType();
            // The header lays its columns out on its window's scheduler, which batch mode's window never runs
            yield return null;
            Assume.That(header.layout.width, Is.GreaterThan(100f), "the header is laid out");
            if ((bool)type.GetProperty("isDirty", AnyInstance).GetValue(layout)) header.GetType().GetMethod("DoLayout", AnyInstance).Invoke(header, null);
            type.GetMethod("BeginDragResize", AnyInstance).Invoke(layout, new object[] { column, 0f, false });
            type.GetMethod("DragResize", AnyInstance).Invoke(layout, new object[] { column, by });
            type.GetMethod("EndDragResize", AnyInstance).Invoke(layout, new object[] { column, false });
        }

        // The preference that keeps a table's column widths, which a test puts back as it was
        private sealed class KeptPreference : IDisposable
        {
            private readonly string key;
            private readonly bool had;
            private readonly string was;

            public KeptPreference(string key)
            {
                this.key = key;
                had = EditorPrefs.HasKey(key);
                was = EditorPrefs.GetString(key, "");
                EditorPrefs.DeleteKey(key);
            }

            public void Dispose()
            {
                if (had) EditorPrefs.SetString(key, was);
                else EditorPrefs.DeleteKey(key);
            }
        }

        /// <summary>
        /// A column's width wasn't remembered (ED-48, the user's report). A width dragged in a view's list stays through the
        /// other views, and the window remembers it: closed and opened again, as a domain reload builds it again, the
        /// column has the width it was dragged to.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_48_A_dragged_column_keeps_its_width() => Guarded(KeepsAColumnsWidth());

        private IEnumerator KeepsAColumnsWidth()
        {
            using var kept = new KeptPreference("PlayForge_Columns_Debugger.Entities");
            RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
            var window = Open();
            yield return Frames();
            float dragged;
            using (var laid = new LaidOut(window, 1280f, 720f))
            {
                yield return Frames();
                Fit(window, laid, 1280f);
                yield return Switch(window, laid, "ShowEntities");
                var list = laid.Root.Q<MultiColumnListView>("Entities");
                var hub = list.columns["Hub"];
                float before = Laid(hub);
                yield return Drag(list, hub, 20f);
                yield return Frames();
                dragged = Laid(hub);
                Assert.That(dragged, Is.GreaterThan(before + 5f), "the drag widens Hub");

                foreach (var (toggle, _) in SplitViews.Skip(1)) yield return Switch(window, laid, toggle);
                yield return Switch(window, laid, "ShowEntities");
                Assert.That(Laid(hub), Is.EqualTo(dragged).Within(1f), "Hub keeps its width through the other views");
            }

            window.Close();
            yield return Frames();
            window = Open();
            yield return Frames();
            var again = EntityList(window).columns["Hub"];
            Assert.That(again.width.value, Is.EqualTo(dragged).Within(1f), "and the window remembers it");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // FEAT-12: the Timeline
        // ═══════════════════════════════════════════════════════════════════════════

        // The game's time, held still until a test moves it
        private sealed class StoppedClock : IForgeClock
        {
            public float Now;
            public float DeltaTime => 0f;
            public float UnscaledDeltaTime => 0f;
            public float FixedDeltaTime => 0.02f;
            public float Time => Now;
            public float UnscaledTime => Now;
            public int FrameCount => 1;
        }

        // Two stages, each of a task that lands nothing, so the ability runs them both and ends as it activates
        private Ability TwoStages(string name)
        {
            var ability = AbilityWithCooldown(name, null);
            ability.Behaviour = new AbilityBehaviour
            {
                Stages = new List<AbilityTaskBehaviourStage>
                {
                    new() { Tasks = new List<AbstractAbilityTask> { new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect>() } } },
                    new() { Tasks = new List<AbstractAbilityTask> { new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect>() } } },
                },
            };
            return ability;
        }

        private static object Member(object of, string name)
        {
            var type = of.GetType();
            var field = type.GetField(name, AnyInstance);
            return field != null ? field.GetValue(of) : type.GetProperty(name, AnyInstance)?.GetValue(of);
        }

        private static string TrackName(object track) => $"{Member(track, "EntityName")} · {Member(track, "Name")}";

        /// <summary>
        /// The Debugger showed what an entity held now, never what it did over time (FEAT-12, Decision 30). Its Timeline
        /// view records, as the game plays, each run of an ability, stage by stage, a refused activation, and an effect's
        /// time on the shelf. A track selected shows its ability's graph, its stages and their tasks, beside its runs, or
        /// an effect's applications; and in a narrow column its list sits above its detail.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_12_The_Timeline_records_abilities_and_effects_as_they_run() => Guarded(RecordsATimeline());

        private IEnumerator RecordsATimeline()
        {
            var clock = new StoppedClock { Now = 10f };
            var before = ForgeTime.Use(clock);
            try
            {
                var foe = RuntimeHub("Runtime Foe", Set((Attribute("Health"), 100f, 100f)), out _);
                var abilities = foe.GetAbilitySystem();
                var combo = TwoStages("Combo");
                var cooling = Lasting("Channel Cooldown", 30f);
                var channel = AbilityWithCooldown("Channel", cooling);
                channel.Behaviour = new AbilityBehaviour
                {
                    Stages = new List<AbilityTaskBehaviourStage>
                    {
                        new() { Tasks = new List<AbstractAbilityTask> { new DelayTask { DelayMilliseconds = 600000 } } },
                    },
                };
                Assert.IsTrue(abilities.GiveAbility(combo, 1, out int comboSlot), "the foe knows Combo");
                Assert.IsTrue(abilities.GiveAbility(channel, 1, out int channelSlot), "and Channel");
                var frost = Lasting("Frost", 5f);
                var window = Open();
                yield return Frames();
                yield return ShowView(window, "ShowTimeline");

                bool Activate(int slot) => abilities.TryActivateAbility(abilities.CreateActivationRequest(slot, ProcessDataPacket.Default(),
                    EAbilityActivationPolicyExtended.ActivateIfIdle));
                Assert.IsTrue(Activate(comboSlot), "Combo activates, and ends as it does");
                clock.Now = 11f;
                Assert.IsTrue(Activate(channelSlot), "Channel activates, and goes on");
                Assert.IsTrue(Apply(foe, cooling), "its cooldown comes on");
                Assert.IsFalse(Activate(channelSlot), "and it's refused again while it cools down");
                Assert.IsTrue(Apply(foe, frost), "Frost lands");
                clock.Now = 13f;
                foe.RemoveGameplayEffect(frost);
                foe.GetEffectSystem().Tick();
                Sample(window);

                var tracks = window.rootVisualElement.Q<MultiColumnListView>("Tracks");
                Assert.IsNotNull(tracks, "the Timeline view lists its tracks");
                Assert.That(Listed(tracks).Select(TrackName),
                    Is.EquivalentTo(new[] { "Runtime Foe · Combo", "Runtime Foe · Channel", "Runtime Foe · Channel Cooldown", "Runtime Foe · Frost" }),
                    "a track for each ability that ran and each effect that landed");

                tracks.SetSelection(Listed(tracks).FindIndex(track => (string)Member(track, "Name") == "Combo"));
                yield return Frames();
                Sample(window);
                var detail = window.rootVisualElement.Q("TimelineDetail");
                Assert.AreEqual("Combo", detail.Q<Label>("HeaderName").text, "the detail names the ability");
                Assert.AreEqual("Ability · Runtime Foe · 1 run", detail.Q<Label>("HeaderDetail").text, "and how often it ran");
                var graph = detail.Q("Graph");
                Assert.IsNotNull(graph, "its graph shows");
                Assert.IsNotNull(graph.Q("Stage 1"), "its first stage");
                Assert.IsNotNull(graph.Q("Stage 2"), "and its second");
                Assert.AreEqual("Apply Effects", graph.Q("Stage 1").Q<Label>("Task").text, "each with its tasks");
                var run = detail.Q("Run 1");
                Assert.IsNotNull(run, "its run shows");
                Assert.AreEqual("10.0 s", run.Q<Label>("Start").text, "when it started");
                Assert.AreEqual("2/2", run.Q<Label>("Stages").text, "the stages it ran");
                Assert.AreEqual("Ended", run.Q<Label>("Outcome").text, "and how it ended");

                tracks.SetSelection(Listed(tracks).FindIndex(track => (string)Member(track, "Name") == "Channel"));
                yield return Frames();
                Sample(window);
                detail = window.rootVisualElement.Q("TimelineDetail");
                Assert.AreEqual("Running", detail.Q("Run 1").Q<Label>("Outcome").text, "a run that goes on is running");
                Assert.That(detail.Q("Run 2").Q<Label>("Outcome").text, Does.StartWith("Refused"), "and a refusal says so");

                tracks.SetSelection(Listed(tracks).FindIndex(track => (string)Member(track, "Name") == "Frost"));
                yield return Frames();
                Sample(window);
                detail = window.rootVisualElement.Q("TimelineDetail");
                Assert.AreEqual("Effect · Runtime Foe · 1 application", detail.Q<Label>("HeaderDetail").text, "an effect's detail counts its applications");
                var landed = detail.Q("Application 1");
                Assert.AreEqual("11.0 s", landed.Q<Label>("Start").text, "when it landed");
                Assert.AreEqual("2.0 s", landed.Q<Label>("Length").text, "and how long it held");

                Debugger.GetMethod("Fit", AnyInstance, null, new[] { typeof(float) }, null).Invoke(window, new object[] { 360f });
                Assert.AreEqual(TwoPaneSplitViewOrientation.Vertical, window.rootVisualElement.Q<TwoPaneSplitView>("TimelineSplit").orientation,
                    "in a narrow column, its list sits above its detail");
            }
            finally
            {
                ForgeTime.Use(before);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-5: the Processes view
        // ═══════════════════════════════════════════════════════════════════════════

        private sealed class BusyProcess : LazyRuntimeProcess
        {
            public BusyProcess(string name, EProcessLifecycle lifecycle)
                : base(name, EProcessStepPriorityMethod.Manual, 0, EProcessStepTiming.Update, lifecycle) { }

            // Each step takes about two milliseconds
            public override void WhenUpdate()
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalMilliseconds < 2.0) { }
            }
        }

        private static MultiColumnListView ProcessList(EditorWindow window) => window.rootVisualElement.Q<MultiColumnListView>("Processes");

        private static IEnumerator SelectProcess(EditorWindow window, ProcessControlBlock pcb)
        {
            var list = ProcessList(window);
            list.SetSelection(Listed(list).IndexOf(pcb));
            yield return Frames();
            Sample(window);
        }

        /// <summary>
        /// Process Control's usage was the wall-clock time between a process's steps, much the same for every process
        /// whatever its work cost (ED-5). A process's step time is how long its steps took over the last second, as
        /// ProcessControl times them while the debugger shows them, and its peak the longest.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_5_A_process_shows_how_long_its_steps_take() => Guarded(ShowsStepTime());

        private IEnumerator ShowsStepTime()
        {
            Assert.IsTrue(ProcessControl.Register(new BusyProcess("Busy", EProcessLifecycle.SelfTerminating), control, ProcessDataPacket.Default(), out var relay),
                "a busy process registers");
            var window = Open();
            yield return Frames();
            yield return ShowView(window, "ShowProcesses");
            var list = ProcessList(window);
            Assert.That(Listed(list).OfType<ProcessControlBlock>().Select(pcb => pcb.CacheIndex), Has.Member(relay.CacheIndex),
                "the debugger lists the processes");
            Assert.That(list.columns.Select(column => column.title),
                Is.EqualTo(new[] { "ID", "Process", "Handler", "Lifecycle", "Timing", "State", "Lifetime", "Step" }), "as the board has them");

            // ProcessControl steps its processes from Update, which edit mode doesn't call
            var update = typeof(ProcessControl).GetMethod("Update", AnyInstance);
            for (int i = 0; i < 3; i++) update.Invoke(control, null);
            Sample(window);

            Assert.IsTrue(control.TryGetPCB(relay.CacheIndex, out var pcb), "the busy process is registered");
            Assert.That(pcb.Steps, Is.EqualTo(3), "ProcessControl counts its steps while the debugger shows them");
            Assert.That(pcb.StepSeconds / 3, Is.GreaterThan(0.0015), "and times them");
            yield return SelectProcess(window, pcb);

            var detail = window.rootVisualElement.Q("ProcessDetail");
            Assert.AreEqual("Busy", detail.Q("ProcessHeader").Q<Label>("HeaderName").text, "the detail names the process");
            Assert.AreEqual($"Process {relay.CacheIndex} · Runtime process", detail.Q("ProcessHeader").Q<Label>("HeaderDetail").text,
                "and what it is");
            Assert.AreEqual("SelfTerminating", Fact(detail, "Process Lifecycle").text, "its lifecycle");
            Assert.AreEqual("Update", Fact(detail, "Process Timing").text, "the steps it takes");
            var stepTime = Fact(detail, "Process Step Time");
            Assert.IsNotNull(stepTime, "and how long they take");
            StringAssert.EndsWith(" milliseconds", stepTime.text);
            Assert.That(float.Parse(stepTime.text.Split(' ')[0], CultureInfo.InvariantCulture), Is.GreaterThan(1.5f), "a step, over the last second");
            var peak = Fact(detail, "Process Peak Step");
            StringAssert.EndsWith(" milliseconds", peak.text);
            Assert.That(float.Parse(peak.text.Split(' ')[0], CultureInfo.InvariantCulture), Is.GreaterThan(1.5f), "and the longest");
        }

        /// <summary>
        /// Process Control greyed out Pause for every running process that waits or needs control, as it read the state
        /// the process would take next, and its Pause and Resume took different children with them; its Terminated
        /// filter never matched, as a process leaves ProcessControl as it terminates (ED-5). A running process can be
        /// paused and resumed, and the filters are the states a listed process can be in.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_5_A_running_process_pauses_and_resumes() => Guarded(PausesAndResumes());

        private IEnumerator PausesAndResumes()
        {
            Assert.IsTrue(ProcessControl.Register(new BusyProcess("Waits", EProcessLifecycle.RunThenWait), control, ProcessDataPacket.Default(), out var relay),
                "a process that waits after its run registers");
            var window = Open();
            yield return Frames();
            yield return ShowView(window, "ShowProcesses");
            Assert.IsTrue(control.TryGetPCB(relay.CacheIndex, out var pcb));
            yield return SelectProcess(window, pcb);

            var detail = window.rootVisualElement.Q("ProcessDetail");
            Assert.AreEqual(EProcessState.Running, pcb.State, "the process runs");
            var pause = detail.Q<Button>("Pause");
            Assert.IsNotNull(pause, "the detail can pause it");
            Assert.IsTrue(pause.enabledSelf, "Pause is offered for a running process");
            Click(pause);
            Sample(window);
            Assert.AreEqual(EProcessState.Waiting, pcb.State, "Pause pauses it");

            var resume = detail.Q<Button>("Resume");
            Assert.IsTrue(resume.enabledSelf, "and Resume is offered");
            Click(resume);
            Sample(window);
            Assert.AreEqual(EProcessState.Running, pcb.State, "Resume runs it again");

            var filter = window.rootVisualElement.Q<ToolbarMenu>("Filter");
            Assert.IsNotNull(filter, "the processes have a filter");
            var choices = filter.menu.MenuItems().OfType<DropdownMenuAction>().Select(action => action.name).ToList();
            Assert.That(choices, Has.No.Member("Terminated"), "with no Terminated, which no listed process can be");
        }

        /// <summary>
        /// A paused process either holds its run, which a resume picks up, or had it cancelled, so a resume starts a new
        /// one (Decision 5). Nothing showed which (ED-5). The State section's Run says, from the process's own
        /// <c>RunIsHeld</c>, and a cancelled run says what Resume will do.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_5_The_Run_field_says_whether_a_paused_run_is_held() => Guarded(ShowsTheRun());

        private IEnumerator ShowsTheRun()
        {
            Assert.IsTrue(ProcessControl.Register(new BusyProcess("Waits", EProcessLifecycle.RunThenWait), control, ProcessDataPacket.Default(), out var relay),
                "a process that waits after its run registers");
            var window = Open();
            yield return Frames();
            yield return ShowView(window, "ShowProcesses");
            Assert.IsTrue(control.TryGetPCB(relay.CacheIndex, out var pcb));
            yield return SelectProcess(window, pcb);

            var detail = window.rootVisualElement.Q("ProcessDetail");
            var run = Fact(detail, "Process Run");
            Assert.IsNotNull(run, "the State section shows the run");
            Assert.IsTrue(pcb.RunIsHeld, "the process's run is going");
            Assert.AreEqual("Going", run.text, "which the field says");
            var cancelled = detail.Q<HelpBox>("RunCancelled");
            Assert.IsTrue(cancelled.ClassListContains("pf-debugger__hidden"), "with nothing to say of it");

            Click(detail.Q<Button>("Pause"));
            Sample(window);
            Assert.AreEqual(EProcessState.Waiting, pcb.State, "paused");
            Assert.AreEqual(pcb.RunIsHeld ? "Held" : "Cancelled", run.text, "it says whether the pause held the run");
            Assert.AreEqual(pcb.RunIsHeld, cancelled.ClassListContains("pf-debugger__hidden"), "and, cancelled, what Resume does");
            Assert.AreEqual("None", Fact(detail, "Process Queued").text, "with no state queued");
            Assert.IsTrue(Fact(detail, "Process Queued").ClassListContains("pf-debugger__fact-value--none"), "which shows as none");
        }
    }
}
