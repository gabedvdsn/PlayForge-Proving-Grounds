using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The review's authoring round trip (Module 4): an attribute, a set, an entity, an effect, an ability and an item, each
    /// made by The Forge's Create; every simple field of each edited as an inspector edits it, through a SerializedObject,
    /// then undone and redone; one edit across several assets at once; and a chain of templates, A → B → C, pushed. Each
    /// step's saved text is compared with what it should be: a save twice is the same text, an undo puts the file back to
    /// the byte, a redo puts the edit back, and a push changes only the lines it pushes.
    /// </summary>
    public class AuthoringRoundTripTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Folder = "Assets/PlayForge Round Trip";

        private static readonly Type[] Kinds =
        {
            typeof(ForgeAttribute), typeof(AttributeSet), typeof(EntityIdentity), typeof(GameplayEffect), typeof(Ability), typeof(Item),
        };

        private static IEnumerable<Type> EachKind() => Kinds;

        [SetUp]
        public void Begin()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "PlayForge Round Trip");
            Undo.ClearAll();
        }

        [TearDown]
        public void End()
        {
            Undo.ClearAll();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>An asset The Forge makes saves as the same text when it's written again with nothing changed.</summary>
        [Test]
        public void An_asset_The_Forge_makes_saves_the_same_text_again([ValueSource(nameof(EachKind))] Type kind)
        {
            var asset = Create(kind, $"Round {kind.Name}");
            string first = Text(asset);

            AssetDatabase.ForceReserializeAssets(new[] { AssetDatabase.GetAssetPath(asset) });
            Assert.AreEqual(first, Text(asset), "written again, its text is the same");
        }

        /// <summary>
        /// Every simple field of an asset The Forge makes, edited as an inspector edits it: undone, the file is as it was,
        /// to the byte, and redone, as the edit left it.
        /// </summary>
        [Test]
        public void Each_field_edited_undoes_to_the_byte_and_redoes([ValueSource(nameof(EachKind))] Type kind)
        {
            Sweep(Create(kind, $"Edited {kind.Name}"), kind.Name);
        }

        /// <summary>
        /// The same with what a new asset leaves empty filled in: an ability with a stage of two tasks, and an effect with a
        /// worker, each held as a reference to its own type; and a set of two attributes. Each of their fields, edited,
        /// undoes to the byte and redoes.
        /// </summary>
        [Test]
        public void Each_field_of_a_filled_asset_undoes_to_the_byte_and_redoes([Values("Ability", "Effect", "Set")] string kind)
        {
            ScriptableObject asset;
            switch (kind)
            {
                case "Ability":
                    var ability = (Ability)Create(typeof(Ability), "Filled Ability");
                    ability.Behaviour.Stages.Add(new AbilityTaskBehaviourStage
                    {
                        Tasks = new List<AbstractAbilityTask> { new DelayTask { DelayMilliseconds = 100 }, new ChannelAbilityTask { ChannelDuration = 1.5f } },
                    });
                    asset = ability;
                    break;
                case "Effect":
                    var effect = (GameplayEffect)Create(typeof(GameplayEffect), "Filled Effect");
                    effect.Workers ??= new List<AbstractEffectWorker>();
                    effect.Workers.Add(new ThresholdDebuggerEffectWorker { TargetAttribute = (ForgeAttribute)Create(typeof(ForgeAttribute), "Watched") });
                    asset = effect;
                    break;
                default:
                    var set = (AttributeSet)Create(typeof(AttributeSet), "Filled Set");
                    foreach (string name in new[] { "Health", "Mana" })
                    {
                        set.Attributes.Add(new AttributeSetElement
                        {
                            Attribute = (ForgeAttribute)Create(typeof(ForgeAttribute), name),
                            Current = new AttributeMagnitudeSpec { Magnitude = 50f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                            Base = new AttributeMagnitudeSpec { Magnitude = 100f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                            Overflow = new AttributeOverflowData { Policy = EAttributeOverflowPolicy.ZeroToBase },
                            RetentionGroup = Tags.DEFAULT,
                            Constraints = new AttributeConstraints { AutoClamp = true, RoundingMode = EAttributeRoundingPolicy.None },
                        });
                    }
                    asset = set;
                    break;
            }
            Save(asset);
            Sweep(asset, kind);
        }

        // Every simple field of the asset, edited, undone, redone and undone again, each saved and compared
        private static void Sweep(ScriptableObject asset, string kind)
        {
            var wrong = new List<string>();
            int edited = 0;
            foreach (string path in SimpleFields(asset))
            {
                string before = Text(asset);
                Undo.IncrementCurrentGroup();
                if (!EditOnce(asset, path)) continue;
                string after = Save(asset);
                edited++;

                Undo.PerformUndo();
                string undone = Save(asset);
                if (undone != before) wrong.Add($"{path}, undone: {FirstDifference(before, undone)}");

                Undo.PerformRedo();
                string redone = Save(asset);
                if (redone != after) wrong.Add($"{path}, redone: {FirstDifference(after, redone)}");

                Undo.PerformUndo();
                Save(asset);
            }

            TestContext.WriteLine($"{kind}: {edited} fields edited, undone and redone");
            Assert.That(edited, Is.GreaterThan(0), "it has fields to edit");
            Assert.IsEmpty(wrong, $"{wrong.Count} of {edited} fields didn't come back:\n  {string.Join("\n  ", wrong)}");
        }

        /// <summary>An edit across three abilities at once reaches each of them, and one undo takes it back from each.</summary>
        [Test]
        public void An_edit_across_several_assets_reaches_each_and_one_undo_takes_it_back()
        {
            var abilities = Enumerable.Range(0, 3).Select(i => (Ability)Create(typeof(Ability), $"Together {i}")).ToArray();
            var before = abilities.Select(Text).ToArray();

            Undo.IncrementCurrentGroup();
            var serialized = new SerializedObject(abilities.Cast<Object>().ToArray());
            serialized.FindProperty(nameof(Ability.MaxLevel)).intValue = 7;
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Assert.That(abilities.All(ability => ability.MaxLevel == 7), "the edit reaches each");

            var after = abilities.Select(ability => Save(ability)).ToArray();
            for (int i = 0; i < abilities.Length; i++)
                Assert.That(ChangedFields(before[i], after[i]), Is.EqualTo(new[] { nameof(Ability.MaxLevel) }), $"and changes only its Max Level in {abilities[i].name}");

            Undo.PerformUndo();
            for (int i = 0; i < abilities.Length; i++)
                Assert.AreEqual(before[i], Save(abilities[i]), $"one undo puts {abilities[i].name} back to the byte");
        }

        /// <summary>
        /// A chain of templates, A → B → C, where B makes its Starting Level its own: an edit to A's Max Level, pushed,
        /// changes B's Max Level alone, and C's Max Level and the Starting Level it takes from B, and nothing else of
        /// either file. One undo takes the push back from both.
        /// </summary>
        [Test]
        public void A_push_down_a_chain_of_templates_changes_only_what_it_pushes()
        {
            var a = (Ability)Create(typeof(Ability), "Chain A");
            var b = (Ability)Create(typeof(Ability), "Chain B");
            var c = (Ability)Create(typeof(Ability), "Chain C");
            Assert.IsTrue(b.SetTemplate(a) && c.SetTemplate(b), "B takes A as its template, and C takes B");
            EditorUtility.SetDirty(b);
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();

            Undo.IncrementCurrentGroup();
            EditInt(b, nameof(Ability.StartingLevel), 2);
            Assert.IsTrue(b.IsFieldOverridden(nameof(Ability.StartingLevel)), "B's Starting Level is its own");
            Undo.IncrementCurrentGroup();
            EditInt(a, nameof(Ability.MaxLevel), 9);
            AssetDatabase.SaveAssets();
            string bBefore = Text(b), cBefore = Text(c);

            Undo.IncrementCurrentGroup();
            var pushed = Push(a);
            Assert.That(pushed, Is.EquivalentTo(new BaseForgeAsset[] { b, c }), "the push reaches B and C");
            Assert.AreEqual((9, 2), (b.MaxLevel, b.StartingLevel), "B takes A's Max Level and keeps its Starting Level");
            Assert.AreEqual((9, 2), (c.MaxLevel, c.StartingLevel), "C takes B's");

            AssetDatabase.SaveAssets();
            Assert.That(ChangedFields(bBefore, Text(b)), Is.EqualTo(new[] { nameof(Ability.MaxLevel) }), "B's file changes in its Max Level alone");
            Assert.That(ChangedFields(cBefore, Text(c)), Is.EquivalentTo(new[] { nameof(Ability.MaxLevel), nameof(Ability.StartingLevel) }),
                "C's in its Max Level and Starting Level alone");

            Undo.PerformUndo();
            AssetDatabase.SaveAssets();
            Assert.AreEqual(bBefore, Text(b), "one undo puts B back to the byte");
            Assert.AreEqual(cBefore, Text(c), "and C");
        }

        // ═══════════════════════════════════════════════════════════════
        // Making, editing and reading
        // ═══════════════════════════════════════════════════════════════

        // An asset as The Forge's Create makes it, in the round trip's folder, saved
        private static ScriptableObject Create(Type kind, string name)
        {
            var creation = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeAssetCreation");
            Assert.IsNotNull(creation, "The Forge's Create");
            var asset = (ScriptableObject)creation.GetMethod("CreateNew", AnyStatic).Invoke(null, new object[] { kind, name, Folder });
            Assert.IsNotNull(asset, $"The Forge makes a {kind.Name}");
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static string Text(ScriptableObject asset) => File.ReadAllText(AssetDatabase.GetAssetPath(asset));

        private static string Save(ScriptableObject asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return Text(asset);
        }

        // The paths of every visible field of a simple kind: a number, a flag, a word or a choice
        private static List<string> SimpleFields(Object asset)
        {
            var paths = new List<string>();
            using var serialized = new SerializedObject(asset);
            var property = serialized.GetIterator();
            for (bool more = property.NextVisible(true); more; more = property.NextVisible(true))
            {
                if (property.propertyPath == "m_Script" || property.propertyPath.EndsWith(".Array.size")) continue;
                if (property.propertyType is SerializedPropertyType.Integer or SerializedPropertyType.Boolean or SerializedPropertyType.Float
                    or SerializedPropertyType.String or SerializedPropertyType.Enum)
                    paths.Add(property.propertyPath);
            }
            return paths;
        }

        // An edit to one field, as an inspector makes it, and the editor's flush of it at the end of the frame. False when
        // the field can't take another value
        private static bool EditOnce(Object asset, string path)
        {
            using var serialized = new SerializedObject(asset);
            var property = serialized.FindProperty(path);
            if (property == null || !property.editable) return false;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.longValue += 1;
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = !property.boolValue;
                    break;
                case SerializedPropertyType.Float:
                    property.doubleValue += 0.5;
                    break;
                case SerializedPropertyType.String:
                    property.stringValue += "x";
                    break;
                case SerializedPropertyType.Enum:
                    if (property.enumNames.Length < 2) return false;
                    property.enumValueIndex = (property.enumValueIndex + 1) % property.enumNames.Length;
                    break;
                default:
                    return false;
            }
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            return true;
        }

        private static void EditInt(Object asset, string path, int value)
        {
            using var serialized = new SerializedObject(asset);
            serialized.FindProperty(path).intValue = value;
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
        }

        private static List<BaseForgeAsset> Push(BaseForgeAsset template)
        {
            var push = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTemplatePush");
            return (List<BaseForgeAsset>)push.GetMethod("PushToChildren", AnyStatic).Invoke(null, new object[] { template });
        }

        // The fields whose lines differ between two saves of one asset, by the key before each line's colon
        private static string[] ChangedFields(string before, string after)
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            if (a.Length != b.Length) return new[] { $"(lines {a.Length} → {b.Length})" };
            return a.Zip(b, (x, y) => (x, y)).Where(pair => pair.x != pair.y)
                .Select(pair => pair.x.Split(':')[0].Trim()).Distinct().ToArray();
        }

        // Where two texts first differ, as a line of each
        private static string FirstDifference(string expected, string actual)
        {
            var a = expected.Split('\n');
            var b = actual.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i].TrimEnd('\r') : "(none)", y = i < b.Length ? b[i].TrimEnd('\r') : "(none)";
                if (x != y) return $"line {i + 1} was \"{x.Trim()}\", is \"{y.Trim()}\"";
            }
            return "(the same)";
        }
    }
}
