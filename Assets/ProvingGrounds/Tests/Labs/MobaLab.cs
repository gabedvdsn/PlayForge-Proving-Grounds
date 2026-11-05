using System;
using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The MOBA hero kit, Phase 5's second lab: four heroes of four abilities, with mana, cooldowns and cooldown
    /// reduction, armor and magic resist, lifesteal, a silence, a stun, a cleanse, and a channel that a stun ends, a blink
    /// cancels and that blocks other spells while it runs (FEAT-5). It's a small game, built in code as a game would
    /// author it; its bot, the tests in <see cref="MobaLabTests"/>, plays it on a clock of half a second a frame.
    /// </summary>
    public sealed class MobaLab
    {
        public const float SecondsPerFrame = 0.5f;

        public readonly ForgeAttribute Health, Mana, Armor, MagicResist, CooldownReduction, Lifesteal;

        /// <summary>What an ability is: an attack, a spell, or a move.</summary>
        public readonly Tag Attack, Spell, Movement;

        /// <summary>The kinds of damage, which armor and magic resist each reduce.</summary>
        public readonly Tag Physical, Magical;

        /// <summary>The states a debuff gives, and the kind every debuff is, which a cleanse names.</summary>
        public readonly Tag Stunned, Silenced, Debuff;

        public readonly IGameplayAbilitySystem Warden, Pyromancer, Duelist, Oracle;

        // The Warden: a tank. Armor 100, so a physical blow does half.
        public readonly Ability Cleave, Bulwark, ShieldBash, Rally;

        // The Pyromancer: a mage. Magic resist 100, so a spell does half to it.
        public readonly Ability Firebolt, Hush, Blink, Inferno;

        // The Duelist: half its cooldowns off, and a quarter of its physical damage back as health.
        public readonly Ability Lunge, Flurry, Parry, Execute;

        // The Oracle: a support. A fifth of its cooldowns off.
        public readonly Ability Mend, Purify, Smite, Clarity;

        public readonly GameplayEffect Stun, Silence;

        private readonly ForgeAssets make;

        public MobaLab(World world, ForgeAssets make, Hub hub)
        {
            this.make = make;
            Health = make.Attribute("Health");
            Mana = make.Attribute("Mana");
            Armor = make.Attribute("Armor");
            MagicResist = make.Attribute("Magic Resist");
            CooldownReduction = make.Attribute("Cooldown Reduction");
            Lifesteal = make.Attribute("Lifesteal");

            var kind = make.NewTag("Lab.Kind");
            Attack = Tag.Generate(kind.Name + ".Attack");
            Spell = Tag.Generate(kind.Name + ".Spell");
            Movement = Tag.Generate(kind.Name + ".Movement");
            var damage = make.NewTag("Lab.Damage");
            Physical = Tag.Generate(damage.Name + ".Physical");
            Magical = Tag.Generate(damage.Name + ".Magical");
            var state = make.NewTag("Lab.State");
            Stunned = Tag.Generate(state.Name + ".Stunned");
            Silenced = Tag.Generate(state.Name + ".Silenced");
            Debuff = make.NewTag("Lab.Status.Debuff");

            Warden = Hero(world, hub, "Warden", health: 800f, mana: 200f, armor: 100f, magicResist: 0f, reduction: 0f, lifesteal: 0f);
            Pyromancer = Hero(world, hub, "Pyromancer", health: 500f, mana: 400f, armor: 0f, magicResist: 100f, reduction: 0f, lifesteal: 0f);
            Duelist = Hero(world, hub, "Duelist", health: 600f, mana: 200f, armor: 25f, magicResist: 25f, reduction: 0.5f, lifesteal: 0.25f);
            Oracle = Hero(world, hub, "Oracle", health: 500f, mana: 500f, armor: 25f, magicResist: 25f, reduction: 0.2f, lifesteal: 0f);

            Stun = Status("Stun", Stunned, seconds: 1.5f);
            Silence = Status("Silence", Silenced, seconds: 2f);

            Cleave = Learn(Warden, Kit("Cleave", Attack, mana: 20f, cooldown: 4f, Hit(Damage("Cleave", 60f, Physical))));
            Bulwark = Learn(Warden, Kit("Bulwark", Spell, mana: 40f, cooldown: 10f, Hit(Buff("Bulwark", Armor, 50f, seconds: 4f))));
            ShieldBash = Learn(Warden, Kit("Shield Bash", Attack, mana: 50f, cooldown: 8f, Hit(Damage("Shield Bash", 30f, Physical), Stun)));
            Rally = Learn(Warden, Kit("Rally", Spell, mana: 80f, cooldown: 20f, Hit(Restore("Rally", Health, 150f))));

            Firebolt = Learn(Pyromancer, Kit("Firebolt", Spell, mana: 40f, cooldown: 3f, Hit(Damage("Firebolt", 80f, Magical))));
            Hush = Learn(Pyromancer, Kit("Hush", Spell, mana: 60f, cooldown: 12f, Hit(Silence)));
            Blink = Learn(Pyromancer, Kit("Blink", Movement, mana: 30f, cooldown: 6f, Hit(Buff("Haste", null, 0f, seconds: 1f))));

            // The channel: three ticks a second apart, at its target. A stun or a silence ends it, a blink cancels it, and
            // no other spell can be cast while it runs (FEAT-5)
            var tick = Damage("Inferno", 60f, Magical);
            var channel = make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { tick, tick, tick }, DelayBetweenApplicationMs = 1000 });
            channel.CancelledBy = new List<Tag> { Movement };
            channel.Blocks = new List<Tag> { Spell };
            Inferno = Kit("Inferno", Spell, mana: 120f, cooldown: 30f, channel);
            Inferno.Tags.TagRequirements.WhileActiveRequirements = new AvoidRequireTagGroup
            {
                AvoidTags = new List<TagQuery> { new TagQuery { Tag = Stunned }, new TagQuery { Tag = Silenced } },
                RequireTags = new List<TagQuery>(),
            };
            Learn(Pyromancer, Inferno);

            Lunge = Learn(Duelist, Kit("Lunge", Attack, mana: 20f, cooldown: 2f, Hit(Damage("Lunge", 40f, Physical))));
            var cut = Damage("Flurry", 20f, Physical);
            Flurry = Learn(Duelist, Kit("Flurry", Attack, mana: 40f, cooldown: 6f,
                make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { cut, cut, cut }, DelayBetweenApplicationMs = 500 })));
            Parry = Learn(Duelist, Kit("Parry", Spell, mana: 30f, cooldown: 8f, Hit(Buff("Parry", Armor, 100f, seconds: 1f))));
            Execute = Learn(Duelist, Kit("Execute", Attack, mana: 80f, cooldown: 20f, Hit(Damage("Execute", 150f, Physical))));

            Mend = Learn(Oracle, Kit("Mend", Spell, mana: 50f, cooldown: 4f, Hit(Restore("Mend", Health, 100f))));
            var cleanse = make.Effect("Purify", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = null });
            cleanse.Tags.RemovesEffectsTagged = new List<Tag> { Debuff };
            Purify = Learn(Oracle, Kit("Purify", Spell, mana: 60f, cooldown: 10f, Hit(cleanse)));
            Smite = Learn(Oracle, Kit("Smite", Spell, mana: 30f, cooldown: 3f, Hit(Damage("Smite", 60f, Magical))));
            Clarity = Learn(Oracle, Kit("Clarity", Spell, mana: 0f, cooldown: 15f, Hit(Restore("Clarity", Mana, 100f))));
        }

        // A hero: its stats, its armor and magic resist, which reduce what it takes, and lifesteal on what it deals
        private IGameplayAbilitySystem Hero(World world, Hub hub, string name, float health, float mana, float armor, float magicResist,
            float reduction, float lifesteal)
        {
            var set = make.Set(
                make.Element(Health, health, health, EAttributeOverflowPolicy.ZeroToBase),
                make.Element(Mana, mana, mana, EAttributeOverflowPolicy.ZeroToBase),
                make.Element(Armor, armor, armor),
                make.Element(MagicResist, magicResist, magicResist),
                make.Element(CooldownReduction, reduction, reduction),
                make.Element(Lifesteal, lifesteal, lifesteal));

            // A quarter of a physical blow back as health, read from the hero's Lifesteal: the framework's own worker
            set.WorkerGroup.ImpactWorkers.Add(new RelativeOperationContextImpactWorker
            {
                ImpactedAttribute = Health, AnyImpactType = false, ImpactType = new List<Tag> { Physical },
                ImpactTarget = EAttributeTargetExpanded.CurrentOrBase, ImpactSign = ESignPolicy.Negative,
                AnyContextTag = true, ImpactAbilityContextTags = new List<Tag>(),
                WorkAttribute = Health, WorkImpactType = new List<Tag>(), WorkSignPolicy = ESignPolicy.Positive,
                RelativeAttribute = Lifesteal, Operation = ECalculationOperation.Multiply, WithRespectTo = ESourceTarget.Source,
                ExecutionMode = EWorkerExecution.Deferred,
            });

            var hero = world.Entity(hub, set, name);
            hero.GetAttributeSystem().ProvideWorker(Resist(Armor, Physical));
            hero.GetAttributeSystem().ProvideWorker(Resist(MagicResist, Magical));
            return hero;
        }

        private ResistWorker Resist(ForgeAttribute resist, Tag kind) => new()
        {
            TimingConfig = AbstractAttributeWorker.EChangeEventTiming.PreChange,
            TargetAttribute = Health,
            AllowSelfModification = true,
            TargetModification = EAttributeTargetExpanded.Current,
            SignPolicy = ESignPolicyExtended.Negative,
            AnyImpactType = false,
            ImpactTypes = new List<Tag> { kind },
            Resist = resist,
        };

        // An ability of a kind, with its mana cost and its cooldown, which the hero's cooldown reduction shortens. A stun
        // or a silence keeps any of them from being cast
        private Ability Kit(string name, Tag kindOf, float mana, float cooldown, AbilityTaskBehaviourStage stage)
        {
            var ability = make.Ability(name, stage);
            ability.Tags.ContextTags.Add(kindOf);
            ability.Tags.TagRequirements.SourceRequirements.AvoidTags.Add(new TagQuery { Tag = Stunned });
            ability.Tags.TagRequirements.SourceRequirements.AvoidTags.Add(new TagQuery { Tag = Silenced });
            stage.ApplyUsageEffects = true;
            if (mana > 0f)
                ability.Cost = make.Effect($"{name} Cost", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Mana, Magnitude = -mana });
            ability.Cooldown = make.Effect($"{name} Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = cooldown, Target = null });
            ability.Cooldown.DurationSpecification.DurationOperation = new ScalerMagnitudeOperation
            {
                Magnitude = cooldown,
                RealMagnitude = EMagnitudeOperation.MultiplyWithScaler,
                Scaler = new CooldownReductionScaler { Reduction = CooldownReduction },
            };
            return ability;
        }

        private static Ability Learn(IGameplayAbilitySystem hero, Ability ability)
        {
            hero.GetAbilitySystem().GiveAbility(ability, 1, out _);
            return ability;
        }

        private AbilityTaskBehaviourStage Hit(params GameplayEffect[] effects) =>
            make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect>(effects) });

        private GameplayEffect Damage(string name, float amount, Tag kindOf)
        {
            var hit = make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -amount });
            hit.ImpactSpecification.ImpactTypes.Add(kindOf);
            return hit;
        }

        private GameplayEffect Restore(string name, ForgeAttribute attribute, float amount) =>
            make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = attribute, Magnitude = amount });

        private GameplayEffect Buff(string name, ForgeAttribute attribute, float amount, float seconds) =>
            make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = seconds, Target = attribute, Magnitude = amount });

        // A debuff that gives a state for a while; every debuff is a kind the cleanse names
        private GameplayEffect Status(string name, Tag gives, float seconds)
        {
            var status = make.Effect(name, new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = seconds, Target = null, GrantedTags = new List<Tag> { gives },
            });
            status.Tags.ContextTags = new List<Tag> { Tag.Generate(Debuff.Name + "." + name) };
            return status;
        }

        /// <summary>
        /// A hero casts one of its abilities, at a target or, with none, at itself, under Refuse: a cast that can't go now
        /// is refused, not buffered (FEAT-6). False when it's refused.
        /// </summary>
        public bool Cast(IGameplayAbilitySystem hero, Ability ability, IGameplayAbilitySystem target = null)
        {
            var abilities = hero.GetAbilitySystem();
            if (!abilities.TryGetAbilityContainer(ability, out var container)) return false;
            ProcessDataPacket request = null;
            if (target != null)
            {
                request = ProcessDataPacket.Default();
                request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            }
            return abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, request, EAbilityActivationPolicyExtended.ActivateIfIdle));
        }

        public bool Ready(IGameplayAbilitySystem hero, Ability ability) =>
            hero.GetAbilitySystem().TryGetAbilityContainer(ability, out var container) && hero.GetAbilitySystem().CanActivateAbility(container.Index);

        public bool Casting(IGameplayAbilitySystem hero, Ability ability) =>
            hero.GetAbilitySystem().TryGetAbilityContainer(ability, out var container) && container.IsActive;

        public float Value(IGameplayAbilitySystem hero, ForgeAttribute attribute) =>
            hero.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        /// <summary>How long an ability's cooldown on a hero lasts in all, or 0 when it isn't on cooldown.</summary>
        public float CooldownOf(IGameplayAbilitySystem hero, Ability ability) =>
            hero.TryGetEffectContainer(ability.Cooldown, out var cooldown) ? cooldown.TotalDuration : 0f;

        public bool Has(IGameplayAbilitySystem hero, GameplayEffect effect) => hero.TryGetEffectContainer(effect, out _);
    }

    /// <summary>
    /// Armor or magic resist, as a MOBA has it: a blow of its kind does 100 / (100 + resist) of itself, so 100 resist
    /// halves it. An inline worker on health's reductions, as a game would write one.
    /// </summary>
    [Serializable]
    public sealed class ResistWorker : AbstractFocusedAttributeWorker
    {
        public ForgeAttribute Resist;

        public override EWorkerExecution Execution => EWorkerExecution.Inline;

        public override void Intercept(WorkerContext ctx)
        {
            float blow = ctx.Change.Value.CurrentValue;
            if (blow >= 0f || !ctx.AttributeCache.TryGetValue(Resist, out var resist)) return;
            float through = 100f / (100f + Mathf.Max(0f, resist.ActiveValue.CurrentValue));
            ctx.Change.Override(new AttributeValue(blow * through, ctx.Change.Value.BaseValue));
        }

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            yield break;
        }
    }

    /// <summary>A cooldown's share left by its caster's cooldown reduction: 1 less the reduction, as a game would write one.</summary>
    [Serializable]
    public sealed class CooldownReductionScaler : AbstractScaler
    {
        public ForgeAttribute Reduction;

        public override void Initialize(IAttributeImpactCarrier deriv)
        {
        }

        public override float Evaluate(IAttributeImpactCarrier deriv)
        {
            var caster = deriv?.GetOrigin()?.GetSource();
            if (caster == null || !caster.TryGetAttributeValue(Reduction, out var reduction)) return 1f;
            return Mathf.Clamp01(1f - reduction.CurrentValue);
        }
    }

    /// <summary>The lab's clock: half a second of game time each frame, whatever the frame rate, so a round is exact.</summary>
    public sealed class MobaClock : IForgeClock
    {
        public float DeltaTime => MobaLab.SecondsPerFrame;
        public float UnscaledDeltaTime => MobaLab.SecondsPerFrame;
        public float FixedDeltaTime => MobaLab.SecondsPerFrame;
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public int FrameCount => UnityEngine.Time.frameCount;
    }
}
