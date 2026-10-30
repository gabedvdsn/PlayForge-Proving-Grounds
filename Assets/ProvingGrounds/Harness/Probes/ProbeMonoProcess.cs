using System.Threading;
using Cysharp.Threading.Tasks;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// A MonoBehaviour process that records its hooks. Registering a template clones it, and a clone
    /// copies only serialized fields, so the recorder is made on initialization, from the current world.
    /// </summary>
    public sealed class ProbeMonoProcess : AbstractMonoProcess
    {
        public int RunFrames = ProbeRecorder.Forever;

        public ProbeRecorder Recorder { get; private set; }

        public override void WhenInitialize()
        {
            base.WhenInitialize();
            Recorder = new ProbeRecorder(name, World.Current.Faults, RunFrames);
            Recorder.Hit(ProbeHook.Initialize);
        }

        public override void WhenUpdate() => Recorder.Hit(ProbeHook.Update);

        public override void WhenFixedUpdate() => Recorder.Hit(ProbeHook.FixedUpdate);

        public override void WhenLateUpdate() => Recorder.Hit(ProbeHook.LateUpdate);

        public override void WhenWait()
        {
            base.WhenWait();
            Recorder.Hit(ProbeHook.Wait);
        }

        public override void WhenTerminate()
        {
            base.WhenTerminate();
            Recorder.Hit(ProbeHook.Terminate);
        }

        public override UniTask RunProcess(CancellationToken token)
        {
            processActive = true;
            return Recorder.Run(() => processActive, token);
        }
    }
}
