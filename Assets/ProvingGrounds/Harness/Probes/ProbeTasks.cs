using System.Threading;
using Cysharp.Threading.Tasks;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>The hooks of a probe task, each a fault site named <c>"{task}.{hook}"</c>.</summary>
    public enum TaskHook
    {
        Prepare,
        Execute,
        Step,
        Clean
    }

    /// <summary>
    /// A sequence task that reports to the <see cref="TaskLedger"/>. It runs for its given number of
    /// frames, async or stepped, whichever way the sequence drives it.
    /// </summary>
    public sealed class ProbeSequenceTask : ISequenceTask
    {
        private readonly World _world;
        private readonly int _frames;
        private int _stepped;

        public readonly string Name;

        public ProbeSequenceTask(World world, string name, int frames = 0)
        {
            _world = world;
            _frames = frames;
            Name = name;
        }

        public string Site(TaskHook hook) => $"{Name}.{hook}";

        public void Prepare(SequenceDataPacket data)
        {
            _stepped = 0;
            _world.Ledger.Prepared(Name);
            _world.Faults.Trip(Site(TaskHook.Prepare));
        }

        public async UniTask Execute(SequenceDataPacket data, CancellationToken token)
        {
            _world.Ledger.Started(Name);
            try
            {
                _world.Faults.Trip(Site(TaskHook.Execute));
                for (int i = 0; i < _frames; i++) await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            finally
            {
                _world.Ledger.Finished(Name);
            }
        }

        public bool Step(SequenceDataPacket data, float deltaTime)
        {
            _world.Faults.Trip(Site(TaskHook.Step));
            return ++_stepped >= _frames;
        }

        public void Clean(SequenceDataPacket data)
        {
            _world.Ledger.Cleaned(Name);
            _world.Faults.Trip(Site(TaskHook.Clean));
        }
    }

    /// <summary>An ability's asset loader, whose work is its Prepare. Its fault site is <c>"{loader}.Prepare"</c>.</summary>
    public sealed class ProbeLoader : AbstractSetAssetsTask
    {
        private readonly World _world;

        public readonly string Name;

        public override bool IsCriticalSection => false;

        public ProbeLoader(World world, string name)
        {
            _world = world;
            Name = name;
        }

        public string Site => $"{Name}.{TaskHook.Prepare}";

        public override void Prepare(AbilityDataPacket data) => _world.Faults.Trip(Site);
    }

    /// <summary>
    /// A targeting task that passes through fault sites rather than the ledger, because TS-AA would flag every
    /// cancelled one. It takes its given number of frames, then finds its target invalid if told to.
    /// </summary>
    public sealed class ProbeTargetingTask : AbstractTargetingAbilityTask
    {
        private readonly World _world;
        private readonly int _frames;
        private readonly bool _invalid;

        public readonly string Name;

        public ProbeTargetingTask(World world, string name, int frames = 0, bool invalid = false)
        {
            _world = world;
            _frames = frames;
            _invalid = invalid;
            Name = name;
        }

        public string Site(TaskHook hook) => $"{Name}.{hook}";

        public override void Prepare(AbilityDataPacket data)
        {
            _world.Faults.Trip(Site(TaskHook.Prepare));
            base.Prepare(data);
        }

        public override async UniTask Activate(AbilityDataPacket data, CancellationToken token)
        {
            _world.Faults.Trip(Site(TaskHook.Execute));
            for (int i = 0; i < _frames; i++) await UniTask.Yield(PlayerLoopTiming.Update, token);

            if (_invalid) WhenTargetingInvalid(data);
        }

        public override void Clean(AbilityDataPacket data)
        {
            _world.Faults.Trip(Site(TaskHook.Clean));
            base.Clean(data);
        }
    }

    /// <summary>An ability task that reports to the <see cref="TaskLedger"/>, running for its given number of frames.</summary>
    public sealed class ProbeAbilityTask : AbstractAbilityTask
    {
        private readonly World _world;
        private readonly int _frames;

        public readonly string Name;

        public override bool IsCriticalSection { get; }

        public ProbeAbilityTask(World world, string name, int frames = 0, bool critical = false)
        {
            _world = world;
            _frames = frames;
            Name = name;
            IsCriticalSection = critical;
        }

        public string Site(TaskHook hook) => $"{Name}.{hook}";

        public override void Prepare(AbilityDataPacket data)
        {
            _world.Ledger.Prepared(Name);
            _world.Faults.Trip(Site(TaskHook.Prepare));
        }

        public override async UniTask Activate(AbilityDataPacket data, CancellationToken token)
        {
            _world.Ledger.Started(Name);
            try
            {
                _world.Faults.Trip(Site(TaskHook.Execute));
                for (int i = 0; i < _frames; i++) await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            finally
            {
                _world.Ledger.Finished(Name);
            }
        }

        public override void Clean(AbilityDataPacket data)
        {
            _world.Ledger.Cleaned(Name);
            _world.Faults.Trip(Site(TaskHook.Clean));
        }
    }
}
