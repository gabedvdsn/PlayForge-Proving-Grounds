using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// An entity's and an item's cues (EP-50): an entity's Cues component plays as it spawns, dies and revives, and While
    /// Tagged for as long as it holds a tag; an item's plays on its holder as it's equipped, while it's equipped, and as
    /// it's used.
    /// </summary>
    public class EntityItemCueTests : ProvingGroundsTest
    {
        private readonly List<(string Line, CueContext Cue)> heard = new();
        private readonly List<Object> made = new();

        // A cue that writes down each time it plays, by its own name, how, and what raised it
        private sealed class Noting : AbstractCueHandler
        {
            private readonly List<(string, CueContext)> heard;
            private readonly string name;

            public Noting(List<(string, CueContext)> heard, string name)
            {
                this.heard = heard;
                this.name = name;
            }

            public override void Burst(CueContext cue) => heard.Add(($"{name} burst {cue.Moment}", cue));
            public override void Begin(CueContext cue) => heard.Add(($"{name} begin {cue.Moment}", cue));
            public override void End(CueContext cue) => heard.Add(($"{name} end {cue.Moment}", cue));
        }

        [SetUp]
        public void Clear() => heard.Clear();

        [TearDown]
        public void PutBack()
        {
            foreach (var each in made) if (each) Object.Destroy(each);
            made.Clear();
        }

        private List<AbstractCueHandler> Noted(string name) => new() { new Noting(heard, name) };

        private IEnumerable<string> Lines => heard.Select(each => each.Line);

        // An entity on either hub whose identity holds the component and stays as it dies, registered
        private IGameplayAbilitySystem Entity(Hub hub, AbstractEntityComponent component)
        {
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity("Cued", Make.UnusedSet());
                identity.Components.Add(component);
                identity.Death.Policy = EDeathPolicy.Stay;
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                return runtime;
            }
            var asset = Make.Identity("Cued", Make.UnusedSet());
            asset.Components.Add(component);
            asset.Death.Policy = EDeathPolicy.Stay;
            var host = new GameObject("Cued");
            made.Add(host);
            var mono = GameplayAbilitySystem.AddToGameObject(host, asset);
            mono.ProcessLifecycle = EProcessLifecycle.Synchronous;
            mono.ProcessTiming = EProcessStepTiming.UpdateAndLate;
            World.Register(mono);
            return mono;
        }

        /// <summary>An entity's Cues component plays as it spawns, dies and revives, on the entity (EP-50).</summary>
        [UnityTest]
        public IEnumerator EP_50_An_entitys_cues_play_as_it_spawns_dies_and_revives([Values] Hub hub) => Live(async () =>
        {
            var entity = Entity(hub, new CuesEntityComponent { OnSpawn = Noted("spawn"), OnDeath = Noted("death"), OnRevive = Noted("revive") });
            await Frames.Step();
            Assert.That(Lines, Is.EqualTo(new[] { "spawn burst Spawned" }), "it spawns");

            heard.Clear();
            entity.GetActionQueue().Enqueue(new DeathAction(entity));
            await Frames.Step(2);
            Assert.IsTrue(entity.IsDead, "it dies");
            Assert.That(Lines, Is.EqualTo(new[] { "death burst Died" }), "and plays its death");

            heard.Clear();
            Assert.IsTrue(entity.Revive(), "it revives");
            await Frames.Step(2);
            Assert.That(Lines, Is.EqualTo(new[] { "revive burst Revived" }), "and plays its revival");
            Assert.AreSame(entity, heard[0].Cue.Target, "each on the entity");
        });

        /// <summary>
        /// An entity's While Tagged cues begin as it takes the tag on and end as it lets it go, sharing a key (EP-50).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_50_While_tagged_cues_last_as_the_entity_holds_its_tag([Values] Hub hub) => Live(async () =>
        {
            var stunned = Tag.Generate("Test.Status.Stunned");
            var entity = Entity(hub, new CuesEntityComponent
            {
                WhileTagged = new List<TaggedCues> { new() { Tag = stunned, Cues = Noted("stars") } },
            });
            var stun = Make.Effect("Stun", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null, GrantedTags = new List<Tag> { stunned } });
            await Frames.Step();
            Assert.That(Lines, Is.Empty, "nothing plays untagged");

            Assert.IsTrue(World.Apply(entity, entity, stun), "a stun lands");
            await Frames.Step(2);
            Assert.That(Lines, Is.EqualTo(new[] { "stars begin Tagged" }), "its stars begin as it's stunned");

            entity.RemoveGameplayEffect(stun);
            await Frames.Step(2);
            Assert.That(Lines, Is.EqualTo(new[] { "stars begin Tagged", "stars end Tagged" }), "and end as the stun goes");
            Assert.AreSame(heard[0].Cue.Key, heard[1].Cue.Key, "its beginning and its end share a key");
            Assert.AreSame(entity, heard[0].Cue.Target, "on the entity");
        });

        /// <summary>
        /// An item's Cues component plays on its holder as it's equipped, lasts while it's equipped, and plays as it's
        /// used, as the ability it lends is (EP-50).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_50_An_items_cues_play_as_its_equipped_used_and_taken_off([Values] Hub hub) => Live(async () =>
        {
            var swing = Make.Ability("Swing", Make.Stage(new ProbeAbilityTask(World, "Swing Task")));
            var sword = Make.Item("Sword");
            sword.ActiveAbility = swing;
            sword.Components.Add(new CuesItemComponent { OnEquip = Noted("equip"), WhileEquipped = Noted("equipped"), OnUse = Noted("use") });
            var holder = World.Entity(hub, name: "Holder");
            var items = holder.GetItemSystem();
            Assert.IsTrue(items.GiveItem(sword, out int index), "the sword is given");

            Assert.IsTrue(items.EquipItem(index), "and equipped");
            await Frames.Step();
            Assert.That(Lines, Is.EqualTo(new[] { "equip burst Equipped", "equipped begin Equipped" }), "its equip plays, and its lasting cue begins");
            Assert.AreSame(holder, heard[0].Cue.Target, "on its holder");

            heard.Clear();
            Assert.IsTrue(holder.GetAbilitySystem().TryActivateAbility(swing), "its swing activates");
            await Frames.Until(() => Ledger.Cleans("Swing Task") == 1, 10, "the swing ran");
            await Frames.Step(2);
            Assert.That(Lines, Is.EqualTo(new[] { "use burst Used" }), "and its use plays");

            heard.Clear();
            Assert.IsTrue(items.UnequipItem(index), "it's taken off");
            await Frames.Step();
            Assert.That(Lines, Is.EqualTo(new[] { "equipped end Equipped" }), "and its lasting cue ends");
        });
    }
}
