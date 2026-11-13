using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
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

        /// <summary>Where the formula presets go, beside the recipes (AC-3).</summary>
        public const string FormulasRoot = "Assets/PlayForgeAdditional/Formulas";

        /// <summary>Where the starter set goes, beside the recipes (AC-5).</summary>
        public const string StarterRoot = "Assets/PlayForgeAdditional/Starter Set";

        /// <summary>Where the Sparring scene and its dummy go, beside the starter set (AC-2).</summary>
        public const string SparringRoot = "Assets/PlayForgeAdditional/Sparring";

        /// <summary>PlayForge's Bootstrapper, which brings up ProcessControl and the GameRoot as a scene starts.</summary>
        public const string BootstrapperPath = "Packages/com.faremerald.playforge/Runtime/Prefabs/Bootstrapper/Bootstrapper.prefab";

        // The sample's feedback scripts, which the scene's objects carry, by name: they compile only where the sample is
        private static readonly string[] SampleScripts = { "CameraShaker", "FloatingNumbers", "HitFlash", "HitSound", "StatusBar", "Sparring" };

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

        /// <summary>
        /// Saves the recipes, the formula presets and the starter set, each under its root, each asset in place where it's
        /// there; how many it saved. The starter set's hits read the recipes' Health (AC-3, AC-5). Given a root for it, the
        /// Sparring scene's dummy and the scene too, which needs the sample's scripts in the project (AC-2).
        /// </summary>
        public static int SaveAll(string recipes = Root, string formulas = FormulasRoot, string starter = StarterRoot, string sparring = null)
        {
            var recipeMaker = new SavingMaker(recipes);
            var set = RecipeBook.Build(recipeMaker);
            var formulaMaker = new SavingMaker(formulas);
            FormulaBook.Build(formulaMaker);
            var starterMaker = new SavingMaker(starter);
            var starterSet = StarterBook.Build(starterMaker, set.Health);
            int saved = recipeMaker.Saved + formulaMaker.Saved + starterMaker.Saved;
            if (sparring != null)
            {
                var sparringMaker = new SavingMaker(sparring);
                var dummy = SparringBook.Build(sparringMaker, starterSet, set.Health);
                AssetDatabase.SaveAssets();
                saved += sparringMaker.Saved;
                if (SaveScene(sparring, starterSet, dummy, set.Health) != null) saved++;
            }
            AssetDatabase.SaveAssets();
            return saved;
        }

        /// <summary>
        /// Saves the Sparring scene under its root, over the one there so its GUID holds; its path, or null where the
        /// sample's scripts aren't in the project (AC-2, Decision 69). PlayForge's Bootstrapper, a camera with the sample's
        /// Camera Shaker, a light and a floor, and two dummies of the dummy identity, each with every feedback component on
        /// and a Sparring that trades the starter set's attacks and statuses with the other.
        /// </summary>
        public static string SaveScene(string root, StarterSet starter, SparringSet sparring, ForgeAttribute health)
        {
            var scripts = new Dictionary<string, Type>();
            foreach (var name in SampleScripts)
            {
                var type = Type.GetType($"FarEmerald.PlayForge.AdditionalContent.{name}, FarEmerald.PlayForge.AdditionalContent");
                if (type == null)
                {
                    Debug.LogWarning($"[RecipeExport] The Sparring scene needs the Additional Content sample's scripts, and {name} isn't here.");
                    return null;
                }
                scripts[name] = type;
            }
            var bootstrapper = AssetDatabase.LoadAssetAtPath<GameObject>(BootstrapperPath);
            if (bootstrapper == null) throw new InvalidOperationException($"No Bootstrapper at {BootstrapperPath}");

            Folder(root);
            string path = $"{root}/Sparring.unity";
            // Beside the scenes open, where each is saved; an untitled one, as in batch mode or a test, it takes the place
            // of, and leaves an empty one after, as it found
            bool beside = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) beside = false;
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, beside ? NewSceneMode.Additive : NewSceneMode.Single);
            try
            {
                SceneManager.SetActiveScene(scene);
                PrefabUtility.InstantiatePrefab(bootstrapper, scene);

                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
                camera.transform.SetPositionAndRotation(new Vector3(0f, 3f, -6.5f), Quaternion.Euler(16f, 0f, 0f));
                camera.AddComponent(scripts["CameraShaker"]);

                var light = new GameObject("Directional Light", typeof(Light));
                light.GetComponent<Light>().type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Floor";

                var left = Dummy("Left Dummy", new Vector3(-1.6f, 1f, 0f), sparring.Dummy, scripts, health);
                var right = Dummy("Right Dummy", new Vector3(1.6f, 1f, 0f), sparring.Dummy, scripts, health);
                Spar(left, right, starter, scripts["Sparring"], health, slowFirst: 1.5f, shieldFirst: 3f);
                Spar(right, left, starter, scripts["Sparring"], health, slowFirst: 3.5f, shieldFirst: 1f);

                if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException($"The scene didn't save at {path}");
            }
            finally
            {
                if (beside)
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            return path;
        }

        // A dummy of the identity, a capsule standing on the floor, its hub stepping as the scene's processes do, with
        // every feedback component on, each showing its health: its numbers and bar placed for a capsule's middle
        private static GameplayAbilitySystem Dummy(string name, Vector3 at, EntityIdentity identity, Dictionary<string, Type> scripts,
            ForgeAttribute health)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = name;
            body.transform.position = at;
            var gas = body.AddComponent<GameplayAbilitySystem>();
            gas.EntityData = identity;
            gas.ProcessLifecycle = EProcessLifecycle.Synchronous;
            gas.ProcessTiming = EProcessStepTiming.UpdateAndLate;
            foreach (var feedback in new[] { "FloatingNumbers", "HitFlash", "HitSound", "StatusBar" })
                Set(body.AddComponent(scripts[feedback]), "Attribute", health);
            Set(body.GetComponent(scripts["FloatingNumbers"]), "Offset", new Vector3(0f, 1.75f, 0f));
            Set(body.GetComponent(scripts["StatusBar"]), "Offset", new Vector3(0f, 1.35f, 0f));
            return gas;
        }

        // Its Sparring: the starter set's attacks at its partner, its slow on the partner and its shield on itself, each
        // first at its own time, so the two dummies' don't land together
        private static void Spar(GameplayAbilitySystem dummy, GameplayAbilitySystem partner, StarterSet starter, Type sparringType,
            ForgeAttribute health, float slowFirst, float shieldFirst)
        {
            var sparring = dummy.gameObject.AddComponent(sparringType);
            Set(sparring, "Partner", partner);
            Set(sparring, "Abilities", new List<Ability> { starter.MeleeAttack, starter.Bolt });
            Set(sparring, "Attribute", health);
            var statusType = sparringType.GetNestedType("Status") ?? throw new MissingMemberException(sparringType.Name, "Status");
            var statuses = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(statusType));
            statuses.Add(Status(statusType, starter.Slow, false, slowFirst, 4f));
            statuses.Add(Status(statusType, starter.ShieldEffect, true, shieldFirst, 7f));
            Set(sparring, "Statuses", statuses);
        }

        private static object Status(Type statusType, GameplayEffect effect, bool onSelf, float first, float every)
        {
            var status = Activator.CreateInstance(statusType);
            Set(status, "Effect", effect);
            Set(status, "OnSelf", onSelf);
            Set(status, "First", first);
            Set(status, "Every", every);
            return status;
        }

        private static void Set(object target, string field, object value)
        {
            var info = target.GetType().GetField(field) ?? throw new MissingFieldException(target.GetType().Name, field);
            info.SetValue(target, value);
        }

        public static void Build()
        {
            try
            {
                int saved = SaveAll(sparring: SparringRoot);
                Debug.Log($"[RecipeExport] Saved {saved} assets under {Root}, {FormulasRoot}, {StarterRoot} and {SparringRoot}.");
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
