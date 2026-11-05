using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The editor's stress runs (the review's Module 4): The Forge over 2,000 effects and 500 abilities, a template pushed
    /// to 2,000 children, and the Debugger following 200 entities. Each cost is compared per asset or entity with the same
    /// run at a tenth of the size, as the stress suite compares, so the machine's speed doesn't decide it.
    /// </summary>
    [Category("Stress")]
    public class EditorStressTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Folder = "Assets/PlayForge Stress";
        private const int Effects = 2000, Abilities = 500, Children = 2000, Entities = 200;

        private static readonly Type Debugger = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.RuntimeDebugger");

        private readonly List<Object> made = new();

        [TearDown]
        public void Clean()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            foreach (var each in made)
                if (each != null) Object.DestroyImmediate(each);
            made.Clear();
        }

        // ═══════════════════════════════════════════════════════════════
        // The Forge
        // ═══════════════════════════════════════════════════════════════

        private sealed class ForgeCosts
        {
            public int Listed;
            public double Open, Reopen, Sort, Filter;
        }

        /// <summary>
        /// The Forge over 2,500 of the project's assets lists them, sorts them by each column and filters them about as
        /// fast an asset as over 250: within three times. Its first open checks each asset for problems, which the
        /// validator keeps, so a later one is quicker. And open over them, an asset's inspector showing, it listens to no
        /// editor update, so an idle Forge costs nothing however big the project is.
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_The_Forge_lists_sorts_and_filters_2500_assets_as_fast_an_asset_as_250() => Guarded(ForgeBody());

        private IEnumerator ForgeBody()
        {
            var few = new ForgeCosts();
            var many = new ForgeCosts();
            yield return MeasureForge(Effects / 10, Abilities / 10, few, idleCheck: false);
            AssetDatabase.DeleteAsset(Folder);
            yield return MeasureForge(Effects, Abilities, many, idleCheck: true);

            TestContext.WriteLine($"µs an asset, {few.Listed} → {many.Listed} listed: to list {few.Open:F1} → {many.Open:F1} the first time " +
                                  $"and {few.Reopen:F1} → {many.Reopen:F1} again, to sort by every column {few.Sort:F1} → {many.Sort:F1}, " +
                                  $"to filter {few.Filter:F1} → {many.Filter:F1}");
            Assert.That(many.Open, Is.LessThan(3 * few.Open), "to list an asset");
            Assert.That(many.Sort, Is.LessThan(3 * few.Sort), "to sort one by every column");
            Assert.That(many.Filter, Is.LessThan(3 * few.Filter), "to filter one");
        }

        private IEnumerator MeasureForge(int effects, int abilities, ForgeCosts costs, bool idleCheck)
        {
            MakeAssets(effects, abilities);
            int ours = effects + abilities;

            var watch = Stopwatch.StartNew();
            var forge = OpenForge();
            while (Ours(forge) < ours && watch.Elapsed.TotalSeconds < 60) yield return null;
            watch.Stop();
            Assert.AreEqual(ours, Ours(forge), "The Forge lists every asset made");
            costs.Listed = Listed(forge).Count;
            costs.Open = watch.Elapsed.TotalMilliseconds * 1000 / costs.Listed;

            // Again, once the validator knows each asset's problems
            forge.Close();
            yield return null;
            watch.Restart();
            forge = OpenForge();
            while (Ours(forge) < ours && watch.Elapsed.TotalSeconds < 60) yield return null;
            watch.Stop();
            costs.Reopen = watch.Elapsed.TotalMilliseconds * 1000 / costs.Listed;

            var assets = Listed(forge).Cast<ScriptableObject>().ToList();
            var sorted = typeof(TheForge).GetMethod("Sorted", AnyStatic);
            watch.Restart();
            foreach (var column in new[] { "Name", "Type", "AssetTag", "Levels", "Problems" })
            {
                sorted.Invoke(null, new object[] { assets, column, true });
            }
            watch.Stop();
            costs.Sort = watch.Elapsed.TotalMilliseconds * 1000 / assets.Count;

            // The Effect filter, and a search within it
            var root = forge.rootVisualElement;
            watch.Restart();
            root.Q<ToolbarToggle>(nameof(GameplayEffect)).value = true;
            while (Listed(forge).Count > effects + Others(forge, typeof(GameplayEffect)) && watch.Elapsed.TotalSeconds < 60) yield return null;
            root.Q<ToolbarSearchField>("Search").value = "Stress Effect 1";
            while (Listed(forge).Any(asset => asset is GameplayEffect fx && !fx.name.Contains("Stress Effect 1") && Ours(asset)) && watch.Elapsed.TotalSeconds < 60) yield return null;
            watch.Stop();
            costs.Filter = watch.Elapsed.TotalMilliseconds * 1000 / costs.Listed;
            Assert.That(Listed(forge).All(asset => asset is GameplayEffect), "the filter shows effects alone");

            if (idleCheck)
            {
                // Idle over them, with an asset's inspector showing
                root.Q<ToolbarSearchField>("Search").value = "";
                yield return null;
                var list = root.Q<MultiColumnListView>("Assets");
                list.SetSelection(0);
                for (int i = 0; i < 10; i++) yield return null;
                var forgeAssembly = typeof(TheForge).Assembly;
                var listening = (EditorApplication.update?.GetInvocationList() ?? Array.Empty<Delegate>())
                    .Where(listener => listener.Method.DeclaringType?.Assembly == forgeAssembly).ToList();
                Assert.IsEmpty(listening.Select(listener => $"{listener.Method.DeclaringType?.Name}.{listener.Method.Name}"),
                    "an idle Forge listens to no editor update");
            }

            forge.Close();
            yield return null;
        }

        // Effects and abilities in a folder of their own, made in one import
        private void MakeAssets(int effects, int abilities)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "PlayForge Stress");
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < effects; i++)
                {
                    var effect = ScriptableObject.CreateInstance<GameplayEffect>();
                    effect.Definition.Name = $"Stress Effect {i}";
                    AssetDatabase.CreateAsset(effect, $"{Folder}/Stress Effect {i}.asset");
                }
                for (int i = 0; i < abilities; i++)
                {
                    var ability = ScriptableObject.CreateInstance<Ability>();
                    ability.Definition.Name = $"Stress Ability {i}";
                    ability.MaxLevel = 1 + i % 9;
                    AssetDatabase.CreateAsset(ability, $"{Folder}/Stress Ability {i}.asset");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
        }

        private static TheForge OpenForge() => (TheForge)typeof(TheForge).GetMethod("Open", AnyStatic).Invoke(null, null);

        private static List<Object> Listed(TheForge forge) =>
            forge.rootVisualElement.Q<MultiColumnListView>("Assets").itemsSource.Cast<Object>().ToList();

        private static bool Ours(Object asset) => AssetDatabase.GetAssetPath(asset).StartsWith(Folder + "/");

        private static int Ours(TheForge forge) => Listed(forge).Count(Ours);

        // The project's own assets of a type, which a filter shows beside ours
        private static int Others(TheForge forge, Type type) => Listed(forge).Count(asset => type.IsInstanceOfType(asset) && !Ours(asset));

        // ═══════════════════════════════════════════════════════════════
        // A template's push
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// A template pushed to 2,000 children costs about as much a child as pushed to 200, and saving the template, which
        /// changes no child (ED-13), costs about the same with 2,000 children as with 200: a save that went through them
        /// would cost ten times as much.
        /// </summary>
        [Test]
        public void Stress_A_template_pushes_to_2000_children_as_fast_a_child_as_to_200()
        {
            var (pushFew, saveFew) = PushCost(Children / 10);
            AssetDatabase.DeleteAsset(Folder);
            var (pushMany, saveMany) = PushCost(Children);

            TestContext.WriteLine($"{Children / 10} → {Children} children: µs a child to push {pushFew:F1} → {pushMany:F1}, ms to save the template {saveFew:F1} → {saveMany:F1}");
            Assert.That(pushMany, Is.LessThan(3 * pushFew), "to push to a child");
            Assert.That(saveMany, Is.LessThan(3 * saveFew), "to save the template, which changes no child");
        }

        // Microseconds a child to push the template, and milliseconds to save it after an edit
        private (double push, double save) PushCost(int children)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "PlayForge Stress");
            Ability template;
            var family = new List<Ability>(children);
            AssetDatabase.StartAssetEditing();
            try
            {
                template = SavedAbility("Stress Template", 4);
                for (int i = 0; i < children; i++)
                {
                    var child = SavedAbility($"Stress Child {i}", 4);
                    child.SetTemplate(template);
                    EditorUtility.SetDirty(child);
                    family.Add(child);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();

            // An edit to the template, saved: no child changes
            EditInt(template, nameof(Ability.MaxLevel), 9);
            var watch = Stopwatch.StartNew();
            AssetDatabase.SaveAssets();
            watch.Stop();
            double save = watch.Elapsed.TotalMilliseconds;
            Assert.That(family.All(child => child.MaxLevel == 4), "saving the template changes no child");

            var push = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeTemplatePush");
            watch.Restart();
            var pushed = (List<BaseForgeAsset>)push.GetMethod("PushToChildren", AnyStatic).Invoke(null, new object[] { template });
            watch.Stop();
            Assert.AreEqual(children, pushed.Count, "the push reaches every child");
            Assert.That(family.All(child => child.MaxLevel == 9), "and each takes the template's value");
            Undo.ClearAll();
            return (watch.Elapsed.TotalMilliseconds * 1000 / children, save);
        }

        private static Ability SavedAbility(string name, int maxLevel)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = name;
            ability.MaxLevel = maxLevel;
            AssetDatabase.CreateAsset(ability, $"{Folder}/{name}.asset");
            return ability;
        }

        // An edit as an inspector makes one, flushed as the editor flushes each frame's
        private static void EditInt(Object asset, string path, int value)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty(path).intValue = value;
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
        }

        // ═══════════════════════════════════════════════════════════════
        // The Debugger
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// The Debugger lists 200 entities, each with attributes and lasting effects, and samples them, as it does while the
        /// game plays, about as fast an entity as 20.
        /// </summary>
        [UnityTest]
        public IEnumerator Stress_The_Debugger_follows_200_entities_as_fast_an_entity_as_20() => Guarded(DebuggerBody());

        private IEnumerator DebuggerBody()
        {
            Assert.IsNotNull(Debugger, "the Debugger");
            var previous = ProcessControl.Instance;
            var host = new GameObject("Stress Process Control") { hideFlags = HideFlags.HideAndDontSave };
            var control = host.AddComponent<ProcessControl>();
            var options = new SerializedObject(control);
            options.FindProperty("DontDestroyOnLoad").boolValue = false;
            options.FindProperty("CollectOnAwake").boolValue = false;
            options.ApplyModifiedPropertiesWithoutUndo();
            ProcessControl.Instance = null;
            control.Bootstrap();
            var liveBefore = Debugger.GetField("Live", AnyStatic).GetValue(null);
            try
            {
                var health = Attribute("Health");
                var mana = Attribute("Mana");
                var set = Set((health, 100f, 100f), (mana, 50f, 50f));
                var effects = Enumerable.Range(0, 5).Select(i => Lasting($"Stress Lasting {i}")).ToList();

                double few = 0, many = 0;
                var watch = new Stopwatch();
                foreach (int count in new[] { Entities / 10, Entities })
                {
                    var relays = new List<ProcessRelay>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var gas = new RuntimeGameplayAbilitySystem(Identity($"Stress Entity {i}", set));
                        Assert.IsTrue(ProcessControl.Register(gas, control, ProcessDataPacket.Default(), out var relay), "an entity registers");
                        foreach (var effect in effects) gas.ApplyGameplayEffect(gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(gas), effect));
                        relays.Add(relay);
                    }

                    Debugger.GetField("Live", AnyStatic).SetValue(null, (Func<bool>)(() => true));
                    var window = (EditorWindow)Debugger.GetMethod("Open", AnyStatic).Invoke(null, null);
                    for (int i = 0; i < 4; i++) yield return null;
                    var sample = Debugger.GetMethod("SampleNow", AnyInstance);
                    sample.Invoke(window, null);
                    yield return null;

                    var listed = window.rootVisualElement.Q<MultiColumnListView>("Entities").itemsSource.Cast<object>().ToList();
                    Assert.AreEqual(count, listed.OfType<IGameplayAbilitySystem>().Count(gas => gas.GetName().StartsWith("Stress Entity")),
                        $"the Debugger lists the {count} entities");

                    // The fastest of five samples
                    double fastest = double.MaxValue;
                    for (int round = 0; round < 5; round++)
                    {
                        watch.Restart();
                        sample.Invoke(window, null);
                        watch.Stop();
                        fastest = Math.Min(fastest, watch.Elapsed.TotalMilliseconds * 1000 / count);
                        yield return null;
                    }
                    if (count == Entities) many = fastest;
                    else few = fastest;

                    window.Close();
                    foreach (var relay in relays) control.TerminateImmediate(relay.CacheIndex, false);
                    yield return null;
                }

                TestContext.WriteLine($"µs an entity to sample, {Entities / 10} → {Entities}: {few:F1} → {many:F1}");
                Assert.That(many, Is.LessThan(3 * few), "to sample an entity");
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll(Debugger).Cast<EditorWindow>().ToList()) window.Close();
                Debugger.GetField("Live", AnyStatic).SetValue(null, liveBefore);
                control.TerminateAllImmediately();
                Object.DestroyImmediate(host);
                ProcessControl.Instance = previous;
            }
        }

        private T Own<T>(T asset) where T : Object
        {
            made.Add(asset);
            return asset;
        }

        private ForgeAttribute Attribute(string name)
        {
            var attribute = Own(ScriptableObject.CreateInstance<ForgeAttribute>());
            attribute.name = attribute.Name = name;
            attribute.Textures = new List<TextureItem>();
            return attribute;
        }

        private AttributeSet Set(params (ForgeAttribute attribute, float current, float @base)[] values)
        {
            var set = Own(ScriptableObject.CreateInstance<AttributeSet>());
            set.name = set.Name = "Stress Set";
            set.AssetTag = Tag.Generate("Test.Stress.Set");
            set.Attributes = values.Select(value => new AttributeSetElement
            {
                Attribute = value.attribute,
                Current = new AttributeMagnitudeSpec { Magnitude = value.current, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Base = new AttributeMagnitudeSpec { Magnitude = value.@base, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Overflow = new AttributeOverflowData { Policy = EAttributeOverflowPolicy.Unlimited },
                RetentionGroup = Tags.DEFAULT,
                Constraints = new AttributeConstraints { AutoClamp = true, RoundingMode = EAttributeRoundingPolicy.None },
            }).ToList();
            set.SubSets = new List<AttributeSet>();
            set.WorkerGroup = new StandardWorkerGroup();
            return set;
        }

        private static RuntimeEntityIdentity Identity(string name, AttributeSet set) => new()
        {
            Name = name,
            AssetTag = Tag.Generate($"Test.Stress.{name.Replace(" ", "")}"),
            GrantedTags = new List<Tag>(),
            ContextTags = new List<Tag>(),
            Affiliation = new List<Tag>(),
            AttributeSet = set,
            MaxAbilitiesOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
            MaxItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
            MaxEquippedItemsOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 8 },
        };

        // An effect that lasts and changes nothing
        private GameplayEffect Lasting(string name)
        {
            var effect = Own(ScriptableObject.CreateInstance<GameplayEffect>());
            effect.name = name;
            effect.Definition = new GameplayEffectDefinition { Name = name, Textures = new List<TextureItem>() };
            effect.Tags = new GameplayEffectTags { AssetTag = Tag.Generate($"Test.Stress.{name.Replace(" ", "")}"), ContextTags = new List<Tag>(), GrantedTags = new List<Tag>() };
            effect.Workers = new List<AbstractEffectWorker>();
            var none = new EffectTagRequirements
            {
                ApplicationRequirements = new AvoidRequireTagGroup(),
                OngoingRequirements = new AvoidRequireTagGroup(),
                RemovalRequirements = new AvoidRequireTagGroup(),
            };
            effect.SourceRequirements = none;
            effect.TargetRequirements = none;
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = null,
                ImpactOperation = ECalculationOperation.Add,
                AffiliationPolicy = EAffiliationPolicy.AlwaysAllow,
                ImpactTypes = new List<Tag>(),
                MagnitudeOperation = new ScalerMagnitudeOperation { RealMagnitude = EMagnitudeOperation.UseMagnitude },
                Packets = Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Infinite,
                DurationOperation = new ScalerMagnitudeOperation { Magnitude = 0f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                TicksOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 0 },
                TickIntervalOperation = new ScalerMagnitudeOperation { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                AdditionalExecuteTicksOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 0 },
                StackAmountOperation = new ScalerIntegerMagnitudeOperation { Magnitude = 1 },
                StackingConfigs = new List<AbstractStackingConfig>(),
                RealDeltaTime = EMagnitudeOperation.AddScaler,
            };
            return effect;
        }

        // ═══════════════════════════════════════════════════════════════
        // Guarding batch mode's windows
        // ═══════════════════════════════════════════════════════════════

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens. Every other
        // error logged while a window is open is its own
        private sealed class WindowLog : IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public List<string> Errors { get; } = new();

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

        private static IEnumerator Guarded(IEnumerator test)
        {
            var log = new WindowLog();
            try
            {
                while (test.MoveNext()) yield return test.Current;
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
                if (Debugger != null)
                    foreach (var window in Resources.FindObjectsOfTypeAll(Debugger).Cast<EditorWindow>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while a window was open");
        }
    }
}
