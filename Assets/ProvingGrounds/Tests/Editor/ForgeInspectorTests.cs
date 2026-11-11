using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>The inspectors PlayForge draws for its assets and processes, built as the Inspector builds them.</summary>
    public class ForgeInspectorTests
    {
        private sealed class CascadeProcess : AbstractMonoProcess
        {
            public override UniTask RunProcess(CancellationToken token) => UniTask.CompletedTask;
        }

        private static string[] BoundPaths(Object target)
        {
            var editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                return root.Query<PropertyField>().ToList().Select(field => field.bindingPath).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        private static string[] ToggleLabels(Object target)
        {
            var editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                return root.Query<Toggle>().ToList().Select(toggle => toggle.label).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        /// <summary>
        /// Two settings nothing read: an entity's Cap At Max, though its level always stops at Max Level, and an item's
        /// Allow Duplicates, though the entity's Allow Duplicate Items decides. Both are gone, from the assets and from
        /// the inspectors.
        /// </summary>
        [Test]
        public void GAS_45_Settings_nothing_reads_are_gone()
        {
            const BindingFlags fields = BindingFlags.Public | BindingFlags.Instance;
            var entity = ScriptableObject.CreateInstance<EntityIdentity>();
            var item = ScriptableObject.CreateInstance<Item>();
            try
            {
                var left = new List<string>();
                if (typeof(EntityIdentity).GetField("CapAtMaxLevel", fields) != null) left.Add("the entity's Cap At Max");
                if (typeof(RuntimeEntityIdentity).GetField("CapAtMaxLevel", fields) != null) left.Add("the runtime identity's copy");
                if (typeof(ItemDefinition).GetField("AllowDuplicates", fields) != null) left.Add("the item's Allow Duplicates");
                if (ToggleLabels(entity).Contains("Cap At Max")) left.Add("the Entity inspector's toggle");
                if (ToggleLabels(item).Contains("Allow Duplicates")) left.Add("the Item inspector's toggle");
                Assert.That(left, Is.Empty, "settings nothing reads");
            }
            finally
            {
                Object.DestroyImmediate(entity);
                Object.DestroyImmediate(item);
            }
        }

        /// <summary>
        /// Items inherit Local Data, as every asset does, and the five other asset inspectors draw it, but the Item
        /// inspector built no section for it, so an Item's Local Data couldn't be edited.
        /// </summary>
        [Test]
        public void ED_23_The_item_inspector_draws_local_data()
        {
            var item = ScriptableObject.CreateInstance<Item>();
            try
            {
                Assert.That(BoundPaths(item), Does.Contain(nameof(Item.LocalData)));
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        /// <summary>
        /// The Attribute inspector's Find All References opened a dialog telling the user how to search, under a list of
        /// sentences about where attributes can go (ED-15). It lists the assets that use the attribute instead, in a
        /// Used By section: Docs/EDITOR_STYLE.md's "a list that shows what uses what".
        /// </summary>
        [Test]
        public void ED_15_The_Attribute_inspector_lists_what_uses_it()
        {
            const string folder = "Assets/ED-15 Used By";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-15 Used By");
            try
            {
                var mana = ScriptableObject.CreateInstance<Attribute>();
                mana.Name = "Mana";
                AssetDatabase.CreateAsset(mana, $"{folder}/Mana.asset");
                var speed = ScriptableObject.CreateInstance<Attribute>();
                speed.Name = "Speed";
                AssetDatabase.CreateAsset(speed, $"{folder}/Speed.asset");
                var casters = ScriptableObject.CreateInstance<AttributeSet>();
                casters.Attributes.Add(new AttributeSetElement { Attribute = mana });
                AssetDatabase.CreateAsset(casters, $"{folder}/Caster Set.asset");
                AssetDatabase.SaveAssets();

                Assert.That(UsedBy(mana), Is.EqualTo(new[] { "Caster Set" }), "the set that holds Mana");
                Assert.That(UsedBy(speed), Is.Empty, "nothing uses Speed");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// The Item inspector strayed from Docs/EDITOR_STYLE.md (ED-37): its sections came in another order, with Level
        /// Source apart from Leveling, a collapsed section showed nothing, and seven sections' Import and Clear only
        /// logged a warning. Its sections keep the style doc's order now, Leveling holds the level link, each collapsed
        /// section shows one line, and every section but Used By imports and clears.
        /// </summary>
        [Test]
        public void ED_37_The_Item_inspector_keeps_the_style_docs_sections()
        {
            var torch = ScriptableObject.CreateInstance<Item>();
            torch.Definition.Name = "Torch";
            var burning = ScriptableObject.CreateInstance<GameplayEffect>();
            torch.GrantedEffects.Add(burning);
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            hero.MaxLevel = 20;
            var lamp = ScriptableObject.CreateInstance<Item>();
            lamp.LinkToProvider(hero);
            var unlit = ScriptableObject.CreateInstance<Item>();
            unlit.LinkMode = EItemLinkMode.LinkedToProvider;
            try
            {
                Inspect(torch, root =>
                {
                    var sections = root.Query(className: "pf-section").ToList();
                    Assert.That(sections.Select(section => section.name.Replace("_CONTAINER", "")), Is.EqualTo(new[]
                    {
                        "Identity", "Leveling", "Tags", "Effects", "Abilities", "Workers", "LocalData", "UsedBy",
                    }), "the style doc's order: Identity, Leveling, Tags, the item's own, Workers, Local Data");

                    Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–4"), "Leveling's range");
                    Assert.That(Summary(root, "Tags"), Is.EqualTo("None"), "no tags");
                    Assert.That(Summary(root, "Effects"), Is.EqualTo("1 effect"), "the effects it grants");
                    Assert.That(Summary(root, "Abilities"), Is.EqualTo("None"), "no active ability");
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("None"), "no workers");
                    Assert.That(Summary(root, "LocalData"), Is.EqualTo("None"), "no Local Data");
                    Assert.That(Summary(root, "UsedBy"), Is.EqualTo("None"), "nothing uses an unsaved item");
                    AssertEachSectionImportsAndClears(sections, "UsedBy");
                });
                Inspect(lamp, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–20 · from Hero"),
                    "a linked item's range is its provider's"));
                Inspect(unlit, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–4 · no provider"),
                    "an item linked to nothing keeps its own range"));
            }
            finally
            {
                Object.DestroyImmediate(torch);
                Object.DestroyImmediate(burning);
                Object.DestroyImmediate(hero);
                Object.DestroyImmediate(lamp);
                Object.DestroyImmediate(unlit);
            }
        }

        /// <summary>
        /// The Item inspector drew its link state in tinted boxes with colored borders, and its hints in small colored
        /// text, from styles set in C# (ED-37). Outside the drawers it hosts, it takes its look from the style sheet.
        /// </summary>
        [Test]
        public void ED_37_The_Item_inspector_sets_no_look_in_code()
        {
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            var standalone = ScriptableObject.CreateInstance<Item>();
            standalone.Definition.Name = "Torch";
            var linked = ScriptableObject.CreateInstance<Item>();
            linked.Definition.Name = "Lamp";
            linked.LinkToProvider(hero);
            var unlit = ScriptableObject.CreateInstance<Item>();
            unlit.Definition.Name = "Candle";
            unlit.LinkMode = EItemLinkMode.LinkedToProvider;
            try
            {
                var wrong = new List<string>();
                foreach (var target in new Object[][] { new Object[] { standalone }, new Object[] { linked }, new Object[] { unlit }, new Object[] { standalone, unlit } })
                    wrong.AddRange(LookSetInCode(string.Join(", ", target.Select(each => ((Item)each).Definition.Name)), target));
                Assert.That(wrong, Is.Empty, "styles the style sheet gives");
            }
            finally
            {
                Object.DestroyImmediate(hero);
                Object.DestroyImmediate(standalone);
                Object.DestroyImmediate(linked);
                Object.DestroyImmediate(unlit);
            }
        }

        // What an inspector, outside the drawers it hosts, sets of the look the style sheet gives
        private static IEnumerable<string> LookSetInCode(string label, params Object[] targets)
        {
            var editor = UnityEditor.Editor.CreateEditor(targets);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                return root.Query<VisualElement>().ToList()
                    .Where(element => element.GetFirstAncestorOfType<PropertyField>() == null && element is not PropertyField)
                    .SelectMany(element => LookSetInCode(element).Select(style =>
                        $"{label}: {element.GetType().Name} \"{(element as TextElement)?.text ?? element.name}\" sets {style}"))
                    .ToList();
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        // Every section but the read-only ones imports and clears: their buttons only logged a warning (ED-37)
        private static void AssertEachSectionImportsAndClears(List<VisualElement> sections, params string[] readOnly)
        {
            foreach (var section in sections.Where(section => !readOnly.Contains(section.name.Replace("_CONTAINER", ""))))
            {
                var actions = section.Query<Button>(className: "pf-section__action").ToList().Select(button => button.tooltip).ToList();
                Assert.That(actions, Has.Some.StartsWith("Import "), $"{section.name} has Import");
                Assert.That(actions, Has.Some.StartsWith("Reset "), $"{section.name} has Clear");
                Assert.That(actions, Has.None.Contains("not configured"), $"{section.name}'s Import and Clear work");
            }
        }

        // The look a control may take only from the style sheet: its colors, borders and text
        internal static IEnumerable<string> LookSetInCode(VisualElement element)
        {
            var style = element.style;
            if (style.backgroundColor.keyword != StyleKeyword.Null) yield return "background-color";
            if (style.color.keyword != StyleKeyword.Null) yield return "color";
            if (style.borderTopColor.keyword != StyleKeyword.Null || style.borderBottomColor.keyword != StyleKeyword.Null ||
                style.borderLeftColor.keyword != StyleKeyword.Null || style.borderRightColor.keyword != StyleKeyword.Null)
                yield return "border-color";
            if (style.borderTopWidth.keyword != StyleKeyword.Null || style.borderBottomWidth.keyword != StyleKeyword.Null ||
                style.borderLeftWidth.keyword != StyleKeyword.Null || style.borderRightWidth.keyword != StyleKeyword.Null)
                yield return "border-width";
            if (style.borderTopLeftRadius.keyword != StyleKeyword.Null || style.borderTopRightRadius.keyword != StyleKeyword.Null ||
                style.borderBottomLeftRadius.keyword != StyleKeyword.Null || style.borderBottomRightRadius.keyword != StyleKeyword.Null)
                yield return "border-radius";
            if (style.fontSize.keyword != StyleKeyword.Null) yield return "font-size";
            if (style.unityFontStyleAndWeight.keyword != StyleKeyword.Null) yield return "font-style";
            if (style.opacity.keyword != StyleKeyword.Null) yield return "opacity";
        }

        /// <summary>
        /// The Item inspector lists the assets that use the item, as the Attribute inspector does (ED-37): here an entity
        /// that starts with it and an effect linked to it.
        /// </summary>
        [Test]
        public void ED_37_The_Item_inspector_lists_what_uses_it()
        {
            const string folder = "Assets/ED-37 Used By";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-37 Used By");
            try
            {
                var torch = ScriptableObject.CreateInstance<Item>();
                torch.Definition.Name = "Torch";
                AssetDatabase.CreateAsset(torch, $"{folder}/Torch.asset");
                var lamp = ScriptableObject.CreateInstance<Item>();
                lamp.Definition.Name = "Lamp";
                AssetDatabase.CreateAsset(lamp, $"{folder}/Lamp.asset");
                var hero = ScriptableObject.CreateInstance<EntityIdentity>();
                hero.Name = "Hero";
                hero.StartingItems.Add(new StartingItemContainer { Item = torch });
                AssetDatabase.CreateAsset(hero, $"{folder}/Hero.asset");
                var burning = ScriptableObject.CreateInstance<GameplayEffect>();
                burning.LinkToProvider(torch);
                AssetDatabase.CreateAsset(burning, $"{folder}/Burning.asset");
                AssetDatabase.SaveAssets();

                Assert.That(UsedBy(torch), Is.EqualTo(new[] { "Burning", "Hero" }), "the effect linked to the torch and the entity that starts with it");
                Assert.That(UsedBy(lamp), Is.Empty, "nothing uses the lamp");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// The Entity inspector strayed from Docs/EDITOR_STYLE.md as the Item inspector did (ED-37): Level Source sat
        /// apart from Leveling, which was called Level, the Attribute Set sat in Definition, a collapsed section showed
        /// nothing, and all but one section's Import and Clear only logged a warning. Its sections keep the style doc's
        /// order now, with the Entity mockup's Attributes, Abilities and Items as its own, each collapsed section shows
        /// one line, and every section but Used By imports and clears.
        /// </summary>
        [Test]
        public void ED_37_The_Entity_inspector_keeps_the_style_docs_sections()
        {
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            goblin.Name = "Goblin";
            var cleave = ScriptableObject.CreateInstance<Ability>();
            goblin.StartingAbilities.Add(cleave);
            var cleaver = ScriptableObject.CreateInstance<Item>();
            goblin.StartingItems.Add(new StartingItemContainer { Item = cleaver, EquipOnInit = true });
            var health = ScriptableObject.CreateInstance<Attribute>();
            var stats = ScriptableObject.CreateInstance<AttributeSet>();
            stats.Name = "Goblin Stats";
            stats.Attributes.Add(new AttributeSetElement { Attribute = health });
            goblin.AttributeSet = stats;
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            hero.MaxLevel = 20;
            var minion = ScriptableObject.CreateInstance<EntityIdentity>();
            minion.LinkToProvider(hero);
            var stray = ScriptableObject.CreateInstance<EntityIdentity>();
            stray.LinkMode = EItemLinkMode.LinkedToProvider;
            try
            {
                Inspect(goblin, root =>
                {
                    var sections = root.Query(className: "pf-section").ToList();
                    Assert.That(sections.Select(section => section.name.Replace("_CONTAINER", "")), Is.EqualTo(new[]
                    {
                        "Identity", "Leveling", "Tags", "Attributes", "Abilities", "Items", "Workers", "LocalData", "UsedBy",
                    }), "the style doc's order: Identity, Leveling, Tags, the entity's own, Workers, Local Data");

                    Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–99"), "Leveling's range");
                    Assert.That(Summary(root, "Tags"), Is.EqualTo("None"), "no tags");
                    Assert.That(Summary(root, "Attributes"), Is.EqualTo("Goblin Stats · 1 attribute"), "the set and its size");
                    Assert.That(Summary(root, "Abilities"), Is.EqualTo("1 ability"), "the starting abilities");
                    Assert.That(Summary(root, "Items"), Is.EqualTo("1 item · 1 equipped"), "the starting items, and how many it equips");
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("None"), "no workers");
                    Assert.That(Summary(root, "LocalData"), Is.EqualTo("None"), "no Local Data");
                    Assert.That(Summary(root, "UsedBy"), Is.EqualTo("None"), "nothing uses an unsaved entity");
                    AssertEachSectionImportsAndClears(sections, "UsedBy");

                    Assert.IsNotNull(root.Q(name: "Attributes_CONTAINER").Q<PropertyField>("AttributeSet"),
                        "the Attribute Set sits in Attributes");
                    Assert.IsNotNull(root.Q(name: "Identity_CONTAINER").Q<TextField>("AssetTag"), "and the asset tag in Identity");
                });
                Inspect(minion, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–20 · from Hero"),
                    "a linked entity's range is its provider's"));
                Inspect(stray, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–99 · no provider"),
                    "an entity linked to nothing keeps its own range"));
            }
            finally
            {
                foreach (var each in new Object[] { goblin, cleave, cleaver, health, stats, hero, minion, stray }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Entity inspector drew its link state in tinted boxes with colored borders, its hints in small colored text,
        /// and dividers between its fields, from styles set in C# (ED-37). Outside the drawers it hosts, it takes its look
        /// from the style sheet.
        /// </summary>
        [Test]
        public void ED_37_The_Entity_inspector_sets_no_look_in_code()
        {
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            goblin.Name = "Goblin";
            var minion = ScriptableObject.CreateInstance<EntityIdentity>();
            minion.Name = "Minion";
            minion.LinkToProvider(hero);
            var stray = ScriptableObject.CreateInstance<EntityIdentity>();
            stray.Name = "Stray";
            stray.LinkMode = EItemLinkMode.LinkedToProvider;
            try
            {
                var wrong = new List<string>();
                foreach (var entity in new[] { goblin, minion, stray }) wrong.AddRange(LookSetInCode(entity.Name, entity));
                Assert.That(wrong, Is.Empty, "styles the style sheet gives");
            }
            finally
            {
                foreach (var each in new Object[] { hero, goblin, minion, stray }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Entity inspector lists the assets that use the entity, as the Item inspector does (ED-37): here an ability
        /// and an item linked to it.
        /// </summary>
        [Test]
        public void ED_37_The_Entity_inspector_lists_what_uses_it()
        {
            const string folder = "Assets/ED-37 Entity Used By";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-37 Entity Used By");
            try
            {
                var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
                goblin.Name = "Goblin";
                AssetDatabase.CreateAsset(goblin, $"{folder}/Goblin.asset");
                var hero = ScriptableObject.CreateInstance<EntityIdentity>();
                hero.Name = "Hero";
                AssetDatabase.CreateAsset(hero, $"{folder}/Hero.asset");
                var cleave = ScriptableObject.CreateInstance<Ability>();
                cleave.LinkToProvider(goblin);
                AssetDatabase.CreateAsset(cleave, $"{folder}/Cleave.asset");
                var cleaver = ScriptableObject.CreateInstance<Item>();
                cleaver.Definition.Name = "Cleaver";
                cleaver.LinkToProvider(goblin);
                AssetDatabase.CreateAsset(cleaver, $"{folder}/Cleaver.asset");
                AssetDatabase.SaveAssets();

                Assert.That(UsedBy(goblin), Is.EqualTo(new[] { "Cleave", "Cleaver" }), "the ability and the item linked to the goblin");
                Assert.That(UsedBy(hero), Is.Empty, "nothing uses the hero");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // An attribute set element: its attribute, the Current and Base it starts at, and its range
        private static AttributeSetElement Element(Attribute attribute, float value, EAttributeOverflowPolicy range,
            float floor = 0, float ceil = 0) => new()
        {
            Attribute = attribute,
            Current = new AttributeMagnitudeSpec { Magnitude = value },
            Base = new AttributeMagnitudeSpec { Magnitude = value },
            Overflow = new AttributeOverflowData { Policy = range, Floor = new AttributeValue(floor, floor), Ceil = new AttributeValue(ceil, ceil) },
        };

        private static Attribute NamedAttribute(string name)
        {
            var attribute = ScriptableObject.CreateInstance<Attribute>();
            attribute.Name = name;
            return attribute;
        }

        /// <summary>
        /// The Attribute Set inspector strayed from Docs/EDITOR_STYLE.md as the Item and Entity inspectors did (ED-37):
        /// Level Source sat apart from Leveling, a collapsed section showed a count or nothing, the Summary section's
        /// button listed the attributes in a dialog, and Import copied the name. Its sections keep the style doc's order
        /// now, each collapsed section shows one line, and every section but the two that only show imports and clears.
        /// </summary>
        [Test]
        public void ED_37_The_Attribute_Set_inspector_keeps_the_style_docs_sections()
        {
            var health = NamedAttribute("Health");
            var armor = NamedAttribute("Armor");
            var speed = NamedAttribute("Move Speed");
            var speedy = ScriptableObject.CreateInstance<AttributeSet>();
            speedy.Name = "Speedy";
            speedy.Attributes.Add(Element(speed, 5.5f, EAttributeOverflowPolicy.FloorToCeil, 1, 12));
            var stats = ScriptableObject.CreateInstance<AttributeSet>();
            stats.Name = "Goblin Stats";
            stats.Attributes.Add(Element(health, 120, EAttributeOverflowPolicy.ZeroToBase));
            stats.Attributes.Add(Element(armor, 4, EAttributeOverflowPolicy.Unlimited));
            stats.SubSets = new List<AttributeSet> { speedy };
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            hero.MaxLevel = 20;
            var linked = ScriptableObject.CreateInstance<AttributeSet>();
            linked.LinkToProvider(hero);
            var stray = ScriptableObject.CreateInstance<AttributeSet>();
            stray.LinkMode = EAttributeSetLinkMode.LinkedToProvider;
            try
            {
                Inspect(stats, root =>
                {
                    var sections = root.Query(className: "pf-section").ToList();
                    Assert.That(sections.Select(section => section.name.Replace("_CONTAINER", "")), Is.EqualTo(new[]
                    {
                        "Identity", "Leveling", "Attributes", "Subsets", "Settings", "AllAttributes", "Workers", "LocalData", "UsedBy",
                    }), "the style doc's order: Identity, Leveling, the set's own, Workers, Local Data");

                    Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–4"), "Leveling's range");
                    Assert.That(Summary(root, "Attributes"), Is.EqualTo("2 attributes"), "its own attributes");
                    Assert.That(Summary(root, "Subsets"), Is.EqualTo("1 sub-set"), "its sub-sets");
                    Assert.That(Summary(root, "Settings"), Is.EqualTo("Use Maximum"), "how collisions settle");
                    Assert.That(Summary(root, "AllAttributes"), Is.EqualTo("3 attributes"), "every attribute it gives");
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("None"), "no workers");
                    Assert.That(Summary(root, "LocalData"), Is.EqualTo("None"), "no Local Data");
                    Assert.That(Summary(root, "UsedBy"), Is.EqualTo("None"), "nothing uses an unsaved set");
                    AssertEachSectionImportsAndClears(sections, "AllAttributes", "UsedBy");

                    Assert.IsNotNull(root.Q(name: "Identity_CONTAINER").Q<TextField>("AssetTag"), "the asset tag sits in Identity");
                    Assert.That(root.Query<Button>().ToList().Select(button => button.text), Has.None.EqualTo("List All Unique Attributes"),
                        "no button opens a dialog of the attributes");
                });
                Inspect(linked, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–20 · from Hero"),
                    "a linked set's range is its provider's"));
                Inspect(stray, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–4 · no provider"),
                    "a set linked to nothing keeps its own range"));
            }
            finally
            {
                foreach (var each in new Object[] { health, armor, speed, speedy, stats, hero, linked, stray }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// An entity's attributes showed only as its Attribute Set's name, and the set's own inspector listed them in a
        /// dialog (ED-37). Both show a table now, as the Entity mockup draws it: each attribute the set gives, its sub-sets'
        /// with them, the value it starts at, and its range. Where sets overlap, the value is the one the runtime settles
        /// on: here the sub-set's higher Health, under the set's Use Maximum.
        /// </summary>
        [Test]
        public void ED_37_The_attribute_table_shows_what_an_entity_starts_with()
        {
            var health = NamedAttribute("Health");
            var armor = NamedAttribute("Armor");
            var speed = NamedAttribute("Move Speed");
            var speedy = ScriptableObject.CreateInstance<AttributeSet>();
            speedy.Name = "Speedy";
            speedy.Attributes.Add(Element(speed, 5.5f, EAttributeOverflowPolicy.FloorToCeil, 1, 12));
            speedy.Attributes.Add(Element(health, 150, EAttributeOverflowPolicy.ZeroToBase));
            var stats = ScriptableObject.CreateInstance<AttributeSet>();
            stats.Name = "Goblin Stats";
            stats.Attributes.Add(Element(health, 120, EAttributeOverflowPolicy.ZeroToBase));
            stats.Attributes.Add(Element(armor, 4, EAttributeOverflowPolicy.Unlimited));
            stats.SubSets = new List<AttributeSet> { speedy };
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            goblin.Name = "Goblin";
            goblin.AttributeSet = stats;
            var expected = new[] { "Health | 150 | 0–max", "Armor | 4 | —", "Move Speed | 5.5 | 1–12" };
            try
            {
                Inspect(stats, root => Assert.That(TableRows(root.Q(name: "AllAttributes_CONTAINER")), Is.EqualTo(expected),
                    "the set's inspector shows every attribute it gives"));
                Inspect(goblin, root => Assert.That(TableRows(root.Q(name: "Attributes_CONTAINER")), Is.EqualTo(expected),
                    "and the entity's, the attributes its set gives it"));
            }
            finally
            {
                foreach (var each in new Object[] { health, armor, speed, speedy, stats, goblin }) Object.DestroyImmediate(each);
            }
        }

        // An attribute table's rows, each as "name | starts at | range"
        private static string[] TableRows(VisualElement container)
        {
            Assert.IsNotNull(container, "the section is there");
            return container.Query(className: "pf-table__row").ToList().Select(row => string.Join(" | ",
                row.Q<Label>(className: "pf-table__name")?.text, row.Q<Label>(className: "pf-table__start")?.text,
                row.Q<Label>(className: "pf-table__range")?.text)).ToArray();
        }

        /// <summary>
        /// The Attribute Set inspector drew its link state in tinted boxes with colored borders, its hints in small
        /// colored text, and its Summary in small colored text, from styles set in C# (ED-37). Outside the drawers it
        /// hosts, it takes its look from the style sheet.
        /// </summary>
        [Test]
        public void ED_37_The_Attribute_Set_inspector_sets_no_look_in_code()
        {
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            var health = NamedAttribute("Health");
            var stats = ScriptableObject.CreateInstance<AttributeSet>();
            stats.Name = "Goblin Stats";
            stats.Attributes.Add(Element(health, 120, EAttributeOverflowPolicy.ZeroToBase));
            var linked = ScriptableObject.CreateInstance<AttributeSet>();
            linked.Name = "Linked";
            linked.LinkToProvider(hero);
            var stray = ScriptableObject.CreateInstance<AttributeSet>();
            stray.Name = "Stray";
            stray.LinkMode = EAttributeSetLinkMode.LinkedToProvider;
            try
            {
                var wrong = new List<string>();
                foreach (var set in new[] { stats, linked, stray }) wrong.AddRange(LookSetInCode(set.Name, set));
                Assert.That(wrong, Is.Empty, "styles the style sheet gives");
            }
            finally
            {
                foreach (var each in new Object[] { hero, health, stats, linked, stray }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Attribute Set inspector lists the assets that use the set, as the Item and Entity inspectors do (ED-37):
        /// here an entity built from it and a set that holds it as a sub-set.
        /// </summary>
        [Test]
        public void ED_37_The_Attribute_Set_inspector_lists_what_uses_it()
        {
            const string folder = "Assets/ED-37 Set Used By";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-37 Set Used By");
            try
            {
                var stats = ScriptableObject.CreateInstance<AttributeSet>();
                stats.Name = "Goblin Stats";
                AssetDatabase.CreateAsset(stats, $"{folder}/Goblin Stats.asset");
                var spare = ScriptableObject.CreateInstance<AttributeSet>();
                spare.Name = "Spare";
                AssetDatabase.CreateAsset(spare, $"{folder}/Spare.asset");
                var chief = ScriptableObject.CreateInstance<AttributeSet>();
                chief.Name = "Chief Stats";
                chief.SubSets = new List<AttributeSet> { stats };
                AssetDatabase.CreateAsset(chief, $"{folder}/Chief Stats.asset");
                var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
                goblin.Name = "Goblin";
                goblin.AttributeSet = stats;
                AssetDatabase.CreateAsset(goblin, $"{folder}/Goblin.asset");
                AssetDatabase.SaveAssets();

                Assert.That(UsedBy(stats), Is.EqualTo(new[] { "Chief Stats", "Goblin" }), "the set that holds it and the entity built from it");
                Assert.That(UsedBy(spare), Is.Empty, "nothing uses the spare set");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // The text an enum field shows for its value
        private static string Shown(EnumField field) => field?.Q<TextElement>(className: "unity-enum-field__text")?.text;

        /// <summary>
        /// The Ability inspector strayed from Docs/EDITOR_STYLE.md as the other asset inspectors did (ED-37): Level
        /// Source sat apart from Leveling, under the same section name, Cost and Cooldown sat in a tinted box in
        /// Definition, Runtime Behaviour came before Tags, and the tag requirements sat in Tags, apart from the
        /// validation rules. Its sections follow the style doc and the Ability mockup now: Activation holds When Busy,
        /// Activate At Start, Cost, Cooldown and targeting, and Requirements holds the tag requirements and the checks.
        /// The mockup's Activate When Granted says Activate At Start, since only a Starting Ability tries it.
        /// </summary>
        [Test]
        public void ED_37_The_Ability_inspector_keeps_the_style_docs_sections()
        {
            var fireball = ScriptableObject.CreateInstance<Ability>();
            fireball.Definition.Name = "Fireball";
            var cost = ScriptableObject.CreateInstance<GameplayEffect>();
            cost.Definition.Name = "Fireball Cost";
            fireball.Cost = cost;
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            hero.MaxLevel = 20;
            var linked = ScriptableObject.CreateInstance<Ability>();
            linked.LinkToProvider(hero);
            var stray = ScriptableObject.CreateInstance<Ability>();
            stray.LinkMode = EAbilityLinkMode.LinkedToProvider;
            try
            {
                Inspect(fireball, root =>
                {
                    var sections = root.Query(className: "pf-section").ToList();
                    Assert.That(sections.Select(section => section.name.Replace("_CONTAINER", "")), Is.EqualTo(new[]
                    {
                        "Identity", "Leveling", "Tags", "Activation", "Stages", "Requirements", "Workers", "LocalData", "UsedBy",
                    }), "the style doc's order: Identity, Leveling, Tags, the ability's own, Requirements, Workers, Local Data");

                    Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–4"), "Leveling's range");
                    Assert.That(Summary(root, "Tags"), Is.EqualTo("None"), "no tags");
                    Assert.That(Summary(root, "Activation"), Is.EqualTo("Entity's Policy · Fireball Cost"), "how it activates, and what it costs");
                    Assert.That(Summary(root, "Stages"), Is.EqualTo("None"), "no stages");
                    Assert.That(Summary(root, "Requirements"), Is.EqualTo("4 checks"), "its three caster checks and one target check");
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("None"), "no workers");
                    Assert.That(Summary(root, "LocalData"), Is.EqualTo("None"), "no Local Data");
                    Assert.That(Summary(root, "UsedBy"), Is.EqualTo("None"), "nothing uses an unsaved ability");
                    AssertEachSectionImportsAndClears(sections, "UsedBy");

                    Assert.IsNotNull(root.Q(name: "Identity_CONTAINER").Q<TextField>("AssetTag"), "the asset tag sits in Identity");
                    var activation = root.Q(name: "Activation_CONTAINER");
                    Assert.IsNotNull(activation.Q<PropertyField>("Cost"), "Cost sits in Activation");
                    var policy = activation.Q<EnumField>("ActivationPolicy");
                    Assert.AreEqual("When Busy", policy?.label, "as When Busy, as the mockup names it");
                    Assert.AreEqual("Entity's Policy", Shown(policy), "showing its choice by its own name");
                    Assert.AreEqual("Activate At Start", activation.Q<Toggle>("ActivateImmediately")?.label, "and Activate At Start");
                    Assert.IsNotNull(root.Q(name: "Requirements_CONTAINER").Q<PropertyField>("SourceActivationRules"), "the checks sit in Requirements");
                });
                Inspect(linked, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–20 · from Hero"),
                    "a linked ability's range is its provider's"));
                Inspect(stray, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–4 · no provider"),
                    "an ability linked to nothing keeps its own range"));
                Inspect(hero, root =>
                {
                    var policy = root.Q<EnumField>("ActivationPolicy");
                    Assert.AreEqual("When Busy", policy?.label, "the entity's policy is When Busy too");
                    Assert.AreEqual("Queue", Shown(policy), "showing Queue");
                });
            }
            finally
            {
                foreach (var each in new Object[] { fireball, cost, hero, linked, stray }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Ability inspector drew its link state in tinted boxes with colored borders, its hints in small colored
        /// text, Cost and Cooldown and the granted tags in tinted boxes, and dividers between its fields, from styles set
        /// in C# (ED-37). Outside the drawers it hosts, it takes its look from the style sheet.
        /// </summary>
        [Test]
        public void ED_37_The_Ability_inspector_sets_no_look_in_code()
        {
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            var fireball = ScriptableObject.CreateInstance<Ability>();
            fireball.Definition.Name = "Fireball";
            var linked = ScriptableObject.CreateInstance<Ability>();
            linked.Definition.Name = "Linked";
            linked.LinkToProvider(hero);
            var stray = ScriptableObject.CreateInstance<Ability>();
            stray.Definition.Name = "Stray";
            stray.LinkMode = EAbilityLinkMode.LinkedToProvider;
            try
            {
                var wrong = new List<string>();
                foreach (var target in new Object[][] { new Object[] { fireball }, new Object[] { linked }, new Object[] { stray }, new Object[] { fireball, stray } })
                    wrong.AddRange(LookSetInCode(string.Join(", ", target.Select(each => ((Ability)each).Definition.Name)), target));
                Assert.That(wrong, Is.Empty, "styles the style sheet gives");
            }
            finally
            {
                foreach (var each in new Object[] { hero, fireball, linked, stray }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Ability inspector lists the assets that use the ability, as the other asset inspectors do (ED-37): here an
        /// entity that starts with it and an item that grants it.
        /// </summary>
        [Test]
        public void ED_37_The_Ability_inspector_lists_what_uses_it()
        {
            const string folder = "Assets/ED-37 Ability Used By";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-37 Ability Used By");
            try
            {
                var fireball = ScriptableObject.CreateInstance<Ability>();
                fireball.Definition.Name = "Fireball";
                AssetDatabase.CreateAsset(fireball, $"{folder}/Fireball.asset");
                var frost = ScriptableObject.CreateInstance<Ability>();
                frost.Definition.Name = "Frost";
                AssetDatabase.CreateAsset(frost, $"{folder}/Frost.asset");
                var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
                goblin.Name = "Goblin";
                goblin.StartingAbilities.Add(fireball);
                AssetDatabase.CreateAsset(goblin, $"{folder}/Goblin.asset");
                var torch = ScriptableObject.CreateInstance<Item>();
                torch.Definition.Name = "Torch";
                torch.ActiveAbility = fireball;
                AssetDatabase.CreateAsset(torch, $"{folder}/Torch.asset");
                AssetDatabase.SaveAssets();

                Assert.That(UsedBy(fireball), Is.EqualTo(new[] { "Goblin", "Torch" }), "the entity that starts with it and the item that grants it");
                Assert.That(UsedBy(frost), Is.Empty, "nothing uses Frost");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // An effect as the Effect tests give it: Burning, 6 s with 6 ticks and one on applying, taking 8 of an attribute
        private static GameplayEffect Burning(Attribute health)
        {
            var burning = ScriptableObject.CreateInstance<GameplayEffect>();
            burning.Definition.Name = "Burning";
            burning.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = health,
                MagnitudeOperation = new ScalableFloat { Magnitude = -8f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            };
            burning.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Durational,
                DurationOperation = new ScalableFloat { Magnitude = 6f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                EnablePeriodicTicks = true,
                TickOnApplication = true,
                TicksOperation = new ScalableInt { Magnitude = 6 },
                TickIntervalOperation = new ScalableFloat { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            };
            return burning;
        }

        /// <summary>
        /// The Effect inspector strayed from Docs/EDITOR_STYLE.md as the other asset inspectors did (ED-37): Level Source
        /// sat apart from Leveling, Impact came before Duration and Workers before Requirements, and the asset tag sat in
        /// Tags. Its sections follow the style doc and the Effect mockup now: Identity, Leveling, Tags, Duration, Impact,
        /// Requirements, Workers and Local Data, then Used By, each with a one-line summary. Requirements shows one side at
        /// a time, the target's first.
        /// </summary>
        [Test]
        public void ED_37_The_Effect_inspector_keeps_the_style_docs_sections()
        {
            var health = ScriptableObject.CreateInstance<Attribute>();
            health.Name = "Health";
            var burning = Burning(health);
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            hero.MaxLevel = 20;
            var linked = Burning(health);
            linked.LinkToProvider(hero);
            var stray = Burning(health);
            stray.LinkMode = EEffectLinkMode.LinkedToProvider;
            try
            {
                Inspect(burning, root =>
                {
                    var sections = root.Query(className: "pf-section").ToList();
                    Assert.That(sections.Select(section => section.name.Replace("_CONTAINER", "")), Is.EqualTo(new[]
                    {
                        "Identity", "Leveling", "Tags", "Duration", "Impact", "Levels", "Cues", "Requirements", "Workers", "LocalData", "UsedBy",
                    }), "the style doc's order, as the Effect mockup has it, with Cues among the effect's own (FEAT-1), and Levels after Impact (FEAT-32)");

                    Assert.That(Summary(root, "Leveling"), Is.EqualTo("From what applies it"), "a standalone effect takes its origin's level");
                    Assert.That(Summary(root, "Tags"), Is.EqualTo("None"), "no tags");
                    Assert.That(Summary(root, "Duration"), Is.EqualTo("Durational · 6 s · 6 ticks"), "how long, and how often it ticks");
                    Assert.That(Summary(root, "Impact"), Is.EqualTo("Health −8 · Periodic"), "what it changes, and when");
                    Assert.That(Summary(root, "Levels"), Is.EqualTo("-8 Health"), "and what it does level by level (FEAT-32)");
                    Assert.That(Summary(root, "Requirements"), Is.EqualTo("None"), "no requirements");
                    Assert.That(Summary(root, "Cues"), Is.EqualTo("None"), "no cues");
                    Assert.That(Summary(root, "Workers"), Is.EqualTo("None"), "no workers");
                    Assert.That(Summary(root, "LocalData"), Is.EqualTo("None"), "no Local Data");
                    Assert.That(Summary(root, "UsedBy"), Is.EqualTo("None"), "nothing uses an unsaved effect");
                    // Levels only reads, as Used By does, so neither imports nor clears (FEAT-32)
                    AssertEachSectionImportsAndClears(sections, "Levels", "UsedBy");

                    Assert.IsNotNull(root.Q(name: "Identity_CONTAINER").Q<TextField>("AssetTag"), "the asset tag sits in Identity");
                    var requirements = root.Q(name: "Requirements_CONTAINER");
                    Assert.That(requirements.Q(className: "pf-segmented")?.Query<Button>().ToList().Select(button => button.text),
                        Is.EqualTo(new[] { "On the target", "On the source" }), "Requirements switches between the target and the source");
                    Assert.AreNotEqual(DisplayStyle.None, requirements.Q<PropertyField>("TargetRequirements")?.style.display.value,
                        "the target's show first");
                    Assert.AreEqual(DisplayStyle.None, requirements.Q<PropertyField>("SourceRequirements")?.style.display.value,
                        "and the source's wait");
                });
                Inspect(linked, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("Levels 1–20 · from Hero"),
                    "a linked effect's range is its provider's"));
                Inspect(stray, root => Assert.That(Summary(root, "Leveling"), Is.EqualTo("No provider"),
                    "an effect linked to nothing says so"));
            }
            finally
            {
                foreach (var each in new Object[] { health, burning, hero, linked, stray }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Effect inspector drew its link state in tinted boxes with colored borders, its hints in small italic text,
        /// and a red-tinted Unlink button, from styles set in C# (ED-37). Outside the drawers it hosts, it takes its look
        /// from the style sheet.
        /// </summary>
        [Test]
        public void ED_37_The_Effect_inspector_sets_no_look_in_code()
        {
            var health = ScriptableObject.CreateInstance<Attribute>();
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            var burning = Burning(health);
            var linked = Burning(health);
            linked.Definition.Name = "Linked";
            linked.LinkToProvider(hero);
            var instant = Burning(health);
            instant.Definition.Name = "Instant";
            instant.DurationSpecification.DurationPolicy = EEffectDurationPolicy.Instant;
            try
            {
                var wrong = new List<string>();
                foreach (var target in new[] { burning, linked, instant })
                    wrong.AddRange(LookSetInCode(target.Definition.Name, target));
                Assert.That(wrong, Is.Empty, "styles the style sheet gives");
            }
            finally
            {
                foreach (var each in new Object[] { health, hero, burning, linked, instant }) Object.DestroyImmediate(each);
            }
        }

        /// <summary>
        /// The Effect inspector lists the assets that use the effect, as the other asset inspectors do (ED-37): here an
        /// ability whose cost it is and an item that grants it.
        /// </summary>
        [Test]
        public void ED_37_The_Effect_inspector_lists_what_uses_it()
        {
            const string folder = "Assets/ED-37 Effect Used By";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-37 Effect Used By");
            try
            {
                var burning = Burning(null);
                AssetDatabase.CreateAsset(burning, $"{folder}/Burning.asset");
                var frost = Burning(null);
                frost.Definition.Name = "Frost";
                AssetDatabase.CreateAsset(frost, $"{folder}/Frost.asset");
                var fireball = ScriptableObject.CreateInstance<Ability>();
                fireball.Definition.Name = "Fireball";
                fireball.Cost = burning;
                AssetDatabase.CreateAsset(fireball, $"{folder}/Fireball.asset");
                var torch = ScriptableObject.CreateInstance<Item>();
                torch.Definition.Name = "Torch";
                torch.GrantedEffects.Add(burning);
                AssetDatabase.CreateAsset(torch, $"{folder}/Torch.asset");
                AssetDatabase.SaveAssets();

                Assert.That(UsedBy(burning), Is.EqualTo(new[] { "Fireball", "Torch" }), "the ability it's the cost of and the item that grants it");
                Assert.That(UsedBy(frost), Is.Empty, "nothing uses Frost");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// What ED-60 left (ED-67): an effect's Tags summary named the tag it removes or blocks, an item's Identity
        /// summary its visibility tag, and the import window's list each asset's tag, by the tag's name, which a unique
        /// tag pads. Each shows the tag's display name.
        /// </summary>
        [Test]
        public void ED_67_A_summary_names_a_tag_by_its_display_name()
        {
            const string path = "Test.ED67.Debuff";
            var tag = Tag.GenerateAsUnique(path);
            Assume.That(tag.Name, Is.Not.EqualTo(path), "a unique tag's name is padded");
            var cleanse = ScriptableObject.CreateInstance<GameplayEffect>();
            cleanse.Tags.RemovesEffectsTagged = new List<Tag> { tag };
            var ward = ScriptableObject.CreateInstance<GameplayEffect>();
            ward.Tags.BlocksEffectsTagged = new List<Tag> { tag };
            var torch = ScriptableObject.CreateInstance<Item>();
            torch.Definition.Visibility = tag;
            var fireball = ScriptableObject.CreateInstance<Ability>();
            fireball.Tags.AssetTag = tag;
            try
            {
                Inspect(cleanse, root => Assert.That(Summary(root, "Tags"), Is.EqualTo($"Removes {path}"), "the tag an effect removes"));
                Inspect(ward, root => Assert.That(Summary(root, "Tags"), Is.EqualTo($"Blocks {path}"), "the tag an effect blocks"));
                Inspect(torch, root => Assert.That(Summary(root, "Identity"), Is.EqualTo(path), "an item's visibility"));
                var tagOf = typeof(ForgeImportWindow).GetMethod("TagOf", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(tagOf.Invoke(null, new object[] { fireball }), Is.EqualTo(path), "the import window's list of assets");
            }
            finally
            {
                foreach (var made in new Object[] { cleanse, ward, torch, fireball }) Object.DestroyImmediate(made);
            }
        }

        // Builds an inspector as the Inspector does, and hands it to a check
        private static void Inspect(Object target, System.Action<VisualElement> check)
        {
            var editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                check(root);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        // The one line a section's header shows while it's collapsed
        private static string Summary(VisualElement root, string section)
        {
            var container = root.Q(name: $"{section}_CONTAINER");
            Assert.IsNotNull(container, $"the inspector has a {section} section");
            return container.Q<Label>(className: "pf-section__summary").text;
        }

        // The names a Used By section lists
        private static string[] UsedBy(Object target)
        {
            var editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                var section = root.Q(name: "UsedBy_CONTAINER");
                Assert.IsNotNull(section, "the inspector has a Used By section");
                return section.Query<Label>(className: "pf-used-by__name").ToList().Select(label => label.text).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        /// <summary>
        /// The process inspector hides the process fields from the default inspector and draws them itself, but it left
        /// out ParticipateInSiblingCascade, so the field ProcessControl reads couldn't be set.
        /// </summary>
        [Test]
        public void ED_24_The_process_inspector_draws_sibling_cascade()
        {
            var host = new GameObject("ED-24") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var process = host.AddComponent<CascadeProcess>();
                Assert.That(BoundPaths(process), Does.Contain(nameof(AbstractMonoProcess.ParticipateInSiblingCascade)));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// A Mono Process's inspector is a Process section of Unity's fields, each choice saying what it does in its
        /// tooltip, with Priority Method only while the process steps and Priority under it only while the method is
        /// Manual (ED-61). It drew a tinted box, gray hints in 10 px under each field, set 50 ms after it drew, and a rule.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_61_The_process_inspector_is_a_section_of_Unitys_fields()
        {
            // A scene's object, as a process is: Unity counts no change to one it doesn't save, so its bindings see none
            var owner = new GameObject("ED-61");
            var process = owner.AddComponent<CascadeProcess>();
            var editor = UnityEditor.Editor.CreateEditor(process);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("ED-61 Panel") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = editor.CreateInspectorGUI();
                document.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                for (int i = 0; i < 4; i++) yield return null;

                Assert.IsNotNull(root.Q(name: "Process_CONTAINER"), "the process's fields are a section");
                Assert.IsNull(root.Q<Label>("LifecycleHint"), "with no hints under them");
                Assert.That(LookSetInCode("Process", process), Is.Empty, "and no look set in code");
                StringAssert.StartsWith("Self Terminating: ", root.Q<PropertyField>(nameof(AbstractMonoProcess.ProcessLifecycle)).tooltip,
                    "a choice says what it does");

                var method = root.Q<PropertyField>(nameof(AbstractMonoProcess.PriorityMethod));
                var priority = root.Q<PropertyField>(nameof(AbstractMonoProcess.ProcessStepPriority));
                Assert.IsTrue(method.ClassListContains("pf-hidden"), "a process that doesn't step has no priority method");
                // Changed from outside the inspector, as an Undo or another inspector changes it
                var other = new SerializedObject(process);
                other.FindProperty(nameof(AbstractMonoProcess.ProcessTiming)).intValue = (int)EProcessStepTiming.Update;
                other.ApplyModifiedProperties();
                // The bindings look for a change made outside them on a timer of their own
                var until = EditorApplication.timeSinceStartup + 1.0;
                while (method.ClassListContains("pf-hidden") && EditorApplication.timeSinceStartup < until) yield return null;
                Assert.IsFalse(method.ClassListContains("pf-hidden"), "one that steps has");
                Assert.IsTrue(priority.ClassListContains("pf-hidden"), "and Priority only for Manual");
                other.Update();
                other.FindProperty(nameof(AbstractMonoProcess.PriorityMethod)).intValue = (int)EProcessStepPriorityMethod.Manual;
                other.ApplyModifiedProperties();
                until = EditorApplication.timeSinceStartup + 1.0;
                while (priority.ClassListContains("pf-hidden") && EditorApplication.timeSinceStartup < until) yield return null;
                Assert.IsFalse(priority.ClassListContains("pf-hidden"), "which shows it");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>The Entity inspector's Allow Duplicate Equipped Items toggle opened showing Allow Duplicate Items' value.</summary>
        [Test]
        public void ED_29_Allow_Duplicate_Equipped_Items_shows_its_own_value()
        {
            var entity = ScriptableObject.CreateInstance<EntityIdentity>();
            entity.AllowDuplicateItems = true;
            entity.AllowDuplicateEquippedItems = false;
            var editor = UnityEditor.Editor.CreateEditor(entity);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsFalse(root.Q<Toggle>("AllowDuplicateEquippedItems").value);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(entity);
            }
        }

        /// <summary>
        /// A Local Data row of type ScriptableObject edited the MonoBehaviour value, and one of type MonoBehaviour the
        /// ScriptableObject value, so neither kept what was assigned to it.
        /// </summary>
        [TestCase(EDataWrapperType.ScriptableObject, typeof(ScriptableObject), "scriptableObjectValue")]
        [TestCase(EDataWrapperType.MonoBehaviour, typeof(MonoBehaviour), "monoBehaviourValue")]
        public void ED_30_A_Local_Data_object_row_edits_its_own_value(EDataWrapperType type, System.Type objectType, string value)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            try
            {
                ability.LocalData.Add(new DataWrapper { Type = type });
                var row = new SerializedObject(ability).FindProperty(nameof(Ability.LocalData)).GetArrayElementAtIndex(0);
                var root = new DataWrapperDrawer().CreatePropertyGUI(row);

                var field = root.Query<ObjectField>().ToList().Single(f => f.objectType == objectType);
                Assert.That(field.bindingPath, Does.EndWith(value));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// A Local Data row's key was a tag field under its label, "Key", in a column at most 170 wide: the label, as
        /// wide as an inspector's label column, took the column, and left the tag's box no width to press (ED-68, the
        /// user's report). The key is the row's first column, with no label, and a value that's a tag is drawn the same
        /// way. The row is drawn in a runtime panel, which has no label column, so the label is given an inspector's.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_68_A_Local_Data_rows_key_has_its_column_to_press()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("ED-68 Panel") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                ability.LocalData.Add(new DataWrapper { Type = EDataWrapperType.Tag });
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(ability);
                // As wide as a narrow Inspector
                var inspector = new VisualElement { style = { width = 380 } };
                document.rootVisualElement.Add(inspector);
                var row = new PropertyField(serialized.FindProperty(nameof(Ability.LocalData)).GetArrayElementAtIndex(0));
                inspector.Add(row);
                row.Bind(serialized);
                for (int i = 0; i < 4; i++) yield return null;

                var tags = row.Query(name: "TagField").ToList();
                Assert.AreEqual(2, tags.Count, "the key and a tag's value are tag fields");
                var key = tags[0];
                var label = key.Q<Label>(className: BaseField<string>.labelUssClassName);
                if (label != null)
                {
                    // An inspector's label column (Docs/EDITOR_STYLE.md), in a row as Unity's theme lays a field out
                    key.style.flexDirection = FlexDirection.Row;
                    label.style.width = 148;
                    label.style.flexShrink = 0;
                    for (int i = 0; i < 4; i++) yield return null;
                }

                var box = key.Q(className: "pf-tag-field__box");
                Assert.That(box.layout.width, Is.GreaterThan(100f), $"the key's box has its column: {box.layout} in {key.layout}");
                Assert.IsNull(label, "with no label");
                Assert.IsNull(tags[1].Q<Label>(className: BaseField<string>.labelUssClassName), "and a tag's value has none");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(ability);
            }
        }
    }
}
