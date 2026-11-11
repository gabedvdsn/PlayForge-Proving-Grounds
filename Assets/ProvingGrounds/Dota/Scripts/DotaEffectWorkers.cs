using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.Dota
{
    /// <summary>When an effect's effects for its caster land.</summary>
    public enum EApplyToSourceMoment
    {
        /// <summary>As the effect lands on its target.</summary>
        OnApplication,

        /// <summary>As the effect deals its impact, on the caster's side.</summary>
        OnImpact,
    }

    /// <summary>
    /// Lands effects on the effect's caster as the effect lands on its target: the counter on a target that gives its
    /// attacker charges, the stat a thief steals. A condition, where it has one, must hold where the effect is read, as
    /// "the caster has charges". PlayForge's contained and overflow effects land on the target alone.
    /// </summary>
    [Serializable]
    public class ApplyToSourceEffectWorker : AbstractEffectWorker
    {
        [Tooltip("When they land: as the effect lands on its target, or as it deals its impact, which is when the " +
                 "caster's own workers read what it dealt")]
        public EApplyToSourceMoment Moment = EApplyToSourceMoment.OnApplication;

        [Tooltip("The condition under which they land, read where the effect is; with none, they always do")]
        [SerializeReference]
        public GameplayCondition When;

        [Tooltip("The effects that land on the caster")]
        public List<GameplayEffect> Effects = new();

        public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx) =>
            Moment == EApplyToSourceMoment.OnApplication ? Land(ctx) : Array.Empty<IRootAction>();

        public override IEnumerable<IRootAction> OnEffectImpact(EffectWorkerContext ctx) =>
            Moment == EApplyToSourceMoment.OnImpact && ctx.ImpactData.HasValue ? Land(ctx) : Array.Empty<IRootAction>();

        private IEnumerable<IRootAction> Land(EffectWorkerContext ctx)
        {
            var origin = ctx.Carrier?.GetOrigin();
            var source = origin?.GetSource();
            if (source == null || Effects == null || Effects.Count == 0) return Array.Empty<IRootAction>();
            if (When != null && !When.Holds(NumberContext.Of(ctx.Carrier))) return Array.Empty<IRootAction>();

            var actions = new List<IRootAction>(Effects.Count);
            foreach (var effect in Effects)
                if (effect != null) actions.Add(new ApplyEffectAction(source, effect, origin));
            return actions;
        }
    }

    /// <summary>
    /// Heals the caster by a share of what its effect takes from an attribute, as lifesteal, while a condition holds. It
    /// runs on the caster's side, as the effect's impact is dealt, so it reads the damage after the target's reductions.
    /// </summary>
    [Serializable]
    public class LifestealEffectWorker : AbstractEffectWorker
    {
        [Tooltip("The attribute the effect takes from, and the caster's that heals, as Health")]
        public Attribute Attribute;

        [Tooltip("The share of what the effect takes that heals the caster")]
        public ScalableFloat Share = 0.3f;

        [Tooltip("The condition under which it heals, read where the effect is; with none, it always does")]
        [SerializeReference]
        public GameplayCondition When;

        public override IEnumerable<IRootAction> OnEffectImpact(EffectWorkerContext ctx)
        {
            if (Attribute == null || ctx.ImpactData is not { } impact || impact.Attribute == null || !impact.Attribute.Equals(Attribute))
                return Array.Empty<IRootAction>();
            float taken = -impact.RealImpact.CurrentValue;
            if (taken <= 0f) return Array.Empty<IRootAction>();

            var context = NumberContext.Of(ctx.Carrier);
            if (When != null && !When.Holds(context)) return Array.Empty<IRootAction>();
            float heal = taken * (Share?.Evaluate(context, this) ?? 0f);
            if (heal <= 0f) return Array.Empty<IRootAction>();
            return new IRootAction[]
            {
                new ModifyAttributeAction(ctx.System, Attribute, SourcedModifiedAttributeValue.GenerateUnworkable(ctx.System, Attribute, heal, 0f)),
            };
        }
    }

    /// <summary>
    /// Gives the effect's target attribute workers while the effect lasts, so an effect carries its own rules: a
    /// frostbite that blocks healing, a grave that keeps its target alive, a mail that returns damage. Each worker is
    /// given to an entity once, however many of the effect it holds, and taken back as the last of them goes.
    /// </summary>
    [Serializable]
    public class GrantAttributeWorkersEffectWorker : AbstractEffectWorker
    {
        [Tooltip("The attribute workers the target has while the effect lasts")]
        [SerializeReference]
        public List<AbstractAttributeWorker> Workers = new();

        // How many of the effect each entity holds, so the workers go as the last does
        [NonSerialized] private Dictionary<IGameplayAbilitySystem, int> holders;

        public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx)
        {
            var attributes = ctx.System?.GetAttributeSystem();
            if (attributes == null || Workers == null) return Array.Empty<IRootAction>();
            holders ??= new Dictionary<IGameplayAbilitySystem, int>();
            holders.TryGetValue(ctx.System, out int held);
            holders[ctx.System] = held + 1;
            if (held == 0)
                foreach (var worker in Workers)
                    if (worker != null) attributes.ProvideWorker(worker);
            return Array.Empty<IRootAction>();
        }

        public override IEnumerable<IRootAction> OnEffectRemoval(EffectWorkerContext ctx)
        {
            var attributes = ctx.System?.GetAttributeSystem();
            if (attributes == null || Workers == null || holders == null || !holders.TryGetValue(ctx.System, out int held))
                return Array.Empty<IRootAction>();
            if (held > 1)
            {
                holders[ctx.System] = held - 1;
                return Array.Empty<IRootAction>();
            }
            holders.Remove(ctx.System);
            foreach (var worker in Workers)
                if (worker != null) attributes.RemoveWorker(worker);
            return Array.Empty<IRootAction>();
        }
    }
}
