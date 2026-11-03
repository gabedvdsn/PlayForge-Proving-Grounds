using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class AbilityDataTests : ProvingGroundsTest
    {
        /// <summary>An injection that came while the ability's sequence wasn't running took the legacy path, which threw.</summary>
        [UnityTest, Category("Red")]
        public IEnumerator TS_S_Injecting_into_a_targeting_ability_is_safe([Values] Hub hub) => Live(async () =>
        {
            var ability = Make.Ability("Targeted", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.Behaviour.Targeting = new ProbeTargetingTask(World, "Slow Targeting", frames: 5);

            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(ability, 1, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index));
            await Frames.Step(2);

            Assert.DoesNotThrow(() => abilities.Inject(index, SkipStageInjection.Instance), "the injection is safe");
            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 12, "and the ability goes on");
        });

        /// <summary>An activation's packet took its request's payload lists as they were, so writing to one wrote to both.</summary>
        [UnityTest]
        public IEnumerator TS_X_An_activation_keeps_its_requests_payload_apart([Values] Hub hub) => Live(async () =>
        {
            var key = Make.NewTag("Test.Data.Hits");
            var request = ProcessDataPacket.Default();
            request.AddPayload(key, 1);

            var abilities = World.Entity(hub).GetAbilitySystem();
            abilities.GiveAbility(Make.Ability("Writer", Make.Stage(new PayloadWritingTask(key))), 1, out int index);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request));
            await Frames.Step(3);

            Assert.That(request.Count(key), Is.EqualTo(1), "the request's own payload is as it was");
        });

        private sealed class PayloadWritingTask : AbstractAbilityTask
        {
            private readonly Tag _key;

            public PayloadWritingTask(Tag key) => _key = key;

            public override bool IsCriticalSection => false;

            public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
            {
                data.AddPayload(_key, 2);
                return UniTask.CompletedTask;
            }
        }
    }
}
