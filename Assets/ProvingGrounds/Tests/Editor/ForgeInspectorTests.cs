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
