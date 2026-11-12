using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Recipes.Building
{
    /// <summary>
    /// Saves the recipe book's recipes as the Additional Content sample's assets (TW-23, Decision 63), under
    /// <see cref="Root"/>, each in place where one is already there, so a rebuild keeps every GUID and the assets'
    /// references to each other. Copy the sample's Recipes folder there first, run it with -executeMethod
    /// FarEmerald.PlayForge.ProvingGrounds.Recipes.Building.RecipeExport.Build, then copy the folder back over
    /// Samples~/Additional Content/Recipes, metas and all. A tool for the sample's authors, not part of the sample.
    /// </summary>
    public static class RecipeExport
    {
        public const string Root = "Assets/PlayForgeAdditional/Recipes";

        // Saves each asset at its path under its folder, or copies it into the one there
        private sealed class SavingMaker : IRecipeMaker
        {
            private readonly string root;
            public int Saved;

            public SavingMaker(string root) => this.root = root;

            public T Keep<T>(string path, T fresh) where T : ScriptableObject
            {
                string full = $"{root}/{path}.asset";
                Folder(Path.GetDirectoryName(full)?.Replace('\\', '/'));
                fresh.name = Path.GetFileNameWithoutExtension(full);
                Saved++;
                var existing = AssetDatabase.LoadAssetAtPath<T>(full);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(fresh, full);
                    return fresh;
                }
                EditorUtility.CopySerialized(fresh, existing);
                existing.name = fresh.name;
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
        }

        /// <summary>Saves every recipe's assets under <paramref name="root"/>, each in place where it's there; how many it saved.</summary>
        public static int Save(string root)
        {
            var maker = new SavingMaker(root);
            RecipeBook.Build(maker);
            AssetDatabase.SaveAssets();
            return maker.Saved;
        }

        public static void Build()
        {
            try
            {
                int saved = Save(Root);
                Debug.Log($"[RecipeExport] Saved {saved} recipe assets under {Root}.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RecipeExport] Failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void Folder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
