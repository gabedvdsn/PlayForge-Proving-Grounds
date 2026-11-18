using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A cue's data packet (EP-62), the data a Process or a Sequence cue runs with, loaded for what raised it: an ability's
    /// cue starts from its activation's packet; on it go the cue, its asset and spec, its source, the one it plays on,
    /// where it plays and its owner's clock; then the asset's Local Data, its numbers read for the cue; then the cue's own
    /// Data list, each step's keys in place of an earlier one's (Decision 91).
    /// </summary>
    public class CuePacketTests : ProvingGroundsTest
    {
        private readonly List<Object> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        // A cue that loads its packet each time it plays, with its own Data list
        private sealed class Loading : AbstractCueHandler
        {
            public readonly List<DataWrapper> Data = new();
            public readonly List<(CueContext Cue, SequenceDataPacket Packet)> Loaded = new();

            public override void Burst(CueContext cue) => Loaded.Add((cue, CuePackets.For(cue, Data)));
        }

        private static DataWrapper Word(Tag key, string word) => new() { Key = key, Value = new LocalString { Value = word } };

        private static T First<T>(ProcessDataPacket packet, Tag key)
        {
            Assert.That(packet.TryGetFirst(key, out T value), $"the packet holds a {typeof(T).Name} under {key}");
            return value;
        }

        /// <summary>
        /// An effect's cue packet holds the cue, the effect and its spec, its source, the one it's on as its target, where it
        /// plays and that one's clock; the effect's Local Data, its numbers read at the cue's level; and the cue's own Data
        /// list last, in place of the effect's under the same key (EP-62).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_62_An_effects_cue_packet_holds_its_effect_and_its_data([Values] Hub hub) => Live(async () =>
        {
            var word = Make.NewTag("Test.Data.Word");
            var power = Make.NewTag("Test.Data.Power");
            var extra = Make.NewTag("Test.Data.Extra");
            var health = Make.Attribute("Health");
            var source = World.Entity(hub, name: "Source");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), name: "Target");
            if (target is Component placed) placed.transform.position = new Vector3(3f, 0f, 4f);
            var brand = Make.Effect("Brand", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -5f });
            brand.LocalData.Add(Word(word, "the effect's"));
            brand.LocalData.Add(new DataWrapper
            {
                Key = power, Value = new LocalFloat { Value = ScalableFloat.From(context => context.Level.CurrentValue * 2f) },
            });
            var cue = new Loading();
            cue.Data.Add(Word(word, "the cue's"));
            cue.Data.Add(new DataWrapper { Key = extra, Value = new LocalInt { Value = 3 } });
            brand.Components.Add(new CuesEffectComponent { OnApply = { cue } });

            Assert.IsTrue(World.Apply(source, target, brand), "the brand lands");
            await Frames.Step();

            Assert.That(cue.Loaded, Has.Count.EqualTo(1), "its cue played once, as it landed");
            var (raised, packet) = cue.Loaded[0];
            Assert.AreEqual(ECueMoment.Applied, First<CueContext>(packet, Tags.CUE).Moment, "the packet holds the cue");
            Assert.AreSame(brand, First<GameplayEffect>(packet, Tags.ASSET), "its effect");
            Assert.AreSame(brand, First<GameplayEffectSpec>(packet, Tags.SPEC).Base, "and its spec");
            Assert.AreSame(source, First<ISource>(packet, Tags.SOURCE), "its source");
            Assert.AreSame(target, First<ITarget>(packet, Tags.TARGET_REAL), "the one it's on, as its target");
            Assert.AreSame(target.GetClock(), packet.Clock, "whose clock it goes by");
            if (hub == Hub.Mono)
                Assert.AreEqual(new Vector3(3f, 0f, 4f), First<Vector3>(packet, Tags.POSITION), "where it plays");

            Assert.AreEqual("the cue's", First<string>(packet, word), "the cue's own data has the last word");
            Assert.AreEqual(1, packet.Count(word), "in place of the effect's");
            Assert.AreEqual(3, First<int>(packet, extra), "with keys of its own");
            float level = NumberContext.Of(in raised).Level.CurrentValue;
            Assert.That(level, Is.GreaterThan(0f), "the cue has a level");
            Assert.AreEqual(level * 2f, First<float>(packet, power), "the effect's Local Data, its numbers read at the cue's level");
        });

        /// <summary>
        /// An ability's cue packet starts from its activation's: its target, not the caster it plays on, and the number its
        /// caller set; then its ability and spec, its caster and its clock, and the ability's Local Data. It's a packet of
        /// its own, so writing to it leaves the activation's alone (EP-62).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_62_An_abilitys_cue_packet_starts_from_its_activations([Values] Hub hub) => Live(async () =>
        {
            var word = Make.NewTag("Test.Data.Word");
            var damage = Make.NewTag("Test.Caller.Damage");
            var cue = new Loading();
            var ability = Make.Ability("Mark", Make.Stage(new ProbeAbilityTask(World, "Stage Task")));
            ability.LocalData.Add(Word(word, "the ability's"));
            ability.Components.Add(new CuesAbilityComponent { OnCommit = { cue } });
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            request.SetByCaller(damage, 12f);

            abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request));
            await Frames.Until(() => Ledger.Cleans("Stage Task") == 1, 10, "the stage ran");

            Assert.That(cue.Loaded, Has.Count.EqualTo(1), "its cue played as it committed");
            var (raised, packet) = cue.Loaded[0];
            Assert.AreSame(target, First<ITarget>(packet, Tags.TARGET_REAL), "the activation's target, not its caster");
            Assert.That(packet.TryGetSetByCaller(damage, out float set) && set == 12f, "and the number its caller set");
            Assert.AreSame(ability, First<Ability>(packet, Tags.ASSET), "its ability");
            Assert.AreSame(ability, First<AbilitySpec>(packet, Tags.SPEC).Base, "and its spec");
            Assert.AreSame(caster, First<ISource>(packet, Tags.SOURCE), "its caster");
            Assert.AreSame(caster.GetClock(), packet.Clock, "whose clock it goes by");
            Assert.AreEqual("the ability's", First<string>(packet, word), "the ability's Local Data");

            Assert.IsNotNull(raised.Data, "the cue was raised with its activation's packet");
            packet.AddPayload(Tags.TARGET_REAL, (ITarget)caster);
            Assert.AreEqual(1, raised.Data.Count(Tags.TARGET_REAL), "and its own packet leaves the activation's alone");
        });

        /// <summary>
        /// An entity's cue packet holds its identity, itself as its target, its clock and where it is, and its identity's
        /// Local Data; an entity has no spec (EP-62).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_62_An_entitys_cue_packet_holds_its_identity_and_its_Local_Data([Values] Hub hub) => Live(async () =>
        {
            var word = Make.NewTag("Test.Data.Word");
            var cue = new Loading();
            var asset = Make.Identity("Cued", Make.UnusedSet());
            asset.Components.Add(new CuesEntityComponent { OnSpawn = { cue } });
            asset.LocalData.Add(Word(word, "the entity's"));
            IGameplayAbilitySystem entity;
            if (hub == Hub.Runtime)
            {
                var runtime = new RuntimeGameplayAbilitySystem(asset);
                World.Register(runtime);
                entity = runtime;
            }
            else
            {
                var host = new GameObject("Cued");
                made.Add(host);
                host.transform.position = new Vector3(1f, 2f, 3f);
                var mono = GameplayAbilitySystem.AddToGameObject(host, asset);
                mono.ProcessLifecycle = EProcessLifecycle.Synchronous;
                mono.ProcessTiming = EProcessStepTiming.UpdateAndLate;
                World.Register(mono);
                entity = mono;
            }
            await Frames.Until(() => cue.Loaded.Count > 0, 8, "its cue played as it spawned");

            var (_, packet) = cue.Loaded[0];
            Assert.AreSame(asset, First<EntityIdentity>(packet, Tags.ASSET), "its identity");
            Assert.IsFalse(packet.ContainsKey(Tags.SPEC), "and no spec, as an entity has none");
            Assert.AreSame(entity, First<ITarget>(packet, Tags.TARGET_REAL), "itself, as its target");
            Assert.AreSame(entity.GetClock(), packet.Clock, "whose clock it goes by");
            Assert.AreEqual("the entity's", First<string>(packet, word), "its identity's Local Data");
            if (hub == Hub.Mono)
                Assert.AreEqual(new Vector3(1f, 2f, 3f), First<Vector3>(packet, Tags.POSITION), "where it is");
        });
    }
}
