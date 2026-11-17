using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// Every row of an inspector's section starts in one column (EP-4, the user's editor pass of 2026-11-15): a field's
    /// label, or a foldout's arrow, at the same place across. An effect's Tags → Purges, Unity's own foldout, hung its arrow
    /// to the left of the others', and an ability's Activation → Targeting, a kind field that folds, pushed its label to the
    /// right of the others'. Each inspector is laid out as the Inspector holds it, in an InspectorElement, in the editor's
    /// theme.
    /// </summary>
    public class RowAlignmentTests
    {
        private const float Tolerance = 1f;

        // A label's text sits this far past a foldout arrow's box, as Unity lays a field's label and a foldout's toggle
        private const float LabelInset = 5f;

        // A runtime panel in the editor's theme, as batch mode gives a window none to lay out in
        private sealed class Host : IDisposable
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("EP-4") { hideFlags = HideFlags.HideAndDontSave };
            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Add(VisualElement content)
            {
                var theme = EditorGUIUtility.isProSkin ? "Dark" : "Light";
                var sheet = EditorGUIUtility.Load($"StyleSheets/Generated/DefaultCommon{theme}_inter.uss.asset") as StyleSheet;
                Assert.IsNotNull(sheet, "the editor theme's style sheet loads");
                var root = Document.rootVisualElement;
                if (!root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
                var scroll = new ScrollView();
                scroll.style.width = 520;
                scroll.style.height = 2400;
                scroll.Add(content);
                root.Add(scroll);
                return content;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        // A section shown open, as its saved state says, and put back as it was after
        private sealed class OpenSection : IDisposable
        {
            private readonly string key;
            private readonly bool had;
            private readonly bool was;

            public OpenSection(Type editor, string section)
            {
                key = $"{editor.FullName}.{section}";
                had = EditorPrefs.HasKey(key);
                was = EditorPrefs.GetBool(key, true);
                EditorPrefs.SetBool(key, true);
            }

            public void Dispose()
            {
                if (had) EditorPrefs.SetBool(key, was);
                else EditorPrefs.DeleteKey(key);
            }
        }

        // Where a row starts across, in the arrows' column: its leftmost arrow on its first line, or its label there less
        // the label's inset, as a field's label sits past the arrow of a foldout beside it
        private static float StartOf(VisualElement row)
        {
            float top = row.worldBound.y;
            float least = float.MaxValue;
            foreach (var each in row.Query<VisualElement>().ToList())
            {
                if (!Leads(each) || !Shown(each)) continue;
                var bound = each.worldBound;
                if (bound.y > top + 16f) continue;
                least = Mathf.Min(least, each.ClassListContains(BaseField<int>.labelUssClassName) ? bound.x - LabelInset : bound.x);
            }
            return least;
        }

        private static bool Leads(VisualElement element) =>
            element.ClassListContains(BaseField<int>.labelUssClassName)
            || element.ClassListContains(Toggle.checkmarkUssClassName) && element.GetFirstAncestorOfType<Foldout>() != null
            && element.GetFirstAncestorOfType<Toggle>()?.ClassListContains(Foldout.toggleUssClassName) == true
            || element.ClassListContains("pf-element__arrow");

        private static bool Shown(VisualElement element)
        {
            var bound = element.worldBound;
            if (float.IsNaN(bound.x) || bound.width <= 0f || bound.height <= 0f) return false;
            for (var each = element; each != null; each = each.parent)
                if (each.resolvedStyle.display == DisplayStyle.None || each.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        private static string Starts(Dictionary<string, float> starts) =>
            string.Join(", ", starts.Select(each => $"{each.Key} {each.Value:0.#}"));

        // Frames until a condition holds or some seconds have gone, the player loop queued each frame, as a runtime panel
        // runs its bindings and its layout in the player loop, which edit mode runs only when asked
        private static IEnumerator Until(Func<bool> done, double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until && !done())
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
        }

        // An element and what it holds, a few levels down, by type, name and classes, for a failure to say what was drawn
        private static string Tree(VisualElement element, int depth) => element == null ? "null"
            : $"{element.GetType().Name}#{element.name}[{string.Join(" ", element.GetClasses())}]"
              + (depth > 0 && element.childCount > 0 ? "{" + string.Join(", ", element.Children().Select(child => Tree(child, depth - 1))) + "}" : "");

        // Frames until every row is drawn, as Unity binds an inspector in slices of 50 ms, or five seconds have gone
        private static IEnumerator Drawn(VisualElement inspector, IEnumerable<string> rows)
        {
            double until = EditorApplication.timeSinceStartup + 5;
            for (int frame = 0; EditorApplication.timeSinceStartup < until; frame++)
            {
                yield return null;
                if (frame >= 4 && rows.All(row => inspector.Q(name: row) is { } found && StartOf(found) < float.MaxValue)) break;
            }
            yield return null;
        }

        /// <summary>An effect's Tags section: its tag lists, Granted Abilities and Purges each start in one column.</summary>
        [UnityTest]
        public IEnumerator EP_4_An_effects_Tags_rows_start_in_one_column()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.name = "EP-4 Effect";
            UnityEditor.Editor editor = null;
            using var open = new OpenSection(typeof(FarEmerald.PlayForge.Editor.GameplayEffectEditor), "Tags");
            using var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(effect);
                var inspector = host.Add(new InspectorElement(editor));
                var rows = new[] { "GrantedTags", "GrantedAbilities", "ContextTags", "RemovesEffectsTagged", "Purges", "BlocksEffectsTagged" };
                yield return Drawn(inspector, rows);

                var starts = rows.ToDictionary(row => row, row => inspector.Q(name: row) is { } found ? StartOf(found) : float.NaN);
                Assert.That(starts.Values, Has.None.NaN.And.None.EqualTo(float.MaxValue), $"each row is drawn: {Starts(starts)}");
                Assert.That(starts.Values.Max() - starts.Values.Min(), Is.LessThan(Tolerance), $"each starts in one column: {Starts(starts)}");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// An ability's Activation section: its choices, numbers, lists, Cost, Cooldown and Targeting each start in one
        /// column, Targeting empty and with a kind of fields to fold.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_4_An_abilitys_Activation_rows_start_in_one_column([Values] bool targetingHasFields)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.name = "EP-4 Ability";
            ability.Behaviour ??= new AbilityBehaviour();
            ability.Behaviour.Targeting = targetingHasFields ? new SelectGasTargetInRangeTask() : null;
            UnityEditor.Editor editor = null;
            using var open = new OpenSection(typeof(FarEmerald.PlayForge.Editor.AbilityEditor), "Activation");
            using var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(ability);
                var inspector = host.Add(new InspectorElement(editor));
                var rows = new[] { "ActivationPolicy", "ActivateImmediately", "Triggers", "ProcCoefficient", "IgnoresCrowdControl", "Cost", "AttributeCosts", "Cooldown", "Targeting", "UseImplicitTargeting" };
                yield return Drawn(inspector, rows);

                var starts = rows.ToDictionary(row => row, row => inspector.Q(name: row) is { } found ? StartOf(found) : float.NaN);
                Assert.That(starts.Values, Has.None.NaN.And.None.EqualTo(float.MaxValue), $"each row is drawn: {Starts(starts)}");
                Assert.That(starts.Values.Max() - starts.Values.Min(), Is.LessThan(Tolerance), $"each starts in one column: {Starts(starts)}");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// A Levels row, as a Scaler asset's Try and an effect's Levels show one, holds its two levels in number fields alike:
        /// the first stretched across the whole row and the last stood 48 px wide (EP-18, the user's screenshot).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_18_A_Levels_rows_number_fields_are_alike([Values("ScalerTryView", "EffectLevelsView")] string view)
        {
            var scaler = ScriptableObject.CreateInstance<ScalerAsset>();
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            using var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var type = typeof(FarEmerald.PlayForge.Editor.TagDrawer).Assembly.GetType("FarEmerald.PlayForge.Editor." + view, true);
                ScriptableObject shown = view == "ScalerTryView" ? scaler : effect;
                var built = (VisualElement)type.GetConstructor(new[] { shown.GetType() }).Invoke(new object[] { shown });
                // In PlayForge's theme, as the inspector that holds the view gives it
                var root = new VisualElement();
                type.Assembly.GetType("FarEmerald.PlayForge.Editor.ForgeTheme", true)
                    .GetMethod("Apply", new[] { typeof(VisualElement) }).Invoke(null, new object[] { root });
                root.Add(built);
                host.Add(root);
                for (int i = 0; i < 6; i++) yield return null;

                var from = built.Q<IntegerField>("From").Q(className: BaseField<int>.inputUssClassName);
                var to = built.Q<IntegerField>("To").Q(className: BaseField<int>.inputUssClassName);
                Assert.That(Mathf.Abs(from.worldBound.width - to.worldBound.width), Is.LessThan(Tolerance),
                    $"the two fields alike: {from.worldBound.width:0.#} and {to.worldBound.width:0.#}");
                Assert.That(from.worldBound.width, Is.LessThan(100f), $"each a few digits wide: {from.worldBound.width:0.#}");
            }
            finally
            {
                Object.DestroyImmediate(scaler);
                Object.DestroyImmediate(effect);
            }
        }

        // An effect whose Duration reads an × Attribute scaler
        private static GameplayEffect AttributeDurationEffect()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.name = "ED-89 Effect";
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Durational,
                DurationOperation = new ScalableFloat { RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = new AttributeBackedScaler() },
            };
            return effect;
        }

        // The block an × Attribute scaler's fields sit on, its rows drawn, once Unity has bound the inspector down to it:
        // each level of drawers nested in an inspector binds in a later update, and the kind's fields, made after the
        // inspector was bound, are bound as the Inspector binds them
        private static IEnumerator ScalerBlock(VisualElement inspector, SerializedObject serialized, string[] rows, Action<VisualElement> found)
        {
            VisualElement Block() => inspector.Query(className: "pf-scaler-block").ToList().FirstOrDefault(each => each.Q(name: "CaptureAttribute") != null);
            yield return Until(() => Block() != null, 30);
            var block = Block();
            Assert.IsNotNull(block, "the scaler's fields sit on its block");
            block.Bind(serialized);
            yield return Until(() => rows.All(row => block.Q(name: row) is { } each && StartOf(each) < float.MaxValue), 30);
            yield return null;
            found(block);
        }

        /// <summary>
        /// An × Attribute scaler's block: its Combine, its fields, its Groups and its Behaviours each start in one column
        /// (ED-89, the user's screenshot of 2026-11-17). Groups, a class drawn as Unity draws one, hung its arrow 12 px into
        /// the margin, out to the block's edge and to the left of Behaviours' arrow.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_89_An_attribute_scalers_rows_start_in_one_column()
        {
            var effect = AttributeDurationEffect();
            UnityEditor.Editor editor = null;
            using var open = new OpenSection(typeof(FarEmerald.PlayForge.Editor.GameplayEffectEditor), "Duration");
            using var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(effect);
                var inspector = host.Add(new InspectorElement(editor));
                var rows = new[] { "Combine", "CaptureAttribute", "CaptureFrom", "CaptureWhen", "CaptureWhat", "Groups", "ReadAs", "Behaviours" };
                VisualElement block = null;
                yield return ScalerBlock(inspector, editor.serializedObject, rows, found => block = found);

                var starts = rows.ToDictionary(row => row, row => block.Q(name: row) is { } found ? StartOf(found) : float.NaN);
                Assert.That(starts.Values, Has.None.NaN.And.None.EqualTo(float.MaxValue), $"each row is drawn: {Starts(starts)}");
                Assert.That(starts.Values.Max() - starts.Values.Min(), Is.LessThan(Tolerance), $"each starts in one column: {Starts(starts)}");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// A read's list of groups shows only for the reads that name groups, Only These and All But These, as its
        /// [ForgeShowWhen] says (ED-89). Unity's drawing of the class showed it whatever Read said. Each read is drawn
        /// afresh: a runtime panel in edit mode doesn't poll the object for a change, which shows or hides it as one's made.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_89_A_reads_list_of_groups_shows_only_for_the_reads_that_name_groups([Values] ERetentionGroupRead reads)
        {
            var effect = AttributeDurationEffect();
            ((AttributeBackedScaler)effect.DurationSpecification.DurationOperation.Scaler).Groups = new RetentionGroups { Read = reads };
            UnityEditor.Editor editor = null;
            using var open = new OpenSection(typeof(FarEmerald.PlayForge.Editor.GameplayEffectEditor), "Duration");
            using var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(effect);
                // Groups open, as a user opens it
                var groups = editor.serializedObject.FindProperty("DurationSpecification.DurationOperation.Scaler.Groups");
                Assert.IsNotNull(groups, "the scaler's Groups");
                groups.isExpanded = true;
                var inspector = host.Add(new InspectorElement(editor));
                VisualElement block = null;
                yield return ScalerBlock(inspector, editor.serializedObject, new[] { "Groups" }, found => block = found);

                // Its fields, bound as the Inspector binds them where they bind late
                var row = block.Q(name: "Groups");
                VisualElement Field(string path) => row.Query<PropertyField>().ToList()
                    .FirstOrDefault(each => each.bindingPath != null && each.bindingPath.EndsWith(path));
                yield return Until(() => Field(".Groups.Read") is { } drawn && Shown(drawn), 10);
                if (!(Field(".Groups.Read") is { } first && Shown(first)))
                {
                    row.Bind(editor.serializedObject);
                    yield return Until(() => Field(".Groups.Read") is { } drawn && Shown(drawn), 10);
                }
                yield return null;
                Assume.That(Field(".Groups.Read") is { } read && Shown(read),
                    $"Groups' Read is drawn: {Tree(row, 4)}; open {row.Q<Foldout>()?.value}; Read at {Field(".Groups.Read")?.worldBound}");
                bool shown = Field(".Groups.Groups") is { } list && Shown(list);
                bool names = reads != ERetentionGroupRead.All;
                Assert.That(shown, Is.EqualTo(names), names
                    ? $"{reads} names its groups, so its list of groups shows"
                    : "All reads every group and names none, so its list of groups is hidden");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// An ability's Turn To Face: its Turns and its Turn start in one column (ED-89, the user's word of 2026-11-17).
        /// Turn, a class drawn as Unity draws one, hung its arrow into the margin, to the left of Turns' label.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_89_Turn_To_Faces_rows_start_in_one_column()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.name = "ED-89 Ability";
            ability.Components = new List<AbstractAbilityComponent> { new TurnToFaceAbilityComponent() };
            UnityEditor.Editor editor = null;
            using var open = new OpenSection(typeof(FarEmerald.PlayForge.Editor.AbilityEditor), "Components");
            using var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                editor = UnityEditor.Editor.CreateEditor(ability);
                // Its component open, as the user's was
                var element = editor.serializedObject.FindProperty("Components.Array.data[0]");
                Assert.IsNotNull(element, "the ability's component");
                element.isExpanded = true;
                var inspector = host.Add(new InspectorElement(editor));
                var rows = new[] { "Turns", "Turn" };

                // The component's fields, once Unity has bound the inspector down to them
                VisualElement Fields() => inspector.Query(name: "TypeFields").ToList().FirstOrDefault(each => each.Q(name: "Turns") != null);
                yield return Until(() => Fields() != null, 30);
                var fields = Fields();
                Assert.IsNotNull(fields, "the component's fields are drawn");
                fields.Bind(editor.serializedObject);
                yield return Until(() => rows.All(row => fields.Q(name: row) is { } each && StartOf(each) < float.MaxValue), 30);
                yield return null;

                var starts = rows.ToDictionary(row => row, row => fields.Q(name: row) is { } found ? StartOf(found) : float.NaN);
                Assert.That(starts.Values, Has.None.NaN.And.None.EqualTo(float.MaxValue), $"each row is drawn: {Starts(starts)}; {Tree(fields, 3)}");
                Assert.That(starts.Values.Max() - starts.Values.Min(), Is.LessThan(Tolerance), $"each starts in one column: {Starts(starts)}");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(ability);
            }
        }
    }
}
