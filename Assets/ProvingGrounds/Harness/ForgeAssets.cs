using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>What <see cref="ForgeAssets.Effect"/> builds. Each field is the plain default unless a test needs otherwise.</summary>
    public sealed class EffectConfig
    {
        public EEffectDurationPolicy Policy = EEffectDurationPolicy.Infinite;
        public ForgeAttribute Target;
        public ECalculationOperation Op = ECalculationOperation.Add;
        public EAttributeTarget Slot = EAttributeTarget.Current;
        public EMultiplyBehaviour MultiplyBehaviour = EMultiplyBehaviour.Multiplicative;
        public float Magnitude;
        public AbstractScaler MagnitudeScaler;
        public EMagnitudeOperation MagnitudeOp = EMagnitudeOperation.UseMagnitude;
        public bool ReverseOnRemoval;

        public float Duration;
        public bool PeriodicTicks;
        public int Ticks;
        public bool TickOnApplication;
        public float FixedDeltaTime;

        public EEffectReApplicationPolicy Reapply = EEffectReApplicationPolicy.AppendNewContainer;
        public EEffectInteractionPolicy Interaction = EEffectInteractionPolicy.DoNothing;
        public EEffectStackPolicy StackPolicy = EEffectStackPolicy.StacksShareOneDuration;
        public int StackAmount = 1;

        public List<Tag> GrantedTags = new();
        public List<AbstractEffectWorker> Workers = new();
    }

    /// <summary>
    /// Builds framework assets in code, for one world. Everything it makes is destroyed with the world,
    /// and tag names are unique within it.
    /// </summary>
    public sealed class ForgeAssets
    {
        private readonly List<ScriptableObject> _made = new();
        private int _unique;

        internal void DestroyAll()
        {
            foreach (var asset in _made)
            {
                if (asset) Object.Destroy(asset);
            }
            _made.Clear();
        }

        private T Own<T>(T asset) where T : ScriptableObject
        {
            _made.Add(asset);
            return asset;
        }

        public string Unique(string stem) => $"{stem}_{_unique++}";

        public Tag NewTag(string stem) => Tag.Generate(Unique(stem));

        public ForgeAttribute Attribute(string name)
        {
            var attribute = Own(ScriptableObject.CreateInstance<ForgeAttribute>());
            attribute.name = name;
            attribute.Name = name;
            attribute.Textures = new List<TextureItem>();
            return attribute;
        }

        public AttributeSetElement Element(
            ForgeAttribute attribute, float current, float @base,
            EAttributeOverflowPolicy overflow = EAttributeOverflowPolicy.Unlimited,
            AttributeMagnitudeSpec currentSpecOverride = null,
            AttributeMagnitudeSpec baseSpecOverride = null,
            bool linkCurrentToBase = false)
        {
            return new AttributeSetElement
            {
                Attribute = attribute,
                Current = currentSpecOverride ?? new AttributeMagnitudeSpec { Magnitude = current, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Base = baseSpecOverride ?? new AttributeMagnitudeSpec { Magnitude = @base, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                LinkCurrentToBase = linkCurrentToBase,
                Overflow = new AttributeOverflowData { Policy = overflow },
                RetentionGroup = Tags.DEFAULT,
                Constraints = new AttributeConstraints { AutoClamp = true, RoundingMode = EAttributeRoundingPolicy.None },
            };
        }

        public AttributeSet Set(params AttributeSetElement[] elements)
        {
            var set = Own(ScriptableObject.CreateInstance<AttributeSet>());
            set.name = Unique("Set");
            set.Name = set.name;
            set.AssetTag = NewTag("Test.Set");
            set.Attributes = new List<AttributeSetElement>(elements);
            set.SubSets = new List<AttributeSet>();
            set.WorkerGroup = new StandardWorkerGroup();
            return set;
        }

        /// <summary>A set with one attribute nobody reads, for entities whose attributes don't matter.</summary>
        public AttributeSet UnusedSet() => Set(Element(Attribute("Unused"), 1f, 1f));

        public EntityIdentity Identity(string name, AttributeSet set)
        {
            var entity = Own(ScriptableObject.CreateInstance<EntityIdentity>());
            entity.name = entity.Name = Unique(name);
            entity.Description = "";
            entity.Textures = new List<TextureItem>();
            entity.AssetTag = NewTag("Test.Entity");
            entity.Affiliation = new List<Tag>();
            entity.GrantedTags = new List<Tag>();
            entity.ContextTags = new List<Tag>();
            entity.MaxLevel = 1;
            entity.MaxAbilitiesOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 };
            entity.MaxItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 };
            entity.MaxEquippedItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 };
            entity.StartingAbilities = new List<Ability>();
            entity.StartingItems = new List<StartingItemContainer>();
            entity.AttributeSet = set;
            entity.WorkerGroup = new StandardWorkerGroup();
            return entity;
        }

        public RuntimeEntityIdentity RuntimeIdentity(string name, AttributeSet set)
        {
            return new RuntimeEntityIdentity
            {
                Name = Unique(name),
                AssetTag = NewTag("Test.Entity"),
                GrantedTags = new List<Tag>(),
                ContextTags = new List<Tag>(),
                Affiliation = new List<Tag>(),
                AttributeSet = set,
                MaxAbilitiesOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
                MaxItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
                MaxEquippedItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
            };
        }

        public GameplayEffect Effect(string name, EffectConfig cfg)
        {
            var fx = Own(ScriptableObject.CreateInstance<GameplayEffect>());
            fx.name = name;
            fx.Definition = new GameplayEffectDefinition { Name = name, Textures = new List<TextureItem>() };
            fx.Tags = new GameplayEffectTags
            {
                AssetTag = NewTag($"Test.Effect.{name}"),
                ContextTags = new List<Tag>(),
                GrantedTags = cfg.GrantedTags,
            };
            fx.Workers = cfg.Workers;
            fx.SourceRequirements = EmptyRequirements();
            fx.TargetRequirements = EmptyRequirements();

            fx.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = cfg.Target,
                TargetImpact = cfg.Slot,
                ImpactOperation = cfg.Op,
                MultiplyBehaviour = cfg.MultiplyBehaviour,
                AffiliationPolicy = EAffiliationPolicy.AlwaysAllow,
                ImpactTypes = new List<Tag>(),
                ReverseImpactOnRemoval = cfg.ReverseOnRemoval,
                MagnitudeOperation = new ScalerMagnitudeOperation
                {
                    Magnitude = cfg.Magnitude,
                    Scaler = cfg.MagnitudeScaler,
                    RealMagnitude = cfg.MagnitudeOp,
                },
                Packets = System.Array.Empty<ContainedEffectPacket>(),
            };

            fx.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = cfg.Policy,
                DurationOperation = new ScalerMagnitudeOperation { Magnitude = cfg.Duration, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                EnablePeriodicTicks = cfg.PeriodicTicks,
                TickOnApplication = cfg.TickOnApplication,
                TicksOperation = new ScalerIntegerMagnitudeOperation { Magnitude = cfg.Ticks },
                TickIntervalOperation = new ScalerMagnitudeOperation { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                AdditionalExecuteTicksOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 0 },
                StackAmountOperation = new ScalerIntegerMagnitudeOperation { Magnitude = cfg.StackAmount },
                StackingConfigs = new List<AbstractStackingConfig>(),
                ReApplicationPolicy = cfg.Reapply,
                ReApplicationInteraction = cfg.Interaction,
                StackPolicy = cfg.StackPolicy,
                RealDeltaTime = cfg.FixedDeltaTime > 0f ? EMagnitudeOperation.UseScaler : EMagnitudeOperation.AddScaler,
                DeltaTimeScaler = cfg.FixedDeltaTime > 0f ? new ConstantScaler { Value = cfg.FixedDeltaTime } : null,
            };

            return fx;
        }

        // Non-null groups with no queries: application passes and removal never self-triggers. A null
        // group flags immediate removal (EffectTagRequirements.CheckRemovalRequirements).
        private static EffectTagRequirements EmptyRequirements()
        {
            return new EffectTagRequirements
            {
                ApplicationRequirements = new AvoidRequireTagGroup(),
                OngoingRequirements = new AvoidRequireTagGroup(),
                RemovalRequirements = new AvoidRequireTagGroup(),
            };
        }

        public Item Item(string name)
        {
            var item = Own(ScriptableObject.CreateInstance<Item>());
            item.name = name;
            item.Tags.AssetTag = NewTag($"Test.Item.{name}");
            return item;
        }

        public AbilityTaskBehaviourStage Stage(params AbstractAbilityTask[] tasks)
        {
            return new AbilityTaskBehaviourStage { Tasks = new List<AbstractAbilityTask>(tasks) };
        }

        public Ability Ability(string name, params AbilityTaskBehaviourStage[] stages)
        {
            var ability = Own(ScriptableObject.CreateInstance<Ability>());
            ability.name = name;
            ability.Definition = new AbilityDefinition { Name = name, Textures = new List<TextureItem>() };
            ability.Tags = new AbilityTags
            {
                AssetTag = NewTag($"Test.Ability.{name}"),
                ContextTags = new List<Tag>(),
                PassiveGrantedTags = new List<Tag>(),
                ActiveGrantedTags = new List<Tag>(),
                TagRequirements = new AbilityTagRequirements
                {
                    SourceRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
                    TargetRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
                },
            };
            ability.Behaviour = new AbilityBehaviour { Stages = new List<AbilityTaskBehaviourStage>(stages) };
            ability.WorkerGroup = new StandardWorkerGroup();
            return ability;
        }
    }
}
