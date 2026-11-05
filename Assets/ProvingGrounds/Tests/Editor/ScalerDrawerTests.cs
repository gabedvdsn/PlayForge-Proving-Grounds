using System.Collections;
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
    /// The scaler drawer, built inside the inspector of the asset that holds the scaler. The inspector is added whole to a
    /// runtime panel, since batch mode has no graphics device to give an editor window one, so its bindings run there.
    /// </summary>
    public class ScalerDrawerTests
    {
        // A runtime panel to build an inspector in, and the inspector itself
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("ED-34") { hideFlags = HideFlags.HideAndDontSave };
            private UnityEditor.Editor editor;

            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Inspect(Object target)
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                Document.rootVisualElement.Add(root);
                // The Inspector binds what an editor builds to its SerializedObject
                root.Bind(editor.serializedObject);
                return root;
            }

            public void Destroy()
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        // Lets the panel run its bindings and scheduled rebuilds, which the editor's updates drive
        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        // Presses every scaler's Expand button, as a user opens them, and says how many it pressed
        private static int ExpandScalers(VisualElement root)
        {
            var buttons = root.Query<Button>().ToList().Where(button => button.tooltip == "Expand").ToList();
            foreach (var button in buttons)
            {
                using var submit = NavigationSubmitEvent.GetPooled();
                submit.target = button;
                button.SendEvent(submit);
            }
            return buttons.Count;
        }

        /// <summary>
        /// Opening a scaler wrote to it while drawing. On an effect with no level link, Lock mode cut its per-level table
        /// to one value; the other modes resized the table to Max Level, and a Max Level of 0 or less became 10.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_34_Opening_a_scaler_leaves_its_table_alone()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification ??= new GameplayEffectDuration();
            effect.DurationSpecification.DurationOperation ??= new ScalerMagnitudeOperation();
            effect.DurationSpecification.DurationPolicy = EEffectDurationPolicy.Durational;
            effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler
            {
                Configuration = ELevelConfig.LockToLevelProvider, MaxLevel = 5, LevelValues = new[] { 1f, 2f, 3f, 4f, 5f },
            };
            effect.DurationSpecification.DeltaTimeScaler = new SimpleScaler
            {
                Configuration = ELevelConfig.Unlocked, MaxLevel = 0, LevelValues = new[] { 2f, 4f, 6f },
            };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(effect);
                yield return Frames();

                // A scaler's table is open as the field draws, with no arrow to press (ED-33)
                Assume.That(root.Query(name: "LevelTable").ToList().Count, Is.GreaterThanOrEqualTo(2), "both scalers' tables are drawn");

                var locked = effect.DurationSpecification.DurationOperation.Scaler;
                Assert.AreEqual(new[] { 1f, 2f, 3f, 4f, 5f }, locked.LevelValues, "Lock mode with no level link keeps its table");
                Assert.AreEqual(5, locked.MaxLevel, "and its Max Level");
                var unlocked = effect.DurationSpecification.DeltaTimeScaler;
                Assert.AreEqual(new[] { 2f, 4f, 6f }, unlocked.LevelValues, "an unlocked table keeps its own length");
                Assert.AreEqual(0, unlocked.MaxLevel, "and a Max Level of 0 stays 0");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// Building the drawer cloned a scaler that shared its reference id with another list element's, so drawing an
        /// attribute set changed it. The drawer now leaves the shared scaler alone and says so in place. The test draws
        /// the second element's scaler on its own, since a list draws its elements only as a laid-out view needs them.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_34_Opening_a_shared_scaler_leaves_it_shared()
        {
            var set = ScriptableObject.CreateInstance<AttributeSet>();
            var shared = new CachedSimpleScaler();
            set.Attributes.Add(new AttributeSetElement { Current = new AttributeMagnitudeSpec { Scaling = shared } });
            set.Attributes.Add(new AttributeSetElement { Current = new AttributeMagnitudeSpec { Scaling = shared } });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(set);
                var scaling = serialized.FindProperty("Attributes.Array.data[1].Current.Scaling");
                Assume.That(scaling, Is.Not.Null, "the second element's scaler");
                Assume.That(scaling.managedReferenceId,
                    Is.EqualTo(serialized.FindProperty("Attributes.Array.data[0].Current.Scaling").managedReferenceId),
                    "shares the first one's reference id");
                var root = new PropertyField(scaling);
                host.Document.rootVisualElement.Add(root);
                root.Bind(serialized);
                yield return Frames();
                for (int round = 0; round < 3; round++)
                {
                    ExpandScalers(root);
                    yield return Frames();
                }

                Assert.AreSame(set.Attributes[0].Current.Scaling, set.Attributes[1].Current.Scaling,
                    "the two elements still share one scaler");
                var notes = root.Query<HelpBox>().ToList().Where(box => box.text.Contains("same scaler")).ToList();
                Assert.IsNotEmpty(notes, "and the drawer says so");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(set);
            }
        }

        /// <summary>
        /// Every scaler spread its values over a level range (Decision 28). A scaler the drawer makes maps levels exactly,
        /// and its Stretch Over Levels shows it, which a scaler saved before the option keeps on (ED-33).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_A_new_scaler_maps_levels_exactly()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification = new GameplayEffectDuration { DurationOperation = new ScalerMagnitudeOperation() };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(effect);
                var field = new PropertyField(serialized.FindProperty("DurationSpecification.DurationOperation.Scaler"));
                host.Document.rootVisualElement.Add(field);
                field.Bind(serialized);
                yield return Frames();

                var kind = field.Query<PopupField<string>>().ToList().FirstOrDefault();
                Assert.IsNotNull(kind, "the drawer offers a kind");
                kind.value = "By Level";
                yield return Frames();
                var made = effect.DurationSpecification.DurationOperation.Scaler as SimpleScaler;
                Assert.IsNotNull(made, "choosing By Level makes a simple scaler");
                Assert.IsFalse(made.StretchOverLevels, "which maps levels exactly");

                ExpandScalers(field);
                yield return Frames();
                Assert.IsNotNull(field.Query<Toggle>().ToList().FirstOrDefault(toggle => toggle.label == "Stretch Over Levels"),
                    "and its Stretch Over Levels says so");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens, as the smoke
        // suite's windows do. Every other error logged while the window is open is its own
        private sealed class WindowLog : System.IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public System.Collections.Generic.List<string> Errors { get; } = new();

            public WindowLog()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += Take;
            }

            private void Take(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(NoDevice))
                    Errors.Add($"{type}: {message}");
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Take;
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // An effect whose Duration reads a table of five levels, 6 to 8 s, and whose Tick Every is a constant 1 s
        private static GameplayEffect Burning()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.name = "Burning";
            effect.Definition.Name = "Burning";
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Durational,
                DurationOperation = new ScalerMagnitudeOperation
                {
                    RealMagnitude = EMagnitudeOperation.UseScaler,
                    Scaler = new SimpleScaler { LevelValues = new[] { 6f, 6.5f, 7f, 7.5f, 8f }, StretchOverLevels = false },
                },
                TickIntervalOperation = new ScalerMagnitudeOperation { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
            };
            return effect;
        }

        private const string DurationPath = "DurationSpecification.DurationOperation";

        private static PropertyField Draw(Host host, SerializedObject serialized, string path)
        {
            var field = new PropertyField(serialized.FindProperty(path));
            host.Document.rootVisualElement.Add(field);
            field.Bind(serialized);
            return field;
        }

        // The looks a drawer sets in code, outside the drawers nested in it
        private static System.Collections.Generic.List<string> OwnLook(PropertyField field) => field.Query<VisualElement>().ToList()
            .Where(element => element.GetFirstAncestorOfType<PropertyField>() == field)
            .SelectMany(element => ForgeInspectorTests.LookSetInCode(element).Select(style => $"{element.GetType().Name} \"{element.name}\" sets {style}"))
            .ToList();

        /// <summary>
        /// A magnitude field showed its Magnitude, Real Magnitude and scaler one after another, with the scaler in a tinted
        /// box (ED-33). It reads as the scaler board draws it: its name and kind, By Level, with the range it gives, "6–8 s",
        /// and its field menu, then its values per level; a Constant shows its number and unit, as Tick Every's "1 s".
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_A_magnitude_field_reads_as_the_scaler_board()
        {
            var effect = Burning();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(effect);
                var duration = Draw(host, serialized, DurationPath);
                yield return Frames();

                Assert.AreEqual("By Level", duration.Q<PopupField<string>>("Kind")?.value, "the field's kind");
                Assert.AreEqual("6–8 s", duration.Q<Label>("Range")?.text, "the range it gives, in seconds");
                Assert.That(Enumerable.Range(1, 5).Select(level => duration.Q<FloatField>($"Level{level}")?.value),
                    Is.EqualTo(new float?[] { 6f, 6.5f, 7f, 7.5f, 8f }), "a value for each level");
                Assert.IsNotNull(duration.Q<Button>("FieldMenu"), "and its field menu");
                Assert.That(OwnLook(duration), Is.Empty, "the field sets no look in code");

                var tick = Draw(host, serialized, "DurationSpecification.TickIntervalOperation");
                yield return Frames();
                Assert.AreEqual("Constant", tick.Q<PopupField<string>>("Kind")?.value, "Tick Every is a constant");
                Assert.AreEqual(1f, tick.Q<FloatField>("Number")?.value, "that shows its number");
                Assert.AreEqual("s", tick.Q<Label>("Unit")?.text, "and its unit");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// A field's kind changes as one Undo step: a constant made By Level keeps its number as its first level's value,
        /// and Make Constant keeps the value the first level read (ED-33).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_A_fields_kind_changes_as_one_undo_step()
        {
            var effect = Burning();
            effect.DurationSpecification.TickIntervalOperation.Magnitude = 2f;
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(effect);
                var tick = Draw(host, serialized, "DurationSpecification.TickIntervalOperation");
                yield return Frames();

                Undo.IncrementCurrentGroup();
                tick.Q<PopupField<string>>("Kind").value = "By Level";
                yield return Frames();
                var interval = effect.DurationSpecification.TickIntervalOperation;
                Assert.IsInstanceOf<SimpleScaler>(interval.Scaler, "By Level gives the field a table");
                Assert.AreEqual(new[] { 2f }, interval.Scaler.LevelValues, "whose first level keeps the field's number");
                Assert.AreEqual(EMagnitudeOperation.UseScaler, interval.RealMagnitude, "and the field reads it");
                Assert.IsFalse(interval.Scaler.StretchOverLevels, "levels map exactly");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                yield return Frames();
                Assert.IsNull(effect.DurationSpecification.TickIntervalOperation.Scaler, "one Undo makes it a constant again");

                Undo.IncrementCurrentGroup();
                ScalerActions.MakeConstant(serialized.FindProperty(DurationPath));
                var duration = effect.DurationSpecification.DurationOperation;
                Assert.IsNull(duration.Scaler, "Make Constant takes the scaler away");
                Assert.AreEqual(6f, duration.Magnitude, "and keeps the value level 1 read");
                Assert.AreEqual(EMagnitudeOperation.UseMagnitude, duration.RealMagnitude, "which the field reads now");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsInstanceOf<SimpleScaler>(effect.DurationSpecification.DurationOperation.Scaler, "and one Undo brings the table back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// A magnitude field drawn for two selected effects read the first effect's kind, and its kind menu gave both a
        /// new scaler that started with the first effect's value (ED-12). Fields whose kinds differ show a mixed kind;
        /// choosing one gives it to each effect's field, from that field's own value, and a field of that kind keeps its
        /// scaler. Levels and Make Constant work on each field's own values, each as one Undo step.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_A_magnitude_field_edits_every_selected_asset()
        {
            var burning = Burning();
            var chilled = Burning();
            chilled.name = "Chilled";
            chilled.Definition.Name = "Chilled";
            chilled.DurationSpecification.DurationOperation = new ScalerMagnitudeOperation
            {
                Magnitude = 3f,
                RealMagnitude = EMagnitudeOperation.UseMagnitude,
            };
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(new Object[] { burning, chilled });
                var field = Draw(host, serialized, DurationPath);
                yield return Frames();

                Assert.IsTrue(field.Q<PopupField<string>>("Kind").showMixedValue, "kinds that differ show as mixed");
                Assert.IsNull(field.Q("LevelTable"), "and neither field's table is drawn as both's");

                Undo.IncrementCurrentGroup();
                field.Q<PopupField<string>>("Kind").value = "By Level";
                yield return Frames();
                var table = burning.DurationSpecification.DurationOperation.Scaler;
                var made = chilled.DurationSpecification.DurationOperation.Scaler;
                Assert.AreEqual(new[] { 6f, 6.5f, 7f, 7.5f, 8f }, table.LevelValues, "a field of that kind keeps its scaler");
                Assert.IsInstanceOf<SimpleScaler>(made, "the other takes the kind");
                Assert.AreEqual(new[] { 3f }, made.LevelValues, "from its own value");
                Assert.AreEqual(EMagnitudeOperation.UseScaler, chilled.DurationSpecification.DurationOperation.RealMagnitude);

                var count = field.Q<IntegerField>("LevelCount");
                Assert.IsTrue(count.showMixedValue, "tables of different lengths show their count as mixed");
                Assert.AreEqual("—", field.Q<Label>("Range").text, "and ranges that differ show as mixed");
                Undo.IncrementCurrentGroup();
                count.value = 2;
                Assert.AreEqual(new[] { 6f, 6.5f }, burning.DurationSpecification.DurationOperation.Scaler.LevelValues,
                    "two levels cut the longer table");
                Assert.AreEqual(new[] { 3f, 3f }, chilled.DurationSpecification.DurationOperation.Scaler.LevelValues,
                    "and the shorter one's new level takes its own last value");

                Undo.IncrementCurrentGroup();
                ScalerActions.MakeConstant(serialized.FindProperty(DurationPath));
                Assert.IsNull(burning.DurationSpecification.DurationOperation.Scaler, "Make Constant takes each scaler away");
                Assert.AreEqual(6f, burning.DurationSpecification.DurationOperation.Magnitude, "and keeps the value each read at level 1");
                Assert.AreEqual(3f, chilled.DurationSpecification.DurationOperation.Magnitude);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(new[] { 6f, 6.5f }, burning.DurationSpecification.DurationOperation.Scaler.LevelValues,
                    "one Undo takes it back from both");
                Assert.AreEqual(new[] { 3f, 3f }, chilled.DurationSpecification.DurationOperation.Scaler.LevelValues);
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(burning);
                Object.DestroyImmediate(chilled);
            }
        }

        /// <summary>
        /// A scaler drawn on its own, as an attribute set element's Scaling, read the first selected set's kind (ED-12).
        /// Kinds that differ show as mixed, and choosing one gives each set's element its own scaler of that kind, as one
        /// Undo step; an element of that kind keeps its scaler.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_12_A_scaler_drawn_on_its_own_edits_every_selected_asset()
        {
            var health = ScriptableObject.CreateInstance<Attribute>();
            var goblin = ScriptableObject.CreateInstance<AttributeSet>();
            goblin.Attributes.Add(new AttributeSetElement { Attribute = health });
            goblin.Attributes[0].Current.Scaling = new CachedSimpleScaler { LevelValues = new[] { 10f, 20f }, StretchOverLevels = false };
            var ogre = ScriptableObject.CreateInstance<AttributeSet>();
            ogre.Attributes.Add(new AttributeSetElement { Attribute = health });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(new Object[] { goblin, ogre });
                var field = Draw(host, serialized, "Attributes.Array.data[0].Current.Scaling");
                yield return Frames();

                var kind = field.Q<PopupField<string>>("Kind");
                Assert.IsTrue(kind.showMixedValue, "kinds that differ show as mixed");
                Undo.IncrementCurrentGroup();
                kind.value = "By Level";
                yield return Frames();
                Assert.AreEqual(new[] { 10f, 20f }, goblin.Attributes[0].Current.Scaling.LevelValues, "an element of that kind keeps its scaler");
                Assert.IsInstanceOf<CachedSimpleScaler>(ogre.Attributes[0].Current.Scaling, "the other takes the kind");
                Assert.AreNotSame(goblin.Attributes[0].Current.Scaling, ogre.Attributes[0].Current.Scaling);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsNull(ogre.Attributes[0].Current.Scaling, "one Undo takes it back");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(goblin);
                Object.DestroyImmediate(ogre);
                Object.DestroyImmediate(health);
            }
        }

        /// <summary>
        /// Save as Scaler Asset saves a field's scaler as a Scaler asset and makes the field share it; Use a Scaler Asset
        /// makes another field share it too. The asset's inspector counts the fields that use it, and a shared field says
        /// how many fields share it (ED-33).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_Save_as_Scaler_Asset_shares_the_fields_values()
        {
            const string folder = "Assets/ED-33 Scalers";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ED-33 Scalers");
            var host = new Host();
            try
            {
                var burning = Burning();
                AssetDatabase.CreateAsset(burning, $"{folder}/Burning.asset");
                var ignite = Burning();
                ignite.Definition.Name = "Ignite";
                AssetDatabase.CreateAsset(ignite, $"{folder}/Ignite.asset");

                var saved = ScalerActions.SaveAsScalerAsset(new SerializedObject(burning).FindProperty(DurationPath), "Fire Duration", folder);
                Assert.IsNotNull(saved, "the field's scaler is saved");
                Assert.AreEqual($"{folder}/FireDuration.asset", AssetDatabase.GetAssetPath(saved), "in the folder, under its name");
                Assert.AreEqual(new[] { 6f, 6.5f, 7f, 7.5f, 8f }, saved.Scaler.LevelValues, "with the field's values");
                var shared = burning.DurationSpecification.DurationOperation.Scaler as SharedScaler;
                Assert.IsNotNull(shared, "and the field shares it");
                Assert.AreSame(saved, shared.Asset);
                Assert.AreEqual(8f, AbilityInspector.EvaluateAtLevel(burning.DurationSpecification.DurationOperation, 5, 1, 5, out _),
                    "reading its values");

                ScalerActions.UseScalerAsset(new SerializedObject(ignite).FindProperty(DurationPath), saved);
                AssetDatabase.SaveAssets();
                Assert.AreSame(saved, (ignite.DurationSpecification.DurationOperation.Scaler as SharedScaler)?.Asset, "another field shares it too");

                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(saved);
                yield return Frames();
                Assert.AreEqual("Scaler · By Level · 2 uses", root.Q<Label>("AssetAbout")?.text, "the asset counts the fields that use it");
                host.Document.rootVisualElement.Clear();
                var field = Draw(host, new SerializedObject(burning), DurationPath);
                yield return Frames();
                Assert.AreEqual("Shared by 2 fields", field.Q<Label>("SharedUses")?.text, "and a shared field says so");
            }
            finally
            {
                host.Destroy();
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// The scaler import window ignored the kinds a field allows and kept its list for 120 s (ED-32). Import Values
        /// compares a Scaler asset's values with the field's, level by level, says which levels a shorter table's last
        /// value holds for, and copies them in as one Undo step (ED-33).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_Import_Values_copies_a_scalers_values()
        {
            var effect = Burning();
            var source = ScriptableObject.CreateInstance<ScalerAsset>();
            source.Scaler = new SimpleScaler { LevelValues = new[] { 4f, 5f, 5.5f }, StretchOverLevels = false };
            var log = new WindowLog();
            ScalerValuesWindow window = null;
            try
            {
                var serialized = new SerializedObject(effect);
                window = ScalerValuesWindow.Show(serialized.FindProperty($"{DurationPath}.Scaler"), "Burning Duration");
                yield return null;
                window.Choose(source);
                yield return null;
                Assert.AreEqual("Levels 4–5 take level 3's value.", window.rootVisualElement.Q<Label>("Holds")?.text,
                    "a shorter table's last value holds past its end");

                Undo.IncrementCurrentGroup();
                window.CopyValues();
                window = null;
                Assert.AreEqual(new[] { 4f, 5f, 5.5f }, effect.DurationSpecification.DurationOperation.Scaler.LevelValues, "the values are copied");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(new[] { 6f, 6.5f, 7f, 7.5f, 8f }, effect.DurationSpecification.DurationOperation.Scaler.LevelValues,
                    "and one Undo takes them back");
            }
            finally
            {
                if (window != null) window.Close();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(source);
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the window was open");
        }
    }
}
