using System.Collections;
using System.Collections.Generic;
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
    /// An effect level by level in its inspector, which The Forge shows (FEAT-32, LVL-4); and a Scaler asset tried there,
    /// against stand-ins for its caster and its target, with values typed or filled from an entity (LVL-5, Decision 82).
    /// </summary>
    public class LevelByLevelEditorTests
    {
        private readonly List<Object> made = new();

        [TearDown]
        public void DestroyMade()
        {
            foreach (var each in made) if (each != null) Object.DestroyImmediate(each, true);
            made.Clear();
        }

        private Attribute NamedAttribute(string name)
        {
            var attribute = ScriptableObject.CreateInstance<Attribute>();
            attribute.Name = name;
            attribute.name = name;
            made.Add(attribute);
            return attribute;
        }

        private ScalerAsset AssetOf(AbstractScaler scaler)
        {
            var asset = ScriptableObject.CreateInstance<ScalerAsset>();
            asset.name = "Tried";
            asset.Scaler = scaler;
            made.Add(asset);
            return asset;
        }

        // An attribute read as it is, the caster's or the target's
        private static ScalableFloat Read(Attribute attribute, ESourceTarget from) => new(
            new AttributeBackedScaler { CaptureAttribute = attribute, CaptureFrom = from, ReadAs = EAttributeRead.AsIs }, 1f,
            EMagnitudeOperation.UseScaler);

        // What a hit leaves of itself against the target's Armor: 100 ÷ (100 + Armor)
        private static StackingCurveKind ArmorLeaves(Attribute armor) => new()
        {
            Curve = EStackingCurve.Reciprocal, Coefficient = 0.01f, Count = Read(armor, ESourceTarget.Target),
        };

        /// <summary>A try reads its scaler at each level of its range, with the parts behind each (LVL-5).</summary>
        [Test]
        public void LVL_5_A_try_reads_a_scaler_at_each_level()
        {
            var tried = new ScalerTry { From = 1, To = 3 };
            var reading = tried.Read(new SimpleScaler { LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = false });
            Assert.AreEqual((1, 3), (reading.From, reading.To), "levels 1 to 3");
            Assert.That(reading.Values, Is.EqualTo(new[] { 10f, 20f, 30f }), "its value at each");
            Assert.That(reading.Parts.Length, Is.EqualTo(3), "and the parts behind each");
            StringAssert.Contains("= 20", reading.Parts[1].ToString(), "level 2's parts give 20");

            tried.To = 40;
            Assert.AreEqual(ScalerTry.MostLevels, tried.Read(new SimpleScaler { LevelValues = new[] { 1f } }).Values.Length,
                "at most as many levels as Levels shows");
        }

        /// <summary>
        /// A try finds what its scaler reads, each attribute on its side, and reads the values typed for it: the target's
        /// Armor, 0 until typed, then 100, which leaves a half; and a side without it reads as an entity without it does.
        /// </summary>
        [Test]
        public void LVL_5_A_try_finds_what_a_scaler_reads_and_reads_what_is_typed()
        {
            var armor = NamedAttribute("Armor");
            var strength = NamedAttribute("Strength");
            var tried = new ScalerTry { From = 1, To = 2 };

            var curve = ArmorLeaves(armor);
            Assert.That(tried.Read(curve).Values, Is.EqualTo(new[] { 1f, 1f }), "Armor 0 leaves it whole");
            Assert.That(tried.Target.Attributes.Select(row => row.Attribute), Is.EqualTo(new[] { armor }), "it reads the target's Armor");
            Assert.That(tried.Caster.Attributes, Is.Empty, "and nothing of the caster");

            tried.Target.Set(armor, 100f);
            Assert.That(tried.Read(curve).Values, Is.EqualTo(new[] { 0.5f, 0.5f }).Within(0.0001f), "100 Armor leaves a half");
            StringAssert.Contains("Armor", tried.Read(curve).Parts[0].ToString(), "and its parts read it");

            tried.Target.Lack(armor);
            Assert.That(tried.Read(curve).Values, Is.EqualTo(new[] { 1f, 1f }), "a target without Armor reads it as 0");

            var caster = new ScalerTry { From = 1, To = 1 };
            caster.Read(new AttributeBackedScaler { CaptureAttribute = strength, CaptureFrom = ESourceTarget.Source, ReadAs = EAttributeRead.AsIs });
            Assert.That(caster.Caster.Attributes.Select(row => row.Attribute), Is.EqualTo(new[] { strength }), "a caster's read is the caster's");
        }

        /// <summary>A try takes a caller's value for each key its scaler reads: unset, the scaler's default; set, the value.</summary>
        [Test]
        public void LVL_5_A_try_takes_a_callers_value()
        {
            Tag key = "Test.LVL5.Bonus";
            var scaler = new SetByCallerScaler { Key = key, Default = 5f };
            var tried = new ScalerTry { From = 1, To = 1 };

            Assert.AreEqual(5f, tried.Read(scaler).Values[0], 0.0001f, "unset, its default");
            Assert.That(tried.Caller.Select(each => each.Key), Is.EqualTo(new[] { key }), "the key it asked for");
            tried.SetCaller(key, 12f);
            Assert.AreEqual(12f, tried.Read(scaler).Values[0], 0.0001f, "set, the caller's value");
            tried.UnsetCaller(key);
            Assert.AreEqual(5f, tried.Read(scaler).Values[0], 0.0001f, "and unset again, its default");
        }

        /// <summary>
        /// A side fills from an entity: each attribute the scaler reads takes what the entity's attribute set starts it at,
        /// at the entity's starting level, and one it doesn't give, it lacks. A typed value changes it after.
        /// </summary>
        [Test]
        public void LVL_5_A_side_fills_from_an_entity()
        {
            var armor = NamedAttribute("Armor");
            var resistance = NamedAttribute("Magic Resistance");
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            set.Name = "Knight Stats";
            set.Attributes.Add(new AttributeSetElement
            {
                Attribute = armor,
                Current = new AttributeMagnitudeSpec { Magnitude = 50f },
                Base = new AttributeMagnitudeSpec { Magnitude = 50f },
                Overflow = new AttributeOverflowData { Policy = EAttributeOverflowPolicy.Unlimited },
            });
            set.SubSets = new List<AttributeSet>();
            made.Add(set);
            var knight = ScriptableObject.CreateInstance<EntityIdentity>();
            knight.Name = "Knight";
            knight.AttributeSet = set;
            made.Add(knight);

            var curve = ArmorLeaves(armor);
            var tried = new ScalerTry { From = 1, To = 1 };
            tried.Read(curve);
            tried.Target.FillFrom(knight);
            Assert.AreSame(knight, tried.Target.Entity, "the target is the knight's");
            Assert.AreEqual(50f, tried.Target.Of(armor).Current, 0.0001f, "its Armor, as its set starts it");
            Assert.AreEqual(100f / 150f, tried.Read(curve).Values[0], 0.0001f, "which leaves two thirds");

            tried.Read(new AttributeBackedScaler { CaptureAttribute = resistance, CaptureFrom = ESourceTarget.Target, ReadAs = EAttributeRead.AsIs });
            Assert.IsFalse(tried.Target.Of(resistance).Has, "an attribute the knight's set doesn't give, it lacks");

            tried.Target.Set(armor, 200f);
            Assert.AreEqual(100f / 300f, tried.Read(curve).Values[0], 0.0001f, "a typed value after the fill reads");
        }

        /// <summary>
        /// A try is kept for the session, per asset: a Scaler asset's try comes back as it was left, and nothing of it is
        /// written into the asset.
        /// </summary>
        [Test]
        public void LVL_5_A_try_is_kept_for_the_session_not_on_the_asset()
        {
            var armor = NamedAttribute("Armor");
            var asset = AssetOf(ArmorLeaves(armor));
            var before = EditorJsonUtility.ToJson(asset, true);
            try
            {
                var tried = ScalerTry.For(asset);
                tried.To = 3;
                tried.Read(asset.Scaler);
                tried.Target.Set(armor, 100f);
                tried.Keep();

                var again = ScalerTry.For(asset);
                Assert.AreEqual(3, again.To, "its range kept");
                Assert.AreEqual(100f, again.Target.Of(armor)?.Current ?? -1f, 0.0001f, "and its values");
                Assert.AreEqual(before, EditorJsonUtility.ToJson(asset, true), "nothing written into the asset");
            }
            finally
            {
                ScalerTry.Forget(asset);
            }
        }

        // A runtime panel to build an inspector in, as the Inspector holds one, so its bindings and events run (ED-12's host)
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("LVL-5") { hideFlags = HideFlags.HideAndDontSave };
            private UnityEditor.Editor editor;

            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Inspect(Object target)
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                Document.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                return root;
            }

            public void Destroy()
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        /// <summary>
        /// A Scaler asset's inspector has a Try section after Values (LVL-5): a row of its value at each level, a field for
        /// each attribute it reads on each side, and the parts behind the selected level under the table. A value typed
        /// there reads at once, and the section, as Levels, has no Import or Clear.
        /// </summary>
        [UnityTest]
        public IEnumerator LVL_5_A_Scaler_assets_inspector_tries_it()
        {
            var armor = NamedAttribute("Armor");
            var asset = AssetOf(ArmorLeaves(armor));
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(asset);
                yield return Frames();

                var sections = root.Children().Select(child => child.name).ToList();
                Assert.That(sections.IndexOf("Try_CONTAINER"), Is.EqualTo(sections.IndexOf("Values_CONTAINER") + 1),
                    $"its Try section after Values: {string.Join(", ", sections)}");
                var section = root.Q("Try_CONTAINER");
                Assert.IsNull(section.Q("TryImport"), "no Import, as it only reads the asset");
                Assert.IsNull(section.Q("TryClear"), "and no Clear");

                var view = section.Q("ScalerTry");
                Assert.IsNotNull(view, "the try");
                var value = view.Query<VisualElement>(className: "pf-levels__row").ToList()
                    .First(row => row.Q<Label>(className: "pf-levels__name")?.text == "Value");
                Assert.That(value.Query<Label>(className: "pf-levels__cell").ToList().Select(cell => cell.text),
                    Is.EqualTo(Enumerable.Repeat("1", 5)), "Armor 0 leaves it whole at each level");

                var current = view.Q<FloatField>("Target Armor Current");
                Assert.IsNotNull(current, "a field for the target's Armor");
                current.value = 100f;
                yield return Frames();
                value = view.Query<VisualElement>(className: "pf-levels__row").ToList()
                    .First(row => row.Q<Label>(className: "pf-levels__name")?.text == "Value");
                Assert.That(value.Query<Label>(className: "pf-levels__cell").ToList().Select(cell => cell.text),
                    Is.EqualTo(Enumerable.Repeat("0.5", 5)), "100 Armor typed leaves a half");
                StringAssert.Contains("Armor", view.Q<Label>("TryParts").text, "the parts behind level 1 read it");
            }
            finally
            {
                host.Destroy();
                ScalerTry.Forget(asset);
            }
        }

        /// <summary>
        /// An effect's inspector has a Levels section: a row for each impact with its value at each level, and under the
        /// table, the parts behind the selected cell, the first impact's first level until another is picked (LVL-4).
        /// </summary>
        [Test]
        public void LVL_4_An_effect_shows_its_levels_and_the_parts_behind_a_cell()
        {
            var health = ScriptableObject.CreateInstance<Attribute>();
            health.Name = "Health";
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = health,
                MagnitudeOperation = new ScalableFloat(
                    new SimpleScaler { LevelValues = new[] { -10f, -20f, -30f }, StretchOverLevels = false }, 1f, EMagnitudeOperation.UseScaler),
                ImpactTypes = new System.Collections.Generic.List<Tag>(),
                Packets = System.Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Instant,
                DurationOperation = new ScalableFloat(0f),
                TicksOperation = new ScalableInt(0),
                TickIntervalOperation = new ScalableFloat(1f),
                StackingConfigs = new System.Collections.Generic.List<AbstractStackingConfig>(),
            };
            var editor = UnityEditor.Editor.CreateEditor(effect);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assume.That(root, Is.Not.Null, "the inspector builds");
                var view = root.Q("EffectLevels");
                Assert.IsNotNull(view, "the effect shows its levels");

                var rows = view.Query<VisualElement>(className: "pf-levels__row").ToList();
                Assert.That(rows.Count, Is.GreaterThanOrEqualTo(2), "a header and a row for its impact");
                var header = rows[0].Query<Label>(className: "pf-levels__cell").ToList().Select(cell => cell.text);
                Assert.That(header, Is.EqualTo(new[] { "1", "2", "3", "4", "5" }), "levels 1 to 5, as it isn't linked");
                var impact = rows.First(row => row.Q<Label>(className: "pf-levels__name").text.StartsWith("Health"));
                var cells = impact.Query<Label>(className: "pf-levels__cell").ToList().Select(cell => cell.text);
                Assert.That(cells, Is.EqualTo(new[] { "-10", "-20", "-30", "-30", "-30" }), "its impact at each level, the last past its values");

                var parts = view.Q<Label>("Parts");
                StringAssert.Contains("Health as it lands, level 1", parts.text, "the parts behind the first cell");
                StringAssert.Contains("= -10", parts.text, "with what the read gave");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(health);
            }
        }
    }
}
