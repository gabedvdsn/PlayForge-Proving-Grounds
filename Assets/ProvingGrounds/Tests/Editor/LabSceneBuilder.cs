using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Builds the labs' scenes (Decision 33): for each lab, a camera, a light and its runner set to it, under
    /// Assets/ProvingGrounds/Scenes/Labs. Run it in batch mode with
    /// <c>-executeMethod FarEmerald.PlayForge.ProvingGrounds.EditorTests.LabSceneBuilder.Build</c>. It has no menu item:
    /// ED-19 keeps every menu item under Window/PlayForge, which is PlayForge's own.
    /// </summary>
    public static class LabSceneBuilder
    {
        // The runner is in the PlayMode tests' assembly, which this one doesn't reference
        private const string RunnerType = "FarEmerald.PlayForge.ProvingGrounds.Tests.LabScene, FarEmerald.PlayForge.ProvingGrounds.Tests";

        public static void Build()
        {
            var runner = Type.GetType(RunnerType) ?? throw new InvalidOperationException("The lab runner isn't compiled");
            var labs = runner.GetNestedType("ELab") ?? throw new InvalidOperationException("The lab runner has no labs");
            var scenePath = runner.GetMethod("ScenePath") ?? throw new InvalidOperationException("The lab runner has no scene paths");
            var title = runner.GetMethod("Title") ?? throw new InvalidOperationException("The lab runner has no titles");

            foreach (var lab in Enum.GetValues(labs))
            {
                var path = (string)scenePath.Invoke(null, new[] { lab });
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");

                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var camera = Camera.main;
                if (camera != null)
                {
                    // Where the runner's framing would start: above and in front of the origin
                    camera.transform.position = new Vector3(0f, 6.4f, -8.5f);
                    camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, 0f) - camera.transform.position);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.17f, 0.18f, 0.2f);
                }

                var host = new GameObject($"{(string)title.Invoke(null, new[] { lab })} Lab");
                var component = host.AddComponent(runner);
                var serialized = new SerializedObject(component);
                serialized.FindProperty("Lab").intValue = Convert.ToInt32(lab);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.SaveScene(scene, path);
                Debug.Log($"[Proving Grounds] Built {path}");
            }

            AssetDatabase.Refresh();
        }
    }
}
