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
                    AssertEachSectionImportsAndClears(sections);
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

        // Every section but Used By imports and clears: their buttons only logged a warning (ED-37)
        private static void AssertEachSectionImportsAndClears(List<VisualElement> sections)
        {
            foreach (var section in sections.Where(section => section.name != "UsedBy_CONTAINER"))
            {
                var actions = section.Query<Button>(className: "pf-section__action").ToList().Select(button => button.tooltip).ToList();
                Assert.That(actions, Has.Some.StartsWith("Import "), $"{section.name} has Import");
                Assert.That(actions, Has.Some.StartsWith("Reset "), $"{section.name} has Clear");
                Assert.That(actions, Has.None.Contains("not configured"), $"{section.name}'s Import and Clear work");
            }
        }

        // The look a control may take only from the style sheet: its colors, borders and text
        private static IEnumerable<string> LookSetInCode(VisualElement element)
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
                    AssertEachSectionImportsAndClears(sections);

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
    }
}
