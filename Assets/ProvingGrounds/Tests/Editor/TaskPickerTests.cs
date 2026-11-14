using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor.IMGUI.Controls;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The ability tasks pass (FN-24, Decision 83): each of PlayForge's tasks names its category, and a kind's picker lists
    /// its kinds in a submenu for each, those naming none after them; and every number a task has is Scalable.
    /// </summary>
    public class TaskPickerTests
    {
        private static readonly Assembly Runtime = typeof(AbstractAbilityRelatedTask).Assembly;
        private static readonly Assembly EditorAssembly = typeof(TaskSequenceReferenceDrawer).Assembly;

        // The category a kind names, read by the attribute's name, so the test reads as it should where there's none
        private static string CategoryOf(Type type)
        {
            var attribute = Runtime.GetType("FarEmerald.PlayForge.ForgeCategoryAttribute");
            Assert.IsNotNull(attribute, "a kind can name its category");
            var found = System.Attribute.GetCustomAttribute(type, attribute, true);
            return found?.GetType().GetProperty("Name")?.GetValue(found) as string;
        }

        // A task of no category, as a project's own may be
        private sealed class Loose : AbstractAbilityTask
        {
            public override bool IsCriticalSection => false;
            public override UniTask Activate(AbilityDataPacket data, CancellationToken token) => UniTask.CompletedTask;
        }

        [Test]
        public void FN_24_Each_of_PlayForges_tasks_names_its_category()
        {
            var tasks = Runtime.GetTypes()
                .Where(type => !type.IsAbstract && typeof(AbstractAbilityRelatedTask).IsAssignableFrom(type) && type != typeof(CompositeTargetingTask))
                .ToList();
            Assert.That(tasks, Is.Not.Empty, "there are tasks");
            foreach (var task in tasks) Assert.IsNotNull(CategoryOf(task), $"{task.Name} names its category");
            Assert.AreEqual("Effects", CategoryOf(typeof(ApplyEffectsAbilityTask)));
            Assert.AreEqual("Asset Loaders", CategoryOf(typeof(SetEffectsTask)), "a class's category holds for those made from it");
            Assert.AreEqual("Process", CategoryOf(typeof(CreateMonoProcessAbilityTask)));
            Assert.AreEqual("Sequence", CategoryOf(typeof(RunSequenceTask)));
            Assert.AreEqual("Waits", CategoryOf(typeof(DelayTask)));
            Assert.AreEqual("Picked", CategoryOf(typeof(SelectGasTargetTask)), "a targeting task's too");
        }

        // Every name under an item, its submenus' among them
        private static IEnumerable<string> Names(AdvancedDropdownItem item) =>
            item.children.SelectMany(child => new[] { child.name }.Concat(Names(child)));

        [Test]
        public void FN_24_A_kinds_picker_lists_each_category_as_a_submenu()
        {
            var picker = EditorAssembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTypePicker");
            Assert.IsNotNull(picker, "the kind picker");
            var kinds = new List<(Type, string)> { (typeof(ApplyEffectsAbilityTask), "Apply Effects"), (typeof(DelayTask), "Delay"), (typeof(Loose), "Loose") };
            var dropdown = Activator.CreateInstance(picker, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { kinds, true, "Ability Task", null }, null);
            var root = (AdvancedDropdownItem)picker.GetMethod("BuildRoot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dropdown, null);
            var children = root.children.ToList();

            Assert.AreEqual("None", children[0].name, "None first");
            var effects = children.FirstOrDefault(child => child.name == "Effects");
            Assert.IsNotNull(effects, $"a submenu for Effects: {string.Join(", ", children.Select(child => child.name))}");
            Assert.That(effects.children.Select(child => child.name), Is.EquivalentTo(new[] { "Apply Effects" }), "holding its kinds");
            Assert.That(children.FirstOrDefault(child => child.name == "Waits")?.children.Select(child => child.name),
                Is.EquivalentTo(new[] { "Delay" }), "and one for Waits");
            Assert.AreEqual("Loose", children.Last().name, "a kind naming none after the submenus");
            Assert.That(Names(root), Has.Member("Delay"), "each kind found under its category");
        }

        [Test]
        public void FN_24_Every_number_a_task_has_is_Scalable()
        {
            var tasks = Runtime.GetTypes()
                .Where(type => !type.IsAbstract && typeof(AbstractAbilityTask).IsAssignableFrom(type))
                .ToList();
            foreach (var task in tasks)
                foreach (var field in task.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    Assert.That(field.FieldType != typeof(float) && field.FieldType != typeof(int), $"{task.Name}.{field.Name} is a Scalable number");
        }
    }
}
