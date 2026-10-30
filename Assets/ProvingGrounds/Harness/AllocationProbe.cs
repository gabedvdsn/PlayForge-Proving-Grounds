using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>What a run of frames allocated.</summary>
    public readonly struct FrameAllocation
    {
        public readonly int Frames;
        public readonly long Total;

        /// <summary>The most any one frame allocated.</summary>
        public readonly long Peak;

        /// <summary>How many frames allocated anything at all.</summary>
        public readonly int FramesOver;

        public FrameAllocation(int frames, long total, long peak, int framesOver)
        {
            Frames = frames;
            Total = total;
            Peak = peak;
            FramesOver = framesOver;
        }

        public float PerFrame => Frames == 0 ? 0f : Total / (float)Frames;

        public override string ToString() =>
            $"{PerFrame:0.#} bytes per frame over {Frames} frames ({FramesOver} frames allocated, peak {Peak})";
    }

    /// <summary>
    /// Measures allocation with <see cref="ProfilerRecorder"/>, because the GC API returns 0 under Mono
    /// (C15). Unity's "GC Allocated In Frame" counter reads live within a frame, so an operation is the
    /// counter's change across it.
    ///
    /// A frame takes more care, because the counter's own total for a frame is noisy: 450 to 532 bytes
    /// with nothing running, shifting at random. Sampling every player loop stage shows why. All of that
    /// lands outside the player loop, in the editor's work between frames, except 40 bytes from the test
    /// runner's coroutine tick. So a frame here is the counter's change from the player loop's first
    /// stage to its last, less that tick, measured on the same frame. A clean frame reads exactly 0, with
    /// no baseline to calibrate, and a baseline would be worse than noise: taken while the noise ran high,
    /// it would hide real allocation.
    ///
    /// The cost is that anything allocated in a coroutine goes unmeasured with the test runner's.
    /// PlayForge's stepping runs in Update and its async work in UniTask continuations, which are measured.
    ///
    /// Now and then a burst of a kilobyte or so still lands on a few frames at random, whatever is running.
    /// Its source isn't known: it isn't finalizers, as a collection before measuring doesn't stop it. It
    /// doesn't repeat, where real allocation does, so a measure that isn't clean is taken again and the
    /// cleanest stands. A leak fails every attempt.
    /// </summary>
    public sealed class AllocationProbe : IDisposable
    {
        private const string Counter = "GC Allocated In Frame";
        private const int Attempts = 3;

        private sealed class FrameStart : IPlayerLoopItem
        {
            private readonly AllocationProbe _probe;
            public FrameStart(AllocationProbe probe) => _probe = probe;
            public bool MoveNext() => _probe.BeginFrame();
        }

        private sealed class CoroutinesFinished : IPlayerLoopItem
        {
            private readonly AllocationProbe _probe;
            public CoroutinesFinished(AllocationProbe probe) => _probe = probe;
            public bool MoveNext() => _probe.EndCoroutines();
        }

        private sealed class FrameEnd : IPlayerLoopItem
        {
            private readonly AllocationProbe _probe;
            public FrameEnd(AllocationProbe probe) => _probe = probe;
            public bool MoveNext() => _probe.EndFrame();
        }

        private ProfilerRecorder _bytes;
        private readonly GameObject _markerHost;
        private bool _disposed;

        private long _frameStart, _coroutinesStart, _coroutineBytes;

        // A measure starts with a whole frame: BeginFrame picks up what MeasureFrames asks for
        private int _requested, _remaining;
        private long _total, _peak;
        private int _framesOver;

        public AllocationProbe()
        {
            _bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, Counter);
            if (!_bytes.Valid) throw new AssertionException($"The profiler counter \"{Counter}\" is not available");

            _markerHost = new GameObject("Allocation Marker");
            Object.DontDestroyOnLoad(_markerHost);
            _markerHost.AddComponent<AllocationMarker>().Probe = this;

            PlayerLoopHelper.AddAction(PlayerLoopTiming.Initialization, new FrameStart(this));
            PlayerLoopHelper.AddAction(PlayerLoopTiming.LastUpdate, new CoroutinesFinished(this));
            PlayerLoopHelper.AddAction(PlayerLoopTiming.LastPostLateUpdate, new FrameEnd(this));
        }

        public void Dispose()
        {
            _disposed = true;
            _bytes.Dispose();
            Object.Destroy(_markerHost);
        }

        private bool BeginFrame()
        {
            if (_disposed) return false;

            _frameStart = _coroutinesStart = _bytes.CurrentValue;

            if (_requested > 0)
            {
                _remaining = _requested;
                _requested = 0;
            }
            return true;
        }

        internal void MarkUpdatesFinished()
        {
            if (!_disposed) _coroutinesStart = _bytes.CurrentValue;
        }

        private bool EndCoroutines()
        {
            if (_disposed) return false;

            _coroutineBytes = _bytes.CurrentValue - _coroutinesStart;
            return true;
        }

        private bool EndFrame()
        {
            if (_disposed) return false;
            if (_remaining == 0) return true;

            long bytes = _bytes.CurrentValue - _frameStart - _coroutineBytes;

            _remaining--;
            _total += bytes;
            _peak = Math.Max(_peak, bytes);
            if (bytes != 0) _framesOver++;
            return true;
        }

        /// <summary>Bytes <paramref name="operation"/> allocates, run once within this frame.</summary>
        public long Measure(Action operation)
        {
            long before = _bytes.CurrentValue;
            operation();
            return _bytes.CurrentValue - before;
        }

        /// <summary>
        /// Mean bytes per run of <paramref name="operation"/>, after <paramref name="warmup"/> unmeasured
        /// runs so the JIT and any caches the operation fills are out of the way.
        /// </summary>
        public float MeasureEach(Action operation, int runs = 100, int warmup = 10)
        {
            for (int i = 0; i < warmup; i++) operation();

            long before = _bytes.CurrentValue;
            for (int i = 0; i < runs; i++) operation();
            return (_bytes.CurrentValue - before) / (float)runs;
        }

        /// <summary>
        /// Steps <paramref name="count"/> whole frames and reports what they allocated, taking the cleanest
        /// of up to three attempts.
        /// </summary>
        public async UniTask<FrameAllocation> MeasureFrames(FrameStepper frames, int count)
        {
            var best = await MeasureOnce(frames, count);

            for (int attempt = 1; attempt < Attempts && best.Total != 0; attempt++)
            {
                var again = await MeasureOnce(frames, count);
                if (again.Total < best.Total) best = again;
            }

            return best;
        }

        private async UniTask<FrameAllocation> MeasureOnce(FrameStepper frames, int count)
        {
            _total = _peak = 0;
            _framesOver = 0;
            _requested = count;

            // The last frame's end stage runs after this test resumes in it, so the count is read a frame on
            await frames.Step(count + 1);

            return new FrameAllocation(count, _total, _peak, _framesOver);
        }
    }
}
