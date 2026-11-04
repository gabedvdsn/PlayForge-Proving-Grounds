using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The one Import window (ED-32, Decision 28): choose a source, then the fields that differ, each pre-checked, and
    /// import them as one Undo step. A whole asset keeps its identity, template and level link (ED-35).
    /// </summary>
    public class ImportWindowTests
    {
        // An effect that lasts a while, as the import tests need one
        private static GameplayEffect Effect(string name, float duration, bool tickOnApplication)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.name = name;
            effect.Definition.Name = name;
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = EEffectDurationPolicy.Durational,
                DurationOperation = new ScalerMagnitudeOperation { Magnitude = duration, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                TickOnApplication = tickOnApplication,
            };
            return effect;
        }

        private static List<VisualElement> Rows(ForgeImportWindow window) =>
            window.rootVisualElement.Query(className: "pf-import__row").ToList();

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens, as the smoke
        // suite's windows do. Every other error logged while the window is open is its own
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

        /// <summary>
        /// Section import copied every field of the section with no choice, and the Effect's Duration couldn't import at
        /// all. The window lists the fields that differ, by the inspector's names and each pre-checked, says which are
        /// unchanged, imports the checked ones and leaves the rest, as one Undo step.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_32_The_import_window_lists_the_fields_that_differ()
        {
            var burning = Effect("Burning", 6f, true);
            var ignite = Effect("Ignite", 4f, false);
            ForgeImportWindow window = null;
            bool imported = false;
            var log = new WindowLog();
            try
            {
                window = ForgeImportWindow.ShowFor(new SerializedObject(burning), "Duration",
                    new[] { nameof(GameplayEffect.DurationSpecification) }, () => imported = true);
                Assert.IsNotNull(window, "the window opens");
                yield return null;
                window.Choose(ignite);
                yield return null;

                var rows = Rows(window);
                Assert.That(rows.Select(row => row.name), Is.EquivalentTo(new[]
                {
                    "DurationSpecification.DurationOperation.Magnitude", "DurationSpecification.TickOnApplication",
                }), "a row for each field that differs");
                Assert.That(rows.Select(row => row.Q<Label>(className: "pf-import__field")?.text),
                    Is.EquivalentTo(new[] { "Duration", "Tick On Application" }), "named as the inspector names them");
                Assert.That(rows.All(row => row.Q<Toggle>("Include").value), "each pre-checked");
                Assert.That(window.rootVisualElement.Q<Label>("Unchanged")?.text, Does.Contain("Duration Policy"),
                    "and the rest listed as unchanged");

                rows.First(row => row.name.EndsWith(nameof(GameplayEffectDuration.TickOnApplication))).Q<Toggle>("Include").value = false;
                yield return null;
                Assert.That(window.Changes.Select(field => field.Path), Is.EqualTo(new[] { "DurationSpecification.DurationOperation.Magnitude" }),
                    "an unchecked field stays out");

                Undo.IncrementCurrentGroup();
                window.Import();
                window = null;
                Assert.AreEqual(4f, burning.DurationSpecification.DurationOperation.Magnitude, "the checked field is imported");
                Assert.IsTrue(burning.DurationSpecification.TickOnApplication, "and the unchecked one isn't");
                Assert.IsTrue(imported, "the section hears of it");

                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(6f, burning.DurationSpecification.DurationOperation.Magnitude, "one Undo takes the import back");
            }
            finally
            {
                if (window != null) window.Close();
                Object.DestroyImmediate(burning);
                Object.DestroyImmediate(ignite);
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the window was open");
        }

        /// <summary>
        /// A list is replaced by default, and its row can choose Add to the List instead (Decision 28).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_32_A_list_can_take_the_sources_items_after_its_own()
        {
            var burning = Effect("Burning", 6f, true);
            var ignite = Effect("Ignite", 6f, true);
            var hot = Tag.Generate("Status.Burning");
            var lit = Tag.Generate("Status.Ignited");
            burning.Tags = new GameplayEffectTags { GrantedTags = new List<Tag> { hot }, ContextTags = new List<Tag>() };
            ignite.Tags = new GameplayEffectTags { GrantedTags = new List<Tag> { lit }, ContextTags = new List<Tag>() };
            var path = $"{nameof(GameplayEffect.Tags)}.{nameof(GameplayEffectTags.GrantedTags)}";
            ForgeImportWindow window = null;
            var log = new WindowLog();
            try
            {
                window = ForgeImportWindow.ShowFor(new SerializedObject(burning), "Tags", new[] { path });
                yield return null;
                window.Choose(ignite);
                yield return null;
                var row = Rows(window).FirstOrDefault(each => each.name == path);
                Assert.IsNotNull(row, "the granted tags differ");
                var append = row.Q<Toggle>("Append");
                Assert.AreEqual(Visibility.Visible, append.style.visibility.value, "and a list's row offers Add to the List");
                append.value = true;
                window.Import();
                Assert.That(burning.Tags.GrantedTags, Is.EqualTo(new[] { hot, lit }), "the source's tags follow the list's own");

                window = ForgeImportWindow.ShowFor(new SerializedObject(burning), "Tags", new[] { path });
                yield return null;
                window.Choose(ignite);
                window.Import();
                Assert.That(burning.Tags.GrantedTags, Is.EqualTo(new[] { lit }), "and by default the list is replaced");
                window = null;
            }
            finally
            {
                if (window != null) window.Close();
                Object.DestroyImmediate(burning);
                Object.DestroyImmediate(ignite);
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the window was open");
        }

        /// <summary>
        /// Importing a whole asset copied the template and the level link, and the nested Definition.Name, since it copied
        /// each top-level field whole (ED-35); the asset tag came along in Tags. A whole asset keeps its name, description,
        /// icons, asset tag, template and level link now, and takes the rest.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_35_Importing_a_whole_asset_leaves_its_identity_and_links()
        {
            var hero = ScriptableObject.CreateInstance<EntityIdentity>();
            var goblin = ScriptableObject.CreateInstance<EntityIdentity>();
            var mine = ScriptableObject.CreateInstance<Ability>();
            var theirs = ScriptableObject.CreateInstance<Ability>();
            var fireball = ScriptableObject.CreateInstance<Ability>();
            fireball.Definition.Name = "Fireball";
            var tags = fireball.Tags;
            tags.AssetTag = Tag.Generate("Ability.Fireball");
            fireball.Tags = tags;
            fireball.StartingLevel = 1;
            fireball.LinkToProvider(hero);
            fireball.SetTemplate(mine);
            var frost = ScriptableObject.CreateInstance<Ability>();
            frost.Definition.Name = "Frost";
            tags = frost.Tags;
            tags.AssetTag = Tag.Generate("Ability.Frost");
            frost.Tags = tags;
            frost.StartingLevel = 3;
            frost.LinkToProvider(goblin);
            frost.SetTemplate(theirs);
            ForgeImportWindow window = null;
            var log = new WindowLog();
            try
            {
                window = ForgeImportWindow.ShowForAsset(new SerializedObject(fireball));
                yield return null;
                window.Choose(frost);
                yield return null;

                var paths = Rows(window).Select(row => row.name).ToList();
                Assert.That(paths, Has.Member(nameof(Ability.StartingLevel)), "the rest of the asset is offered");
                Assert.That(paths.Where(path => path.EndsWith("Name") || path.EndsWith("AssetTag") || path.Contains("_template") ||
                                                path.Contains("_linkedSource") || path.EndsWith("LinkMode")), Is.Empty,
                    "and its name, tag, template and link aren't");

                window.Import();
                window = null;
                Assert.AreEqual(3, fireball.StartingLevel, "the import takes the source's levels");
                Assert.AreEqual("Fireball", fireball.Definition.Name, "and leaves the name");
                Assert.AreEqual("Ability.Fireball", fireball.Tags.AssetTag.Name, "the asset tag");
                Assert.AreSame(hero, fireball.LinkedProvider, "the level link");
                Assert.AreSame(mine, fireball.Template, "and the template");
            }
            finally
            {
                if (window != null) window.Close();
                foreach (var each in new Object[] { hero, goblin, mine, theirs, fireball, frost }) Object.DestroyImmediate(each);
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while the window was open");
        }
    }
}
