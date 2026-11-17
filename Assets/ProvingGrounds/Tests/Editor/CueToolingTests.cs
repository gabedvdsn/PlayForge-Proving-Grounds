using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Cue tooling (EP-52): the Problems view notes a Cue Tag cue that plays nowhere, and a Cue Library's inspector shows
    /// each cue tag the project's assets raise, with the route that plays it and the assets that raise it.
    /// </summary>
    public class CueToolingTests
    {
        private readonly List<string> saved = new();
        private readonly List<Object> made = new();
        private ForgeProject previous;
        private bool projectSet;

        [TearDown]
        public void Clean()
        {
            if (projectSet) ForgeProject.Use(previous);
            projectSet = false;
            foreach (var path in saved) AssetDatabase.DeleteAsset(path);
            saved.Clear();
            foreach (var each in made) if (each) Object.DestroyImmediate(each);
            made.Clear();
        }

        // A project whose cue library routes the tags given, used as the game's
        private CueLibrary UseLibrary(params string[] routes)
        {
            var library = ScriptableObject.CreateInstance<CueLibrary>();
            library.name = "EP-52 Cues";
            foreach (var route in routes) library.Routes.Add(new CueRoute { Tag = Tag.Generate(route) });
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            project.Settings.Cues = library;
            made.Add(project);
            previous = ForgeProject.Use(project);
            projectSet = true;
            return library;
        }

        private static GameplayEffect Raising(string name, params string[] tags)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.name = name;
            effect.Definition.Name = name;
            foreach (var tag in tags) effect.Cues.Add(new TagCue { Tag = Tag.Generate(tag) });
            return effect;
        }

        private T Save<T>(T asset, string name) where T : Object
        {
            var path = $"Assets/EP52 {name}.asset";
            AssetDatabase.CreateAsset(asset, path);
            saved.Add(path);
            return asset;
        }

        /// <summary>
        /// A Cue Tag cue whose tag the library routes nowhere, neither its own route nor a parent's, is noted; one under a
        /// route isn't; and with no library, each is (EP-52).
        /// </summary>
        [Test]
        public void EP_52_A_cue_tag_that_plays_nowhere_is_noted()
        {
            UseLibrary("Test.Cue.Hit");
            var effect = Raising("Strike", "Test.Cue.Nowhere", "Test.Cue.Hit.Fire");
            made.Add(effect);

            var notes = ForgeValidator.NotesOf(effect).Select(note => note.Message).ToList();
            Assert.That(notes, Has.Exactly(1).Contains("Test.Cue.Nowhere"), $"the tag no route plays is noted: {string.Join(" | ", notes)}");
            Assert.That(notes, Has.None.Contains("Test.Cue.Hit.Fire"), "a tag under a route plays the route");
            Assert.That(ForgeValidator.ProblemsOf(effect).Select(problem => problem.Message), Has.None.Contains("Test.Cue"),
                "and it's a note, not a warning");

            ForgeProject.Use(previous);
            var bare = ScriptableObject.CreateInstance<ForgeProject>();
            made.Add(bare);
            ForgeProject.Use(bare);
            Assert.That(ForgeValidator.NotesOf(effect).Select(note => note.Message).Where(message => message.Contains("Test.Cue")).ToList(),
                Has.Count.EqualTo(2), "with no library, each tag plays nowhere");
        }

        /// <summary>
        /// A route added to the library drops a saved asset's note that its tag plays nowhere: an edit to the library has
        /// every asset checked again, as the note reads it (EP-52).
        /// </summary>
        [Test]
        public void EP_52_A_route_added_to_the_library_drops_the_note()
        {
            var library = Save(UseLibrary("Test.Cue.Hit"), "Library");
            var effect = Save(Raising("EP52 Late", "Test.Cue.Late"), "Late");
            Assert.That(ForgeValidator.NotesOf(effect).Select(note => note.Message), Has.Exactly(1).Contains("Test.Cue.Late"),
                "the tag no route plays is noted");

            library.Routes.Add(new CueRoute { Tag = Tag.Generate("Test.Cue.Late") });
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(library), ImportAssetOptions.ForceUpdate);

            Assert.That(ForgeValidator.NotesOf(effect).Select(note => note.Message), Has.None.Contains("Test.Cue.Late"),
                "a route plays it now, and its note goes");
        }

        /// <summary>
        /// A Cue Library's inspector shows each cue tag the project's assets raise: the route that plays it, or that it
        /// plays nowhere, and the assets that raise it (EP-52).
        /// </summary>
        [Test]
        public void EP_52_The_cue_librarys_inspector_shows_what_raises_each_tag()
        {
            var library = Save(UseLibrary("Test.Cue.Hit"), "Library");
            Save(Raising("EP52 Strike", "Test.Cue.Hit.Fire"), "Strike");
            Save(Raising("EP52 Shrug", "Test.Cue.Nowhere"), "Shrug");
            var editor = UnityEditor.Editor.CreateEditor(library);
            try
            {
                var root = editor.CreateInspectorGUI();
                var raised = root.Q(name: "Raised");
                Assert.IsNotNull(raised, "the inspector shows what's raised");
                var rows = raised.Query(className: "pf-cue-raised__row").ToList();
                string Text(VisualElement row) => string.Join(" ", row.Query<Label>().ToList().Select(label => label.text))
                                                  + " " + string.Join(" ", row.Query<Button>().ToList().Select(button => button.text));
                var fire = rows.Select(Text).SingleOrDefault(text => text.Contains("Test.Cue.Hit.Fire"));
                Assert.IsNotNull(fire, $"a row for the tag the strike raises: {string.Join(" | ", rows.Select(Text))}");
                StringAssert.Contains("Test.Cue.Hit", fire.Replace("Test.Cue.Hit.Fire", ""), "with the route that plays it");
                StringAssert.Contains("EP52 Strike", fire, "and the asset that raises it");
                var nowhere = rows.Select(Text).SingleOrDefault(text => text.Contains("Test.Cue.Nowhere"));
                Assert.IsNotNull(nowhere, "a row for the tag the shrug raises");
                StringAssert.Contains("nowhere", nowhere, "which plays nowhere");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }
    }
}
