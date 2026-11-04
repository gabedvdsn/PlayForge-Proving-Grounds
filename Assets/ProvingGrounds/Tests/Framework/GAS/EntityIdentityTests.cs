using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class EntityIdentityTests : ProvingGroundsTest
    {
        /// <summary>A runtime copy of an identity lost what it starts with, so the entity came up with nothing.</summary>
        [UnityTest]
        public IEnumerator GAS_09_A_runtime_copy_starts_with_what_the_identity_does() => Live(async () =>
        {
            var identity = Filled(out var sword);

            var gas = new RuntimeGameplayAbilitySystem(new RuntimeEntityIdentity(identity));
            World.Register(gas);
            await Frames.Step();

            Assert.That(gas.GetAbilitySystem().AbilityCount, Is.EqualTo(1), "its starting ability");
            Assert.That(gas.GetItemSystem().HasItem(sword), "its starting item");
        });

        /// <summary>Every field the two share, so the next one added can't be left out of the copy either.</summary>
        [Test]
        public void GAS_09_A_runtime_copy_carries_every_field_the_identity_has()
        {
            var identity = Filled(out _);
            var copy = new RuntimeEntityIdentity(identity);

            foreach (var field in typeof(RuntimeEntityIdentity).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var original = typeof(EntityIdentity).GetField(field.Name, BindingFlags.Public | BindingFlags.Instance);
                if (original != null) Assert.That(field.GetValue(copy), Is.EqualTo(original.GetValue(identity)), field.Name);
            }
        }

        /// <summary>
        /// A GAS registered with an affiliation wrote it onto its identity, which every entity of that kind shares: the
        /// Mono GAS onto its Entity asset, a runtime GAS onto a shared runtime identity. One entity sent to the other side
        /// moved them all, and in the editor it changed the asset itself.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_47_A_registered_affiliation_stays_with_its_entity([Values] Hub hub) => Live(async () =>
        {
            var red = Make.NewTag("Test.Team.Red");
            var blue = Make.NewTag("Test.Team.Blue");
            var turncoatData = ProcessDataPacket.Default();
            turncoatData.SetPrimary(Tags.AFFILIATION, new List<Tag> { blue });
            GameObject host = null;
            try
            {
                IGameplayAbilitySystem turncoat, loyal;
                System.Func<List<Tag>> shared;
                if (hub == Hub.Mono)
                {
                    var mono = World.NewMonoEntity(name: "Turncoat");
                    var identity = mono.EntityData;
                    identity.Affiliation = new List<Tag> { red };
                    World.Register(mono, turncoatData);

                    // A second entity of the same kind, with no affiliation of its own
                    host = new GameObject("Loyal");
                    var second = GameplayAbilitySystem.AddToGameObject(host, identity);
                    second.ProcessLifecycle = EProcessLifecycle.Synchronous;
                    second.ProcessTiming = EProcessStepTiming.UpdateAndLate;
                    World.Register(second);
                    (turncoat, loyal, shared) = (mono, second, () => identity.Affiliation);
                }
                else
                {
                    var identity = new RuntimeEntityIdentity(Make.Identity("Kind", Make.UnusedSet())) { Affiliation = new List<Tag> { red } };
                    var first = new RuntimeGameplayAbilitySystem(identity);
                    World.Register(first, turncoatData);
                    var second = new RuntimeGameplayAbilitySystem(identity);
                    World.Register(second);
                    (turncoat, loyal, shared) = (first, second, () => identity.Affiliation);
                }
                await Frames.Step();

                Assert.That(turncoat.GetAffiliation(), Is.EqualTo(new[] { blue }), "the turncoat changed sides");
                Assert.That(loyal.GetAffiliation(), Is.EqualTo(new[] { red }), "the loyal one didn't");
                Assert.That(shared(), Is.EqualTo(new[] { red }), "and the identity still says Red");
            }
            finally
            {
                if (host != null) Object.Destroy(host);
                await Frames.Step();
            }
        });

        /// <summary>
        /// Only the Mono GAS applied its entity's affiliation as tags, so a requirement on a side's tag held for one hub
        /// and failed for the other. The two kept copies of the same code, which had drifted.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_S1_Both_hubs_apply_the_affiliation_as_tags([Values] Hub hub) => Live(async () =>
        {
            var red = Make.NewTag("Test.Team.Red");
            var gas = Kind(hub, identity => identity.Affiliation.Add(red));
            await Frames.Step();

            Assert.That(gas.GetTagWeight(red), Is.EqualTo(1));
        });

        /// <summary>
        /// A runtime GAS made from an Entity asset lost the asset's Local Data, because its copy of the identity gave
        /// none. The Mono GAS kept it.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_S1_Both_hubs_keep_the_entity_s_local_data([Values] Hub hub) => Live(async () =>
        {
            var key = Make.NewTag("Test.Data.Marker");
            var gas = Kind(hub, identity => identity.LocalData.Add(new DataWrapper { Key = key, Type = EDataWrapperType.Int, intValue = 7 }));
            await Frames.Step();

            Assert.That(gas.TryGetLocalData(key, out var data), "the entity's Local Data");
            Assert.That(data.intValue, Is.EqualTo(7));
        });

        // A registered entity made from an Entity asset, set up first, on either hub
        private IGameplayAbilitySystem Kind(Hub hub, System.Action<EntityIdentity> setUp)
        {
            if (hub == Hub.Mono)
            {
                var mono = World.NewMonoEntity(name: "Kind");
                setUp(mono.EntityData);
                World.Register(mono);
                return mono;
            }

            var identity = Make.Identity("Kind", Make.UnusedSet());
            setUp(identity);
            var runtime = new RuntimeGameplayAbilitySystem(identity);
            World.Register(runtime);
            return runtime;
        }

        private EntityIdentity Filled(out Item sword)
        {
            sword = Make.Item("Starting Sword");

            var identity = Make.Identity("Hero", Make.UnusedSet());
            identity.StartingAbilities.Add(Make.Ability("Starting Ability"));
            identity.StartingItems.Add(new StartingItemContainer { Item = sword });
            identity.AllowDuplicateAbilities = true;
            identity.AllowDuplicateItems = true;
            identity.AllowDuplicateEquippedItems = true;
            return identity;
        }
    }
}
