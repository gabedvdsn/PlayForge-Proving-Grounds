using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Time magic, a stretch scenario (the review's): a slow field, haste, and a three-second rewind rebuilt from the frame
    /// summaries' history. Each entity runs on its own time (FEAT-7), which an effect on its time-scale attribute slows or
    /// hastes (FEAT-22); an entity's time could be set only from code, so no effect could slow one.
    /// </summary>
    [Category("Stretch")]
    public class TimeMagicTests : ProvingGroundsTest
    {
        private ForgeAttribute time;
        private ForgeAttribute health;

        // An entity whose time runs at its Time Scale attribute, at full health
        private RuntimeGameplayAbilitySystem Entity(string name)
        {
            var identity = Make.RuntimeIdentity(name, Make.Set(Make.Element(time, 1f, 1f), Make.Element(health, 100f, 100f)));
            identity.TimeScaleAttribute = time;
            var gas = new RuntimeGameplayAbilitySystem(identity);
            World.Register(gas);
            return gas;
        }

        private static float Scale(IGameplayAbilitySystem gas) => gas.GetClock().TimeScale;

        private static float Health(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static bool Holds(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.GetEffectSystem().Shelf.Any(container => container.Spec.Base == effect && container.Ongoing);

        /// <summary>
        /// An entity's identity names its time-scale attribute, and its time runs at that attribute's current value, so an
        /// effect that lowers it slows the entity and one that raises it hastes it, and as each ends the entity's time is
        /// its game's again. An asset's identity hands the attribute to its runtime copy (FEAT-22).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_22_An_effect_on_the_time_scale_attribute_slows_or_hastes_its_entity() => Live(async () =>
        {
            time = Make.Attribute("Time Scale");
            health = Make.Attribute("Health");
            var mage = Entity("Mage");
            var slow = Make.Effect("Slow", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = time, Magnitude = -0.5f });
            var haste = Make.Effect("Haste", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = time, Magnitude = 0.5f });
            await Frames.Step();
            Assert.AreEqual(1f, Scale(mage), 1e-4f, "at first its time is the game's");

            Assert.IsTrue(World.Apply(mage, mage, slow), "a slow lands");
            await Frames.Step(2);
            Assert.AreEqual(0.5f, Scale(mage), 1e-4f, "and its time runs at half");
            mage.RemoveGameplayEffect(slow);
            await Frames.Step(2);
            Assert.AreEqual(1f, Scale(mage), 1e-4f, "until it comes off");

            Assert.IsTrue(World.Apply(mage, mage, haste), "a haste lands");
            await Frames.Step(2);
            Assert.AreEqual(1.5f, Scale(mage), 1e-4f, "and its time runs at half again as fast");

            var asset = Make.Identity("Copied", Make.Set(Make.Element(time, 1f, 1f)));
            asset.TimeScaleAttribute = time;
            Assert.AreSame(time, new RuntimeEntityIdentity(asset).TimeScaleAttribute, "an asset's identity hands its time-scale attribute on");
        });

        // What an entity's frames did to its attributes, for the last seconds of its own time, which a rewind undoes
        private sealed class History
        {
            private readonly IGameplayAbilitySystem gas;
            private readonly float keep;
            private readonly List<(float Time, IAttribute Attribute, AttributeValue Real)> impacts = new();

            public History(IGameplayAbilitySystem gas, float keep)
            {
                this.gas = gas;
                this.keep = keep;
                gas.GetCallbacks().OnFrameComplete += Heard;
            }

            public void Stop() => gas.GetCallbacks().OnFrameComplete -= Heard;

            // Each frame's impacts, copied as the frame's summary is handed over, and the oldest let go
            private void Heard(FrameSummarySnapshot frame)
            {
                float now = gas.GetClock().Time;
                foreach (var impact in frame.ImpactsReceived) impacts.Add((now, impact.Attribute, impact.RealImpact));
                impacts.RemoveAll(each => each.Time < now - keep - 1f);
            }

            // Undoes what the last seconds of the entity's own time did to its attributes, the newest first, each as a change
            // of the entity's own that no worker sees. Over the hit's own carrier, the attribute system commits the hit as
            // authored, whatever the values given, so each undo has a carrier of its own
            public int Rewind(float seconds)
            {
                float since = gas.GetClock().Time - seconds;
                int undone = 0;
                for (int i = impacts.Count - 1; i >= 0 && impacts[i].Time > since; i--)
                {
                    var (_, attribute, real) = impacts[i];
                    gas.TryModifyAttribute(attribute, SourcedModifiedAttributeValue.GenerateUnworkable(gas, attribute, -real.CurrentValue, -real.BaseValue), false);
                    impacts.RemoveAt(i);
                    undone++;
                }
                return undone;
            }
        }

        // An ability's task that rewinds its caster by its history
        private sealed class RewindTask : AbstractAbilityTask
        {
            public History History;
            public float Seconds = 3f;
            public int Undone;

            public override UniTask Activate(AbilityDataPacket data, CancellationToken token)
            {
                Undone = History.Rewind(Seconds);
                return UniTask.CompletedTask;
            }

            public override bool IsCriticalSection => false;
        }

        /// <summary>
        /// A slow field takes the entities within 5 m of its centre to half time for its 3 seconds of their own time, so
        /// for 6 of the game's, while one outside it runs on: a 2 s mark on each lasts 4 game seconds inside the field and
        /// 2 outside. Haste then runs an entity at half again as fast, so its 3 s mark lasts 2 game seconds. And a mage hit
        /// three times casts Rewind, which undoes what the last 3 seconds of its time did to it, rebuilt from its frames'
        /// summaries, so only the hit before them stays.
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_Time_magic_slows_hastes_and_rewinds() => Live(async () =>
        {
            time = Make.Attribute("Time Scale");
            health = Make.Attribute("Health");
            var mage = Entity("Mage");
            var knight = Entity("Knight");
            var archer = Entity("Archer");
            var where = new Dictionary<IGameplayAbilitySystem, Vector3>
            {
                [mage] = Vector3.zero, [knight] = new Vector3(3f, 0f, 1f), [archer] = new Vector3(12f, 0f, 0f),
            };

            // The slow field, and a mark that shows how long each entity's time lets an effect last
            var field = Make.Effect("Slow Field", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 3f, Target = time, Magnitude = -0.5f });
            var mark = Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 2f });
            await Frames.Step();
            var inside = where.Where(each => each.Value.magnitude <= 5f).Select(each => each.Key).ToList();
            Assert.That(inside, Is.EquivalentTo(new IGameplayAbilitySystem[] { mage, knight }), "the field covers the mage and the knight");
            foreach (var gas in inside) Assert.IsTrue(World.Apply(mage, gas, field), "and slows each");
            foreach (var gas in where.Keys) Assert.IsTrue(World.Apply(mage, gas, mark), "and each is marked");
            await Frames.Step();
            Assert.AreEqual(0.5f, Scale(knight), 1e-4f, "inside, time runs at half");
            Assert.AreEqual(1f, Scale(archer), 1e-4f, "outside, as the game's");

            await Frames.Seconds(2.5f);
            Assert.IsFalse(Holds(archer, mark), "outside, the mark's 2 seconds are over");
            Assert.IsTrue(Holds(knight, mark), "inside, it has half its time left");
            await Frames.Seconds(2f);
            Assert.IsFalse(Holds(knight, mark), "until 4 of the game's seconds are by");
            Assert.AreEqual(0.5f, Scale(knight), 1e-4f, "and the field, 3 seconds of their time, holds on");
            await Frames.Seconds(2f);
            Assert.AreEqual(1f, Scale(knight), 1e-4f, "until 6 of the game's are by");
            Assert.AreEqual(1f, Scale(mage), 1e-4f, "for the mage too");

            // Haste: half again as fast, so a 3 s mark lasts 2 of the game's seconds
            var haste = Make.Effect("Haste", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 10f, Target = time, Magnitude = 0.5f });
            var longMark = Make.Effect("Long Mark", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 3f });
            Assert.IsTrue(World.Apply(archer, archer, haste), "the archer hastes itself");
            await Frames.Step();
            Assert.AreEqual(1.5f, Scale(archer), 1e-4f, "and its time runs at half again as fast");
            Assert.IsTrue(World.Apply(archer, archer, longMark), "a 3 s mark lands on it");
            await Frames.Seconds(1.6f);
            Assert.IsTrue(Holds(archer, longMark), "which holds a little past 1.6 of the game's seconds");
            await Frames.Seconds(0.6f);
            Assert.IsFalse(Holds(archer, longMark), "and is over by 2.2");

            // Rewind: three seconds of the mage's time undone, from its frames' summaries
            var history = new History(mage, keep: 3f);
            try
            {
                var hit = Make.Effect("Hit", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -10f });
                var rewindTask = new RewindTask { History = history, Seconds = 3f };
                var rewind = Make.Ability("Rewind", Make.Stage(rewindTask));
                Assert.IsTrue(mage.GetAbilitySystem().GiveAbility(rewind, 1, out int slot), "the mage knows Rewind");

                Assert.IsTrue(World.Apply(knight, mage, hit), "the mage is hit");
                await Frames.Seconds(1.5f);
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(World.Apply(knight, mage, hit), "and hit again");
                    await Frames.Seconds(0.8f);
                }
                Assert.AreEqual(60f, Health(mage, health), 1e-3f, "four hits take 40");

                Assert.IsTrue(mage.GetAbilitySystem().TryActivateAbility(mage.GetAbilitySystem().CreateActivationRequest(slot)), "the mage casts Rewind");
                await Frames.Step(2);
                Assert.AreEqual(3, rewindTask.Undone, "which undoes the last 3 seconds' three hits");
                Assert.AreEqual(90f, Health(mage, health), 1e-3f, "so only the first stays");
            }
            finally
            {
                history.Stop();
            }
        });
    }
}
