using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Cue tags (EP-47): a Cue Tag cue plays what its tag routes to, in place of naming what it shows, as Unreal's
    /// GameplayCue tags play their notifies. The project's cue library plays a tag's route, or its nearest parent's, a
    /// route that doesn't override playing its parent's too; an entity's Cue Receiver plays a tag its own way first, and
    /// its Variant picks the library's route under its name.
    /// </summary>
    public class CueRoutingTests : ProvingGroundsTest
    {
        private readonly List<string> heard = new();
        private readonly List<Object> made = new();

        // A cue that writes down each time it plays, by its own name, how, and the tag it was raised for
        private sealed class Noting : AbstractCueHandler
        {
            private readonly List<string> heard;
            private readonly string name;

            public Noting(List<string> heard, string name)
            {
                this.heard = heard;
                this.name = name;
            }

            public override void Burst(CueContext cue) => heard.Add($"{name} burst {cue.Tag.Path}");
            public override void Begin(CueContext cue) => heard.Add($"{name} begin {cue.Tag.Path}");
            public override void End(CueContext cue) => heard.Add($"{name} end {cue.Tag.Path}");
        }

        [SetUp]
        public void NoLibrary()
        {
            heard.Clear();
            ForgeCueRoutes.Library = null;
        }

        [TearDown]
        public void PutBack()
        {
            ForgeCueRoutes.Library = null;
            foreach (var each in made) if (each) Object.Destroy(each);
            made.Clear();
        }

        // A library of routes, each a tag, whether it overrides its parent's, and the name of the one cue it plays
        private CueLibrary Library(params (string Tag, bool Overrides, string Name)[] routes)
        {
            var library = ScriptableObject.CreateInstance<CueLibrary>();
            made.Add(library);
            foreach (var (tag, overrides, name) in routes)
                library.Routes.Add(Route(tag, name, overrides));
            return library;
        }

        private CueRoute Route(string tag, string name, bool overrides = true) =>
            new() { Tag = Tag.Generate(tag), Override = overrides, Handlers = new List<AbstractCueHandler> { new Noting(heard, name) } };

        // A burst raised on the entity by a Cue Tag cue
        private static void Raise(string tag, IGameplayAbilitySystem on) =>
            ForgeCues.Burst(new TagCue { Tag = Tag.Generate(tag) }, new CueContext(on, ECueMoment.Raised));

        // An entity on either hub whose identity holds the receiver, registered
        private IGameplayAbilitySystem Golem(Hub hub, CueReceiverEntityComponent receiver)
        {
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity("Golem", Make.UnusedSet());
                identity.Components.Add(receiver);
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                return runtime;
            }
            var asset = Make.Identity("Golem", Make.UnusedSet());
            asset.Components.Add(receiver);
            var host = new GameObject("Golem");
            made.Add(host);
            var mono = GameplayAbilitySystem.AddToGameObject(host, asset);
            mono.ProcessLifecycle = EProcessLifecycle.Synchronous;
            mono.ProcessTiming = EProcessStepTiming.UpdateAndLate;
            World.Register(mono);
            return mono;
        }

        /// <summary>
        /// A cue tag plays its library route, or else its nearest parent's, and a route that doesn't override plays its
        /// parent's after it (EP-47).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_47_A_cue_tag_plays_its_route_or_its_nearest_parents() => Live(async () =>
        {
            ForgeCueRoutes.Library = Library(("Test.Cue.Hit", true, "hit"), ("Test.Cue.Hit.Fire", false, "fire"), ("Test.Cue", true, "any"));
            var gas = World.Entity(Hub.Runtime);
            await Frames.Step();

            Raise("Test.Cue.Hit.Fire", gas);
            Assert.That(heard, Is.EqualTo(new[] { "fire burst Test.Cue.Hit.Fire", "hit burst Test.Cue.Hit.Fire" }),
                "its own route, which doesn't override, then its parent's, which does");
            heard.Clear();
            Raise("Test.Cue.Hit.Ice", gas);
            Assert.That(heard, Is.EqualTo(new[] { "hit burst Test.Cue.Hit.Ice" }), "a tag with no route plays its nearest parent's");
            heard.Clear();
            Raise("Test.Elsewhere", gas);
            Assert.That(heard, Is.Empty, "and a tag under no route plays nothing");
        });

        /// <summary>A lasting cue tag begins and ends through its route, with the key its beginning and end share (EP-47).</summary>
        [UnityTest]
        public IEnumerator EP_47_A_lasting_cue_tag_begins_and_ends_through_its_route() => Live(async () =>
        {
            ForgeCueRoutes.Library = Library(("Test.Cue.Hit", true, "hit"));
            var gas = World.Entity(Hub.Runtime);
            await Frames.Step();
            var cue = new TagCue { Tag = Tag.Generate("Test.Cue.Hit") };
            var key = new object();

            ForgeCues.Begin(cue, new CueContext(gas, ECueMoment.Raised, key: key));
            ForgeCues.End(cue, new CueContext(gas, ECueMoment.Raised, key: key));

            Assert.That(heard, Is.EqualTo(new[] { "hit begin Test.Cue.Hit", "hit end Test.Cue.Hit" }));
        });

        /// <summary>An effect's Cue Tag cue plays its route as the effect lands, lasts and comes off (EP-47).</summary>
        [UnityTest]
        public IEnumerator EP_47_An_effects_cue_tag_plays_its_route([Values] Hub hub) => Live(async () =>
        {
            ForgeCueRoutes.Library = Library(("Test.Cue.Hit", true, "hit"));
            var gas = World.Entity(hub);
            var aura = Make.Effect("Aura", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = null });
            aura.Cues.Add(new TagCue { Tag = Tag.Generate("Test.Cue.Hit") });

            Assert.IsTrue(World.Apply(gas, gas, aura), "an aura lands");
            await Frames.Step();
            gas.RemoveGameplayEffect(aura);
            await Frames.Step(2);

            Assert.That(heard, Is.EqualTo(new[]
            {
                "hit burst Test.Cue.Hit", "hit begin Test.Cue.Hit", "hit burst Test.Cue.Hit", "hit end Test.Cue.Hit",
            }), "its route plays its landing, its lasting and its coming off");
        });

        /// <summary>
        /// An entity's Cue Receiver plays a tag its own way first, in place of the library's, or before it where it passes
        /// it through; an entity without one plays the library's (EP-47).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_47_An_entitys_receiver_plays_its_own_way_first([Values] Hub hub) => Live(async () =>
        {
            ForgeCueRoutes.Library = Library(("Test.Cue.Hit", true, "hit"));
            var receiver = new CueReceiverEntityComponent { Routes = new List<CueRoute> { Route("Test.Cue.Hit", "golem's own") } };
            var golem = Golem(hub, receiver);
            var plain = World.Entity(hub);
            await Frames.Step();

            Raise("Test.Cue.Hit", golem);
            Assert.That(heard, Is.EqualTo(new[] { "golem's own burst Test.Cue.Hit" }), "the golem's own way, in place of the library's");
            heard.Clear();
            receiver.PassThrough = true;
            Raise("Test.Cue.Hit", golem);
            Assert.That(heard, Is.EqualTo(new[] { "golem's own burst Test.Cue.Hit", "hit burst Test.Cue.Hit" }),
                "passed through, the library's after its own");
            heard.Clear();
            Raise("Test.Cue.Hit", plain);
            Assert.That(heard, Is.EqualTo(new[] { "hit burst Test.Cue.Hit" }), "and an entity without one plays the library's");
        });

        /// <summary>
        /// An entity's Variant plays the library's route under its name in place of the tag's own, as Cue.Hit.Golem for
        /// Cue.Hit, at each step up the tag's parents, where the library has one (EP-47).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_47_A_variant_plays_the_library_routes_under_its_name([Values] Hub hub) => Live(async () =>
        {
            ForgeCueRoutes.Library = Library(("Test.Cue.Hit", true, "hit"), ("Test.Cue.Hit.Golem", true, "golem hit"));
            var golem = Golem(hub, new CueReceiverEntityComponent { Variant = Tag.Generate("Test.Variant.Golem") });
            var plain = World.Entity(hub);
            await Frames.Step();

            Raise("Test.Cue.Hit", golem);
            Assert.That(heard, Is.EqualTo(new[] { "golem hit burst Test.Cue.Hit" }), "the golem's variant of the tag's route");
            heard.Clear();
            Raise("Test.Cue.Hit.Fire", golem);
            Assert.That(heard, Is.EqualTo(new[] { "golem hit burst Test.Cue.Hit.Fire" }), "and of its nearest parent's");
            heard.Clear();
            Raise("Test.Cue.Hit", plain);
            Assert.That(heard, Is.EqualTo(new[] { "hit burst Test.Cue.Hit" }), "while an entity without one plays the tag's own");
        });

        /// <summary>The running project's settings give the library, where code gives none (EP-47).</summary>
        [UnityTest]
        public IEnumerator EP_47_The_running_projects_settings_give_the_library() => Live(async () =>
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            made.Add(project);
            project.Settings.Cues = Library(("Test.Cue.Hit", true, "hit"));
            var was = ForgeProject.Use(project);
            try
            {
                var gas = World.Entity(Hub.Runtime);
                await Frames.Step();
                Raise("Test.Cue.Hit", gas);
                Assert.That(heard, Is.EqualTo(new[] { "hit burst Test.Cue.Hit" }), "the project's library routes the tag");
            }
            finally
            {
                ForgeProject.Use(was);
            }
        });
    }
}
