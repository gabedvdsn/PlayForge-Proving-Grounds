using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// The user's word of 2026-11-19 (EP-80): a targeting task that casts its pointer's ray reaches any distance by default,
    /// as EP-66 made Select Position's, and the inspector shows a new one's distance as infinite, not 0.
    /// </summary>
    public class RaycastDistanceDefaultTests
    {
        // The field that limits how far a task's pointer's ray reaches
        private static readonly string[] RayDistances = { "MaxDistance", "MaxRayDistance" };

        // PlayForge's targeting tasks that cast their pointer's ray, as their Raycast Layers say
        private static List<Type> RaycastTasks() =>
            TypeCache.GetTypesDerivedFrom<AbstractTargetingAbilityTask>()
                .Where(type => !type.IsAbstract && type.Assembly == typeof(AbstractTargetingAbilityTask).Assembly
                               && type.GetField("RaycastLayers") != null)
                .OrderBy(type => type.Name).ToList();

        private static FieldInfo RayDistance(Type type) =>
            RayDistances.Select(type.GetField).FirstOrDefault(field => field != null && field.FieldType == typeof(ScalableFloat));

        // A runtime panel to draw fields in, since batch mode has no window to give them one
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("EP-80") { hideFlags = HideFlags.HideAndDontSave };

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

        /// <summary>Each targeting task that casts its pointer's ray reaches any distance as it's made (EP-80).</summary>
        [Test]
        public void EP_80_Each_raycast_targeting_task_reaches_any_distance_by_default()
        {
            var tasks = RaycastTasks();
            Assert.That(tasks.Count, Is.GreaterThanOrEqualTo(5), string.Join(", ", tasks.Select(type => type.Name)));
            var wrong = new List<string>();
            foreach (var type in tasks)
            {
                var field = RayDistance(type);
                if (field == null)
                {
                    wrong.Add($"{type.Name} names no ray distance");
                    continue;
                }
                var number = (ScalableFloat)field.GetValue(Activator.CreateInstance(type));
                if (number == null || !number.IsConstant || !float.IsPositiveInfinity(number.Constant))
                    wrong.Add($"{type.Name}.{field.Name} is {(number == null ? "none" : number.Constant.ToString())}");
            }
            Assert.That(wrong, Is.Empty, string.Join("; ", wrong));
        }

        /// <summary>
        /// Picked as an ability's targeting, as its menu makes it, each such task's distance shows as infinite in the
        /// inspector, not 0 (EP-80).
        /// </summary>
        [UnityTest]
        public IEnumerator EP_80_The_inspector_shows_a_new_raycast_tasks_distance_as_infinite()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            var host = new Host();
            var wrong = new List<string>();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                foreach (var type in RaycastTasks())
                {
                    var serialized = new SerializedObject(ability);
                    var targeting = serialized.FindProperty($"{nameof(Ability.Behaviour)}.{nameof(AbilityBehaviour.Targeting)}");
                    targeting.managedReferenceValue = Activator.CreateInstance(type);
                    serialized.ApplyModifiedProperties();
                    var field = host.Draw(serialized, targeting.propertyPath);
                    for (int i = 0; i < 4; i++) yield return null;
                    var distance = field.Query<PropertyField>().ToList()
                        .FirstOrDefault(each => each.bindingPath != null && RayDistances.Any(name => each.bindingPath.EndsWith("." + name)));
                    var number = distance?.Q<FloatField>("Number");
                    if (number == null) wrong.Add($"{type.Name}: no number field for its distance");
                    else if (!float.IsPositiveInfinity(number.value)) wrong.Add($"{type.Name}: shows {number.value} (\"{number.text}\")");
                    field.RemoveFromHierarchy();
                }
                Assert.That(wrong, Is.Empty, string.Join("; ", wrong));
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(ability);
            }
        }
    }
}
