using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// An asset with number fields for FEAT-29's editor tests: one whose context has no target, one in percent, one
    /// that was a float before it became a number, which moves its saved float into the number as it loads, and some
    /// whose constants keep limits.
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

        /// <summary>Two numbers whose code gives them their owner, so either can read the other (NUM-9).</summary>
        [NumberContext(NumberReads.All)]
        public ScalableFloat Low = 1f;

        [NumberContext(NumberReads.All)]
        public ScalableFloat High = 2f;

        /// <summary>A plain float a relative number can read.</summary>
        public float PlainCap = 9f;

        /// <summary>Numbers whose constants keep limits in the inspector (NUM-15).</summary>
        [NumberMin(0f)]
        public ScalableFloat Floor = 1f;

        [NumberMax(10f)]
        public ScalableFloat Ceiling = 5f;

        [NumberRange(0f, 1f), NumberContext(NumberReads.All, Unit = "%")]
        public ScalableFloat Chance = 0.5f;

        [NumberRange(1f, 5f)]
        public ScalableInt Charges = 2;

        [NumberMin(1f)]
        public ScalableInt Count = 1;

        /// <summary>A number with a tooltip, which its label shows (NUM-15).</summary>
        [Tooltip("How far it reaches")]
        public ScalableFloat Told = 3f;

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
