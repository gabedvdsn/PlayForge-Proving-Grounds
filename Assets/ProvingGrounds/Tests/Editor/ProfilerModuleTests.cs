using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Unity.Profiling.Editor;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// PlayForge's two modules in Unity's Profiler (FEAT-18, Decision 32): PlayForge Processes and PlayForge GAS chart its
    /// counters, ten at most each, so a frame's PlayForge work reads at a glance beside Unity's own. The counters showed
    /// only in the Profiler's lists of counters, one at a time.
    /// </summary>
    public class ProfilerModuleTests
    {
        /// <summary>
        /// The Profiler finds PlayForge's two modules; each charts ten counters at most, each one of PlayForge's that
        /// exists; between them they chart every counter once; and the Processes module has details of its own, the
        /// frame's costliest processes.
        /// </summary>
        [Test]
        public void FEAT_18_The_Profiler_modules_chart_PlayForges_counters()
        {
            var editor = AppDomain.CurrentDomain.GetAssemblies().First(assembly => assembly.GetName().Name == "FarEmerald.PlayForge.Editor");
            var modules = TypeCache.GetTypesDerivedFrom<ProfilerModule>()
                .Where(type => type.Assembly == editor)
                .ToDictionary(type => type.GetCustomAttribute<ProfilerModuleMetadataAttribute>()?.DisplayName ?? type.Name);
            Assert.That(modules.Keys, Is.EquivalentTo(new[] { "PlayForge Processes", "PlayForge GAS" }), "the Profiler finds PlayForge's two modules");

            RuntimeHelpers.RunClassConstructor(typeof(ForgeCounters).TypeHandle);
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var counters = new HashSet<string>(handles.Select(ProfilerRecorderHandle.GetDescription)
                .Where(description => description.Category.Name == "PlayForge")
                .Select(description => description.Name));
            Assert.That(counters, Has.Count.EqualTo(20), "PlayForge's twenty counters exist");

            var charted = new List<string>();
            foreach (var (name, type) in modules)
            {
                var chart = type.GetField("Counters", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as ProfilerCounterDescriptor[];
                Assert.IsNotNull(chart, $"{name} names the counters it charts");
                Assert.That(chart.Length, Is.InRange(1, 10), $"{name} charts ten at most");
                foreach (var counter in chart)
                {
                    Assert.AreEqual("PlayForge", counter.CategoryName, $"{name}'s {counter.Name} is PlayForge's");
                    Assert.That(counters, Does.Contain(counter.Name), $"{name}'s {counter.Name} is a counter that exists");
                    charted.Add(counter.Name);
                }
            }
            Assert.That(charted, Is.EquivalentTo(counters), "and between them they chart every counter once");

            var processes = modules["PlayForge Processes"];
            Assert.AreEqual(processes, processes.GetMethod(nameof(ProfilerModule.CreateDetailsViewController))?.DeclaringType,
                "the Processes module has details of its own");
        }
    }
}
