using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>Counts the phases Unity actually ran, so the stepper can show a frame was a whole one.</summary>
    [DefaultExecutionOrder(int.MinValue)]
    public sealed class FramePulse : MonoBehaviour
    {
        public int Updates { get; private set; }
        public int FixedUpdates { get; private set; }
        public int LateUpdates { get; private set; }

        private void Update() => Updates++;
        private void FixedUpdate() => FixedUpdates++;
        private void LateUpdate() => LateUpdates++;
    }
}
