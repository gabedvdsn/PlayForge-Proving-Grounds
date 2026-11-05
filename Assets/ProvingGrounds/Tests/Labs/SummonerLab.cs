using System.Collections.Generic;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Summoner lab, Phase 5's sixth: a summoner calls imps, each a runtime entity it handles, so each ends as the
    /// summoner does. An imp lasts its lifetime, an effect whose end ends the imp. The summoner's bond heals each imp a
    /// little each second, but only while the summoner stands: an ongoing requirement on the bond's source. It plays on a
    /// clock of half a second a frame.
    /// </summary>
    public sealed class SummonerLab
    {
        public const float SecondsPerFrame = 0.5f;
        public const float Lifetime = 8f, BondHeal = 5f;

        public readonly ForgeAttribute Health;
        public readonly IGameplayAbilitySystem Summoner;
        public readonly List<IGameplayAbilitySystem> Imps = new();

        /// <summary>What a standing summoner has, which the bond's ticks ask for; a kneeling one doesn't.</summary>
        public readonly Tag Standing;

        public readonly GameplayEffect Summoned, Bond, Stand;

        private readonly World world;
        private readonly ForgeAssets make;
        private readonly AttributeSet impSet;
        private readonly Dictionary<IGameplayAbilitySystem, ProcessRelay> relays = new();

        public SummonerLab(World world, ForgeAssets make, Hub hub)
        {
            this.world = world;
            this.make = make;
            Health = make.Attribute("Health");

            var masterSet = make.Set(make.Element(Health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            masterSet.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = Health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });
            // Unobserved, as its imps are: the invariants match what an observed entity deals with what its targets receive
            Summoner = world.Entity(hub, masterSet, "Summoner", observed: false);

            impSet = make.Set(make.Element(Health, 20f, 50f, EAttributeOverflowPolicy.ZeroToBase));

            // A standing summoner, which the bond asks for
            Standing = make.NewTag("Lab.State.Standing");
            Stand = make.Effect("Stand", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { Standing } });
            world.Apply(Summoner, Summoner, Stand);

            // An imp's lifetime: as it runs out, the imp ends
            Summoned = make.Effect("Summoned", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = Lifetime, Target = null });

            // The bond: 5 health a second to an imp, while its source, the summoner, stands
            Bond = make.Effect("Bond", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = Health, Magnitude = BondHeal, PeriodicTicks = true, Ticks = 1,
            });
            Bond.SourceRequirements.OngoingRequirements.RequireTags = new List<TagQuery> { new TagQuery { Tag = Standing } };
        }

        /// <summary>
        /// The summoner calls an imp: a runtime entity the summoner handles, so it ends as the summoner ends. Its lifetime
        /// and the bond land on it as it comes.
        /// </summary>
        public IGameplayAbilitySystem Summon()
        {
            var imp = new RuntimeGameplayAbilitySystem(make.RuntimeIdentity($"Imp {Imps.Count}", impSet));
            var registration = ProcessControl.Register(imp).HandledBy(Summoner).Run();
            if (!registration) return null;
            world.Own(registration.Relay);
            relays[imp] = registration.Relay;
            Imps.Add(imp);

            imp.GetCallbacks().OnEffectRemoved += effect =>
            {
                if (effect == Summoned) imp.MarkDead();
            };
            world.Apply(Summoner, imp, Summoned);
            world.Apply(Summoner, imp, Bond);
            return imp;
        }

        /// <summary>Whether an imp's process still runs.</summary>
        public bool Present(IGameplayAbilitySystem imp) =>
            relays.TryGetValue(imp, out var relay) && world.Control.IsRegistered(relay.CacheIndex);

        public void Kneel() => Summoner.RemoveGameplayEffect(Stand);

        public void Rise() => world.Apply(Summoner, Summoner, Stand);

        public float Value(IGameplayAbilitySystem entity) => entity.TryGetAttributeValue(Health, out var value) ? value.CurrentValue : float.NaN;
    }

    /// <summary>The lab's clock: half a second of game time each frame, so a round is exact.</summary>
    public sealed class SummonerClock : IForgeClock
    {
        public float DeltaTime => SummonerLab.SecondsPerFrame;
        public float UnscaledDeltaTime => SummonerLab.SecondsPerFrame;
        public float FixedDeltaTime => SummonerLab.SecondsPerFrame;
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public int FrameCount => UnityEngine.Time.frameCount;
    }
}
