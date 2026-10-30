using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    public enum ProbeHook
    {
        Initialize,
        Run,
        Update,
        FixedUpdate,
        LateUpdate,
        Wait,
        Terminate
    }

    /// <summary>
    /// What a probe process has been through. Every hook counts here and passes through a fault site
    /// named <c>"{probe}.{hook}"</c>, so a fault can be armed on any of them.
    /// </summary>
    public sealed class ProbeRecorder
    {
        public const int Forever = -1;

        private static readonly ProbeHook[] Hooks = (ProbeHook[])Enum.GetValues(typeof(ProbeHook));

        private readonly int[] _counts = new int[Hooks.Length];
        private readonly string[] _sites = new string[Hooks.Length];
        private readonly FaultInjector _faults;

        public readonly string Name;

        /// <summary>How many frames a run lasts, or <see cref="Forever"/> until released or stopped.</summary>
        public int RunFrames;

        /// <summary>Set to end the current run at its next frame.</summary>
        public bool Released;

        /// <summary>Runs inside every hook, for a test that needs a process to do something at that moment.</summary>
        public Action<ProbeHook> OnHit;

        public int RunsCompleted { get; private set; }
        public int RunsCancelled { get; private set; }

        public ProbeRecorder(string name, FaultInjector faults, int runFrames = Forever)
        {
            Name = name;
            RunFrames = runFrames;
            _faults = faults;

            // Built once: a hook runs every frame, and a probe mustn't show up in an allocation test
            foreach (var hook in Hooks) _sites[(int)hook] = $"{name}.{hook}";
        }

        public int this[ProbeHook hook] => _counts[(int)hook];

        public string Site(ProbeHook hook) => _sites[(int)hook];

        public void Hit(ProbeHook hook)
        {
            _counts[(int)hook]++;
            OnHit?.Invoke(hook);
            _faults?.Trip(Site(hook));
        }

        public async UniTask Run(Func<bool> active, CancellationToken token)
        {
            Hit(ProbeHook.Run);

            try
            {
                for (int frames = 0; !Released && active() && (RunFrames == Forever || frames < RunFrames); frames++)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
                RunsCompleted++;
            }
            catch (OperationCanceledException)
            {
                RunsCancelled++;
                throw;
            }
        }
    }
}
