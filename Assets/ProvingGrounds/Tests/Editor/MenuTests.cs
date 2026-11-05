using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>PlayForge's menu, its windows' titles, and the names the editor uses for them.</summary>
    public class MenuTests
    {
        private static string PackageFolder
        {
            get
            {
                var package = PackageInfo.FindForAssetPath("Packages/com.faremerald.playforge/package.json");
                Assert.IsNotNull(package, "PlayForge is installed as a package");
                return package.resolvedPath;
            }
        }

        /// <summary>
        /// Every item sat in a top-level PlayForge menu. The Asset Store's rule 2.5.1.a expects an existing menu such as
        /// Window, and Decision 18 puts them under Window/PlayForge.
        /// </summary>
        [Test]
        public void ED_19_Every_menu_item_sits_under_Window_PlayForge()
        {
            var paths = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .Where(method => method.DeclaringType.Assembly.GetName().Name.StartsWith("FarEmerald.PlayForge"))
                .SelectMany(method => method.GetCustomAttributes<MenuItem>())
                .Select(item => item.menuItem)
                .Where(path => !path.StartsWith("CONTEXT/") && !path.StartsWith("Assets/") && !path.StartsWith("GameObject/"))
                .ToList();
            Assert.That(paths, Is.Not.Empty, "PlayForge has menu items");
            Assert.That(paths, Has.All.StartsWith("Window/PlayForge/"));
        }

        /// <summary>
        /// No window's title matched its menu item: The Forge opened "PlayForge Manager", the GAS Debugger "GAS Debug",
        /// Process Control "Process Debugger", and the Tag Debugger "Runtime Tags". The three debuggers are one now (Q4),
        /// the Debugger, as the design canvas's boards title it.
        /// </summary>
        [TestCase("Window/PlayForge/The Forge", nameof(TheForge))]
        [TestCase("Window/PlayForge/Debugger", "RuntimeDebugger")]
        public void ED_19_Each_window_is_titled_as_its_menu_item(string path, string window)
        {
            var type = typeof(TheForge).Assembly.GetTypes().Single(t => t.Name == window);
            // Batch mode has no graphics device to draw a window, and logs an error saying so
            LogAssert.ignoreFailingMessages = true;
            try
            {
                CloseAll(type);
                Assert.IsTrue(EditorApplication.ExecuteMenuItem(path), "the menu item exists");
                var opened = (EditorWindow)Resources.FindObjectsOfTypeAll(type).Single();
                Assert.AreEqual(path.Substring(path.LastIndexOf('/') + 1), opened.titleContent.text);
            }
            finally
            {
                CloseAll(type);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static void CloseAll(System.Type window)
        {
            foreach (var open in Resources.FindObjectsOfTypeAll(window)) ((EditorWindow)open).Close();
        }

        /// <summary>
        /// Run Golden Tests sat on a buyer's menu. The Test Runner runs the golden scenarios now (SP-18), and batch mode
        /// still calls RunAll, so Decision 18 takes it off the menu.
        /// </summary>
        [Test]
        public void ED_19_Run_Golden_Tests_is_off_the_menu()
        {
            var runner = File.ReadAllText(Path.Combine(PackageFolder, "Tests", "Editor", "PlayForgeGoldenTests.cs"));
            Assert.That(runner, Does.Not.Contain("[MenuItem("));
        }

        /// <summary>The editor called the main window "PlayForge Manager", and its inspectors offered "Open in Manager".</summary>
        [Test]
        public void ED_19_The_editor_names_The_Forge()
        {
            var literal = new Regex(@"""[^""]*\b(PlayForge Manager|in Manager)\b[^""]*""");
            var offenders = Directory.GetFiles(Path.Combine(PackageFolder, "Editor"), "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, number: i + 1)))
                .Where(x => literal.IsMatch(x.line) && !x.line.TrimStart().StartsWith("//"))
                .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
                .ToList();
            Assert.That(offenders, Is.Empty);
        }
    }
}
