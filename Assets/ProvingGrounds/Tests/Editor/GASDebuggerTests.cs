using System.Collections;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>The GAS Debugger window, driven as its Update drives it in play mode.</summary>
    public class GASDebuggerTests
    {
        /// <summary>
        /// The debugger hooked three callbacks on each entity and never removed them, so a window closed in play mode
        /// kept sampling every entity, and reopening it added another set.
        /// </summary>
        [Test]
        public void ED_26_Closing_the_debugger_removes_its_hooks()
        {
            var body = new GameObject("ED-26") { hideFlags = HideFlags.HideAndDontSave };
            var gas = body.AddComponent<GameplayAbilitySystem>();
            var window = ScriptableObject.CreateInstance<GASDebugger>();
            try
            {
                Assert.IsFalse(gas.Callbacks.HasFrameCompleteListeners, "nothing listens yet");

                // What the window's Update does in play mode for each entity it finds
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                ((IList)typeof(GASDebugger).GetField("_gasObjects", flags).GetValue(window)).Add(gas);
                typeof(GASDebugger).GetMethod("EnsureFrameSummaryHooks", flags).Invoke(window, null);
                Assert.IsTrue(gas.Callbacks.HasFrameCompleteListeners, "the open window listens");

                Object.DestroyImmediate(window);
                Assert.IsFalse(gas.Callbacks.HasFrameCompleteListeners, "the closed window doesn't");
            }
            finally
            {
                if (window != null) Object.DestroyImmediate(window);
                Object.DestroyImmediate(body);
            }
        }
    }
}
