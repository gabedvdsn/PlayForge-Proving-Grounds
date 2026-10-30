using System.Collections;
using NUnit.Framework;
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
        /// An Any stage ends with its first finished task, and every compiled stage carries a callback task
        /// that finishes at once, so an Any stage ends as it starts and cancels its real work. The stage here
        /// has one task of a second, which is the least it can last.
        /// </summary>
        [UnityTest, Category("Red")]
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
        /// A failed targeting task sets TargetingFailed and nobody reads it. Its cancellation stays inside the
        /// targeting sequence, so to the ability targeting just finished, and the stages run.
        /// </summary>
        [UnityTest, Category("Red")]
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
        /// Interrupting cancels the wait on targeting, but the proxy catches that itself, so again targeting
        /// just looks finished and the stages run.
        /// </summary>
        [UnityTest, Category("Red")]
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

        /// <summary>The control for the two above: targeting that succeeds does go on to the stages.</summary>
        [UnityTest]
        public IEnumerator Successful_targeting_runs_the_stages([Values] Hub hub) => Live(async () =>
        {
            var targeting = new ProbeTargetingTask(World, "Good Targeting", frames: 1);
            var abilities = GrantTargeted(hub, targeting, out int index);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));

            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");
        });

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
