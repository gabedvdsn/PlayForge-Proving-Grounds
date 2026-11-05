using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// ProcessControl outside play mode, where editor tools and edit-mode tests drive it. The golden scenarios run this
    /// way in the Test Runner (SP-18).
    /// </summary>
    public class ProcessControlEditModeTests
    {
        private sealed class StillProcess : AbstractMonoProcess
        {
            public override UniTask RunProcess(CancellationToken token) => UniTask.CompletedTask;
        }

        /// <summary>
        /// PooledMonoProcessInstantiator was a stub. It gave back no process, and when a process made through it ended
        /// it kept the process's object rather than destroying it, so the GameObject leaked. Nothing used it, and it's
        /// gone, rather than built into a pool (Decision 23).
        /// </summary>
        [Test]
        public void PC_24_The_pooled_instantiator_stub_is_gone()
        {
            var runtime = typeof(AbstractMonoProcessInstantiator).Assembly;

            Assert.That(runtime.GetType("FarEmerald.PlayForge.PooledMonoProcessInstantiator"), Is.Null, "the stub");
        }

        /// <summary>
        /// A world of its own runs a sequence outside play mode, with no scene at all (FEAT-10, step 6): no ProcessControl,
        /// no GameRoot and no frames, only its owner's steps. A sequence registered without a packet took the scene root's,
        /// which needed a GameRoot, so with none the registration threw.
        /// </summary>
        [Test]
        public void FEAT_10_A_world_of_its_own_runs_a_sequence_with_no_scene_at_all()
        {
            var world = new ProcessWorld();
            bool done = false;
            var sequence = TaskSequenceBuilder.Create("FEAT-10 No Scene")
                .Stage(stage => stage.Delay(1f))
                .Stage(stage => stage.Task(_ => done = true))
                .BuildSequence();
            var result = ProcessControl.Register(sequence).In(world).Run();
            Assert.IsTrue(result, "a world of its own takes a sequence with no scene");

            int steps = 0;
            while (!done && steps < 90)
            {
                world.Step(1f / 60f);
                steps++;
            }

            Assert.IsTrue(done, "and its owner's steps run it through its delay");
            Assert.That(steps, Is.InRange(58, 75), "after the steps its second needs");
            result.Relay.TerminateImmediate();
        }

        /// <summary>
        /// Terminating a Mono process destroyed its object with Object.Destroy, which Unity refuses outside play mode:
        /// it logged an error, and the object stayed.
        /// </summary>
        [Test]
        public void PC_32_A_process_terminated_in_edit_mode_destroys_its_object()
        {
            var previous = ProcessControl.Instance;
            var host = new GameObject("PC-32 Process Control") { hideFlags = HideFlags.HideAndDontSave };
            var control = host.AddComponent<ProcessControl>();
            var options = new SerializedObject(control);
            options.FindProperty("DontDestroyOnLoad").boolValue = false;
            options.FindProperty("CollectOnAwake").boolValue = false;
            options.ApplyModifiedPropertiesWithoutUndo();

            // In the scene, as golden S22's is: an object outside any scene is cloned on registration (PF-C19)
            var body = new GameObject("PC-32 Body") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor };
            try
            {
                ProcessControl.Instance = null;
                control.Bootstrap();
                Assert.IsTrue(ProcessControl.Register(body.AddComponent<StillProcess>(), control, ProcessDataPacket.Default(), out _),
                    "it registers");

                control.TerminateAllImmediately();

                Assert.IsTrue(body == null, "terminating the process destroys its object");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (body != null) Object.DestroyImmediate(body);
                Object.DestroyImmediate(host);
                ProcessControl.Instance = previous;
            }
        }
    }
}
