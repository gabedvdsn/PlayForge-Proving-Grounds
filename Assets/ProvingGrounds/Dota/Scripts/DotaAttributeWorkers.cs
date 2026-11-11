using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.Dota
{
    /// <summary>
    /// Blocks every gain to its attribute: no healing, as Ice Blast's frostbite. An inline worker before the change, so a
    /// heal lands as nothing.
    /// </summary>
    [Serializable]
    public class HealBlockAttributeWorker : AbstractFocusedAttributeWorker
    {
        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            if (ctx.Change.Value.CurrentValue <= 0f) return;
            ctx.Change.Override(new AttributeValue(0f, ctx.Change.Value.BaseValue));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }

    /// <summary>
    /// Takes its attribute to 0 where a blow would leave it below a share of its base: Ice Blast's shatter. An inline
    /// worker before the change, so the blow that crosses the line is the one that shatters.
    /// </summary>
    [Serializable]
    public class ShatterAttributeWorker : AbstractFocusedAttributeWorker
    {
        [Tooltip("The share of its base below which a blow takes it to 0, as 0.13 for 13%")]
        public float BelowShareOfBase = 0.13f;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            float blow = ctx.Change.Value.CurrentValue;
            if (blow >= 0f || !ctx.AttributeCache.TryGetValue(TargetAttribute, out var cached)) return;
            float now = cached.ActiveValue.CurrentValue;
            float after = now + blow;
            if (after > 0f && after < cached.ActiveValue.BaseValue * BelowShareOfBase)
                ctx.Change.Override(new AttributeValue(-now, ctx.Change.Value.BaseValue));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }

    /// <summary>
    /// Keeps its attribute from falling below a least value: whoever has it can't die, as under Shallow Grave. An inline
    /// worker before the change, so a blow takes it to the least and no further.
    /// </summary>
    [Serializable]
    public class DeathPreventionAttributeWorker : AbstractFocusedAttributeWorker
    {
        [Tooltip("The least it can fall to")]
        public float Least = 1f;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            float blow = ctx.Change.Value.CurrentValue;
            if (blow >= 0f || !ctx.AttributeCache.TryGetValue(TargetAttribute, out var cached)) return;
            float now = cached.ActiveValue.CurrentValue;
            if (now + blow >= Least) return;
            ctx.Change.Override(new AttributeValue(Mathf.Min(0f, Least - now), ctx.Change.Value.BaseValue));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }

    /// <summary>
    /// Returns a share of what an entity takes to whoever dealt it, as Blade Mail. The damage it returns is unworkable, so
    /// no worker reads it, and two mails don't return it to each other forever.
    /// </summary>
    [Serializable]
    public class DamageReturnAttributeWorker : AbstractFocusedAttributeWorker
    {
        [Tooltip("The share of what it takes that goes back to the one who dealt it")]
        public float Share = 0.85f;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            float blow = ctx.Change.Value.CurrentValue;
            if (blow >= 0f || !ctx.Change.Value.ImpactIsWorkable) return;
            // Damage returned is the system's own, typed so no worker reads it: it isn't returned again
            var types = ctx.Change.Value.Carrier?.GetImpactTypes();
            if (types != null && types.Contains(Tags.DisallowImpact)) return;
            var dealer = ctx.Change.Value.Carrier?.GetOrigin()?.GetSource();
            if (dealer == null || ReferenceEquals(dealer, ctx.System)) return;
            float back = -blow * Share;
            if (back <= 0f) return;
            ctx.ActionQueue.Enqueue(new ModifyAttributeAction(dealer, TargetAttribute,
                SourcedModifiedAttributeValue.GenerateUnworkable(ctx.System, TargetAttribute, -back, 0f)));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }
}
