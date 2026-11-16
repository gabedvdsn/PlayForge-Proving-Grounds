using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// A Forge Project keeps its asset catalog current (EP-15, the user's answer to EP-6's): each PlayForge asset made,
    /// copied or moved into its folder is named for saves by its GUID as it lands, and a deleted one's entry goes, so a save
    /// finds every asset with no Add Its Assets to run. One moved out stays named, so a save made before still finds it.
    /// With Keep Catalog Current off, a project is left as it is. Each test works in folders of its own.
    /// </summary>
    public class CatalogKeeperTests
    {
        private const string Arena = "Assets/EP15 Arena";
        private const string Dungeon = "Assets/EP15 Dungeon";
        private const string Outside = "Assets/EP15 Outside";

        [TearDown]
        public void Clean()
        {
            foreach (var folder in new[] { Arena, Dungeon, Outside }) AssetDatabase.DeleteAsset(folder);
        }

        // A project in a folder of its own, named for it
        private static ForgeProject Project(string folder)
        {
            Folder(folder);
            var name = folder.Substring(folder.LastIndexOf('/') + 1);
            var project = ScriptableObject.CreateInstance<ForgeProject>();
            AssetDatabase.CreateAsset(project, $"{folder}/{name}.asset");
            return project;
        }

        private static void Folder(string folder)
        {
            var parts = folder.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                var parent = string.Join("/", parts.Take(i));
                if (!AssetDatabase.IsValidFolder($"{parent}/{parts[i]}")) AssetDatabase.CreateFolder(parent, parts[i]);
            }
        }

        private static T Make<T>(string folder, string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, $"{folder}/{name}.asset");
            return asset;
        }

        private static string GuidOf(Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        private static bool Names(ForgeAssetCatalog catalog, string id) => catalog.Entries.Any(entry => entry.Id == id);

        // Frames for the asset database to settle, as it hands a batch's imports to its postprocessors
        private static IEnumerator Settle()
        {
            for (int i = 0; i < 3; i++) yield return null;
        }

        /// <summary>
        /// An asset made in a project's folder is named by its GUID as it's made, a sub-asset by its file's GUID and its own
        /// ID, in the innermost project whose folder holds it; what isn't a PlayForge asset isn't named.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_15_A_project_names_an_asset_as_its_made_in_its_folder()
        {
            var arena = Project(Arena);
            var inner = Project($"{Arena}/Inner");
            var fire = Make<Ability>(Arena, "EP15 Fireball");
            var health = Make<Attribute>(Arena, "EP15 Health");
            var bolt = Make<GameplayEffect>($"{Arena}/Inner", "EP15 Bolt");
            var catalog = Make<ForgeAssetCatalog>(Arena, "EP15 Other Catalog");
            yield return Settle();

            Assert.IsTrue(arena.TryGetId(fire, out var id), "it names the ability made in its folder");
            Assert.AreEqual(GuidOf(fire), id, "by its GUID");
            Assert.IsTrue(arena.TryGetId(health, out _), "and the attribute");
            Assert.IsTrue(inner.TryGetId(bolt, out _), "a project inside it names its own");
            Assert.IsFalse(arena.TryGetId(bolt, out _), "which the outer one doesn't");
            Assert.IsFalse(arena.TryGetId(catalog, out _), "and a catalog isn't a PlayForge asset to name");

            // A sub-asset by its file's GUID and its own ID, as Add Its Assets names one, as its file is imported again
            var part = ScriptableObject.CreateInstance<GameplayEffect>();
            part.name = "EP15 Part";
            AssetDatabase.AddObjectToAsset(part, fire);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(fire), ImportAssetOptions.ForceUpdate);
            yield return Settle();
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(part, out string guid, out long local));
            Assert.IsTrue(arena.TryGetId(part, out id) && id == $"{guid}:{local}", "a sub-asset by its file's GUID and its own ID");
            Assert.That(arena.Entries.Count(entry => entry.Id == GuidOf(fire)), Is.EqualTo(1), "and its file named once");
        }

        /// <summary>A deleted asset's entry goes, its sub-assets' with it; the others stay.</summary>
        [UnityTest]
        public IEnumerator EP_15_A_deleted_assets_entry_goes()
        {
            var arena = Project(Arena);
            var fire = Make<Ability>(Arena, "EP15 Fireball");
            var health = Make<Attribute>(Arena, "EP15 Health");
            yield return Settle();
            string guid = GuidOf(fire);
            Assume.That(Names(arena, guid), "named as it's made");

            Assert.IsTrue(AssetDatabase.DeleteAsset($"{Arena}/EP15 Fireball.asset"));
            yield return Settle();
            Assert.IsFalse(arena.Entries.Any(entry => entry.Id == guid || entry.Id.StartsWith(guid + ":")), "the deleted ability's entry goes");
            Assert.IsTrue(arena.TryGetId(health, out _), "and the attribute's stays");
            Assert.IsFalse(arena.Entries.Any(entry => entry.Asset == null), "no entry names nothing");
        }

        /// <summary>
        /// An asset moved into another project's folder is named there by the GUID it keeps, and stays named where it was, so
        /// a save made before finds it; a copy is named in the project it's copied into, by its own GUID.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_15_An_asset_moved_or_copied_in_is_named_there()
        {
            var arena = Project(Arena);
            var dungeon = Project(Dungeon);
            var torch = Make<Ability>(Arena, "EP15 Torch");
            yield return Settle();
            string guid = GuidOf(torch);

            Assert.IsEmpty(AssetDatabase.MoveAsset($"{Arena}/EP15 Torch.asset", $"{Dungeon}/EP15 Torch.asset"), "it moves");
            yield return Settle();
            Assert.IsTrue(dungeon.TryGetId(torch, out var id) && id == guid, "the dungeon names it, by the GUID it keeps");
            Assert.IsTrue(arena.TryGetId(torch, out _), "and the arena still does, for its saves");

            Assert.IsTrue(AssetDatabase.CopyAsset($"{Dungeon}/EP15 Torch.asset", $"{Arena}/EP15 Torch Copy.asset"), "it copies");
            yield return Settle();
            var copy = AssetDatabase.LoadAssetAtPath<Ability>($"{Arena}/EP15 Torch Copy.asset");
            Assert.IsTrue(arena.TryGetId(copy, out id) && id == GuidOf(copy), "the copy is named in the arena by its own GUID");
        }

        /// <summary>
        /// A project made where assets are already, as a sample's is as it's imported, names them as it's made; and one kept
        /// before it kept its catalog, its assets made unnamed, catches up on them, as it does once each editor session.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_15_A_project_catches_up_on_the_assets_it_doesnt_name()
        {
            Folder(Arena);
            var fire = Make<Ability>(Arena, "EP15 Fireball");
            var health = Make<Attribute>(Arena, "EP15 Health");
            yield return Settle();
            var arena = Project(Arena);
            yield return Settle();
            Assert.IsTrue(arena.TryGetId(fire, out _) && arena.TryGetId(health, out _), "made where they are, it names them");

            var dungeon = Project(Dungeon);
            dungeon.KeepCatalogCurrent = false;
            var torch = Make<Ability>(Dungeon, "EP15 Torch");
            yield return Settle();
            Assume.That(dungeon.TryGetId(torch, out _), Is.False, "unnamed as it's made, kept by no one");
            dungeon.KeepCatalogCurrent = true;
            var keeper = typeof(FarEmerald.PlayForge.Extended.Editor.PlayForgeSettings).Assembly.GetType("FarEmerald.PlayForge.Extended.Editor.ForgeCatalogKeeper", true);
            var caught = (int)keeper.GetMethod("CatchUp", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { dungeon, ForgeProject.Folders() });
            Assert.AreEqual(1, caught, "kept from before, it catches up on its one");
            Assert.IsTrue(dungeon.TryGetId(torch, out _), "naming it");
        }

        /// <summary>
        /// A project's inspector says what its catalog names in one line, where it drew each entry, a list as long as the
        /// project: how many, and how it keeps them.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_15_The_projects_inspector_says_what_its_catalog_names()
        {
            var arena = Project(Arena);
            Make<Ability>(Arena, "EP15 Fireball");
            Make<Attribute>(Arena, "EP15 Health");
            yield return Settle();
            var line = typeof(FarEmerald.PlayForge.Extended.Editor.ForgeProjectEditor)
                .GetMethod("CatalogLine", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.AreEqual("Names 2 assets for saves, kept current as they're made, moved in and deleted", line.Invoke(null, new object[] { arena }));
            arena.KeepCatalogCurrent = false;
            Assert.AreEqual("Names 2 assets for saves; Add Its Assets names them", line.Invoke(null, new object[] { arena }), "and, kept by hand, how");

            var editor = UnityEditor.Editor.CreateEditor(arena);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNull(root.Q<UnityEditor.UIElements.PropertyField>("PropertyField:entries"), "no list of its entries");
                Assert.IsNotNull(root.Q<UnityEngine.UIElements.Label>("CatalogLine"), "but its line");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        /// <summary>
        /// An asset in no project's folder is named by none; with Keep Catalog Current off, a project names nothing as it's
        /// made or deleted, and Add Its Assets still names its assets.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_15_A_project_set_not_to_keep_its_catalog_is_left_as_it_is()
        {
            var arena = Project(Arena);
            var dungeon = Project(Dungeon);
            dungeon.KeepCatalogCurrent = false;
            EditorUtility.SetDirty(dungeon);
            AssetDatabase.SaveAssetIfDirty(dungeon);
            Folder(Outside);
            var stray = Make<Ability>(Outside, "EP15 Stray");
            var torch = Make<Ability>(Dungeon, "EP15 Torch");
            yield return Settle();

            Assert.IsFalse(arena.TryGetId(stray, out _) || dungeon.TryGetId(stray, out _), "an asset in no project is named by none");
            Assert.IsFalse(dungeon.TryGetId(torch, out _), "the dungeon names nothing as it's made, its catalog left as it is");
            Assert.AreEqual(1, dungeon.AddOwnAssets(), "and Add Its Assets names it");

            Assert.IsTrue(AssetDatabase.DeleteAsset($"{Dungeon}/EP15 Torch.asset"));
            yield return Settle();
            Assert.AreEqual(1, dungeon.Count, "nor lets go of one deleted");
        }
    }
}
