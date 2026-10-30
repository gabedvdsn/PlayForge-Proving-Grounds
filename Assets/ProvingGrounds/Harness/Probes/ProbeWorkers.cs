using System;
using System.Collections.Generic;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    // One probe for each worker base. Each hook passes through a fault site named "{worker}.{hook}" and
    // counts as a hit there, so a test reads how often a worker ran from FaultInjector.Hits.

    public sealed class ProbeImpactWorker : AbstractImpactWorker
    {
        private readonly FaultInjector _faults;
        private readonly IAttribute _attribute;

        public readonly string Name;

        public override EWorkerExecution Execution { get; }

        public ProbeImpactWorker(string name, IAttribute attribute, FaultInjector faults, EWorkerExecution execution = EWorkerExecution.Inline)
        {
            Name = name;
            Execution = execution;
            _attribute = attribute;
            _faults = faults;
        }

        public override bool PreValidateWorkFor(ImpactData impactData) => true;

        public override bool ValidateWorkFor(ImpactData impactData) => true;

        public override void ActivateInline(ImpactData impactData) => _faults.Trip($"{Name}.Inline");

        public override IEnumerable<IRootAction> CreateDeferredActions(ImpactWorkerContext ctx)
        {
            _faults.Trip($"{Name}.Deferred");
            return Array.Empty<IRootAction>();
        }

        public override void SubscribeToCache(ImpactWorkerCache cache) => cache.ProvideWorker(_attribute, this);

        public override void UnsubscribeFromCache(ImpactWorkerCache cache) => cache.RemoveWorker(_attribute, this);
    }

    public sealed class ProbeEffectWorker : AbstractEffectWorker
    {
        private readonly FaultInjector _faults;

        public readonly string Name;

        public ProbeEffectWorker(string name, FaultInjector faults)
        {
            Name = name;
            _faults = faults;
        }

        public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx) => Hit("Application");

        public override IEnumerable<IRootAction> OnEffectTick(EffectWorkerContext ctx) => Hit("Tick");

        public override IEnumerable<IRootAction> OnEffectRemoval(EffectWorkerContext ctx) => Hit("Removal");

        public override IEnumerable<IRootAction> OnEffectImpact(EffectWorkerContext ctx) => Hit("Impact");

        private IEnumerable<IRootAction> Hit(string hook)
        {
            _faults.Trip($"{Name}.{hook}");
            return Array.Empty<IRootAction>();
        }
    }

    public sealed class ProbeTagWorker : AbstractTagWorker
    {
        private readonly FaultInjector _faults;

        public readonly string Name;

        public ProbeTagWorker(string name, FaultInjector faults, int tickPause = 0)
        {
            Name = name;
            TickPause = tickPause;
            _faults = faults;
        }

        public override void Activate(IGameplayAbilitySystem system) => _faults.Trip($"{Name}.Activate");

        public override void Tick(IGameplayAbilitySystem system) => _faults.Trip($"{Name}.Tick");

        public override void Resolve(IGameplayAbilitySystem system) => _faults.Trip($"{Name}.Resolve");
    }

    public sealed class ProbeAnalysisWorker : AbstractAnalysisWorker
    {
        private readonly FaultInjector _faults;

        public readonly string Name;

        public ProbeAnalysisWorker(string name, FaultInjector faults)
        {
            Name = name;
            _faults = faults;
        }

        public override IEnumerable<IRootAction> Analyze(IGameplayAbilitySystem system, FrameSummary frameSummary)
        {
            _faults.Trip($"{Name}.Analyze");
            return Array.Empty<IRootAction>();
        }
    }

    public sealed class ProbeAttributeWorker : AbstractAttributeWorker
    {
        private readonly FaultInjector _faults;
        private readonly IAttribute _attribute;

        public readonly string Name;

        public override EWorkerExecution Execution { get; }
        public override EChangeEventTiming Timing { get; }

        public ProbeAttributeWorker(string name, IAttribute attribute, FaultInjector faults,
            EChangeEventTiming timing = EChangeEventTiming.PreChange, EWorkerExecution execution = EWorkerExecution.Inline)
        {
            Name = name;
            Execution = execution;
            Timing = timing;
            _attribute = attribute;
            _faults = faults;
        }

        public override bool PreValidateWorkFor(ChangeValue change) => true;

        public override bool ValidateWorkFor(WorkerContext ctx) => true;

        public override void Intercept(WorkerContext ctx) => _faults.Trip($"{Name}.Intercept");

        public override IEnumerable<IRootAction> DeferredIntercept(WorkerContext ctx)
        {
            _faults.Trip($"{Name}.Deferred");
            return Array.Empty<IRootAction>();
        }

        public override bool RegisterWithHandler(AttributeChangeMomentHandler preChange, AttributeChangeMomentHandler postChange)
        {
            return Timing switch
            {
                EChangeEventTiming.PreChange => preChange.AddWorker(_attribute, this),
                EChangeEventTiming.PostChange => postChange.AddWorker(_attribute, this),
                EChangeEventTiming.Both => preChange.AddWorker(_attribute, this) | postChange.AddWorker(_attribute, this),
                _ => false
            };
        }

        public override bool DeRegisterFromHandler(AttributeChangeMomentHandler preChange, AttributeChangeMomentHandler postChange)
        {
            return Timing switch
            {
                EChangeEventTiming.PreChange => preChange.RemoveWorker(_attribute, this),
                EChangeEventTiming.PostChange => postChange.RemoveWorker(_attribute, this),
                EChangeEventTiming.Both => preChange.RemoveWorker(_attribute, this) | postChange.RemoveWorker(_attribute, this),
                _ => false
            };
        }
    }
}
