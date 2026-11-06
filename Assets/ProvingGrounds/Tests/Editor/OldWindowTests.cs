using System;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The windows older than Phase 4 (ED-61): the Asset Browser, which picked an asset's template, and the Create Linked
    /// Effect dialog. Each set its look in C#, and each does what Unity's own controls, or a window of The Forge's, do.
    /// </summary>
    public class OldWindowTests
    {
        private const string Folder = "Assets/ED-61 Templates";

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens
        private sealed class WindowLog : IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public List<string> Errors { get; } = new();

            public WindowLog()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(NoDevice))
                    Errors.Add($"{type}: {message}");
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static GameplayEffect Effect(string name, string file)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition.Name = name;
            AssetDatabase.CreateAsset(effect, $"{Folder}/{file}.asset");
            return effect;
        }

        /// <summary>
        /// An asset's template is picked from the other assets of its type, by name, but those that take it as theirs,
        /// which would make a loop, and two of one name add their file's (ED-61). The Asset Browser listed every asset of
        /// the type but the one itself, so a child could be made its own template's template.
        /// </summary>
        [Test]
        public void ED_61_A_template_is_picked_from_the_other_assets_of_its_type()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "ED-61 Templates");
            try
            {
                var basis = Effect("Base", "Base");
                var child = Effect("Child", "Child");
                var grandchild = Effect("Grandchild", "Grandchild");
                Effect("Other", "Other");
                Effect("Twin", "Twin A");
                Effect("Twin", "Twin B");
                Assume.That(child.SetTemplate(basis) && grandchild.SetTemplate(child), "the family is set");

                List<string> Names(BaseForgeAsset asset) => BasePlayForgeEditor.TemplatesFor(asset)
                    .Select(each => each.Name).Where(name => name is "Base" or "Child" or "Grandchild" or "Other" || name.StartsWith("Twin")).ToList();

                Assert.That(Names(basis), Is.EqualTo(new[] { "Other", "Twin (Twin A)", "Twin (Twin B)" }),
                    "a template is offered none that take it as theirs, through any templates between");
                Assert.That(Names(child), Is.EqualTo(new[] { "Base", "Other", "Twin (Twin A)", "Twin (Twin B)" }),
                    "a child is offered its own template and the rest, but its child");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        /// <summary>
        /// New Linked Effect... names the effect in The Forge's New Asset window, which makes it where The Forge makes
        /// effects, and the effect takes its levels from the asset that holds the field (ED-61). A dialog of its own asked
        /// for the name, with a tinted link box, colored badges and a green Create.
        /// </summary>
        [Test]
        public void ED_61_New_linked_effect_is_made_in_the_New_Asset_window()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = "Fireball";
            var field = new ObjectField { objectType = typeof(GameplayEffect) };
            var log = new WindowLog();
            ForgeNewAssetWindow window = null;
            string made = null;
            try
            {
                window = GameplayEffectDrawer.NewLinked(field, ability);
                Assert.IsNotNull(window, "the New Asset window opens");
                var effect = window.CreateAsset() as GameplayEffect;
                window = null;
                Assert.IsNotNull(effect, "and makes an effect");
                made = AssetDatabase.GetAssetPath(effect);
                Assert.AreEqual(effect, field.value, "which the field takes");
                Assert.IsTrue(effect.IsLinkedTo(ability), "and which takes its levels from the field's asset");
            }
            finally
            {
                if (window != null) window.Close();
                if (!string.IsNullOrEmpty(made)) AssetDatabase.DeleteAsset(made);
                Object.DestroyImmediate(ability);
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the window was open");
        }
    }
}
