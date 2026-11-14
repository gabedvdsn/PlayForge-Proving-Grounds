using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-24, Decision 64: The Forge's presets. Each built-in preset makes its asset ready to fill in, reading the project's
    /// roles and crowd control where it needs them; a Forge Project's own template assets are presets too; and The Forge's
    /// Create menu lists them under each type.
    /// </summary>
    public class PresetEditorTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Folder = "Assets/TW24 Presets";
        private ForgeProject project, previous;
        private bool used;
        private ForgeAttribute health, mana;
        private Tag stunned;

        [SetUp]
        public void MakeProject()
        {
            Assume.That(AssetDatabase.IsValidFolder(Folder), Is.False, $"{Folder} is free");
            AssetDatabase.CreateFolder("Assets", "TW24 Presets");
            project = ScriptableObject.CreateInstance<ForgeProject>();
            AssetDatabase.CreateAsset(project, $"{Folder}/Forge Project.asset");
            health = Saved(Attribute("Health"));
            mana = Saved(Attribute("Mana"));
            project.Settings.Roles.Add(new AttributeRole { Role = Tags.ROLE_HEALTH, Attribute = health });
            project.Settings.Roles.Add(new AttributeRole { Role = Tag.Generate("Test.TW24.Role.Mana"), Attribute = mana });
            stunned = Tag.Generate("Test.TW24.Stunned");
            var rules = Saved(ScriptableObject.CreateInstance<CrowdControlRules>(), "Crowd Control");
            rules.Rules.Add(new CrowdControlRule { Tag = Tag.Generate("Test.TW24.Silenced"), Stops = ECrowdControlStops.AbilitiesTagged });
            rules.Rules.Add(new CrowdControlRule { Tag = stunned, Stops = ECrowdControlStops.EveryAbility });
            project.Settings.CrowdControl = rules;
            EditorUtility.SetDirty(project);
            previous = PlayForgeSettings.UseProject(project);
            used = true;
        }

        [TearDown]
        public void Clean()
        {
            if (used) PlayForgeSettings.UseProject(previous);
            used = false;
            AssetDatabase.DeleteAsset(Folder);
        }

        private static ForgeAttribute Attribute(string name)
        {
            var attribute = ScriptableObject.CreateInstance<ForgeAttribute>();
            attribute.name = attribute.Name = name;
            attribute.Textures = new List<TextureItem>();
            return attribute;
        }

        private static T Saved<T>(T asset, string name = null) where T : ScriptableObject
        {
            AssetDatabase.CreateAsset(asset, $"{Folder}/{name ?? asset.name}.asset");
            return asset;
        }

        private static ForgePreset Preset<T>(string name) where T : ScriptableObject
        {
            var preset = ForgePresets.For(typeof(T)).FirstOrDefault(each => each.Name == name);
            Assert.IsNotNull(preset, $"a {name} preset");
            return preset;
        }

        private static T Make<T>(string preset, string name) where T : ScriptableObject
        {
            var asset = ForgePresets.Make(Preset<T>(preset), name, Folder) as T;
            Assert.IsNotNull(asset, $"{preset} makes a {typeof(T).Name}");
            Assert.That(AssetDatabase.GetAssetPath(asset), Does.StartWith(Folder), "saved where it's asked");
            return asset;
        }

        /// <summary>The effects: each with its impact, duration and sides set, the project's Health hit where it hits.</summary>
        [Test]
        public void TW_24_The_effect_presets_make_their_effects_ready_to_fill()
        {
            var damage = Make<GameplayEffect>("Damage", "Fire Bolt");
            Assert.AreEqual("Fire Bolt", damage.GetName(), "named as asked");
            Assert.AreEqual(EEffectDurationPolicy.Instant, damage.DurationSpecification.DurationPolicy, "Damage lands once");
            Assert.AreSame(health, damage.ImpactSpecification.AttributeTarget, "on the project's Health");
            Assert.Less(damage.ImpactSpecification.MagnitudeOperation.Magnitude, 0f, "lowering it");
            Assert.AreEqual(EAffiliationPolicy.Unaffiliated, damage.ImpactSpecification.AffiliationPolicy, "on those of other sides");

            var burn = Make<GameplayEffect>("Damage over Time", "Burn");
            Assert.AreEqual(EEffectDurationPolicy.Durational, burn.DurationSpecification.DurationPolicy);
            Assert.IsTrue(burn.DurationSpecification.EnablePeriodicTicks, "a DoT ticks");
            Assert.Greater(burn.DurationSpecification.TicksOperation.Magnitude, 0, "a number of times");
            Assert.AreSame(health, burn.ImpactSpecification.AttributeTarget);

            var buff = Make<GameplayEffect>("Buff", "Haste");
            Assert.AreEqual(EEffectDurationPolicy.Durational, buff.DurationSpecification.DurationPolicy);
            Assert.IsFalse(buff.DurationSpecification.EnablePeriodicTicks, "a buff holds while it lasts");
            Assert.Greater(buff.ImpactSpecification.MagnitudeOperation.Magnitude, 0f);
            Assert.AreEqual(EAffiliationPolicy.Affiliated, buff.ImpactSpecification.AffiliationPolicy, "on its own side");

            var debuff = Make<GameplayEffect>("Debuff", "Slow");
            Assert.Less(debuff.ImpactSpecification.MagnitudeOperation.Magnitude, 0f);
            Assert.AreEqual(EAffiliationPolicy.Unaffiliated, debuff.ImpactSpecification.AffiliationPolicy);

            var stun = Make<GameplayEffect>("Stun", "Bash");
            Assert.AreEqual(EEffectDurationPolicy.Durational, stun.DurationSpecification.DurationPolicy);
            Assert.That(stun.Tags.GrantedTags, Has.Member(stunned), "granting the tag of the project's rule that stops every ability");

            var aura = Make<GameplayEffect>("Aura", "Command");
            Assert.AreEqual(EEffectDurationPolicy.Infinite, aura.DurationSpecification.DurationPolicy);
            Assert.AreEqual(1, aura.Components.OfType<AuraEffectComponent>().Count(), "an Aura component to fill in");
        }

        /// <summary>The items: an On Hit to fill in; an ability of its own, with a cost and a cooldown.</summary>
        [Test]
        public void TW_24_The_item_presets_make_their_items_ready_to_fill()
        {
            var blade = Make<Item>("On-Hit", "Venom Blade");
            Assert.AreEqual(1, blade.OnHit.Count, "an On Hit entry");
            Assert.AreEqual(EOnHitTo.Target, blade.OnHit[0].To);

            var wand = Make<Item>("Active", "Blink Wand");
            var use = wand.ActiveAbility;
            Assert.IsNotNull(use, "an ability it grants");
            Assert.IsNotEmpty(AssetDatabase.GetAssetPath(use), "saved as an asset");
            Assert.IsNotNull(use.Cost, "with a cost");
            Assert.IsNotNull(use.Cooldown, "and a cooldown");
        }

        /// <summary>The passive: a trigger on a gameplay event, and a stage that applies its effects.</summary>
        [Test]
        public void TW_24_The_passive_preset_makes_a_triggered_ability()
        {
            var thorns = Make<Ability>("Passive (Triggered)", "Spiked Hide");
            Assert.AreEqual(1, thorns.Definition.Triggers.Count, "a trigger");
            Assert.AreEqual(EAbilityTriggerSource.GameplayEvent, thorns.Definition.Triggers[0].Source, "on a gameplay event");
            Assert.That(thorns.Behaviour.Stages.SelectMany(stage => stage.Tasks), Has.Some.InstanceOf<ApplyEffectsAbilityTask>(), "applying its effects");
        }

        /// <summary>The hero: an attribute set of its own, of the attributes playing the project's roles, Health first.</summary>
        [Test]
        public void TW_24_The_hero_preset_makes_an_entity_with_its_attributes()
        {
            var hero = Make<EntityIdentity>("Hero", "Knight");
            var set = hero.AttributeSet;
            Assert.IsNotNull(set, "an attribute set");
            Assert.IsNotEmpty(AssetDatabase.GetAssetPath(set), "saved as an asset");
            Assert.That(set.Attributes.Select(element => element.Attribute), Is.EqualTo(new[] { health, mana }), "Health, then the project's other roles");
            Assert.Greater(set.Attributes[0].Base.Magnitude, 0f, "Health starts above 0");
        }

        /// <summary>A project's own template asset is a preset too: a new asset starts as its copy, with its own name and tag.</summary>
        [Test]
        public void TW_24_A_projects_templates_are_presets_too()
        {
            var template = Make<GameplayEffect>("Damage", "Frost Template");
            template.DurationSpecification.DurationPolicy = EEffectDurationPolicy.Durational;
            template.DurationSpecification.DurationOperation = new ScalableFloat(7f);
            project.Settings.Authoring.Templates.Add(template);

            var names = ForgePresets.For(typeof(GameplayEffect)).Select(preset => preset.Name).ToList();
            Assert.That(names, Has.Member("Frost Template"), "the project's template is listed");
            Assert.Greater(names.IndexOf("Frost Template"), names.IndexOf("Aura"), "after the built-ins");

            var frostbite = Make<GameplayEffect>("Frost Template", "Frostbite");
            Assert.AreEqual(7f, frostbite.DurationSpecification.DurationOperation.Magnitude, 0.01f, "a copy of the template");
            Assert.AreEqual("Frostbite", frostbite.GetName(), "named its own");
            Assert.AreNotEqual(template.Tags.AssetTag, frostbite.Tags.AssetTag, "with its own tag");
        }

        /// <summary>The Forge's Create menu lists each type's presets under it, Blank first, the project's templates after.</summary>
        [UnityTest]
        public IEnumerator TW_24_The_Forges_Create_menu_lists_the_presets()
        {
            var template = Make<GameplayEffect>("Damage", "Storm Template");
            project.Settings.Authoring.Templates.Add(template);

            // A batch run with no graphics device says so as a window opens; any other error fails the test
            var errors = new List<string>();
            void Take(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith("No graphic device is available"))
                    errors.Add($"{type}: {message}");
            }
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Take;
            TheForge forge = null;
            try
            {
                forge = (TheForge)typeof(TheForge).GetMethod("Open", AnyStatic).Invoke(null, null);
                yield return null;
                var create = forge.rootVisualElement.Query<ToolbarMenu>().ToList().FirstOrDefault(menu => menu.name == "Create");
                Assert.IsNotNull(create, "The Forge has Create");
                var items = create.menu.MenuItems().OfType<DropdownMenuAction>().Select(action => action.name).ToList();
                Assert.That(items, Is.SupersetOf(new[]
                {
                    "Gameplay Effect/Blank", "Gameplay Effect/Damage", "Gameplay Effect/Damage over Time", "Gameplay Effect/Buff",
                    "Gameplay Effect/Debuff", "Gameplay Effect/Stun", "Gameplay Effect/Aura", "Gameplay Effect/Storm Template",
                    "Item/On-Hit", "Item/Active", "Ability/Passive (Triggered)", "Entity/Hero", "Attribute",
                }), $"each type's presets: {string.Join(", ", items)}");
            }
            finally
            {
                if (forge) forge.Close();
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.That(errors, Is.Empty, "no error while The Forge was open");
        }
    }
}
