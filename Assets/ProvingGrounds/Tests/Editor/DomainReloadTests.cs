using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Play sessions with domain reloading off (Enter Play Mode Options), where statics outlive play mode. The Asset
    /// Store requires every package to work this way from Editor 6.6 (rule 2.5.h), and Decision 17 does it anyway.
    /// </summary>
    public class DomainReloadTests
    {
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic;

        private bool _optionsWereOn;
        private EnterPlayModeOptions _options;

        [SetUp]
        public void TurnDomainReloadOff()
        {
            _optionsWereOn = EditorSettings.enterPlayModeOptionsEnabled;
            _options = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        }

        [TearDown]
        public void PutOptionsBack()
        {
            EditorSettings.enterPlayModeOptionsEnabled = _optionsWereOn;
            EditorSettings.enterPlayModeOptions = _options;
        }

        private static IDictionary Runs() =>
            (IDictionary)typeof(TaskSequenceRuntime).GetField("RunsByToken", Statics).GetValue(null);

        private static FieldInfo NextActivationId => typeof(AbilityActivationHandle).GetField("_nextId", Statics);

        /// <summary>
        /// The attribute registry, the sequence runs by token and the activation ids carried one session's state into the
        /// next. An attribute registered last session kept its name, so this session's attribute of that name was turned
        /// away.
        /// </summary>
        [UnityTest]
        public IEnumerator SP_22_A_second_play_session_starts_clean()
        {
            var first = ScriptableObject.CreateInstance<ForgeAttribute>();
            var second = ScriptableObject.CreateInstance<ForgeAttribute>();
            first.Name = second.Name = "SP-22 Health";
            try
            {
                yield return new EnterPlayMode(false);
                Assert.IsTrue(AttributeRegistry.Add(first), "the first session registers its attribute");
                NextActivationId.SetValue(null, 41);
                Runs().Add(new CancellationTokenSource().Token, null);
                yield return new ExitPlayMode();

                yield return new EnterPlayMode(false);
                Assert.IsTrue(AttributeRegistry.Add(second), "this session's attribute of that name is registered");
                Assert.AreEqual(0, NextActivationId.GetValue(null), "activation ids start over");
                Assert.That(Runs(), Is.Empty, "no run is left from the last session");
                yield return new ExitPlayMode();
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
