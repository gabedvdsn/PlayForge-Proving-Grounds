using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class BootstrapTests : ProvingGroundsTest
    {
        /// <summary>
        /// A second GameRoot destroyed itself and then took Instance anyway, so a frame later Instance was a
        /// destroyed object, and every overload that defaults to GameRoot handed its processes to that.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_23_A_second_GameRoot_leaves_Instance_alone() => Live(async () =>
        {
            var host = new GameObject("Second Root");
            host.SetActive(false);
            host.AddComponent<GameRoot>().Bootstrap();
            await Frames.Step();

            Assert.That(GameRoot.Instance, Is.SameAs(World.Root));
        });

        /// <summary>
        /// A Bootstrapper whose GameRoot was refused read the relay it never got and threw, so it never finished.
        /// The bare clone it meant to fall back on would never have initialized or stepped anyway.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_23_A_refused_GameRoot_is_reported() => Live(async () =>
        {
            var host = NewBootstrapper();
            try
            {
                World.Control.SetControlState(EProcessControlState.Closed);
                GameRoot.Instance = null;

                LogAssert.Expect(LogType.Error, new Regex("couldn't register GameRoot"));
                host.SetActive(true);
                await Frames.Step();

                Assert.That(!host, "the Bootstrapper finished");
            }
            finally
            {
                if (host) Object.Destroy(host);
                World.Control.SetControlState(EProcessControlState.Ready);
                GameRoot.Instance = World.Root;
            }
        });

        /// <summary>
        /// A ProcessControl nothing bootstrapped had no step groups, so it threw in every Update, LateUpdate and
        /// FixedUpdate. It steps nothing, and says once that it isn't in use.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_23_A_ProcessControl_nothing_bootstrapped_steps_nothing() => Live(async () =>
        {
            var host = new GameObject("Stray ProcessControl");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("never bootstrapped"));
                host.AddComponent<ProcessControl>();
                await Frames.Step(2);
            }
            finally
            {
                Object.Destroy(host);
            }
        });

        /// <summary>
        /// Unity serializes a field that holds Unity objects as a plain reference, [SerializeReference] or not, so
        /// the attribute on the Bootstrapper's sets, and on ten fields like them, only misled.
        /// </summary>
        [Test]
        public void PC_23_No_field_of_Unity_objects_is_marked_SerializeReference()
        {
            var marked = typeof(ProcessControl).Assembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(field => field.IsDefined(typeof(SerializeReference), false) && HoldsUnityObjects(field.FieldType))
                .Select(field => $"{field.DeclaringType?.Name}.{field.Name}");

            Assert.That(marked, Is.Empty);
        }

        /// <summary>
        /// With domain reload off, statics outlive play mode, so a second session began with each Instance on the
        /// last session's destroyed object. Each clears its own when play mode starts.
        /// </summary>
        [Test]
        public void PC_26_Play_mode_starts_with_no_singletons()
        {
            var control = ProcessControl.Instance;
            var root = GameRoot.Instance;
            try
            {
                foreach (var type in new[] { typeof(ProcessControl), typeof(GameRoot) })
                {
                    var reset = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .SingleOrDefault(method => method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()?.loadType
                                                   == RuntimeInitializeLoadType.SubsystemRegistration);
                    Assert.That(reset, Is.Not.Null, $"{type.Name} is reset when play mode starts");
                    reset.Invoke(null, null);
                }

                Assert.That(ProcessControl.Instance, Is.Null, "no ProcessControl");
                Assert.That(GameRoot.Instance, Is.Null, "and no GameRoot");
            }
            finally
            {
                ProcessControl.Instance = control;
                GameRoot.Instance = root;
            }
        }

        /// <summary>
        /// The Bootstrapper asked `is null` of the singletons, which a destroyed one isn't, so once a scene took them
        /// the next Bootstrapper made nothing and initialized the destroyed pair.
        /// </summary>
        [UnityTest]
        public IEnumerator PC_26_A_Bootstrapper_replaces_destroyed_singletons() => Live(async () =>
        {
            Object.Destroy(World.Control.gameObject);
            Object.Destroy(World.Root.gameObject);
            await Frames.Step(2);

            ProcessControl control = null;
            GameRoot root = null;
            try
            {
                NewBootstrapper().SetActive(true);
                control = ProcessControl.Instance;
                root = GameRoot.Instance;

                Assert.That((bool)control, "a live ProcessControl");
                Assert.That((bool)root, "and a live GameRoot");

                var probe = World.NewProbe("After");
                Assert.That(ProcessControl.Register(probe, ProcessDataPacket.Default(), out _), "which takes processes");
                await Frames.Step(2);

                Assert.That(probe.Recorder[ProbeHook.Update], Is.GreaterThan(0), "and steps them");
            }
            finally
            {
                if (root) Object.Destroy(root.gameObject);
                if (control) Object.Destroy(control.gameObject);
            }
        });

        /// <summary>
        /// A ProcessControl set to start Closed refused GameRoot as well, so no StartState that refuses processes could
        /// work. GameRoot registers while ProcessControl is Ready, and StartState follows.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator PC_29_A_closed_start_still_admits_GameRoot(
            [Values(EProcessControlState.Closed, EProcessControlState.ClosedWaiting)] EProcessControlState start) => Live(async () =>
        {
            Object.Destroy(World.Control.gameObject);
            Object.Destroy(World.Root.gameObject);
            await Frames.Step(2);

            // A prefab's stand-in: under an object that isn't active, so it never starts, while its clone does
            var templates = new GameObject("Control Templates") { hideFlags = HideFlags.HideAndDontSave };
            templates.SetActive(false);
            var template = new GameObject("Closed Control").AddComponent<ProcessControl>();
            template.transform.SetParent(templates.transform, false);
            typeof(ProcessControl).GetField("StartState", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(template, start);

            ProcessControl control = null;
            GameRoot root = null;
            try
            {
                var host = NewBootstrapper();
                host.GetComponent<Bootstrapper>().ProcessControlPrefab = template;
                host.SetActive(true);
                control = ProcessControl.Instance;
                root = GameRoot.Instance;

                Assert.That((bool)root && control.IsRegistered(root.ProcessRelay.CacheIndex), "GameRoot registered");
                Assert.That(control.State, Is.EqualTo(start), "and ProcessControl started as it was set to");
                await Frames.Step();
            }
            finally
            {
                if (root) Object.Destroy(root.gameObject);
                if (control) Object.Destroy(control.gameObject);
                Object.Destroy(templates);
            }
        });

        private static bool HoldsUnityObjects(Type type)
        {
            var element = type.IsArray ? type.GetElementType()
                : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0]
                : type;
            return typeof(Object).IsAssignableFrom(element);
        }

        /// <summary>A Bootstrapper as World makes one, left inactive so a test can choose when it wakes.</summary>
        private GameObject NewBootstrapper()
        {
            var rootTemplate = World.Template<GameRoot>("Game Root");
            rootTemplate.EntityData = Make.Identity("Game Root", Make.UnusedSet());
            rootTemplate.ProcessLifecycle = EProcessLifecycle.Synchronous;
            rootTemplate.ProcessTiming = EProcessStepTiming.UpdateAndLate;

            var host = new GameObject("Bootstrapper");
            host.SetActive(false);
            var bootstrapper = host.AddComponent<Bootstrapper>();
            bootstrapper.GameRootPrefab = rootTemplate;
            bootstrapper.RegisterSets = new List<AttributeSet>();
            return host;
        }
    }
}
