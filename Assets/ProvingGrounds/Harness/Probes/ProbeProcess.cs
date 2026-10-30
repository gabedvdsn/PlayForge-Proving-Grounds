using System.Threading;
using Cysharp.Threading.Tasks;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// A runtime process that records its hooks. It extends the base directly rather than the Lazy
    /// process, so it measures ProcessControl without carrying PC-5's or PC-18's bugs along.
    /// </summary>
    public sealed class ProbeProcess : AbstractRuntimeProcess
    {
        public readonly ProbeRecorder Recorder;

        public ProbeProcess(string name, EProcessLifecycle lifecycle, EProcessStepTiming timing, FaultInjector faults,
            int runFrames = ProbeRecorder.Forever, int priority = 0)
            : base(name, EProcessStepPriorityMethod.Manual, priority, timing, lifecycle)
        {
            Recorder = new ProbeRecorder(name, faults, runFrames);
        }

        public override void WhenInitialize()
        {
            base.WhenInitialize();
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
