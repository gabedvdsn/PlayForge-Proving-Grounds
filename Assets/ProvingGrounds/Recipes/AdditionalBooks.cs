using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Recipes
{
    /// <summary>The Additional Content sample's formula presets (AC-3, Decision 68).</summary>
    public sealed class FormulaSet
    {
        public ForgeAttribute Armor;

        // A hit's multiplier, or its flat reduction, from its target's Armor
        public ScalerAsset ArmorReciprocal, ArmorHundredOver, ArmorFlat;

        // An item's stacks through a line, a hyperbola and a power
        public ScalerAsset StacksLinear, StacksHyperbolic, StacksExponential;
    }

    /// <summary>The rest of the Additional Content sample's starter set (AC-5, Decision 68).</summary>
    public sealed class StarterSet
    {
        public ForgeAttribute MovementSpeed, Shield;
        public Ability MeleeAttack, Bolt;
        public GameplayEffect MeleeHit, MeleeCooldown, BoltHit, BoltCooldown, Slow, ShieldEffect;
    }

    /// <summary>
    /// The Additional Content sample's formula presets (AC-3, Decision 68): Scaler assets, each named for what it computes,
    /// never for a game. A field reads one through its Shared kind: a Value Pipeline's Multiply stage reads an Armor
    /// multiplier for each hit, its Add stage the flat one, and an item's effect reads its stacks through a curve.
    /// </summary>
    public static class FormulaBook
    {
        public static FormulaSet Build(IRecipeMaker maker)
        {
            var set = new FormulaSet();
            var armor = set.Armor = maker.Keep("Attributes/Armor", RecipeBook.Attribute("Armor"));

            set.ArmorReciprocal = maker.Keep("Armor/Armor Reciprocal 0.06", Formula("1 ÷ (1 + 0.06 × Armor)", "Armor",
                "What a hit leaves of itself against its target's Armor, as a Multiply stage reads it: all of it at 0 Armor, " +
                "a half at about 17, never none; more than all of it below 0.",
                Curve(EStackingCurve.Reciprocal, 0.06f, ArmorOf(armor))));
            set.ArmorHundredOver = maker.Keep("Armor/Armor Hundred Over", Formula("100 ÷ (100 + Armor)", "Armor",
                "What a hit leaves of itself against its target's Armor, as a Multiply stage reads it: a half at 100 Armor, " +
                "a third at 200.",
                Curve(EStackingCurve.Reciprocal, 0.01f, ArmorOf(armor))));
            set.ArmorFlat = maker.Keep("Armor/Armor Flat", Formula("−Armor", "Armor",
                "The target's Armor taken off each hit, as an Add stage reads it: a blow of 100 against 20 Armor lands as 80.",
                Curve(EStackingCurve.Linear, -1f, ArmorOf(armor))));

            var stacks = new ScalableFloat(new ItemStacksScaler(), 1f, EMagnitudeOperation.UseScaler);
            set.StacksLinear = maker.Keep("Stacking/Stacks Linear 0.15", Formula("0.15 × Stacks", "Stacking",
                "An item's stacks, 0.15 each: a bonus that grows by as much with each stack, as 15% more speed a stack.",
                Curve(EStackingCurve.Linear, 0.15f, stacks)));
            set.StacksHyperbolic = maker.Keep("Stacking/Stacks Hyperbolic 0.15", Formula("1 − 1 ÷ (1 + 0.15 × Stacks)", "Stacking",
                "A chance from an item's stacks that nears 1 and never reaches it: 13% with one stack, 60% with ten.",
                Curve(EStackingCurve.Hyperbolic, 0.15f, stacks)));
            set.StacksExponential = maker.Keep("Stacking/Stacks Exponential 0.9", Formula("0.9 ^ Stacks", "Stacking",
                "A share that falls by a tenth with each of an item's stacks: 0.9 with one, 0.73 with three.",
                Curve(EStackingCurve.Exponential, 0.9f, stacks)));
            return set;
        }

        // What its Armor reads, of the one a hit lands on, as the hit lands
        private static ScalableFloat ArmorOf(ForgeAttribute armor) => new(new AttributeBackedScaler
        {
            CaptureAttribute = armor, CaptureFrom = ESourceTarget.Target, CaptureWhen = ECaptureAttributeWhen.OnApplication,
        }, 1f, EMagnitudeOperation.UseScaler);

        private static StackingCurveKind Curve(EStackingCurve curve, float a, ScalableFloat count) =>
            new() { Curve = curve, Coefficient = a, Count = count };

        private static ScalerAsset Formula(string named, string category, string description, AbstractScaler scaler)
        {
            var asset = ScriptableObject.CreateInstance<ScalerAsset>();
            scaler.Name = named;
            asset.Scaler = scaler;
            asset.Category = category;
            asset.Description = description;
            return asset;
        }
    }

    /// <summary>
    /// The rest of the Additional Content sample's starter set (AC-5, Decision 68), built from PlayForge's own assets and
    /// named for what they are: a melee attack and a bolt, each on a cooldown of its own; a slow; and a shield that takes
    /// its holder's hits first. The recipes hold the rest: a damage over time, an on hit, an on kill and a boss's phase.
    /// </summary>
    public static class StarterBook
    {
        private const string Family = "Starter";

        public static StarterSet Build(IRecipeMaker maker, ForgeAttribute health)
        {
            var set = new StarterSet();
            set.MovementSpeed = maker.Keep("Attributes/Movement Speed", RecipeBook.Attribute("Movement Speed"));
            set.Shield = maker.Keep("Attributes/Shield", RecipeBook.Attribute("Shield"));

            // A melee attack: 10 to the one it's cast at, once a second
            set.MeleeHit = maker.Keep("Melee Attack/Melee Hit", RecipeBook.Effect("Melee Hit", health, -10f, family: Family));
            set.MeleeCooldown = maker.Keep("Melee Attack/Melee Cooldown",
                RecipeBook.Effect("Melee Cooldown", null, 0f, EEffectDurationPolicy.Durational, duration: 1f, family: Family));
            var melee = RecipeBook.Ability(Family, "Melee Attack", new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { set.MeleeHit } });
            melee.Cooldown = set.MeleeCooldown;
            set.MeleeAttack = maker.Keep("Melee Attack/Melee Attack", melee);

            // A bolt: 25 to the one it's cast at, once in three seconds
            set.BoltHit = maker.Keep("Bolt/Bolt Hit", RecipeBook.Effect("Bolt Hit", health, -25f, family: Family));
            set.BoltCooldown = maker.Keep("Bolt/Bolt Cooldown",
                RecipeBook.Effect("Bolt Cooldown", null, 0f, EEffectDurationPolicy.Durational, duration: 3f, family: Family));
            var bolt = RecipeBook.Ability(Family, "Bolt", new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { set.BoltHit } });
            bolt.Cooldown = set.BoltCooldown;
            set.Bolt = maker.Keep("Bolt/Bolt", bolt);

            // A slow: 70% of its target's Movement Speed, for 3 seconds
            var slow = RecipeBook.Effect("Slow", set.MovementSpeed, 0.7f, EEffectDurationPolicy.Durational, duration: 3f, family: Family);
            slow.ImpactSpecification.ImpactOperation = ECalculationOperation.Multiply;
            set.Slow = maker.Keep("Slow/Slow", slow);

            // A shield: 50 of it while it lasts, 5 seconds, which takes its holder's hits on Health first, as an Absorb stage
            // the effect adds to each landing on its holder
            var shield = RecipeBook.Effect("Shield", set.Shield, 50f, EEffectDurationPolicy.Durational, duration: 5f, family: Family);
            shield.ImpactSpecification.TargetImpact = EAttributeTarget.CurrentAndBase;
            shield.Modifiers.Add(new EffectModifierEntry
            {
                Rule = new EffectModifierRule
                {
                    Direction = EModifierDirection.Incoming,
                    Changes = new List<AbstractEffectChange>
                    {
                        new ChangeImpactChange { Attribute = health, Stage = new AbsorbStage { Into = set.Shield, On = NumberSide.Target } },
                    },
                },
            });
            set.ShieldEffect = maker.Keep("Shield/Shield", shield);
            return set;
        }
    }
}
