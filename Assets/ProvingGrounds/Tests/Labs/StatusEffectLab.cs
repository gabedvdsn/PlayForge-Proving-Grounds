using System;
using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Status-effect lab, the first of Phase 5's labs: a training dummy and a caster, with poison, burn and bleed under
    /// each stacking policy, a cleanse, an antidote that makes the dummy immune to poison, and a barrier whose shield
    /// takes damage before health. It's a small game, built in code as a game would author it; its bot, the tests in
    /// <see cref="StatusEffectLabTests"/>, plays it. Every effect advances a second a frame, so a round is exact.
    /// </summary>
    public sealed class StatusEffectLab
    {
        public readonly ForgeAttribute Health;
        public readonly ForgeAttribute Shield;
        public readonly IGameplayAbilitySystem Dummy;
        public readonly IGameplayAbilitySystem Caster;

        /// <summary>The kind every debuff is, which the cleanse names.</summary>
        public readonly Tag Debuff;
        public readonly Tag PoisonKind;

        /// <summary>2 damage a second for 4 seconds; its stacks share one duration, and each tick deals once a stack.</summary>
        public readonly GameplayEffect Poison;

        /// <summary>3 damage a second for 2 seconds; each stack keeps its own duration.</summary>
        public readonly GameplayEffect Burn;

        /// <summary>1 damage a second for 2 seconds a stack; its stacks run one after another, and each tick deals once a stack.</summary>
        public readonly GameplayEffect Bleed;

        /// <summary>Takes off every debuff (FEAT-8).</summary>
        public readonly GameplayEffect Cleanse;

        /// <summary>For 5 seconds, no poison lands (FEAT-8).</summary>
        public readonly GameplayEffect Antidote;

        /// <summary>30 shield, which takes damage before health.</summary>
        public readonly GameplayEffect Barrier;

        /// <summary>A blow of 20 damage.</summary>
        public readonly GameplayEffect Strike;

        /// <summary>40 health back, up to its base.</summary>
        public readonly GameplayEffect Potion;

        public StatusEffectLab(World world, ForgeAssets make, Hub hub)
        {
            Health = make.Attribute("Health");
            Shield = make.Attribute("Shield");
            Dummy = world.Entity(hub, make.Set(
                make.Element(Health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
                make.Element(Shield, 0f, 50f, EAttributeOverflowPolicy.ZeroToBase)), "Dummy");
            Caster = world.Entity(hub, name: "Caster");
            Dummy.GetAttributeSystem().ProvideWorker(new ShieldWorker
            {
                TimingConfig = AbstractAttributeWorker.EChangeEventTiming.PreChange,
                TargetAttribute = Health,
                AllowSelfModification = true,
                TargetModification = EAttributeTargetExpanded.Current,
                SignPolicy = ESignPolicyExtended.Negative,
                Shield = Shield,
            });

            Debuff = make.NewTag("Lab.Status.Debuff");
            PoisonKind = Tag.Generate(Debuff.Name + ".Poison");
            Poison = Status(make, "Poison", PoisonKind, Health, -2f, seconds: 4, EEffectStackPolicy.StacksShareOneDuration);
            Burn = Status(make, "Burn", Tag.Generate(Debuff.Name + ".Burn"), Health, -3f, seconds: 2, EEffectStackPolicy.StacksHaveIndependentDurations);
            Bleed = Status(make, "Bleed", Tag.Generate(Debuff.Name + ".Bleed"), Health, -1f, seconds: 2, EEffectStackPolicy.DurationTakenFromOneStack);

            Cleanse = make.Effect("Cleanse", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = null });
            Cleanse.Tags.RemovesEffectsTagged = new List<Tag> { Debuff };

            Antidote = make.Effect("Antidote", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 5f, Target = null, FixedDeltaTime = 1f });
            Antidote.Tags.BlocksEffectsTagged = new List<Tag> { PoisonKind };

            Barrier = make.Effect("Barrier", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Shield, Magnitude = 30f });
            Strike = make.Effect("Strike", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -20f });
            Potion = make.Effect("Potion", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = 40f });
        }

        // A debuff that deals its damage to health once a second for its duration, and stacks as the policy says
        private static GameplayEffect Status(ForgeAssets make, string name, Tag kind, ForgeAttribute health, float damage, int seconds,
            EEffectStackPolicy stacking)
        {
            var status = make.Effect(name, new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational,
                Target = health,
                Magnitude = damage,
                Duration = seconds,
                PeriodicTicks = true,
                Ticks = seconds,
                FixedDeltaTime = 1f,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = stacking,
                StackAmount = 1,
            });
            status.Tags.ContextTags = new List<Tag> { kind };
            return status;
        }

        /// <summary>The caster applies an effect to the dummy, as an ability would. False when it doesn't land.</summary>
        public bool Cast(World world, GameplayEffect effect) => world.Apply(Caster, Dummy, effect);

        public float Value(ForgeAttribute attribute) => Dummy.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        public bool Has(GameplayEffect effect) => Dummy.TryGetEffectContainer(effect, out _);

        public int Stacks(GameplayEffect effect) =>
            Dummy.TryGetEffectContainer(effect, out var container) && container is AbstractStackingEffectContainer stacking ? stacking.Stacks : 0;
    }

    /// <summary>
    /// A shield that takes damage before health, as a game would write one: an inline worker on health's reductions lets
    /// through what the shield can't take, and queues the shield's own loss for the end of the frame. Two blows in one
    /// frame share what the shield has left, as the queued losses haven't landed yet.
    /// </summary>
    [Serializable]
    public sealed class ShieldWorker : AbstractFocusedAttributeWorker
    {
        public ForgeAttribute Shield;

        // What each entity's shield has taken this frame, which its queued losses haven't taken off yet
        [NonSerialized] private readonly Dictionary<IGameplayAbilitySystem, (int frame, float taken)> taken = new();

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            float blow = ctx.Change.Value.CurrentValue;
            if (blow >= 0f || !ctx.AttributeCache.TryGetValue(Shield, out var shield)) return;

            // PlayForge's frame, which a tick is when ticks are on
            int frame = ForgeTime.FrameCount;
            float already = taken.TryGetValue(ctx.System, out var record) && record.frame == frame ? record.taken : 0f;
            float absorbed = Mathf.Min(Mathf.Max(0f, shield.ActiveValue.CurrentValue - already), -blow);
            if (absorbed <= 0f) return;

            ctx.Change.Override(new AttributeValue(blow + absorbed, ctx.Change.Value.BaseValue));
            taken[ctx.System] = (frame, already + absorbed);
            ctx.ActionQueue.Enqueue(new ModifyAttributeAction(ctx.System, Shield,
                SourcedModifiedAttributeValue.GenerateUnworkable(ctx.System, Shield, -absorbed, 0f)));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }
}
