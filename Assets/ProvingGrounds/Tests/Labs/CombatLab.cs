using System.Collections.Generic;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Combat lab, Phase 5's last: a training dummy, and every way an effect lands on it. Each delivery, each
    /// re-application policy with what follows it, and each stacking policy, from a trainer onto one dummy, with a health
    /// bar drawn from the dummy's Health breakdown by retention group: what it has of its own, what effects add while
    /// they last, and the damage it took. It plays on a clock of a quarter second a frame, exact in floats.
    /// </summary>
    public sealed class CombatLab
    {
        public const float SecondsPerFrame = 0.25f;
        public const float StartingHealth = 200f, StartingArmor = 50f;

        public readonly ForgeAttribute Health, Armor, Power;

        /// <summary>The groups the bar draws: the dummy's own, what effects add while they last, and the damage it took.</summary>
        public readonly Tag BaseGroup, BonusGroup, DamageGroup;

        public readonly IGameplayAbilitySystem Trainer, Dummy;

        private readonly World world;
        private readonly ForgeAssets make;

        public CombatLab(World world, ForgeAssets make, Hub hub)
        {
            this.world = world;
            this.make = make;

            Health = make.Attribute("Health");
            Armor = make.Attribute("Armor");
            Power = make.Attribute("Power");
            BaseGroup = make.NewTag("Combat.Group.Base");
            BonusGroup = make.NewTag("Combat.Group.Bonus");
            DamageGroup = make.NewTag("Combat.Group.Damage");

            var health = make.Element(Health, StartingHealth, StartingHealth, EAttributeOverflowPolicy.ZeroToBase);
            var armor = make.Element(Armor, StartingArmor, StartingArmor);
            var power = make.Element(Power, 0f, 0f);
            foreach (var element in new[] { health, armor, power }) element.RetentionGroup = BaseGroup;
            Dummy = world.Entity(hub, make.Set(health, armor, power), "Dummy");
            Trainer = world.Entity(hub, make.Set(make.Element(Health, 100f, 100f)), "Trainer");
        }

        /// <summary>An effect whose changes count toward a group of the bar.</summary>
        public GameplayEffect Effect(string name, EffectConfig config, Tag group)
        {
            var effect = make.Effect(name, config);
            effect.Definition.RetentionGroup = group;
            return effect;
        }

        /// <summary>The trainer applies an effect to the dummy.</summary>
        public bool Apply(GameplayEffect effect) => world.Apply(Trainer, Dummy, effect);

        public float Value(ForgeAttribute attribute) => Dummy.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        public float Max(ForgeAttribute attribute) => Dummy.TryGetAttributeValue(attribute, out var value) ? value.BaseValue : float.NaN;

        /// <summary>How many stacks the effect's container on the dummy holds, 0 once it's gone.</summary>
        public int Stacks(GameplayEffect effect) =>
            Dummy.TryGetEffectContainer(effect, out var container) ? (container as AbstractStackingEffectContainer)?.Stacks ?? 1 : 0;

        /// <summary>The longest time any of the effect's containers on the dummy has left, 0 once it's gone.</summary>
        public float Left(GameplayEffect effect)
        {
            if (!Dummy.TryGetEffectContainers(effect, out var containers)) return 0f;
            float longest = 0f;
            foreach (var container in containers) longest = UnityEngine.Mathf.Max(longest, container.DurationRemaining);
            return longest;
        }

        public HealthBar Bar() => HealthBar.Of(Dummy, Health);
    }

    /// <summary>
    /// A health bar as a game would draw one, from the attribute's breakdown: a segment for each retention group, in the
    /// order the groups came, which together make the attribute's Current and its Base.
    /// </summary>
    public sealed class HealthBar
    {
        public readonly List<(Tag Group, float Current, float Base)> Segments = new();
        public float Current, Max;

        public static HealthBar Of(IGameplayAbilitySystem gas, IAttribute attribute)
        {
            var bar = new HealthBar();
            foreach (var slice in gas.GetAttributeSystem().GetBreakdown(attribute))
            {
                bar.Segments.Add((slice.Group, slice.Value.CurrentValue, slice.Value.BaseValue));
                bar.Current += slice.Value.CurrentValue;
                bar.Max += slice.Value.BaseValue;
            }
            return bar;
        }

        /// <summary>A group's segment of the Current, 0 where the bar has none.</summary>
        public float Of(Tag group)
        {
            foreach (var segment in Segments)
                if (segment.Group == group) return segment.Current;
            return 0f;
        }
    }

    /// <summary>The lab's clock: a quarter second of game time each frame, exact in floats.</summary>
    public sealed class CombatClock : IForgeClock
    {
        public float DeltaTime => CombatLab.SecondsPerFrame;
        public float UnscaledDeltaTime => CombatLab.SecondsPerFrame;
        public float FixedDeltaTime => CombatLab.SecondsPerFrame;
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public int FrameCount => UnityEngine.Time.frameCount;
    }
}
