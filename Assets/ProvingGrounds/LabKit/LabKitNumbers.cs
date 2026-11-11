using System;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.LabKit
{
    /// <summary>How a count turns into a number, as games stack their items and their armor.</summary>
    public enum EStackingCurve
    {
        /// <summary>a·x: Soldier's Syringe's 15% a stack.</summary>
        Linear,

        /// <summary>1 − 1/(1 + a·x): Tougher Times' block chance, which nears 1 and never reaches it.</summary>
        Hyperbolic,

        /// <summary>1/(1 + a·x): what's left after a hyperbolic share, as Dota's armor leaves of a physical hit.</summary>
        Reciprocal,

        /// <summary>a/x: a time shortened by a rate, as an attack's wind-up by attack speed. 0 where x is 0.</summary>
        Inverse,

        /// <summary>a^x: Shaped Glass's halving a stack.</summary>
        Exponential,
    }

    /// <summary>
    /// A count, as an item's stacks, an effect's stacks or an attribute, through a curve, plus a number after it: 0.15 a
    /// stack hyperbolically for Tougher Times, 1 + 0.15 a stack for Soldier's Syringe's multiplier. Wishlist: stacking
    /// curves.
    /// </summary>
    [Serializable]
    [NumberKind("Stacking Curve", Category = "Lab Kit")]
    public class StackingCurveKind : NumberKind
    {
        [Tooltip("What it counts: an item's stacks, an effect's stacks, an attribute")]
        public ScalableFloat Count = 0f;

        [Tooltip("How the count turns into the number")]
        public EStackingCurve Curve = EStackingCurve.Linear;

        [Tooltip("The curve's a")]
        public float Coefficient = 1f;

        [Tooltip("Added after the curve: 1 makes a share a multiplier")]
        public float Plus;

        protected override float Evaluate(in NumberContext context)
        {
            float x = Count?.Evaluate(in context, this) ?? 0f;
            float a = Coefficient;
            float y = Curve switch
            {
                EStackingCurve.Linear => a * x,
                EStackingCurve.Hyperbolic => 1f - 1f / (1f + a * x),
                EStackingCurve.Reciprocal => 1f / (1f + a * x),
                EStackingCurve.Inverse => Mathf.Approximately(x, 0f) ? 0f : a / x,
                EStackingCurve.Exponential => Mathf.Pow(a, x),
                _ => x,
            };
            return y + Plus;
        }

        public override void Initialize(IAttributeImpactCarrier deriv) => Count?.Initialize(deriv);

        public override string Describe() => $"{Curve} {Coefficient} of {NumberText.Of(Count)}" + (Plus != 0f ? $", plus {Plus}" : "");

        public override void Explain(in NumberContext context, NumberTrace trace) => trace.Add(Count, context, this, "Count");
    }

    /// <summary>
    /// Holds on a roll: a chance, read as a number, so it can be an attribute, as a critical strike chance, or an item's
    /// stacks through a curve. Each reading rolls again. Wishlist: chance, critical strikes.
    /// </summary>
    [Serializable]
    [GameplayCondition("Chance", Category = "Lab Kit")]
    public class ChanceCondition : GameplayCondition
    {
        [Tooltip("The chance it holds, from 0 to 1")]
        public ScalableFloat Chance = 0.1f;

        protected override bool Evaluate(in NumberContext context)
        {
            float chance = Chance?.Evaluate(in context, this) ?? 0f;
            if (chance <= 0f) return false;
            if (chance >= 1f) return true;
            return ForgeRandom.Value < chance;
        }

        public override void Initialize(IAttributeImpactCarrier carrier) => Chance?.Initialize(carrier);

        public override void Explain(in NumberContext context, NumberTrace trace) => trace.Add(Chance, context, this, "Chance");

        public override string Describe() => $"a {NumberText.Of(Chance)} chance";
    }
}
