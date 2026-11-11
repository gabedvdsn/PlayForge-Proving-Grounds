using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// Forge Projects (FEAT-34, Decision 44): a project is a folder with a project asset at its root. The Forge shows the
    /// active project's assets alone and switches between projects, new assets go into the active one, and a project
    /// names its own assets for saves.
    /// </summary>
    public class ForgeProjectTests
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly System.Type Projects = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeProjects");
        private readonly List<string> folders = new();
        private Object keptActive;

        [SetUp]
        public void Keep() => keptActive = Active;

        [TearDown]
        public void Clean()
        {
            foreach (var folder in folders) AssetDatabase.DeleteAsset(folder);
            folders.Clear();
            Active = keptActive;
            foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
        }

        // The active project, as The Forge reads and sets it
        private static Object Active
        {
            get => (Object)Projects.GetProperty("Active", AnyStatic).GetValue(null);
            set => Projects.GetProperty("Active", AnyStatic).SetValue(null, value);
        }

        // A project of its own, in a folder of the name given, which the test deletes
        private ForgeProject Project(string folder)
        {
            CreateFolders(folder);
            if (!folder.Contains("/Inner")) folders.Add(folder);
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            var name = folder.Substring(folder.LastIndexOf('/') + 1);
            AssetDatabase.CreateAsset(project, $"{folder}/{name}.asset");
            return project;
        }

        private static void CreateFolders(string folder)
        {
            var parts = folder.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                var parent = string.Join("/", parts.Take(i));
                if (!AssetDatabase.IsValidFolder($"{parent}/{parts[i]}")) AssetDatabase.CreateFolder(parent, parts[i]);
            }
        }

        private static Ability AbilityIn(string folder, string name)
        {
            var ability = ScriptableObject.CreateInstance<Ability>();
            ability.Definition.Name = name;
            AssetDatabase.CreateAsset(ability, $"{folder}/{name}.asset");
            return ability;
        }

        private static IEnumerator Frames(int count = 4)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static List<Object> Listed(TheForge forge) =>
            forge.rootVisualElement.Q<MultiColumnListView>("Assets").itemsSource.Cast<Object>().ToList();

        /// <summary>
        /// The Forge showed every PlayForge asset in the project (the user's request, FEAT-34). It shows the active Forge
        /// Project's alone: those under its folder, and in no project inside it, as a folder's project is the innermost
        /// that holds it. Its project menu switches to another.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_34_The_forge_shows_the_active_projects_assets()
        {
            var log = new LogGuard();
            try
            {
                var arena = Project("Assets/FEAT34 Arena");
                var dungeon = Project("Assets/FEAT34 Dungeon");
                Project("Assets/FEAT34 Arena/Inner");
                var fire = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Fireball");
                var inner = AbilityIn("Assets/FEAT34 Arena/Inner", "FEAT34 Inner Bolt");
                var torch = AbilityIn("Assets/FEAT34 Dungeon", "FEAT34 Torchlight");
                Active = arena;

                var forge = (TheForge)typeof(TheForge).GetMethod("Open", AnyStatic).Invoke(null, null);
                yield return Frames();
                var listed = Listed(forge);
                Assert.That(listed, Has.Member(fire), "The Forge shows the active project's assets");
                Assert.That(listed, Has.No.Member(torch), "and not another's");
                Assert.That(listed, Has.No.Member(inner), "nor a project's inside it");

                var menu = forge.rootVisualElement.Q<ToolbarMenu>("Project");
                Assert.IsNotNull(menu, "its toolbar has the project menu");
                Assert.AreEqual("FEAT34 Arena", menu.text, "naming the active project");
                var other = menu.menu.MenuItems().OfType<DropdownMenuAction>().FirstOrDefault(action => action.name == "FEAT34 Dungeon");
                Assert.IsNotNull(other, "which lists the others");
                other.Execute();
                yield return Frames(8);
                Assert.AreSame(dungeon, Active, "choosing one makes it the active one");
                listed = Listed(forge);
                Assert.That(listed, Has.Member(torch), "and The Forge shows its assets");
                Assert.That(listed, Has.No.Member(fire), "and not the one before's");
            }
            finally
            {
                // Closed while the guard is on: a window shown in batch mode says there's no graphics device as it's drawn
                foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while The Forge was open");
        }

        /// <summary>A new asset goes into the active project: its type's folder within the project's (FEAT-34).</summary>
        [Test]
        public void FEAT_34_A_new_asset_goes_into_the_active_project()
        {
            Active = Project("Assets/FEAT34 Arena");
            var made = (Object)typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeAssetCreation")
                .GetMethod("CreateNew", AnyStatic).Invoke(null, new object[] { typeof(Ability), "FEAT34 Made", null });
            StringAssert.StartsWith("Assets/FEAT34 Arena/", AssetDatabase.GetAssetPath(made), "it's made in the active project's folder");
        }

        /// <summary>
        /// A project names its own assets for saves, as the Asset Catalog it grew from names every one: those in its folder
        /// and in no project inside it (FEAT-34, FEAT-9).
        /// </summary>
        [Test]
        public void FEAT_34_A_project_names_its_own_assets_for_saves()
        {
            var arena = Project("Assets/FEAT34 Arena");
            Project("Assets/FEAT34 Arena/Inner");
            var fire = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Fireball");
            var inner = AbilityIn("Assets/FEAT34 Arena/Inner", "FEAT34 Inner Bolt");
            Assert.That(arena.AddOwnAssets(), Is.GreaterThanOrEqualTo(1), "it adds its assets");
            Assert.IsTrue(arena.TryGetId(fire, out _), "naming its own");
            Assert.IsFalse(arena.TryGetId(inner, out _), "and not a project's inside it");
        }

        /// <summary>
        /// Assets move or copy into another project, each into its type's folder there, from The Forge's asset menu, which
        /// acts on the selection (FEAT-34, the user's request): one moved leaves the active project's list, and one copied
        /// stays, with its copy in the other.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_34_Assets_move_or_copy_into_another_project()
        {
            var log = new LogGuard();
            try
            {
                var arena = Project("Assets/FEAT34 Arena");
                Project("Assets/FEAT34 Dungeon");
                var fire = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Fireball");
                var frost = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Frost");
                var bolt = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Bolt");
                Active = arena;
                var forge = (TheForge)typeof(TheForge).GetMethod("Open", AnyStatic).Invoke(null, null);
                yield return Frames();

                var list = forge.rootVisualElement.Q<MultiColumnListView>("Assets");
                var listed = Listed(forge);
                list.SetSelection(new[] { listed.IndexOf(fire), listed.IndexOf(frost) });
                yield return Frames();
                DropdownMenuAction Item(string name)
                {
                    var menu = new DropdownMenu();
                    typeof(TheForge).GetMethod("FillAssetMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(forge, new object[] { menu });
                    return menu.MenuItems().OfType<DropdownMenuAction>().FirstOrDefault(action => action.name == name);
                }

                var move = Item("Move to Project/FEAT34 Dungeon");
                Assert.IsNotNull(move, "the asset menu moves the selection into each other project");
                move.Execute();
                yield return Frames(8);
                StringAssert.StartsWith("Assets/FEAT34 Dungeon/", AssetDatabase.GetAssetPath(fire), "each selected asset moves there");
                StringAssert.StartsWith("Assets/FEAT34 Dungeon/", AssetDatabase.GetAssetPath(frost));
                Assert.That(Listed(forge), Has.No.Member(fire).And.No.Member(frost), "and leaves the active project's list");

                listed = Listed(forge);
                list.SetSelection(listed.IndexOf(bolt));
                yield return Frames();
                var copy = Item("Copy to Project/FEAT34 Dungeon");
                Assert.IsNotNull(copy, "and copies it");
                copy.Execute();
                yield return Frames(8);
                StringAssert.StartsWith("Assets/FEAT34 Arena/", AssetDatabase.GetAssetPath(bolt), "a copied asset stays");
                Assert.That(AssetDatabase.FindAssets("FEAT34 Bolt t:Ability", new[] { "Assets/FEAT34 Dungeon" }), Is.Not.Empty,
                    "with its copy in the other project");
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<TheForge>().ToList()) window.Close();
                log.Dispose();
            }
            Assert.That(log.Errors, Is.Empty, "errors while The Forge was open");
        }

        private static ScalerAsset ScalerIn(string folder, string name)
        {
            var asset = ScriptableObject.CreateInstance<ScalerAsset>();
            asset.Scaler = new SimpleScaler { Name = name, LevelValues = new[] { 1f, 2f }, StretchOverLevels = false };
            AssetDatabase.CreateAsset(asset, $"{folder}/{name}.asset");
            return asset;
        }

        /// <summary>
        /// Every asset picker offered every asset of its type in the Unity project, whatever its Forge Project (the user's
        /// point, FEAT-34). Each offers the active project's alone: a number field's Scaler Assets, the template picker,
        /// an object field's picker, and the tag picker, which offers the tags the project's assets use and not those only
        /// another's do.
        /// </summary>
        [Test]
        public void FEAT_34_Pickers_offer_the_active_projects_assets_alone()
        {
            var arena = Project("Assets/FEAT34 Arena");
            Project("Assets/FEAT34 Dungeon");
            var near = ScalerIn("Assets/FEAT34 Arena", "FEAT34 Arena Damage");
            ScalerIn("Assets/FEAT34 Dungeon", "FEAT34 Dungeon Damage");
            var fire = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Fireball");
            var frost = AbilityIn("Assets/FEAT34 Arena", "FEAT34 Frost");
            var torch = AbilityIn("Assets/FEAT34 Dungeon", "FEAT34 Torchlight");
            var arenaTag = Tag.Generate("Test.FEAT34.Arena");
            var dungeonTag = Tag.Generate("Test.FEAT34.Dungeon");
            fire.Tags.ContextTags = new List<Tag> { arenaTag };
            torch.Tags.ContextTags = new List<Tag> { dungeonTag };
            EditorUtility.SetDirty(fire);
            EditorUtility.SetDirty(torch);
            AssetDatabase.SaveAssets();
            Active = arena;

            // A number field's Scaler Assets
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            try
            {
                effect.DurationSpecification = new GameplayEffectDuration
                {
                    DurationPolicy = EEffectDurationPolicy.Durational,
                    DurationOperation = new ScalableFloat { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseMagnitude },
                };
                var operation = new SerializedObject(effect).FindProperty("DurationSpecification.DurationOperation");
                var menu = typeof(TheForge).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.MagnitudeField")
                    .GetMethod("MenuFor", AnyStatic).Invoke(null, new object[] { operation, "Duration", null });
                var paths = ((IEnumerable)menu.GetType().GetProperty("Items").GetValue(menu)).Cast<object>()
                    .Select(item => (string)item.GetType().GetField("Item1").GetValue(item)).ToList();
                Assert.That(paths, Has.Member($"Scaler Assets/{near.DisplayName}"), "a number field offers the project's Scaler assets");
                Assert.That(paths, Has.None.Contains("FEAT34 Dungeon Damage"), "and not another's");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }

            // The template picker
            var templates = BasePlayForgeEditor.TemplatesFor(fire).Select(each => each.Asset).ToList();
            Assert.That(templates, Has.Member(frost), "the template picker offers the project's abilities");
            Assert.That(templates, Has.No.Member(torch), "and not another's");

            // An object field's picker
            var picked = ((IEnumerable)typeof(AbilityDrawer).BaseType.GetMethod("ProjectAssets", AnyStatic).Invoke(null, null)).Cast<Object>().ToList();
            Assert.That(picked, Has.Member(fire).And.Member(frost), "an ability field's picker offers the project's abilities");
            Assert.That(picked, Has.No.Member(torch), "and not another's");

            // The tag picker
            ForgeTagRegistry.RefreshCache();
            var offers = (System.Func<Tag, bool>)Projects.GetMethod("OffersTag", AnyStatic).Invoke(null, null);
            Assert.IsTrue(offers(arenaTag), "the tag picker offers a tag the project's assets use");
            Assert.IsFalse(offers(dungeonTag), "and not one only another's do");
        }

        /// <summary>
        /// With projects, and none chosen, the active project is the one at the Unity project's root, which holds what no
        /// other does, or none, when The Forge shows the assets in no project. It was the first by name, so a project
        /// whose only Forge Project was the Demo's showed the Demo's assets alone (FEAT-34).
        /// </summary>
        [Test]
        public void FEAT_34_With_none_chosen_the_active_project_is_the_roots_or_none()
        {
            var arena = Project("Assets/FEAT34 Arena");
            var dungeon = Project("Assets/FEAT34 Dungeon");
            Active = null;
            var active = Active as ForgeProject;
            Assert.That(active, Is.Not.SameAs(arena).And.Not.SameAs(dungeon), "a project not chosen isn't the active one");
            Assert.That(active == null || active.Folder == "Assets", Is.True, "it's the project at the root, or none");
        }

        /// <summary>The Demo's assets make one Forge Project, its own, at the sample's root (FEAT-34).</summary>
        [Test]
        public void FEAT_34_The_demos_assets_make_a_project()
        {
            // The Demo, found by its roster, which no other asset is named
            var roster = AssetDatabase.FindAssets("Demo Roster").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(path => path.EndsWith("/Demo Roster.asset"));
            Assume.That(roster, Is.Not.Null, "the Demo is imported");
            var folder = roster.Substring(0, roster.LastIndexOf('/'));
            var folders = ForgeProject.Folders();
            var demo = folders.FirstOrDefault(each => each.Folder == folder).Project;
            Assert.IsNotNull(demo, "the Demo has a Forge Project at its root");
            var ability = AssetDatabase.FindAssets("t:Ability", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            Assert.IsNotNull(ability, "the Demo has abilities");
            Assert.AreSame(demo, ForgeProject.Owner(ability, folders), "each of them in the Demo's project");
        }

        // Batch mode has no graphics device to show a window, and logs an error saying so as each one opens
        private sealed class LogGuard : System.IDisposable
        {
            private const string NoDevice = "No graphic device is available";
            public List<string> Errors { get; } = new();

            public LogGuard()
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
    }
}
