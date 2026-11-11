using System;
using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.LabKit
{
    // The workers the Dota, Risk of Rain and Hades labs needed and PlayForge doesn't have. Each stands in for an item of
    // Docs/TOOLING_WISHLIST.md, which names it; the labs' assets use them as an author would a built-in one.

    /// <summary>Who an on-hit effect lands on.</summary>
    public enum EOnHitWho
    {
        /// <summary>The one the hit landed on.</summary>
        Target,

        /// <summary>The one who dealt the hit: the worker's holder.</summary>
        Self,
    }

    /// <summary>
    /// Applies effects when its holder's hit lands: an item's or a boon's on-hit, as Desolator's Corruption, Tri-Tip
    /// Dagger's bleed or Heartbreak Strike's Weak; or on the hit that kills, as Topaz Brooch's barrier. It takes the hits
    /// any impact worker takes, by attribute, sign and impact type, so a bleed's ticks, typed apart from attacks, don't set
    /// it off again. An effect's own Chance to Apply rolls as it lands. Wishlist: on-hit and on-kill effects.
    /// <para>
    /// Its holder is the hit's dealer, its context's System: an impact worker runs for the dealer. Its System was the one
    /// hit until GAS-72, so the lab found its holder through the hit's carrier.
    /// </para>
    /// </summary>
    [Serializable]
    public class OnHitApplyEffectsImpactWorker : AbstractContextImpactWorker
    {
        [Tooltip("The effects it applies")]
        public List<GameplayEffect> Effects = new();

        [Tooltip("Who they land on: the one hit, or the holder")]
        public EOnHitWho To = EOnHitWho.Target;

        [Tooltip("Only on the hit that takes its attribute from above 0 to 0 or below: a kill")]
        public bool OnlyOnKill;

        [Tooltip("Applies only while this holds, read at the hit, the holder its caster and the one hit its target")]
        [SerializeReference] public GameplayCondition When;

        public override bool ValidateWorkFor(ImpactData impactData)
        {
            if (!base.ValidateWorkFor(impactData)) return false;
            return !OnlyOnKill || (impactData.OldValue.CurrentValue > 0f && impactData.LiveValue.CurrentValue <= 0f);
        }

        public override IEnumerable<IRootAction> CreateDeferredActions(ImpactWorkerContext ctx)
        {
            var actions = new List<IRootAction>();
            if (Effects == null || Effects.Count == 0) return actions;
            if (When != null && !When.Holds(NumberContext.Of(in ctx))) return actions;

            var holder = ctx.System;
            if (holder == null) return actions;
            var onto = To == EOnHitWho.Self ? holder as ITarget : ctx.ImpactData.Target;
            if (onto == null) return actions;
            var origin = IEffectOrigin.GenerateSourceDerivation(holder);
            foreach (var effect in Effects)
                if (effect != null) actions.Add(new ApplyEffectAction(onto, effect, origin));
            return actions;
        }
    }

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

    /// <summary>
    /// Sends its holder an event as a hit lands on it, on a chance: what sets off an ability "when attacked", as Axe's
    /// Counter Helix on a fifth of the hits. The chance is here, as an ability's activation rules run twice for each
    /// activation, at the request and as it starts, so a rule that rolls draws twice. The dealer can be the event's
    /// target, for a counterattack; an area ability, as Counter Helix, finds its own, as the dealer as its target would
    /// take the place of what its search finds. Wishlist: built-in gameplay events, and a chance to activate.
    /// </summary>
    [Serializable]
    public class HitTakenEventAttributeWorker : AbstractFocusedAttributeWorker
    {
        [Tooltip("The event its holder is sent")]
        public Tag Event;

        [Tooltip("The chance it's sent, from 0 to 1, read at the hit with the holder as its target")]
        public ScalableFloat Chance = 1f;

        [Tooltip("The dealer is the target of the ability the event sets off, as a counterattack's. Off, the ability finds " +
                 "its own: an event's data has no place for who set it off apart from the ability's targets")]
        public bool DealerIsTarget;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            if (!Event.IsValid || ctx.Change.Value.CurrentValue >= 0f) return;
            float chance = Chance?.Evaluate(NumberContext.Of(in ctx)) ?? 1f;
            if (chance <= 0f || (chance < 1f && ForgeRandom.Value >= chance)) return;
            var dealer = DealerIsTarget ? ctx.Change.Value.Carrier?.GetOrigin()?.GetSource() as ITarget : null;
            var self = ctx.System;
            var tag = Event;
            ctx.ActionQueue.Enqueue(new LambdaAction(_ =>
            {
                var data = ProcessDataPacket.Default();
                if (dealer != null) data.SetPrimary(Tags.TARGET_REAL, dealer);
                self.SendGameplayEvent(tag, data);
            }, $"Send {tag}"));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }
}
