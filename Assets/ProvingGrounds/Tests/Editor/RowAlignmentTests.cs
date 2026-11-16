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
            using var open = new OpenSection(typeof(FarEmerald.PlayForge.Extended.Editor.GameplayEffectEditor), "Tags");
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
            using var open = new OpenSection(typeof(FarEmerald.PlayForge.Extended.Editor.AbilityEditor), "Activation");
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
    }
}
