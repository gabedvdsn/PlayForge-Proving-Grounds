using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The turn to face (EP-36, the user's ask of 2026-11-17): a character turns to face its ability's target before the
    /// ability's first stage. An entity's Turns To Face turns it for each of its abilities with a target or a point; the
    /// first stage starts once it faces within the Start Angle, and the turn goes on to the Facing Angle, past the ability's
    /// end, until another order, its game's word or its death ends it. The cost is paid after the turn. An ability's own
    /// Turn To Face turns its own way, or not at all; a turn that takes too long waits, starts anyway or is cancelled; a
    /// turn taken over before it faces cancels the activation; a game's turner on the entity's object takes each step.
    /// </summary>
    public class TurnToFaceTests : ProvingGroundsTest
    {
        // The game's clock for a test: each frame a fixed step, with Unity's frame
        private sealed class GameClock : IForgeClock, IDisposable
        {
            private readonly IForgeClock previous;
            public float Step;

            public GameClock(float step)
            {
                Step = step;
                previous = ForgeTime.Use(this);
            }

            public void Dispose() => ForgeTime.Use(previous);

            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        // An ability's task that notes when its stage started, and how far its caster had left to turn
        [Serializable]
        private sealed class StageStart : AbstractAbilityTask
        {
            [NonSerialized] public Func<int> Now;
            [NonSerialized] public IGameplayAbilitySystem Caster;
            public int Runs, Frame = -1;
            public float Angle = -1f;

            public override bool IsCriticalSection => false;

            public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
            {
                Runs++;
                Frame = Now();
                Angle = Caster.AngleToFace;
                return UniTask.CompletedTask;
            }
        }

        // A turner of a game's own on the entity's object, which takes each step of a turn and turns nothing itself
        private sealed class Turner : MonoBehaviour, IForgeTurner
        {
            public int Steps;
            public Vector3 Direction;
            public float Speed;

            public void Face(Vector3 direction, float degreesPerSecond, float deltaTime)
            {
                Steps++;
                Direction = direction;
                Speed = degreesPerSecond;
            }
        }

        private ForgeAttribute mana;

        [SetUp]
        public void Pools() => mana = Make.Attribute("Mana");

        private static TurnToFaceSettings Settings(float speed, ETurnTooLong ifTooLong = ETurnTooLong.NoLimit, float maxWait = 1f,
            float startAngle = 30f) => new()
        {
            TurnSpeed = speed, StartAngle = startAngle, FacingAngle = 2f, IfTooLong = ifTooLong, MaxWait = maxWait,
        };

        // An entity on either hub, at a point, facing along Z, with mana to pay and the components it's given
        private IGameplayAbilitySystem Entity(Hub hub, string name, Vector3 at, params AbstractEntityComponent[] components)
        {
            var set = Make.Set(Make.Element(mana, 100f, 100f));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Components = components.ToList();
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Components = components.ToList();
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = at;
            gas.GetTargetingPacket().rotation = Quaternion.identity;
            return gas;
        }

        private StageStart Recorder(IGameplayAbilitySystem caster) => new() { Now = () => Frames.Frame, Caster = caster };

        private static int Give(IGameplayAbilitySystem caster, Ability ability)
        {
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(ability, 1, out int index), "the caster knows it");
            return index;
        }

        // Asks the caster to activate its ability, handed a target or nothing
        private static void Cast(IGameplayAbilitySystem caster, int index, ITarget target = null)
        {
            var request = ProcessDataPacket.Default();
            if (target != null) request.SetPrimary(Tags.TARGET_REAL, target);
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request)), "it activates");
        }

        // How far the caster faces from the target, flat, in degrees
        private static float Facing(IGameplayAbilitySystem caster, IGameplayAbilitySystem target)
        {
            var at = caster.GetTargetingPacket();
            var to = target.GetTargetingPacket().position - at.position;
            to.y = 0f;
            var forward = at.rotation * Vector3.forward;
            forward.y = 0f;
            return Vector3.Angle(forward, to);
        }

        private float Mana(IGameplayAbilitySystem gas)
        {
            gas.TryGetAttributeValue(mana, out var value);
            return value.CurrentValue;
        }

        /// <summary>
        /// An entity with Turns To Face turns to face its ability's target before the ability's first stage, which starts
        /// once it's within the Start Angle; the turn goes on to the Facing Angle after the ability has ended.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_36_An_ability_turns_its_caster_to_face_its_target_before_its_first_stage([Values] Hub hub) => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            // Its first stage starts within 90 degrees, so the turn has a way to go once the ability is over
            var caster = Entity(hub, "Caster", Vector3.zero, new TurnsToFaceEntityComponent { Turn = Settings(90f, startAngle: 90f) });
            var target = Entity(hub, "Target", new Vector3(0f, 0f, -5f));
            var start = Recorder(caster);
            int index = Give(caster, Make.Ability("Strike", Make.Stage(start)));
            await Frames.Step();

            int from = Frames.Frame;
            Cast(caster, index, target);
            await Frames.Until(() => start.Runs > 0, 40, "the first stage starts");
            Assert.That(start.Angle, Is.InRange(2f, 90f), "once its caster faces within the Start Angle, still turning");
            Assert.That(start.Frame - from, Is.InRange(9, 13), "after turning 90 degrees at 9 a frame");

            await Frames.Until(() => !caster.GetAbilitySystem().IsExecuting, 10, "the ability is over");
            Assert.Greater(caster.AngleToFace, 2f, "while its caster is still turning");
            await Frames.Until(() => caster.AngleToFace == 0f, 20, "the turn ends");
            Assert.That(Facing(caster, target), Is.LessThanOrEqualTo(2f), "within the Facing Angle");
        });

        /// <summary>The cost is paid after the turn, and not at all where the turn is interrupted, which ends it too.</summary>
        [UnityTest]
        public IEnumerator EP_36_The_cost_is_paid_after_the_turn([Values] Hub hub) => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            var caster = Entity(hub, "Caster", Vector3.zero, new TurnsToFaceEntityComponent { Turn = Settings(90f) });
            var target = Entity(hub, "Target", new Vector3(0f, 0f, -5f));
            var start = Recorder(caster);
            var strike = Make.Ability("Strike", Make.Stage(start));
            strike.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = 10f });
            int index = Give(caster, strike);
            await Frames.Step();

            Cast(caster, index, target);
            await Frames.Step(5);
            Assert.AreEqual(100f, Mana(caster), 1e-4f, "nothing paid while it turns");
            await Frames.Until(() => start.Runs > 0, 40, "the first stage starts");
            Assert.AreEqual(90f, Mana(caster), 1e-4f, "paid as the stage starts");
            await Frames.Until(() => caster.AngleToFace == 0f, 10, "the turn ends");
            await Frames.Until(() => !caster.GetAbilitySystem().IsExecuting, 10, "the ability ends");

            // Behind it again, and interrupted part way round
            target.GetTargetingPacket().position = new Vector3(0f, 0f, 5f);
            Cast(caster, index, target);
            await Frames.Step(5);
            caster.GetAbilitySystem().Inject(index, InterruptSequenceInjection.Instance);
            await Frames.Step(3);
            Assert.AreEqual(1, start.Runs, "the interrupted activation's stage never starts");
            Assert.AreEqual(90f, Mana(caster), 1e-4f, "and it pays nothing");
            Assert.AreEqual(0f, caster.AngleToFace, "its turn ends with it");
        });

        /// <summary>An ability's own Turn To Face turns its own way, or, with Turns off, not at all.</summary>
        [UnityTest]
        public IEnumerator EP_36_An_abilitys_own_Turn_To_Face_wins([Values] Hub hub) => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            var caster = Entity(hub, "Caster", Vector3.zero, new TurnsToFaceEntityComponent { Turn = Settings(90f) });
            var target = Entity(hub, "Target", new Vector3(0f, 0f, -5f));

            var still = Recorder(caster);
            var backward = Make.Ability("Backward", Make.Stage(still));
            backward.Components.Add(new TurnToFaceAbilityComponent { Turns = false });
            int stillIndex = Give(caster, backward);
            var quick = Recorder(caster);
            var spin = Make.Ability("Spin", Make.Stage(quick));
            spin.Components.Add(new TurnToFaceAbilityComponent { Turn = Settings(900f) });
            int quickIndex = Give(caster, spin);
            await Frames.Step();

            int from = Frames.Frame;
            Cast(caster, stillIndex, target);
            await Frames.Until(() => still.Runs > 0, 5, "with Turns off, its stage starts at once");
            Assert.AreEqual(0f, caster.AngleToFace, "and nothing turns");
            Assert.That(Facing(caster, target), Is.EqualTo(180f).Within(0.5f), "its caster facing away still");

            await Frames.Until(() => !caster.GetAbilitySystem().IsExecuting, 5, "it ends");
            from = Frames.Frame;
            Cast(caster, quickIndex, target);
            await Frames.Until(() => quick.Runs > 0, 10, "its own turn's stage starts");
            Assert.That(quick.Frame - from, Is.LessThanOrEqualTo(4), "at its own 90 degrees a frame, not the entity's 9");
            Assert.That(Facing(caster, target), Is.LessThanOrEqualTo(30f), "its caster turned to face its target");
        });

        /// <summary>
        /// A turn that takes longer than its Max Wait to reach the Start Angle waits as long as it takes, by default; starts
        /// the stage anyway, the turn going on; or cancels the activation, its cost unpaid and its turn ended.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_36_A_turn_that_takes_too_long([Values] ETurnTooLong ifTooLong) => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            // A tenth of a degree a frame: it reaches no Start Angle in this test
            var caster = Entity(Hub.Runtime, "Caster", Vector3.zero,
                new TurnsToFaceEntityComponent { Turn = Settings(1f, ifTooLong, maxWait: 0.5f) });
            var target = Entity(Hub.Runtime, "Target", new Vector3(0f, 0f, -5f));
            var start = Recorder(caster);
            var strike = Make.Ability("Strike", Make.Stage(start));
            strike.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = 10f });
            int index = Give(caster, strike);
            await Frames.Step();

            Cast(caster, index, target);
            await Frames.Step(10);
            switch (ifTooLong)
            {
                case ETurnTooLong.NoLimit:
                    Assert.AreEqual(0, start.Runs, "it waits as long as it takes");
                    Assert.IsTrue(caster.GetAbilitySystem().IsExecuting, "the activation still winding up");
                    caster.GetAbilitySystem().Inject(index, InterruptSequenceInjection.Instance);
                    await Frames.Until(() => !caster.GetAbilitySystem().IsExecuting, 5, "until it's interrupted");
                    break;
                case ETurnTooLong.StartAnyway:
                    Assert.AreEqual(1, start.Runs, "the stage starts anyway after half a second");
                    Assert.Greater(start.Angle, 30f, "short of the Start Angle");
                    Assert.Greater(caster.AngleToFace, 0f, "the turn going on");
                    Assert.AreEqual(90f, Mana(caster), 1e-4f, "its cost paid");
                    break;
                case ETurnTooLong.Cancel:
                    Assert.AreEqual(0, start.Runs, "the activation is cancelled after half a second");
                    Assert.IsFalse(caster.GetAbilitySystem().IsExecuting, "and over");
                    Assert.AreEqual(100f, Mana(caster), 1e-4f, "its cost unpaid");
                    Assert.AreEqual(0f, caster.AngleToFace, "and its turn ended");
                    break;
            }
        });

        /// <summary>A new order replaces a turn, and the game's word ends it where it is.</summary>
        [UnityTest]
        public IEnumerator EP_36_Another_order_or_the_games_word_ends_a_turn([Values] Hub hub) => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            var caster = Entity(hub, "Caster", Vector3.zero);
            caster.Face(new Vector3(5f, 0f, 0f), 90f, 2f);
            Assert.AreEqual(90f, caster.AngleToFace, 0.5f, "a quarter turn to its right to make");
            await Frames.Step(3);
            Assert.That(caster.AngleToFace, Is.LessThan(90f), "turning");

            caster.Face(new Vector3(0f, 0f, -5f), 90f, 2f);
            Assert.Greater(caster.AngleToFace, 90f, "a new order turns it toward what's behind it instead");
            await Frames.Step(2);
            var rotation = caster.GetTargetingPacket().rotation;
            caster.StopTurning();
            Assert.AreEqual(0f, caster.AngleToFace, "the game's word ends the turn");
            await Frames.Step(3);
            Assert.That(Quaternion.Angle(rotation, caster.GetTargetingPacket().rotation), Is.LessThan(0.01f), "and it turns no more");
        });

        /// <summary>
        /// A turn taken over while its ability waits on it, by the game's word or another order, cancels the activation, its
        /// cost unpaid: another action takes precedence. A new order turns on.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_36_A_turn_taken_over_cancels_its_activation([Values] Hub hub, [Values] bool byOrder) => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            var caster = Entity(hub, "Caster", Vector3.zero, new TurnsToFaceEntityComponent { Turn = Settings(90f) });
            var target = Entity(hub, "Target", new Vector3(0f, 0f, -5f));
            var start = Recorder(caster);
            var strike = Make.Ability("Strike", Make.Stage(start));
            strike.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = 10f });
            int index = Give(caster, strike);
            await Frames.Step();

            Cast(caster, index, target);
            await Frames.Step(5);
            Assert.IsTrue(caster.GetAbilitySystem().IsExecuting, "winding up");
            if (byOrder) caster.Face(new Vector3(5f, 0f, 0f), 10f);
            else caster.StopTurning();
            await Frames.Until(() => !caster.GetAbilitySystem().IsExecuting, 5, "the activation gives way");
            Assert.AreEqual(0, start.Runs, "its stage never starts");
            Assert.AreEqual(100f, Mana(caster), 1e-4f, "and it pays nothing");
            if (byOrder) Assert.Greater(caster.AngleToFace, 2f, "the new order turning it still");
            else Assert.AreEqual(0f, caster.AngleToFace, "and it turns no more");
            caster.StopTurning();
        });

        /// <summary>A turner of the game's own on the entity's object takes each step of a turn, and PlayForge turns nothing.</summary>
        [UnityTest]
        public IEnumerator EP_36_A_games_turner_takes_the_turn() => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            var caster = (GameplayAbilitySystem)Entity(Hub.Mono, "Caster", Vector3.zero);
            var turner = caster.gameObject.AddComponent<Turner>();
            caster.Face(new Vector3(5f, 0f, 0f), 90f, 2f);
            await Frames.Step(3);
            Assert.Greater(turner.Steps, 0, "the turner takes the steps");
            Assert.AreEqual(90f, turner.Speed, "at the turn's speed");
            Assert.That(Vector3.Angle(turner.Direction, Vector3.right), Is.LessThan(0.01f), "toward what it faces");
            Assert.That(Quaternion.Angle(Quaternion.identity, caster.transform.rotation), Is.LessThan(0.01f), "PlayForge turning nothing itself");
            caster.StopTurning();
        });

        /// <summary>An ability with nothing to face, or with its caster for its target, turns nothing and starts at once.</summary>
        [UnityTest]
        public IEnumerator EP_36_Nothing_to_face_turns_nothing([Values] Hub hub) => Live(async () =>
        {
            using var game = new GameClock(0.1f);
            var caster = Entity(hub, "Caster", Vector3.zero, new TurnsToFaceEntityComponent { Turn = Settings(90f) });
            var start = Recorder(caster);
            int index = Give(caster, Make.Ability("Shout", Make.Stage(start)));
            await Frames.Step();

            Cast(caster, index);
            await Frames.Until(() => start.Runs == 1, 5, "with no target its stage starts at once");
            Assert.AreEqual(0f, caster.AngleToFace, "turning nothing");
            await Frames.Until(() => !caster.GetAbilitySystem().IsExecuting, 5, "it ends");

            Cast(caster, index, caster);
            await Frames.Until(() => start.Runs == 2, 5, "nor with its caster for its target");
            Assert.AreEqual(0f, caster.AngleToFace, "turning nothing");
        });
    }
}
