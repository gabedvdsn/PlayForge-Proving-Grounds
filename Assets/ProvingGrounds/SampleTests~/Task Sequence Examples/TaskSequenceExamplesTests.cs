using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FarEmerald.PlayForge.Testing;

namespace FarEmerald.PlayForge.Samples.TaskSequences.Tests
{
    /// <summary>
    /// Each example of the Task Sequence Examples sample runs (EP-29): every method an ability's sequence dropdown offers
    /// makes its sequence or chain, which runs as a process for two seconds, its waits for keys and all, without a fault
    /// logged. The Errors examples throw on purpose, so what they log is theirs; they're run all the same.
    /// </summary>
    public class TaskSequenceExamplesTests : ForgeTest
    {
        // An example may leave a run of its own going, as Every Run at Once does; the world's end ends it
        protected override bool Audits => false;

        public static IEnumerable<string> Examples() =>
            typeof(TaskSequenceExampleRunner).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(method => method.GetCustomAttribute<TaskSequenceMethodAttribute>() != null)
                .Select(method => $"{method.DeclaringType.Name}.{method.Name}")
                .OrderBy(name => name);

        private static MethodInfo Find(string example)
        {
            var parts = example.Split('.');
            return typeof(TaskSequenceExampleRunner).Assembly.GetTypes()
                .First(type => type.Name == parts[0])
                .GetMethod(parts[1], BindingFlags.Public | BindingFlags.Static);
        }

        [Test]
        public void EP_29_The_dropdown_offers_every_example()
        {
            Assert.That(Examples().Count(), Is.GreaterThanOrEqualTo(40), "the examples the sample marks");
        }

        [UnityTest]
        public IEnumerator EP_29_Each_example_runs_without_a_fault([ValueSource(nameof(Examples))] string example) => Live(async () =>
        {
            var method = Find(example);
            string display = method.GetCustomAttribute<TaskSequenceMethodAttribute>().DisplayName ?? method.Name;
            bool throwsOnPurpose = display.StartsWith("Errors");

            var faults = new List<string>();
            void Hear(string message, string stack, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert) faults.Add($"{type}: {message}");
            }
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Hear;
            try
            {
                var made = method.Invoke(null, null);
                Assert.IsNotNull(made, $"{display} makes its sequence");
                var registration = made switch
                {
                    TaskSequence sequence => ProcessControl.Register(sequence).With(SequenceDataPacket.SceneRoot()).Run(),
                    TaskSequenceChain chain => ProcessControl.Register(chain).With(SequenceDataPacket.SceneRoot()).Run(),
                    _ => default
                };
                Assert.IsTrue(registration, $"{display} runs as a process");

                await Frames.Seconds(2f);
                if (registration.Relay.ProcessActive) registration.Relay.Terminate();
                await Frames.Step(2);
            }
            finally
            {
                Application.logMessageReceived -= Hear;
                LogAssert.ignoreFailingMessages = false;
            }

            if (!throwsOnPurpose)
                Assert.That(faults, Is.Empty, $"{display} logged: {string.Join(" | ", faults.Take(3))}");
        });
    }
}
