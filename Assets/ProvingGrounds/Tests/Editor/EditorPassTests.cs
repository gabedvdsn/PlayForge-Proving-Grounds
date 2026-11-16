using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended;
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
    /// The editor pass's smaller notes (the user's of 2026-11-15): The Forge's Declared For shows the contexts a tag's code
    /// declares, checked and locked (EP-7); a field's New asks for the new asset's name first (EP-8); and the asset types
    /// with no icon of their own show PlayForge's, an anvil (EP-9). The kit's types are the editor's own, read by reflection.
    /// </summary>
    public class EditorPassTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly EditorAssembly = typeof(TagDrawer).Assembly;

        private static Type Kit(string name) => EditorAssembly.GetType("FarEmerald.PlayForge.Extended.Editor." + name, true);

        private static object Get(object target, string member) => target.GetType().GetProperty(member, Any)?.GetValue(target);

        private static IEnumerable<object> Items(object dropdown) => ((IEnumerable)Get(dropdown, "Items")).Cast<object>();

        private static object Static(string type, string method, params object[] args) =>
            Kit(type).GetMethods(Any).Single(each => each.Name == method && each.GetParameters().Length == args.Length).Invoke(null, args);

        /// <summary>
        /// A tag's code declares its contexts, as Impact Dealt's [ForgeTagExposed] declares Event: The Forge's Declared For
        /// shows them, its menu checks them and can't take them away, saying why, and they're no Tag Library's to declare.
        /// </summary>
        [Test]
        public void EP_7_Declared_For_shows_the_contexts_a_tags_code_declares_checked_and_locked()
        {
            ForgeTagRegistry.RefreshCache();
            var code = (List<string>)Static("ForgeTagContexts", "CodeContextsOf", Tags.IMPACT_DEALT);
            Assert.That(code, Is.EqualTo(new[] { ForgeContext.Event }), "Impact Dealt's code declares it for Event");

            var field = (BaseField<string>)Activator.CreateInstance(Kit("ForgeTagContextsField"), Any, null,
                new object[] { "Declared For", new List<string>(), code }, null);
            StringAssert.Contains(ForgeContext.Event, field.value, "the field shows it");
            var menu = field.GetType().GetMethod("Menu", Any).Invoke(field, null);
            var dropdown = menu.GetType().GetMethod("ToDropdown").Invoke(menu, null);
            var item = Items(dropdown).FirstOrDefault(each => (string)Get(each, "Name") == ForgeContext.Event);
            Assert.IsNotNull(item, "its menu lists it");
            Assert.IsTrue((bool)Get(item, "Checked"), "checked");
            Assert.IsFalse((bool)Get(item, "Enabled"), "and locked");
            StringAssert.Contains("code", (string)Get(item, "Context"), "its Context saying it's declared in code");

            field.GetType().GetMethod("Toggle", Any).Invoke(field, new object[] { ForgeContext.Event });
            StringAssert.Contains(ForgeContext.Event, field.value, "a toggle doesn't take it away");
            Assert.That((IEnumerable<string>)Get(field, "Contexts"), Has.No.Member(ForgeContext.Event), "nor is it a Tag Library's to declare");
        }

        /// <summary>
        /// A field's New asks for the new asset's name in The Forge's New dialog, starting as one for the asset that holds the
        /// field and the field; nothing's made until it's named, and then the field takes it. It made the asset at once.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_8_A_fields_New_asks_for_the_new_assets_name_first()
        {
            // The dialog is a window, which batch mode has no device to show
            using var windows = new WindowsTolerated();
            const string folder = "Assets/EP8 New";
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "EP8 Fireball";
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("EP-8") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                AssetDatabase.CreateFolder("Assets", "EP8 New");
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(ability);
                var field = new PropertyField(serialized.FindProperty(nameof(Ability.Cost)));
                document.rootVisualElement.Add(field);
                field.Bind(serialized);
                for (int i = 0; i < 6 && field.Q<Button>("ReferenceAction") == null; i++) yield return null;

                var action = field.Q<Button>("ReferenceAction");
                Assert.IsNotNull(action, "the Cost field has its New");
                Assert.AreEqual("New", action.text);
                using (var click = NavigationSubmitEvent.GetPooled())
                {
                    click.target = action;
                    action.SendEvent(click);
                }
                yield return null;

                var window = Resources.FindObjectsOfTypeAll<ForgeNewAssetWindow>().FirstOrDefault();
                Assert.IsNotNull(window, "New opens The Forge's New dialog");
                Assert.AreEqual("EP8 Fireball Cost", window.rootVisualElement.Q<TextField>("AssetName").value,
                    "named for the asset that holds the field and the field");
                Assert.IsNull(ability.Cost, "and nothing's made until it's named");

                window.rootVisualElement.Q<TextField>("Folder").value = folder;
                window.rootVisualElement.Q<TextField>("AssetName").value = "EP8 Mana Cost";
                var made = window.CreateAsset();
                yield return null;
                Assert.IsNotNull(made, "Create makes it");
                Assert.AreEqual($"{folder}/EP8ManaCost.asset", AssetDatabase.GetAssetPath(made), "its file by the name given, without its spaces, in the folder given");
                Assert.AreEqual("EP8 Mana Cost", ((GameplayEffect)made).GetName(), "and the name as given");
                Assert.AreSame(made, ability.Cost, "and the field takes it");
            }
            finally
            {
                foreach (var open in Resources.FindObjectsOfTypeAll<ForgeNewAssetWindow>()) open.Close();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(ability);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// Every asset type The Forge lists shows an icon: the six types their own, and the rest PlayForge's anvil, as
        /// Crowd Control Rules, a Value Pipeline, a Forge Project and a Tag Library; a type that isn't PlayForge's shows none.
        /// </summary>
        [Test]
        public void EP_9_Every_PlayForge_asset_type_shows_an_icon()
        {
            var listed = ((Type[])typeof(TheForge).GetField("Types", Any).GetValue(null)).Concat(new[] { typeof(ForgeProject), typeof(ForgeTagLibrary) });
            var blank = listed.Where(type => Static("ForgeAssetTypes", "GlyphOf", type).ToString() == "None").Select(type => type.Name).ToList();
            Assert.IsEmpty(blank, $"types with no icon: {string.Join(", ", blank)}");
            Assert.AreEqual("Ability", Static("ForgeAssetTypes", "GlyphOf", typeof(Ability)).ToString(), "the six keep their own");
            Assert.AreEqual("PlayForge", Static("ForgeAssetTypes", "GlyphOf", typeof(CrowdControlRules)).ToString(), "the rest show PlayForge's");
            Assert.AreEqual("playforge", Static("ForgeAssetTypes", "ModifierOf", typeof(ValuePipeline)), "in its own color");
            Assert.AreEqual("None", Static("ForgeAssetTypes", "GlyphOf", typeof(PanelSettings)).ToString(), "and a type not PlayForge's shows none");
        }

        /// <summary>
        /// No worker's field carries a [Header]: a worker's fields are drawn in its row of an inspector's Workers, where a
        /// header's bold line stood out of the style (EP-17, the user's note). Every worker kind is checked, its bases' fields
        /// and its helper types' with it.
        /// </summary>
        [Test]
        public void EP_17_No_workers_field_carries_a_Header()
        {
            var bases = new[] { typeof(AbstractAnalysisWorker), typeof(AbstractAttributeWorker), typeof(AbstractEffectWorker), typeof(AbstractImpactWorker), typeof(AbstractTagWorker) };
            var workers = typeof(Tag).Assembly.GetTypes()
                .Where(type => bases.Any(each => each.IsAssignableFrom(type)) || type.Name.Contains("Worker"));
            var headed = workers
                .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(field => field.IsDefined(typeof(HeaderAttribute), false))
                    .Select(field => $"{type.Name}.{field.Name}"))
                .ToList();
            Assert.IsEmpty(headed, $"worker fields with a [Header]: {string.Join(", ", headed)}");
        }
    }
}
