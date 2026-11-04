using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class AbilityLifecycleTests : ProvingGroundsTest
    {
        private const int TaskFrames = 10;

        /// <summary>
        /// A GAS gave its Starting Abilities, and tried the ones set to Activate Immediately, before it built its
        /// attributes. So the default Cost rule, finding no attribute to pay from, refused any such ability with a cost.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_49_An_opening_ability_with_a_cost_activates([Values] Hub hub) => Live(async () =>
        {
            var mana = Make.Attribute("Mana");
            var set = Make.Set(Make.Element(mana, 50f, 50f));
            var opener = Make.Ability("Opening Move", Make.Stage(new ProbeAbilityTask(World, "Opening Move")));
            opener.Definition.ActivateImmediately = true;
            opener.Cost = Make.Effect("Cost", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = mana, Op = ECalculationOperation.Add, Magnitude = -10f,
            });

            if (hub == Hub.Mono)
            {
                var gas = World.NewMonoEntity(set, "Opener");
                gas.EntityData.StartingAbilities.Add(opener);
                World.Register(gas);
            }
            else
            {
                var identity = Make.Identity("Opener", set);
                identity.StartingAbilities.Add(opener);
                World.Register(new RuntimeGameplayAbilitySystem(new RuntimeEntityIdentity(identity)));
            }

            await Frames.Until(() => Ledger.Cleans("Opening Move") == 1, 10, "the opening move runs");
        });

        /// <summary>
        /// OnAbilityEnd fired when the claim was released, which for an ability with no critical stage is as it
        /// starts. Nothing reported the real end.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_9_OnAbilityEnd_fires_when_the_ability_ends([Values] Hub hub) => Live(async () =>
        {
            var abilities = Grant(hub, Make.Ability("Timed", Make.Stage(new ProbeAbilityTask(World, "Work", frames: TaskFrames))), out int index);

            int ended = 0;
            abilities.Callbacks.OnAbilityEnd += _ => ended++;

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(3);
            Assert.That(ended, Is.EqualTo(0), "it's still running");

            await Frames.Until(() => ended == 1, TaskFrames + 5, "and then it ends");
        });

        /// <summary>Task callbacks only ever fired on the legacy path, which a compiled ability doesn't take.</summary>
        [UnityTest]
        public IEnumerator TS_9_Task_callbacks_fire_for_a_compiled_ability([Values] Hub hub) => Live(async () =>
        {
            var abilities = Grant(hub, Make.Ability("Timed", Make.Stage(new ProbeAbilityTask(World, "Work", frames: 2))), out int index);

            int started = 0, ended = 0;
            abilities.Callbacks.OnAbilityTaskActivate += _ => started++;
            abilities.Callbacks.OnAbilityTaskEnd += _ => ended++;

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Until(() => Ledger.Cleans("Work") == 1, 10, "the task runs");

            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));
        });

        /// <summary>The activation went in under the definition's policy and came out under the request's.</summary>
        [UnityTest]
        public IEnumerator TS_T_An_activation_with_its_own_policy_leaves_no_trace([Values] Hub hub) => Live(async () =>
        {
            var abilities = Grant(hub, Make.Ability("Overridden", Make.Stage(new ProbeAbilityTask(World, "Work", frames: 2))), out int index);

            var request = abilities.CreateActivationRequest(index, null, EAbilityActivationPolicyExtended.AlwaysActivate);
            Assert.That(abilities.TryActivateAbility(request), "activated");

            await Frames.Until(() => Ledger.Cleans("Work") == 1, 10, "it runs");
            await Frames.Until(() => !abilities.IsExecuting, 5, "and then nothing says it still is");
        });

        /// <summary>Each ability held one entry, so the first of two overlapping activations cleared it for both.</summary>
        [UnityTest]
        public IEnumerator TS_T_Overlapping_activations_keep_the_ability_active([Values] Hub hub) => Live(async () =>
        {
            var abilities = Grant(hub, Make.Ability("Overlapping", Make.Stage(new DelayTask { DelayMilliseconds = 200 })), out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Seconds(0.1f);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Seconds(0.15f);
            Assert.That(abilities.IsExecuting, "the first is over and the second isn't");

            await Frames.Seconds(0.1f);
            Assert.That(abilities.IsExecuting, Is.False, "and now neither is");
        });

        /// <summary>
        /// Removing an ability left its slot in the active list, so the next activation looked it up and threw,
        /// and compiled tags with it still granted.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_20_Removing_an_active_ability_leaves_nothing_behind([Values] Hub hub) => Live(async () =>
        {
            var granted = Make.NewTag("Test.Status.Trained");
            var removed = Make.Ability("Removed", Make.Stage(new ProbeAbilityTask(World, "Work", frames: 100)));
            removed.Tags.PassiveGrantedTags.Add(granted);

            var gas = World.Entity(hub);
            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(removed, 1, out int index);
            abilities.GiveAbility(Make.Ability("Next", Make.Stage(new ProbeAbilityTask(World, "Next Task"))), 1, out int next);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(2);

            abilities.RemoveAbility(removed);
            Assert.That(gas.GetTagWeight(granted), Is.LessThanOrEqualTo(0), "its tags went with it");

            Assert.DoesNotThrow(() => abilities.TryActivateAbility(abilities.CreateActivationRequest(next)));

            await Frames.Until(() => Ledger.Cleans("Next Task") == 1, 10, "and the next ability runs");
        });

        /// <summary>A removed ability's claim was let go without the activations queued behind it hearing.</summary>
        [UnityTest]
        public IEnumerator GAS_20_Removing_an_ability_lets_its_queue_go_on() => Live(async () =>
        {
            var removed = Make.Ability("Removed", Make.Stage(new ProbeAbilityTask(World, "Holding", frames: 100, critical: true)));
            var queued = Make.Ability("Queued", Make.Stage(new ProbeAbilityTask(World, "Queued Task")));

            var abilities = World.Entity(Hub.Runtime).GetAbilitySystem();
            abilities.GiveAbility(removed, 1, out int first);
            abilities.GiveAbility(queued, 1, out int second);

            const EAbilityActivationPolicyExtended queue = EAbilityActivationPolicyExtended.QueueActivationIfBusy;
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(first, null, queue)), "the first activates");
            await Frames.Step(2);
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(second, null, queue)), "the second is queued");
            await Frames.Step();
            Assert.That(Ledger.Prepares("Queued Task"), Is.EqualTo(0), "behind the first");

            abilities.RemoveAbility(removed);

            await Frames.Until(() => Ledger.Prepares("Queued Task") == 1, 5, "the queued one runs once the first is gone");
        });

        /// <summary>The claim found the ability by asset, so a duplicate's activation landed on the first one's slot.</summary>
        [UnityTest]
        public IEnumerator GAS_21_A_duplicate_ability_claims_its_own_slot() => Live(async () =>
        {
            var ability = Make.Ability("Twin", Make.Stage(new ProbeAbilityTask(World, "Work", frames: 2)));

            var identity = Make.Identity("Twins", Make.UnusedSet());
            identity.AllowDuplicateAbilities = true;

            var gas = new RuntimeGameplayAbilitySystem(new RuntimeEntityIdentity(identity));
            World.Register(gas);

            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int first);
            abilities.GiveAbility(ability, 1, out int second);
            Assert.That(second, Is.Not.EqualTo(first), "two slots");

            abilities.TryActivateAbility(abilities.CreateActivationRequest(second));

            await Frames.Until(() => Ledger.Cleans("Work") == 1, 10, "it runs");
            await Frames.Until(() => !abilities.IsExecuting, 5, "and then its slot is free again");
        });

        /// <summary>
        /// The task kept its relays on itself, and a task is shared by every activation, so the second
        /// activation lost the first's relays and the process was never ended.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_U_Overlapping_activations_keep_their_own_processes([Values] Hub hub) => Live(async () =>
        {
            var probe = World.NewProbe("Run By Ability");
            var task = new RunProcessTask
            {
                Processes = new List<AbstractRuntimeProcess> { probe },
                MaxDuration = 0.1f,
                UseUnscaledDeltaTime = false,
            };

            var abilities = Grant(hub, Make.Ability("Overlapping", Make.Stage(task)), out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(2);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => probe.Recorder[ProbeHook.Terminate] == 1, 12, "the first activation ends its process");
        });

        /// <summary>
        /// A targeting task that throws is broken, not invalid, so BreakRuntimeOnInvalid has no say. The throw
        /// stayed inside the targeting sequence, so the ability went on with no target.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_V_A_throw_in_targeting_ends_the_ability(
            [Values] Hub hub,
            [Values] bool breaksOnInvalid,
            [Values(TaskHook.Prepare, TaskHook.Execute)] TaskHook hook) => Live(async () =>
        {
            var targeting = new ProbeTargetingTask(World, "Broken Targeting", frames: 1) { BreakRuntimeOnInvalid = breaksOnInvalid };
            Faults.Arm(targeting.Site(hook), loggedAs: LogType.Error);

            var ability = Make.Ability("Targeted", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.Behaviour.Targeting = targeting;

            var abilities = Grant(hub, ability, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(5);

            Assert.That(Ledger.Prepares("Stage Task"), Is.EqualTo(0), "the ability doesn't go on");
            Assert.That(abilities.IsExecuting, Is.False, "and it's over");
        });

        /// <summary>
        /// Targeting that ends before its task finishes has no target to give, whatever ended it. Here its process is
        /// ended from outside, part way through.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_V_Targeting_cut_short_ends_the_ability() => Live(async () =>
        {
            var ability = Make.Ability("Cut Short", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.Behaviour.Targeting = new ProbeTargetingTask(World, "Slow Targeting", frames: 5);

            var abilities = Grant(Hub.Runtime, ability, out int index);
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "it activates");
            await Frames.Step(2);

            var targeting = World.Control.FetchActiveProcesses().Values
                .First(block => block.Wrapper.ProcessName.StartsWith("Targeting:"));
            targeting.Relay.TerminateImmediate();
            await Frames.Step(3);

            Assert.That(Ledger.Prepares("Stage Task"), Is.EqualTo(0), "the ability goes no further");
            Assert.That(abilities.IsExecuting, Is.False, "and it's over");
        });

        /// <summary>
        /// A closed ProcessControl takes no new processes, and the ability fell back to running outside it, where
        /// nothing could pause or end it.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_AC_A_closed_ProcessControl_refuses_an_ability(
            [Values(EProcessControlState.Closed, EProcessControlState.ClosedWaiting)] EProcessControlState closed) => Live(async () =>
        {
            var abilities = Grant(Hub.Runtime, Make.Ability("Late", Make.Stage(new ProbeAbilityTask(World, "Late Task"))), out int index);
            await Frames.Step();

            World.Control.SetControlState(closed);

            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), Is.False, "it's refused");
            await Frames.Step(3);

            Assert.That(Ledger.Prepares("Late Task"), Is.EqualTo(0), "and nothing of it runs");
            Assert.That(abilities.IsExecuting, Is.False);

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();
        });

        /// <summary>The same once the ability is past its checks, with ProcessControl closing as it starts its stages.</summary>
        [UnityTest]
        public IEnumerator TS_AC_An_ability_ProcessControl_refuses_doesnt_run_outside_it() => Live(async () =>
        {
            var ability = Make.Ability("Closed On", Make.Stage(new ProbeAbilityTask(World, "Late Task")));
            ability.Behaviour.AssetLoaders.Add(new ClosingLoader());

            var abilities = Grant(Hub.Runtime, ability, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(3);

            Assert.That(Ledger.Prepares("Late Task"), Is.EqualTo(0), "nothing of it runs");
            Assert.That(abilities.IsExecuting, Is.False, "and it's over");

            World.Control.SetControlState(EProcessControlState.Ready);
            await Frames.Step();
        });

        /// <summary>How a test ends an entity, through its relay.</summary>
        public enum Ending
        {
            /// <summary>Queued, and cascading to its children.</summary>
            Terminate,
            /// <summary>At once, and cascading to its children.</summary>
            TerminateImmediate,
            /// <summary>At once, without cascading.</summary>
            WithoutCascade
        }

        /// <summary>Where an activation is when its entity ends.</summary>
        public enum Phase
        {
            Targeting,
            Stage,
            CriticalStage
        }

        /// <summary>
        /// A runtime entity's abilities ran on after it ended. A Mono process ends the processes it handles as it ends,
        /// and a runtime process didn't, so an ability waiting in a long stage outlived a runtime entity, whichever way it
        /// ended. Either kind of entity also took activations after it ended. Ending an entity now ends its abilities,
        /// critical or not, and it starts none after.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_52_Ending_an_entity_ends_its_abilities([Values] Hub hub, [Values] Ending ending, [Values] Phase phase) => Live(async () =>
        {
            var ability = Make.Ability("Outlived", Make.Stage(new ProbeAbilityTask(World, "Long Work", frames: 100000, critical: phase == Phase.CriticalStage)));
            if (phase == Phase.Targeting) ability.Behaviour.Targeting = new ProbeTargetingTask(World, "Long Targeting", frames: 100000);

            var gas = World.Entity(hub, name: "Ending");
            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int index);
            await Frames.Step();

            int ended = 0;
            abilities.Callbacks.OnAbilityEnd += _ => ended++;
            var before = new HashSet<int>(World.Control.FetchActiveProcesses().Keys);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(2);
            Assert.That(ended, Is.EqualTo(0), "it's running");

            var relay = gas is AbstractMonoProcess mono ? mono.ProcessRelay : ((AbstractRuntimeProcess)gas).Relay;
            switch (ending)
            {
                case Ending.Terminate: relay.Terminate(); break;
                case Ending.TerminateImmediate: relay.TerminateImmediate(); break;
                case Ending.WithoutCascade: relay.TerminateImmediate(cascade: false); break;
            }
            await Frames.Step(3);

            Assert.That(ended, Is.EqualTo(1), "its ability ended with it");
            var left = World.Control.FetchActiveProcesses().Where(p => !before.Contains(p.Key)).Select(p => p.Value.Wrapper.ProcessName).ToList();
            Assert.That(left, Is.Empty, "and nothing it started still runs");
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), Is.False, "and it starts none after");
            await Frames.Step();
        });

        /// <summary>How a test interrupts every running ability.</summary>
        public enum Interruption
        {
            /// <summary><see cref="AbilitySystemComponent.InjectAll"/>.</summary>
            All,
            /// <summary><c>Inject</c> with the policy they run under.</summary>
            ByPolicy,
            /// <summary>Turning <see cref="AbilitySystemComponent.Enabled"/> off.</summary>
            Disabling
        }

        /// <summary>
        /// Interrupting every running ability under a policy walked the live set of them, and since TS-Z an interrupted
        /// ability ends inside the interrupt, taking itself off that set. So the walk threw once the first had ended,
        /// and the rest ran on. Each now ends.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_53_Interrupting_every_running_ability_ends_each([Values] Hub hub, [Values] Interruption interruption) => Live(async () =>
        {
            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("First", Make.Stage(new ProbeAbilityTask(World, "First Work", frames: 1000))), 1, out int first);
            abilities.GiveAbility(Make.Ability("Second", Make.Stage(new ProbeAbilityTask(World, "Second Work", frames: 1000))), 1, out int second);
            await Frames.Step();

            int ended = 0;
            abilities.Callbacks.OnAbilityEnd += _ => ended++;
            var request = abilities.CreateActivationRequest(first);
            abilities.TryActivateAbility(request);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(second));
            await Frames.Step(2);
            Assert.That(ended, Is.EqualTo(0), "both are running");

            Assert.DoesNotThrow(() =>
            {
                switch (interruption)
                {
                    case Interruption.All: abilities.InjectAll(InterruptSequenceInjection.Instance); break;
                    case Interruption.ByPolicy: abilities.Inject(request.Policy, InterruptSequenceInjection.Instance); break;
                    case Interruption.Disabling: abilities.Enabled = false; break;
                }
            }, "the interrupt goes through");
            await Frames.Step(3);

            Assert.That(ended, Is.EqualTo(2), "and both end");
            Assert.That(abilities.IsExecuting, Is.False, "and nothing runs");
            abilities.Enabled = true;
        });

        private AbilitySystemComponent Grant(Hub hub, Ability ability, out int index)
        {
            var abilities = World.Entity(hub).GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out index), "granted");
            return abilities;
        }

        // Closes ProcessControl as the ability loads its assets, just before it registers its stages
        private sealed class ClosingLoader : AbstractSetAssetsTask
        {
            public override bool IsCriticalSection => false;

            public override void Prepare(AbilityDataPacket data) => ProcessControl.Instance.SetControlState(EProcessControlState.Closed);
        }
    }
}
