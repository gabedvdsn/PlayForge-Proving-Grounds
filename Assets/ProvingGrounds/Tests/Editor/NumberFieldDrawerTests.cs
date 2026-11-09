using System.Collections;
using System.IO;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
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
    /// FEAT-29's number field in the inspector, its Problems rule and its migration: the kind menu by category, offering
    /// only the kinds the field's context resolves; the one-line summary; percent fields; a kind found where its field
    /// can't resolve it; and a float saved before its field became a number.
    /// </summary>
    public class NumberFieldDrawerTests
    {
        // A runtime panel to draw fields in, since batch mode has no window to give them one
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("FEAT-29") { hideFlags = HideFlags.HideAndDontSave };

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

        private static IEnumerator Drawn(Host host, NumberFieldFixture fixture, string path, System.Action<PropertyField, SerializedObject> check)
        {
            var serialized = new SerializedObject(fixture);
            var field = host.Draw(serialized, path);
            yield return Frames();
            check(field, serialized);
            field.RemoveFromHierarchy();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-5: the inspector
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The kind menu lists the common kinds at its top, and files the rest under their categories: a scaler of every
        /// kind under Scaler, and a kind of a project's own under the category it names (NUM-5).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_5_The_kind_menu_groups_kinds_by_category()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Share), (field, _) =>
                {
                    var kind = field.Q<PopupField<string>>("Kind");
                    Assert.IsNotNull(kind, "the field has a kind menu");
                    Assert.That(kind.choices, Is.SupersetOf(new[] { "Constant", "By Level", "× Attribute", "Set By Caller", "Clamped", "Reach" }));
                    Assert.AreEqual("Constant", kind.formatListItemCallback("Constant"), "Constant is at the top");
                    Assert.AreEqual("By Level", kind.formatListItemCallback("By Level"), "with By Level");
                    Assert.AreEqual("× Attribute", kind.formatListItemCallback("× Attribute"), "and × Attribute");
                    Assert.AreEqual("Scaler/Clamped", kind.formatListItemCallback("Clamped"), "a scaler of every kind is under Scaler");
                    Assert.AreEqual("Testing/Reach", kind.formatListItemCallback("Reach"), "a kind of the project's own under its category");
                    Assert.AreEqual("Constant", kind.value, "the field shows its kind by its name");
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// A field offers only the kinds its context resolves: a field with no target offers no kind that reads one, and a
        /// field that has everything offers them all (NUM-5).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_5_The_kind_menu_offers_only_kinds_the_field_can_resolve()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Range), (field, _) =>
                {
                    var choices = field.Q<PopupField<string>>("Kind").choices;
                    Assert.That(choices, Has.Member("By Level"), "a field with a level offers By Level");
                    Assert.That(choices, Has.Member("× Attribute"), "and the caster's attribute");
                    Assert.That(choices, Has.No.Member("Reach"), "but no kind that reads a target it hasn't got");
                    Assert.That(choices, Has.No.Member("Distance to Target"));
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Threshold), (field, _) =>
                    Assert.That(field.Q<PopupField<string>>("Kind").choices, Has.Member("Reach"), "a field that declares nothing offers every kind"));
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// A number of a kind shows a one-line summary of what it reads, as "30% × Target's Health (base)", and a
        /// constant shows none, as its number says it (NUM-5).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_5_A_number_of_a_kind_shows_its_summary()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var health = ScriptableObject.CreateInstance<Attribute>();
            health.Name = "Health";
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                fixture.Share = new ScalableFloat(new AttributeBackedScaler
                {
                    CaptureAttribute = health, CaptureFrom = ESourceTarget.Target, CaptureWhat = EAttributeTargetBinary.Base,
                }, 0.3f);
                fixture.Range = new ScalableFloat(new SimpleScaler { LevelValues = new[] { 8f, 9f, 10f, 12f }, StretchOverLevels = false }, 0f, EMagnitudeOperation.UseScaler);

                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Share), (field, _) =>
                    Assert.AreEqual("30% × Target's Health (base)", field.Q<Label>("Summary")?.text, "a percent field's summary"));
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Range), (field, _) =>
                    Assert.AreEqual("8/9/10/12 by level", field.Q<Label>("Summary")?.text, "a By Level's"));
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Threshold), (field, _) =>
                    Assert.IsNull(field.Q<Label>("Summary"), "a constant has none"));
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
                Object.DestroyImmediate(health);
            }
        }

        /// <summary>A field whose unit is "%" stores 0.3, and shows and takes it as 30% (NUM-5).</summary>
        [UnityTest]
        public IEnumerator NUM_5_A_percent_field_shows_and_takes_a_percentage()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Share), (field, serialized) =>
                {
                    var number = field.Q<FloatField>("Number");
                    Assert.IsNotNull(number, "a constant shows its number");
                    Assert.AreEqual(30f, number.value, 0.001f, "0.3 shows as 30");
                    Assert.AreEqual("%", field.Q<Label>("Unit")?.text, "with its unit");
                    number.value = 45f;
                    serialized.Update();
                    Assert.AreEqual(0.45f, fixture.Share.Magnitude, 0.0001f, "and 45 typed in stores 0.45");
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Range), (field, _) =>
                    Assert.AreEqual(5f, field.Q<FloatField>("Number").value, "a field with no unit shows its number as it is"));
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-6: the Problems rule
        // ═══════════════════════════════════════════════════════════════════════════

        private static System.Collections.Generic.List<string> NumberProblems(Object asset) => ForgeValidator.Check(asset)
            .Where(problem => problem.Rule?.GetType().Name == "NumberContextRule").Select(problem => problem.Message).ToList();

        /// <summary>
        /// The Problems window flags a number whose kind its field's context can't resolve, as one copied from another
        /// field: a kind of a project's own that reads a target, an attribute read from the target, and either inside
        /// another scaler. Where the field has what they read, there's nothing to flag (NUM-6).
        /// </summary>
        [Test]
        public void NUM_6_Problems_flags_a_kind_its_field_cannot_resolve()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            try
            {
                Assert.That(NumberProblems(fixture), Is.Empty, "constants resolve anywhere");

                fixture.Range = new ScalableFloat(new ReachKind(), 2f);
                var found = NumberProblems(fixture);
                Assert.AreEqual(1, found.Count, string.Join("\n", found));
                StringAssert.Contains("Range", found[0], "it names the field");
                StringAssert.Contains("target", found[0], "and what it lacks");

                fixture.Range = new ScalableFloat(new AttributeBackedScaler { CaptureFrom = ESourceTarget.Target }, 1f);
                Assert.AreEqual(1, NumberProblems(fixture).Count, "an attribute read from the target is flagged too");
                fixture.Range = new ScalableFloat(new AttributeBackedScaler { CaptureFrom = ESourceTarget.Source }, 1f);
                Assert.That(NumberProblems(fixture), Is.Empty, "but not one read from the caster");

                var group = new ScalerGroup();
                group.AddMember(new ReachKind(), ECalculationOperation.Add);
                fixture.Range = new ScalableFloat(group, 1f);
                Assert.AreEqual(1, NumberProblems(fixture).Count, "a kind inside another scaler is flagged");

                fixture.Range = 5f;
                fixture.Share = new ScalableFloat(new ReachKind(), 1f);
                Assert.That(NumberProblems(fixture), Is.Empty, "a field that has a target takes the kind");
            }
            finally
            {
                Object.DestroyImmediate(fixture);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-7: migration
        // ═══════════════════════════════════════════════════════════════════════════

        private const string Folder = "Assets/NumberFieldTests";

        private static string ScriptGuid()
        {
            var made = ScriptableObject.CreateInstance<NumberFieldFixture>();
            try
            {
                return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(made)));
            }
            finally
            {
                Object.DestroyImmediate(made);
            }
        }

        // An asset as Unity writes one, of the fixture's class, with the fields given
        private static string Yaml(string guid, string name, string fields) =>
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n" +
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
            "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n" +
            $"  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n  m_Name: {name}\n  m_EditorClassIdentifier: \n" + fields;

        /// <summary>
        /// A float saved before its field became a number loads into the number, through the hidden legacy field the
        /// owner moves on load; the upgrade command re-saves the asset in the new form, and it loads the same again (NUM-7).
        /// </summary>
        [Test]
        public void NUM_7_A_float_saved_before_its_field_became_a_number_loads_into_it()
        {
            var path = $"{Folder}/Legacy.asset";
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(path, Yaml(ScriptGuid(), "Legacy", "  Threshold: 0.3\n"));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                var loaded = AssetDatabase.LoadAssetAtPath<NumberFieldFixture>(path);
                Assert.IsNotNull(loaded, "the old asset loads");
                Assert.IsTrue(loaded.Threshold.IsConstant, "its float became a constant");
                Assert.AreEqual(0.3f, loaded.Threshold.Constant, 0.0001f, "of the value it saved");

                Assert.That(ForgeUpgrade.AssetsToUpgrade(Folder), Has.Member(path), "the upgrade command finds it");
                Assert.AreEqual(1, ForgeUpgrade.Resave(new[] { path }), "and re-saves it");
                var text = File.ReadAllText(path);
                StringAssert.DoesNotContain("Threshold: 0.3", text, "the float is gone");
                StringAssert.Contains("Magnitude: 0.3", text, "for the number's");

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var again = AssetDatabase.LoadAssetAtPath<NumberFieldFixture>(path);
                Assert.AreEqual(0.3f, again.Threshold.Constant, 0.0001f, "and it loads the same in the new form");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        /// <summary>
        /// The upgrade command finds the assets that hold PlayForge data, by their class or the classes inside them, and
        /// leaves others alone (NUM-7).
        /// </summary>
        [Test]
        public void NUM_7_The_upgrade_command_finds_the_assets_that_hold_PlayForge_data()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
                var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
                AssetDatabase.CreateAsset(fixture, $"{Folder}/Fixture.asset");
                var effect = ScriptableObject.CreateInstance<GameplayEffect>();
                AssetDatabase.CreateAsset(effect, $"{Folder}/Effect.asset");
                var other = new Material(Shader.Find("Hidden/InternalErrorShader"));
                AssetDatabase.CreateAsset(other, $"{Folder}/Other.mat");
                AssetDatabase.SaveAssets();

                var found = ForgeUpgrade.AssetsToUpgrade(Folder);
                Assert.That(found, Has.Member($"{Folder}/Fixture.asset"), "an asset whose class holds PlayForge's numbers");
                Assert.That(found, Has.Member($"{Folder}/Effect.asset"), "a PlayForge asset");
                Assert.That(found, Has.No.Member($"{Folder}/Other.mat"), "and nothing else");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }
    }
}
