using System;
using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.LabKit
{
    // The workers the Dota, Risk of Rain and Hades labs needed and PlayForge doesn't have. Each stands in for an item of
    // Docs/TOOLING_WISHLIST.md, which names it; the labs' assets use them as an author would a built-in one. Each goes as
    // its item lands (AC-6): the on-hit worker went to TW-6's On Hit, the hit-taken event to TW-5's Impact Taken.

    /// <summary>
    /// Blocks a hit outright on a chance, as Tougher Times does. The chance is a number read at the hit with the holder as
    /// its target, so it can read the holder's stacks of an item through a curve. A blocked hit lands as nothing, so no
    /// on-hit effect comes of it. An inline worker before the change. Wishlist: chance, block and evasion.
    /// </summary>
    [Serializable]
    public class BlockChanceAttributeWorker : AbstractFocusedAttributeWorker
    {
        [Tooltip("The chance to block, from 0 to 1, read at the hit with the holder as its target")]
        public ScalableFloat Chance = 0.15f;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            if (ctx.Change.Value.CurrentValue >= 0f || !ctx.Change.Value.ImpactIsWorkable) return;
            float chance = Chance?.Evaluate(NumberContext.Of(in ctx)) ?? 0f;
            if (chance <= 0f || ForgeRandom.Value >= chance) return;
            ctx.Change.Override(new AttributeValue(0f, ctx.Change.Value.BaseValue));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }

    /// <summary>
    /// Takes a hit on a second attribute first: a barrier or a shield, as Topaz Brooch's barrier. What it soaks comes off
    /// the barrier, unworkable, so no worker reads it again; the rest goes through. An inline worker before the change.
    /// Wishlist: absorption layers.
    /// </summary>
    [Serializable]
    public class BarrierAttributeWorker : AbstractFocusedAttributeWorker
    {
        [Tooltip("The attribute that takes the hit first")]
        public ForgeAttribute Barrier;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            float blow = ctx.Change.Value.CurrentValue;
            if (blow >= 0f || Barrier == null || !ctx.AttributeCache.TryGetValue(Barrier, out var cached)) return;
            float soaked = Mathf.Min(cached.ActiveValue.CurrentValue, -blow);
            if (soaked <= 0f) return;
            ctx.Change.Override(new AttributeValue(blow + soaked, ctx.Change.Value.BaseValue));
            ctx.ActionQueue.Enqueue(new ModifyAttributeAction(ctx.System, Barrier,
                SourcedModifiedAttributeValue.GenerateUnworkable(ctx.System, Barrier, -soaked, 0f)));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }

    /// <summary>
    /// Acts as its attribute falls past a share of its base: a boss's second phase, as Megaera's at half her health. On
    /// the blow that crosses the line, it applies effects to its holder and sends it an event. It reads the blow before it
    /// lands, so an inline worker after it may still change it. Wishlist: thresholds that act.
    /// </summary>
    [Serializable]
    public class ThresholdCrossingAttributeWorker : AbstractFocusedAttributeWorker
    {
        [Tooltip("The line, as a share of its base: 0.5 for half")]
        public float ShareOfBase = 0.5f;

        [Tooltip("The effects its holder takes as its attribute falls past the line")]
        public List<GameplayEffect> ApplyToSelf = new();

        [Tooltip("The event its holder is sent as its attribute falls past the line, if any")]
        public Tag SendEvent;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            float blow = ctx.Change.Value.CurrentValue;
            if (blow >= 0f || !ctx.AttributeCache.TryGetValue(TargetAttribute, out var cached)) return;
            float now = cached.ActiveValue.CurrentValue;
            float line = cached.ActiveValue.BaseValue * ShareOfBase;
            if (now < line || now + blow >= line) return;

            var self = ctx.System;
            var origin = IEffectOrigin.GenerateSourceDerivation(self);
            if (ApplyToSelf != null)
                foreach (var effect in ApplyToSelf)
                    if (effect != null) ctx.ActionQueue.Enqueue(new ApplyEffectAction(self as ITarget, effect, origin));
            if (SendEvent.IsValid)
            {
                var tag = SendEvent;
                ctx.ActionQueue.Enqueue(new LambdaAction(_ => self.SendGameplayEvent(tag), $"Send {tag}"));
            }
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }
}
