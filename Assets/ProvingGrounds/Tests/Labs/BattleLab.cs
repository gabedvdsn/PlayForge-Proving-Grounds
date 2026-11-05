using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Battle lab: Blue and Red, eight fighters a side, fight until one side stands. Knights cleave and bash, knocking
    /// their foes back and stunning them; archers shoot arrows and poison darts; mages throw fireballs that burst over an
    /// area and cast a frost nova that slows; clerics heal and bless; summoners call imps and throw bolts. It's built from
    /// PlayForge's own parts: attributes with armor as a worker, abilities with mana costs and cooldowns that a stun
    /// refuses, effects that burn, poison, chill, fortify and regenerate, knockback as a cue, projectiles as the
    /// framework's projectile process drawn from a pool, summons with a lifetime, and deaths through the death watcher.
    /// Each fighter thinks each frame: it takes the nearest foe, closes to its range, and casts what's ready.
    /// </summary>
    public sealed class BattleLab : IDisposable
    {
        public enum EClass
        {
            Knight,
            Archer,
            Mage,
            Cleric,
            Summoner,
            Imp,
        }

        /// <summary>How an effect shows over a fighter's health bar: good or bad, in its color.</summary>
        public readonly struct Badge
        {
            public readonly string Name;
            public readonly bool Positive;
            public readonly Color Color;

            public Badge(string name, bool positive, Color color)
            {
                Name = name;
                Positive = positive;
                Color = color;
            }
        }

        public static readonly Color[] TeamColors = { new(0.25f, 0.55f, 1f), new(1f, 0.32f, 0.28f) };
        public static readonly string[] TeamNames = { "Blue", "Red" };

        /// <summary>The battle playing now, which its tasks, its shots and its cue reach.</summary>
        public static BattleLab Current { get; private set; }

        public readonly ForgeAttribute Health, Mana, Armor, MoveSpeed, ShotSpeed;
        public readonly Tag Physical, Magical, Stunned, Debuff;
        public readonly List<BattleFighter> Fighters = new();
        public readonly Dictionary<GameplayEffect, Badge> Badges = new();

        // What the battle did, which its round checks
        public int Shots, AreaHits, Stuns, Knockbacks, Summons, Heals, Blessings, Deaths;

        /// <summary>Game time since the battle began.</summary>
        public float Elapsed { get; private set; }

        /// <summary>Whether a side has no one standing.</summary>
        public bool Over => Standing(0) == 0 || Standing(1) == 0;

        /// <summary>The side that stands once it's over, or -1 while it isn't or if neither does.</summary>
        public int Winner => !Over ? -1 : Standing(0) > 0 ? 0 : Standing(1) > 0 ? 1 : -1;

        internal readonly Ability Cleave, Bash, Arrow, Dart, Fireball, FrostNova, Mend, Bless, Holy, Summon, Bolt, Bite;
        internal readonly GameplayEffect Regen;

        private readonly World world;
        private readonly ForgeAssets make;
        private readonly Random random;
        private readonly ShotPool pool = new();
        private readonly List<Object> owned = new();
        private readonly Dictionary<IGameplayAbilitySystem, BattleFighter> byGas = new();
        private readonly GameplayEffect summoned, manaRegen, chill, burn, fortify, novaHit;
        private readonly Material[] teamMaterials;
        private readonly Material fallenMaterial;
        private AttributeSet impSet;
        private int summonCount, frames;

        public BattleLab(World world, ForgeAssets make, int seed)
        {
            this.world = world;
            this.make = make;
            random = new Random(seed);
            Current = this;

            Health = make.Attribute("Health");
            Mana = make.Attribute("Mana");
            Armor = make.Attribute("Armor");
            MoveSpeed = make.Attribute("Move Speed");
            ShotSpeed = make.Attribute("Shot Speed");

            var damage = make.NewTag("Battle.Damage");
            Physical = Tag.Generate(damage.Name + ".Physical");
            Magical = Tag.Generate(damage.Name + ".Magical");
            Stunned = make.NewTag("Battle.State.Stunned");
            Debuff = make.NewTag("Battle.Status.Debuff");

            teamMaterials = new[] { Lit(TeamColors[0]), Lit(TeamColors[1]) };
            fallenMaterial = Lit(new Color(0.18f, 0.18f, 0.2f));

            // ─── The effects ─────────────────────────────────────────────────────
            var cleave = Damage("Cleave", 26f, Physical);
            cleave.Cues.Add(new KnockbackCue { Strength = 7f });
            var bash = Damage("Shield Bash", 12f, Physical);
            bash.Cues.Add(new KnockbackCue { Strength = 4f });
            var stun = Status("Stunned", 1.4f, new Color(1f, 0.88f, 0.2f), Stunned);
            var arrow = Damage("Arrow", 15f, Physical);
            var dart = Damage("Poison Dart", 5f, Physical);
            var poison = Ticking("Poisoned", Health, -3f, seconds: 4f, ticks: 4, positive: false, new Color(0.62f, 0.28f, 0.9f));
            var fire = Damage("Fireball", 22f, Magical);
            burn = Ticking("Burning", Health, -3f, seconds: 3f, ticks: 3, positive: false, new Color(1f, 0.48f, 0.1f));
            var splash = Damage("Fireball Splash", 14f, Magical);
            novaHit = Damage("Frost Nova", 12f, Magical);
            chill = Lasting("Chilled", MoveSpeed, -1.8f, seconds: 3f, positive: false, new Color(0.45f, 0.85f, 1f));
            var mend = make.Effect("Mend", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = 40f });
            Regen = Ticking("Blessed", Health, 4f, seconds: 6f, ticks: 6, positive: true, new Color(0.38f, 0.92f, 0.42f));
            fortify = Lasting("Fortified", Armor, 30f, seconds: 6f, positive: true, new Color(0.92f, 0.92f, 0.98f));
            var holy = Damage("Holy Bolt", 9f, Magical);
            var bolt = Damage("Shadow Bolt", 11f, Magical);
            var bite = Damage("Bite", 7f, Physical);
            summoned = make.Effect("Summoned", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 9f, Target = null });
            Badges[summoned] = new Badge("Summoned", true, new Color(0.78f, 0.55f, 1f));
            manaRegen = make.Effect("Mana Regen", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = Mana, Magnitude = 4f, PeriodicTicks = true, Ticks = 1,
            });

            // ─── The shots: the framework's projectile process, drawn from a pool ──
            var arrowShot = Shot("Arrow", new Color(0.95f, 0.9f, 0.75f), 0.16f, arrow);
            var dartShot = Shot("Poison Dart", new Color(0.62f, 0.28f, 0.9f), 0.18f, dart, poison);
            var fireShot = Shot("Fireball", new Color(1f, 0.5f, 0.12f), 0.42f, fire, burn);
            fireShot.SplashRadius = 2.6f;
            fireShot.Splash = new List<GameplayEffect> { splash, burn };
            var holyShot = Shot("Holy Bolt", new Color(1f, 0.95f, 0.55f), 0.2f, holy);
            var boltShot = Shot("Shadow Bolt", new Color(0.55f, 0.2f, 0.75f), 0.24f, bolt);

            // ─── The abilities ───────────────────────────────────────────────────
            Cleave = Kit("Cleave", mana: 0f, cooldown: 1.4f, Hit(cleave));
            Bash = Kit("Shield Bash", mana: 0f, cooldown: 6.5f, Hit(bash, stun));
            Arrow = Kit("Shoot", mana: 0f, cooldown: 1.1f, make.Stage(new ShootTask { Shot = arrowShot }));
            Dart = Kit("Poison Dart", mana: 0f, cooldown: 5f, make.Stage(new ShootTask { Shot = dartShot }));
            Fireball = Kit("Fireball", mana: 24f, cooldown: 3.2f, make.Stage(new ShootTask { Shot = fireShot }));
            FrostNova = Kit("Frost Nova", mana: 30f, cooldown: 7f, make.Stage(new NovaTask { Radius = 3.4f }));
            Mend = Kit("Mend", mana: 18f, cooldown: 2.4f, Hit(mend));
            Bless = Kit("Blessing", mana: 28f, cooldown: 6f, Hit(Regen, fortify));
            Holy = Kit("Holy Bolt", mana: 0f, cooldown: 2f, make.Stage(new ShootTask { Shot = holyShot }));
            Summon = Kit("Summon Imps", mana: 40f, cooldown: 11f, make.Stage(new SummonTask { Count = 2 }));
            Bolt = Kit("Shadow Bolt", mana: 0f, cooldown: 1.4f, make.Stage(new ShootTask { Shot = boltShot }));
            Bite = Kit("Bite", mana: 0f, cooldown: 0.8f, Hit(bite));

            // ─── The field and the sides ─────────────────────────────────────────
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Battlefield";
            Object.Destroy(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(36f, 0.2f, 18f);
            ground.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.24f, 0.29f, 0.25f));
            owned.Add(ground);

            var roster = new[] { EClass.Knight, EClass.Knight, EClass.Archer, EClass.Archer, EClass.Mage, EClass.Mage, EClass.Cleric, EClass.Summoner };
            for (int team = 0; team < 2; team++)
            {
                float side = team == 0 ? -1f : 1f;
                for (int i = 0; i < roster.Length; i++)
                {
                    var role = roster[i];
                    // Knights in front, archers and mages behind them, the cleric and the summoner at the back
                    float depth = role switch { EClass.Knight => 10.5f, EClass.Archer or EClass.Mage => 12.5f, _ => 14.5f };
                    float lane = role switch
                    {
                        EClass.Cleric => -1.5f,
                        EClass.Summoner => 1.5f,
                        _ => (i % 2 == 0 ? -1f : 1f) * (role == EClass.Knight ? 1.6f : role == EClass.Archer ? 4f : 2.2f),
                    };
                    var at = new Vector3(side * depth + Jitter(0.4f), 0f, lane + Jitter(0.4f));
                    Enlist(role, team, at, $"{TeamNames[team]} {role} {i % 2 + 1}");
                }
            }

            var view = new GameObject("Battle View").AddComponent<BattleView>();
            view.Lab = this;
            owned.Add(view.gameObject);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Building
        // ═══════════════════════════════════════════════════════════════════════════

        private float Jitter(float most) => (float)(random.NextDouble() * 2.0 - 1.0) * most;

        private static Material Lit(Color color)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            return new Material(shader) { color = color };
        }

        private GameplayEffect Damage(string name, float amount, Tag kind)
        {
            var hit = make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -amount });
            hit.ImpactSpecification.ImpactTypes.Add(kind);
            return hit;
        }

        // A debuff that gives a state for a while, refreshed if it lands again; a debuff, so it shows as one
        private GameplayEffect Status(string name, float seconds, Color color, Tag gives)
        {
            var status = make.Effect(name, new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = seconds, Target = null, GrantedTags = new List<Tag> { gives },
                Reapply = EEffectReApplicationPolicy.ReplaceOldContainer, Interaction = EEffectInteractionPolicy.RefreshContainerDuration,
            });
            status.Tags.ContextTags = new List<Tag> { Tag.Generate(Debuff.Name + "." + name) };
            Badges[status] = new Badge(name, false, color);
            return status;
        }

        // A change in ticks over a while: a burn or a poison, which stack, or a blessing's regeneration, which refreshes
        private GameplayEffect Ticking(string name, ForgeAttribute attribute, float each, float seconds, int ticks, bool positive, Color color)
        {
            var effect = make.Effect(name, new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = seconds, Target = attribute, Magnitude = each, PeriodicTicks = true,
                Ticks = ticks,
                Reapply = positive ? EEffectReApplicationPolicy.ReplaceOldContainer : EEffectReApplicationPolicy.AppendNewContainer,
                Interaction = positive ? EEffectInteractionPolicy.RefreshContainerDuration : EEffectInteractionPolicy.DoNothing,
            });
            if (!positive) effect.Tags.ContextTags = new List<Tag> { Tag.Generate(Debuff.Name + "." + name) };
            Badges[effect] = new Badge(name, positive, color);
            return effect;
        }

        // A change for as long as it lasts, refreshed if it lands again: a chill on speed, or armor that fortifies
        private GameplayEffect Lasting(string name, ForgeAttribute attribute, float amount, float seconds, bool positive, Color color)
        {
            var effect = make.Effect(name, new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = seconds, Target = attribute, Magnitude = amount,
                Reapply = EEffectReApplicationPolicy.ReplaceOldContainer, Interaction = EEffectInteractionPolicy.RefreshContainerDuration,
            });
            if (!positive) effect.Tags.ContextTags = new List<Tag> { Tag.Generate(Debuff.Name + "." + name) };
            Badges[effect] = new Badge(name, positive, color);
            return effect;
        }

        // A shot's template: the framework's projectile process, with a ball and a trail to see it fly
        private BattleShot Shot(string name, Color color, float size, params GameplayEffect[] effects)
        {
            var shot = world.Template<BattleShot>(name);
            shot.Effects = new List<GameplayEffect>(effects);
            shot.AttributePairs = new List<TagAttributePair> { new(Tags.PROJECTILE_SPEED, ShotSpeed) };
            shot.Instantiator = pool;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.name = "Ball";
            ball.transform.SetParent(shot.transform, false);
            ball.transform.localScale = Vector3.one * size;
            var lit = Lit(color);
            lit.EnableKeyword("_EMISSION");
            lit.SetColor("_EmissionColor", color * 0.8f);
            ball.GetComponent<Renderer>().sharedMaterial = lit;

            var trail = shot.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.startWidth = size * 0.7f;
            trail.endWidth = 0f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            return shot;
        }

        // An ability with its mana cost and its cooldown, which a stun keeps from being cast
        private Ability Kit(string name, float mana, float cooldown, AbilityTaskBehaviourStage stage)
        {
            var ability = make.Ability(name, stage);
            ability.Tags.TagRequirements.SourceRequirements.AvoidTags.Add(new TagQuery { Tag = Stunned });
            stage.ApplyUsageEffects = true;
            if (mana > 0f)
                ability.Cost = make.Effect($"{name} Cost", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Mana, Magnitude = -mana });
            ability.Cooldown = make.Effect($"{name} Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = cooldown, Target = null });
            return ability;
        }

        private AbilityTaskBehaviourStage Hit(params GameplayEffect[] effects) =>
            make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect>(effects) });

        // A fighter's numbers: health, mana, armor, how fast it moves, and how fast its shots fly
        private AttributeSet SetFor(EClass role)
        {
            var (health, mana, armor, speed) = role switch
            {
                EClass.Knight => (300f, 0f, 45f, 3.3f),
                EClass.Archer => (165f, 0f, 10f, 3.6f),
                EClass.Mage => (150f, 130f, 5f, 3.4f),
                EClass.Cleric => (175f, 160f, 15f, 3.4f),
                EClass.Summoner => (165f, 130f, 10f, 3.4f),
                _ => (55f, 0f, 5f, 4.2f),
            };
            var set = make.Set(
                make.Element(Health, health, health, EAttributeOverflowPolicy.ZeroToBase),
                make.Element(Mana, mana, mana, EAttributeOverflowPolicy.ZeroToBase),
                make.Element(Armor, armor, armor),
                make.Element(MoveSpeed, speed, speed),
                make.Element(ShotSpeed, 16f, 16f));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = Health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            return set;
        }

        // A fighter on a side: an entity in the scene, with its body, its kit and its mind
        private BattleFighter Enlist(EClass role, int team, Vector3 at, string name)
        {
            // Unobserved: the invariants match what an observed entity deals with what its targets receive, and a battle's
            // worth of listeners would cost more than the lab
            var set = role == EClass.Imp ? impSet ??= SetFor(EClass.Imp) : SetFor(role);
            var gas = world.MonoEntity(set, name, observed: false);
            gas.transform.position = at;
            gas.transform.rotation = Quaternion.LookRotation(team == 0 ? Vector3.right : Vector3.left);
            gas.GetAttributeSystem().ProvideWorker(new ResistWorker
            {
                TimingConfig = AbstractAttributeWorker.EChangeEventTiming.PreChange,
                TargetAttribute = Health,
                AllowSelfModification = true,
                TargetModification = EAttributeTargetExpanded.Current,
                SignPolicy = ESignPolicyExtended.Negative,
                AnyImpactType = false,
                ImpactTypes = new List<Tag> { Physical, Magical },
                Resist = Armor,
            });

            var fighter = gas.gameObject.AddComponent<BattleFighter>();
            fighter.Begin(this, gas, role, team, Body(gas.transform, role, team));
            Fighters.Add(fighter);
            byGas[gas] = fighter;

            var kit = role switch
            {
                EClass.Knight => new[] { Bash, Cleave },
                EClass.Archer => new[] { Dart, Arrow },
                EClass.Mage => new[] { FrostNova, Fireball },
                EClass.Cleric => new[] { Mend, Bless, Holy },
                EClass.Summoner => new[] { Summon, Bolt },
                _ => new[] { Bite },
            };
            foreach (var ability in kit) gas.GetAbilitySystem().GiveAbility(ability, 1, out _);
            if (role is EClass.Mage or EClass.Cleric or EClass.Summoner) world.Apply(gas, gas, manaRegen);
            gas.GetCallbacks().OnEntityDied += Died;
            return fighter;
        }

        // A body to see: a capsule in its side's color, and a mark on top for its class
        private Transform Body(Transform host, EClass role, int team)
        {
            float size = role switch { EClass.Knight => 1.12f, EClass.Imp => 0.55f, _ => 0.92f };
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(capsule.GetComponent<Collider>());
            capsule.name = "Body";
            capsule.transform.SetParent(host, false);
            capsule.transform.localPosition = new Vector3(0f, size, 0f);
            capsule.transform.localScale = new Vector3(size * 0.8f, size, size * 0.8f);
            capsule.GetComponent<Renderer>().sharedMaterial = teamMaterials[team];

            if (role == EClass.Imp) return capsule.transform;
            var (shape, color) = role switch
            {
                EClass.Knight => (PrimitiveType.Cube, new Color(0.7f, 0.72f, 0.78f)),
                EClass.Archer => (PrimitiveType.Cylinder, new Color(0.55f, 0.38f, 0.2f)),
                EClass.Mage => (PrimitiveType.Sphere, new Color(1f, 0.5f, 0.12f)),
                EClass.Cleric => (PrimitiveType.Sphere, new Color(0.98f, 0.98f, 0.9f)),
                _ => (PrimitiveType.Sphere, new Color(0.55f, 0.2f, 0.75f)),
            };
            var mark = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(mark.GetComponent<Collider>());
            mark.name = "Mark";
            mark.transform.SetParent(capsule.transform, false);
            mark.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            mark.transform.localScale = shape == PrimitiveType.Cylinder ? new Vector3(0.35f, 0.18f, 0.35f) : Vector3.one * 0.42f;
            mark.GetComponent<Renderer>().sharedMaterial = Lit(color);
            return capsule.transform;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Playing
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A frame of the battle: each fighter standing thinks, moves and casts. The sides take turns to go first, frame by
        /// frame: Blue, enlisted first, struck first every frame, which gave it the edge.
        /// </summary>
        public void Step(float deltaTime)
        {
            if (Over) return;
            Elapsed += deltaTime;
            bool backward = (frames++ & 1) == 1;
            int count = Fighters.Count;
            for (int n = 0; n < count; n++)
            {
                var fighter = Fighters[backward ? count - 1 - n : n];
                if (fighter != null && fighter.Alive) fighter.Think(deltaTime);
            }
        }

        /// <summary>How many fighters stand on a side.</summary>
        public int Standing(int team)
        {
            int standing = 0;
            foreach (var fighter in Fighters)
                if (fighter != null && fighter.Alive && fighter.Team == team) standing++;
            return standing;
        }

        internal BattleFighter FighterOf(ITarget target) =>
            target is IGameplayAbilitySystem gas && byGas.TryGetValue(gas, out var fighter) && fighter != null && fighter.Alive ? fighter : null;

        /// <summary>The nearest standing foe, or ally with <paramref name="foe"/> off, within reach.</summary>
        internal BattleFighter Nearest(BattleFighter from, bool foe, float reach = float.MaxValue)
        {
            BattleFighter best = null;
            float closest = reach * reach;
            foreach (var other in Fighters)
            {
                if (other == null || !other.Alive || other == from || (other.Team != from.Team) != foe) continue;
                float distance = (other.Position - from.Position).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                best = other;
            }
            return best;
        }

        /// <summary>The ally within reach, the fighter itself among them, whose health is lowest by its share, below a share.</summary>
        internal BattleFighter Weakest(BattleFighter from, float reach, float below)
        {
            BattleFighter best = null;
            float lowest = below;
            foreach (var other in Fighters)
            {
                if (other == null || !other.Alive || other.Team != from.Team) continue;
                if ((other.Position - from.Position).sqrMagnitude > reach * reach) continue;
                float share = other.HealthShare;
                if (share >= lowest) continue;
                lowest = share;
                best = other;
            }
            return best;
        }

        internal int FoesNear(Vector3 at, int team, float radius)
        {
            int near = 0;
            foreach (var other in Fighters)
                if (other != null && other.Alive && other.Team != team && (other.Position - at).sqrMagnitude <= radius * radius) near++;
            return near;
        }

        /// <summary>A fighter casts an ability at a target, or at itself, as a request: refused while it's on cooldown, short of mana or stunned.</summary>
        internal bool Cast(BattleFighter caster, Ability ability, BattleFighter target)
        {
            var abilities = caster.Gas.GetAbilitySystem();
            if (!abilities.TryGetAbilityContainer(ability, out var container)) return false;
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)(target != null ? target.Gas : caster.Gas));
            if (!abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, request, EAbilityActivationPolicyExtended.ActivateIfIdle)))
                return false;

            if (ability == Bash) Stuns++;
            else if (ability == Mend)
            {
                Heals++;
                if (target != null) BattleFlash.Ring(target.Position, 1.1f, new Color(0.4f, 1f, 0.45f), 0.45f);
            }
            else if (ability == Bless)
            {
                Blessings++;
                if (target != null) BattleFlash.Ring(target.Position, 1.4f, new Color(0.9f, 1f, 0.6f), 0.6f);
            }
            return true;
        }

        /// <summary>Fires a shot from a fighter at a target, through the pool. False when ProcessControl refuses it.</summary>
        internal bool Fire(IGameplayAbilitySystem caster, ITarget target, BattleShot template)
        {
            if (!byGas.TryGetValue(caster, out var shooter) || shooter == null || FighterOf(target) == null) return false;
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, target);
            data.SetPrimary(Tags.DERIVATION, IEffectOrigin.GenerateSourceDerivation(caster));
            pool.Muzzle = shooter.Position + Vector3.up * 1.3f + shooter.transform.forward * 0.5f;
            var registration = ProcessControl.Register(template).HandledBy(caster).With(data).Run();
            if (!registration) return false;
            world.Own(registration.Relay);
            Shots++;
            return true;
        }

        /// <summary>A fireball bursts where it lands: its splash hits each foe of its thrower within the radius but the one it struck.</summary>
        internal void Burst(BattleShot shot, ITarget struck, Vector3 at)
        {
            if (shot.ProcessRelay?.Handler is not IGameplayAbilitySystem thrower || !byGas.TryGetValue(thrower, out var from) || from == null) return;
            BattleFlash.Ring(at, shot.SplashRadius, new Color(1f, 0.55f, 0.15f), 0.4f);
            foreach (var other in Fighters.ToArray())
            {
                if (other == null || !other.Alive || other.Team == from.Team || ReferenceEquals(other.Gas, struck)) continue;
                if ((other.Position - at).sqrMagnitude > shot.SplashRadius * shot.SplashRadius) continue;
                foreach (var effect in shot.Splash) world.Apply(thrower, other.Gas, effect);
                AreaHits++;
            }
        }

        /// <summary>A frost nova from a fighter: each foe within the radius is struck and chilled.</summary>
        internal void NovaAround(IGameplayAbilitySystem caster, float radius)
        {
            if (!byGas.TryGetValue(caster, out var from) || from == null) return;
            BattleFlash.Ring(from.Position, radius, new Color(0.5f, 0.9f, 1f), 0.45f);
            foreach (var other in Fighters.ToArray())
            {
                if (other == null || !other.Alive || other.Team == from.Team) continue;
                if ((other.Position - from.Position).sqrMagnitude > radius * radius) continue;
                world.Apply(caster, other.Gas, novaHit);
                world.Apply(caster, other.Gas, chill);
                AreaHits++;
            }
        }

        /// <summary>A knockback, which a cue on a blow asks for: the struck fighter slides away from the one who struck it.</summary>
        internal void Knock(IGameplayAbilitySystem struck, IGameplayAbilitySystem by, float strength)
        {
            var target = FighterOf(struck);
            if (target == null) return;
            var away = by != null && byGas.TryGetValue(by, out var from) && from != null ? target.Position - from.Position : target.transform.forward * -1f;
            away.y = 0f;
            target.Push(away.sqrMagnitude > 0.001f ? away.normalized * strength : Vector3.zero);
            Knockbacks++;
        }

        /// <summary>A summoner calls imps beside itself, each a fighter on its side for its lifetime.</summary>
        internal void Call(IGameplayAbilitySystem caster, int count)
        {
            if (!byGas.TryGetValue(caster, out var summoner) || summoner == null) return;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                var at = summoner.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.4f;
                var imp = Enlist(EClass.Imp, summoner.Team, at, $"{TeamNames[summoner.Team]} Imp {++summonCount}");
                var gas = imp.Gas;
                gas.GetCallbacks().OnEffectRemoved += effect =>
                {
                    if (effect == summoned && imp != null && imp.Alive) gas.MarkDead();
                };
                world.Apply(caster, gas, summoned);
                BattleFlash.Ring(at, 0.9f, new Color(0.78f, 0.55f, 1f), 0.5f);
                Summons++;
            }
        }

        // A fighter falls: it leaves a mark where it fell, as its entity ends
        private void Died(IGameplayAbilitySystem gas)
        {
            if (!byGas.TryGetValue(gas, out var fighter) || fighter == null || !fighter.Alive) return;
            fighter.Fall();
            Deaths++;
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(mark.GetComponent<Collider>());
            mark.name = "Fallen";
            mark.transform.position = fighter.Position + Vector3.up * 0.02f;
            mark.transform.localScale = new Vector3(0.9f, 0.02f, 0.9f);
            mark.GetComponent<Renderer>().sharedMaterial = fallenMaterial;
            owned.Add(mark);
            BattleFlash.Ring(fighter.Position, 1.2f, new Color(0.85f, 0.85f, 0.85f), 0.5f);
        }

        public void Dispose()
        {
            if (Current == this) Current = null;
            foreach (var thing in owned) if (thing) Object.Destroy(thing);
            owned.Clear();
            pool.DestroyAll();
            BattleFlash.ClearAll();
        }
    }

    /// <summary>
    /// A fighter's mind and body: it takes the nearest foe, closes to its range, casts what's ready, slides when it's
    /// knocked back, and stands still while it's stunned.
    /// </summary>
    public sealed class BattleFighter : MonoBehaviour
    {
        public BattleLab.EClass Role { get; private set; }
        public int Team { get; private set; }
        public bool Alive { get; private set; } = true;
        public IGameplayAbilitySystem Gas { get; private set; }
        public Transform Body { get; private set; }

        private BattleLab lab;
        private BattleFighter target;
        private Vector3 push;

        public Vector3 Position => transform.position;

        /// <summary>The fighter's health, as its share of its most.</summary>
        public float HealthShare => Gas.TryGetAttributeValue(lab.Health, out var health) && health.BaseValue > 0f ? health.CurrentValue / health.BaseValue : 0f;

        public bool Stunned => Gas.GetTagCache().HasTagExact(lab.Stunned);

        /// <summary>How near a fighter of its class closes before it casts.</summary>
        public float Range => Role switch
        {
            BattleLab.EClass.Knight => 1.8f,
            BattleLab.EClass.Imp => 1.3f,
            BattleLab.EClass.Archer => 10f,
            BattleLab.EClass.Mage => 8.5f,
            BattleLab.EClass.Cleric => 8f,
            _ => 9f,
        };

        internal void Begin(BattleLab battle, IGameplayAbilitySystem gas, BattleLab.EClass role, int team, Transform body)
        {
            lab = battle;
            Gas = gas;
            Role = role;
            Team = team;
            Body = body;
        }

        internal void Push(Vector3 velocity) => push = velocity;

        internal void Fall() => Alive = false;

        /// <summary>A frame of the fighter: a knockback slides it, a stun holds it, and otherwise it closes and casts.</summary>
        internal void Think(float deltaTime)
        {
            if (push.sqrMagnitude > 0.0001f)
            {
                transform.position += push * deltaTime;
                push = Vector3.MoveTowards(push, Vector3.zero, 22f * deltaTime);
            }

            bool stunned = Stunned;
            Body.localRotation = stunned ? Quaternion.Euler(0f, 0f, 14f * Mathf.Sin(lab.Elapsed * 18f)) : Quaternion.identity;
            if (stunned) return;

            if (target == null || !target.Alive || (target.Position - Position).sqrMagnitude > 16f * 16f) target = lab.Nearest(this, foe: true);
            if (target == null) return;

            var toward = target.Position - Position;
            toward.y = 0f;
            float distance = toward.magnitude;
            if (distance > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toward), 10f * deltaTime);

            if (Act(distance)) return;
            if (distance <= Range * 0.92f) return;

            // Closes on its foe, kept apart from its allies, slowed by a chill
            float speed = Gas.TryGetAttributeValue(lab.MoveSpeed, out var moving) ? Mathf.Max(0.8f, moving.CurrentValue) : 3f;
            var step = toward / Mathf.Max(distance, 0.01f);
            var ally = lab.Nearest(this, foe: false, reach: 1.1f);
            if (ally != null)
            {
                var apart = Position - ally.Position;
                apart.y = 0f;
                if (apart.sqrMagnitude > 0.0001f) step += apart.normalized * 0.6f;
            }
            transform.position += step.normalized * (speed * deltaTime);
        }

        // What it casts, by its class, and whether it cast anything
        private bool Act(float distance)
        {
            switch (Role)
            {
                case BattleLab.EClass.Knight:
                    return distance <= 2f && (lab.Cast(this, lab.Bash, target) || lab.Cast(this, lab.Cleave, target));
                case BattleLab.EClass.Archer:
                    return distance <= 10.5f && (lab.Cast(this, lab.Dart, target) || lab.Cast(this, lab.Arrow, target));
                case BattleLab.EClass.Mage:
                    if (lab.FoesNear(Position, Team, 3.2f) >= 2 && lab.Cast(this, lab.FrostNova, null)) return true;
                    return distance <= 9f && lab.Cast(this, lab.Fireball, target);
                case BattleLab.EClass.Cleric:
                    var hurt = lab.Weakest(this, reach: 9f, below: 0.7f);
                    if (hurt != null && lab.Cast(this, lab.Mend, hurt)) return true;
                    var tired = lab.Weakest(this, reach: 9f, below: 0.92f);
                    if (tired != null && !tired.Gas.TryGetEffectContainer(lab.Regen, out _) && lab.Cast(this, lab.Bless, tired)) return true;
                    return distance <= 8.5f && lab.Cast(this, lab.Holy, target);
                case BattleLab.EClass.Summoner:
                    if (distance <= 13f && lab.Cast(this, lab.Summon, null)) return true;
                    return distance <= 9.5f && lab.Cast(this, lab.Bolt, target);
                default:
                    return distance <= 1.4f && lab.Cast(this, lab.Bite, target);
            }
        }
    }

    /// <summary>A shot: the framework's projectile process, which flies at its target and lands its effects, and a fireball's burst.</summary>
    public sealed class BattleShot : ProjectileMonoProcess
    {
        [Tooltip("How far a burst reaches from where the shot lands. 0 doesn't burst")]
        [Min(0f)] public float SplashRadius;

        [Tooltip("What the burst does to each foe it reaches")]
        public List<GameplayEffect> Splash = new();

        // A target that falls on the way is no target: the shot fizzles, or bursts where it was
        protected override async UniTask RunTargetedProcess(CancellationToken token)
        {
            var lab = BattleLab.Current;
            var last = transform.position;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var struck = lab?.FighterOf(target);
                if (struck != null) last = struck.Position + Vector3.up * 1.1f;
                var step = GetProjectileSpeed() * ForgeTime.DeltaTimeFor(ProcessRelay?.Handler);
                transform.position = Vector3.MoveTowards(transform.position, last, step);
                if ((transform.position - last).sqrMagnitude <= 0.04f) break;
                await ForgeTick.NextFrame(token);
            }

            if (SplashRadius > 0f) lab?.Burst(this, target, transform.position);
            // Its effects land on its target only if it still stands
            if (lab?.FighterOf(target) == null) throw new OperationCanceledException();
        }
    }

    /// <summary>A pool of shots, one stack of idle shots for each template, each placed at the muzzle.</summary>
    public sealed class ShotPool : AbstractMonoProcessInstantiator
    {
        private readonly Dictionary<AbstractMonoProcess, Stack<AbstractMonoProcess>> idle = new();
        private readonly Dictionary<AbstractMonoProcess, AbstractMonoProcess> templateOf = new();
        private readonly List<GameObject> all = new();

        public Vector3 Muzzle;

        protected override AbstractMonoProcess PrepareNew(AbstractMonoProcess template, ProcessDataPacket data)
        {
            AbstractMonoProcess shot;
            if (idle.TryGetValue(template, out var stack) && stack.Count > 0)
            {
                shot = stack.Pop();
                shot.transform.position = Muzzle;
                shot.gameObject.SetActive(true);
            }
            else
            {
                shot = Object.Instantiate(template, Muzzle, Quaternion.identity);
                templateOf[shot] = template;
                all.Add(shot.gameObject);
            }

            if (shot.TryGetComponent<TrailRenderer>(out var trail)) trail.Clear();
            return shot;
        }

        protected override AbstractMonoProcess PrepareExisting(AbstractMonoProcess process, ProcessDataPacket data) => process;

        public override void CleanProcess(AbstractMonoProcess process)
        {
            if (process == null) return;
            process.gameObject.SetActive(false);
            if (!templateOf.TryGetValue(process, out var template)) return;
            if (!idle.TryGetValue(template, out var stack)) idle[template] = stack = new Stack<AbstractMonoProcess>();
            stack.Push(process);
        }

        public void DestroyAll()
        {
            foreach (var shot in all)
            {
                if (!shot) continue;
                // A shot still flying ends first, so nothing of it runs on once its object goes
                var process = shot.GetComponent<AbstractMonoProcess>();
                if (process != null && process.ProcessRelay is { ProcessActive: true } relay) relay.TerminateImmediate();
                Object.Destroy(shot);
            }
            all.Clear();
            idle.Clear();
            templateOf.Clear();
        }
    }

    /// <summary>A knockback, as a cue on a blow: the struck fighter slides away from whoever struck it.</summary>
    [Serializable]
    public sealed class KnockbackCue : AbstractCueHandler
    {
        [Tooltip("How fast the struck fighter starts to slide, in units a second")]
        public float Strength = 6f;

        public override void OnApplied(CueContext cue) =>
            BattleLab.Current?.Knock(cue.Target, cue.Spec?.GetOrigin()?.GetSource() as IGameplayAbilitySystem, Strength);
    }

    /// <summary>Fires the ability's shot at its target.</summary>
    [Serializable]
    public sealed class ShootTask : AbstractAbilityTask
    {
        public BattleShot Shot;

        public override bool IsCriticalSection => false;

        public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
        {
            if (BattleLab.Current != null && data.TryGetFirstTarget(out var target) && data.EffectOrigin?.GetSource() is IGameplayAbilitySystem caster)
                BattleLab.Current.Fire(caster, target, Shot);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>A frost nova around the caster.</summary>
    [Serializable]
    public sealed class NovaTask : AbstractAbilityTask
    {
        public float Radius = 3f;

        public override bool IsCriticalSection => false;

        public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
        {
            if (BattleLab.Current != null && data.EffectOrigin?.GetSource() is IGameplayAbilitySystem caster) BattleLab.Current.NovaAround(caster, Radius);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Calls imps beside the caster.</summary>
    [Serializable]
    public sealed class SummonTask : AbstractAbilityTask
    {
        public int Count = 2;

        public override bool IsCriticalSection => false;

        public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
        {
            if (BattleLab.Current != null && data.EffectOrigin?.GetSource() is IGameplayAbilitySystem caster) BattleLab.Current.Call(caster, Count);
            return UniTask.CompletedTask;
        }
    }
}
