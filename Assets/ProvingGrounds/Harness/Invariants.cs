using System.Collections.Generic;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// What must hold after every test, whatever it did. The base test class runs these in teardown, so a
    /// test gets them without asking, and a test about something else still catches a leak.
    /// </summary>
    public sealed class Invariants
    {
        private const float Tolerance = 0.001f;

        private readonly List<IGameplayAbilitySystem> _observed = new();
        private readonly Dictionary<(ISource, IAttribute), AttributeValue> _dealt = new();
        private readonly Dictionary<(ISource, IAttribute), AttributeValue> _received = new();

        /// <summary>Includes an entity in the attribute checks. <see cref="World"/> does this as it spawns one.</summary>
        public void Observe(IGameplayAbilitySystem gas)
        {
            _observed.Add(gas);
            gas.GetCallbacks().OnFrameComplete += summary => Tally(gas, summary);
        }

        private void Tally(IGameplayAbilitySystem gas, FrameSummarySnapshot summary)
        {
            foreach (var impact in summary.ImpactsDealt) Add(_dealt, gas, impact);

            foreach (var impact in summary.ImpactsReceived)
            {
                var source = impact.SourcedModifier.Carrier?.GetOrigin()?.GetSource();
                if (source != null) Add(_received, source, impact);
            }
        }

        private static void Add(Dictionary<(ISource, IAttribute), AttributeValue> tally, ISource source, ImpactData impact)
        {
            var key = (source, impact.Attribute);
            tally.TryGetValue(key, out var total);
            tally[key] = total + impact.RealImpact;
        }

        /// <summary>
        /// Every observed entity's attribution slices sum to ActiveValue, and what each dealt, read from its
        /// frame summaries, equals what its targets received from it. Totals are compared over the whole
        /// test, not per frame: a source and target that end their frames apart can record one impact in
        /// neighbouring frames.
        /// </summary>
        public List<string> CheckAttributes()
        {
            var violations = new List<string>();

            foreach (var gas in _observed)
            {
                var attributes = gas.GetAttributeSystem();
                if (attributes == null) continue;

                foreach (var attribute in attributes.GetDefinedAttributes())
                {
                    if (!attributes.TryGetValue(attribute, out var active)) continue;

                    AttributeValue sum = default;
                    foreach (var slice in attributes.GetBreakdown(attribute)) sum += slice.Value;

                    if (!Near(sum, active))
                    {
                        violations.Add($"{gas.GetName()}'s {attribute.GetName()} slices sum to {Show(sum)}, but its ActiveValue is {Show(active)}");
                    }
                }
            }

            foreach (var key in AllKeys())
            {
                if (!_observed.Contains(key.Item1 as IGameplayAbilitySystem)) continue;

                _dealt.TryGetValue(key, out var dealt);
                _received.TryGetValue(key, out var received);

                if (!Near(dealt, received))
                {
                    violations.Add($"{key.Item1.GetName()} dealt {Show(dealt)} to {key.Item2.GetName()}, but its targets received {Show(received)}");
                }
            }

            return violations;
        }

        /// <summary>Control blocks still registered that weren't there when the world began.</summary>
        public List<string> CheckBlocks(ProcessControl control, ISet<int> baseline)
        {
            var violations = new List<string>();

            foreach (var (index, block) in control.FetchActiveProcesses())
            {
                if (baseline.Contains(index)) continue;
                violations.Add($"control block #{index} leaked: {block.Wrapper.ProcessName}, {block.State}, queued {block.QueuedState}");
            }

            return violations;
        }

        private HashSet<(ISource, IAttribute)> AllKeys()
        {
            var keys = new HashSet<(ISource, IAttribute)>(_dealt.Keys);
            keys.UnionWith(_received.Keys);
            return keys;
        }

        private static bool Near(AttributeValue a, AttributeValue b)
        {
            return Mathf.Abs(a.CurrentValue - b.CurrentValue) <= Tolerance && Mathf.Abs(a.BaseValue - b.BaseValue) <= Tolerance;
        }

        private static string Show(AttributeValue value) => $"{value.CurrentValue}/{value.BaseValue}";
    }
}
