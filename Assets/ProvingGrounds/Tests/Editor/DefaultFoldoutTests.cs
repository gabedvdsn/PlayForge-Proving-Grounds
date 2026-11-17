using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// No class PlayForge's inspectors draw is left to Unity's default foldout (ED-90, the user's word of 2026-11-17,
    /// after ED-89): Unity hangs that foldout's arrow 12 px into the margin, out of the column the rows around it keep.
    /// Each class a PlayForge type serializes in a field, and each class those hold in turn, through every kind a managed
    /// reference can hold, has a drawer of its own, PlayForge's aligned foldout where it has nothing else.
    /// </summary>
    public class DefaultFoldoutTests
    {
        // PlayForge's own runtime, whose types its inspectors draw
        private static readonly string[] Runtime = { "FarEmerald.PlayForge", "FarEmerald.PlayForge.Lab" };

        [Test]
        public void ED_90_No_class_is_drawn_as_Unitys_default_foldout()
        {
            var left = DefaultFoldouts().Select(each => $"{each.Key.FullName}, as {each.Value}").OrderBy(each => each, StringComparer.Ordinal).ToList();
            Assert.That(left, Is.Empty, $"{left.Count} classes are drawn as Unity's default foldout:\n{string.Join("\n", left)}");
        }

        /// <summary>The scan reaches the two classes ED-89 found by eye, and finds each drawn by the aligned foldout.</summary>
        [Test]
        public void ED_90_The_scan_reaches_ED_89s_classes()
        {
            var reached = Reached();
            Assert.That(reached, Does.Contain(typeof(RetentionGroups)), "an × Attribute scaler's Groups is reached");
            Assert.That(reached, Does.Contain(typeof(TurnToFaceSettings)), "a Turn To Face's Turn is reached");
            Assert.That(DrawerOf(typeof(RetentionGroups))?.Name, Is.EqualTo("AlignedFoldoutDrawer"), "Groups has the aligned foldout");
            Assert.That(DrawerOf(typeof(TurnToFaceSettings))?.Name, Is.EqualTo("AlignedFoldoutDrawer"), "Turn has the aligned foldout");
        }

        // Each class drawn as Unity's default foldout, and a field that holds it
        private static Dictionary<Type, string> DefaultFoldouts()
        {
            var found = new Dictionary<Type, string>();
            Walk((holder, field, held) =>
            {
                if (DrawerOf(held) == null && !DrawnByItsAttribute(field) && !found.ContainsKey(held)) found[held] = $"{holder.Name}.{field.Name}";
            });
            return found;
        }

        // Each class the walk reaches
        private static HashSet<Type> Reached()
        {
            var reached = new HashSet<Type>();
            Walk((_, _, held) => reached.Add(held));
            return reached;
        }

        // Every class a PlayForge type serializes in a field, from each asset and behaviour of PlayForge's runtime, down
        // through the classes those hold and each kind a managed reference can hold
        private static void Walk(Action<Type, FieldInfo, Type> each)
        {
            var seen = new HashSet<Type>();
            var queue = new Queue<Type>(TypeCache.GetTypesDerivedFrom<ScriptableObject>().Concat(TypeCache.GetTypesDerivedFrom<MonoBehaviour>()).Where(Ours));
            while (queue.Count > 0)
            {
                var type = queue.Dequeue();
                if (!seen.Add(type)) continue;
                foreach (var field in SerializedFields(type))
                {
                    var held = ElementOf(field.FieldType);
                    if (held == null) continue;
                    if (field.GetCustomAttribute<SerializeReference>() != null)
                    {
                        // Each kind it can hold, which its kind's drawer draws field by field
                        foreach (var kind in TypeCache.GetTypesDerivedFrom(held).Append(held).Where(Ours).Where(Concrete)) queue.Enqueue(kind);
                        continue;
                    }
                    if (!Folds(held)) continue;
                    each(type, field, held);
                    queue.Enqueue(held);
                }
            }
        }

        private static bool Ours(Type type) => type != null && Runtime.Contains(type.Assembly.GetName().Name);

        private static bool Concrete(Type type) => !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters;

        // A class or a struct of PlayForge's own that Unity serializes as a group of fields, so it folds
        private static bool Folds(Type type) =>
            Ours(type) && type.IsSerializable && !type.IsEnum && !type.IsPrimitive && !typeof(UnityEngine.Object).IsAssignableFrom(type)
            && !type.ContainsGenericParameters && (type.IsClass || type.IsValueType && !type.IsPrimitive);

        // A list's or an array's element, or the type itself
        private static Type ElementOf(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return type;
        }

        // The fields Unity serializes and draws: each instance field, a base class's private ones among them, that's public
        // or marked to serialize, not marked to skip or to hide
        private static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            for (var level = type; level != null && level != typeof(object) && level != typeof(ScriptableObject) && level != typeof(MonoBehaviour); level = level.BaseType)
            {
                foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsNotSerialized) continue;
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null && field.GetCustomAttribute<SerializeReference>() == null) continue;
                    if (field.GetCustomAttribute<HideInInspector>() != null) continue;
                    yield return field;
                }
            }
        }

        // A field a property attribute draws, as a tag list its context's drawer does
        private static bool DrawnByItsAttribute(FieldInfo field) =>
            field.GetCustomAttributes<UnityEngine.PropertyAttribute>(true).Any(attribute => DrawerOf(attribute.GetType()) != null);

        // The drawer Unity finds for a type, by its own lookup
        private static readonly MethodInfo Lookup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ScriptAttributeUtility")
            ?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Name == "GetDrawerTypeForType")
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault();

        private static Type DrawerOf(Type type)
        {
            Assume.That(Lookup, Is.Not.Null, "Unity's drawer lookup is there to ask");
            bool given = false;
            var args = new List<object>();
            foreach (var parameter in Lookup.GetParameters())
            {
                if (parameter.ParameterType == typeof(Type) && !given)
                {
                    args.Add(type);
                    given = true;
                }
                else if (parameter.ParameterType == typeof(Type[])) args.Add(Type.EmptyTypes);
                else if (parameter.HasDefaultValue) args.Add(parameter.DefaultValue);
                else args.Add(parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null);
            }
            return Lookup.Invoke(null, args.ToArray()) as Type;
        }
    }
}
