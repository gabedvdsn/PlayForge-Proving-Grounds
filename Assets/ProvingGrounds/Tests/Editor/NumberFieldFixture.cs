using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// An asset with number fields for FEAT-29's editor tests: one whose context has no target, one in percent, and one
    /// that was a float before it became a number, which moves its saved float into the number as it loads.
    /// </summary>
    public class NumberFieldFixture : ScriptableObject, ISerializationCallbackReceiver
    {
        [NumberContext(NumberReads.Level | NumberReads.Caster)]
        public ScalableFloat Range = 5f;

        [NumberContext(NumberReads.All, Unit = "%")]
        public ScalableFloat Share = 0.3f;

        /// <summary>A float until FEAT-29: an asset saved before keeps its value in the legacy field below.</summary>
        public ScalableFloat Threshold = 0f;

        [FormerlySerializedAs(nameof(Threshold)), SerializeField, HideInInspector]
        private float legacyThreshold = LegacyNumber.None;

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize() => LegacyNumber.Move(ref legacyThreshold, ref Threshold);
    }

    /// <summary>A kind of the tests' own that needs a target, filed under a category of its own.</summary>
    [Serializable]
    [NumberKind("Reach", Category = "Testing", Reads = NumberReads.Target)]
    public class ReachKind : NumberKind
    {
        public float Scale = 1f;

        protected override float Evaluate(in NumberContext context) => Scale;

        public override string Describe() => "the reach";
    }
}
