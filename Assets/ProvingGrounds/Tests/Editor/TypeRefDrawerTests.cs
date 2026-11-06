using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// The drawers that pick a [SerializeReference] field's type. A field is drawn with a PropertyField of its own in a
    /// runtime panel, since batch mode has no graphics device to give an editor window one, so its bindings run there.
    /// </summary>
    public class TypeRefDrawerTests
    {
        /// <summary>
        /// A drawer with a default type wrote it into an empty field as it drew, so opening an asset changed it, and a
        /// field whose class Unity couldn't find lost the data Unity kept for it (ED-12). An empty scaler behaviour shows.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_Drawing_an_empty_type_field_leaves_it_empty()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification ??= new GameplayEffectDuration();
            effect.DurationSpecification.DurationOperation ??= new ScalerMagnitudeOperation();
            var scaler = new SimpleScaler();
            scaler.Behaviours.Add(null);
            effect.DurationSpecification.DurationOperation.Scaler = scaler;
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var owner = new GameObject("ED-12") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                Assume.That(document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                var serialized = new SerializedObject(effect);
                var slot = serialized.FindProperty("DurationSpecification.DurationOperation.Scaler.Behaviours.Array.data[0]");
                Assume.That(slot, Is.Not.Null, "the scaler's first behaviour");
                var field = new PropertyField(slot);
                document.rootVisualElement.Add(field);
                field.Bind(serialized);
                for (int i = 0; i < 4; i++) yield return null;

                Assert.IsNull(((SimpleScaler)effect.DurationSpecification.DurationOperation.Scaler).Behaviours[0],
                    "drawing the empty behaviour left it empty");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(effect);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The kind field (ED-56)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>An asset with fields of a kind the type drawers draw: a task, a list of tasks, and a cue.</summary>
        public sealed class TypeRefHolder : ScriptableObject
        {
            [SerializeReference] public AbstractAbilityTask Task;
            [SerializeReference] public List<AbstractAbilityTask> Tasks = new();
            [SerializeReference] public AbstractCueHandler Cue;
        }

        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new("ED-56") { hideFlags = HideFlags.HideAndDontSave };
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

        // Nested fields build a few frames late
        private static IEnumerator Until(Func<bool> done, double seconds = 1.0)
        {
            var until = EditorApplication.timeSinceStartup + seconds;
            while (!done() && EditorApplication.timeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// A task field was a button that opened a list drawn under it in tinted boxes, its fields in a box with an orange
        /// left border, from styles set in C# (ED-56, the user's report). It's Unity's field, its label in the label
        /// column and the kind's name in its box, with the kind's own fields under it.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_56_A_kind_field_is_Unitys_field_with_the_kinds_fields_under_it()
        {
            var holder = ScriptableObject.CreateInstance<TypeRefHolder>();
            holder.Task = new DelayTask { DelayMilliseconds = 250 };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(TypeRefHolder.Task));
                yield return Until(() => field.Q<PropertyField>("DelayMilliseconds")?.Q<IntegerField>() != null);
                yield return Frames();

                var kind = field.Q(name: "TypeField");
                Assert.IsNotNull(kind, "the kind is a field");
                Assert.AreEqual("Task", kind.Q<Label>(className: "unity-base-field__label")?.text, "with its label in the label column");
                Assert.AreEqual("Delay", kind.Q<Label>("TypeName")?.text, "and the kind's name in its box");
                Assert.AreEqual(250, field.Q<PropertyField>("DelayMilliseconds")?.Q<IntegerField>()?.value, "the kind's own field shows under it");

                var wrong = field.Query<VisualElement>().ToList()
                    .Where(each => each.GetFirstAncestorOfType<PropertyField>() == field)
                    .SelectMany(each => ForgeInspectorTests.LookSetInCode(each).Select(style => $"{each.GetType().Name} \"{each.name}\" sets {style}"))
                    .ToList();
                Assert.That(wrong, Is.Empty, "the drawer sets no look in code");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// Every element of a list of tasks shares one drawer, which kept the last element's fields and its kind in fields
        /// of its own, so an edit to one element could fill another with its fields (ED-56). Each element shows its own
        /// kind's fields, after an edit to another as before it. Unity gives a list's elements one drawer by their path
        /// without its index, so the two elements are drawn as the list draws them, each its own field; a list's view
        /// builds no rows in a panel with no screen to size it.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_56_Each_element_of_a_list_shows_its_own_kinds_fields()
        {
            var holder = ScriptableObject.CreateInstance<TypeRefHolder>();
            holder.Tasks = new List<AbstractAbilityTask> { new DelayTask { DelayMilliseconds = 100 }, new ChannelAbilityTask { ChannelDuration = 2f } };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(holder);
                var first = host.Draw(serialized, $"{nameof(TypeRefHolder.Tasks)}.Array.data[0]");
                var second = host.Draw(serialized, $"{nameof(TypeRefHolder.Tasks)}.Array.data[1]");
                List<VisualElement> Kinds() => new[] { first, second }.Select(each => each.Q(name: "TypeRef")).Where(each => each != null).ToList();
                yield return Until(() => Kinds().Count == 2 && Kinds()[1].Q<PropertyField>("ChannelDuration") != null);
                yield return Frames();

                serialized.FindProperty($"{nameof(TypeRefHolder.Tasks)}.Array.data[0].{nameof(DelayTask.DelayMilliseconds)}").intValue = 500;
                serialized.ApplyModifiedProperties();
                yield return Frames();

                var kinds = Kinds();
                Assert.AreEqual(2, kinds.Count, "each element is a kind field");
                Assert.IsNotNull(kinds[0].Q<PropertyField>("DelayMilliseconds"), "the first shows its Delay's field");
                Assert.IsNull(kinds[0].Q<PropertyField>("ChannelDuration"), "and not the second's");
                Assert.IsNotNull(kinds[1].Q<PropertyField>("ChannelDuration"), "the second its Channel's, after an edit to the first");
                Assert.IsNull(kinds[1].Q<PropertyField>("DelayMilliseconds"), "and not the first's");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>Picking a kind makes one, and one Undo takes it back; the field shows the kind's fields (ED-56).</summary>
        [UnityTest]
        public IEnumerator ED_56_Picking_a_kind_is_one_Undo_step_and_shows_its_fields()
        {
            var holder = ScriptableObject.CreateInstance<TypeRefHolder>();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(holder);
                var field = host.Draw(serialized, nameof(TypeRefHolder.Task));
                yield return Frames();
                Assert.AreEqual("None", field.Q<Label>("TypeName")?.text, "an empty field reads None");

                var drawer = new AbilityTaskDrawer();
                Undo.IncrementCurrentGroup();
                typeof(AbstractTypeRefDrawer<AbstractAbilityTask>).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(drawer, new object[] { serialized.FindProperty(nameof(TypeRefHolder.Task)), typeof(ChannelAbilityTask) });
                Assert.IsInstanceOf<ChannelAbilityTask>(holder.Task, "picking a kind makes one");
                yield return Until(() => field.Q<PropertyField>("ChannelDuration") != null);
                Assert.AreEqual("Channel", field.Q<Label>("TypeName")?.text, "the field names it");
                Assert.IsNotNull(field.Q<PropertyField>("ChannelDuration"), "and shows its fields");

                Undo.PerformUndo();
                Assert.IsNull(holder.Task, "one Undo takes it back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>The picker offers each concrete kind by its name, as the field shows it, and no abstract one (ED-56).</summary>
        [Test]
        public void ED_56_The_picker_offers_the_concrete_kinds_by_name()
        {
            var holder = ScriptableObject.CreateInstance<TypeRefHolder>();
            try
            {
                var prop = new SerializedObject(holder).FindProperty(nameof(TypeRefHolder.Task));
                var kinds = ((IEnumerable<(Type Type, string Name)>)typeof(AbstractTypeRefDrawer<AbstractAbilityTask>)
                    .GetMethod("KindsFor", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(new AbilityTaskDrawer(), new object[] { prop })).ToList();
                Assert.That(kinds, Has.Some.EqualTo((typeof(DelayTask), "Delay")), "Delay, by its name");
                Assert.That(kinds, Has.Some.EqualTo((typeof(ChannelAbilityTask), "Channel")), "Channel");
                Assert.That(kinds.Select(kind => kind.Type), Has.None.Matches<Type>(type => type.IsAbstract), "no abstract kind");
                Assert.That(kinds.Select(kind => kind.Name), Is.Ordered.Using((IComparer<string>)StringComparer.OrdinalIgnoreCase), "in name order");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// A generic field folds, as an attribute set element does, to one line of its values, where it drew colored
        /// badges: a camera shake reads "Strength 2 · Duration 0.5 · On Each Tick" (ED-56).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_56_A_generic_kind_folds_to_a_line_of_its_values()
        {
            var holder = ScriptableObject.CreateInstance<TypeRefHolder>();
            holder.Cue = new CameraShakeCue { Strength = 2f, Duration = 0.5f, OnEachTick = true };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = host.Draw(new SerializedObject(holder), nameof(TypeRefHolder.Cue));
                yield return Until(() => field.Q<Label>("Summary") != null);
                yield return Frames();

                Assert.IsNotNull(field.Q(name: "CollapseBtn"), "it folds");
                Assert.AreEqual("Strength 2 · Duration 0.5 · On Each Tick", field.Q<Label>("Summary")?.text, "to a line of its values");
                Assert.IsNull(field.Q(name: "summary"), "with no badges");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// An Inject task's injection got its Interrupt default only from the drawer, as it drew. A new task now starts
        /// with it, from the field's initializer, which the type picker runs when it makes a task (ED-12).
        /// </summary>
        [Test]
        public void ED_12_A_new_inject_task_interrupts()
        {
            Assert.IsInstanceOf<InterruptSequenceInjection>(new InjectAbilityTask().Injection);
        }
    }
}
