using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
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
    }
}
