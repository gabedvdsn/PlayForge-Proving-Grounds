using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The ability tasks pass (FN-24, Decision 83): each of PlayForge's tasks names its category, its [ForgeMenu] path since
    /// EP-2, and a kind's picker lists its kinds in a submenu for each, those naming none after them; and every number a task
    /// has is Scalable.
    /// </summary>
    public class TaskPickerTests
    {
        private static readonly Assembly Runtime = typeof(AbstractAbilityRelatedTask).Assembly;
        private static readonly Assembly EditorAssembly = typeof(TaskSequenceReferenceDrawer).Assembly;

        // The submenu a kind's picker files it under, its [ForgeMenu]'s or a class it's made from's (EP-2), or null for none
        private static string CategoryOf(Type type) => ForgeMenuAttribute.PathOf(type) is { Length: > 0 } path ? path : null;

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

        // A node of PlayForge's dropdown, read by reflection, as the dropdown is the editor's own (EP-1): its children, its
        // name, a line's as "—", and every name under it, its submenus' among them
        private static IEnumerable<object> Children(object node) =>
            ((System.Collections.IEnumerable)node.GetType().GetProperty("Children").GetValue(node)).Cast<object>();

        private static string NameOf(object node) =>
            (bool)node.GetType().GetProperty("IsSeparator").GetValue(node) ? "—" : (string)node.GetType().GetProperty("Name").GetValue(node);

        private static IEnumerable<string> Names(object node) => Children(node).SelectMany(child => new[] { NameOf(child) }.Concat(Names(child)));

        [Test]
        public void FN_24_A_kinds_picker_lists_each_category_as_a_submenu()
        {
            var picker = EditorAssembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTypePicker");
            Assert.IsNotNull(picker, "the kind picker");
            var kinds = new List<(Type, string)> { (typeof(ApplyEffectsAbilityTask), "Apply Effects"), (typeof(DelayTask), "Delay"), (typeof(Loose), "Loose") };
            var made = Activator.CreateInstance(picker, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { kinds, true, "Ability Task", null, null }, null);
            // PlayForge's dropdown, which the picker shows (EP-1)
            var dropdown = picker.GetProperty("Dropdown")?.GetValue(made);
            Assert.IsNotNull(dropdown, "the picker shows PlayForge's dropdown");
            var root = dropdown.GetType().GetMethod("Build").Invoke(dropdown, null);
            var children = Children(root).ToList();

            Assert.AreEqual("None", NameOf(children[0]), "None first");
            var effects = children.FirstOrDefault(child => NameOf(child) == "Effects");
            Assert.IsNotNull(effects, $"a submenu for Effects: {string.Join(", ", children.Select(NameOf))}");
            Assert.That(Children(effects).Select(NameOf), Is.EquivalentTo(new[] { "Apply Effects" }), "holding its kinds");
            Assert.That(Children(children.First(child => NameOf(child) == "Waits")).Select(NameOf),
                Is.EquivalentTo(new[] { "Delay" }), "and one for Waits");
            Assert.AreEqual("Loose", NameOf(children.Last()), "a kind naming none after the submenus");
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
