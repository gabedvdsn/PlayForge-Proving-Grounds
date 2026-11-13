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
            effect.DurationSpecification.DurationOperation ??= new ScalableFloat();
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
        /// Every scaler spread its values over a level range (Decision 28). A scaler the drawer makes maps levels exactly
        /// (ED-33): a Table, whose kind says so, with no Values choice of its own (TW-27).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_33_A_new_scaler_maps_levels_exactly()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.DurationSpecification = new GameplayEffectDuration { DurationOperation = new ScalableFloat() };
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
                kind.value = "Table";
                yield return Frames();
                var made = effect.DurationSpecification.DurationOperation.Scaler as SimpleScaler;
                Assert.IsNotNull(made, "choosing Table makes a simple scaler");
                Assert.IsFalse(made.StretchOverLevels, "which maps levels exactly");

                ExpandScalers(field);
                yield return Frames();
                // Its kind says so: a Table, where a Stretch Over Levels box, then a Values choice, said it (ED-54, TW-27)
                Assert.AreEqual("Table", field.Q<PopupField<string>>("Kind")?.value, "and its kind says so");
                Assert.IsNull(field.Q("ValuesMode"), "with no Values choice of its own");
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
                DurationOperation = new ScalableFloat
                {
                    RealMagnitude = EMagnitudeOperation.UseScaler,
                    Scaler = new SimpleScaler { LevelValues = new[] { 6f, 6.5f, 7f, 7.5f, 8f }, StretchOverLevels = false },
                },
                TickIntervalOperation = new ScalableFloat { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
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
        /// box (ED-33). It reads as the scaler board draws it: its name and kind, a Table (TW-27), with the range it gives,
        /// "6–8 s", and its field menu, then its values per level; a Constant shows its number and unit, as Tick Every's "1 s".
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

                Assert.AreEqual("Table", duration.Q<PopupField<string>>("Kind")?.value, "the field's kind");
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
        /// A field's kind changes as one Undo step: a constant made a Table keeps its number as its first level's value,
        /// and Make Constant keeps the value the first level read (ED-33, TW-27).
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
                tick.Q<PopupField<string>>("Kind").value = "Table";
                yield return Frames();
                var interval = effect.DurationSpecification.TickIntervalOperation;
                Assert.IsInstanceOf<SimpleScaler>(interval.Scaler, "Table gives the field a table");
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
            chilled.DurationSpecification.DurationOperation = new ScalableFloat
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
                field.Q<PopupField<string>>("Kind").value = "Table";
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
                Assert.AreEqual("Scaler · Table · 2 uses", root.Q<Label>("AssetAbout")?.text, "the asset counts the fields that use it");
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

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-51 to ED-54: a scaler's fields, its menu, its levels and its spread
        // ═══════════════════════════════════════════════════════════════════════════

        private const string HiddenClass = "pf-scaler__hidden";

        // Whether a part is drawn and not hidden by its kind
        private static bool Shown(VisualElement element) => element != null && !element.ClassListContains(HiddenClass);

        // An effect whose Duration reads a scaler, drawn on its own in a panel
        private static IEnumerator DrawDuration(Host host, GameplayEffect effect, AbstractScaler scaler, System.Action<PropertyField> check)
        {
            effect.DurationSpecification.DurationOperation = new ScalableFloat { RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = scaler };
            var serialized = new SerializedObject(effect);
            var field = Draw(host, serialized, DurationPath);
            yield return Frames();
            check(field);
            field.RemoveFromHierarchy();
        }

        /// <summary>
        /// A scaler showed parts its kind never reads, and hid parts it does: a Fixed Value showed a table of values per
        /// level, while a Randomized scaler, whose base value is its table, hid it (ED-51, the user's report). A scaler
        /// draws what its kind reads, and a kind's field that applies only to some of its settings shows only for them.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_51_A_scaler_draws_what_its_kind_reads()
        {
            var effect = Burning();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");

                yield return DrawDuration(host, effect, new ConstantScaler { Value = 3f }, field =>
                {
                    Assert.IsNotNull(field.Q<PropertyField>("Value"), "a Fixed Value shows its value");
                    Assert.IsNull(field.Q("LevelTable"), "and no table, which it never reads");
                    Assert.IsNull(field.Q("Behaviours"), "nor behaviours");
                });
                yield return DrawDuration(host, effect, new SetByCallerScaler(), field =>
                {
                    Assert.IsNotNull(field.Q<PropertyField>("Key"), "a Set By Caller shows its key");
                    Assert.IsNotNull(field.Q<PropertyField>("Default"), "and its default");
                    Assert.IsNull(field.Q("LevelTable"), "and no table");
                });
                yield return DrawDuration(host, effect, new EffectStacksScaler(), field =>
                {
                    Assert.IsNull(field.Q("LevelTable"), "an Effect Stacks shows no table");
                    Assert.IsNull(field.Q("Behaviours"), "nor behaviours");
                });
                yield return DrawDuration(host, effect, new ThresholdScaler(), field =>
                {
                    Assert.IsNotNull(field.Q<PropertyField>("Thresholds"), "a Threshold shows its thresholds");
                    Assert.IsNull(field.Q("LevelTable"), "and no table");
                });
                yield return DrawDuration(host, effect, new AttributeBackedScaler(), field =>
                {
                    Assert.IsNull(field.Q("LevelTable"), "an Attribute Backed shows no table");
                    Assert.IsNotNull(field.Q("Behaviours"), "but its behaviours, which adjust the attribute it reads");
                });

                var randomized = new RandomizedScaler { VarianceMode = EVarianceMode.Percentage, LevelValues = new[] { 10f, 20f } };
                yield return DrawDuration(host, effect, randomized, field =>
                {
                    Assert.IsNotNull(field.Q("LevelTable"), "a Randomized shows the table its base value comes from");
                    Assert.IsTrue(Shown(field.Q<PropertyField>("Variance")), "a percentage's variance");
                    Assert.IsFalse(Shown(field.Q<PropertyField>("MinMultiplier")), "and not a multiplier's range");
                });
                randomized.VarianceMode = EVarianceMode.Multiplier;
                yield return DrawDuration(host, effect, randomized, field =>
                {
                    Assert.IsFalse(Shown(field.Q<PropertyField>("Variance")), "a multiplier hides the variance");
                    Assert.IsTrue(Shown(field.Q<PropertyField>("MinMultiplier")), "and shows its range");
                    Assert.IsTrue(Shown(field.Q<PropertyField>("MaxMultiplier")));
                });

                // Its condition is a class with a drawer of its own, and its branches are numbers (FEAT-29)
                var conditional = new ConditionalScaler { When = new LevelCondition { Comparison = EComparisonOperator.GreaterThan, Value = 4f } };
                yield return DrawDuration(host, effect, conditional, field =>
                {
                    Assert.IsNotNull(field.Q<PropertyField>("When"), "a Conditional shows its condition");
                    Assert.IsNotNull(field.Q<PropertyField>("TrueValue"), "and its two branches");
                    Assert.IsNotNull(field.Q<PropertyField>("FalseValue"));
                    Assert.IsNull(field.Q("LevelTable"), "and no table, as each branch is a number of its own");
                });

                yield return DrawDuration(host, effect, new SimpleScaler { LevelValues = new[] { 1f, 2f }, StretchOverLevels = false }, field =>
                {
                    var block = field.Q(className: "pf-scaler-block");
                    Assert.IsNotNull(block, "a scaler's fields sit on one block");
                    Assert.IsNotNull(block.Q("Combine"), "its Combine");
                    Assert.IsNotNull(block.Q("LevelTable"), "its values");
                    Assert.IsNotNull(block.Q("Behaviours"), "and its behaviours");
                    Assert.IsNull(block.Q("Kind"), "under the field's kind, which isn't on it");
                });
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// The menu beside a scaler field opened as narrow as its button, so none of its items could be read (ED-52, the
        /// user's report). It opens as wide as its items.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_52_The_field_menu_opens_wide_enough_to_read()
        {
            var effect = Burning();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, new SerializedObject(effect), DurationPath);
                yield return Frames();
                var menu = field.Q<Button>("FieldMenu");
                Assert.IsNotNull(menu, "the field has its menu");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = menu;
                    menu.SendEvent(submit);
                }
                yield return Frames();
                var opened = host.Document.rootVisualElement.panel.visualTree.Query(className: "unity-base-dropdown__container-outer").ToList().FirstOrDefault();
                Assert.IsNotNull(opened, "the menu opens");
                Assert.That(opened.layout.width, Is.GreaterThan(150f), "as wide as its items, as Save as Scaler Asset…");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        // A scaler a user writes: a base of their own, with a field, then a kind with fields of many types, one kept out of
        // the inspector, one shown only for a setting, and a scaler of its own inside it
        [System.Serializable]
        public abstract class UserBaseScaler : AbstractScaler
        {
            public float Weight = 1f;
        }

        [System.Serializable]
        public class UserScaler : UserBaseScaler
        {
            public enum EMode { Flat, Curved }

            public EMode Mode;
            [ForgeShowWhen(nameof(Mode), EMode.Curved)] public AnimationCurve Shape = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            public bool Enabled = true;
            public int Count = 2;
            public string Note = "x";
            public Tag Marker;
            public System.Collections.Generic.List<float> Steps = new() { 1f, 2f };
            public Vector2 Range = new(0f, 1f);
            [SerializeField] private float secret = 3f;
            [HideInInspector] public float Hidden;
            [SerializeReference] public AbstractScaler Inner;

            public float Secret => secret;
            public override void Initialize(IAttributeImpactCarrier deriv) { }
            public override float Evaluate(IAttributeImpactCarrier deriv) => Weight;
            public override EScalerParts Parts => EScalerParts.None;
        }

        /// <summary>
        /// A scaler of the user's own draws each field it adds, whatever its type, as Unity draws the type (the user's
        /// question, ED-51): the fields of a base of the user's own, which the drawer lost by stopping at the first
        /// abstract class, among them. A field kept out of the inspector stays out, one marked to show for a setting shows
        /// only for it, and the parts of every scaler show as its Parts say.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_51_A_scaler_of_the_users_own_draws_each_of_its_fields()
        {
            var effect = Burning();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var scaler = new UserScaler();
                yield return DrawDuration(host, effect, scaler, field =>
                {
                    Assert.That(field.Q<PopupField<string>>("Kind")?.choices, Has.Member("User"), "the kind menu lists it, by its name");
                    foreach (var name in new[] { "Weight", "Mode", "Enabled", "Count", "Note", "Marker", "Steps", "Range", "secret", "Inner" })
                        Assert.IsNotNull(field.Q<PropertyField>(name), $"its {name} draws");
                    Assert.IsNull(field.Q<PropertyField>("Hidden"), "a field kept out of the inspector stays out");
                    Assert.IsFalse(Shown(field.Q<PropertyField>("Shape")), "a field for another setting is hidden");
                    Assert.IsNull(field.Q("LevelTable"), "and the values it doesn't read aren't drawn");
                });
                scaler.Mode = UserScaler.EMode.Curved;
                yield return DrawDuration(host, effect, scaler, field =>
                    Assert.IsTrue(Shown(field.Q<PropertyField>("Shape")), "the setting that uses it shows it"));
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        // Burning, linked to an ability of the levels given, Fireball
        private static (GameplayEffect Effect, Ability Ability) Linked(int levels)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.name = "Fireball";
            ability.Definition.Name = "Fireball";
            ability.MaxLevel = levels;
            var effect = Burning();
            effect.LinkToProvider(ability);
            return (effect, ability);
        }

        /// <summary>
        /// A scaler's Levels set how many levels it had, whatever its asset's level provider, whose levels it must keep
        /// (ED-53, the user's report). A scaler that follows its provider shows the provider's levels, and no count: a
        /// level past its values reads the last, and typing there gives it its own. Its own levels give it a count again,
        /// and Quick Fill fills the levels the table shows.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_53_A_scalers_levels_follow_its_level_provider()
        {
            var (effect, ability) = Linked(4);
            var host = new Host();
            var log = new WindowLog();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler
                {
                    LevelValues = new[] { 6f, 8f }, StretchOverLevels = false, Configuration = ELevelConfig.LockToLevelProvider,
                };
                var serialized = new SerializedObject(effect);
                var field = Draw(host, serialized, DurationPath);
                yield return Frames();

                var source = field.Q<PopupField<string>>("LevelSource");
                Assert.IsNotNull(source, "the Levels row names where its levels come from");
                StringAssert.Contains("Fireball", source.value, "the provider");
                StringAssert.Contains("1–4", source.value, "and its levels");
                Assert.IsNull(field.Q<IntegerField>("LevelCount"), "with no count to change");
                Assert.That(Enumerable.Range(1, 4).Select(level => field.Q<FloatField>($"Level{level}")?.value),
                    Is.EqualTo(new float?[] { 6f, 8f, 8f, 8f }), "a value for each of the provider's levels, the last holding past the end");
                Assert.IsNull(field.Q<FloatField>("Level5"), "and none past them");

                Undo.IncrementCurrentGroup();
                field.Q<FloatField>("Level4").value = 9f;
                yield return Frames();
                Assert.AreEqual(new[] { 6f, 8f, 8f, 9f }, effect.DurationSpecification.DurationOperation.Scaler.LevelValues,
                    "typing at a level past the values gives it its own");

                effect.DurationSpecification.DurationOperation.Scaler.LevelValues = new[] { 6f };
                serialized.Update();
                QuickFillWizard.Show(serialized.FindProperty($"{DurationPath}.Scaler"), null);
                var wizard = Resources.FindObjectsOfTypeAll<QuickFillWizard>().FirstOrDefault();
                Assert.IsNotNull(wizard, "Quick Fill opens");
                Assert.AreEqual(4, typeof(QuickFillWizard).GetField("_levelCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(wizard),
                    "and fills the provider's levels, not the one value the scaler has");
                wizard.Close();
                yield return Frames();

                // The rows are built again as the values change, so the popup is found again
                source = field.Q<PopupField<string>>("LevelSource");
                source.value = source.choices.First(choice => choice.StartsWith("Own"));
                yield return Frames();
                Assert.AreEqual(ELevelConfig.Unlocked, effect.DurationSpecification.DurationOperation.Scaler.Configuration, "its own levels");
                Assert.IsNotNull(field.Q<IntegerField>("LevelCount"), "give it a count again");
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<QuickFillWizard>().ToList()) window.Close();
                host.Destroy();
                log.Dispose();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ability);
            }
            Assert.That(log.Errors, Is.Empty, "errors while the wizard was open");
        }

        /// <summary>
        /// A spread scaler's table named each point by a level it sat at, as 1.8 or 2.5, which no entity can be, and never
        /// said what a real level reads (ED-62, the user's report). Its points are numbered, and under them each of the
        /// provider's levels shows what it reads, as the runtime reads it: Fireball's five levels read 10, 15, 20, 25 and
        /// 30 from three points of 10, 20 and 30. A stored curve that doesn't follow the points is what the levels read, and
        /// a message says so.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_62_Spread_points_are_numbered_and_each_level_shows_what_it_reads()
        {
            var (effect, ability) = Linked(5);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var spread = new SimpleScaler
                {
                    LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = true, Configuration = ELevelConfig.LockToLevelProvider,
                    Interpolation = EScalerInterpolation.Linear,
                };
                spread.RegenerateCurve();
                effect.DurationSpecification.DurationOperation.Scaler = spread;
                var field = Draw(host, new SerializedObject(effect), DurationPath);
                yield return Frames();

                Assert.That(Enumerable.Range(1, 3).Select(point => field.Q<Label>($"At{point}")?.text), Is.EqualTo(new[] { "1", "2", "3" }),
                    "the points are numbered");
                Assert.That(Enumerable.Range(1, 5).Select(level => field.Q<Label>($"Reads{level}")?.text),
                    Is.EqualTo(new[] { "10", "15", "20", "25", "30" }), "each of Fireball's levels shows what it reads");
                Assert.IsTrue(field.Q<HelpBox>("CurveOutOfDate")?.ClassListContains("pf-scaler__hidden") ?? false,
                    "and its curve follows its points");

                // A curve made with the values, never kept: the levels read the stored curve, flat at 1
                effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler
                {
                    LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = true, Configuration = ELevelConfig.LockToLevelProvider,
                };
                host.Document.rootVisualElement.Clear();
                field = Draw(host, new SerializedObject(effect), DurationPath);
                yield return Frames();
                Assert.That(Enumerable.Range(1, 5).Select(level => field.Q<Label>($"Reads{level}")?.text),
                    Is.EqualTo(new[] { "1", "1", "1", "1", "1" }), "a stale curve is what the levels read");
                Assert.IsFalse(field.Q<HelpBox>("CurveOutOfDate")?.ClassListContains("pf-scaler__hidden") ?? true,
                    "and a message says its curve doesn't follow its points");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// The Reads row reads a scaler spread over its provider's levels "up to a level" as the runtime does (GAS-64): three
        /// values spread up to level 3 of Fireball's five read 10, 20 and 30, then 30. It ended the range at the level being
        /// read, as the runtime did, so every level after the first read 30.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_64_The_reads_row_spreads_up_to_a_level()
        {
            var (effect, ability) = Linked(5);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var upTo = new SimpleScaler
                {
                    LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = true, Configuration = ELevelConfig.Clamped,
                    MaxLevel = 3, Interpolation = EScalerInterpolation.Linear,
                };
                upTo.RegenerateCurve();
                effect.DurationSpecification.DurationOperation.Scaler = upTo;
                var field = Draw(host, new SerializedObject(effect), DurationPath);
                yield return Frames();

                Assert.That(Enumerable.Range(1, 5).Select(level => field.Q<Label>($"Reads{level}")?.text),
                    Is.EqualTo(new[] { "10", "20", "30", "30", "30" }), "spread to level 3, then its last value");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ability);
            }
        }

        /// <summary>
        /// Quick Fill drew a look of its own, a purple bold title, a green Apply, 10 px hints and tinted boxes, from styles set
        /// in C#, with its fields outside the label column (ED-63, the user's report). It's a dialog in the style: what it
        /// fills, the pattern and its values, rounding, and a preview of the values as the scaler's table shows them. Apply
        /// writes them, and one Undo takes them back.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_63_Quick_fill_previews_what_it_writes_and_applies_as_one_step()
        {
            var (effect, ability) = Linked(5);
            effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler
            {
                LevelValues = new[] { 1f }, StretchOverLevels = false, Configuration = ELevelConfig.LockToLevelProvider,
            };
            var serialized = new SerializedObject(effect);
            // Batch mode has no graphics device to give the window a view, which it says; anything else it logs fails
            var log = new WindowLog();
            try
            {
                QuickFillWizard.Show(serialized.FindProperty($"{DurationPath}.Scaler"), null);
                var wizard = Resources.FindObjectsOfTypeAll<QuickFillWizard>().FirstOrDefault();
                Assert.IsNotNull(wizard, "Quick Fill opens");
                yield return Frames();
                var root = wizard.rootVisualElement;
                var wrong = root.Query<VisualElement>().ToList()
                    .SelectMany(each => ForgeInspectorTests.LookSetInCode(each).Select(style => $"{each.GetType().Name} \"{(each as TextElement)?.text ?? each.name}\" sets {style}"))
                    .ToList();
                Assert.That(wrong, Is.Empty, "it sets no look in code");
                Assert.AreEqual("Fireball's levels (1–5)", root.Q<TextField>("Fills")?.value, "it fills Fireball's levels");

                root.Q<EnumField>("Pattern").value = QuickFillWizard.EFillPattern.Linear;
                yield return Frames();
                root.Q<FloatField>("From").value = 10f;
                root.Q<FloatField>("To").value = 50f;
                yield return Frames();
                Assert.That(Enumerable.Range(1, 5).Select(level => root.Q<Label>($"Preview{level}")?.text),
                    Is.EqualTo(new[] { "10", "20", "30", "40", "50" }), "a straight line from 10 to 50 over the five levels");

                var rounding = root.Q<EnumField>("Rounding");
                Assert.IsNotNull(rounding, "it rounds");
                rounding.value = (System.Enum)System.Enum.Parse(rounding.value.GetType(), "ToStep");
                yield return Frames();
                root.Q<FloatField>("Step").value = 25f;
                yield return Frames();
                Assert.That(Enumerable.Range(1, 5).Select(level => root.Q<Label>($"Preview{level}")?.text),
                    Is.EqualTo(new[] { "0", "25", "25", "50", "50" }), "rounded to the nearest 25");

                Undo.IncrementCurrentGroup();
                typeof(QuickFillWizard).GetMethod("Apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(wizard, null);
                yield return Frames();
                Assert.AreEqual(new[] { 0f, 25f, 25f, 50f, 50f }, effect.DurationSpecification.DurationOperation.Scaler.LevelValues,
                    "Apply writes them");
                Undo.PerformUndo();
                Assert.AreEqual(new[] { 1f }, effect.DurationSpecification.DurationOperation.Scaler.LevelValues, "and one Undo takes them back");
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<QuickFillWizard>().ToList()) window.Close();
                log.Dispose();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ability);
            }
            Assert.That(log.Errors, Is.Empty, "errors while the wizard was open");
        }

        /// <summary>
        /// What Stretch Over Levels did was hard to see (ED-54, the user's report). A number by level reads Per Level or
        /// spread over the levels, which its kind says: a Table, or By Level (TW-27). By Level's table numbers its points,
        /// over the provider's levels, and says how a level between two reads (ED-62). Made a Table, it shows a value for
        /// each level.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_54_Spread_over_levels_says_where_each_value_sits()
        {
            var (effect, ability) = Linked(5);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler
                {
                    LevelValues = new[] { 10f, 20f, 30f }, StretchOverLevels = true, Configuration = ELevelConfig.LockToLevelProvider,
                };
                var serialized = new SerializedObject(effect);
                var field = Draw(host, serialized, DurationPath);
                yield return Frames();

                var kind = field.Q<PopupField<string>>("Kind");
                Assert.AreEqual("By Level", kind?.value, "spread over the levels, it's By Level");
                Assert.IsNull(field.Q("ValuesMode"), "whose kind says how it reads, with no Values choice of its own");
                Assert.That(Enumerable.Range(1, 3).Select(point => field.Q<Label>($"At{point}")?.text), Is.EqualTo(new[] { "1", "2", "3" }),
                    "each point is numbered");
                Assert.IsNotNull(field.Q<Label>("Reads5"), "and each of Fireball's levels shows what it reads");
                Assert.IsNotNull(field.Q("BetweenLevels"), "and how a level between two reads");

                kind.value = "Table";
                yield return Frames();
                Assert.IsFalse(effect.DurationSpecification.DurationOperation.Scaler.StretchOverLevels, "a Table reads Per Level");
                Assert.IsNotNull(field.Q<FloatField>("Level5"), "shows a value for each of Fireball's levels");
                Assert.IsNull(field.Q("BetweenLevels"), "and nothing between them");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ability);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-80: the Group
        // ═══════════════════════════════════════════════════════════════════════════

        // A Group of fixed values, each with how it joins the others
        private static ScalerGroup Group(params (float Value, ECalculationOperation Joins)[] members)
        {
            var group = new ScalerGroup();
            foreach (var (value, joins) in members) group.AddMember(new ConstantScaler { Value = value }, joins);
            return group;
        }

        private static System.Collections.Generic.List<string> Problems(Object asset) =>
            ForgeValidator.Check(asset).Select(problem => problem.Message).ToList();

        /// <summary>
        /// A Group's members that multiply multiply the sum of those that add, so with none that adds they multiply 0. The
        /// Group says so under its members, and the Problems view flags it; a member that adds clears both. A Group shows
        /// no values per level (ED-80, the user's notes).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_80_A_group_that_multiplies_nothing_says_so()
        {
            var effect = Burning();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var group = Group((2f, ECalculationOperation.Multiply), (1f, ECalculationOperation.FlatBonus));
                effect.DurationSpecification.DurationOperation = new ScalableFloat { RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = group };
                var serialized = new SerializedObject(effect);
                var field = Draw(host, serialized, DurationPath);
                yield return Frames();

                Assert.IsTrue(Shown(field.Q<HelpBox>("MultipliesNothing")), "members that multiply, with none that adds, say so");
                Assert.IsNull(field.Q("LevelTable"), "and the Group shows no values per level");
                Assert.That(Problems(effect), Has.Some.Contains("multiply"), "the Problems view flags it");

                var members = serialized.FindProperty($"{DurationPath}.{nameof(ScalableFloat.Scaler)}.{nameof(ScalerGroup.Calculations)}");
                members.arraySize++;
                var added = members.GetArrayElementAtIndex(members.arraySize - 1);
                added.FindPropertyRelative(nameof(MagnitudeModifierGroupMember.Calculation)).managedReferenceValue = new ConstantScaler { Value = 4f };
                added.FindPropertyRelative(nameof(MagnitudeModifierGroupMember.RelativeOperation)).intValue = (int)ECalculationOperation.Add;
                serialized.ApplyModifiedProperties();
                yield return Frames();

                Assert.IsFalse(Shown(field.Q<HelpBox>("MultipliesNothing")), "a member that adds clears it");
                Assert.That(Problems(effect), Has.None.Contains("multiply"), "in the Problems view too");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// A Group with no members reads as no kind, so its field reads its number (Decision 44). The field shows that
        /// number, to edit, where it hid it for a Group read alone, and its summary reads it (ED-80).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_80_An_empty_groups_field_shows_the_number_it_reads()
        {
            var effect = Burning();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                effect.DurationSpecification.DurationOperation = new ScalableFloat
                {
                    Magnitude = 5f, RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = new ScalerGroup(),
                };
                var serialized = new SerializedObject(effect);
                var field = Draw(host, serialized, DurationPath);
                yield return Frames();

                var number = field.Q("CombineNumber");
                Assert.IsNotNull(number, "the field has its number");
                Assert.AreNotEqual(DisplayStyle.None, number.style.display.value, "and shows it, as it reads it");
                StringAssert.StartsWith("5", field.Q<Label>("Summary")?.text, "its summary reads the number");
                Assert.IsNull(field.Q("LevelTable"), "and the Group shows no values per level");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ED-81, ED-77 and ED-82: a scaler field's menus, and Quick Fill on a spread scaler
        // ═══════════════════════════════════════════════════════════════════════════

        private const string FirstMember = DurationPath + ".Scaler.Calculations.Array.data[0].Calculation";
        private const string TempFolder = "Assets/ED77Temp";

        // Burning, whose Duration is a Group of one member, a By Level of 7 then 9
        private static GameplayEffect WithAMember()
        {
            var effect = Burning();
            var group = new ScalerGroup();
            group.AddMember(new SimpleScaler { LevelValues = new[] { 7f, 9f }, StretchOverLevels = false }, ECalculationOperation.Add);
            effect.DurationSpecification.DurationOperation = new ScalableFloat { RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = group };
            return effect;
        }

        private static AbstractScaler MemberOf(GameplayEffect effect) =>
            ((ScalerGroup)effect.DurationSpecification.DurationOperation.Scaler).Calculations[0].Calculation;

        // A Scaler asset saved in a folder the test deletes, a By Level of 3 then 4
        private static ScalerAsset SavedScalerAsset(string name)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", "ED77Temp");
            var asset = ScriptableObject.CreateInstance<ScalerAsset>();
            asset.Scaler = new SimpleScaler { Name = name, LevelValues = new[] { 3f, 4f }, StretchOverLevels = false };
            AssetDatabase.CreateAsset(asset, $"{TempFolder}/{name}.asset");
            return asset;
        }

        // Opens a field's menu, as a press of its button does, and gives its items' text, each with whether it's on. In a
        // runtime panel it's UI Toolkit's dropdown, whose items show their paths
        private static IEnumerator OpenMenu(Host host, VisualElement field, System.Collections.Generic.List<(string Text, bool On)> items)
        {
            var button = field.Q<Button>("FieldMenu");
            Assert.IsNotNull(button, "the field has its menu");
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            yield return Frames();
            var opened = host.Document.rootVisualElement.panel.visualTree
                .Query(className: GenericDropdownMenu.containerOuterUssClassName).ToList().LastOrDefault();
            Assert.IsNotNull(opened, "the menu opens");
            items.AddRange(opened.Query(className: GenericDropdownMenu.itemUssClassName).ToList()
                .Select(item => (item.Q<Label>()?.text ?? "", item.enabledSelf)));
        }

        /// <summary>
        /// A scaler field's kind menu listed its kinds flat, where a number field's files them by category (the user's
        /// note, ED-81). A Group member's scaler takes the number field's menu: the kinds at its top, then each category's,
        /// as "Scaler/Clamped"; and none of an attribute set's cached kinds, which it offered beside them, each name twice.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_81_A_scaler_fields_kinds_file_by_category()
        {
            var effect = WithAMember();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, new SerializedObject(effect), FirstMember);
                yield return Frames();

                var kind = field.Q<PopupField<string>>("Kind");
                Assert.IsNotNull(kind, "the member's scaler has its kind");
                Assert.AreEqual("Table", kind.value, "a Table (TW-27)");
                Assert.AreEqual("Scaler/Clamped", kind.formatListItemCallback?.Invoke("Clamped"), "Clamped files under Scaler");
                Assert.AreEqual("Effect/Effect Stacks", kind.formatListItemCallback?.Invoke("Effect Stacks"), "Effect Stacks under Effect");
                Assert.AreEqual("Table", kind.formatListItemCallback?.Invoke("Table"), "Table sits at the top");
                Assert.AreEqual("By Level", kind.formatListItemCallback?.Invoke("By Level"), "and By Level");
                Assert.AreEqual("Clamped", kind.formatSelectedValueCallback?.Invoke("Clamped"), "and the box shows a kind's name alone");
                Assert.That(kind.choices.IndexOf("By Level"), Is.LessThan(kind.choices.IndexOf("Clamped")), "the top's kinds come first");
                // The cached Table, By Level and Shared share their names with a number's
                var twice = kind.choices.GroupBy(choice => choice).Where(each => each.Count() > 1).Select(each => each.Key).ToList();
                Assert.That(twice, Has.No.Member("Table").And.No.Member("By Level").And.No.Member("Shared"),
                    "a member takes none of the cached kinds, which share names with its own");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// A number field's menu listed every Scaler asset at its top (the user's report, ED-77): UI Toolkit's dropdown has
        /// no submenus in this Unity. They file under Scaler Assets, which Unity's own menu, in the inspector, shows as a
        /// submenu: Save as Scaler Asset, then the assets, Import Values, Quick Fill and Make Constant.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_77_The_field_menu_files_scaler_assets_under_a_submenu()
        {
            var effect = Burning();
            var host = new Host();
            try
            {
                SavedScalerAsset("ED-77 Damage");
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, new SerializedObject(effect), DurationPath);
                yield return Frames();

                var items = new System.Collections.Generic.List<(string Text, bool On)>();
                yield return OpenMenu(host, field, items);
                var texts = items.Select(item => item.Text).ToList();
                Assert.That(texts, Has.Member("Scaler Assets/ED-77 Damage"), "each Scaler asset files under Scaler Assets: " + string.Join(", ", texts));
                Assert.That(texts, Has.None.StartsWith("Use a Scaler Asset"), "where they sat under the menu's own items");
                Assert.That(texts, Is.SupersetOf(new[] { "Save as Scaler Asset…", "Import Values…", "Quick Fill…", "Make Constant" }),
                    "beside the menu's other items");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        /// <summary>
        /// Wherever a scaler is chosen, its field offers the number field's menu (the user's request, ED-77): a Group
        /// member's scaler saves as a Scaler asset and shares it, shares another, and Make Constant makes it a Fixed Value
        /// of what it read at level 1, as it has no number of its own to read.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_77_A_group_members_scaler_has_the_field_menu()
        {
            var effect = WithAMember();
            var host = new Host();
            try
            {
                var bonus = SavedScalerAsset("ED-77 Bonus");
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(effect);
                var field = Draw(host, serialized, FirstMember);
                yield return Frames();

                var items = new System.Collections.Generic.List<(string Text, bool On)>();
                yield return OpenMenu(host, field, items);
                var on = items.Where(item => item.On).Select(item => item.Text).ToList();
                Assert.That(on, Is.SupersetOf(new[] { "Save as Scaler Asset…", "Scaler Assets/ED-77 Bonus", "Import Values…", "Quick Fill…", "Make Constant" }),
                    "a member's scaler offers the number field's menu: " + string.Join(", ", items.Select(item => item.Text)));

                var saved = ScalerActions.SaveAsScalerAsset(serialized.FindProperty(FirstMember), "ED-77 Saved", TempFolder);
                Assert.IsNotNull(saved, "Save as Scaler Asset saves the member's scaler");
                Assert.AreEqual(new[] { 7f, 9f }, saved.Scaler.LevelValues, "with its values");
                Assert.AreSame(saved, (MemberOf(effect) as SharedScaler)?.Asset, "and the member shares it");

                serialized.Update();
                ScalerActions.UseScalerAsset(serialized.FindProperty(FirstMember), bonus);
                Assert.AreSame(bonus, (MemberOf(effect) as SharedScaler)?.Asset, "a Scaler asset chosen from the menu, the member shares");

                serialized.Update();
                Assert.IsTrue(ScalerActions.CanMakeConstant(serialized.FindProperty(FirstMember)), "it can be made a constant");
                ScalerActions.MakeConstant(serialized.FindProperty(FirstMember));
                Assert.AreEqual(3f, (MemberOf(effect) as ConstantScaler)?.Value, "a Fixed Value of what it read at level 1, the asset's 3");
                serialized.Update();
                Assert.IsFalse(ScalerActions.CanMakeConstant(serialized.FindProperty(FirstMember)), "after which it's a constant already");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        /// <summary>
        /// Quick Fill on a scaler spread over levels fills its points, as many as it has, not a value for each level (the
        /// user's note, ED-82), and shows what each level reads between them, as the scaler's table does. Apply writes the
        /// points, and the curve the scaler reads through them.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_82_Quick_fill_fills_a_spread_scalers_points()
        {
            var (effect, ability) = Linked(5);
            effect.DurationSpecification.DurationOperation.Scaler = new SimpleScaler
            {
                LevelValues = new[] { 10f, 30f }, StretchOverLevels = true, Configuration = ELevelConfig.LockToLevelProvider,
                Interpolation = EScalerInterpolation.Linear,
            };
            var serialized = new SerializedObject(effect);
            // Batch mode has no graphics device to give the window a view, which it says; anything else it logs fails
            var log = new WindowLog();
            try
            {
                QuickFillWizard.Show(serialized.FindProperty($"{DurationPath}.Scaler"), null);
                var wizard = Resources.FindObjectsOfTypeAll<QuickFillWizard>().FirstOrDefault();
                Assert.IsNotNull(wizard, "Quick Fill opens");
                yield return Frames();
                var root = wizard.rootVisualElement;
                Assert.AreEqual("2 points", root.Q<TextField>("Fills")?.value, "it fills the scaler's two points");
                var count = root.Q<IntegerField>("Count");
                Assert.AreEqual("Points", count?.label, "and takes a count of points");

                count.value = 3;
                root.Q<EnumField>("Pattern").value = QuickFillWizard.EFillPattern.Linear;
                yield return Frames();
                root.Q<FloatField>("From").value = 0f;
                root.Q<FloatField>("To").value = 40f;
                yield return Frames();
                Assert.That(Enumerable.Range(1, 3).Select(point => root.Q<Label>($"Preview{point}")?.text), Is.EqualTo(new[] { "0", "20", "40" }),
                    "three points from 0 to 40");
                Assert.IsNull(root.Q<Label>("Preview4"), "and no more");
                Assert.That(Enumerable.Range(1, 5).Select(level => root.Q<Label>($"Reads{level}")?.text), Is.EqualTo(new[] { "0", "10", "20", "30", "40" }),
                    "each of Fireball's five levels reads between them");

                typeof(QuickFillWizard).GetMethod("Apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(wizard, null);
                yield return Frames();
                var scaler = effect.DurationSpecification.DurationOperation.Scaler;
                Assert.AreEqual(new[] { 0f, 20f, 40f }, scaler.LevelValues, "Apply writes the points");
                Assert.IsTrue(scaler.StretchOverLevels, "which the scaler still spreads over its levels");
                Assert.AreEqual(20f, scaler.Scaling.Evaluate(0.5f), 0.01f, "through the curve it reads");
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<QuickFillWizard>().ToList()) window.Close();
                log.Dispose();
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ability);
            }
            Assert.That(log.Errors, Is.Empty, "errors while the wizard was open");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // FN-2: a Group's new member, and what two elements share
        // ═══════════════════════════════════════════════════════════════════════════

        private static void Press(Button button)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        /// <summary>
        /// A member the Group's list adds is a copy of the last, as Unity's list makes one, with a scaler of its own. Grown as
        /// an array, the list gave it the last member's scaler, so the two shared one and each warned of it (FN-2).
        /// </summary>
        [UnityTest]
        public IEnumerator FN_2_A_new_group_member_holds_a_scaler_of_its_own()
        {
            var effect = WithAMember();
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var field = Draw(host, new SerializedObject(effect), DurationPath);
                Button AddButton() => field.Q<PropertyField>(nameof(ScalerGroup.Calculations))?.Q<Button>("unity-list-view__add-button");
                yield return Frames();
                for (int round = 0; round < 3 && AddButton() == null; round++)
                {
                    ExpandScalers(field);
                    yield return Frames();
                }
                Assert.IsNotNull(AddButton(), "the Group's members are a list that adds");
                // Its + pressed as the list presses it: a submit sent to it here, in a number field's list, never reaches it
                var list = field.Q<PropertyField>(nameof(ScalerGroup.Calculations)).Q<ListView>();
                if (list.overridingAddButtonBehavior != null) list.overridingAddButtonBehavior(list, AddButton());
                else if (list.onAdd != null) list.onAdd(list);
                else list.viewController.AddItems(1);
                yield return Frames();

                var members = ((ScalerGroup)effect.DurationSpecification.DurationOperation.Scaler).Calculations;
                Assert.AreEqual(2, members.Length, "a second member");
                Assert.AreNotSame(members[0].Calculation, members[1].Calculation, "with a scaler of its own");
                Assert.IsInstanceOf<SimpleScaler>(members[1].Calculation, "a copy of the last member's");
                CollectionAssert.AreEqual(new[] { 7f, 9f }, ((SimpleScaler)members[1].Calculation).LevelValues, "with its values");
                Assert.AreEqual(members[0].RelativeOperation, members[1].RelativeOperation, "and how it joins");
                CollectionAssert.AreEqual(new[] { 7f, 9f }, ((SimpleScaler)members[0].Calculation).LevelValues, "the last member keeps its own");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }

        /// <summary>
        /// Two Group members holding one scaler each warn of it. Make a Separate Copy on one gives it a scaler of its own, with
        /// the same values, and neither warns on: each warning was drawn once and never looked again, so the copy cleared
        /// neither, and pressed on the other after, it found nothing shared to copy. Two conditions of a list, which share one
        /// as a scaler does, do the same (FN-2).
        /// </summary>
        [UnityTest]
        public IEnumerator FN_2_A_separate_copy_clears_both_elements_warnings()
        {
            var effect = WithAMember();
            ((ScalerGroup)effect.DurationSpecification.DurationOperation.Scaler).AddMember(MemberOf(effect), ECalculationOperation.Add);
            var condition = new LevelCondition();
            effect.Conditions.Application.Add(condition);
            effect.Conditions.Application.Add(condition);
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var serialized = new SerializedObject(effect);
                var lists = new[]
                {
                    new[] { FirstMember, FirstMember.Replace("data[0]", "data[1]") },
                    new[] { "Conditions.Application.Array.data[0]", "Conditions.Application.Array.data[1]" },
                };
                foreach (var paths in lists)
                {
                    // Each element on its own, as a list draws its elements only as a laid-out view needs them
                    var elements = paths.Select(path => Draw(host, serialized, path)).ToArray();
                    yield return Frames();
                    Assume.That(elements.All(each => each.Q<HelpBox>("SharedSibling") != null), $"both of {paths[0]} and the next warn of what they share");

                    Press(elements[1].Q<HelpBox>("SharedSibling").Q<Button>("MakeSeparateCopy"));
                    yield return Frames();
                    Assert.IsNull(elements[1].Q<HelpBox>("SharedSibling"), $"{paths[1]}, with a copy of its own, warns no more");
                    Assert.IsNull(elements[0].Q<HelpBox>("SharedSibling"), $"nor does {paths[0]}");
                    foreach (var each in elements) each.RemoveFromHierarchy();
                }

                var members = ((ScalerGroup)effect.DurationSpecification.DurationOperation.Scaler).Calculations;
                Assert.AreNotSame(members[0].Calculation, members[1].Calculation, "the members hold a scaler each");
                CollectionAssert.AreEqual(new[] { 7f, 9f }, ((SimpleScaler)members[1].Calculation).LevelValues, "with the same values");
                Assert.AreNotSame(effect.Conditions.Application[0], effect.Conditions.Application[1], "and the conditions one each");
            }
            finally
            {
                host.Destroy();
                Object.DestroyImmediate(effect);
            }
        }
    }
}
