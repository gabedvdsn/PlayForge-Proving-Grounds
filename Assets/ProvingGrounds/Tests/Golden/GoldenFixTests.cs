using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The golden scenarios from Phase 0, S13 to S25, each pinning a fix by its tracker ID. Several of the
    /// originals reached past the framework, by reflection or by calling EndOfFrame themselves, because
    /// nothing was stepping it. Here live frames do that.
    /// </summary>
    public class GoldenFixTests : GoldenTest
    {
        /// <summary>
        /// Ongoing requirements gate periodic ticks: a regen that avoids a tag stops ticking while another
        /// effect grants that tag, and resumes once it is gone. The granting effect lands AFTER the regen,
        /// the order that used to leave the regen ticking, because ongoing state was evaluated before the
        /// new effect's tags were compiled (PF-002).
        /// </summary>
        [UnityTest]
        public IEnumerator S13_GAS_10_OngoingRequirement_PausesPeriodicTicks([Values] Hub hub) => Live(async () =>
        {
            var attr = Make.Attribute("Poise");
            var gas = Entity(hub, Make.Element(attr, 0f, 100f));
            var blocked = Make.NewTag("Test.Status.RegenBlocked");

            var regen = Make.Effect("Regen", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Infinite, Target = attr, Op = ECalculationOperation.Add, Magnitude = 5f,
                PeriodicTicks = true, FixedDeltaTime = 1f,
            });
            regen.TargetRequirements.OngoingRequirements.AvoidTags = new List<TagQuery> { TagQuery.HasTag(blocked) };

            var blocker = Make.Effect("Blocker", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 2f, Target = null,
                GrantedTags = new List<Tag> { blocked }, FixedDeltaTime = 1f,
            });

            Apply(gas, regen);
            await Frames.Step();
            AssertValue(gas, attr, "regen ticks while unblocked", 5f);

            Apply(gas, blocker);
            await Frames.Step();
            AssertValue(gas, attr, "regen paused while the avoided tag is granted", 5f);

            await Frames.Step(4);
            gas.TryGetAttributeValue(attr, out var value);
            Assert.That(value.CurrentValue, Is.GreaterThan(5f), "regen resumes once the tag is gone");
        });

        /// <summary>
        /// An ability's own source tag requirements gate activation: avoiding "stunned" blocks it while the
        /// caster has that tag, and allows it again once the tag is gone. These used to be authored but
        /// never checked (PF-003).
        /// </summary>
        [UnityTest]
        public IEnumerator S14_GAS_11_AbilitySourceTagRequirements_BlockActivation([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var stunned = Make.NewTag("Test.Status.Stunned");

            var ability = Make.Ability("Strike");
            ability.Tags.TagRequirements.SourceRequirements.AvoidTags.Add(TagQuery.HasTag(stunned));

            var abilities = gas.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "ability granted");
            Assert.That(abilities.CanActivateAbility(index), "activatable while not stunned");

            var stun = Make.Effect("Stun", new EffectConfig { Target = null, GrantedTags = new List<Tag> { stunned } });
            Apply(gas, stun);
            await Frames.Step();
            Assert.That(abilities.CanActivateAbility(index), Is.False, "blocked while the avoided tag is on the caster");

            await Remove(gas, stun);
            Assert.That(abilities.CanActivateAbility(index), "activatable again once the tag is gone");
        });

        /// <summary>
        /// Pre-change workers and the pre-change callback adjust what a commit records. Armor is a
        /// pre-change <see cref="MathAttributeWorker"/> scaling incoming Health loss by (1 − Armor): it
        /// sees the INCOMING delta, the commit clamps after, and history records the clamped result. Both
        /// used to be silent no-ops, as the commit ran from the original value (PF-004).
        /// </summary>
        [UnityTest]
        public IEnumerator S15_GAS_02_PreChangeWorkers_AdjustTheCommittedDelta([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var armor = Make.Attribute("Armor");
            var gas = Entity(hub,
                Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(armor, 0.5f, 0.5f));
            var attrSystem = gas.GetAttributeSystem();

            attrSystem.ProvideWorker(new MathAttributeWorker
            {
                TimingConfig = AbstractAttributeWorker.EChangeEventTiming.PreChange,
                TargetAttribute = health,
                AllowSelfModification = true,
                TargetModification = EAttributeTargetExpanded.Current,
                SignPolicy = ESignPolicyExtended.Negative,
                From = EFromSelfSource.FromSelf,
                RelativeTo = armor,
                Operation = ECalculationOperation.Multiply,
                OperationTarget = EAttributeTarget.Current,
                OperationPolicy = EMathApplicationPolicy.OneMinus,
            });

            var damageGroup = Make.NewTag("Test.Group.Damage");
            var hit30 = Make.Effect("Hit30", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = -30f,
            });
            hit30.Definition.RetentionGroup = damageGroup;
            var hit150 = Make.Effect("Hit150", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = -150f,
            });
            var heal50 = Make.Effect("Heal50", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = 50f,
            });

            Apply(gas, hit30);
            await ValueHolds(gas, health, "armor halves a 30 hit", 85f);

            Apply(gas, hit30);
            await ValueHolds(gas, health, "second 30 hit", 70f);

            attrSystem.TryGetGroupValue(health, damageGroup, out var damageSlice);
            Assert.That(damageSlice.CurrentValue, Is.EqualTo(-30f).Within(0.001f), "adjusted commits land in the effect's group");

            // Incoming −150 → −75, THEN clamp: 70 → 0. Halving the clamped −70 would leave 35
            Apply(gas, hit150);
            await ValueHolds(gas, health, "armor applies before the clamp", 0f);

            // History holds the clamped −70, not −75, so the heal lands on 50, not 45. The worker ignores
            // heals (Negative sign policy), so this commit takes the untouched path
            Apply(gas, heal50);
            await ValueHolds(gas, health, "heal after an overkill hit", 50f);

            // Raw deltas go through the same path. The pre-change callback is the original repro: it
            // halves a −30 delta to −15. Workers see raw deltas too (Decision 9), so the armor worker then
            // halves what the callback left, and Health falls by 7.5
            AttributeSystemCallbacks.AttributeChangeDelegate halveLoss = (attribute, change) =>
            {
                if (ReferenceEquals(attribute, health) && change.Value.CurrentValue < 0f)
                    change.Override(change.Value.ToAttributeValue() * 0.5f);
            };
            attrSystem.Callbacks.OnAttributePreChange += halveLoss;
            gas.TryModifyAttribute(health, SourcedModifiedAttributeValue.GenerateSimple(gas, health, -30f, 0f));
            attrSystem.Callbacks.OnAttributePreChange -= halveLoss;
            await ValueHolds(gas, health, "the callback and then armor halve a raw −30", 42.5f, 100f);
        });

        /// <summary>
        /// A raw delta that moves Base and Current together lands whole on a pool clamped to its Base.
        /// Raising both (a max-Health bonus that also heals; an enemy's dials) used to lose the Current
        /// gain: it was clamped against the old Base before the Base rose (PF-005). Lowering both still
        /// ends with Current inside the new Base.
        /// </summary>
        [UnityTest]
        public IEnumerator S16_GAS_12_RawDelta_MovesBaseAndCurrentTogether([Values] Hub hub) => Live(async () =>
        {
            var poise = Make.Attribute("Poise");
            var dial = Make.Attribute("Dial");
            var gas = Entity(hub,
                Make.Element(poise, 25f, 25f, EAttributeOverflowPolicy.ZeroToBase),
                Make.Element(dial, 0f, 0f, EAttributeOverflowPolicy.ZeroToBase));

            gas.TryModifyAttribute(dial, SourcedModifiedAttributeValue.GenerateSimple(gas, dial, 2f, 2f));
            await ValueHolds(gas, dial, "raising an empty pool by 2/2", 2f, 2f);

            gas.TryModifyAttribute(poise, SourcedModifiedAttributeValue.GenerateSimple(gas, poise, 12.5f, 12.5f));
            await ValueHolds(gas, poise, "raising a full pool by 12.5/12.5", 37.5f, 37.5f);

            gas.TryModifyAttribute(poise, SourcedModifiedAttributeValue.GenerateSimple(gas, poise, -10f, -10f));
            await ValueHolds(gas, poise, "lowering it by 10/10", 27.5f, 27.5f);

            gas.TryModifyAttribute(poise, SourcedModifiedAttributeValue.GenerateSimple(gas, poise, 0f, -20f));
            await ValueHolds(gas, poise, "lowering only the Base pulls Current down with it", 7.5f, 7.5f);
        });

        /// <summary>
        /// A GAS with nothing going on costs no garbage per frame. Every fighter's GAS runs its end of frame
        /// each frame, and that used to allocate about 1 KB apiece: the action queue enumerated a
        /// SortedDictionary to learn it was empty, and a frame summary snapshot copied six lists for a
        /// callback nobody listened to (PF-006). Unobserved, or the invariants would be that listener.
        /// </summary>
        [UnityTest]
        public IEnumerator S17_GAS_P1_IdleFrame_AllocatesNothing([Values] Hub hub) => Live(async () =>
        {
            using var probe = new AllocationProbe();
            World.Entity(hub, Make.Set(Make.Element(Make.Attribute("Health"), 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)), observed: false);
            await Frames.Step(20);

            var frames = await probe.MeasureFrames(Frames, 2000);

            Assert.That(frames.Total, Is.EqualTo(0), frames.ToString());
        });

        /// <summary>
        /// Compiling granted tags and checking tag requirements allocate nothing, while the applied tags they
        /// read still follow tags as they come and go. Both run several times for every effect applied or
        /// removed, and they used to allocate about 15 KB per effect between them: the applied tags were
        /// copied into a new list for every check (twice over), the granted tags were gathered through
        /// iterators, and tag queries counted with LINQ (PF-007).
        /// </summary>
        [UnityTest]
        public IEnumerator S18_GAS_P2_TagChecks_AllocateNothing([Values] Hub hub) => Live(async () =>
        {
            var gas = Entity(hub, Make.Element(Make.Attribute("Health"), 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));

            var marked = Make.NewTag("Test.Status.Marked");
            var parent = Make.NewTag("Test.Status");
            var markFx = Make.Effect("Mark", new EffectConfig { Target = null, GrantedTags = new List<Tag> { marked } });
            Assert.That(Apply(gas, markFx), "apply the marking effect");
            Assert.That(gas.GetAppliedTags().Contains(marked), "the applied tags include a granted tag");

            var required = new List<TagQuery> { TagQuery.HasTag(marked) };
            var avoid = new List<TagQuery> { TagQuery.HasTag(parent) };
            Assert.That(ForgeHelper.ValidateAvoidRequireQueries(gas.GetAppliedTags(), required, avoid), "requirements read the applied tags");

            void Check()
            {
                gas.CompileGrantedTags();
                ForgeHelper.ValidateAvoidRequireQueries(gas.GetAppliedTags(), required, avoid);
                ForgeHelper.ValidateAllQueries(required, (IEnumerable<Tag>)gas.GetAppliedTags());
            }

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(Check, runs: 2000, warmup: 20);
            Assert.That(bytes, Is.EqualTo(0f), "compiling and checking tags allocates nothing");

            // The cached list follows the tags: gone once the effect that granted it is
            await Remove(gas, markFx);
            Assert.That(gas.GetAppliedTags().Contains(marked), Is.False, "the applied tags drop a removed tag");
            Assert.That(ForgeHelper.ValidateAvoidRequireQueries(gas.GetAppliedTags(), required, avoid), Is.False, "requirements see it gone");
        });

        /// <summary>
        /// An exception thrown while an ability runs is reported, not swallowed. A bare catch around
        /// execution, meant for cancellation, also ate real failures, such as an asset loader that throws
        /// before any stage starts, so a broken ability looked like one that did nothing (PF-008). The fault
        /// injector is what checks it: a fault that fires must reach the log exactly once.
        /// </summary>
        [UnityTest]
        public IEnumerator S19_TS_04_BrokenAbility_IsReported([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var loader = new ProbeLoader(World, "Broken Loader");
            var ability = Make.Ability("Broken");
            ability.Behaviour.AssetLoaders.Add(loader);

            var abilities = gas.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "ability granted");

            Faults.Arm(loader.Site, loggedAs: LogType.Error);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(2);

            Assert.That(Faults.Fired(loader.Site), Is.EqualTo(1));
        });

        /// <summary>
        /// A process whose step throws is reported, and the frame goes on without it: the processes stepped
        /// after it still step, and a state queued for it, such as a termination, still applies. A throwing
        /// step used to end the whole frame's stepping and skip its own flush, so a fighter despawned on a
        /// frame where something else threw kept stepping its destroyed body for good, throwing every frame
        /// and starving every process stepped after it (PF-011).
        /// </summary>
        [UnityTest]
        public IEnumerator S20_PC_01_ThrowingStep_DoesNotStallTheFrame() => Live(async () =>
        {
            // Stepped in the order registered: the broken one first
            var broken = World.Probe("Broken", EProcessLifecycle.Synchronous);
            var after = World.Probe("After", EProcessLifecycle.Synchronous);
            Faults.Arm(broken.Recorder.Site(ProbeHook.Update), times: FaultInjector.Always);

            // Torn down while running, as a despawned fighter is: the termination waits for its step
            World.Control.Terminate(broken.Relay.CacheIndex);

            await Frames.Step();

            Assert.That(after.Recorder[ProbeHook.Update], Is.EqualTo(1), "the process after the broken one still steps that frame");
            Assert.That(broken.Relay.State, Is.EqualTo(EProcessState.Terminated), "the broken process's termination applies");
            Assert.That(World.Control.IsRegistered(broken.Relay.CacheIndex), Is.False, "and its block is gone");
        });

        /// <summary>
        /// Activating an ability again reuses the behaviour an earlier activation compiled, once that
        /// activation is over, instead of compiling it anew. Compiling was most of what an activation
        /// allocated, about 2.8 KB each, and a fighter activates several abilities a second (PF-012). The
        /// activations here end at once, on their loader failing, which they do the same way every time.
        /// </summary>
        [UnityTest]
        public IEnumerator S21_TS_03_RepeatedActivation_ReusesItsCompiledBehaviour([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var loader = new ProbeLoader(World, "Repeated Loader");
            var ability = Make.Ability("Repeated");
            ability.Behaviour.AssetLoaders.Add(loader);

            var abilities = gas.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "ability granted");
            Assert.That(abilities.TryGetAbilityContainer(index, out var container), "its container");

            const int Activations = 5;
            Faults.Arm(loader.Site, times: FaultInjector.Always, loggedAs: LogType.Error);

            for (int i = 0; i < Activations; i++)
            {
                abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
                await Frames.Step();
            }

            Assert.That(Faults.Fired(loader.Site), Is.EqualTo(Activations), "every activation ran its behaviour");
            Assert.That(container.CompiledProxies, Is.EqualTo(1), "the behaviour was compiled once, not once per activation");
        });

        /// <summary>
        /// A MonoBehaviour GAS raises <c>OnSystemInitialized</c> to a subscriber that attached before it was
        /// registered, once it's ready to use. Its callbacks used to be created partway through
        /// initialization, just before the event fired, so nobody outside could subscribe in time (PF-013).
        /// A runtime GAS initializes in its constructor, so this is the Mono hub's alone.
        /// </summary>
        [UnityTest]
        public IEnumerator S22_GAS_13_SystemInitialized_ReachesEarlySubscribers() => Live(async () =>
        {
            var gas = World.NewMonoEntity();

            // Subscribed after the GAS is added and before it's registered, as game code would
            var callbacks = gas.Callbacks;
            Assert.That(callbacks, Is.Not.Null, "the GAS has its callbacks before it's initialized");

            int fired = 0;
            bool readyWhenFired = false;
            callbacks.OnSystemInitialized += () =>
            {
                fired++;
                readyWhenFired = gas.IsInitialized && gas.AttributeSystem != null;
            };

            World.Register(gas);
            await Frames.Until(() => gas.IsInitialized, 3, "the GAS initializes");

            Assert.That(fired, Is.EqualTo(1), "OnSystemInitialized reaches the early subscriber once");
            Assert.That(readyWhenFired, "the GAS is ready to use when it fires");
            Assert.That(gas.Callbacks, Is.SameAs(callbacks), "initialization keeps the callbacks it had");
        });

        /// <summary>
        /// Resuming a sequence leaves Time.timeScale alone (TS-1, PF-001). Time scale is global, so a sequence
        /// resuming during a hit-stop or slow motion must not end it. A sequence is paused and resumed the
        /// way a game does, by pausing ProcessControl with the sequence mid-run.
        /// </summary>
        [UnityTest]
        public IEnumerator S23_TS_01_Resume_LeavesTimeScaleAlone() => Live(async () =>
        {
            var task = new ProbeSequenceTask(World, "Held", frames: 6);
            var sequence = new TaskSequence(TaskSequenceBuilder.Create("Held Sequence").Task(task).Build());
            ProcessControl.Register(sequence, out var relay);
            World.Own(relay);
            await Frames.Step(2);
            Assert.That(relay.IsRunning, "the sequence is mid-run");

            Time.timeScale = 0.25f;

            World.Control.SetControlState(EProcessControlState.Waiting);
            await Frames.Step();
            Assert.That(relay.IsWaiting, "pausing ProcessControl pauses the sequence");

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();

            Assert.That(relay.IsRunning, "resuming ProcessControl resumes it");
            Assert.That(Time.timeScale, Is.EqualTo(0.25f).Within(0.0001f), "the time scale survives the resume");

            await Frames.Until(() => Ledger.Cleans("Held") == 1, 20, "the sequence runs on to its end");
        });

        /// <summary>
        /// A GAS can be given an item (GAS-3). ItemSystemComponent never created its shelf, so GiveItem threw
        /// a NullReferenceException, and every read of the empty inventory allocated a new list.
        /// </summary>
        [UnityTest]
        public IEnumerator S24_GAS_03_GiveItem_LandsOnTheShelf([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            var items = gas.GetItemSystem();
            var sword = Make.Item("Sword");

            Assert.That(items.ItemCount, Is.EqualTo(0), "an empty inventory holds nothing");
            Assert.That(items.GetItemContainers(), Is.Empty);

            Assert.That(items.GiveItem(sword, out int index), "give the item");
            await Frames.Step();

            Assert.That(index, Is.EqualTo(0), "it is on the shelf at index 0");
            Assert.That(items.HasItem(sword), "the item is there");
            Assert.That(items.ItemCount, Is.EqualTo(1));
            Assert.That(items.GetItemContainers(), Has.Count.EqualTo(1), "the containers list it");
        });

        /// <summary>
        /// A GAS registered with ProcessControl runs its end of frame each frame (GAS-1). A runtime GAS
        /// stepped in Update only, so WhenLateUpdate, and with it EndOfFrame (queued actions, tag workers,
        /// analysis workers, death), never ran. The originals called WhenLateUpdate by hand, which is how
        /// this stayed hidden.
        /// </summary>
        [UnityTest]
        public IEnumerator S25_GAS_01_GAS_RunsItsEndOfFrame([Values] Hub hub) => Live(async () =>
        {
            var gas = World.Entity(hub);
            int framesCompleted = 0;
            gas.GetCallbacks().OnFrameComplete += _ => framesCompleted++;

            await Frames.Step(3);

            Assert.That(framesCompleted, Is.EqualTo(3), "one stepped frame runs one end of frame");
        });
    }
}
