using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// Runs after every other Update, so it marks where ProcessControl's stepping has finished and the
    /// test runner's coroutine tick is about to begin.
    /// </summary>
    [DefaultExecutionOrder(int.MaxValue)]
    public sealed class AllocationMarker : MonoBehaviour
    {
        public AllocationProbe Probe;

        private void Update() => Probe?.MarkUpdatesFinished();
    }
}
