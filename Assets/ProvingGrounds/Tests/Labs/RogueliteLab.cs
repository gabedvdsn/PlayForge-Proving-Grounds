using System;
using System.Collections.Generic;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Roguelite lab, Phase 5's fifth: a hero's build of relics and items. A whetstone stacks, +5 might a stack
    /// (FEAT-14); a three-piece Ember set grants a bonus at two pieces and a bigger one at three, which follows the set's
    /// tag as it comes and goes (FEAT-3); tomes stack and each teaches +2 might, twice that under the Library aura; and the
    /// Crown of Ages grants +4 might a level of its own, rescaling as it levels up. It's a small game, built in code as a
    /// game would author it; its bot, the tests in <see cref="RogueliteLabTests"/>, plays it.
    /// </summary>
    public sealed class RogueliteLab
    {
        public const float WhetstoneMight = 5f, PactMight = 20f, AscensionMight = 50f, LessonMight = 2f;
        public static readonly float[] CrownMight = { 4f, 8f, 12f, 16f };

        public readonly ForgeAttribute Might;
        public readonly IGameplayAbilitySystem Hero;
        public readonly Item Whetstone, EmberRing, EmberCrown, EmberBoots, Tome, CrownOfAges;
        public readonly GameplayEffect Pact, Ascension, Library, Lesson;

        /// <summary>What an Ember piece grants while equipped, which the set's bonuses count.</summary>
        public readonly Tag Ember;
        public readonly Tag Studious;

        private readonly World world;
        private readonly TagListener setListener;

        public RogueliteLab(World world, ForgeAssets make, Hub hub)
        {
            this.world = world;
            Might = make.Attribute("Might");
            Hero = world.Entity(hub, make.Set(make.Element(Might, 0f, 0f)), "Hero");

            // A whetstone: +5 might a stack, its one effect reading its stack count (FEAT-14)
            var sharpness = make.Effect("Sharpness", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = Might, Magnitude = WhetstoneMight,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = new ItemStacksScaler(),
            });
            Whetstone = make.Item("Whetstone");
            Whetstone.Definition.MaxStackSize = 5;
            Whetstone.GrantedEffects = new List<GameplayEffect> { sharpness };

            // The Ember set: each piece grants the set's tag while equipped, and the hero's bonuses follow its count (FEAT-3)
            Ember = make.NewTag("Lab.Set.Ember");
            EmberRing = SetPiece(make, "Ember Ring");
            EmberCrown = SetPiece(make, "Ember Crown");
            EmberBoots = SetPiece(make, "Ember Boots");
            Pact = make.Effect("Ember Pact", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = Might, Magnitude = PactMight });
            Ascension = make.Effect("Ember Ascension", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = Might, Magnitude = AscensionMight });
            setListener = Hero.GetTagCache().Listen(Ember, change => Bonuses(Math.Max(0, change.After)), ETagChangeKind.AnyCount);

            // Tomes: each teaches +2 might for good, twice that while the hero is under the Library aura
            Studious = make.NewTag("Lab.State.Studious");
            Library = make.Effect("Library", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { Studious } });
            Lesson = make.Effect("Lesson", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = Might, Slot = EAttributeTarget.CurrentAndBase, Magnitude = LessonMight,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler,
                MagnitudeScaler = new ConditionalScaler
                {
                    When = new TagCondition { On = NumberSide.Caster, Query = new TagQuery { Tag = Studious } },
                    TrueValue = 2f, FalseValue = 1f,
                },
            });
            Tome = make.Item("Tome of Strength");
            Tome.Definition.MaxStackSize = 10;

            // The Crown of Ages: +4 might a level of its own
            var ages = make.Effect("Ages", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = Might, MagnitudeOp = EMagnitudeOperation.UseScaler,
                MagnitudeScaler = new SimpleScaler { StretchOverLevels = false, MaxLevel = CrownMight.Length, LevelValues = (float[])CrownMight.Clone() },
            });
            CrownOfAges = make.Item("Crown of Ages");
            CrownOfAges.MaxLevel = CrownMight.Length;
            CrownOfAges.GrantedEffects = new List<GameplayEffect> { ages };
        }

        private Item SetPiece(ForgeAssets make, string name)
        {
            var piece = make.Item(name);
            piece.Tags.PassiveGrantedTags = new List<Tag> { Ember };
            return piece;
        }

        // Two pieces make the pact, three the ascension, and each goes as the count falls below it
        private void Bonuses(int pieces)
        {
            Keep(Pact, pieces >= 2);
            Keep(Ascension, pieces >= 3);
        }

        private void Keep(GameplayEffect bonus, bool on)
        {
            bool has = Hero.TryGetEffectContainer(bonus, out _);
            if (on && !has) world.Apply(Hero, Hero, bonus);
            else if (!on && has) Hero.RemoveGameplayEffect(bonus);
        }

        public ItemSystemComponent Items => Hero.GetItemSystem();

        public bool Equip(Item item) => Items.GiveAndEquipItem(item, out _);

        /// <summary>Reads a tome: one comes off its stack, and its lesson lands. False with none left.</summary>
        public bool Read()
        {
            if (Items.RemoveItem(Tome, 1) != 1) return false;
            return world.Apply(Hero, Hero, Lesson);
        }

        /// <summary>Sets the crown's own level, as an upgrade at a shrine would.</summary>
        public bool LevelCrown(int level) =>
            Hero.FindLevelSystem(out var levels) && levels.TrySetLevel(CrownOfAges.Tags.AssetTag, new IntValuePair(level, CrownOfAges.MaxLevel)).Success;

        public float Value => Hero.TryGetAttributeValue(Might, out var value) ? value.CurrentValue : float.NaN;
        public float BaseValue => Hero.TryGetAttributeValue(Might, out var value) ? value.BaseValue : float.NaN;
    }
}
