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

                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("linking-buttons"), Is.Null, "a standalone item offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EItemLinkMode.LinkedToProvider;
                Assert.AreEqual(EItemLinkMode.LinkedToProvider, item.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("linking-buttons"), "Level Source offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EItemLinkMode.Standalone, item.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("linking-buttons"), "and Level Source shows the mode Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(item);
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

                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("linking-buttons"), Is.Null, "a standalone entity offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EItemLinkMode.LinkedToProvider;
                Assert.AreEqual(EItemLinkMode.LinkedToProvider, entity.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("linking-buttons"), "Level Source offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EItemLinkMode.Standalone, entity.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("linking-buttons"), "and Level Source shows the mode Undo brought back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(entity);
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

                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("linking-buttons"), Is.Null, "a standalone set offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EAttributeSetLinkMode.LinkedToProvider;
                Assert.AreEqual(EAttributeSetLinkMode.LinkedToProvider, attributeSet.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("linking-buttons"), "Level Source offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EAttributeSetLinkMode.Standalone, attributeSet.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("linking-buttons"), "and Level Source shows the mode Undo brought back");
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

                var linkMode = root.Query<EnumField>().ToList().FirstOrDefault(field => field.label == "Link Mode");
                Assert.IsNotNull(linkMode, "the inspector shows Link Mode");
                Assume.That(root.Q("linking-buttons"), Is.Null, "a standalone ability offers no provider");
                Undo.IncrementCurrentGroup();
                linkMode.value = EAbilityLinkMode.LinkedToProvider;
                Assert.AreEqual(EAbilityLinkMode.LinkedToProvider, ability.LinkMode, "Link Mode is written");
                yield return Frames();
                Assert.IsNotNull(root.Q("linking-buttons"), "Level Source offers a provider");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(EAbilityLinkMode.Standalone, ability.LinkMode, "one Undo takes back Link Mode");
                yield return Frames();
                Assert.IsNull(root.Q("linking-buttons"), "and Level Source shows the mode Undo brought back");
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

        // Clears a drawer type's static collapse and rebuild state, which is keyed by property path and outlives a test
        private static void ClearDrawerState(System.Type drawer)
        {
            foreach (var name in new[] { "CollapsedStates", "RebuildingProperties" })
            {
                var field = drawer.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(field, $"{drawer.Name} keeps {name}");
                field.GetValue(null).GetType().GetMethod("Clear").Invoke(field.GetValue(null), null);
            }
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
    }
}
