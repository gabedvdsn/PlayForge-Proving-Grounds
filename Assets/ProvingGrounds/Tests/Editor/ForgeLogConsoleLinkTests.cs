using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// A console message ForgeLog wrote opens at the code that logged it (EP-65): a double-click opened ForgeLog's own call
    /// to Unity's log. ForgeLog's methods are marked to hide in the Console's stack, and the Console's double-click opens
    /// the first frame of the stack that isn't ForgeLog's, nor a method marked to hide.
    /// </summary>
    public class ForgeLogConsoleLinkTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private const string Logged =
            "[PlayForge] the world stepped\n" +
            "UnityEngine.Debug:Log (object,UnityEngine.Object)\n" +
            "FarEmerald.PlayForge.ForgeLog:Write (FarEmerald.PlayForge.ForgeLog/ESystem,FarEmerald.PlayForge.ForgeLog/ELevel,string,UnityEngine.Object,object) (at Assets/PlayForge/Runtime/PlayForge/Core/ForgeLog.cs:128)\n" +
            "FarEmerald.PlayForge.ForgeLog:Info (FarEmerald.PlayForge.ForgeLog/ESystem,string,UnityEngine.Object,object) (at Assets/PlayForge/Runtime/PlayForge/Core/ForgeLog.cs:94)\n" +
            "FarEmerald.PlayForge.NumberContext:LogUnresolved (FarEmerald.PlayForge.NumberKind,FarEmerald.PlayForge.NumberContext&) (at Assets/PlayForge/Runtime/PlayForge/Numbers/NumberContext.cs:493)\n" +
            "FarEmerald.PlayForge.ProcessControl:Step () (at Assets/PlayForge/Runtime/PlayForge/Management/GameplayProcess/Core/ProcessControl.cs:242)\n";

        private static bool TryCallerOf(string stack, out string path, out int line)
        {
            var link = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeLogConsoleLink");
            Assert.IsNotNull(link, "the Console's link");
            var method = link.GetMethod("TryCallerOf", Any);
            Assert.IsNotNull(method, "its frame finder");
            var args = new object[] { stack, null, 0 };
            bool found = (bool)method.Invoke(null, args);
            path = (string)args[1];
            line = (int)args[2];
            return found;
        }

        /// <summary>ForgeLog's log methods, and the number helper that logs, are marked to hide in the Console's stack (EP-65).</summary>
        [Test]
        public void EP_65_ForgeLogs_methods_hide_in_the_consoles_stack()
        {
            var logging = typeof(ForgeLog).GetMethods(Any)
                .Where(method => method.Name is "Verbose" or "Info" or "Warning" or "Error" or "Exception" or "Write")
                .ToList();
            Assert.That(logging.Count, Is.GreaterThanOrEqualTo(10), "its log methods");
            Assert.That(logging.Where(method => !method.IsDefined(typeof(HideInCallstackAttribute), false)).Select(method => method.Name),
                Is.Empty, "each marked to hide");
            var helper = typeof(NumberContext).GetMethod("LogUnresolved", Any);
            Assert.IsNotNull(helper, "the number helper that logs");
            Assert.IsTrue(helper.IsDefined(typeof(HideInCallstackAttribute), false), "is marked to hide too");
        }

        /// <summary>
        /// A message ForgeLog wrote opens at the first frame that isn't ForgeLog's, nor marked to hide: the code that logged
        /// it (EP-65).
        /// </summary>
        [Test]
        public void EP_65_A_forgelog_message_opens_at_the_code_that_logged_it()
        {
            Assert.IsTrue(TryCallerOf(Logged, out var path, out int line), "a message ForgeLog wrote has a caller");
            Assert.AreEqual("Assets/PlayForge/Runtime/PlayForge/Management/GameplayProcess/Core/ProcessControl.cs", path,
                "past ForgeLog's frames and the hidden helper's");
            Assert.AreEqual(242, line, "at the line that logged");
        }

        /// <summary>A message that didn't pass through ForgeLog opens as Unity opens it (EP-65).</summary>
        [Test]
        public void EP_65_Another_message_opens_as_unity_opens_it()
        {
            const string other = "boom\nUnityEngine.Debug:Log (object)\nGame.Player:Update () (at Assets/Game/Player.cs:12)\n";
            Assert.IsFalse(TryCallerOf(other, out _, out _), "not ForgeLog's, so not moved");
            Assert.IsFalse(TryCallerOf("", out _, out _), "and nothing for an empty stack");
        }
    }
}
