using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The labs' scenes (Decision 33): each loads with its lab's runner, which plays the bot's rounds, and every round
    /// passes there as it does in the lab's tests. The runner plays as fast as it can here.
    /// </summary>
    [Category("Lab")]
    public class LabScenesTests
    {
        private static IEnumerable<LabScene.ELab> Labs => Enum.GetValues(typeof(LabScene.ELab)).Cast<LabScene.ELab>();

        /// <summary>A lab's scene plays its bot's rounds, and each passes, as the lab's tests do.</summary>
        [UnityTest]
        public IEnumerator Lab_Each_scene_plays_its_bots_rounds([ValueSource(nameof(Labs))] LabScene.ELab lab)
        {
#if UNITY_EDITOR
            LabScene.Fast = true;
            var path = LabScene.ScenePath(lab);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            Assert.IsNotNull(load, $"{path} loads");
            while (!load.isDone) yield return null;

            var scene = SceneManager.GetSceneByPath(path);
            var runner = Object.FindObjectsByType<LabScene>(FindObjectsSortMode.None).FirstOrDefault(each => each.gameObject.scene == scene);
            Assert.IsNotNull(runner, "the scene holds its lab's runner");
            Assert.AreEqual(lab, runner.Lab, "set to its lab");

            for (int frame = 0; frame < 60000 && !runner.Finished; frame++) yield return null;
            Assert.IsTrue(runner.Finished, "its rounds end");
            Assert.That(runner.Rounds.Count, Is.GreaterThan(0), "it found the bot's rounds");
            var failed = runner.Rounds.Where(round => round.State == LabScene.ERoundState.Failed).Select(round => $"{round.Name}: {round.Message}");
            Assert.That(failed, Is.Empty, "and each passes, as the lab's tests do");
#else
            Assert.Ignore("The lab scenes load in the editor");
            yield break;
#endif
        }

        // A scene a failed round left playing ends its world, then goes, so the next test starts clean
        [UnityTearDown]
        public IEnumerator UnloadLabScenes()
        {
            LabScene.Fast = false;
            foreach (var runner in Object.FindObjectsByType<LabScene>(FindObjectsSortMode.None))
                yield return runner.StopAndEnd();

            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.path.StartsWith("Assets/ProvingGrounds/Scenes/Labs/", StringComparison.Ordinal)) continue;
                var unload = SceneManager.UnloadSceneAsync(scene);
                while (unload != null && !unload.isDone) yield return null;
            }
        }
    }
}
