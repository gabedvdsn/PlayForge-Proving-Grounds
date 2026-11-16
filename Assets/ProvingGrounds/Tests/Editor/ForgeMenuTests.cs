using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// [ForgeMenu] files a kind in its pickers (EP-2, the user's editor pass of 2026-11-15): under the submenus its path
    /// names, a slash nesting them, with a name in place of its class's and a description the picker's Context says. It
    /// replaced ForgeCategory, the ability tasks' one level of groups, and the Category of a number's kind and of a
    /// condition; and every kind PlayForge ships says what it does.
    /// </summary>
    public class ForgeMenuTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly EditorAssembly = typeof(TagDrawer).Assembly;

        [Serializable]
        [ForgeMenu("EP2/Deep", Description = "A kind two submenus down")]
        private sealed class DeepTask : AbstractAbilityTask
        {
            public override bool IsCriticalSection => false;
            public override UniTask Activate(AbilityDataPacket data, CancellationToken token) => UniTask.CompletedTask;
        }

        [Serializable]
        [ForgeMenu("EP2 Based")]
        private abstract class BasedTask : AbstractAbilityTask
        {
            public override bool IsCriticalSection => false;
            public override UniTask Activate(AbilityDataPacket data, CancellationToken token) => UniTask.CompletedTask;
        }

        [Serializable]
        [ForgeMenu(Name = "EP2 Renamed", Description = "A kind under its base's path, by a name of its own")]
        private sealed class NamedTask : BasedTask
        {
        }

        [Serializable]
        [ForgeMenu("")]
        private sealed class TopTask : BasedTask
        {
        }

        private static object Get(object target, string member) => target.GetType().GetProperty(member, Any)?.GetValue(target);

        private static IEnumerable<object> Children(object node) => ((IEnumerable)Get(node, "Children")).Cast<object>();

        private static object Menu(object node, string name) => node.GetType().GetMethod("Menu", Any).Invoke(node, new object[] { name });

        /// <summary>
        /// A kind sits under the submenus its [ForgeMenu] names, two deep where its path says; a name or a description alone
        /// keeps its base's path, "" puts it at the top whatever its base names, and a description is a kind's own. The kind
        /// picker files it so, and says its description.
        /// </summary>
        [Test]
        public void EP_2_A_kind_sits_under_the_submenus_its_ForgeMenu_names()
        {
            Assert.AreEqual("EP2/Deep", ForgeMenuAttribute.PathOf(typeof(DeepTask)), "a path two deep");
            Assert.AreEqual("EP2 Based", ForgeMenuAttribute.PathOf(typeof(NamedTask)), "a name and a description alone keep the base's path");
            Assert.AreEqual("", ForgeMenuAttribute.PathOf(typeof(TopTask)), "\"\" is the top, whatever the base names");
            Assert.AreEqual("EP2 Renamed", ForgeMenuAttribute.NameOf(typeof(NamedTask)), "a name of its own");
            Assert.IsNull(ForgeMenuAttribute.DescriptionOf(typeof(TopTask)), "a description is a kind's own, not its base's");

            var kinds = new List<(Type, string)> { (typeof(DeepTask), "Deep"), (typeof(NamedTask), "EP2 Renamed") };
            var picker = Activator.CreateInstance(EditorAssembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTypePicker", true), Any, null,
                new object[] { kinds, true, "Ability Task", null, null }, null);
            var dropdown = Get(picker, "Dropdown");
            var root = dropdown.GetType().GetMethod("Build").Invoke(dropdown, null);
            var deep = Menu(Menu(root, "EP2"), "Deep");
            Assert.IsNotNull(deep, "the picker files it two submenus down");
            var item = Get(Children(deep).Single(), "Item");
            Assert.AreEqual("A kind two submenus down", Get(item, "Context"), "and its Context says its description");
            Assert.IsNotNull(Menu(root, "EP2 Based"), "a kind with a name alone sits under its base's path");
        }

        /// <summary>A kind field names a kind by its [ForgeMenu] name, in its box and in its picker.</summary>
        [Test]
        public void EP_2_A_kind_field_names_a_kind_by_its_ForgeMenu_name()
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            try
            {
                ability.Behaviour.Stages = new List<AbilityTaskBehaviourStage> { new() { Tasks = new List<AbstractAbilityTask> { new NamedTask() } } };
                var serialized = new SerializedObject(ability);
                var task = serialized.FindProperty("Behaviour.Stages.Array.data[0].Tasks.Array.data[0]");
                Assert.IsNotNull(task, "the stage's task");
                var drawer = Activator.CreateInstance(EditorAssembly.GetType("FarEmerald.PlayForge.Extended.Editor.AbilityTaskDrawer", true));
                var kindsFor = drawer.GetType().GetMethod("KindsFor", Any);
                var kinds = (IEnumerable<(Type Type, string Name)>)kindsFor.Invoke(drawer, new object[] { task });
                Assert.That(kinds.Where(kind => kind.Type == typeof(NamedTask)).Select(kind => kind.Name), Is.EqualTo(new[] { "EP2 Renamed" }),
                    "the picker names it by its [ForgeMenu] name");
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// ForgeCategory is gone, and so is the Category of a number's kind and of a condition: each one's old category is
        /// its [ForgeMenu] path, a scaler sits under Scaler as AbstractScaler names, and a kind of number that named none at
        /// the top.
        /// </summary>
        [Test]
        public void EP_2_ForgeMenu_replaces_ForgeCategory_and_the_Category_of_numbers_and_conditions()
        {
            Assert.IsNull(typeof(Tag).Assembly.GetType("FarEmerald.PlayForge.ForgeCategoryAttribute"), "ForgeCategory is gone");
            Assert.IsNull(typeof(NumberKindAttribute).GetProperty("Category"), "a number kind names no Category");
            Assert.IsNull(typeof(GameplayConditionAttribute).GetProperty("Category"), "nor a condition");
            Assert.AreEqual("Effects", ForgeMenuAttribute.PathOf(typeof(ApplyEffectsAbilityTask)), "a task's category is its path");
            Assert.AreEqual("Asset Loaders", ForgeMenuAttribute.PathOf(typeof(SetEffectsTask)), "a base's holds for those made from it");
            Assert.AreEqual("Scaler", ForgeMenuAttribute.PathOf(typeof(ClampedScaler)), "a number kind's old category");
            Assert.AreEqual("Logic", ForgeMenuAttribute.PathOf(typeof(AllCondition)), "and a condition's");
            Assert.AreEqual("Scaler", ForgeMenuAttribute.PathOf(typeof(CachedAttributeScaler)), "a scaler under Scaler");
            Assert.AreEqual("", ForgeMenuAttribute.PathOf(typeof(SimpleScaler)), "By Level at the top");
            Assert.AreEqual("", ForgeMenuAttribute.PathOf(typeof(LocalDataKind)), "and a kind of number that named none");
        }

        /// <summary>Every kind PlayForge ships says what it does, for its picker's Context, in every family a picker offers.</summary>
        [Test]
        public void EP_2_Every_kind_PlayForge_ships_says_what_it_does()
        {
            var families = new[]
            {
                typeof(AbstractAbilityTask), typeof(AbstractTargetingAbilityTask), typeof(AbstractCueHandler), typeof(AbstractEffectComponent),
                typeof(AbstractAbilityComponent), typeof(AbstractEntityComponent), typeof(AbstractItemComponent), typeof(AbstractImpactWorker),
                typeof(AbstractAttributeWorker), typeof(AbstractEffectWorker), typeof(AbstractTagWorker), typeof(AbstractAnalysisWorker),
                typeof(AbstractScaler), typeof(GameplayCondition), typeof(AbstractValueStage), typeof(AbstractEffectChange),
                typeof(AbstractStackingConfig), typeof(IAbilityValidationRule), typeof(AbstractChargeRecharge), typeof(AbstractPurgeFilter),
                typeof(AbstractScalerBehaviour), typeof(IAbilityProxyStagePolicy),
            };
            var silent = typeof(Tag).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition && !type.IsInterface && families.Any(family => family.IsAssignableFrom(type)))
                .Where(type => ForgeMenuAttribute.DescriptionOf(type) == null)
                .Select(type => type.Name)
                .OrderBy(name => name)
                .ToList();
            Assert.IsEmpty(silent, $"kinds that don't say what they do: {string.Join(", ", silent)}");
        }
    }
}
