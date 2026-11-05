using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// The Runtime Debugger, one window for the GAS, Process and Tag views (Q4). It lists both kinds of hub from
    /// ProcessControl's registry (Q28) and follows their values in place while the game plays; in edit mode it offers
    /// Play Mode. These tests run it in edit mode over a ProcessControl bootstrapped there, as the golden scenarios run,
    /// and tell it the game is live.
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
        }

        [TearDown]
        public void End()
        {
            if (Debugger != null)
            {
                foreach (var window in Resources.FindObjectsOfTypeAll(Debugger).Cast<EditorWindow>().ToList()) window.Close();
                if (liveBefore != null) Debugger.GetField("Live", AnyStatic).SetValue(null, liveBefore);
            }
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

        // An effect that lasts, and changes nothing
        private GameplayEffect Lasting(string name, float seconds)
        {
            var effect = Own(ScriptableObject.CreateInstance<GameplayEffect>());
            effect.name = name;
            effect.Definition = new GameplayEffectDefinition { Name = name, Textures = new List<TextureItem>() };
            effect.Tags = new GameplayEffectTags
            {
                AssetTag = Tag.Generate($"Test.Debugger.{name}"),
                ContextTags = new List<Tag>(),
                GrantedTags = new List<Tag>(),
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
            Assert.IsNotNull(Debugger, "one Runtime Debugger merges the GAS, Process and Tag views (Q4)");
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

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-4: the GAS view
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

            Assert.IsNotNull(EntityList(window), "the debugger lists the entities");
            Assert.That(Listed(EntityList(window)), Has.Member(runtime), "a runtime hub, which the scan never found");
            Assert.That(Listed(EntityList(window)), Has.Member(mono), "and a MonoBehaviour hub");

            control.TerminateImmediate(relay.CacheIndex);
            Sample(window);
            Assert.That(Listed(EntityList(window)), Has.No.Member(runtime), "a hub that leaves the registry leaves the list");
            Assert.That(Listed(EntityList(window)), Has.Member(mono), "and the others stay");
        }

        /// <summary>
        /// The GAS Debugger cleared its details and built them again every 0.25 s, so a field was replaced while it was
        /// typed in (ED-4). A sample changes the values the detail shows, in place.
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

            var row = Detail(window)?.Q("Attribute Health");
            Assert.IsNotNull(row, "the detail lists the entity's attributes");
            Assert.AreEqual(100f, row.Q<FloatField>("Current").value, "with its current value");
            Assert.AreEqual(100f, row.Q<FloatField>("Base").value, "and its base");

            foe.TryModifyAttribute(health, SourcedModifiedAttributeValue.GenerateUnworkable(foe, health, -30f, 0f));
            Sample(window);
            Assert.AreSame(row, Detail(window).Q("Attribute Health"), "a sample keeps the row");
            Assert.AreEqual(70f, row.Q<FloatField>("Current").value, "and shows the new value");
        }

        /// <summary>
        /// The GAS Debugger showed an ability's cooldown as its effect's raw magnitude, before its scaler, and nowhere how
        /// long it had left; a badge printed a value as "current/base" or "min/current/max" with no label (ED-4). An
        /// ability on cooldown says how long it has left, and every value says what it is.
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

            var state = Detail(window)?.Q("Ability Fireball")?.Q<Label>("State");
            Assert.IsNotNull(state, "the detail lists the entity's abilities");
            StringAssert.StartsWith("Cooldown", state.text);
            StringAssert.Contains("3.0 s", state.text, "with the time it has left");

            var remaining = Detail(window).Q("Effect Fireball Cooldown")?.Q<Label>("Remaining");
            Assert.IsNotNull(remaining, "the detail lists the entity's effects");
            Assert.AreEqual("3.0 s of 3.0 s", remaining.text, "each with the time it has left of its duration");

            var level = Detail(window).Q("Ability Fireball").Q<Label>("Level");
            Assert.AreEqual("Level 1", level.text, "a level says it is one");
        }

        /// <summary>
        /// The debugger named an entity through <c>ToGASObject</c>, which a runtime GAS answers with null, having no
        /// MonoBehaviour, so every runtime entity showed as "unknown" (GAS-S1).
        /// </summary>
        [Test]
        public void GAS_S1_The_debugger_names_a_runtime_entity()
        {
            Assert.IsNotNull(Debugger, "one Runtime Debugger merges the GAS, Process and Tag views (Q4)");
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
        /// debuggers offered Play Mode (Q28). The debugger offers Play Mode.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_4_In_edit_mode_the_debugger_offers_Play_Mode() => Guarded(OffersPlayMode());

        private IEnumerator OffersPlayMode()
        {
            var window = Open(live: false);
            yield return Frames();
            var prompt = window.rootVisualElement.Q("Prompt");
            Assert.IsNotNull(prompt, "the debugger has a prompt");
            Assert.IsFalse(prompt.ClassListContains("pf-debugger__hidden"), "which shows in edit mode");
            Assert.IsNotNull(prompt.Q<Button>("EnterPlayMode"), "with Enter Play Mode");
            Assert.IsTrue(window.rootVisualElement.Q("Views").ClassListContains("pf-debugger__hidden"), "and the views wait");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-5: the Process view
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

        private static IEnumerator ShowProcesses(EditorWindow window)
        {
            var toggle = window.rootVisualElement.Q<ToolbarToggle>("ShowProcesses");
            Assert.IsNotNull(toggle, "the toolbar switches to the processes");
            toggle.value = true;
            yield return Frames();
            Sample(window);
        }

        /// <summary>
        /// Process Control's usage was the wall-clock time between a process's steps, much the same for every process
        /// whatever its work cost (ED-5). A process's step column says how long its steps take, as ProcessControl times
        /// them while the debugger shows them.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_5_A_process_shows_how_long_its_steps_take() => Guarded(ShowsStepTime());

        private IEnumerator ShowsStepTime()
        {
            Assert.IsTrue(ProcessControl.Register(new BusyProcess("Busy", EProcessLifecycle.SelfTerminating), control, ProcessDataPacket.Default(), out var relay),
                "a busy process registers");
            var window = Open();
            yield return Frames();
            yield return ShowProcesses(window);
            Assert.That(Listed(ProcessList(window)).OfType<ProcessControlBlock>().Select(pcb => pcb.CacheIndex), Has.Member(relay.CacheIndex),
                "the debugger lists the processes");

            // ProcessControl steps its processes from Update, which edit mode doesn't call
            var update = typeof(ProcessControl).GetMethod("Update", AnyInstance);
            for (int i = 0; i < 3; i++) update.Invoke(control, null);
            Sample(window);

            Assert.IsTrue(control.TryGetPCB(relay.CacheIndex, out var pcb), "the busy process is registered");
            var steps = typeof(ProcessControlBlock).GetProperty("Steps")?.GetValue(pcb);
            var seconds = typeof(ProcessControlBlock).GetProperty("StepSeconds")?.GetValue(pcb);
            Assert.That(steps, Is.EqualTo(3), "ProcessControl counts its steps while the debugger shows them");
            Assert.That((double)seconds / 3, Is.GreaterThan(0.0015), "and times them");
            var list = ProcessList(window);
            list.SetSelection(Listed(list).IndexOf(pcb));
            yield return Frames();
            Sample(window);
            var stepLabel = window.rootVisualElement.Q("ProcessDetail")?.Q<Label>("StepTime");
            Assert.IsNotNull(stepLabel, "the selected process's detail");
            StringAssert.EndsWith(" ms per step", stepLabel.text, "says how long its steps take");
            Assert.That(float.Parse(stepLabel.text.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(1.5f));
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
            yield return ShowProcesses(window);
            var list = ProcessList(window);
            Assert.IsTrue(control.TryGetPCB(relay.CacheIndex, out var pcb));
            list.SetSelection(Listed(list).IndexOf(pcb));
            yield return Frames();
            Sample(window);

            var detail = window.rootVisualElement.Q("ProcessDetail");
            Assert.AreEqual(EProcessState.Running, pcb.State, "the process runs");
            var pause = detail.Q<Button>("Pause");
            Assert.IsNotNull(pause, "the detail can pause it");
            Assert.IsTrue(pause.enabledSelf, "Pause is offered for a running process");
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = pause;
                pause.SendEvent(click);
            }
            Sample(window);
            Assert.AreEqual(EProcessState.Waiting, pcb.State, "Pause pauses it");

            var resume = detail.Q<Button>("Resume");
            Assert.IsTrue(resume.enabledSelf, "and Resume is offered");
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = resume;
                resume.SendEvent(click);
            }
            Sample(window);
            Assert.AreEqual(EProcessState.Running, pcb.State, "Resume runs it again");

            var filter = window.rootVisualElement.Q<ToolbarMenu>("Filter");
            Assert.IsNotNull(filter, "the processes have a filter");
            var choices = filter.menu.MenuItems().OfType<DropdownMenuAction>().Select(action => action.name).ToList();
            Assert.That(choices, Has.No.Member("Terminated"), "with no Terminated, which no listed process can be");
        }
    }
}
