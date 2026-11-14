using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// An effect's texture from its origin (FN-25, Decision 83): under its Definition's Inherit From Origin, a spec's icon is
    /// its origin's texture under Origin Texture, Primary to start, the ability, item or entity that applied it, and its own
    /// where the origin has none of that tag; without it, its own.
    /// </summary>
    public class EffectTextureTests : ProvingGroundsTest
    {
        private readonly List<Object> made = new();
        private ForgeAttribute health;

        [SetUp]
        public void Attributes() => health = Make.Attribute("Health");

        [TearDown]
        public void Clean()
        {
            foreach (var each in made)
                if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private Texture2D Texture(string name)
        {
            var texture = new Texture2D(2, 2) { name = name };
            made.Add(texture);
            return texture;
        }

        private static TextureItem Slot(Tag tag, Texture2D texture) => new() { Tag = tag, Texture = texture };

        // A lasting effect with a Primary texture of its own
        private GameplayEffect Mark(Texture2D own)
        {
            var mark = Make.Effect("Mark", new EffectConfig { Policy = EEffectDurationPolicy.Infinite, Target = health, Magnitude = 0f });
            mark.Definition.Textures = new List<TextureItem> { Slot(Tags.PRIMARY, own) };
            return mark;
        }

        private static GameplayEffectSpec SpecOn(IGameplayAbilitySystem holder, GameplayEffect effect)
        {
            foreach (var container in holder.GetEffectSystem().Shelf)
                if (container?.Spec?.Base == effect) return container.Spec;
            Assert.Fail($"{effect.GetName()} is on {holder}");
            return null;
        }

        [UnityTest]
        public IEnumerator FN_25_An_effect_takes_its_abilitys_texture_by_its_tag([Values] Hub hub) => Live(async () =>
        {
            var own = Texture("Own");
            var fire = Texture("Fireball");
            var glow = Texture("Fireball Glow");
            var glowing = Make.NewTag("Test.FN25.Glow");
            var mark = Mark(own);

            // With no targeting, implicit targeting makes the caster the target, the ability the effect's origin
            var ability = Make.Ability("Fireball", Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { mark } }));
            ability.Definition.Textures = new List<TextureItem> { Slot(Tags.PRIMARY, fire), Slot(glowing, glow) };
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "activated");
            await Frames.Step(5);
            var spec = SpecOn(caster, mark);

            Assert.AreSame(own, spec.GetIcon(), "its own, as it inherits nothing");
            mark.Definition.InheritFromOrigin = true;
            Assert.AreEqual(Tags.PRIMARY, mark.Definition.OriginTexture, "an effect inherits its origin's Primary to start");
            Assert.AreSame(fire, spec.GetIcon(), "its ability's Primary");
            mark.Definition.OriginTexture = glowing;
            Assert.AreSame(glow, spec.GetIcon(), "or its texture under another tag");
            mark.Definition.OriginTexture = Make.NewTag("Test.FN25.Missing");
            Assert.AreSame(own, spec.GetIcon(), "and its own where its ability has none under the tag");
        });

        [UnityTest]
        public IEnumerator FN_25_An_effect_takes_its_entitys_texture([Values] Hub hub) => Live(async () =>
        {
            var own = Texture("Own");
            var crest = Texture("Crest");
            var mark = Mark(own);
            mark.Definition.InheritFromOrigin = true;
            var source = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Source");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            var textures = new List<TextureItem> { Slot(Tags.PRIMARY, crest) };
            switch (source)
            {
                case RuntimeGameplayAbilitySystem runtime: runtime.EntityData.Textures = textures; break;
                case GameplayAbilitySystem mono: mono.EntityData.Textures = textures; break;
                default: Assert.Fail($"an entity of a hub the test knows: {source.GetType().Name}"); break;
            }
            await Frames.Step();

            Assert.IsTrue(World.Apply(source, target, mark), "the mark lands");
            await Frames.Step(2);
            Assert.AreSame(crest, SpecOn(target, mark).GetIcon(), "its entity's Primary, where an entity applied it");
        });
    }
}
