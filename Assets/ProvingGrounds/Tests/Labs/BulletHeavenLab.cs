using System.Collections.Generic;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Bullet heaven, Phase 5's fourth lab: a wizard and a horde. The wizard fires bolts by the thousand, the framework's
    /// projectile process drawn from a pool a game writes (Decision 23, PC-33); its aura burns every enemy within reach;
    /// and an enemy that dies blasts its neighbours, which can set off a cascade. Enemies are runtime entities on a grid;
    /// the lab plays on a clock of 1/64 s a frame. It stresses ProcessControl's churn, pooling and applying effects over an
    /// area, as the review's plan for it asks.
    /// </summary>
    public sealed class BulletHeavenLab
    {
        public const float SecondsPerFrame = 1f / 64f;
        public const float BoltDamage = 1f, BlastDamage = 30f, BlastRadius = 1.1f;

        public readonly ForgeAttribute Health, BoltSpeed;
        public readonly IGameplayAbilitySystem Wizard;
        public readonly List<IGameplayAbilitySystem> Enemies = new();

        /// <summary>How many times each enemy died: once each, or the cascade hit one twice.</summary>
        public readonly Dictionary<IGameplayAbilitySystem, int> Deaths = new();

        public readonly GameplayEffect Bolt, Burning, Blast;
        public readonly ProjectileMonoProcess BoltTemplate;
        public readonly BoltPool Pool = new();

        public int Fired { get; private set; }

        private readonly World world;
        private readonly ForgeAssets make;
        private readonly IEffectOrigin wizardOrigin;

        public BulletHeavenLab(World world, ForgeAssets make)
        {
            this.world = world;
            this.make = make;
            Health = make.Attribute("Health");
            BoltSpeed = make.Attribute("Bolt Speed");

            // Ten units a frame, so a bolt lands within a few frames. Unobserved, as its horde is: the invariants match what an
            // observed entity deals with what its targets receive, and 500 listeners would cost more than the lab
            Wizard = world.RuntimeEntity(make.Set(
                make.Element(Health, 1000f, 1000f),
                make.Element(BoltSpeed, 640f, 640f)), "Wizard", observed: false);
            wizardOrigin = IEffectOrigin.GenerateSourceDerivation(Wizard);

            Bolt = make.Effect("Bolt", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -BoltDamage });
            Blast = make.Effect("Blast", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -BlastDamage });
            // Each pulse refreshes the burn: Replace Existing alone leaves its time alone, as EFFECTS.md says
            Burning = make.Effect("Burning", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 1f, Target = null,
                Reapply = EEffectReApplicationPolicy.ReplaceOldContainer, Interaction = EEffectInteractionPolicy.RefreshContainerDuration,
            });

            BoltTemplate = world.Template<ProjectileMonoProcess>("Bolt");
            BoltTemplate.Effects = new List<GameplayEffect> { Bolt };
            BoltTemplate.AttributePairs = new List<TagAttributePair> { new(Tags.PROJECTILE_SPEED, BoltSpeed) };
            BoltTemplate.Instantiator = Pool;
        }

        /// <summary>
        /// A grid of enemies, starting at <paramref name="corner"/>, a unit apart. Each dies at 0 health, through the
        /// framework's death watcher, and blasts each living neighbour as it does.
        /// </summary>
        public List<IGameplayAbilitySystem> Horde(int columns, int rows, Vector3 corner, float health)
        {
            var set = make.Set(make.Element(Health, health, health, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = Health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });

            var made = new List<IGameplayAbilitySystem>(columns * rows);
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < columns; x++)
            {
                var enemy = world.RuntimeEntity(set, $"Enemy {Enemies.Count}", observed: false);
                enemy.GetTargetingPacket().position = corner + new Vector3(x, 0f, z);
                enemy.GetCallbacks().OnEntityDied += Died;
                Enemies.Add(enemy);
                made.Add(enemy);
            }
            return made;
        }

        // A death blasts each living neighbour within reach, which can set off a cascade
        private void Died(IGameplayAbilitySystem enemy)
        {
            Deaths[enemy] = Deaths.TryGetValue(enemy, out int before) ? before + 1 : 1;
            var at = enemy.GetTargetingPacket().position;
            foreach (var other in Enemies)
            {
                if (other == enemy || Deaths.ContainsKey(other)) continue;
                if ((other.GetTargetingPacket().position - at).sqrMagnitude <= BlastRadius * BlastRadius) world.Apply(enemy, other, Blast);
            }
        }

        /// <summary>
        /// Fires a bolt from the wizard at an enemy, through the pool. False when ProcessControl refuses it. An
        /// <paramref name="own"/> bolt the world ends if the test doesn't; a soak's millions aren't owned, as the world would
        /// keep every one, and the world ends whatever is left as it goes anyway.
        /// </summary>
        public bool Fire(IGameplayAbilitySystem at, bool own = true)
        {
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)at);
            data.SetPrimary(Tags.DERIVATION, wizardOrigin);
            var registration = ProcessControl.Register(BoltTemplate).HandledBy(Wizard).With(data).Run();
            if (!registration) return false;
            if (own) world.Own(registration.Relay);
            Fired++;
            return true;
        }

        /// <summary>The wizard's aura: every living enemy within the radius burns. How many it reached.</summary>
        public int Pulse(float radius)
        {
            int reached = 0;
            var at = Wizard.GetTargetingPacket().position;
            foreach (var enemy in Enemies)
            {
                if (Deaths.ContainsKey(enemy)) continue;
                if ((enemy.GetTargetingPacket().position - at).sqrMagnitude > radius * radius) continue;
                if (world.Apply(Wizard, enemy, Burning)) reached++;
            }
            return reached;
        }

        public float Value(IGameplayAbilitySystem entity) =>
            entity.TryGetAttributeValue(Health, out var value) ? value.CurrentValue : float.NaN;

        public bool Burns(IGameplayAbilitySystem enemy) => enemy.TryGetEffectContainer(Burning, out _);
    }

    /// <summary>
    /// A pool of bolts, as a game writes one (Decision 23): an idle bolt if it has one, a new clone if not, each placed at
    /// the muzzle, and each taken back as its process ends (PC-33).
    /// </summary>
    public sealed class BoltPool : AbstractMonoProcessInstantiator
    {
        private readonly Stack<AbstractMonoProcess> idle = new();
        private readonly List<GameObject> all = new();

        /// <summary>How many bolts the pool made in all, and how many are out now.</summary>
        public int Made, Out;

        public Vector3 Muzzle;

        public int Idle => idle.Count;

        protected override AbstractMonoProcess PrepareNew(AbstractMonoProcess template, ProcessDataPacket data)
        {
            AbstractMonoProcess bolt;
            if (idle.Count > 0)
            {
                bolt = idle.Pop();
                bolt.gameObject.SetActive(true);
            }
            else
            {
                bolt = Object.Instantiate(template);
                all.Add(bolt.gameObject);
                Made++;
            }
            bolt.transform.position = Muzzle;
            Out++;
            return bolt;
        }

        protected override AbstractMonoProcess PrepareExisting(AbstractMonoProcess process, ProcessDataPacket data) => process;

        public override void CleanProcess(AbstractMonoProcess process)
        {
            Out--;
            process.gameObject.SetActive(false);
            idle.Push(process);
        }

        /// <summary>Destroys every bolt the pool made, as the round ends.</summary>
        public void DestroyAll()
        {
            foreach (var bolt in all) if (bolt) Object.Destroy(bolt);
            all.Clear();
            idle.Clear();
        }
    }

    /// <summary>The lab's clock: 1/64 s of game time each frame, so a round is exact.</summary>
    public sealed class HeavenClock : IForgeClock
    {
        public float DeltaTime => BulletHeavenLab.SecondsPerFrame;
        public float UnscaledDeltaTime => BulletHeavenLab.SecondsPerFrame;
        public float FixedDeltaTime => BulletHeavenLab.SecondsPerFrame;
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public int FrameCount => UnityEngine.Time.frameCount;
    }
}
