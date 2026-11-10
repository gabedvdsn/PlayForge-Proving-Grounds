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

        /// <summary>
        /// Phase 2's kinds sit in the kind menu: Local Data, Group Impact and Relative to a Field at its top, Tracked
        /// Impact under Effect. A field whose code gives it no owner doesn't offer Relative to a Field (NUM-8 to NUM-11).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_8_The_new_kinds_sit_in_the_menu()
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
                    Assert.That(kind.choices, Is.SupersetOf(new[] { "Local Data", "Relative to a Field", "Tracked Impact", "Group Impact" }));
                    Assert.AreEqual("Local Data", kind.formatListItemCallback("Local Data"), "Local Data is at the top");
                    Assert.AreEqual("Group Impact", kind.formatListItemCallback("Group Impact"), "with Group Impact");
                    Assert.AreEqual("Relative to a Field", kind.formatListItemCallback("Relative to a Field"), "and Relative to a Field");
                    Assert.AreEqual("Effect/Tracked Impact", kind.formatListItemCallback("Tracked Impact"), "Tracked Impact is under Effect");
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Threshold), (field, _) =>
                {
                    var choices = field.Q<PopupField<string>>("Kind").choices;
                    Assert.That(choices, Has.Member("Local Data"));
                    Assert.That(choices, Has.No.Member("Relative to a Field"), "a field whose code gives no owner can't read another field");
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// A relative number's field menu offers its object's other number fields, plain or a number, by name, and leaves
        /// out the field itself and any field that reads it back, so a loop can't be made (NUM-9).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_9_The_relative_picker_offers_the_objects_other_number_fields()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            fixture.Low = new ScalableFloat(new RelativeToFieldKind { Field = nameof(NumberFieldFixture.High) }, 1f);
            fixture.High = new ScalableFloat(new RelativeToFieldKind(), 1f);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.High), (field, serialized) =>
                {
                    var picker = field.Q<PopupField<string>>("Field");
                    Assert.IsNotNull(picker, "the kind picks its field from a menu");
                    Assert.That(picker.choices, Is.SupersetOf(new[] { "Range", "Share", "Threshold", "PlainCap" }), "the object's other number fields");
                    Assert.That(picker.choices, Has.No.Member("High"), "not itself");
                    Assert.That(picker.choices, Has.No.Member("Low"), "nor a field that reads it back");
                    Assert.That(picker.choices, Has.No.Member("legacyThreshold"), "nor a hidden one");
                    Assert.AreEqual("Plain Cap", picker.formatListItemCallback("PlainCap"), "each by its name as the inspector writes it");

                    picker.value = "PlainCap";
                    serialized.Update();
                    Assert.AreEqual("PlainCap", ((RelativeToFieldKind)fixture.High.Scaler).Field, "picking one sets it");
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        private const string HiddenClass = "pf-scaler__hidden";

        private static bool Shown(VisualElement element) => element != null && !element.ClassListContains(HiddenClass);

        /// <summary>
        /// Worker, Asset Part and Group sit at the kind menu's top. What a Worker or an Asset Part reads follows how
        /// it's set, so every field's menu offers them, a field with no target among them (NUM-12 to NUM-14).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_12_Worker_Asset_Part_and_Group_sit_at_the_menus_top()
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
                    Assert.That(kind.choices, Is.SupersetOf(new[] { "Worker", "Asset Part", "Group" }));
                    Assert.AreEqual("Worker", kind.formatListItemCallback("Worker"), "Worker is at the top");
                    Assert.AreEqual("Asset Part", kind.formatListItemCallback("Asset Part"), "with Asset Part");
                    Assert.AreEqual("Group", kind.formatListItemCallback("Group"), "and Group");
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Range), (field, _) =>
                    Assert.That(field.Q<PopupField<string>>("Kind").choices, Is.SupersetOf(new[] { "Worker", "Asset Part", "Group" }),
                        "a field with no target offers them too, as either can read the caster"));
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// An Asset Part shows the part for what it looks for, and its worker key only for an effect's Worker Value: a
        /// field with two conditions shows while both hold (NUM-13).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_13_An_asset_part_shows_the_fields_its_settings_use()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var part = new AssetPartKind { What = EAssetPartOf.Effect, EffectPart = EEffectPart.Stacks };
            fixture.Share = new ScalableFloat(part, 1f);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Share), (field, _) =>
                {
                    Assert.IsTrue(Shown(field.Q<PropertyField>("EffectPart")), "an effect's part shows for an effect");
                    Assert.IsFalse(Shown(field.Q<PropertyField>("AbilityPart")), "and not an ability's");
                    Assert.IsFalse(Shown(field.Q<PropertyField>("ItemPart")), "nor an item's");
                    Assert.IsNotNull(field.Q("WorkerKeyHolder"), "the worker key has its place");
                    Assert.IsFalse(Shown(field.Q<PropertyField>("WorkerKey")), "and stays out for an effect's stacks");
                });

                part.EffectPart = EEffectPart.WorkerValue;
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Share), (field, _) =>
                    Assert.IsTrue(Shown(field.Q<PropertyField>("WorkerKey")), "an effect's Worker Value shows the key it asks for"));

                part.What = EAssetPartOf.Ability;
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Share), (field, _) =>
                {
                    Assert.IsFalse(Shown(field.Q<PropertyField>("WorkerKey")), "an ability has no workers to ask, whatever the effect part was");
                    Assert.IsFalse(Shown(field.Q<PropertyField>("EffectPart")));
                    Assert.IsTrue(Shown(field.Q<PropertyField>("AbilityPart")), "and shows an ability's part");
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// The Problems window reads a Worker and an Asset Part as they're set: one that asks the target is flagged in
        /// a field with no target, and one that asks the caster isn't (NUM-12, NUM-13).
        /// </summary>
        [Test]
        public void NUM_12_Problems_reads_a_worker_or_an_asset_part_as_its_set()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            try
            {
                fixture.Range = new ScalableFloat(new WorkerNumberKind { From = EWorkerNumberFrom.Target }, 1f);
                var asked = NumberProblems(fixture);
                Assert.AreEqual(1, asked.Count, string.Join("\n", asked));
                StringAssert.Contains("target", asked[0], "a worker asked of the target needs one");
                fixture.Range = new ScalableFloat(new WorkerNumberKind { From = EWorkerNumberFrom.Caster }, 1f);
                Assert.That(NumberProblems(fixture), Is.Empty, "one asked of the caster doesn't");
                fixture.Range = new ScalableFloat(new WorkerNumberKind { From = EWorkerNumberFrom.ThisEffect }, 1f);
                Assert.AreEqual(1, NumberProblems(fixture).Count, "and one asked of its effect needs an effect");

                fixture.Range = new ScalableFloat(new AssetPartKind { On = NumberSide.Target }, 1f);
                Assert.AreEqual(1, NumberProblems(fixture).Count, "an asset part read on the target needs one");
                fixture.Range = new ScalableFloat(new AssetPartKind { On = NumberSide.Caster }, 1f);
                Assert.That(NumberProblems(fixture), Is.Empty, "one read on the caster doesn't");
            }
            finally
            {
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// The Problems window flags a relative number that can't read its field: one whose field is gone, as after a
        /// rename, and one in a field whose code gives it no owner (NUM-9).
        /// </summary>
        [Test]
        public void NUM_9_Problems_flags_a_relative_number_that_cant_read_its_field()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            try
            {
                fixture.High = new ScalableFloat(new RelativeToFieldKind { Field = nameof(NumberFieldFixture.Low) }, 2f);
                Assert.That(NumberProblems(fixture), Is.Empty, "a field that reads a sibling it has is fine");

                fixture.High = new ScalableFloat(new RelativeToFieldKind { Field = "Gone" }, 2f);
                var gone = NumberProblems(fixture);
                Assert.AreEqual(1, gone.Count, string.Join("\n", gone));
                StringAssert.Contains("High", gone[0], "it names the field");
                StringAssert.Contains("Gone", gone[0], "and the one it can't find");

                fixture.High = 2f;
                fixture.Threshold = new ScalableFloat(new RelativeToFieldKind { Field = nameof(NumberFieldFixture.Low) }, 2f);
                var ownerless = NumberProblems(fixture);
                Assert.AreEqual(1, ownerless.Count, string.Join("\n", ownerless));
                StringAssert.Contains("Threshold", ownerless[0], "a field whose code gives no owner");
            }
            finally
            {
                Object.DestroyImmediate(fixture);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-15: limits and tooltips on a number field
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A number field with [NumberMin] or [NumberMax] keeps its constant inside them: a number typed past one is
        /// kept at it, and the field shows what was kept. A whole-number field keeps them too (NUM-15).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_15_A_constant_keeps_inside_its_limits()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Floor), (field, serialized) =>
                {
                    var number = field.Q<FloatField>("Number");
                    Assert.IsNotNull(number, "a least alone keeps the number's field");
                    Assert.AreEqual(1f, number.value, "showing its number");
                    number.value = -3f;
                    serialized.Update();
                    Assert.AreEqual(0f, fixture.Floor.Magnitude, "a number under its least is kept at it");
                    Assert.AreEqual(0f, number.value, "and the field shows what was kept");
                    number.value = 250f;
                    serialized.Update();
                    Assert.AreEqual(250f, fixture.Floor.Magnitude, "with no most, a number over it is taken as it is");
                    Assert.AreEqual("At least 0", number.tooltip, "the field says its limit");
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Ceiling), (field, serialized) =>
                {
                    var number = field.Q<FloatField>("Number");
                    number.value = 12f;
                    serialized.Update();
                    Assert.AreEqual(10f, fixture.Ceiling.Magnitude, "a number over its most is kept at it");
                    Assert.AreEqual(10f, number.value);
                    number.value = -4f;
                    serialized.Update();
                    Assert.AreEqual(-4f, fixture.Ceiling.Magnitude, "with no least, a number under it is taken as it is");
                    Assert.AreEqual("At most 10", number.tooltip);
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Count), (field, serialized) =>
                {
                    var number = field.Q<IntegerField>("Number");
                    Assert.IsNotNull(number, "a whole-number field keeps its field");
                    number.value = 0;
                    serialized.Update();
                    Assert.AreEqual(1, fixture.Count.Magnitude, "and its least");
                    Assert.AreEqual(1, number.value);
                    number.value = 7;
                    serialized.Update();
                    Assert.AreEqual(7, fixture.Count.Magnitude);
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// A number field with [NumberRange] sets its constant on a slider with a field to type in, as Unity's [Range]
        /// does for a float. A percent field's slider runs in percent, and a whole-number field's takes whole numbers
        /// (NUM-15).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_15_A_range_sets_its_constant_on_a_slider()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Chance), (field, serialized) =>
                {
                    var slider = field.Q<Slider>("Number");
                    Assert.IsNotNull(slider, "a range draws a slider");
                    Assert.IsTrue(slider.showInputField, "with a field to type in");
                    Assert.AreEqual(0f, slider.lowValue, "a percent field's range is in percent");
                    Assert.AreEqual(100f, slider.highValue);
                    Assert.AreEqual(50f, slider.value, 0.001f, "0.5 shows as 50");
                    Assert.AreEqual("%", field.Q<Label>("Unit")?.text, "with its unit");
                    slider.value = 150f;
                    serialized.Update();
                    Assert.AreEqual(1f, fixture.Chance.Magnitude, 0.0001f, "a number past the range is kept at its end");
                    slider.value = 25f;
                    serialized.Update();
                    Assert.AreEqual(0.25f, fixture.Chance.Magnitude, 0.0001f, "and 25 stores 0.25");
                    Assert.IsNotNull(field.Q<PopupField<string>>("Kind"), "the kind menu is still beside it");
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Charges), (field, serialized) =>
                {
                    var slider = field.Q<SliderInt>("Number");
                    Assert.IsNotNull(slider, "a whole-number field's range draws a whole-number slider");
                    Assert.AreEqual(1, slider.lowValue);
                    Assert.AreEqual(5, slider.highValue);
                    Assert.AreEqual(2, slider.value);
                    slider.value = 9;
                    serialized.Update();
                    Assert.AreEqual(5, fixture.Charges.Magnitude, "kept at the range's end");
                    slider.value = 3;
                    serialized.Update();
                    Assert.AreEqual(3, fixture.Charges.Magnitude);
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// The limits are the constant's. A number of a kind is worked out as the game runs, so the number it combines
        /// with its kind takes any value, and it draws no slider (NUM-15).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_15_A_kinds_number_takes_any_number()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var byLevel = new SimpleScaler { LevelValues = new[] { 1f, 2f }, StretchOverLevels = false };
            fixture.Floor = new ScalableFloat(byLevel, 1f, EMagnitudeOperation.MultiplyWithScaler);
            fixture.Chance = new ScalableFloat(new SimpleScaler { LevelValues = new[] { 1f, 2f }, StretchOverLevels = false }, 0.5f,
                EMagnitudeOperation.MultiplyWithScaler);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Floor), (field, serialized) =>
                {
                    var number = field.Q<FloatField>("CombineNumber");
                    Assert.IsNotNull(number, "a kind's number sits in its Combine row");
                    number.value = -2f;
                    serialized.Update();
                    Assert.AreEqual(-2f, fixture.Floor.Magnitude, "and takes a number its constant wouldn't");
                });
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Chance), (field, serialized) =>
                {
                    Assert.IsNull(field.Q<Slider>("Number"), "a kind's number has no slider");
                    Assert.IsNull(field.Q<Slider>("CombineNumber"));
                    var number = field.Q<FloatField>("CombineNumber");
                    Assert.IsNotNull(number, "it's a field to type in");
                    number.value = 300f;
                    serialized.Update();
                    Assert.AreEqual(3f, fixture.Chance.Magnitude, 0.0001f, "and takes a number past the range, in percent as before");
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>
        /// A number field's [Tooltip] shows on its label, as any field's does: on its number while it's a constant, and
        /// on its kind while it's another. A slider says its range (NUM-15).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_15_A_number_field_shows_its_tooltip()
        {
            var fixture = ScriptableObject.CreateInstance<NumberFieldFixture>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Told), (field, _) =>
                    Assert.AreEqual("How far it reaches", field.Q<FloatField>("Number").labelElement.tooltip, "a constant's label"));

                // Unity gives a label too narrow for its text that text as its tooltip, which took the field's place
                var clipped = new SerializedObject(fixture);
                var narrow = host.Draw(clipped, nameof(NumberFieldFixture.Told));
                yield return Frames();
                var name = narrow.Q<FloatField>("Number").labelElement;
                name.style.minWidth = 0;
                name.style.width = 12;
                name.style.overflow = Overflow.Hidden;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.textOverflow = TextOverflow.Ellipsis;
                yield return Frames();
                Assert.AreEqual("How far it reaches", name.tooltip, "a clipped label's");
                narrow.RemoveFromHierarchy();

                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Chance), (field, _) =>
                    Assert.AreEqual("As a percentage: 30 is 0.3. From 0 to 100", field.Q<Slider>("Number")?.tooltip, "a percent slider's range"));
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Charges), (field, _) =>
                    Assert.AreEqual("From 1 to 5", field.Q<SliderInt>("Number")?.tooltip, "a whole-number slider's"));

                fixture.Told = new ScalableFloat(new SimpleScaler { LevelValues = new[] { 1f, 2f }, StretchOverLevels = false }, 0f,
                    EMagnitudeOperation.UseScaler);
                yield return Drawn(host, fixture, nameof(NumberFieldFixture.Told), (field, _) =>
                    Assert.AreEqual("How far it reaches", field.Q<PopupField<string>>("Kind").labelElement.tooltip, "a kind's label"));
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(fixture);
            }
        }

        // Number fields with Unity's own limits. The class isn't saved, so the check as scripts load passes over it
        private class UnityLimits
        {
            [Min(0)] public ScalableFloat Least = 1f;
            [UnityEngine.Range(0, 1)] public ScalableInt Between = 1;
            [Min(0)] public System.Collections.Generic.List<ScalableFloat> Each = new();
            [Min(0)] public float Plain = 1f;
            [NumberMin(0)] public ScalableFloat Ours = 1f;
        }

        /// <summary>
        /// Unity gives a field with its [Min] or [Range] to that attribute's drawer, which can't draw a number, so a
        /// number field that has one is named with what to use in its place. A plain float's is Unity's to draw, and a
        /// number field with PlayForge's own has nothing to name (NUM-15).
        /// </summary>
        [Test]
        public void NUM_15_Unitys_Min_or_Range_on_a_number_field_is_named()
        {
            var least = NumberFields.UnityLimitOn(typeof(UnityLimits).GetField(nameof(UnityLimits.Least)));
            Assert.IsNotNull(least, "a number field with Unity's [Min]");
            StringAssert.Contains("UnityLimits.Least", least, "is named");
            StringAssert.Contains("[NumberMin]", least, "with what to use in its place");

            var between = NumberFields.UnityLimitOn(typeof(UnityLimits).GetField(nameof(UnityLimits.Between)));
            Assert.IsNotNull(between, "one with Unity's [Range]");
            StringAssert.Contains("[NumberRange]", between);

            Assert.IsNotNull(NumberFields.UnityLimitOn(typeof(UnityLimits).GetField(nameof(UnityLimits.Each))), "a list of numbers too");
            Assert.IsNull(NumberFields.UnityLimitOn(typeof(UnityLimits).GetField(nameof(UnityLimits.Plain))), "a float's is Unity's to draw");
            Assert.IsNull(NumberFields.UnityLimitOn(typeof(UnityLimits).GetField(nameof(UnityLimits.Ours))), "PlayForge's own is fine");
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
