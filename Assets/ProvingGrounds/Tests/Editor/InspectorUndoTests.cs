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
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Undo and multi-editing in the asset inspectors (ED-12). Each test builds an inspector as the Inspector builds it
    /// and adds it whole to a runtime panel, since batch mode has no graphics device to give an editor window one, so
    /// its bindings run there.
    /// </summary>
    public class InspectorUndoTests
    {
        // One of the Attribute inspector's text fields: its name in the inspector, and how to read it from the asset
        public sealed class AttributeText
        {
            public string Field;
            public System.Func<Attribute, string> Get;
            public override string ToString() => Field;
        }

        private static IEnumerable<AttributeText> AttributeTexts()
        {
            yield return new AttributeText { Field = "Name", Get = a => a.Name };
            yield return new AttributeText { Field = "Abbreviation", Get = a => a.Abbreviation };
            yield return new AttributeText { Field = "Description", Get = a => a.Description };
        }

        // A runtime panel to build an inspector in, and the inspector itself
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("ED-12") { hideFlags = HideFlags.HideAndDontSave };
            private UnityEditor.Editor editor;

            public UIDocument Document { get; }
            public UnityEditor.Editor Editor => editor;

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Inspect(params Object[] targets)
            {
                editor = UnityEditor.Editor.CreateEditor(targets);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                Document.rootVisualElement.Add(root);
                // The Inspector binds what an editor builds to its SerializedObject
                root.Bind(editor.serializedObject);
                return root;
            }

            public void Destroy()
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static Attribute MakeAttribute(string name, string abbreviation, string description)
        {
            var attribute = ScriptableObject.CreateInstance<Attribute>();
            attribute.Name = name;
            attribute.Abbreviation = abbreviation;
            attribute.Description = description;
            return attribute;
        }

        // Types a value into a text field and moves the focus away, as a user commits an edit
        private static void Commit(TextField field, string value)
        {
            field.value = value;
            using var focusOut = FocusOutEvent.GetPooled(field, null, FocusChangeDirection.unspecified,
                field.panel.focusController);
            field.SendEvent(focusOut);
        }

        // Lets the panel run its bindings, which the editor's updates drive
        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        // Presses a button, as a user does
        private static void Press(Button button)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        // The inspectors' link questions, answered while a test runs, since a dialog would block it. Each question's
        // message is kept, and the answer is yes unless the test says no.
        private class AnswerLinks : System.IDisposable
        {
            private static readonly FieldInfo Hook =
                typeof(BasePlayForgeEditor).GetField("ConfirmLinkChange", BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object original;

            public List<string> Messages { get; } = new List<string>();

            public AnswerLinks(bool answer)
            {
                Assert.IsNotNull(Hook, "the inspectors ask through a hook a test can answer");
                original = Hook.GetValue(null);
                Hook.SetValue(null, (System.Func<string, string, string, bool>)((_, message, _) =>
                {
                    Messages.Add(message);
                    return answer;
                }));
            }

            public void Dispose() => Hook.SetValue(null, original);
        }

        private sealed class AnswerLinksYes : AnswerLinks
        {
            public AnswerLinksYes() : base(true) { }
        }

        // Calls one of the base inspector's internal steps, as the provider and template pickers do
        private static void Call(UnityEditor.Editor editor, string step, params object[] arguments)
        {
            var method = typeof(BasePlayForgeEditor).GetMethod(step, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"the pickers go through {step}");
            method.Invoke(editor, arguments);
        }

        /// <summary>
        /// Linking wrote into the assets with no Undo: the provider picker's link, the Effect inspector's Link Contained
        /// Effects and Unlink buttons, and the template picker's Assign.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_Linking_in_the_Effect_inspector_undoes()
        {
            var provider = ScriptableObject.CreateInstance<Ability>();
            provider.Definition.Name = "Provider";
            var contained = ScriptableObject.CreateInstance<GameplayEffect>();
            var template = ScriptableObject.CreateInstance<GameplayEffect>();
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.ImpactSpecification ??= new GameplayEffectImpact();
            effect.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { ContainedEffect = contained } };
            var host = new Host();
            var answers = new AnswerLinksYes();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(effect);
                yield return Frames();

                Undo.IncrementCurrentGroup();
                Call(host.Editor, "LinkTo", effect, provider);
                Assert.AreSame(provider, effect.LinkedProvider, "the picker links the effect");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EEffectLinkMode.Standalone, effect.LinkMode, "one Undo takes back the link");

                effect.LinkToProvider(provider);
                yield return Frames();
                var linkContained = root.Query<Button>().ToList().FirstOrDefault(button => button.text == "Link Contained Effects");
                Assert.IsNotNull(linkContained, "Level Source offers Link Contained Effects");
                Undo.IncrementCurrentGroup();
                Press(linkContained);
                Assert.AreSame(provider, contained.LinkedProvider, "the contained effect is linked");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EEffectLinkMode.Standalone, contained.LinkMode, "one Undo takes back the contained effect's link");

                yield return Frames();
                var unlink = root.Query<Button>().ToList().FirstOrDefault(button => button.text == "Unlink");
                Assert.IsNotNull(unlink, "a linked effect offers Unlink");
                Undo.IncrementCurrentGroup();
                Press(unlink);
                Assert.AreEqual(EEffectLinkMode.Standalone, effect.LinkMode, "Unlink unlinks the effect");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreSame(provider, effect.LinkedProvider, "one Undo links it again");

                Undo.IncrementCurrentGroup();
                Call(host.Editor, "AssignTemplate", template);
                Assert.AreSame(template, effect.Template, "the template picker assigns the template");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsNull(effect.Template, "one Undo takes it back");
            }
            finally
            {
                answers.Dispose();
                host.Destroy();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(contained);
                Object.DestroyImmediate(provider);
            }
        }

        /// <summary>
        /// The Attribute inspector wrote its Name, Abbreviation and Description straight into the asset when a field lost
        /// focus, and recorded no Undo, so Undo took back none of them, and the header kept the edit.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_An_edit_in_the_Attribute_inspector_undoes()
        {
            var attribute = MakeAttribute("Health", "HP", "How much damage it can take.");
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(attribute);
                yield return Frames();

                foreach (var text in AttributeTexts())
                {
                    var field = root.Q<TextField>(text.Field);
                    Assert.IsNotNull(field, $"the inspector shows {text}");
                    var before = text.Get(attribute);

                    Undo.IncrementCurrentGroup();
                    Commit(field, before + " edited");
                    Assert.AreEqual(before + " edited", text.Get(attribute), $"{text} is written");

                    Undo.FlushUndoRecordObjects();
                    Undo.PerformUndo();
                    Assert.AreEqual(before, text.Get(attribute), $"one Undo takes back {text}");

                    yield return Frames();
                    Assert.AreEqual(before, field.value, $"and {text}'s field shows it");
                }

                Assert.AreEqual("Health (HP)", root.Q<Label>("HeaderName").text, "the header shows what Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(attribute);
            }
        }

        /// <summary>
        /// No PlayForge inspector edited more than one asset: each refused a multiple selection. The Attribute inspector
        /// takes one, shows a field the selection disagrees on as mixed, and writes an edit to every selected attribute.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_The_Attribute_inspector_edits_every_selected_attribute()
        {
            Assert.IsTrue(System.Attribute.IsDefined(typeof(AttributeEditor), typeof(CanEditMultipleObjects)),
                "the Inspector gives it a multiple selection");

            var health = MakeAttribute("Health", "HP", "");
            var mana = MakeAttribute("Mana", "MP", "");
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(health, mana);
                yield return Frames();

                var nameField = root.Q<TextField>("Name");
                Assert.IsNotNull(nameField, "the inspector edits a multiple selection");
                Assert.IsTrue(nameField.showMixedValue, "names that differ show as mixed");
                Assert.AreEqual("2 selected", root.Q<Label>("HeaderName").text, "the header counts the selection");
                var oneAssetActions = root.Query<Button>().ToList()
                    .Where(button => button.tooltip is "Set template asset" or "Visualize" or "Open in The Forge");
                Assert.IsEmpty(oneAssetActions, "actions on one asset leave the header");

                Undo.IncrementCurrentGroup();
                Commit(nameField, "Stamina");
                Assert.AreEqual("Stamina", health.Name, "the edit reaches the first attribute");
                Assert.AreEqual("Stamina", mana.Name, "and the second");
                Assert.AreEqual("HP", health.Abbreviation, "a field not edited keeps each one's value");
                Assert.AreEqual("MP", mana.Abbreviation);

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Health", health.Name, "one Undo takes it back from both");
                Assert.AreEqual("Mana", mana.Name);
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(health);
                Object.DestroyImmediate(mana);
            }
        }

        /// <summary>
        /// The Effect inspector wrote its Description and Link Mode straight into the asset, with no Undo, and drew its
        /// Level Source section from the link mode once, so the section kept showing a mode an Undo had taken away.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_An_edit_in_the_Effect_inspector_undoes()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition.Name = "Burning";
            effect.Definition.Description = "Fire damage over time.";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(effect);
                yield return Frames();

                var description = root.Q<TextField>("Description");
                Assert.IsNotNull(description, "the inspector shows Description");
                Undo.IncrementCurrentGroup();
                Commit(description, "Burns.");
                Assert.AreEqual("Burns.", effect.Definition.Description, "Description is written");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Fire damage over time.", effect.Definition.Description, "one Undo takes back Description");
                yield return Frames();
                Assert.AreEqual("Fire damage over time.", description.value, "and its field shows it");

                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("linking-buttons"), Is.Null, "a standalone effect offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EEffectLinkMode.LinkedToProvider;
                Assert.AreEqual(EEffectLinkMode.LinkedToProvider, effect.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("linking-buttons"), "Level Source offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EEffectLinkMode.Standalone, effect.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("linking-buttons"), "and Level Source shows the mode Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// The Item inspector wrote its Description and Link Mode straight into the asset, with no Undo, and drew its
        /// Level Source section from the link mode once, so the section kept showing a mode an Undo had taken away.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_An_edit_in_the_Item_inspector_undoes()
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.Definition.Name = "Torch";
            item.Definition.Description = "Lights the way.";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(item);
                yield return Frames();

                var description = root.Q<TextField>("Description");
                Assert.IsNotNull(description, "the inspector shows Description");
                Undo.IncrementCurrentGroup();
                Commit(description, "Burns out.");
                Assert.AreEqual("Burns out.", item.Definition.Description, "Description is written");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Lights the way.", item.Definition.Description, "one Undo takes back Description");
                yield return Frames();
                Assert.AreEqual("Lights the way.", description.value, "and its field shows it");

                // Leveling holds the level link since ED-37, with the provider in an object field
                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("LevelProvider"), Is.Null, "a standalone item offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EItemLinkMode.LinkedToProvider;
                Assert.AreEqual(EItemLinkMode.LinkedToProvider, item.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("LevelProvider"), "Leveling offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EItemLinkMode.Standalone, item.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("LevelProvider"), "and Leveling shows the mode Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(item);
            }
        }

        /// <summary>
        /// The Item inspector showed its level provider as a name in a tinted box, under a row of Link to buttons, one per
        /// type of provider (ED-37). Leveling shows it in Unity's object field: choosing a provider links the item, one
        /// Undo takes the link back, a provider that would make a loop is refused, and a help box says when none is set.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_37_The_Level_Provider_field_links_the_item()
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.Definition.Name = "Torch";
            item.LinkMode = EItemLinkMode.LinkedToProvider;
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            hero.Name = "Hero";
            hero.MaxLevel = 20;
            // An effect linked to the torch, so linking the torch to it would make a loop
            var burning = ScriptableObject.CreateInstance<GameplayEffect>();
            burning.LinkToProvider(item);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(item);
                yield return Frames();

                var provider = root.Q<ObjectField>("LevelProvider");
                Assert.IsNotNull(provider, "Leveling shows the provider in an object field");
                Assert.That(root.Query<HelpBox>().ToList().Select(box => box.text), Has.Some.StartsWith("No level provider"),
                    "and a help box says none is set");

                Undo.IncrementCurrentGroup();
                provider.value = hero;
                Assert.AreEqual(hero, item.LinkedProvider, "choosing a provider links the item");
                yield return Frames();
                Assert.AreEqual(20, root.Q<IntegerField>("MaxLevel")?.value, "and Leveling shows the provider's range");
                Assert.That(root.Query<HelpBox>().ToList(), Is.Empty, "with no help box");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsNull(item.LinkedProvider, "one Undo takes the link back");
                yield return Frames();
                provider = root.Q<ObjectField>("LevelProvider");
                Assert.IsNull(provider.value, "and the field shows it");

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("circular dependency"));
                provider.value = burning;
                Assert.IsNull(item.LinkedProvider, "a link that would make a loop is refused");
                yield return Frames();
                Assert.IsNull(root.Q<ObjectField>("LevelProvider").value, "and the field goes back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(hero);
                Object.DestroyImmediate(burning);
            }
        }

        /// <summary>
        /// The Entity inspector wrote its Description, Starting and Max Level, Activation Policy, three Allow Duplicate
        /// toggles and Link Mode straight into the asset, with no Undo, and drew Level Source from the link mode once.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_An_edit_in_the_Entity_inspector_undoes()
        {
            var entity = ScriptableObject.CreateInstance<EntityIdentity>();
            entity.Name = "Goblin";
            entity.Description = "Small and mean.";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(entity);
                yield return Frames();

                var description = root.Q<TextField>("Description");
                Assert.IsNotNull(description, "the inspector shows Description");
                Undo.IncrementCurrentGroup();
                Commit(description, "Big and mean.");
                Assert.AreEqual("Big and mean.", entity.Description, "Description is written");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Small and mean.", entity.Description, "one Undo takes back Description");
                yield return Frames();
                Assert.AreEqual("Small and mean.", description.value, "and its field shows it");

                var startingLevel = root.Q<IntegerField>("StartingLevel");
                var maxLevel = root.Q<IntegerField>("MaxLevel");
                var policy = root.Q<EnumField>("ActivationPolicy");
                var duplicateAbilities = root.Q<Toggle>("AllowDuplicates");
                var duplicateItems = root.Q<Toggle>("AllowDuplicateItems");
                var duplicateEquipped = root.Q<Toggle>("AllowDuplicateEquippedItems");
                Assert.IsNotNull(startingLevel, "the inspector shows Starting Level");
                Assert.IsNotNull(maxLevel, "and Max Level");
                Assert.IsNotNull(policy, "and Activation Policy");
                Assert.IsNotNull(duplicateAbilities, "and Allow Duplicate Abilities");
                Assert.IsNotNull(duplicateItems, "and Allow Duplicate Items");
                Assert.IsNotNull(duplicateEquipped, "and Allow Duplicate Equipped Items");

                var edits = new (string label, System.Action edit, System.Func<object> stored, System.Func<object> shown)[]
                {
                    ("Starting Level", () => startingLevel.value = entity.StartingLevel + 4,
                        () => entity.StartingLevel, () => startingLevel.value),
                    ("Max Level", () => maxLevel.value = entity.MaxLevel - 9, () => entity.MaxLevel, () => maxLevel.value),
                    ("Activation Policy", () => policy.value =
                            entity.ActivationPolicy == EAbilityActivationPolicy.AlwaysActivate
                                ? EAbilityActivationPolicy.ActivateIfIdle
                                : EAbilityActivationPolicy.AlwaysActivate,
                        () => entity.ActivationPolicy, () => policy.value),
                    ("Allow Duplicate Abilities", () => duplicateAbilities.value = !entity.AllowDuplicateAbilities,
                        () => entity.AllowDuplicateAbilities, () => duplicateAbilities.value),
                    ("Allow Duplicate Items", () => duplicateItems.value = !entity.AllowDuplicateItems,
                        () => entity.AllowDuplicateItems, () => duplicateItems.value),
                    ("Allow Duplicate Equipped Items", () => duplicateEquipped.value = !entity.AllowDuplicateEquippedItems,
                        () => entity.AllowDuplicateEquippedItems, () => duplicateEquipped.value),
                };
                foreach (var (label, edit, stored, shown) in edits)
                {
                    var before = stored();
                    Undo.IncrementCurrentGroup();
                    edit();
                    Assert.AreNotEqual(before, stored(), $"{label} is written");
                    Undo.FlushUndoRecordObjects();
                    Undo.PerformUndo();
                    Assert.AreEqual(before, stored(), $"one Undo takes back {label}");
                    yield return Frames();
                    Assert.AreEqual(before, shown(), $"and {label}'s field shows it");
                }

                // Leveling holds the level link since ED-37, with the provider in an object field
                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("LevelProvider"), Is.Null, "a standalone entity offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EItemLinkMode.LinkedToProvider;
                Assert.AreEqual(EItemLinkMode.LinkedToProvider, entity.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("LevelProvider"), "Leveling offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EItemLinkMode.Standalone, entity.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("LevelProvider"), "and Leveling shows the mode Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(entity);
            }
        }

        /// <summary>
        /// The Entity inspector let Allow Duplicate Equipped Items be turned on while Allow Duplicate Items was off,
        /// though an inventory that holds one of each item never has a second copy to equip (ED-37). As the Entity mockup
        /// draws it, Equipped Too sits under Allow Duplicates and waits for it, with a note saying so.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_37_Equipped_Too_needs_Allow_Duplicates()
        {
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            goblin.Name = "Goblin";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(goblin);
                yield return Frames();

                var duplicates = root.Q<Toggle>("AllowDuplicateItems");
                var equipped = root.Q<Toggle>("AllowDuplicateEquippedItems");
                Assert.AreEqual("Allow Duplicates", duplicates?.label, "Items shows Allow Duplicates");
                Assert.AreEqual("Equipped Too", equipped?.label, "and Equipped Too under it");
                var needs = root.Query<Label>().ToList().FirstOrDefault(label => label.text == "Needs Allow Duplicates");
                Assert.IsNotNull(needs, "with a note");
                Assert.IsFalse(equipped.enabledSelf, "Equipped Too waits for Allow Duplicates");
                Assert.AreEqual(DisplayStyle.Flex, needs.resolvedStyle.display, "and the note says so");

                Undo.IncrementCurrentGroup();
                duplicates.value = true;
                yield return Frames();
                Assert.IsTrue(goblin.AllowDuplicateItems, "Allow Duplicates is written");
                Assert.IsTrue(equipped.enabledSelf, "and Equipped Too can be set");
                Assert.AreEqual(DisplayStyle.None, needs.resolvedStyle.display, "with no note");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(goblin);
            }
        }

        /// <summary>
        /// A starting item drew as a foldout holding its Item and Equip On Init (ED-37). It takes one row, as the Entity
        /// mockup draws it: the item, then an Equip checkbox, which writes through its binding, so one Undo takes it back.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_37_A_starting_item_takes_one_row()
        {
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            var cleaver = ScriptableObject.CreateInstance<Item>();
            goblin.StartingItems.Add(new StartingItemContainer { Item = cleaver });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(goblin);
                var field = new PropertyField(serialized.FindProperty($"{nameof(EntityIdentity.StartingItems)}.Array.data[0]"));
                host.Document.rootVisualElement.Add(field);
                field.Bind(serialized);
                yield return Frames();

                Assert.IsNull(field.Q<Foldout>(), "a starting item doesn't fold");
                var equip = field.Q<Toggle>(nameof(StartingItemContainer.EquipOnInit));
                Assert.IsNotNull(equip, "it shows Equip");
                Assert.AreEqual("Equip", equip.text, "beside the item");

                Undo.IncrementCurrentGroup();
                equip.value = true;
                Assert.IsTrue(goblin.StartingItems[0].EquipOnInit, "Equip is written");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsFalse(goblin.StartingItems[0].EquipOnInit, "one Undo takes it back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(goblin);
                Object.DestroyImmediate(cleaver);
            }
        }

        /// <summary>
        /// The Attribute Set inspector wrote its Description and Link Mode straight into the asset, with no Undo, and
        /// drew Level Source from the link mode once, so the section kept showing a mode an Undo had taken away.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_An_edit_in_the_Attribute_Set_inspector_undoes()
        {
            var attributeSet = ScriptableObject.CreateInstance<AttributeSet>();
            attributeSet.Name = "Hero";
            attributeSet.Description = "A hero's base attributes.";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(attributeSet);
                yield return Frames();

                var description = root.Q<TextField>("Description");
                Assert.IsNotNull(description, "the inspector shows Description");
                Undo.IncrementCurrentGroup();
                Commit(description, "A villain's.");
                Assert.AreEqual("A villain's.", attributeSet.Description, "Description is written");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("A hero's base attributes.", attributeSet.Description, "one Undo takes back Description");
                yield return Frames();
                Assert.AreEqual("A hero's base attributes.", description.value, "and its field shows it");

                // Leveling holds the level link since ED-37, with the provider in an object field
                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("LevelProvider"), Is.Null, "a standalone set offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EAttributeSetLinkMode.LinkedToProvider;
                Assert.AreEqual(EAttributeSetLinkMode.LinkedToProvider, attributeSet.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("LevelProvider"), "Leveling offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EAttributeSetLinkMode.Standalone, attributeSet.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("LevelProvider"), "and Leveling shows the mode Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(attributeSet);
            }
        }

        /// <summary>
        /// The Ability inspector wrote its Description, Activation Policy, Activate Immediately and Link Mode straight
        /// into the asset, with no Undo, and drew Level Source from the link mode once.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_An_edit_in_the_Ability_inspector_undoes()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "Fireball";
            ability.Definition.Description = "Hurls fire.";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(ability);
                yield return Frames();

                var description = root.Q<TextField>("Description");
                Assert.IsNotNull(description, "the inspector shows Description");
                Undo.IncrementCurrentGroup();
                Commit(description, "Hurls ice.");
                Assert.AreEqual("Hurls ice.", ability.Definition.Description, "Description is written");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Hurls fire.", ability.Definition.Description, "one Undo takes back Description");
                yield return Frames();
                Assert.AreEqual("Hurls fire.", description.value, "and its field shows it");

                var policy = root.Q<EnumField>("ActivationPolicy");
                var immediately = root.Q<Toggle>("ActivateImmediately");
                Assert.IsNotNull(policy, "the inspector shows Activation Policy");
                Assert.IsNotNull(immediately, "and Activate Immediately");
                var edits = new (string label, System.Action edit, System.Func<object> stored, System.Func<object> shown)[]
                {
                    ("Activation Policy", () => policy.value =
                            ability.Definition.ActivationPolicy == EAbilityActivationPolicyExtended.AlwaysActivate
                                ? EAbilityActivationPolicyExtended.UseLocalPolicy
                                : EAbilityActivationPolicyExtended.AlwaysActivate,
                        () => ability.Definition.ActivationPolicy, () => policy.value),
                    ("Activate Immediately", () => immediately.value = !ability.Definition.ActivateImmediately,
                        () => ability.Definition.ActivateImmediately, () => immediately.value),
                };
                foreach (var (label, edit, stored, shown) in edits)
                {
                    var before = stored();
                    Undo.IncrementCurrentGroup();
                    edit();
                    Assert.AreNotEqual(before, stored(), $"{label} is written");
                    Undo.FlushUndoRecordObjects();
                    Undo.PerformUndo();
                    Assert.AreEqual(before, stored(), $"one Undo takes back {label}");
                    yield return Frames();
                    Assert.AreEqual(before, shown(), $"and {label}'s field shows it");
                }

                // Leveling holds the level link since ED-37, with the provider in an object field
                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("LevelProvider"), Is.Null, "a standalone ability offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EAbilityLinkMode.LinkedToProvider;
                Assert.AreEqual(EAbilityLinkMode.LinkedToProvider, ability.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("LevelProvider"), "Leveling offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EAbilityLinkMode.Standalone, ability.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("LevelProvider"), "and Leveling shows the mode Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
            }
        }

        // An asset with one child its Unlink All would unlink, and everything made for it
        public sealed class LinkedChild
        {
            public string Label;
            public System.Func<List<Object>, ScriptableObject> Make;
            public override string ToString() => Label;
        }

        private static IEnumerable<LinkedChild> LinkedChildren()
        {
            // An effect unlinks the contained effects linked to its own provider
            yield return new LinkedChild
            {
                Label = "Effect", Make = made =>
                {
                    var provider = ScriptableObject.CreateInstance<Ability>();
                    var contained = ScriptableObject.CreateInstance<GameplayEffect>();
                    var effect = ScriptableObject.CreateInstance<GameplayEffect>();
                    made.AddRange(new Object[] { provider, contained, effect });
                    effect.ImpactSpecification ??= new GameplayEffectImpact();
                    effect.ImpactSpecification.Packets = new[] { new ContainedEffectPacket { ContainedEffect = contained } };
                    effect.LinkToProvider(provider);
                    contained.LinkToProvider(provider);
                    return effect;
                },
            };
            // An ability unlinks its cost and cooldown when they are linked to it
            yield return new LinkedChild
            {
                Label = "Ability", Make = made =>
                {
                    var cost = ScriptableObject.CreateInstance<GameplayEffect>();
                    var ability = ScriptableObject.CreateInstance<Ability>();
                    made.AddRange(new Object[] { cost, ability });
                    ability.Cost = cost;
                    cost.LinkToProvider(ability);
                    return ability;
                },
            };
            // An item unlinks the granted effects and active ability linked to it
            yield return new LinkedChild
            {
                Label = "Item", Make = made =>
                {
                    var granted = ScriptableObject.CreateInstance<GameplayEffect>();
                    var item = ScriptableObject.CreateInstance<Item>();
                    made.AddRange(new Object[] { granted, item });
                    item.GrantedEffects.Add(granted);
                    granted.LinkToProvider(item);
                    return item;
                },
            };
            // An entity unlinks the starting items and abilities linked to it
            yield return new LinkedChild
            {
                Label = "Entity", Make = made =>
                {
                    var starting = ScriptableObject.CreateInstance<Ability>();
                    var entity = ScriptableObject.CreateInstance<EntityIdentity>();
                    made.AddRange(new Object[] { starting, entity });
                    entity.StartingAbilities.Add(starting);
                    starting.LinkToProvider(entity);
                    return entity;
                },
            };
        }

        /// <summary>
        /// Each inspector's Unlink All question counted the children linked to the editor rather than to the asset, so it
        /// always said it would affect 0 (ED-31). It counts the children the unlink would change.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_31_The_unlink_question_counts_the_linked_children()
        {
            foreach (var child in LinkedChildren())
            {
                var made = new List<Object>();
                var asset = child.Make(made);
                var host = new Host();
                var answers = new AnswerLinks(false);
                try
                {
                    yield return null;
                    Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                    var root = host.Inspect(asset);
                    yield return Frames();

                    var unlinkAll = root.Query<Button>().ToList().FirstOrDefault(button => button.text.StartsWith("Unlink "));
                    Assert.IsNotNull(unlinkAll, $"the {child} inspector offers Unlink All");
                    Press(unlinkAll);
                    Assert.AreEqual(1, answers.Messages.Count, $"the {child} inspector asks first");
                    StringAssert.Contains("This will affect 1 ", answers.Messages[0], $"and the {child} question counts its linked child");
                }
                finally
                {
                    answers.Dispose();
                    host.Destroy();
                    foreach (var each in made) Object.DestroyImmediate(each);
                }
            }
        }

        // Clears a drawer type's static rebuild state, which is keyed by property path and outlives a test. Its collapse
        // state is kept per asset (ED-18), so a new asset starts as the drawer's default
        private static void ClearDrawerState(System.Type drawer)
        {
            var field = drawer.GetField("RebuildingProperties", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{drawer.Name} keeps RebuildingProperties");
            field.GetValue(null).GetType().GetMethod("Clear").Invoke(field.GetValue(null), null);
        }

        /// <summary>
        /// A drawer that rebuilds itself made a SerializedObject of its own over the first selected asset, so after the
        /// rebuild an edit reached only that asset. The tag requirements drawer rebuilds when it opens; its group's Name
        /// then wrote to the first effect alone.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_A_rebuilt_drawer_edits_every_selected_asset()
        {
            ClearDrawerState(typeof(AbstractTagRequirementsDrawer<EffectTagRequirements>));
            var first = ScriptableObject.CreateInstance<GameplayEffect>();
            var second = ScriptableObject.CreateInstance<GameplayEffect>();
            first.SourceRequirements = new EffectTagRequirements { Name = "A" };
            second.SourceRequirements = new EffectTagRequirements { Name = "B" };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(new Object[] { first, second });
                var field = new PropertyField(serialized.FindProperty(nameof(GameplayEffect.SourceRequirements)));
                host.Document.rootVisualElement.Add(field);
                field.Bind(serialized);
                yield return Frames();

                var expand = field.Query<Button>().ToList().FirstOrDefault(button => button.tooltip == "Expand");
                Assert.IsNotNull(expand, "the requirements start closed");
                Press(expand);
                yield return Frames();
                var groupName = field.Query<TextField>().ToList().FirstOrDefault(text =>
                    text.tooltip == "Optional name for this requirement group (helps identify when importing)");
                Assert.IsNotNull(groupName, "opening them shows the group's Name");

                Undo.IncrementCurrentGroup();
                Commit(groupName, "Shared");
                Assert.AreEqual("Shared", first.SourceRequirements.Name, "the edit reaches the first effect");
                Assert.AreEqual("Shared", second.SourceRequirements.Name, "and the second");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("A", first.SourceRequirements.Name, "one Undo takes it back from both");
                Assert.AreEqual("B", second.SourceRequirements.Name);
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        /// <summary>
        /// The Item inspector refused a multiple selection. It takes one now: a field the items disagree on shows as
        /// mixed and an edit reaches both, while what acts on one item steps aside. Name stays read-only, since each
        /// item's tag comes from its own name, the asset tag row hides, and Level Source offers no link buttons.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_The_Item_inspector_edits_every_selected_item()
        {
            Assert.IsTrue(System.Attribute.IsDefined(typeof(ItemEditor), typeof(CanEditMultipleObjects)),
                "the Inspector gives it a multiple selection");
            var torch = ScriptableObject.CreateInstance<Item>();
            torch.Definition.Name = "Torch";
            torch.Definition.Description = "Lights the way.";
            var lamp = ScriptableObject.CreateInstance<Item>();
            lamp.Definition.Name = "Lamp";
            lamp.Definition.Description = "Burns oil.";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(torch, lamp);
                yield return Frames();

                var description = root.Q<TextField>("Description");
                Assert.IsNotNull(description, "the inspector edits a multiple selection");
                Assert.IsTrue(description.showMixedValue, "descriptions that differ show as mixed");
                Assert.IsFalse(root.Q<TextField>("Name").enabledSelf, "a multiple selection can't rename");
                var linking = root.Query<Button>().ToList()
                    .Where(button => button.text.StartsWith("Link ") || button.text.StartsWith("Unlink")).ToList();
                Assert.IsEmpty(linking, "and linking waits for one item");
                Assert.IsNull(root.Q<Button>("AssetTagUpdate"), "nor does an asset tag offer Update");

                Undo.IncrementCurrentGroup();
                Commit(description, "Shines.");
                Assert.AreEqual("Shines.", torch.Definition.Description, "the edit reaches the first item");
                Assert.AreEqual("Shines.", lamp.Definition.Description, "and the second");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Lights the way.", torch.Definition.Description, "one Undo takes it back from both");
                Assert.AreEqual("Burns oil.", lamp.Definition.Description);
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(torch);
                Object.DestroyImmediate(lamp);
            }
        }

        /// <summary>
        /// The Ability inspector refused a multiple selection. It takes one now: a field the abilities disagree on shows
        /// as mixed and an edit reaches both. What acts on one ability steps aside: Name, the asset tag, linking, and the
        /// lists each ability keeps of its own, its stages, asset loaders and activation rules, which a note replaces.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_The_Ability_inspector_edits_every_selected_ability()
        {
            Assert.IsTrue(System.Attribute.IsDefined(typeof(AbilityEditor), typeof(CanEditMultipleObjects)),
                "the Inspector gives it a multiple selection");
            var fireball = ScriptableObject.CreateInstance<Ability>();
            fireball.Definition.Name = "Fireball";
            fireball.Definition.Description = "Hurls fire.";
            var frost = ScriptableObject.CreateInstance<Ability>();
            frost.Definition.Name = "Frost Bolt";
            frost.Definition.Description = "Hurls ice.";
            frost.Definition.ActivateImmediately = true;
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(fireball, frost);
                yield return Frames();

                var description = root.Q<TextField>("Description");
                Assert.IsNotNull(description, "the inspector edits a multiple selection");
                Assert.IsTrue(description.showMixedValue, "descriptions that differ show as mixed");
                Assert.IsTrue(root.Q<Toggle>("ActivateImmediately").showMixedValue, "and so does Activate Immediately");
                Assert.IsFalse(root.Q<TextField>("Name").enabledSelf, "a multiple selection can't rename");
                var linking = root.Query<Button>().ToList()
                    .Where(button => button.text.StartsWith("Link ") || button.text.StartsWith("Unlink")).ToList();
                Assert.IsEmpty(linking, "linking waits for one ability");
                Assert.IsNull(root.Q<Button>("AssetTagUpdate"), "nor does an asset tag offer Update");
                var ownLists = root.Query<PropertyField>().ToList().Where(field => field.bindingPath is
                    "Behaviour.Stages" or "Behaviour.AssetLoaders" or "SourceActivationRules" or "TargetActivationRules").ToList();
                Assert.IsEmpty(ownLists, "and each ability's own lists wait for one ability");

                Undo.IncrementCurrentGroup();
                Commit(description, "Hurls.");
                Assert.AreEqual("Hurls.", fireball.Definition.Description, "the edit reaches the first ability");
                Assert.AreEqual("Hurls.", frost.Definition.Description, "and the second");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual("Hurls fire.", fireball.Definition.Description, "one Undo takes it back from both");
                Assert.AreEqual("Hurls ice.", frost.Definition.Description);
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fireball);
                Object.DestroyImmediate(frost);
            }
        }

        /// <summary>
        /// The MonoProcess inspector binds every field, but without [CanEditMultipleObjects] the Inspector never gave it
        /// a multiple selection: it showed "Multi-object editing not supported" instead.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_The_MonoProcess_inspector_edits_every_selected_process()
        {
            Assert.IsTrue(System.Attribute.IsDefined(typeof(AbstractMonoProcessEditor), typeof(CanEditMultipleObjects)),
                "the Inspector gives it a multiple selection");

            // Plain scene objects: a SerializedObject over several objects flagged DontSave writes only to the first of
            // them, so HideAndDontSave would hide what the inspector does
            var first = new GameObject("ED-12 first");
            var second = new GameObject("ED-12 second");
            var host = new Host();
            try
            {
                // A process class with a script asset of its own, so PropertyField finds its fields' types
                var a = first.AddComponent<LazyMonoProcess>();
                var b = second.AddComponent<LazyMonoProcess>();
                a.ProcessLifecycle = EProcessLifecycle.SelfTerminating;
                b.ProcessLifecycle = EProcessLifecycle.RunThenWait;

                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(a, b);
                yield return Frames();

                var lifecycle = root.Query<PropertyField>().ToList()
                    .FirstOrDefault(field => field.bindingPath == nameof(AbstractMonoProcess.ProcessLifecycle));
                Assert.IsNotNull(lifecycle, "the inspector shows Lifecycle");
                // PropertyField draws an enum as a popup of the enum's names
                var choice = lifecycle.Q<PopupField<string>>();
                Assert.IsNotNull(choice, "as a choice");
                Assert.IsTrue(choice.showMixedValue, "lifecycles that differ show as mixed");

                Undo.IncrementCurrentGroup();
                choice.index = (int)EProcessLifecycle.Synchronous;
                Assert.AreEqual(EProcessLifecycle.Synchronous, a.ProcessLifecycle, "the edit reaches the first process");
                Assert.AreEqual(EProcessLifecycle.Synchronous, b.ProcessLifecycle, "and the second");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EProcessLifecycle.SelfTerminating, a.ProcessLifecycle, "one Undo takes it back from both");
                Assert.AreEqual(EProcessLifecycle.RunThenWait, b.ProcessLifecycle);
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        // A set whose one element starts Current at 150, above the 100 its bounds allow
        private static AttributeSet OverflowingSet(Attribute rage)
        {
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            set.Name = "Berserker";
            set.Attributes.Add(new AttributeSetElement
            {
                Attribute = rage,
                Current = new AttributeMagnitudeSpec { Magnitude = 150f },
                Base = new AttributeMagnitudeSpec { Magnitude = 100f },
                Overflow = new AttributeOverflowData
                {
                    Policy = EAttributeOverflowPolicy.ZeroToCeil, Ceil = new AttributeValue(100f, 100f),
                },
            });
            return set;
        }

        // Waits real time, for work a drawer schedules
        private static IEnumerator Wait(double seconds)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// Opening an attribute set wrote to it: 50 ms after an element drew, and on every change to the set, the element
        /// drawer clamped a magnitude outside its bounds into the asset, so looking at a set changed it (ED-39). Drawing
        /// writes nothing now, and neither does an edit elsewhere in the set; a help box says the runtime clamps it.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_39_Opening_an_attribute_set_writes_nothing()
        {
            var rage = ScriptableObject.CreateInstance<Attribute>();
            rage.Name = "Rage";
            var set = OverflowingSet(rage);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, set, $"{nameof(AttributeSet.Attributes)}.Array.data[0]");
                yield return Frames();
                yield return Wait(0.3);

                Assert.AreEqual(150f, set.Attributes[0].Current.Magnitude, "drawing the element leaves its Current alone");

                var serialized = new SerializedObject(set);
                serialized.FindProperty(nameof(AttributeSet.Description)).stringValue = "Angry.";
                serialized.ApplyModifiedProperties();
                yield return Frames();
                yield return Wait(0.1);
                Assert.AreEqual(150f, set.Attributes[0].Current.Magnitude, "and so does an edit elsewhere in the set");

                Assert.That(field.Query<HelpBox>().ToList().Select(box => box.text), Has.Some.Contains("outside its bounds"),
                    "a help box says it's outside its bounds");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(set);
                Object.DestroyImmediate(rage);
            }
        }

        /// <summary>
        /// Typing a magnitude outside the bounds keeps it inside them, as part of the same edit, so one Undo takes it back
        /// (ED-39). The element drawer takes its look from the style sheet: it drew a tinted, rounded box with a border in
        /// its collision policy's color, tab buttons and 9 px text, from styles set in C#.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_39_A_typed_magnitude_stays_in_bounds()
        {
            var rage = ScriptableObject.CreateInstance<Attribute>();
            rage.Name = "Rage";
            var set = OverflowingSet(rage);
            set.Attributes[0].Current.Magnitude = 50f;
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, set, $"{nameof(AttributeSet.Attributes)}.Array.data[0]");
                yield return Frames();

                var own = field.Query<VisualElement>().ToList()
                    .Where(element => element.GetFirstAncestorOfType<PropertyField>() == field)
                    .SelectMany(element => ForgeInspectorTests.LookSetInCode(element).Select(style => $"{element.GetType().Name} \"{element.name}\" sets {style}"))
                    .ToList();
                Assert.That(own, Is.Empty, "the drawer sets no look in code");

                var magnitude = field.Q<FloatField>("CurrentMagnitude");
                Assert.IsNotNull(magnitude, "the element shows Current's Magnitude");
                // Typed: the field has the focus as a value goes in
                magnitude.Focus();
                yield return null;
                Undo.IncrementCurrentGroup();
                magnitude.value = 160f;
                yield return Frames();
                Assert.AreEqual(100f, set.Attributes[0].Current.Magnitude, "a typed 160 stays within the bounds' 100");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(50f, set.Attributes[0].Current.Magnitude, "and one Undo takes the edit back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(set);
                Object.DestroyImmediate(rage);
            }
        }

        // A property drawn as the Inspector draws it, in the host's panel
        private static PropertyField Draw(Host host, Object target, string path)
        {
            var serialized = new SerializedObject(target);
            var field = new PropertyField(serialized.FindProperty(path));
            host.Document.rootVisualElement.Add(field);
            field.Bind(serialized);
            return field;
        }

        /// <summary>
        /// An asset reference was a button that opened a dropdown list of its own, with hover colors and Clear and Open
        /// icon buttons set in C# (ED-38). It is Unity's object field, with Open beside it once it names an asset and New
        /// while it's empty, as Docs/EDITOR_STYLE.md asks, and it sets no look in code.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_38_An_asset_reference_is_Unity_s_object_field()
        {
            var stats = ScriptableObject.CreateInstance<AttributeSet>();
            stats.Name = "Goblin Stats";
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            goblin.AttributeSet = stats;
            var empty = ScriptableObject.CreateInstance<EntityIdentity>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var set = Draw(host, goblin, nameof(EntityIdentity.AttributeSet));
                var none = Draw(host, empty, nameof(EntityIdentity.AttributeSet));
                yield return Frames();

                var objectField = set.Q<ObjectField>();
                Assert.IsNotNull(objectField, "the reference is Unity's object field");
                Assert.AreEqual(typeof(AttributeSet), objectField.objectType, "of its type");
                Assert.AreEqual(stats, objectField.value, "showing the set");
                Assert.IsNull(set.Q<Button>("ValueButton"), "with no dropdown button");
                Assert.That(set.Query<Button>().ToList().Select(button => button.text), Has.Member("Open"), "Open beside a set");
                Assert.That(none.Query<Button>().ToList().Select(button => button.text), Has.Member("New"), "and New beside an empty field");

                var look = new[] { set, none }.SelectMany(field => field.Query<VisualElement>().ToList())
                    .SelectMany(element => ForgeInspectorTests.LookSetInCode(element).Select(style => $"{element.GetType().Name} \"{element.name}\" sets {style}"))
                    .ToList();
                Assert.That(look, Is.Empty, "no look set in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(goblin);
                Object.DestroyImmediate(empty);
                Object.DestroyImmediate(stats);
            }
        }

        /// <summary>
        /// New makes an asset of the reference's type where The Forge makes that type, named for the asset and the field,
        /// with the tag its name gives, and the field takes it, as one Undo step (ED-38).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_38_New_makes_an_asset_and_uses_it()
        {
            const string folder = "Assets/ED-38 New";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-38 New");
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            goblin.Name = "Goblin";
            AssetDatabase.CreateAsset(goblin, $"{folder}/Goblin.asset");
            var host = new Host();
            string made = null;
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, goblin, nameof(EntityIdentity.AttributeSet));
                yield return Frames();

                var create = field.Query<Button>().ToList().FirstOrDefault(button => button.text == "New");
                Assert.IsNotNull(create, "an empty reference offers New");
                Undo.IncrementCurrentGroup();
                Press(create);
                yield return Frames();

                var set = goblin.AttributeSet;
                Assert.IsNotNull(set, "New makes a set and uses it");
                made = AssetDatabase.GetAssetPath(set);
                Assert.IsNotEmpty(made, "as an asset");
                Assert.AreEqual("Goblin Attribute Set", set.Name, "named for the entity and the field");
                Assert.That(set.AssetTag.Name, Does.Contain("GoblinAttributeSet"), "with the tag its name gives");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsNull(goblin.AttributeSet, "one Undo takes the reference back");
            }
            finally
            {
                host.Destroy();
                if (!string.IsNullOrEmpty(made)) AssetDatabase.DeleteAsset(made);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// Setting an effect field asked in a dialog whether to link the effect to the asset that holds it, and again when
        /// it was linked elsewhere (ED-38). Setting it asks nothing: a line under the field says how the effect is linked
        /// and offers Link, which links it as one Undo step.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_38_An_effect_reference_offers_its_link_in_place()
        {
            var fireball = ScriptableObject.CreateInstance<Ability>();
            fireball.Definition.Name = "Fireball";
            var cost = ScriptableObject.CreateInstance<GameplayEffect>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, fireball, nameof(Ability.Cost));
                yield return Frames();

                Undo.IncrementCurrentGroup();
                field.Q<ObjectField>().value = cost;
                yield return Frames();
                Assert.AreEqual(cost, fireball.Cost, "the cost is set");
                Assert.IsFalse(cost.IsLinked, "and setting it asks nothing and links nothing");
                var link = field.Query<Button>().ToList().FirstOrDefault(button => button.text == "Link");
                Assert.IsNotNull(link, "the field offers Link in place");

                Undo.IncrementCurrentGroup();
                Press(link);
                Assert.IsTrue(cost.IsLinkedTo(fireball), "Link links the cost to the ability");
                yield return Frames();
                Assert.That(field.Query<Label>().ToList().Select(label => label.text), Has.Some.StartsWith("Linked to Fireball"),
                    "and the line says so");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsFalse(cost.IsLinked, "one Undo takes the link back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fireball);
                Object.DestroyImmediate(cost);
            }
        }

        // An effect as a test gives it: a magnitude of -8, a duration of 6 s, and the ticks it names
        private static GameplayEffect MakeEffect(EEffectDurationPolicy policy, bool periodic = false, int ticks = 0,
            bool tickOnApplication = false)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition.Name = "Burning";
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                MagnitudeOperation = new ScalerMagnitudeOperation { Magnitude = -8f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = policy,
                DurationOperation = new ScalerMagnitudeOperation { Magnitude = 6f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                EnablePeriodicTicks = periodic,
                TickOnApplication = tickOnApplication,
                TicksOperation = new ScalerIntegerMagnitudeOperation { Magnitude = ticks },
                TickIntervalOperation = new ScalerMagnitudeOperation { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            };
            return effect;
        }

        // The value a read-only row shows
        private static string Shown(VisualElement root, string name) => root.Q<TextField>(name)?.value;

        // The looks a drawer sets in code, outside the drawers nested in it
        private static List<string> OwnLook(PropertyField field) => field.Query<VisualElement>().ToList()
            .Where(element => element.GetFirstAncestorOfType<PropertyField>() == field)
            .SelectMany(element => ForgeInspectorTests.LookSetInCode(element).Select(style => $"{element.GetType().Name} \"{element.name}\" sets {style}"))
            .ToList();

        /// <summary>
        /// The impact didn't say how it is delivered, and still drew Reverse On Removal, which the impact model removed
        /// (ED-2). It says its delivery, which the duration gives it, and what a periodic impact commits, and it follows
        /// the duration as it changes. It sets no look in code: it drew tinted boxes with colored headings.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_2_The_impact_says_how_it_is_delivered()
        {
            var health = MakeAttribute("Health", "HP", "");
            var burning = MakeEffect(EEffectDurationPolicy.Durational, periodic: true, ticks: 6, tickOnApplication: true);
            burning.ImpactSpecification.AttributeTarget = health;
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, burning, nameof(GameplayEffect.ImpactSpecification));
                yield return Frames();

                Assert.AreEqual("Each tick, and on applying", Shown(field, "Delivery"), "a periodic impact that also ticks on applying");
                Assert.AreEqual("7 over 6 s, level 1", Shown(field, "Commits"), "six ticks and one on applying, over 6 s");
                Assert.That(field.Query<PropertyField>().ToList()
                        .Where(nested => nested.bindingPath.EndsWith(nameof(GameplayEffectImpact.ReverseImpactOnRemoval))), Is.Empty,
                    "Reverse On Removal is gone");
                Assert.That(OwnLook(field), Is.Empty, "the drawer sets no look in code");

                var serialized = new SerializedObject(burning);
                serialized.FindProperty($"{nameof(GameplayEffect.DurationSpecification)}.{nameof(GameplayEffectDuration.DurationPolicy)}")
                    .enumValueIndex = (int)EEffectDurationPolicy.Instant;
                serialized.ApplyModifiedProperties();
                yield return Frames();
                Assert.AreEqual("On applying", Shown(field, "Delivery"), "an Instant effect's impact lands as it's applied");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(burning);
                Object.DestroyImmediate(health);
            }
        }

        /// <summary>
        /// An impact entry had no drawer, so an effect couldn't be given a second impact (ED-3). An additional impact draws
        /// as a row with its attribute and, while folded, one line such as "Mana −10 · Continuous". Its Delivery offers
        /// what the effect's duration can make, and a help box says when the one it names can't happen.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_3_An_additional_impact_draws_as_an_entry()
        {
            var mana = MakeAttribute("Mana", "MP", "");
            var drain = MakeEffect(EEffectDurationPolicy.Instant);
            drain.ImpactSpecification.AdditionalImpacts.Add(new AttributeImpactEntry
            {
                AttributeTarget = mana,
                Delivery = EImpactDelivery.Continuous,
                Magnitude = new ScalerMagnitudeOperation { Magnitude = -10f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, drain, nameof(GameplayEffect.ImpactSpecification));
                yield return Frames();

                var entry = field.Q(name: "ImpactEntry");
                Assert.IsNotNull(entry, "the additional impact draws as an entry");
                Assert.AreEqual("Mana −10 · Continuous", entry.Q<Label>("Summary")?.text, "its line says what it does");
                var delivery = entry.Q<PopupField<EImpactDelivery>>("Delivery");
                Assert.IsNotNull(delivery, "it offers a Delivery");
                Assert.That(delivery.choices, Is.EqualTo(new[] { EImpactDelivery.OnApply, EImpactDelivery.Continuous }),
                    "an Instant effect makes On Apply impacts, and the Continuous it names stays shown");
                Assert.That(entry.Query<HelpBox>().ToList().Select(box => box.text), Has.Some.Contains("never applies"),
                    "and a help box says it can't happen");
                Assert.That(OwnLook(entry.GetFirstAncestorOfType<PropertyField>()), Is.Empty, "the entry sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(drain);
                Object.DestroyImmediate(mana);
            }
        }

        /// <summary>
        /// Add Impact adds an impact the effect's duration can make, and an entry's Remove takes it away; one Undo takes
        /// back each (ED-3).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_3_An_impact_is_added_and_removed_in_place()
        {
            var burning = MakeEffect(EEffectDurationPolicy.Durational, periodic: true, ticks: 2);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, burning, nameof(GameplayEffect.ImpactSpecification));
                yield return Frames();

                var add = field.Q<Button>("AddImpact");
                Assert.IsNotNull(add, "the impact offers Add Impact");
                Undo.IncrementCurrentGroup();
                Press(add);
                yield return Frames();
                var impacts = burning.ImpactSpecification.AdditionalImpacts;
                Assert.AreEqual(1, impacts.Count, "Add Impact adds one");
                Assert.AreEqual(EImpactDelivery.Continuous, impacts[0].Delivery, "which lasts while the effect does, as a durational effect's can");
                Assert.AreEqual(EMagnitudeOperation.UseMagnitude, impacts[0].Magnitude.RealMagnitude, "and uses its own magnitude");
                var entry = field.Q(name: "ImpactEntry");
                Assert.IsNotNull(entry, "and draws it");

                Undo.IncrementCurrentGroup();
                Press(entry.Q<Button>("RemoveImpact"));
                yield return Frames();
                Assert.AreEqual(0, burning.ImpactSpecification.AdditionalImpacts.Count, "Remove takes it away");
                Assert.IsNull(field.Q(name: "ImpactEntry"), "and its entry");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(1, burning.ImpactSpecification.AdditionalImpacts.Count, "one Undo brings it back");
                Undo.PerformUndo();
                Assert.AreEqual(0, burning.ImpactSpecification.AdditionalImpacts.Count, "and another takes back the add");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(burning);
            }
        }
    }
}
