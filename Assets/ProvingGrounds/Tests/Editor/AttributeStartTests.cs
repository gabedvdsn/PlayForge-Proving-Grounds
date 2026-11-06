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
    /// What each attribute an entity starts at, as the attribute set element and the attribute table show it (ED-49, the
    /// user's report): worked out by the runtime, from the element's magnitudes, its scalers at each level, its bounds and
    /// its rounding, with the set's sub-sets, and as part of a set that holds it. The element showed its magnitudes as a
    /// formula, as "10 + Level", and the table read "Scaled" for any value a scaler sets.
    /// </summary>
    public class AttributeStartTests
    {
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("ED-49") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public PropertyField Draw(Object target, string path)
            {
                var serialized = new SerializedObject(target);
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

        // An element drawn in a runtime panel builds its nested fields a few frames late
        private static IEnumerator Until(System.Func<bool> done, double seconds = 1.0)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (!done() && EditorApplication.timeSinceStartup < until) yield return null;
        }

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
            made.Add(attribute);
            return attribute;
        }

        private AttributeSet Set(string name, params AttributeSetElement[] elements)
        {
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            set.Name = name;
            set.Attributes.AddRange(elements);
            set.SubSets = new List<AttributeSet>();
            made.Add(set);
            return set;
        }

        private static AttributeSetElement Element(Attribute attribute, float current, float baseValue,
            EAttributeOverflowPolicy range = EAttributeOverflowPolicy.Unlimited) => new()
        {
            Attribute = attribute,
            Current = new AttributeMagnitudeSpec { Magnitude = current },
            Base = new AttributeMagnitudeSpec { Magnitude = baseValue },
            Overflow = new AttributeOverflowData { Policy = range },
        };

        // Base read from a scaler's value at each level, and Current the same as Base
        private static AttributeSetElement ByLevel(Attribute attribute, params float[] values)
        {
            var element = Element(attribute, 0, 0, EAttributeOverflowPolicy.ZeroToBase);
            element.LinkCurrentToBase = true;
            element.Base = new AttributeMagnitudeSpec
            {
                RealMagnitude = EMagnitudeOperation.UseScaler,
                Scaling = new CachedSimpleScaler { LevelValues = values, StretchOverLevels = false },
            };
            return element;
        }

        // Base read from another attribute, times a number
        private static AttributeSetElement Reading(Attribute attribute, Attribute read, float times)
        {
            var element = Element(attribute, 0, 0);
            element.LinkCurrentToBase = true;
            element.Base = new AttributeMagnitudeSpec
            {
                Magnitude = times,
                RealMagnitude = EMagnitudeOperation.MultiplyWithScaler,
                Scaling = new CachedAttributeScaler { CaptureAttribute = read, CaptureWhat = EAttributeTargetBinary.Base },
            };
            return element;
        }

        private static void AssertStarts(AttributeStartValues.Result result, Attribute attribute, int level, float current, float baseValue, string why)
        {
            var start = result.Of(attribute);
            Assert.IsNotNull(start, $"{attribute.Name} starts ({why})");
            var value = start.At(level);
            Assert.AreEqual(current, value.CurrentValue, 0.001f, $"{attribute.Name}'s Current at level {level}: {why}");
            Assert.AreEqual(baseValue, value.BaseValue, 0.001f, $"{attribute.Name}'s Base at level {level}: {why}");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The values, as the runtime works them out
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// An attribute starts at its magnitudes, within its bounds and rounded, as the runtime starts it: Health's Current
        /// of 150 is held to its Base of 100, and Gold's 10.7 rounds down, at each of the set's levels.
        /// </summary>
        [Test]
        public void ED_49_An_attribute_starts_at_its_magnitudes_bounds_and_rounding()
        {
            var health = NamedAttribute("Health");
            var gold = NamedAttribute("Gold");
            var coins = Element(gold, 10.7f, 10.7f);
            coins.Constraints = new AttributeConstraints { AutoClamp = true, RoundingMode = EAttributeRoundingPolicy.ToFloor };
            var set = Set("Stats", Element(health, 150, 100, EAttributeOverflowPolicy.ZeroToBase), coins);

            var result = AttributeStartValues.Of(set);

            Assert.IsNull(result.Problem, "the values are worked out");
            Assert.AreEqual((1, 4), (result.FirstLevel, result.LastLevel), "over the set's levels");
            for (int level = 1; level <= 4; level++)
            {
                AssertStarts(result, health, level, 100, 100, "Current held to Base");
                AssertStarts(result, gold, level, 10, 10, "rounded down");
            }
            Assert.That(result.Attributes.Select(start => start.Attribute), Is.EqualTo(new[] { health, gold }), "in the set's order");
        }

        /// <summary>A scaler sets an attribute at each level: Health by level reads 100, 120, 140 and 160.</summary>
        [Test]
        public void ED_49_A_scaled_attribute_starts_at_its_value_for_each_level()
        {
            var health = NamedAttribute("Health");
            var set = Set("Stats", ByLevel(health, 100, 120, 140, 160));

            var result = AttributeStartValues.Of(set);

            Assert.IsNull(result.Problem, "the values are worked out");
            float[] expected = { 100, 120, 140, 160 };
            for (int level = 1; level <= 4; level++) AssertStarts(result, health, level, expected[level - 1], expected[level - 1], "its level's value");
            Assert.AreEqual("100/120/140/160", AttributeStartValues.Text(result.Of(health)), "as a series");
        }

        /// <summary>
        /// Where a set and its sub-set both give an attribute, the runtime keeps one, by the set's collision setting: under
        /// Use Maximum, the sub-set's higher Health. The result says which element that is.
        /// </summary>
        [Test]
        public void ED_49_A_sub_set_settles_a_collision()
        {
            var health = NamedAttribute("Health");
            var speedy = Set("Speedy", Element(health, 150, 150));
            var mine = Element(health, 120, 120);
            var stats = Set("Goblin Stats", mine);
            stats.SubSets.Add(speedy);

            var result = AttributeStartValues.Of(stats);

            AssertStarts(result, health, 1, 150, 150, "Use Maximum keeps the sub-set's");
            var start = result.Of(health);
            Assert.That(start.From, Is.EqualTo(new[] { speedy.Attributes[0] }), "from the sub-set's element");
            Assert.That(start.Elements, Is.EqualTo(new[] { mine, speedy.Attributes[0] }), "of the two that give it");
            Assert.AreSame(speedy, result.SetOf(speedy.Attributes[0]), "and the set it's in");
        }

        /// <summary>
        /// A set whose attribute reads another it doesn't give reads 0 alone, and its value as part of a set that holds it:
        /// Max Health is ten times Constitution, which only the set holding it gives.
        /// </summary>
        [Test]
        public void ED_49_A_set_starts_as_part_of_a_set_that_holds_it()
        {
            var constitution = NamedAttribute("Constitution");
            var maxHealth = NamedAttribute("Max Health");
            var derived = Set("Derived", Reading(maxHealth, constitution, 10));
            var warrior = Set("Warrior Stats", Element(constitution, 15, 15));
            warrior.SubSets.Add(derived);

            AssertStarts(AttributeStartValues.Of(derived), maxHealth, 1, 0, 0, "alone, Constitution reads 0");
            AssertStarts(AttributeStartValues.Of(warrior), maxHealth, 1, 150, 150, "in Warrior Stats, ten times its 15");
            Assert.That(AttributeStartValues.MissingReads(derived.Attributes[0], AttributeStartValues.Of(derived)),
                Is.EqualTo(new[] { constitution }), "alone, it reads an attribute its set doesn't give");
            Assert.That(AttributeStartValues.MissingReads(derived.Attributes[0], AttributeStartValues.Of(warrior)), Is.Empty,
                "and in Warrior Stats, none");
        }

        /// <summary>The sets that hold a set, directly or through another, are the sets it can start as part of.</summary>
        [Test]
        public void ED_49_The_sets_that_hold_a_set()
        {
            const string folder = "Assets/ED-49 Holders";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-49 Holders");
            try
            {
                var health = NamedAttribute("Health");
                AssetDatabase.CreateAsset(health, $"{folder}/Health.asset");
                var core = Set("Core", Element(health, 10, 10));
                AssetDatabase.CreateAsset(core, $"{folder}/Core.asset");
                var middle = Set("Middle");
                middle.SubSets.Add(core);
                AssetDatabase.CreateAsset(middle, $"{folder}/Middle.asset");
                var top = Set("Top");
                top.SubSets.Add(middle);
                AssetDatabase.CreateAsset(top, $"{folder}/Top.asset");
                var apart = Set("Apart");
                AssetDatabase.CreateAsset(apart, $"{folder}/Apart.asset");
                AssetDatabase.SaveAssets();

                Assert.That(AttributeStartValues.HoldersOf(core).Select(set => set.Name), Is.EqualTo(new[] { "Middle", "Top" }),
                    "the set holding it, and the set holding that");
                Assert.That(AttributeStartValues.HoldersOf(top), Is.Empty, "and a set nothing holds has none");
            }
            finally
            {
                made.Clear();
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The element and the table
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// An element shows what its attribute starts at, at each level, both open and folded: "100/120/140/160" over
        /// levels 1–4. It showed "Current takes Base's · Base By Level".
        /// </summary>
        [UnityTest]
        public IEnumerator ED_49_An_element_shows_what_it_starts_at()
        {
            var health = NamedAttribute("Health");
            var set = Set("Stats", ByLevel(health, 100, 120, 140, 160));
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(set, $"{nameof(AttributeSet.Attributes)}.Array.data[0]");
                yield return Until(() => field.Q<TextField>("StartsAt") != null);
                yield return Frames();

                var starts = field.Q<TextField>("StartsAt");
                Assert.IsNotNull(starts, "the element shows what it starts at");
                Assert.AreEqual("100/120/140/160", starts.value, "its value at each level");
                Assert.AreEqual("Levels 1–4", field.Q<Label>("StartsAtLevels")?.text, "over the set's levels");
                Assert.AreEqual("Starts at 100/120/140/160 · 0–max", field.Q<Label>("Summary")?.text, "and folded, its line says so");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>
        /// An element whose attribute another set gives as well, and whose value the runtime keeps, says so: Goblin Stats'
        /// Health starts at Speedy's 150, under Use Maximum.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_49_An_element_says_when_another_sets_value_is_used()
        {
            var health = NamedAttribute("Health");
            var speedy = Set("Speedy", Element(health, 150, 150));
            var stats = Set("Goblin Stats", Element(health, 120, 120));
            stats.SubSets.Add(speedy);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(stats, $"{nameof(AttributeSet.Attributes)}.Array.data[0]");
                yield return Until(() => field.Q<TextField>("StartsAt") != null);
                yield return Frames();

                Assert.AreEqual("150", field.Q<TextField>("StartsAt")?.value, "it starts at the value the runtime keeps");
                var note = field.Q<Label>("StartsAtNote");
                Assert.IsNotNull(note, "with a note");
                Assert.IsFalse(note.ClassListContains("pf-hidden"), "that shows");
                Assert.AreEqual("Speedy gives Health too, and its value is the one used.", note.text, "naming the set whose value it is");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>
        /// An element that reads an attribute its set doesn't give says so, and the value it reads, 0, alone; shown as part
        /// of a set that holds it, its value is that set's, and the note goes.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_49_An_element_starts_as_part_of_the_set_its_previewed_in()
        {
            var constitution = NamedAttribute("Constitution");
            var maxHealth = NamedAttribute("Max Health");
            var derived = Set("Derived", Reading(maxHealth, constitution, 10));
            var warrior = Set("Warrior Stats", Element(constitution, 15, 15));
            warrior.SubSets.Add(derived);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(derived, $"{nameof(AttributeSet.Attributes)}.Array.data[0]");
                yield return Until(() => field.Q<TextField>("StartsAt") != null);
                yield return Frames();

                Assert.AreEqual("0", field.Q<TextField>("StartsAt")?.value, "alone, it reads 0");
                var warning = field.Q<HelpBox>("StartsAtWarning");
                Assert.IsNotNull(warning, "with a message");
                Assert.AreEqual("Reads Constitution, which Derived doesn't give, so it reads 0.", warning.text, "that says why");
                Assert.IsFalse(warning.ClassListContains("pf-hidden"), "and shows");

                // The scaler's attribute field, inside the list's element, keeps its label. It dropped it, as a field in a
                // list's row does
                ObjectField Capture() => field.Query<ObjectField>().ToList().FirstOrDefault(each => each.value == constitution);
                yield return Until(() => Capture() != null);
                Assert.IsNotNull(Capture(), "its scaler shows the attribute it reads");
                Assert.AreEqual("Capture Attribute", Capture().label, "with its label");

                AttributeStartValues.PreviewIn(derived, warrior);
                yield return Frames();
                Assert.AreEqual("150", field.Q<TextField>("StartsAt")?.value, "as part of Warrior Stats, ten times its Constitution");
                Assert.IsTrue(warning.ClassListContains("pf-hidden"), "and the message goes");
            }
            finally
            {
                AttributeStartValues.PreviewIn(derived, null);
                host.Destroy();
            }
        }

        /// <summary>
        /// A scaler spread over levels takes another number of points after a change to its kind is undone. The smoke
        /// suite's sweep of the Demos' Boss A set crashed the editor resizing one: the field wrote through a property kept
        /// since it was built, which pointed into a managed reference an Undo had put back. The sweep is what reproduces
        /// the crash; this checks the resize itself.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_49_A_spread_scaler_resizes_after_a_kind_change_is_undone()
        {
            var health = NamedAttribute("Health");
            var element = Element(health, 0, 0);
            element.LinkCurrentToBase = true;
            element.Base = new AttributeMagnitudeSpec
            {
                RealMagnitude = EMagnitudeOperation.UseScaler,
                Scaling = new CachedSimpleScaler { LevelValues = new[] { 1f, 2f, 3f, 4f }, StretchOverLevels = true },
            };
            var set = Set("Stats", element);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(set, $"{nameof(AttributeSet.Attributes)}.Array.data[0]");
                yield return Until(() => field.Q<IntegerField>("PointCount") != null);
                yield return Frames();

                var kind = field.Q<PopupField<string>>("Kind");
                Assert.IsNotNull(kind, "Base shows its kind");
                Undo.IncrementCurrentGroup();
                kind.index = (kind.index + 1) % kind.choices.Count;
                yield return Frames();
                Assume.That(set.Attributes[0].Base.Scaling, Is.Not.InstanceOf<CachedSimpleScaler>(), "the kind changed");
                Undo.PerformUndo();
                yield return Until(() => set.Attributes[0].Base.Scaling is CachedSimpleScaler && field.Q<IntegerField>("PointCount") != null);
                yield return Frames();

                var points = field.Q<IntegerField>("PointCount");
                Assert.IsNotNull(points, "the undone kind spreads its values again");
                points.value = 5;
                yield return Frames();
                Assert.AreEqual(5, set.Attributes[0].Base.Scaling.LevelValues.Length, "and takes a fifth point");
            }
            finally
            {
                host.Destroy();
            }
        }

        /// <summary>The attribute table shows a scaled attribute's values, as the element does. It read "Scaled".</summary>
        [Test]
        public void ED_49_The_attribute_table_shows_a_scaled_attributes_values()
        {
            var health = NamedAttribute("Health");
            var set = Set("Stats", ByLevel(health, 100, 120, 140, 160));
            var editor = UnityEditor.Editor.CreateEditor(set);
            try
            {
                var root = editor.CreateInspectorGUI();
                var start = root.Q(name: "AllAttributes_CONTAINER")?.Q<Label>(className: "pf-table__start");
                Assert.IsNotNull(start, "the table shows Health");
                Assert.AreEqual("100/120/140/160", start.text, "at each level");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        /// <summary>
        /// An element's Bounds, Constraints and their values take their look from the style sheet: they drew a box with a
        /// colored left border, badges, 9 and 10 px text and colored buttons, from styles set in C#.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_49_Bounds_and_constraints_set_no_look_in_code()
        {
            var health = NamedAttribute("Health");
            var element = Element(health, 50, 100, EAttributeOverflowPolicy.FloorToCeil);
            element.Constraints = new AttributeConstraints { RoundingMode = EAttributeRoundingPolicy.SnapTo, SnapInterval = 5 };
            var set = Set("Stats", element);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(set, $"{nameof(AttributeSet.Attributes)}.Array.data[0]");
                yield return Until(() => field.Q(name: "Floor") != null && field.Q(name: "SnapInterval") != null);
                yield return Frames();

                Assert.IsNotNull(field.Q(name: "Floor"), "Floor to Ceil shows its floor");
                Assert.IsNotNull(field.Q(name: "Ceil"), "and its ceiling");
                Assert.IsNotNull(field.Q(name: "SnapInterval"), "Snap To shows its interval");
                var wrong = field.Query<VisualElement>().ToList()
                    .SelectMany(each => ForgeInspectorTests.LookSetInCode(each).Select(style => $"{each.GetType().Name} \"{(each as TextElement)?.text ?? each.name}\" sets {style}"))
                    .ToList();
                Assert.That(wrong, Is.Empty, "the element, its bounds and its constraints set no look in code");
            }
            finally
            {
                host.Destroy();
            }
        }
    }
}
