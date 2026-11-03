using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>The Forge's View tab, its state set as clicking its filters and column headers sets it.</summary>
    public class TheForgeViewTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>
        /// Every column's values are strings, and the tab sorted them as strings, so a numeric column put 10 before 9.
        /// </summary>
        [Test]
        public void ED_7_A_numeric_column_sorts_by_number()
        {
            var forge = ScriptableObject.CreateInstance<TheForge>();
            var nine = ScriptableObject.CreateInstance<Ability>();
            var ten = ScriptableObject.CreateInstance<Ability>();
            try
            {
                nine.name = "Nine";
                nine.MaxLevel = 9;
                ten.name = "Ten";
                ten.MaxLevel = 10;

                // Ability's table, sorted ascending by its "Max Lvl" column
                typeof(TheForge).GetField("selectedTypeFilter", Private).SetValue(forge, typeof(Ability));
                typeof(TheForge).GetField("currentSortColumn", Private).SetValue(forge, "Max Lvl");
                typeof(TheForge).GetField("sortAscending", Private).SetValue(forge, true);

                var sorted = (List<ScriptableObject>)typeof(TheForge).GetMethod("SortAssets", Private)
                    .Invoke(forge, new object[] { new List<ScriptableObject> { ten, nine } });

                Assert.That(sorted.Select(asset => asset.name), Is.EqualTo(new[] { "Nine", "Ten" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(nine);
                UnityEngine.Object.DestroyImmediate(ten);
            }
        }
    }
}
