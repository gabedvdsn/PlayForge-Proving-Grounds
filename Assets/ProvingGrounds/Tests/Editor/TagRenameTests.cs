using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// ED-84, the user's report of 2026-11-11: updating an asset's tag, as the Problems window's fix or Identity's Update
    /// does, changed the asset's tag alone, so whatever stored the old one, as an attack's Asset Part finding Fury
    /// Swipes's stacks by its tag, found the asset no more and broke gameplay. The new tag reaches every PlayForge asset
    /// that stored the old one, with Undo; but where another asset still has the old tag as its own, as a duplicate's
    /// original does, what names it stays with it.
    /// </summary>
    public class TagRenameTests
    {
        private readonly List<string> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var path in made) AssetDatabase.DeleteAsset(path);
            made.Clear();
        }

        private T Save<T>(T asset, string name) where T : ScriptableObject
        {
            var path = $"Assets/ED-84 {name}.asset";
            AssetDatabase.CreateAsset(asset, path);
            made.Add(path);
            return asset;
        }

        private static Tag Stale(string name)
        {
            var tag = Tag.Generate(name);
            tag.DisplayName = name;
            return tag;
        }

        // An effect whose tag isn't the one its name gives, as the labs' generators made them
        private GameplayEffect Swipes(string file, string tag)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition.Name = "Fury Swipes";
            var tags = effect.Tags;
            tags.AssetTag = Stale(tag);
            effect.Tags = tags;
            effect.DurationSpecification = new GameplayEffectDuration { DurationPolicy = EEffectDurationPolicy.Durational };
            effect.ImpactSpecification = new GameplayEffectImpact();
            return Save(effect, file);
        }

        // An attack that reads the swipes' stacks by their tag, blocks them, and an ability that needs them
        private (GameplayEffect attack, Ability ability) Readers(Tag swipes)
        {
            var attack = ScriptableObject.CreateInstance<GameplayEffect>();
            attack.Definition.Name = "Ursa Attack";
            var tags = attack.Tags;
            tags.AssetTag = Stale("Effect.UrsaAttack");
            tags.BlocksEffectsTagged = new List<Tag> { swipes };
            attack.Tags = tags;
            attack.DurationSpecification = new GameplayEffectDuration { DurationPolicy = EEffectDurationPolicy.Instant };
            attack.ImpactSpecification = new GameplayEffectImpact
            {
                MagnitudeOperation = new ScalableFloat(new AssetPartKind { What = EAssetPartOf.Effect, Tagged = swipes, EffectPart = EEffectPart.Stacks }, 20f,
                    EMagnitudeOperation.MultiplyWithScaler),
            };
            Save(attack, "Ursa Attack");

            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "Maul";
            ability.Tags = new AbilityTags
            {
                AssetTag = Stale("Ability.Maul"),
                ContextTags = new List<Tag>(),
                PassiveGrantedTags = new List<Tag>(),
                ActiveGrantedTags = new List<Tag>(),
                TagRequirements = new AbilityTagRequirements
                {
                    SourceRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery> { new() { Tag = swipes } } },
                    TargetRequirements = new AvoidRequireTagGroup { AvoidTags = new List<TagQuery>(), RequireTags = new List<TagQuery>() },
                },
            };
            Save(ability, "Maul");
            return (attack, ability);
        }

        private static string Tagged(GameplayEffect attack) => ((AssetPartKind)attack.ImpactSpecification.MagnitudeOperation.Scaler).Tagged.Name;

        private static ForgeProblem StaleTag(Object asset) =>
            ForgeValidator.Check(asset).Single(problem => problem.Message.Contains("but its name gives"));

        /// <summary>The Problems window's fix carries the new tag to the attack's Asset Part, its Blocks, and the ability's requirement.</summary>
        [Test]
        public void ED_84_A_fixed_asset_tag_reaches_what_stores_it()
        {
            var swipes = Swipes("Fury Swipes", "Effect.Dota.FurySwipes");
            var (attack, ability) = Readers(swipes.Tags.AssetTag);

            var problem = StaleTag(swipes);
            Assert.AreEqual("Store the Tag", problem.Fix?.Name, "the Problems window's fix");
            Undo.IncrementCurrentGroup();
            problem.Fix.Apply();

            Assert.AreEqual("Effect.FurySwipes", swipes.Tags.AssetTag.Name, "the tag its name gives is stored");
            Assert.AreEqual("Effect.FurySwipes", Tagged(attack), "and the attack's Asset Part finds it by it");
            Assert.AreEqual("Effect.FurySwipes", attack.Tags.BlocksEffectsTagged[0].Name, "as its Blocks Effects Tagged does");
            Assert.AreEqual("Effect.FurySwipes", ability.Tags.TagRequirements.SourceRequirements.RequireTags[0].Tag.Name,
                "and the ability's requirement");
            Assert.AreEqual("FurySwipes", attack.Tags.BlocksEffectsTagged[0].DisplayName, "with its display name");

            Undo.PerformUndo();
            Assert.AreEqual("Effect.Dota.FurySwipes", Tagged(attack), "one Undo takes it all back");
        }

        /// <summary>
        /// A duplicate's name gives it a tag of its own, and what names the original's tag stays with the original, as the
        /// original still has it.
        /// </summary>
        [Test]
        public void ED_84_A_duplicate_leaves_what_names_its_original()
        {
            var original = Swipes("Fury Swipes", "Effect.FurySwipes");
            var (attack, _) = Readers(original.Tags.AssetTag);
            var copy = Swipes("Fury Swipes 2", "Effect.FurySwipes");
            copy.Definition.Name = "Fury Swipes Again";

            var problem = StaleTag(copy);
            Undo.IncrementCurrentGroup();
            problem.Fix.Apply();

            Assert.AreEqual("Effect.FurySwipesAgain", copy.Tags.AssetTag.Name, "the copy takes its own tag");
            Assert.AreEqual("Effect.FurySwipes", original.Tags.AssetTag.Name, "the original keeps its own");
            Assert.AreEqual("Effect.FurySwipes", Tagged(attack), "and what names it stays with it");
        }

        /// <summary>Identity's Update, the inspector's own way to store the tag, carries it to what stores it too.</summary>
        [Test]
        public void ED_84_Identitys_Update_reaches_what_stores_it_too()
        {
            var swipes = Swipes("Fury Swipes", "Effect.Dota.FurySwipes");
            var (attack, _) = Readers(swipes.Tags.AssetTag);
            var editor = UnityEditor.Editor.CreateEditor(swipes);
            try
            {
                var store = typeof(BasePlayForgeEditor).GetMethod("StoreAssetTag",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(store, "the inspector's Update");
                store.Invoke(editor, new object[]
                {
                    swipes, swipes.Definition.Name, "Effect", swipes.Tags.AssetTag,
                    (System.Action<Tag>)(tag => { var tags = swipes.Tags; tags.AssetTag = tag; swipes.Tags = tags; }),
                });
                Assert.AreEqual("Effect.FurySwipes", swipes.Tags.AssetTag.Name, "the tag its name gives is stored");
                Assert.AreEqual("Effect.FurySwipes", Tagged(attack), "and the attack's Asset Part follows it");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }
    }
}
