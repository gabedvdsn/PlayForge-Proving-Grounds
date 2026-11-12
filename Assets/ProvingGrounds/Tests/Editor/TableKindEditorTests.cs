using System.Collections;
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
    /// TW-27, Decision 67: Table, a kind of its own beside By Level in the kind menu: one value per level, read exactly, the
    /// last for any level past it, with no curve shown; By Level stays for curves. A number by level saved reading exactly
    /// shows as a Table, and nothing saved changes.
    /// </summary>
    public class TableKindEditorTests
    {
        // A runtime panel to draw fields in, since batch mode has no window to give them one
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("TW-27") { hideFlags = HideFlags.HideAndDontSave };

            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public PropertyField Draw(SerializedObject serialized, string path)
            {
                var field = new PropertyField(serialized.FindProperty(path));
                Document.rootVisualElement.Add(field);
                field.Bind(serialized);
                return field;
            }

            public void Destroy()
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        /// <summary>
        /// The kind menu offers Table before By Level, at its top. Table makes a number by level that reads exactly, its
        /// values alone, with no Per Level or Spread choice and no curve; By Level spreads the same values over the levels,
        /// with its curve.
        /// </summary>
        [UnityTest]
        public IEnumerator TW_27_Table_is_a_kind_of_its_own_beside_By_Level()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(fixture), nameof(NumberFieldFixture.Range));
                yield return Frames();

                var kind = field.Q<PopupField<string>>("Kind");
                int table = kind.choices.IndexOf("Table");
                Assert.That(table, Is.GreaterThan(0), "the kind menu offers Table");
                Assert.That(table, Is.LessThan(kind.choices.IndexOf("By Level")), "before By Level");
                Assert.AreEqual("Table", kind.formatListItemCallback("Table"), "at the menu's top");

                kind.value = "Table";
                yield return Frames();
                var scaler = fixture.Range.Scaler as SimpleScaler;
                Assert.IsNotNull(scaler, "a Table is a number by level");
                Assert.IsFalse(scaler.StretchOverLevels, "read exactly, level by level");
                Assert.AreEqual(EMagnitudeOperation.UseScaler, fixture.Range.RealMagnitude, "its values alone");
                Assert.AreEqual("Table", field.Q<PopupField<string>>("Kind").value, "it shows as a Table");
                Assert.IsNull(field.Q("ValuesMode"), "its kind says how it reads, with no Per Level or Spread choice");
                Assert.IsNull(field.Q("Curve"), "and it shows no curve");

                field.Q<PopupField<string>>("Kind").value = "By Level";
                yield return Frames();
                Assert.IsTrue(((SimpleScaler)fixture.Range.Scaler).StretchOverLevels, "By Level spreads its values over the levels");
                Assert.AreEqual("By Level", field.Q<PopupField<string>>("Kind").value, "and shows as By Level");
                Assert.IsNotNull(field.Q("Curve"), "with its curve");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>A number by level saved reading exactly, as the inspector made them, shows as a Table, and stays as it was saved.</summary>
        [UnityTest]
        public IEnumerator TW_27_A_number_by_level_saved_reading_exactly_shows_as_a_Table()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            fixture.Range = new ScalableFloat(new SimpleScaler { StretchOverLevels = false, LevelValues = new[] { 450f, 600f, 750f } }, 1f,
                EMagnitudeOperation.UseScaler);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(fixture), nameof(NumberFieldFixture.Range));
                yield return Frames();

                Assert.AreEqual("Table", field.Q<PopupField<string>>("Kind").value, "it shows as a Table");
                var scaler = fixture.Range.Scaler as SimpleScaler;
                Assert.IsNotNull(scaler, "still the By Level scaler it was saved as");
                Assert.IsFalse(scaler.StretchOverLevels, "reading exactly");
                Assert.That(scaler.LevelValues, Is.EqualTo(new[] { 450f, 600f, 750f }), "its values as they were");
                Assert.AreEqual("Table", NumberText.KindName(scaler), "and the text that names its kind says Table");
                Assert.AreEqual("By Level", NumberText.KindName(new SimpleScaler { StretchOverLevels = true }), "where a curve's says By Level");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }
    }
}
