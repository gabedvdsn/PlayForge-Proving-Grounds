using System.Linq;
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
