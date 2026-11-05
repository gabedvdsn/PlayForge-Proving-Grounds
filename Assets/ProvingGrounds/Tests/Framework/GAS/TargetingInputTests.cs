using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Click targeting through the game's input (FEAT-11): the five click-targeting tasks read the Input Manager's mouse
    /// and Camera.main themselves, so a game on the Input System, with a camera or a reticle of its own, or a test
    /// couldn't stand in, and a scene with no main camera threw at the click. They read ForgeInput.Current.
    /// </summary>
    public class TargetingInputTests : ProvingGroundsTest
    {
        // The game's input: a press, held until targeting reads it, and the ray it aims along
        private sealed class FakeInput : IForgeInput
        {
            public Ray? Aim;
            private int pressed = -1;

            public void Press(int button) => pressed = button;

            public bool PointerPressed(int button)
            {
                if (pressed != button) return false;
                pressed = -1;
                return true;
            }

            public bool TryGetPointerRay(out Ray ray)
            {
                ray = Aim.GetValueOrDefault();
                return Aim.HasValue;
            }
        }

        // Puts the game's input in, and the one before back as the round ends
        private sealed class GamesInput : IDisposable
        {
            public readonly FakeInput Input = new();
            private readonly IForgeInput previous;

            public GamesInput() => previous = ForgeInput.Use(Input);

            public void Dispose() => ForgeInput.Use(previous);
        }

        // Writes down the target the ability's targeting chose
        private sealed class TargetRecorder : AbstractAbilityTask
        {
            public AbstractTargetingPacket Packet;

            public override bool IsCriticalSection => false;

            public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
            {
                if (data.GetTargetingPacket(Tags.TARGET, out var packet, autoInterrupt: false)) Packet = packet;
                return UniTask.CompletedTask;
            }
        }

        private static AbstractTargetingAbilityTask Targeting(string kind) => kind switch
        {
            "Position" => new SelectPositionTargetTask(),
            "Direction" => new SelectDirectionTargetTask(),
            "Entity" => new SelectGasTargetTask(),
            "Entity or position" => new SelectGasOrPositionTargetTask(),
            "Entity in range" => new SelectGasTargetInRangeTask(),
            _ => throw new ArgumentException(kind),
        };

        /// <summary>
        /// Each click-targeting task waits for the game's input to press, and targets along the ray it aims: the entity
        /// standing at (4, 0, 3), or the point on it the ray hits, or the direction to it from the caster (FEAT-11).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_11_Click_targeting_reads_the_games_input([Values] Hub hub,
            [Values("Position", "Direction", "Entity", "Entity or position", "Entity in range")] string kind) => Live(async () =>
        {
            using var games = new GamesInput();
            var target = World.MonoEntity(name: "Target");
            target.transform.position = new Vector3(4f, 0f, 3f);
            target.gameObject.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            var recorder = new TargetRecorder();
            var ability = Make.Ability("Aimed", Make.Stage(recorder));
            ability.Behaviour.Targeting = Targeting(kind);
            var abilities = World.Entity(hub, name: "Caster").GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "activated");

            await Frames.Step(3);
            Assert.IsNull(recorder.Packet, "targeting waits for the press");

            games.Input.Aim = new Ray(new Vector3(4f, 10f, 3f), Vector3.down);
            games.Input.Press(0);
            await Frames.Until(() => recorder.Packet != null, 10, "the press targets along the game's aim");

            var at = recorder.Packet.position;
            Assert.That(at.x, Is.EqualTo(4f).Within(1e-3f), "at the entity the ray aims at");
            Assert.That(at.z, Is.EqualTo(3f).Within(1e-3f), "at the entity the ray aims at");
            if (kind == "Direction")
            {
                var direction = recorder.Packet.direction;
                Assert.That(direction.x, Is.EqualTo(0.8f).Within(1e-3f), "toward it from the caster");
                Assert.That(direction.z, Is.EqualTo(0.6f).Within(1e-3f), "toward it from the caster");
            }
        });

        /// <summary>
        /// The Input Manager's mouse, the default here, aims through the camera it's given, or else the main camera, and
        /// with neither it aims nowhere, where targeting threw at the click (FEAT-11).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_11_The_Input_Managers_mouse_aims_through_a_camera() => Live(async () =>
        {
            Assert.IsInstanceOf<LegacyPointerInput>(ForgeInput.Default(), "the project's own input is the Input Manager's mouse");
            var mouse = new LegacyPointerInput();
            Assert.IsNull(Camera.main, "the test's scene has no main camera");
            Assert.IsFalse(mouse.TryGetPointerRay(out _), "with no camera, the mouse aims nowhere");

            var eye = new GameObject("FEAT-11 Camera").AddComponent<Camera>();
            try
            {
                mouse.Camera = eye;
                Assert.IsTrue(mouse.TryGetPointerRay(out _), "it aims through the camera it's given");
            }
            finally
            {
                Object.Destroy(eye.gameObject);
            }

            await Frames.Step();
        });
    }
}
