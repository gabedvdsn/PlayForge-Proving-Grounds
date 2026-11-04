using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class AbilityActivationTests : ProvingGroundsTest
    {
        /// <summary>
        /// Cost validation compares what the attribute holds with the effect's signed magnitude, and a cost is
        /// negative, so anything clears it: 10 mana affords a 50 mana ability.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_07_A_cost_blocks_activation_when_short([Values] Hub hub) => Live(async () =>
        {
            int index = GrantCostly(hub, mana: 10f, cost: -50f, out var abilities);

            Assert.That(abilities.CanActivateAbility(index), Is.False, "10 mana can't pay a 50 mana cost");
            await Frames.Step();
        });

        /// <summary>The control for the test above, so that a fix can't pass it by refusing everything.</summary>
        [UnityTest]
        public IEnumerator A_cost_allows_activation_when_affordable([Values] Hub hub, [Values(50f, 60f)] float mana) => Live(async () =>
        {
            int index = GrantCostly(hub, mana, cost: -50f, out var abilities);

            Assert.That(abilities.CanActivateAbility(index), $"{mana} mana can pay a 50 mana cost");
            await Frames.Step();
        });

        /// <summary>An effect that adds to the attribute asks nothing of it, so it can't be unaffordable.</summary>
        [UnityTest]
        public IEnumerator A_cost_that_gives_needs_nothing([Values] Hub hub) => Live(async () =>
        {
            int index = GrantCostly(hub, mana: 0f, cost: 20f, out var abilities);

            Assert.That(abilities.CanActivateAbility(index), "it's a gain, not a price");
            await Frames.Step();
        });

        private int GrantCostly(Hub hub, float mana, float cost, out AbilitySystemComponent abilities)
        {
            var attribute = Make.Attribute("Mana");
            var gas = World.Entity(hub, Make.Set(Make.Element(attribute, mana, 100f)));

            var ability = Make.Ability("Costly");
            ability.Cost = Make.Effect("Cost", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = attribute, Op = ECalculationOperation.Add, Magnitude = cost,
            });

            abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int index);
            return index;
        }

        /// <summary>
        /// An Any stage ends with its first finished task, and every compiled stage carried a callback task
        /// that finished at once, so an Any stage ended as it started and cancelled its real work. The stage
        /// here has one task of a second, which is the least it can last.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_B_An_Any_stage_lasts_until_a_real_task_finishes([Values] Hub hub) => Live(async () =>
        {
            const int OneSecond = 60;
            var task = new ProbeAbilityTask(World, "One Second", frames: OneSecond);
            var stage = Make.Stage(task);
            stage.StagePolicy = new AnyProxyStagePolicy();

            var gas = World.Entity(hub);
            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Any Stage", stage), 1, out int index);
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "activated");

            int taken = await Frames.Until(() => Ledger.Cleans("One Second") == 1, OneSecond + 10, "the stage ends");

            Assert.That(taken, Is.GreaterThanOrEqualTo(OneSecond), "the stage lasts as long as its task");
        });

        /// <summary>
        /// The channel yielded in its body and DoWhileAsync yielded again, so it counted one frame's time in
        /// two and lasted twice as long as it was set to.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_AB_A_channel_lasts_its_duration() => Live(async () =>
        {
            const int HalfASecond = 30;
            var channel = new ChannelAbilityTask { ChannelDuration = 0.5f };
            var ability = Make.Ability("Channelled", Make.Stage(channel), Make.Stage(new ProbeAbilityTask(World, "After")));

            var abilities = World.RuntimeEntity().GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            int taken = await Frames.Until(() => Ledger.Prepares("After") == 1, HalfASecond + 5, "the channel ends");
            Assert.That(taken, Is.GreaterThanOrEqualTo(HalfASecond - 3), "and doesn't end early");
        });

        /// <summary>
        /// A stage set to apply usage at its start, which is the default, fired its ended callback there too,
        /// and never at its real end.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_D_A_stage_reports_its_end_when_it_ends([Values] Hub hub, [Values] bool usageAtStart) => Live(async () =>
        {
            const int TaskFrames = 10;
            var stage = Make.Stage(new ProbeAbilityTask(World, "Stage Work", frames: TaskFrames));
            stage.ApplyUsageAtStageStart = usageAtStart;

            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Reported", stage), 1, out int index);

            int started = 0, ended = 0;
            abilities.Callbacks.OnAbilityStageActivate += _ => started++;
            abilities.Callbacks.OnAbilityStageEnd += _ => ended++;

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(3);

            Assert.That(started, Is.EqualTo(1), "it has started");
            Assert.That(ended, Is.EqualTo(0), "it hasn't ended");

            await Frames.Until(() => ended == 1, TaskFrames + 5, "it ends with its task");
            Assert.That(started, Is.EqualTo(1), "and it started once");
        });

        /// <summary>Usage effects land when the stage asks for them, and only on a stage that ran to its end.</summary>
        [UnityTest]
        public IEnumerator Usage_effects_apply_at_the_moment_the_stage_chooses([Values] Hub hub, [Values] bool usageAtStart) => Live(async () =>
        {
            const int TaskFrames = 10;
            var attribute = Make.Attribute("Mana");
            var gas = World.Entity(hub, Make.Set(Make.Element(attribute, 60f, 100f)));

            var stage = Make.Stage(new ProbeAbilityTask(World, "Stage Work", frames: TaskFrames));
            stage.ApplyUsageEffects = true;
            stage.ApplyUsageAtStageStart = usageAtStart;

            var ability = Make.Ability("Costly", stage);
            ability.Cost = Make.Effect("Cost", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = attribute, Op = ECalculationOperation.Add, Magnitude = -50f,
            });

            var abilities = gas.GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(3);

            gas.TryGetAttributeValue(attribute, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(usageAtStart ? 10f : 60f), "the cost at this point in the stage");

            await Frames.Step(TaskFrames + 5);

            gas.TryGetAttributeValue(attribute, out value);
            Assert.That(value.CurrentValue, Is.EqualTo(10f), "and the cost is paid once by the end");
        });

        /// <summary>
        /// A failed targeting task set TargetingFailed and nobody read it. Its cancellation stayed inside the
        /// targeting sequence, so to the ability targeting just finished, and the stages ran.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_C_Failed_targeting_runs_no_stages([Values] Hub hub) => Live(async () =>
        {
            var targeting = new ProbeTargetingTask(World, "Failing Targeting", frames: 1, invalid: true);
            var abilities = GrantTargeted(hub, targeting, out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(5);

            Assert.That(Faults.Hits(targeting.Site(TaskHook.Execute)), Is.EqualTo(1), "targeting ran");
            Assert.That(Ledger.Prepares("Stage Task"), Is.EqualTo(0), "the ability doesn't go on without a target");
        });

        /// <summary>
        /// Interrupting cancelled the wait on targeting, but the proxy caught that itself, so again targeting
        /// just looked finished and the stages ran.
        /// </summary>
        [UnityTest]
        public IEnumerator TS_C_An_interrupt_during_targeting_runs_no_stages([Values] Hub hub) => Live(async () =>
        {
            var targeting = new ProbeTargetingTask(World, "Waiting Targeting", frames: 1000);
            var abilities = GrantTargeted(hub, targeting, out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(3);
            Assert.That(Ledger.Prepares("Stage Task"), Is.EqualTo(0), "it's still targeting");

            abilities.Inject(index, InterruptSequenceInjection.Instance);
            await Frames.Step(5);

            Assert.That(Ledger.Prepares("Stage Task"), Is.EqualTo(0), "the interrupt ended the ability");
        });

        /// <summary>BreakRuntimeOnInvalid is the task's say: off, the ability goes on with no valid target.</summary>
        [UnityTest]
        public IEnumerator Targeting_set_not_to_break_goes_on_after_failing([Values] Hub hub) => Live(async () =>
        {
            var targeting = new ProbeTargetingTask(World, "Lenient Targeting", frames: 1, invalid: true) { BreakRuntimeOnInvalid = false };
            var abilities = GrantTargeted(hub, targeting, out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");
        });

        /// <summary>The control for the two above: targeting that succeeds does go on to the stages.</summary>
        [UnityTest]
        public IEnumerator Successful_targeting_runs_the_stages([Values] Hub hub) => Live(async () =>
        {
            var targeting = new ProbeTargetingTask(World, "Good Targeting", frames: 1);
            var abilities = GrantTargeted(hub, targeting, out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");
        });

        /// <summary>
        /// Targeting checked a target through its GameObject, which a runtime GAS doesn't have, so an entity
        /// targeting itself threw, and the ability ended before any stage.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_29_An_entity_can_target_itself([Values] Hub hub) => Live(async () =>
        {
            var targeting = new AutoTargetSelfTask
            {
                Requirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
            };
            var abilities = GrantTargeted(hub, targeting, out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");
        });

        /// <summary>The range check measured between GameObjects, so with a runtime GAS at either end it always failed.</summary>
        [UnityTest]
        public IEnumerator GAS_29_A_range_check_measures_any_entity([Values] Hub hub, [Values(3f, 10f)] float distance) => Live(async () =>
        {
            var range = Make.NewTag("Test.Data.Range");
            var ability = Make.Ability("Ranged", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.LocalData.Add(new DataWrapper { Key = range, floatValue = 5f });
            ability.TargetActivationRules.Add(new CheckRangeValidation { Tag = range });

            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            target.GetTargetingPacket().position = new Vector3(distance, 0f, 0f);

            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request));
            await Frames.Step(5);

            Assert.That(Ledger.Cleans("Stage Task"), Is.EqualTo(distance < 5f ? 1 : 0), distance < 5f ? "in range" : "out of range");
        });

        /// <summary>
        /// Auto-targeting measured from the caster's transform, which a runtime GAS doesn't have, so a runtime caster
        /// searched the whole scene, whatever its range, and didn't sort what it found.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_38_A_runtime_caster_auto_targets_within_its_range([Values] bool byAffinity) => Live(async () =>
        {
            var far = World.MonoEntity(name: "Far");
            far.transform.position = new Vector3(10f, 0f, 0f);
            far.gameObject.AddComponent<SphereCollider>();
            Physics.SyncTransforms();

            var abilities = GrantAutoTargeted(byAffinity, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(5);

            Assert.That(Ledger.Cleans("Stage Task"), Is.EqualTo(0), "nothing within 5, so targeting failed");
        });

        /// <summary>The control for the test above: a target within range is found.</summary>
        [UnityTest]
        public IEnumerator A_runtime_caster_auto_targets_what_is_in_range([Values] bool byAffinity) => Live(async () =>
        {
            var near = World.MonoEntity(name: "Near");
            near.transform.position = new Vector3(3f, 0f, 0f);
            near.gameObject.AddComponent<SphereCollider>();
            Physics.SyncTransforms();

            var abilities = GrantAutoTargeted(byAffinity, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");
        });

        /// <summary>
        /// AutoTargetTask's collider buffer was MaxTargets long, so 0, which means every match, had no room and found
        /// nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_43_Auto_targeting_every_match_finds_them([Values] Hub hub) => Live(async () =>
        {
            foreach (float x in new[] { 2f, 3f })
            {
                var target = World.MonoEntity(name: $"Target at {x}");
                target.transform.position = new Vector3(x, 0f, 0f);
                target.gameObject.AddComponent<SphereCollider>();
            }
            Physics.SyncTransforms();

            var abilities = GrantTargeted(hub, AutoTarget(maxTargets: 0), out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "targeting found them, and the stage ran");
        });

        /// <summary>
        /// At MaxTargets 1 the buffer had room for one collider, which could be the caster's own, so a target in range
        /// went unfound.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_43_A_caster_with_a_collider_still_finds_its_target() => Live(async () =>
        {
            var caster = World.MonoEntity(name: "Caster");
            caster.gameObject.AddComponent<SphereCollider>();
            var target = World.MonoEntity(name: "Target");
            target.transform.position = new Vector3(3f, 0f, 0f);
            target.gameObject.AddComponent<SphereCollider>();
            Physics.SyncTransforms();

            var ability = Make.Ability("Targeted", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.Behaviour.Targeting = AutoTarget(maxTargets: 1);
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");
        });

        /// <summary>
        /// Targeting stores a targeting packet under Target, and Apply Effects asks Target Real for the entity, which
        /// nothing wrote. So an ability authored the documented way, a target and then Apply Effects, applied nothing,
        /// whether the target came from implicit targeting or a targeting task.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_46_Apply_Effects_reaches_the_target_targeting_found([Values] Hub hub, [Values] bool targetingTask) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var hurt = Make.Effect("Hurt", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Op = ECalculationOperation.Add, Magnitude = -30f,
            });

            // With no targeting task, implicit targeting makes the caster the target
            var ability = Make.Ability("Self Harm", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { hurt } }));
            if (targetingTask) ability.Behaviour.Targeting = new AutoTargetSelfTask();

            var abilities = gas.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "activated");
            await Frames.Step(5);

            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(70f), "the effect landed on the target");
        });

        private static AutoTargetTask AutoTarget(int maxTargets) => new AutoTargetTask
        {
            Range = 5f, MaxTargets = maxTargets, Mode = EAutoTargetMode.Nearest,
            Requirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
        };

        private AbilitySystemComponent GrantAutoTargeted(bool byAffinity, out int index)
        {
            var requirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() };
            AbstractTargetingAbilityTask targeting = byAffinity
                ? new AutoTargetByAffinityTask
                {
                    Range = 5f, MaxTargets = 1, Mode = EAutoTargetMode.Nearest, AffinityFilter = EAffinityFilter.AlliesAndEnemies,
                    Requirements = requirements,
                }
                : new AutoTargetTask { Range = 5f, MaxTargets = 1, Mode = EAutoTargetMode.Nearest, Requirements = requirements };
            return GrantTargeted(Hub.Runtime, targeting, out index);
        }

        private AbilitySystemComponent GrantTargeted(Hub hub, AbstractTargetingAbilityTask targeting, out int index)
        {
            var ability = Make.Ability("Targeted", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.Behaviour.Targeting = targeting;

            var abilities = World.Entity(hub).GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out index), "granted");
            return abilities;
        }
    }
}
