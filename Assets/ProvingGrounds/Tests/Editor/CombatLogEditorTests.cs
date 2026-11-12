using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>TW-22 in the editor: Project Settings → PlayForge shows what the project's combat log writes and keeps.</summary>
    public class CombatLogEditorTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void TW_22_The_project_settings_show_the_combat_log()
        {
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            try
            {
                var settings = project.Settings.CombatLog;
                Assert.AreEqual(100, settings.Keep, "each entity keeps its last 100 by default");
                Assert.AreEqual(ECombatLogRecords.HitsTaken | ECombatLogRecords.HitsDealt, settings.Records, "of the hits it takes and deals");
                Assert.That(settings.Events, Is.Empty, "and no events");

                var pages = typeof(PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.PlayForgeSettingsPages");
                var section = (VisualElement)pages.GetMethod("ForgeProjectSettings", AnyStatic).Invoke(null, new object[] { project });
                var fields = section.Query<PropertyField>().ToList();
                int resolveOrder = fields.FindIndex(field => field.name == "ResolveOrder");
                int combatLog = fields.FindIndex(field => field.name == "CombatLog");
                Assert.That(combatLog, Is.GreaterThan(resolveOrder).And.GreaterThan(-1), "the Combat Log, after the Resolve Order");
            }
            finally
            {
                Object.DestroyImmediate(project);
            }
        }
    }
}
