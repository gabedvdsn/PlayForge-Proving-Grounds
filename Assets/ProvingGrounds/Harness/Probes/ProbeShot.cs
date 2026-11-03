using System.Threading;
using Cysharp.Threading.Tasks;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// A homing shot: it flies at whatever it targets for its given number of frames, then applies its effects
    /// there. It registers its intent with its target, which is how a disjoint finds it.
    /// </summary>
    public sealed class ProbeShot : AbstractTargetedMonoProcess
    {
        public int RunFrames = 1;

        /// <summary>What the shot is aimed at now.</summary>
        public ITarget Target => target;

        protected override async UniTask RunTargetedProcess(CancellationToken token)
        {
            for (int i = 0; i < RunFrames; i++) await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
    }
}
