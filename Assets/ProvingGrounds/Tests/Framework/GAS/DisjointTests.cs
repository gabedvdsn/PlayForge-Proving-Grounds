using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A disjoint makes the shots already aimed at an entity lose it: they fly on to where it stood and land on
    /// nothing. A shot registers its intent with its target, and the disjoint goes through those.
    /// </summary>
    public class DisjointTests : ProvingGroundsTest
    {
        /// <summary>
        /// The disjoint aimed each shot back at the entity that disjointed, which it was aimed at already, so it
        /// went on and hit. It aims at a placeholder where the entity stood, which takes nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_26_A_disjointed_shot_lands_on_nothing([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            int disjoint = GiveDisjoint(gas);

            var shot = Shoot(gas, Hit(health), frames: 10);
            await Frames.Step(2);

            Activate(gas, disjoint);
            await Frames.Step(2);
            Assert.That(shot.Target, Is.InstanceOf<DisjointTarget>(), "the shot loses its target");

            await Frames.Until(() => !shot, 15, "and lands");
            gas.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(100f), "on nothing");
        });

        /// <summary>
        /// A shot's intent stayed on its target for good, landed or not, and every disjoint added the shots it
        /// re-aimed once more.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_26_A_shot_leaves_no_intent_behind([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            int disjoint = GiveDisjoint(gas);

            var shots = new[] { Shoot(gas, Hit(health), frames: 2), Shoot(gas, Hit(health), frames: 2), Shoot(gas, Hit(health), frames: 8) };
            await Frames.Step(4);

            Activate(gas, disjoint);
            await Frames.Step();
            Activate(gas, disjoint);

            await Frames.Until(() => !shots[0] && !shots[1] && !shots[2], 12, "the shots land");
            Assert.That(gas.Data.Count(Tags.TARGETED_INTENT), Is.EqualTo(0), "and none is still aimed at it");
        });

        /// <summary>A shot aimed somewhere else stayed on its old target's list, and that target's disjoint took it back.</summary>
        [UnityTest]
        public IEnumerator GAS_26_A_reaimed_shot_stays_on_its_new_target([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var first = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "First");
            var second = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Second");
            int disjoint = GiveDisjoint(first);

            var shot = Shoot(first, Hit(health), frames: 10);
            await Frames.Step(2);

            shot.SetTarget(second, second.GetTargetingPacket());
            Activate(first, disjoint);
            await Frames.Step(2);

            Assert.That(shot.Target, Is.SameAs(second), "it's still aimed at the second");
        });

        private ProbeShot Shoot(IGameplayAbilitySystem target, GameplayEffect effect, int frames)
        {
            var template = World.Template<ProbeShot>("Shot");
            template.ProcessLifecycle = EProcessLifecycle.SelfTerminating;
            template.ProcessTiming = EProcessStepTiming.None;
            template.RunFrames = frames;
            template.Effects = new List<GameplayEffect> { effect };

            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            data.SetPrimary(Tags.POSITION, new Vector3(0f, 0f, -5f));
            data.SetPrimary(DisjointProxyTaskBehaviour.IS_DISJOINTABLE, true);
            return World.Spawn(template, data);
        }

        private GameplayEffect Hit(Attribute health)
        {
            return Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
        }

        private int GiveDisjoint(IGameplayAbilitySystem gas)
        {
            Assert.That(gas.GetAbilitySystem().GiveAbility(Make.Ability("Disjoint", Make.Stage(new DisjointAbilityTask())), 1, out int index), "the disjoint is granted");
            return index;
        }

        private static void Activate(IGameplayAbilitySystem gas, int index)
        {
            var abilities = gas.GetAbilitySystem();
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "the disjoint activates");
        }
    }
}
