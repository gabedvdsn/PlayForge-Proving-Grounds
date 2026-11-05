using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Duel lab, Phase 5's third: two fighters with frame data, hit-stop, an input buffer and cancel windows. A jab
    /// commits to its startup and active frames; on a hit, both fighters freeze for its hit-stop, and its recovery can
    /// be cut short by a special; on a miss, it's committed to a longer whiff. It plays on a clock of 1/64 s a frame, a
    /// frame exact in floats, and every wait runs by a fighter's own clock (FEAT-7, TS-6), so a hit-stop holds it.
    /// The moves branch on the hit (TS-7), and a press while a fighter is busy waits in its buffer (FEAT-6).
    /// </summary>
    public sealed class DuelLab
    {
        public const float SecondsPerFrame = 1f / 64f;

        // Frame data
        public const int JabStartup = 4, JabActive = 2, JabHitStop = 6, JabWhiff = 10, JabHitRecovery = 6;
        public const int SpecialStartup = 8, SpecialActive = 2, SpecialHitStop = 8, SpecialRecovery = 12;
        public const float JabDamage = 40f, SpecialDamage = 120f;

        public readonly ForgeAttribute Health;

        /// <summary>A normal, a special, and what every strike is, which a guard refuses.</summary>
        public readonly Tag Normal, Special, Strike;

        /// <summary>What a strike that landed leaves in its move's data, which its later stages branch on (TS-7).</summary>
        public readonly Tag Landed;

        public readonly Tag Guarding;

        public readonly IGameplayAbilitySystem Kestrel, Brand;

        public readonly Ability Jab, Fireball;

        /// <summary>A guard: while it's on, a strike doesn't land (FEAT-8's immunity).</summary>
        public readonly GameplayEffect Guard;

        public DuelLab(World world, ForgeAssets make, Hub hub)
        {
            Health = make.Attribute("Health");
            var kind = make.NewTag("Duel.Kind");
            Normal = Tag.Generate(kind.Name + ".Normal");
            Special = Tag.Generate(kind.Name + ".Special");
            Strike = Tag.Generate(kind.Name + ".Strike");
            Landed = make.NewTag("Duel.Data.Landed");
            Guarding = make.NewTag("Duel.State.Guarding");

            Kestrel = world.Entity(hub, make.Set(make.Element(Health, 1000f, 1000f, EAttributeOverflowPolicy.ZeroToBase)), "Kestrel");
            Brand = world.Entity(hub, make.Set(make.Element(Health, 1000f, 1000f, EAttributeOverflowPolicy.ZeroToBase)), "Brand");

            Guard = make.Effect("Guard", new EffectConfig { Target = null, Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { Guarding } });
            Guard.Tags.BlocksEffectsTagged = new List<Tag> { Strike };

            Jab = Move(make, "Jab", Normal, damage: JabDamage, startup: JabStartup, active: JabActive, hitStop: JabHitStop,
                whiff: JabWhiff, hitRecovery: JabHitRecovery, priority: 1);
            Fireball = Move(make, "Fireball", Special, damage: SpecialDamage, startup: SpecialStartup, active: SpecialActive,
                hitStop: SpecialHitStop, whiff: SpecialRecovery, hitRecovery: SpecialRecovery, priority: 2);

            foreach (var fighter in new[] { Kestrel, Brand })
            {
                fighter.GetAbilitySystem().GiveAbility(Jab, 1, out _);
                fighter.GetAbilitySystem().GiveAbility(Fireball, 1, out _);
            }
        }

        // A move: a critical startup, critical active frames that strike, a hit-stop and a hit recovery on a hit, or a
        // committed whiff on a miss. A special cuts a normal's hit recovery short, and the recovery keeps normals out
        // (FEAT-5). A press waits 12 frames in the buffer (FEAT-6)
        private Ability Move(ForgeAssets make, string name, Tag kindOf, float damage, int startup, int active, int hitStop, int whiff,
            int hitRecovery, int priority)
        {
            var hit = make.Effect($"{name} Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = Health, Magnitude = -damage });
            hit.Tags.ContextTags = new List<Tag> { Strike };

            var startupStage = make.Stage(new FramesTask { Frames = startup, Critical = true });
            var activeStage = make.Stage(
                new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { hit }, RecordLandedAs = Landed },
                new FramesTask { Frames = active, Critical = true });
            var hitStopStage = make.Stage(new HitStopTask { Frames = hitStop });
            hitStopStage.Condition = new AbilityStageCondition { On = EAbilityStageConditionSubject.Activation, Tag = Landed };

            // The whiff, committed, first: a skipped critical stage lets the claim go as the run passes it (TS-7)
            var whiffStage = make.Stage(new FramesTask { Frames = whiff, Critical = true });
            whiffStage.Condition = new AbilityStageCondition { On = EAbilityStageConditionSubject.Activation, Tag = Landed, WhenMissing = true };
            var recoveryStage = make.Stage(new FramesTask { Frames = hitRecovery });
            recoveryStage.Condition = new AbilityStageCondition { On = EAbilityStageConditionSubject.Activation, Tag = Landed };
            if (kindOf == Normal)
            {
                recoveryStage.CancelledBy = new List<Tag> { Special };
                recoveryStage.Blocks = new List<Tag> { Normal };
            }

            var move = make.Ability(name, startupStage, activeStage, hitStopStage, whiffStage, recoveryStage);
            move.Tags.ContextTags.Add(kindOf);
            move.Definition.BufferWindow = 12 * SecondsPerFrame;
            move.Definition.BufferPriority = priority;
            return move;
        }

        /// <summary>
        /// A fighter presses a move at the other. Under Queue, a press while the fighter is busy waits in its buffer; under
        /// Refuse, it's refused. False when it's refused.
        /// </summary>
        public bool Press(IGameplayAbilitySystem fighter, Ability move,
            EAbilityActivationPolicyExtended policy = EAbilityActivationPolicyExtended.QueueActivationIfBusy)
        {
            var abilities = fighter.GetAbilitySystem();
            if (!abilities.TryGetAbilityContainer(move, out var container)) return false;
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)Other(fighter));
            return abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, request, policy));
        }

        public IGameplayAbilitySystem Other(IGameplayAbilitySystem fighter) => fighter == Kestrel ? Brand : Kestrel;

        public float Value(IGameplayAbilitySystem fighter) => fighter.TryGetAttributeValue(Health, out var value) ? value.CurrentValue : float.NaN;

        public bool Doing(IGameplayAbilitySystem fighter, Ability move) =>
            fighter.GetAbilitySystem().TryGetAbilityContainer(move, out var container) && container.IsActive;

        public bool Busy(IGameplayAbilitySystem fighter) => fighter.GetAbilitySystem().IsExecuting;
    }

    /// <summary>Waits a number of frames of the fighter's own time, which a hit-stop holds. A critical one commits the move.</summary>
    [Serializable]
    public sealed class FramesTask : AbstractAbilityTask
    {
        public int Frames;
        public bool Critical;

        public override UniTask Activate(AbilityDataPacket data, CancellationToken token) =>
            SequenceTaskLibrary.Delay(Frames * DuelLab.SecondsPerFrame, token);

        public override bool IsCriticalSection => Critical;
    }

    /// <summary>
    /// A hit-stop: both fighters' time stops for a number of frames, as a heavy blow lands, whatever else they're doing.
    /// Each fighter's own clock is what stops (FEAT-7), so their moves, buffers and effects all wait.
    /// </summary>
    [Serializable]
    public sealed class HitStopTask : AbstractAbilityTask
    {
        public int Frames;

        public override async UniTask Activate(AbilityDataPacket data, CancellationToken token)
        {
            var attacker = data.EffectOrigin?.GetSource()?.ToGAS();
            data.TryGetFirstTarget(out var target);
            var defender = target?.ToGAS();
            Scale(attacker, defender, 0f);
            try
            {
                await SequenceTaskLibrary.WaitFrames(Frames, token);
            }
            finally
            {
                Scale(attacker, defender, 1f);
            }
        }

        private static void Scale(IGameplayAbilitySystem attacker, IGameplayAbilitySystem defender, float scale)
        {
            if (attacker != null) attacker.GetClock().TimeScale = scale;
            if (defender != null) defender.GetClock().TimeScale = scale;
        }

        public override bool IsCriticalSection => true;
    }

    /// <summary>The lab's clock: 1/64 s of game time each frame, exact in floats, so frame data counts exactly.</summary>
    public sealed class DuelClock : IForgeClock
    {
        public float DeltaTime => DuelLab.SecondsPerFrame;
        public float UnscaledDeltaTime => DuelLab.SecondsPerFrame;
        public float FixedDeltaTime => DuelLab.SecondsPerFrame;
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public int FrameCount => UnityEngine.Time.frameCount;
    }
}
