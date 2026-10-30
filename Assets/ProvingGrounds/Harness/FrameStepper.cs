using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// Steps the real player loop a frame at a time, so a test can say "this takes exactly N frames".
    /// A step ends at the frame's last player loop stage, once FixedUpdate, Update and LateUpdate have
    /// all run, so a test only ever looks at finished frames.
    ///
    /// Time is captured: every frame lasts exactly <see cref="DeltaTime"/> and runs one FixedUpdate,
    /// however fast the machine is. Unscaled time does not follow, so anything timed on it (C02) stays
    /// on the wall clock and can't be stepped. WaitForEndOfFrame never resumes under -nographics, which
    /// is why a step waits on a player loop stage instead.
    /// </summary>
    public sealed class FrameStepper
    {
        public const float DefaultDeltaTime = 1f / 60f;

        private readonly FramePulse _pulse;
        private readonly float _savedCapture, _savedFixed, _savedTimeScale;

        public float DeltaTime { get; }

        /// <summary>Frames stepped since the test body started.</summary>
        public int Frame { get; private set; }

        public int Updates => _pulse.Updates;
        public int FixedUpdates => _pulse.FixedUpdates;
        public int LateUpdates => _pulse.LateUpdates;

        internal FrameStepper(float deltaTime)
        {
            DeltaTime = deltaTime;

            var host = new GameObject("Frame Pulse");
            Object.DontDestroyOnLoad(host);
            _pulse = host.AddComponent<FramePulse>();

            _savedCapture = Time.captureDeltaTime;
            _savedFixed = Time.fixedDeltaTime;
            _savedTimeScale = Time.timeScale;

            Time.captureDeltaTime = deltaTime;
            Time.fixedDeltaTime = deltaTime;
            Time.timeScale = 1f;
        }

        internal void Release()
        {
            Time.captureDeltaTime = _savedCapture;
            Time.fixedDeltaTime = _savedFixed;
            Time.timeScale = _savedTimeScale;

            if (_pulse) Object.Destroy(_pulse.gameObject);
        }

        /// <summary>Moves to a frame's end and starts counting there, so frame 0 is well defined.</summary>
        internal async UniTask Align()
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            Frame = 0;
        }

        /// <summary>Runs whole frames.</summary>
        public async UniTask Step(int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                Frame++;
            }
        }

        /// <summary>Runs as many frames as <paramref name="seconds"/> of captured time, rounded to the nearest.</summary>
        public UniTask Seconds(float seconds) => Step(Mathf.RoundToInt(seconds / DeltaTime));

        /// <summary>
        /// Steps until <paramref name="condition"/> holds at a frame's end, and fails the test if it hasn't
        /// within <paramref name="within"/> frames. Returns the frames it took.
        /// </summary>
        public async UniTask<int> Until(Func<bool> condition, int within, string what)
        {
            int taken = 0;
            while (!condition())
            {
                if (taken >= within) throw new AssertionException($"{what}: not true within {within} frames");
                await Step();
                taken++;
            }
            return taken;
        }

        /// <summary>Fails the test unless <paramref name="condition"/> first holds exactly <paramref name="frames"/> frames from now.</summary>
        public async UniTask ExactlyIn(int frames, Func<bool> condition, string what)
        {
            int taken = await Until(condition, frames, what);
            if (taken != frames) throw new AssertionException($"{what}: expected in exactly {frames} frames, took {taken}");
        }

        /// <summary>Fails the test if <paramref name="condition"/> stops holding at any frame's end for the next <paramref name="frames"/> frames.</summary>
        public async UniTask Holds(int frames, Func<bool> condition, string what)
        {
            for (int i = 0; i < frames; i++)
            {
                await Step();
                if (!condition()) throw new AssertionException($"{what}: stopped holding after {i + 1} of {frames} frames");
            }
        }
    }
}
