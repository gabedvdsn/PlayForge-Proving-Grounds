using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// One live PlayForge for one test: a real ProcessControl and GameRoot brought up by the real
    /// Bootstrapper, stepped by Unity's player loop. It spawns what a test needs and takes it all away
    /// afterwards, including the framework's static state, so tests can run in any order.
    /// </summary>
    public sealed class World
    {
        private const string EmptyScenePath = "Assets/ProvingGrounds/Fixtures/Scenes/Empty.unity";

        // Enough for async processes to unwind and for a frame's deferred work to land
        private const int SettleFrames = 3;

        public static World Current { get; private set; }

        public ProcessControl Control { get; private set; }
        public GameRoot Root { get; private set; }

        public FrameStepper Frames { get; }
        public FaultInjector Faults { get; } = new();
        public TaskLedger Ledger { get; } = new();
        public ForgeAssets Make { get; } = new();
        public Invariants Invariants { get; } = new();

        private readonly List<ProcessRelay> _owned = new();
        private readonly List<GameObject> _objects = new();
        private readonly List<Scene> _scenes = new();
        private HashSet<int> _baseline = new();

        internal World(float deltaTime = FrameStepper.DefaultDeltaTime)
        {
            Frames = new FrameStepper(deltaTime);
            Current = this;
        }

        internal void Bootstrap()
        {
            // Destroyed singletons aren't null to the Bootstrapper's `is null` checks (PC-26)
            ProcessControl.Instance = null;
            GameRoot.Instance = null;

            var rootTemplate = Template<GameRoot>("Game Root");
            rootTemplate.EntityData = Make.Identity("Game Root", Make.UnusedSet());
            rootTemplate.ProcessLifecycle = EProcessLifecycle.Synchronous;
            rootTemplate.ProcessTiming = EProcessStepTiming.UpdateAndLate;

            // Awake bootstraps, so the component is filled in before the object wakes
            var host = new GameObject("Bootstrapper");
            host.SetActive(false);
            var bootstrapper = host.AddComponent<Bootstrapper>();
            bootstrapper.GameRootPrefab = rootTemplate;
            bootstrapper.RegisterSets = new List<AttributeSet>();
            host.SetActive(true);

            Control = ProcessControl.Instance;
            Root = GameRoot.Instance;
            _baseline = new HashSet<int>(Control.FetchActiveProcesses().Keys);
        }

        #region Processes

        /// <summary>Registers a process the world will end if the test doesn't.</summary>
        public ProcessRelay Register(AbstractRuntimeProcess process, ProcessDataPacket data = null)
        {
            if (!ProcessControl.Register(process, data ?? ProcessDataPacket.Default(), out var relay))
                throw new AssertionException($"ProcessControl refused {process.ProcessName}");

            _owned.Add(relay);
            return relay;
        }

        /// <summary>Registers a process the world will end if the test doesn't.</summary>
        public ProcessRelay Register(AbstractMonoProcess process, ProcessDataPacket data = null)
        {
            if (!ProcessControl.Register(process, data ?? ProcessDataPacket.Default(), out var relay))
                throw new AssertionException($"ProcessControl refused {process.name}");

            _owned.Add(relay);
            return relay;
        }

        /// <summary>Makes the world end a process that was registered some other way.</summary>
        public void Own(ProcessRelay relay) => _owned.Add(relay);

        /// <summary>A probe process, not registered yet.</summary>
        public ProbeProcess NewProbe(string name, EProcessLifecycle lifecycle = EProcessLifecycle.SelfTerminating,
            EProcessStepTiming timing = EProcessStepTiming.Update, int runFrames = ProbeRecorder.Forever, int priority = 0)
        {
            return new ProbeProcess(name, lifecycle, timing, Faults, runFrames, priority);
        }

        /// <summary>A registered probe process.</summary>
        public ProbeProcess Probe(string name, EProcessLifecycle lifecycle = EProcessLifecycle.SelfTerminating,
            EProcessStepTiming timing = EProcessStepTiming.Update, int runFrames = ProbeRecorder.Forever, int priority = 0)
        {
            var probe = NewProbe(name, lifecycle, timing, runFrames, priority);
            Register(probe);
            return probe;
        }

        /// <summary>A registered probe process on a GameObject of its own in the scene.</summary>
        public ProbeMonoProcess MonoProbe(string name, EProcessLifecycle lifecycle = EProcessLifecycle.SelfTerminating,
            EProcessStepTiming timing = EProcessStepTiming.Update, int runFrames = ProbeRecorder.Forever)
        {
            var host = new GameObject(name);
            _objects.Add(host);

            var probe = host.AddComponent<ProbeMonoProcess>();
            Configure(probe, lifecycle, timing, runFrames);
            Register(probe);
            return probe;
        }

        /// <summary>A probe template, which registering clones as it would a prefab.</summary>
        public ProbeMonoProcess MonoProbeTemplate(string name, EProcessLifecycle lifecycle = EProcessLifecycle.SelfTerminating,
            EProcessStepTiming timing = EProcessStepTiming.Update, int runFrames = ProbeRecorder.Forever)
        {
            var probe = Template<ProbeMonoProcess>(name);
            Configure(probe, lifecycle, timing, runFrames);
            return probe;
        }

        private static void Configure(ProbeMonoProcess probe, EProcessLifecycle lifecycle, EProcessStepTiming timing, int runFrames)
        {
            probe.ProcessLifecycle = lifecycle;
            probe.ProcessTiming = timing;
            probe.RunFrames = runFrames;
        }

        /// <summary>
        /// A stand-in for a prefab: an object outside any scene, as a prefab is, so ProcessControl clones
        /// it on registration. Real prefab assets aren't needed until a test is about the asset itself.
        /// </summary>
        public T Template<T>(string name) where T : AbstractMonoProcess
        {
            var host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            _objects.Add(host);
            return host.AddComponent<T>();
        }

        /// <summary>Registers a template and returns the clone ProcessControl made of it.</summary>
        public T Spawn<T>(T template) where T : AbstractMonoProcess
        {
            var relay = Register(template);
            if (!relay.TryGetProcess<T>(out var clone)) throw new AssertionException($"Registering {template.name} made no {typeof(T).Name}");

            _objects.Add(clone.gameObject);
            return clone;
        }

        #endregion

        #region Entities

        /// <summary>
        /// A registered runtime GAS. <paramref name="observed"/> entities are checked by the invariants,
        /// which means listening for their frame summaries, so an allocation test turns it off.
        /// </summary>
        public RuntimeGameplayAbilitySystem RuntimeEntity(AttributeSet set = null, string name = "Entity", bool observed = true)
        {
            var gas = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity(name, set ?? Make.UnusedSet()));
            Register(gas);
            if (observed) Invariants.Observe(gas);
            return gas;
        }

        /// <summary>
        /// A registered MonoBehaviour GAS on a GameObject of its own, stepped the way Arcade Fighter
        /// steps its fighters. See <see cref="RuntimeEntity"/> for <paramref name="observed"/>.
        /// </summary>
        public GameplayAbilitySystem MonoEntity(AttributeSet set = null, string name = "Entity", bool observed = true)
        {
            var host = new GameObject(name);
            _objects.Add(host);

            var gas = GameplayAbilitySystem.AddToGameObject(host, Make.Identity(name, set ?? Make.UnusedSet()));
            gas.ProcessLifecycle = EProcessLifecycle.Synchronous;
            gas.ProcessTiming = EProcessStepTiming.UpdateAndLate;
            Register(gas);
            if (observed) Invariants.Observe(gas);
            return gas;
        }

        /// <summary>Applies <paramref name="effect"/> from <paramref name="source"/> to <paramref name="target"/>.</summary>
        public bool Apply(IGameplayAbilitySystem source, IGameplayAbilitySystem target, GameplayEffect effect)
        {
            var origin = IEffectOrigin.GenerateSourceDerivation(source);
            return target.ApplyGameplayEffect(target.GenerateEffectSpec(origin, effect));
        }

        #endregion

        #region Scenes

        /// <summary>An empty scene loaded alongside the current ones.</summary>
        public Scene AddScene(string name)
        {
            var scene = SceneManager.CreateScene(Make.Unique(name));
            _scenes.Add(scene);
            return scene;
        }

        public async UniTask UnloadScene(Scene scene)
        {
            _scenes.Remove(scene);
            await SceneManager.UnloadSceneAsync(scene).ToUniTask();
        }

        /// <summary>
        /// Loads an empty scene in place of the current ones, as a game changing levels does: everything
        /// not marked DontDestroyOnLoad goes.
        /// </summary>
        public async UniTask ChangeScene()
        {
#if UNITY_EDITOR
            await EditorSceneManager.LoadSceneAsyncInPlayMode(EmptyScenePath, new LoadSceneParameters(LoadSceneMode.Single)).ToUniTask();
#else
            await SceneManager.LoadSceneAsync(EmptyScenePath, LoadSceneMode.Single).ToUniTask();
#endif
        }

        #endregion

        #region Teardown

        /// <summary>
        /// Lets the frame's work finish, ends everything the test left running, and returns every
        /// invariant that broke. Faults stop here: they are for the test body, not for the audit.
        /// </summary>
        internal async UniTask<List<string>> Audit()
        {
            Faults.DisarmAll();
            await Frames.Step(SettleFrames);

            var violations = Invariants.CheckAttributes();

            if (Control)
            {
                foreach (var relay in _owned)
                {
                    if (Control.IsRegistered(relay.CacheIndex)) Control.TerminateImmediate(relay.CacheIndex, false);
                }

                await Frames.Step(SettleFrames);
                violations.AddRange(Invariants.CheckBlocks(Control, _baseline));
            }

            violations.AddRange(Ledger.Violations());
            return violations;
        }

        /// <summary>
        /// Tears everything down whatever state the test left it in. Forcing every process to end at once
        /// runs each termination path there is, bugs included, and what those report here says nothing
        /// about the test, so it is ignored. The bugs have tests of their own.
        /// </summary>
        internal async UniTask Destroy()
        {
            IgnoreFailingLogs(true);
            Faults.DisarmAll();

            try
            {
                if (Control) Control.TerminateAllImmediately();

                // Async processes unwind over later frames and need ProcessControl while they do
                await Frames.Step(SettleFrames);

                foreach (var scene in _scenes)
                {
                    if (scene.isLoaded) await SceneManager.UnloadSceneAsync(scene).ToUniTask();
                }

                foreach (var host in _objects)
                {
                    if (host) Object.Destroy(host);
                }

                if (Root) Object.Destroy(Root.gameObject);
                if (Control) Object.Destroy(Control.gameObject);
                Make.DestroyAll();

                await Frames.Step();
            }
            finally
            {
                ProcessControl.Instance = null;
                GameRoot.Instance = null;
                TagRegistry.Reset();
                ClearAttributeRegistry();

                Frames.Release();
                Current = null;
                IgnoreFailingLogs(false);
            }
        }

        private static void IgnoreFailingLogs(bool ignore)
        {
            try
            {
                LogAssert.ignoreFailingMessages = ignore;
            }
            catch (InvalidOperationException)
            {
                // A test that failed has lost its log scope by teardown, and with it anything to ignore
            }
        }

        private static void ClearAttributeRegistry()
        {
            var library = typeof(AttributeRegistry).GetField("Library", BindingFlags.NonPublic | BindingFlags.Static);
            ((IDictionary)library?.GetValue(null))?.Clear();
        }

        #endregion
    }
}
