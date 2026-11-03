using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class ContainedEffectTests : ProvingGroundsTest
    {
        /// <summary>
        /// A contained effect lands where the effect carrying it landed, as OnApply and OnTick ones do. An
        /// OnRemove one went to the source, so an enemy's poison ending put its aftermath on the enemy.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_08_An_OnRemove_effect_lands_on_the_target([Values] Hub hub) => Live(async () =>
        {
            var carried = Carried(out var marked);
            var poison = Make.Effect("Poison", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 0.1f, Target = null });
            poison.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnRemove, ContainedEffect = carried } };

            var source = World.Entity(hub, name: "Source");
            var target = World.Entity(hub, name: "Target");
            World.Apply(source, target, poison);

            await Frames.Seconds(0.2f);

            Assert.That(target.GetTagWeight(marked), Is.GreaterThan(0), "it lands where the poison was");
            Assert.That(source.GetTagWeight(marked), Is.LessThanOrEqualTo(0), "and not on whoever applied it");
        });

        /// <summary>The control for the test above: the other two phases have always landed on the target.</summary>
        [UnityTest]
        public IEnumerator An_OnApply_or_OnTick_effect_lands_on_the_target(
            [Values] Hub hub, [Values(EApplyTickRemove.OnApply, EApplyTickRemove.OnTick)] EApplyTickRemove phase) => Live(async () =>
        {
            var carried = Carried(out var marked);
            var carrier = Make.Effect("Carrier", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 0.2f, PeriodicTicks = true, Ticks = 1, Target = null,
            });
            carrier.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = phase, ContainedEffect = carried } };

            var source = World.Entity(hub, name: "Source");
            var target = World.Entity(hub, name: "Target");
            World.Apply(source, target, carrier);

            await Frames.Seconds(0.3f);

            Assert.That(target.GetTagWeight(marked), Is.GreaterThan(0));
            Assert.That(source.GetTagWeight(marked), Is.LessThanOrEqualTo(0));
        });

        /// <summary>
        /// An effect that contains itself applied itself inside its own application, and so on until the stack
        /// overflowed, which nothing can catch. It applies once, and says why.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_28_An_effect_that_contains_itself_applies_once([Values] Hub hub) => Live(async () =>
        {
            var echo = Make.Effect("Echo", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = null });
            echo.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = echo } };

            var gas = World.Entity(hub);
            int applied = 0;
            gas.GetCallbacks().OnEffectApplied += spec =>
            {
                if (spec.Base != echo) return;

                // A fuse, so the unguarded code stops at 50 instead of overflowing the stack
                if (++applied == 50) echo.ImpactSpecification.Packets = System.Array.Empty<ContainedEffectPacket>();
            };

            LogAssert.Expect(LogType.Warning, new Regex("Echo"));
            World.Apply(gas, gas, echo);

            Assert.That(applied, Is.EqualTo(1));
            await Frames.Step();
        });

        /// <summary>The control for the test above: carried twice, an effect applies twice. Only nesting is refused.</summary>
        [UnityTest]
        public IEnumerator An_effect_carried_twice_applies_twice([Values] Hub hub) => Live(async () =>
        {
            var carried = Carried(out _);
            var carrier = Make.Effect("Carrier", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = null });
            carrier.ImpactSpecification.Packets = new[]
            {
                new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = carried },
                new ContainedEffectPacket { Policy = EApplyTickRemove.OnApply, ContainedEffect = carried },
            };

            var gas = World.Entity(hub);
            int applied = 0;
            gas.GetCallbacks().OnEffectApplied += spec =>
            {
                if (spec.Base == carried) applied++;
            };

            World.Apply(gas, gas, carrier);

            Assert.That(applied, Is.EqualTo(2));
            await Frames.Step();
        });

        private GameplayEffect Carried(out Tag marked)
        {
            marked = Make.NewTag("Test.Status.Aftermath");
            return Make.Effect("Aftermath", new EffectConfig { Target = null, GrantedTags = new List<Tag> { marked } });
        }
    }
}
