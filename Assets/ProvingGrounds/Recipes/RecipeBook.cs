using System;
using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Recipes
{
    /// <summary>Keeps a recipe's asset: in memory, as a test builds the recipes, or saved in place, as the sample's builder does.</summary>
    public interface IRecipeMaker
    {
        /// <summary>The asset to use for <paramref name="fresh"/>, at its path under the recipes' folder.</summary>
        T Keep<T>(string path, T fresh) where T : ScriptableObject;
    }

    /// <summary>Keeps each asset in memory, as a test builds the recipes, and lets them go as it's disposed.</summary>
    public sealed class MemoryRecipeMaker : IRecipeMaker, IDisposable
    {
        private readonly List<Object> made = new();

        public T Keep<T>(string path, T fresh) where T : ScriptableObject
        {
            made.Add(fresh);
            return fresh;
        }

        public void Dispose()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }
    }

    /// <summary>The recipes' assets, as Documentation/RECIPES.md describes each (TW-23).</summary>
    public sealed class RecipeSet
    {
        public ForgeAttribute Health;

        // A curse that bursts
        public GameplayEffect Curse, CurseBurst;

        // A counter
        public Ability Retaliate;
        public GameplayEffect Retaliation;

        // A crit
        public ValuePipeline CriticalStrike;
        public GameplayEffect CritBlow;

        // An execute
        public ValuePipeline Execute;
        public GameplayEffect CullingBlow;

        // Lifesteal
        public GameplayEffect VampiricStrike;

        // Damage return
        public GameplayEffect Thorns;

        // Can't die
        public GameplayEffect Undying;

        // On hit
        public Item FrostBlade;
        public GameplayEffect FrostBite;

        // On kill
        public Item ReapersMark;
        public GameplayEffect Feast;

        // A phase
        public AttributeSet BossSet;
        public GameplayEffect Enrage;
    }

    /// <summary>
    /// The recipes of Documentation/RECIPES.md, each built from PlayForge's own assets and fields (TW-23, Decision 63): the
    /// Additional Content sample's assets are made from it, and the recipe tests play what it makes.
    /// </summary>
    public static class RecipeBook
    {
        /// <summary>The tag Enrage grants the boss as its phase turns.</summary>
        // A state the recipe grants, exposed where granted tags are picked (TAG-5)
        [FarEmerald.PlayForge.Extended.ForgeTagExposed(FarEmerald.PlayForge.Extended.ForgeContext.Granted)]
        public static readonly Tag Enraged = Tag.Generate("Recipe.Enraged");

        /// <summary>Builds every recipe, each asset kept by <paramref name="maker"/> before another refers to it.</summary>
        public static RecipeSet Build(IRecipeMaker maker)
        {
            var set = new RecipeSet();
            var health = set.Health = maker.Keep("Attributes/Health", Attribute("Health"));

            // A curse that bursts: 5 each second for 3 seconds, then 30 as it ends, by a contained effect On Remove
            set.CurseBurst = maker.Keep("Bursting Curse/Curse Burst", Effect("Curse Burst", health, -30f));
            var curse = Effect("Curse", health, -5f, EEffectDurationPolicy.Durational, duration: 3f, ticks: 3);
            curse.ImpactSpecification.Packets = new[]
            {
                new ContainedEffectPacket { Policy = EApplyTickRemove.OnRemove, ContainedEffect = set.CurseBurst, LandsOn = EEffectRecipient.Target },
            };
            set.Curse = maker.Keep("Bursting Curse/Curse", curse);

            // A counter: whoever's loss to its holder's Health sets it off takes 10 back, the trigger's target its instigator
            set.Retaliation = maker.Keep("Counter/Retaliation", Effect("Retaliation", health, -10f));
            var retaliate = Ability("Retaliate", new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { set.Retaliation } });
            retaliate.Definition.Triggers.Add(new AbilityTrigger
            {
                Source = EAbilityTriggerSource.GameplayEvent, Tag = Tags.IMPACT_TAKEN, Attribute = health,
                Sign = ETriggerImpactSign.Loss, Target = ETriggerTarget.Instigator,
            });
            set.Retaliate = maker.Keep("Counter/Retaliate", retaliate);

            // A crit: a pipeline whose Chance stage doubles the hit on a 25% chance
            set.CriticalStrike = maker.Keep("Critical Strike/Critical Strike",
                Pipeline("Critical Strike", "Doubles a hit on a 25% chance", new ChanceStage { Chance = 0.25f, Multiplier = 2f, Note = "critical" }));
            var critBlow = Effect("Crit Blow", health, -20f);
            critBlow.ImpactSpecification.Pipeline = set.CriticalStrike;
            set.CritBlow = maker.Keep("Critical Strike/Crit Blow", critBlow);

            // An execute: a pipeline whose Execute stage takes the rest where the hit would leave under a fifth
            set.Execute = maker.Keep("Execute/Execute",
                Pipeline("Execute", "Takes the rest where a hit would leave its target under a fifth of its Health", new ExecuteStage { Share = 0.2f, Attribute = health }));
            var culling = Effect("Culling Blow", health, -15f);
            culling.ImpactSpecification.Pipeline = set.Execute;
            set.CullingBlow = maker.Keep("Execute/Culling Blow", culling);

            // Lifesteal: the effect's own, half of what it deals
            var vampiric = Effect("Vampiric Strike", health, -20f);
            vampiric.Lifesteal = new EffectLifesteal { Share = 0.5f };
            set.VampiricStrike = maker.Keep("Lifesteal/Vampiric Strike", vampiric);

            // Damage return: a lasting effect whose worker group returns half of each loss to its dealer
            var thorns = Effect("Thorns", null, 0f, EEffectDurationPolicy.Infinite);
            thorns.WorkerGroup = Workers(new ReturnDamageAttributeWorker { TargetAttribute = health, Share = 0.5f });
            set.Thorns = maker.Keep("Damage Return/Thorns", thorns);

            // Can't die: a lasting effect whose worker group keeps Health from falling below 1
            var undying = Effect("Undying", null, 0f, EEffectDurationPolicy.Durational, duration: 5f);
            undying.WorkerGroup = Workers(new CantFallBelowAttributeWorker { TargetAttribute = health, Floor = 1f });
            set.Undying = maker.Keep("Undying/Undying", undying);

            // On hit: an item whose On Hit bites each one its holder hits for 5 more
            set.FrostBite = maker.Keep("On Hit/Frost Bite", Effect("Frost Bite", health, -5f));
            var blade = Item("Frost Blade");
            blade.OnHit.Add(new OnHitEntry { Attribute = health, Effects = new List<GameplayEffect> { set.FrostBite }, To = EOnHitTo.Target });
            set.FrostBlade = maker.Keep("On Hit/Frost Blade", blade);

            // On kill: an item whose On Hit, only on a kill, feeds its holder 20
            set.Feast = maker.Keep("On Kill/Feast", Effect("Feast", health, 20f));
            var mark = Item("Reaper's Mark");
            mark.OnHit.Add(new OnHitEntry
            {
                Attribute = health, Effects = new List<GameplayEffect> { set.Feast }, To = EOnHitTo.Holder, OnlyOnKill = true,
            });
            set.ReapersMark = maker.Keep("On Kill/Reapers Mark", mark);

            // A phase: a boss whose Threshold worker enrages it, once, as its Health falls to half
            var enrage = Effect("Enrage", null, 0f, EEffectDurationPolicy.Infinite);
            enrage.Tags.GrantedTags.Add(Enraged);
            set.Enrage = maker.Keep("Phase/Enrage", enrage);
            var boss = Set("Boss", (health, 1000f));
            boss.WorkerGroup.AnalysisWorkers.Add(new ThresholdAnalysisWorker
            {
                Target = health, What = EAttributeTargetBinary.Share, Threshold = 0.5f, Crossing = EThresholdCrossing.Falling,
                TriggerOnce = true, Effects = new List<GameplayEffect> { set.Enrage },
            });
            set.BossSet = maker.Keep("Phase/Boss", boss);
            return set;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Each asset, made as its inspector fills one in
        // ═══════════════════════════════════════════════════════════════════════════

        internal static string Compact(string name) => name.Replace(" ", "").Replace("'", "");

        internal static ForgeAttribute Attribute(string name)
        {
            var attribute = ScriptableObject.CreateInstance<ForgeAttribute>();
            attribute.name = attribute.Name = name;
            attribute.Textures = new List<TextureItem>();
            return attribute;
        }

        private static AttributeSet Set(string name, params (ForgeAttribute Attribute, float Value)[] values)
        {
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            set.name = set.Name = name;
            set.AssetTag = Tag.Generate($"Recipe.Set.{Compact(name)}");
            set.Attributes = new List<AttributeSetElement>();
            foreach (var (attribute, value) in values)
            {
                set.Attributes.Add(new AttributeSetElement
                {
                    Attribute = attribute,
                    Current = new AttributeMagnitudeSpec { Magnitude = value, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                    Base = new AttributeMagnitudeSpec { Magnitude = value, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                    Overflow = new AttributeOverflowData { Policy = EAttributeOverflowPolicy.ZeroToBase },
                    RetentionGroup = Tags.DEFAULT,
                    Constraints = new AttributeConstraints { AutoClamp = true, RoundingMode = EAttributeRoundingPolicy.None },
                });
            }
            set.SubSets = new List<AttributeSet>();
            set.WorkerGroup = new StandardWorkerGroup();
            return set;
        }

        // An effect with one impact, an add to the attribute it names, or none; instant unless it says; its asset tag in a
        // family of the sample's, as Recipe.Effect.Curse
        internal static GameplayEffect Effect(string name, ForgeAttribute attribute, float magnitude,
            EEffectDurationPolicy policy = EEffectDurationPolicy.Instant, float duration = 0f, int ticks = 0, string family = "Recipe")
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.name = name;
            effect.Definition = new GameplayEffectDefinition { Name = name, Textures = new List<TextureItem>() };
            effect.Tags = new GameplayEffectTags
            {
                AssetTag = Tag.Generate($"{family}.Effect.{Compact(name)}"),
                ContextTags = new List<Tag>(),
                GrantedTags = new List<Tag>(),
            };
            effect.Workers = new List<AbstractEffectWorker>();
            effect.SourceRequirements = NoRequirements();
            effect.TargetRequirements = NoRequirements();
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = attribute,
                TargetImpact = EAttributeTarget.Current,
                ImpactOperation = ECalculationOperation.Add,
                AffiliationPolicy = EAffiliationPolicy.AlwaysAllow,
                ImpactTypes = new List<Tag>(),
                MagnitudeOperation = new ScalableFloat { Magnitude = magnitude, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Packets = Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = policy,
                DurationOperation = new ScalableFloat { Magnitude = duration, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                EnablePeriodicTicks = ticks > 0,
                TicksOperation = new ScalableInt { Magnitude = ticks },
                TickIntervalOperation = new ScalableFloat { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                AdditionalExecuteTicksOperation = new ScalableInt { Magnitude = 0 },
                StackAmountOperation = new ScalableInt { Magnitude = 1 },
                StackingConfigs = new List<AbstractStackingConfig>(),
                RealDeltaTime = EMagnitudeOperation.AddScaler,
            };
            return effect;
        }

        internal static EffectTagRequirements NoRequirements() => new()
        {
            ApplicationRequirements = new AvoidRequireTagGroup(),
            OngoingRequirements = new AvoidRequireTagGroup(),
            RemovalRequirements = new AvoidRequireTagGroup(),
        };

        internal static StandardWorkerGroup Workers(params AbstractAttributeWorker[] workers)
        {
            var group = new StandardWorkerGroup();
            group.AttributeWorkers.AddRange(workers);
            return group;
        }

        internal static ValuePipeline Pipeline(string name, string description, params AbstractValueStage[] stages)
        {
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            pipeline.name = name;
            pipeline.Description = description;
            pipeline.Stages = new List<AbstractValueStage>(stages);
            return pipeline;
        }

        internal static Ability Ability(string name, params AbstractAbilityTask[] tasks) => Ability("Recipe", name, tasks);

        // An ability of one stage of these tasks, its asset tag in a family of the sample's
        internal static Ability Ability(string family, string name, params AbstractAbilityTask[] tasks)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.name = name;
            ability.Definition = new AbilityDefinition { Name = name, Textures = new List<TextureItem>() };
            ability.Tags = new AbilityTags
            {
                AssetTag = Tag.Generate($"{family}.Ability.{Compact(name)}"),
                ContextTags = new List<Tag>(),
                PassiveGrantedTags = new List<Tag>(),
                ActiveGrantedTags = new List<Tag>(),
                TagRequirements = new AbilityTagRequirements
                {
                    SourceRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
                    TargetRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
                },
            };
            ability.Behaviour = new AbilityBehaviour
            {
                Stages = new List<AbilityTaskBehaviourStage> { new() { Tasks = new List<AbstractAbilityTask>(tasks) } },
            };
            ability.WorkerGroup = new StandardWorkerGroup();
            return ability;
        }

        internal static Item Item(string name, string family = "Recipe")
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.name = name;
            item.Definition.Name = name;
            item.Tags.AssetTag = Tag.Generate($"{family}.Item.{Compact(name)}");
            return item;
        }
    }
}
