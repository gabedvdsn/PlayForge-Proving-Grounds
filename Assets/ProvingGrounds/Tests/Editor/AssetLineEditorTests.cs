using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-21, Decision 61: one line for each asset. An ability's reads its numbers by level, each effect's main numbers and
    /// then its costs and cooldown; an item's what it changes while equipped and the ability it grants; an entity's its
    /// first attributes' starting values and its abilities; an effect's its first impact by level. The inspector's header
    /// shows the line, and The Forge's list a Summary column, sorting by it.
    /// </summary>
    public class AssetLineEditorTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<Object> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        private T Instance<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            made.Add(instance);
            return instance;
        }

        private Attribute Named(string name)
        {
            var attribute = Instance<Attribute>();
            attribute.Name = name;
            attribute.name = name;
            return attribute;
        }

        private Ability AbilityNamed(string name, int maxLevel = 3)
        {
            var ability = Instance<Ability>();
            ability.Definition.Name = name;
            ability.StartingLevel = 1;
            ability.MaxLevel = maxLevel;
            return ability;
        }

        // A number by level, read from its table at each level
        private static ScalableFloat ByLevel(params float[] values) => values.Length == 1
            ? new ScalableFloat(values[0])
            : new ScalableFloat(new SimpleScaler { LevelValues = values, StretchOverLevels = false }, 1f, EMagnitudeOperation.UseScaler);

        // An effect whose one impact adds to an attribute, its numbers by level, lasting as it says
        private GameplayEffect Effect(EEffectDurationPolicy policy, Attribute attribute, float[] impact, float[] duration = null)
        {
            var effect = Instance<GameplayEffect>();
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = attribute,
                MagnitudeOperation = ByLevel(impact),
                ImpactTypes = new List<Tag>(),
                Packets = System.Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = policy,
                DurationOperation = duration != null ? ByLevel(duration) : new ScalableFloat(0f),
                TicksOperation = new ScalableInt(0),
                TickIntervalOperation = new ScalableFloat(1f),
                StackingConfigs = new List<AbstractStackingConfig>(),
            };
            return effect;
        }

        private static AbilityBehaviour Applying(params GameplayEffect[] effects) => new()
        {
            Stages = new List<AbilityTaskBehaviourStage>
            {
                new() { Tasks = new List<AbstractAbilityTask> { new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect>(effects) } } },
            },
        };

        private static AttributeSetElement Element(Attribute attribute, float value) => new()
        {
            Attribute = attribute,
            Current = new AttributeMagnitudeSpec { Magnitude = value, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            Base = new AttributeMagnitudeSpec { Magnitude = value, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            Overflow = new AttributeOverflowData { Policy = EAttributeOverflowPolicy.ZeroToBase },
            RetentionGroup = Tags.DEFAULT,
            Constraints = new AttributeConstraints { AutoClamp = true, RoundingMode = EAttributeRoundingPolicy.None },
        };

        private Ability Bolt()
        {
            var health = Named("Health");
            var mana = Named("Mana");
            var ability = AbilityNamed("Bolt");
            ability.Behaviour = Applying(Effect(EEffectDurationPolicy.Instant, health, new[] { -450f, -600f, -750f }));
            ability.Cost = Effect(EEffectDurationPolicy.Instant, mana, new[] { -280f, -420f, -680f });
            ability.Cooldown = Effect(EEffectDurationPolicy.Durational, null, new[] { 0f }, new[] { 70f, 60f, 50f });
            return ability;
        }

        /// <summary>An ability's line: its effect's numbers by level, then its cost, then its cooldown.</summary>
        [Test]
        public void TW_21_An_abilitys_line_reads_its_numbers_by_level()
        {
            Assert.AreEqual("-450/-600/-750 Health; 280/420/680 Mana; 70/60/50 s", ForgeAssetLines.Of(Bolt()));
        }

        /// <summary>Its attribute costs join its line, after a Cost effect's, each by level.</summary>
        [Test]
        public void TW_21_An_abilitys_attribute_costs_join_its_line()
        {
            var health = Named("Health");
            var mana = Named("Mana");
            var strike = AbilityNamed("Strike");
            strike.Behaviour = Applying(Effect(EEffectDurationPolicy.Instant, health, new[] { -100f }));
            strike.AttributeCosts.Add(new AttributeCost { Attribute = mana, Amount = ByLevel(30f, 40f, 50f) });
            Assert.AreEqual("-100 Health; 30/40/50 Mana", ForgeAssetLines.Of(strike));
        }

        /// <summary>An item's line: what its effects change while it's equipped, signed, then the ability it grants.</summary>
        [Test]
        public void TW_21_An_items_line_reads_what_it_gives()
        {
            var armor = Named("Armor");
            var item = Instance<Item>();
            item.Definition.Name = "Plate";
            item.GrantedEffects.Add(Effect(EEffectDurationPolicy.Infinite, armor, new[] { 20f }));
            item.ActiveAbility = AbilityNamed("Blink");
            Assert.AreEqual("+20 Armor; Blink", ForgeAssetLines.Of(item));
        }

        /// <summary>An entity's line: its first three attributes as it starts, then its starting abilities.</summary>
        [Test]
        public void TW_21_An_entitys_line_reads_its_attributes_and_abilities()
        {
            var set = Instance<AttributeSet>();
            set.Name = "Hero";
            set.Attributes = new List<AttributeSetElement>
            {
                Element(Named("Health"), 500f), Element(Named("Mana"), 300f), Element(Named("Armor"), 5f), Element(Named("Luck"), 1f),
            };
            set.SubSets = new List<AttributeSet>();
            set.WorkerGroup = new StandardWorkerGroup();
            var hero = Instance<EntityIdentity>();
            hero.Name = "Hero";
            hero.AttributeSet = set;
            hero.StartingAbilities = new List<Ability> { AbilityNamed("Fireball"), AbilityNamed("Blink") };
            Assert.AreEqual("Health 500, Mana 300, Armor 5; Fireball, Blink", ForgeAssetLines.Of(hero));
        }

        /// <summary>An effect's line: its first impact by level, its provider's levels where it's linked to one.</summary>
        [Test]
        public void TW_21_An_effects_line_is_its_first_impact_by_level()
        {
            var effect = Effect(EEffectDurationPolicy.Instant, Named("Health"), new[] { -10f, -20f, -30f });
            effect.LinkToProvider(AbilityNamed("Smite"));
            Assert.AreEqual("-10/-20/-30 Health", ForgeAssetLines.Of(effect));
        }

        /// <summary>The inspector's header shows the line under the name, and The Forge's list a Summary column, sorting by it.</summary>
        [UnityTest]
        public IEnumerator TW_21_The_header_and_The_Forges_list_show_the_line()
        {
            var bolt = Bolt();
            var editor = UnityEditor.Editor.CreateEditor(bolt);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.AreEqual(ForgeAssetLines.Of(bolt), root.Q<Label>("HeaderLine")?.text, "the header shows the line");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }

            var quiet = AbilityNamed("Quiet");
            var sorted = (List<ScriptableObject>)typeof(TheForge).GetMethod("Sorted", AnyStatic)
                .Invoke(null, new object[] { new List<ScriptableObject> { bolt, quiet }, "Summary", true });
            Assert.That(sorted, Is.EqualTo(new ScriptableObject[] { quiet, bolt }), "the list sorts by the line, an empty one first");

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
                var list = forge.rootVisualElement.Q<MultiColumnListView>("Assets");
                Assert.IsNotNull(list, "The Forge lists the assets");
                Assert.IsNotNull(list.columns["Summary"], "with a Summary column");
                Assert.Greater(list.columns.IndexOf(list.columns["Summary"]), list.columns.IndexOf(list.columns["Levels"]), "after Levels");
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
