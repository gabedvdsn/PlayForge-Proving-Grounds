#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.AdditionalContent.Tests
{
    /// <summary>
    /// AC-2, Decision 69: the sample's Sparring scene plays. Its two dummies trade the starter set's attacks and statuses,
    /// and every handler shows it: numbers float, the dummies flash and sound, the status bars show their statuses, the
    /// statuses float their words, and the camera shakes at each bolt. The scene is loaded where the sample was imported.
    /// </summary>
    public class SparringSceneTests
    {
        private float savedCapture;

        [UnitySetUp]
        public IEnumerator LoadTheScene()
        {
            var path = AssetDatabase.FindAssets("Sparring t:Scene").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "Sparring" && p.Contains("Sparring/"));
            Assume.That(path, Is.Not.Null, "the sample's scene is in the project");
            savedCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TakePlayForgeDown()
        {
            Time.captureDeltaTime = savedCapture;
            if (ProcessControl.Instance) ProcessControl.Instance.TerminateAllImmediately();
            yield return null;
            yield return null;
            var empty = SceneManager.CreateScene($"After sparring {Time.frameCount}");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
            if (GameRoot.Instance) Object.Destroy(GameRoot.Instance.gameObject);
            if (ProcessControl.Instance) Object.Destroy(ProcessControl.Instance.gameObject);
            foreach (var layer in Object.FindObjectsByType<FloatingTextLayer>(FindObjectsSortMode.None)) Object.Destroy(layer.gameObject);
            yield return null;
            ProcessControl.Instance = null;
            GameRoot.Instance = null;
            TagRegistry.Reset();
        }

        [UnityTest]
        public IEnumerator AC_2_The_sparring_scene_plays_with_every_handler_on()
        {
            var dummies = Object.FindObjectsByType<Sparring>(FindObjectsSortMode.None);
            Assert.AreEqual(2, dummies.Length, "two dummies spar");
            var shaker = Object.FindAnyObjectByType<CameraShaker>();
            Assert.IsNotNull(shaker, "the camera has its shaker");
            foreach (var dummy in dummies)
            {
                Assert.IsNotNull(dummy.Partner, $"{dummy.name} has a partner");
                Assert.AreNotSame(dummy.GetComponent<GameplayAbilitySystem>(), dummy.Partner, "not itself");
                foreach (var feedback in new EntityFeedback[]
                         {
                             dummy.GetComponent<FloatingNumbers>(), dummy.GetComponent<HitFlash>(), dummy.GetComponent<HitSound>(),
                             dummy.GetComponent<StatusBar>(),
                         })
                {
                    Assert.IsNotNull(feedback, $"{dummy.name} has every feedback component");
                    Assert.IsNotNull(feedback.Counted, $"{feedback.GetType().Name} shows the dummy's health");
                }
            }

            bool shook = false, iconed = false, slowed = false, shielded = false;
            for (int frame = 0; frame < 8 * 60; frame++)
            {
                yield return null;
                shook |= shaker.Shaking;
                iconed |= dummies.Any(d => d.GetComponent<StatusBar>().Icons.Count > 0);
                var words = FloatingTextLayer.Current.Words;
                slowed |= words.Any(w => w.Text == "Slowed");
                shielded |= words.Any(w => w.Text == "Shielded");
            }

            foreach (var dummy in dummies)
            {
                Assert.Greater(dummy.Casts, 3, $"{dummy.name} attacks its partner");
                Assert.Greater(dummy.Landed, 1, $"{dummy.name} lands its statuses");
                Assert.Greater(dummy.GetComponent<FloatingNumbers>().Floated, 3, $"numbers float over {dummy.name}");
                Assert.Greater(dummy.GetComponent<HitFlash>().Flashes, 3, $"{dummy.name} flashes");
                Assert.Greater(dummy.GetComponent<HitSound>().Played, 3, $"{dummy.name} sounds");
            }
            Assert.IsTrue(iconed, "a status bar shows a status");
            Assert.IsTrue(slowed, "the slow floats its word");
            Assert.IsTrue(shielded, "and the shield its own");
            Assert.IsTrue(shook, "each bolt shakes the camera");
        }
    }
}
#endif
