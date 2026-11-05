using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Tactics lab, Phase 5's seventh: a knight and an archer against two goblins, a turn at a time. The lab's clock
    /// stands still until a turn ends, then moves a whole turn in one frame, so a poison lasts its turns and a cooldown
    /// counts them, however many frames a turn takes. Between turns a squad is saved, each unit to JSON, and a save loads
    /// back onto the same units, or onto a fresh squad as a new session would (FEAT-9).
    /// </summary>
    public sealed class TacticsLab
    {
        /// <summary>A turn of game time, which the clock moves at once as a turn ends.</summary>
        public const float Turn = 1f;

        public const float SlashDamage = 30f, PoisonDamage = 10f, MendHeal = 25f;
        public const int PoisonTurns = 3, SlashCooldownTurns = 2, GuardTurns = 1;

        public readonly ForgeAttribute Health;

        /// <summary>What a slash is, which a guard turns aside.</summary>
        public readonly Tag Strike;

        public readonly Ability Slash, PoisonArrow, Guard, Mend;
        public readonly GameplayEffect Poison, Guarded;

        /// <summary>Names the lab's assets for a save, by IDs that last as a new session makes the assets again.</summary>
        public readonly ForgeAssetCatalog Catalog;

        /// <summary>The clock, which stands still until a turn ends.</summary>
        public readonly TurnClock Clock = new();

        private readonly World world;
        private readonly Hub hub;
        private readonly AttributeSet knightSet, archerSet, goblinSet;
        private readonly Dictionary<string, Ability> byName = new();

        public TacticsLab(World world, ForgeAssets make, Hub hub)
        {
            this.world = world;
            this.hub = hub;

            Health = make.Attribute("Health");
            Strike = make.NewTag("Tactics.Kind.Strike");
            var guarding = make.NewTag("Tactics.State.Guarding");

            knightSet = make.Set(make.Element(Health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            archerSet = make.Set(make.Element(Health, 80f, 80f, EAttributeOverflowPolicy.ZeroToBase));
            goblinSet = make.Set(make.Element(Health, 80f, 80f, EAttributeOverflowPolicy.ZeroToBase));

            // Slash: a strike, and two turns before the next. Its stage applies the usage effects, the cooldown among them,
            // which no stage applied at first, so the cooldown never came on (ED-41)
            var slashHit = make.Effect("Slash Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -SlashDamage });
            slashHit.Tags.ContextTags = new List<Tag> { Strike };
            var slashStage = make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { slashHit } });
            slashStage.ApplyUsageEffects = true;
            Slash = make.Ability("Slash", slashStage);
            Slash.Cooldown = make.Effect("Slash Cooldown", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = SlashCooldownTurns * Turn, Target = null,
            });

            // Poison Arrow: a poison that bites as each of its three turns ends
            Poison = make.Effect("Poison", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = PoisonTurns * Turn, Target = Health, Magnitude = -PoisonDamage,
                PeriodicTicks = true, Ticks = PoisonTurns,
            });
            PoisonArrow = make.Ability("Poison Arrow", make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Poison } }));

            // Guard: for the rest of the turn, a slash doesn't land (FEAT-8's immunity)
            Guarded = make.Effect("Guarded", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = GuardTurns * Turn, Target = null, GrantedTags = new List<Tag> { guarding },
            });
            Guarded.Tags.BlocksEffectsTagged = new List<Tag> { Strike };
            Guard = make.Ability("Guard", make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { Guarded } }));

            // Mend: a unit heals, up to its most
            var mended = make.Effect("Mended", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = MendHeal });
            Mend = make.Ability("Mend", make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { mended } }));

            foreach (var ability in new[] { Slash, PoisonArrow, Guard, Mend }) byName[ability.GetName()] = ability;

            // The IDs a game would keep, which name the same assets in every session
            Catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            Catalog.Add("health", Health);
            Catalog.Add("slash", Slash);
            Catalog.Add("slash-hit", slashHit);
            Catalog.Add("slash-cooldown", Slash.Cooldown);
            Catalog.Add("poison-arrow", PoisonArrow);
            Catalog.Add("poison", Poison);
            Catalog.Add("guard", Guard);
            Catalog.Add("guarded", Guarded);
            Catalog.Add("mend", Mend);
            Catalog.Add("mended", mended);
        }

        /// <summary>A squad: the knight and the archer, and the goblins Grub and Snag, each a GAS that knows its abilities.</summary>
        public Squad Muster(string name)
        {
            var squad = new Squad();
            squad.Knight = squad.Add("Knight", Unit(knightSet, $"{name} Knight", Slash, Guard));
            squad.Archer = squad.Add("Archer", Unit(archerSet, $"{name} Archer", PoisonArrow, Mend));
            squad.Grub = squad.Add("Grub", Unit(goblinSet, $"{name} Grub", Slash));
            squad.Snag = squad.Add("Snag", Unit(goblinSet, $"{name} Snag", Slash));
            return squad;
        }

        private IGameplayAbilitySystem Unit(AttributeSet set, string name, params Ability[] abilities)
        {
            var unit = world.Entity(hub, set, name);
            foreach (var ability in abilities) unit.GetAbilitySystem().GiveAbility(ability, 1, out _);
            return unit;
        }

        /// <summary>The lab's ability with the name.</summary>
        public Ability Named(string name) => byName[name];

        /// <summary>A unit uses an ability on a target. Refused while the unit is busy or the ability is on cooldown.</summary>
        public bool Use(IGameplayAbilitySystem unit, Ability ability, IGameplayAbilitySystem target)
        {
            var abilities = unit.GetAbilitySystem();
            if (!abilities.TryGetAbilityContainer(ability, out var container)) return false;
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            return abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, request,
                EAbilityActivationPolicyExtended.ActivateIfIdle));
        }

        public bool Busy(IGameplayAbilitySystem unit) => unit.GetAbilitySystem().IsExecuting;

        public float Value(IGameplayAbilitySystem unit) => unit.TryGetAttributeValue(Health, out var value) ? value.CurrentValue : float.NaN;
    }

    /// <summary>
    /// A squad's units by the IDs a save names them by. A fresh squad has the same IDs, as a game's own IDs last across
    /// sessions, so an effect one unit put on another finds its source again (FEAT-9).
    /// </summary>
    public sealed class Squad : IForgeEntityIds
    {
        public IGameplayAbilitySystem Knight, Archer, Grub, Snag;

        private readonly List<(string Id, IGameplayAbilitySystem Unit)> units = new();

        /// <summary>The units, the allies first.</summary>
        public IReadOnlyList<(string Id, IGameplayAbilitySystem Unit)> Units => units;

        internal IGameplayAbilitySystem Add(string id, IGameplayAbilitySystem unit)
        {
            units.Add((id, unit));
            return unit;
        }

        public string IdOf(IGameplayAbilitySystem entity)
        {
            foreach (var (id, unit) in units)
                if (ReferenceEquals(unit, entity)) return id;
            return null;
        }

        public IGameplayAbilitySystem Find(string id)
        {
            foreach (var (unitId, unit) in units)
                if (unitId == id) return unit;
            return null;
        }

        /// <summary>Each unit's save as JSON, by its ID.</summary>
        public Dictionary<string, string> Save(ForgeAssetCatalog catalog)
        {
            var saves = new Dictionary<string, string>();
            foreach (var (id, unit) in units) saves[id] = JsonUtility.ToJson(ForgeSave.Capture(unit, catalog, this));
            return saves;
        }

        /// <summary>
        /// Loads each unit's save, the allies first, so an effect one of their abilities put on a goblin finds that
        /// ability there. True when every unit loaded.
        /// </summary>
        public bool Load(Dictionary<string, string> saves, ForgeAssetCatalog catalog)
        {
            bool all = true;
            foreach (var (id, unit) in units)
            {
                all &= saves.TryGetValue(id, out string json)
                       && ForgeSave.Restore(unit, JsonUtility.FromJson<ForgeSnapshot>(json), catalog, this);
            }
            return all;
        }
    }

    /// <summary>The lab's clock: still, until a turn ends and it moves a whole turn in one frame.</summary>
    public sealed class TurnClock : IForgeClock
    {
        /// <summary>The time this frame moves: a turn as one ends, and nothing otherwise.</summary>
        public float Step;

        public float DeltaTime => Step;
        public float UnscaledDeltaTime => Step;
        public float FixedDeltaTime => Step;
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public int FrameCount => UnityEngine.Time.frameCount;
    }
}
