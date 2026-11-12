using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// TW-10, Decision 50: auras. An aura holds its effect on each it takes near its holder, of the sides it takes, its
    /// holder too where it says, and takes it off as they leave or as the aura goes: an item's while it's equipped, an
    /// effect's while it lasts, an entity's as it lives. It checks who's near at its interval.
    /// </summary>
    public class AuraTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag red, blue, inspired;

        private sealed class SteppingClock : IForgeClock
        {
            public float Step;
            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        private IForgeClock previousClock;
        private bool clockSet;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            red = Make.NewTag("Test.TW10.Red");
            blue = Make.NewTag("Test.TW10.Blue");
            inspired = Make.NewTag("Test.TW10.Inspired");
        }

        [TearDown]
        public void PutBackTime()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            clockSet = false;
        }

        // An entity of a side, at a point, on either hub, with any auras of its own
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, Vector3 at, params AuraEntry[] own)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                identity.Auras = new List<AuraEntry>(own);
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                mono.EntityData.Auras = new List<AuraEntry>(own);
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = at;
            return gas;
        }

        private AuraEntry Inspiring(float interval = 0f) => new()
        {
            Effect = Make.Effect("Inspire", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, GrantedTags = new List<Tag> { inspired } }),
            Radius = 5f, Takes = EAffinityFilter.Allies, IncludeHolder = true, Interval = interval,
        };

        private bool Inspired(IGameplayAbilitySystem gas) => gas.GetTagCache().GetWeight(inspired) > 0;

        /// <summary>An item's aura holds on its holder's allies near it while it's equipped, and comes off as they leave or as it goes.</summary>
        [UnityTest]
        public IEnumerator TW_10_An_items_aura_holds_on_those_near_its_holder([Values] Hub hub) => Live(async () =>
        {
            var holder = At(hub, "Holder", red, Vector3.zero);
            var ally = At(hub, "Ally", red, new Vector3(3f, 0f, 0f));
            var enemy = At(hub, "Enemy", blue, new Vector3(2f, 0f, 0f));
            var straggler = At(hub, "Straggler", red, new Vector3(9f, 0f, 0f));
            var banner = Make.Item("Banner");
            banner.Auras.Add(Inspiring());
            await Frames.Step();

            Assert.That(holder.GetItemSystem().GiveAndEquipItem(banner, out _), "the banner's equipped");
            await Frames.Step(2);
            Assert.IsTrue(Inspired(holder), "its holder, as it asks");
            Assert.IsTrue(Inspired(ally), "an ally near it");
            Assert.IsFalse(Inspired(enemy), "not an enemy");
            Assert.IsFalse(Inspired(straggler), "nor an ally out of reach");

            ally.GetTargetingPacket().position = new Vector3(8f, 0f, 0f);
            straggler.GetTargetingPacket().position = new Vector3(4f, 0f, 0f);
            await Frames.Step(2);
            Assert.IsFalse(Inspired(ally), "an ally that leaves loses it");
            Assert.IsTrue(Inspired(straggler), "and one that comes near takes it");

            Assert.That(holder.GetItemSystem().RemoveItem(banner), "the banner's taken off");
            await Frames.Step(2);
            Assert.IsFalse(Inspired(holder), "its holder loses it as it goes");
            Assert.IsFalse(Inspired(straggler), "and so does each it held");
        });

        /// <summary>An aura checks who's near at its interval, by its holder's time.</summary>
        [UnityTest]
        public IEnumerator TW_10_An_aura_checks_at_its_interval([Values] Hub hub) => Live(async () =>
        {
            var clock = new SteppingClock { Step = 0.1f };
            previousClock = ForgeTime.Use(clock);
            clockSet = true;
            var holder = At(hub, "Holder", red, Vector3.zero, Inspiring(interval: 0.5f));
            var ally = At(hub, "Ally", red, new Vector3(3f, 0f, 0f));
            await Frames.Step(2);
            Assert.IsTrue(Inspired(ally), "an entity's own aura holds as it lives, from its first check");

            ally.GetTargetingPacket().position = new Vector3(8f, 0f, 0f);
            await Frames.Step(1);
            Assert.IsTrue(Inspired(ally), "a tenth of a second on, it hasn't checked again");
            await Frames.Step(6);
            Assert.IsFalse(Inspired(ally), "half a second on, it has");
        });

        /// <summary>An effect's aura holds on those near the one it's on while it lasts.</summary>
        [UnityTest]
        public IEnumerator TW_10_An_effects_aura_holds_while_it_lasts([Values] Hub hub) => Live(async () =>
        {
            var holder = At(hub, "Holder", red, Vector3.zero);
            var ally = At(hub, "Ally", red, new Vector3(3f, 0f, 0f));
            var war = Make.Effect("War Cry", new EffectConfig { Policy = EEffectDurationPolicy.Infinite });
            war.Auras.Add(Inspiring());
            await Frames.Step();

            Assert.IsTrue(World.Apply(holder, holder, war), "the war cry's on its holder");
            await Frames.Step(2);
            Assert.IsTrue(Inspired(ally), "its aura holds on an ally near");

            Assert.IsTrue(holder.GetEffectSystem().RemoveGameplayEffect(war), "the war cry ends");
            await Frames.Step(3);
            Assert.IsFalse(Inspired(ally), "and its aura comes off");
        });
    }
}
