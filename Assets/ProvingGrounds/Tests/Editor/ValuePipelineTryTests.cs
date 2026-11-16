using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// A Value Pipeline tried in its inspector (EP-23, the user's notes of 2026-11-16): a value put through its stages at
    /// each level, against stand-ins for the hit's caster and its target, as a Scaler asset's try reads a scaler; chances
    /// that come up where they can, or miss where they can; and a Block or Evade stage that stops the hit as it lands.
    /// </summary>
    public class ValuePipelineTryTests
    {
        private readonly List<Object> made = new();

        [TearDown]
        public void DestroyMade()
        {
            foreach (var each in made) if (each != null) Object.DestroyImmediate(each, true);
            made.Clear();
        }

        private Attribute NamedAttribute(string name)
        {
            var attribute = ScriptableObject.CreateInstance<Attribute>();
            attribute.Name = name;
            attribute.name = name;
            made.Add(attribute);
            return attribute;
        }

        private ValuePipeline PipelineOf(params AbstractValueStage[] stages)
        {
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            pipeline.name = "Tried";
            pipeline.Stages.AddRange(stages);
            made.Add(pipeline);
            return pipeline;
        }

        // A number by level: its value at each level from the first
        private static ScalableFloat ByLevel(params float[] values) =>
            new(new SimpleScaler { LevelValues = values, StretchOverLevels = false }, 1f, EMagnitudeOperation.UseScaler);

        // An attribute read as it is, the caster's or the target's
        private static ScalableFloat Read(Attribute attribute, ESourceTarget from) => new(
            new AttributeBackedScaler { CaptureAttribute = attribute, CaptureFrom = from, ReadAs = EAttributeRead.AsIs }, 1f,
            EMagnitudeOperation.UseScaler);

        /// <summary>A try puts its value through each stage at each level of its range, each step's value in and out kept.</summary>
        [Test]
        public void EP_23_A_try_runs_a_value_through_the_stages_at_each_level()
        {
            var pipeline = PipelineOf(new MultiplyStage { By = ByLevel(1f, 1.5f, 2f) }, new AddStage { Amount = 10f });
            var tried = new ValuePipelineTry { Value = -100f, From = 1, To = 3 };
            var reading = tried.Read(pipeline);

            Assert.AreEqual((1, 3, -100f), (reading.From, reading.To, reading.In), "levels 1 to 3, −100 in");
            Assert.That(reading.Levels.Select(run => run.Out), Is.EqualTo(new[] { -110f, -160f, -210f }).Within(0.0001f),
                "× the level's number, then 10 more to the blow's size, at each");
            var steps = reading.Levels[1].Steps;
            Assert.AreEqual(2, steps.Count, "a step for each stage");
            Assert.AreEqual((-100f, -150f), (steps[0].Before, steps[0].After), "level 2's × 1.5");
            Assert.AreEqual((-150f, -160f), (steps[1].Before, steps[1].After), "and its + 10");

            tried.To = 40;
            Assert.AreEqual(ValuePipelineTry.MostLevels, tried.Read(pipeline).Levels.Length, "at most as many levels as a Scaler asset's try");
            Assert.That(tried.Read(null).Levels.Select(run => run.Out), Is.All.EqualTo(-100f), "and no pipeline gives the value as it went in");
        }

        /// <summary>
        /// A try finds what its stages read of each side and reads what's typed: the caster's Damage Amplification, which a
        /// Multiply stage reads, and the target's Health, which an Execute stage reads.
        /// </summary>
        [Test]
        public void EP_23_A_try_reads_what_its_stages_read_of_each_side()
        {
            var amplification = NamedAttribute("Damage Amplification");
            var health = NamedAttribute("Health");
            var pipeline = PipelineOf(
                new MultiplyStage { By = Read(amplification, ESourceTarget.Source) },
                new ExecuteStage { Share = 0.1f, Attribute = health });
            var tried = new ValuePipelineTry { Value = -100f, From = 1, To = 1 };

            tried.Read(pipeline);
            Assert.That(tried.Numbers.Caster.Attributes.Select(row => row.Attribute), Is.EqualTo(new[] { amplification }), "it reads the caster's amplification");
            Assert.That(tried.Numbers.Target.Attributes, Is.Empty, "and, an amplification of 0 leaving no blow, nothing of the target");

            tried.Numbers.Caster.Set(amplification, 1.5f);
            tried.Read(pipeline);
            Assert.That(tried.Numbers.Target.Attributes.Select(row => row.Attribute), Is.EqualTo(new[] { health }), "a blow to execute reads the target's Health");
            tried.Numbers.Target.Set(health, 1000f);
            Assert.AreEqual(-150f, tried.Read(pipeline).Levels[0].Out, 0.0001f, "1.5 amplification makes −100 −150, which leaves 850 Health");

            tried.Numbers.Target.Set(health, 200f, 1000f);
            var run = tried.Read(pipeline).Levels[0];
            Assert.AreEqual(-200f, run.Out, 0.0001f, "a hit leaving 50 of 200 Health, under a tenth of its base of 1000, takes the rest");
            Assert.AreEqual(-150f, run.Steps[0].After, 0.0001f, "after the multiply's step");
            Assert.AreEqual("executed", run.Steps[1].Note, "and the execute says so");
        }

        /// <summary>
        /// Each chance a stage rolls comes up where it can, or misses where it can, as the try says: a critical strike's
        /// double, or none; a chance of 1 comes up either way.
        /// </summary>
        [Test]
        public void EP_23_Chances_come_up_or_miss_as_the_try_says()
        {
            var pipeline = PipelineOf(new ChanceStage { Chance = 0.25f, Multiplier = 2f });
            var tried = new ValuePipelineTry { Value = -100f, From = 1, To = 2 };

            var none = tried.Read(pipeline);
            Assert.That(none.Levels.Select(run => run.Out), Is.All.EqualTo(-100f), "off, the critical strike misses each time");
            Assert.IsNull(none.Levels[0].Steps[0].Note, "and notes nothing");

            tried.ChancesComeUp = true;
            var all = tried.Read(pipeline);
            Assert.That(all.Levels.Select(run => run.Out), Is.All.EqualTo(-200f), "on, it comes up each time");
            Assert.AreEqual("critical", all.Levels[0].Steps[0].Note, "and says so");

            tried.ChancesComeUp = false;
            var sure = PipelineOf(new ChanceStage { Chance = 1f, Multiplier = 2f });
            Assert.AreEqual(-200f, tried.Read(sure).Levels[0].Out, 0.0001f, "a chance of 1 comes up even off");
            Assert.AreSame(UnityRandom.Instance, ForgeRandom.Source, "and the draws are Unity's again after a read");
        }

        /// <summary>
        /// A Block stage rolls first, as the effect lands: one that comes up stops the hit, nothing of it landing, before the
        /// pipeline runs; one that stops losses alone lets a gain past unrolled, and one for any impact stops it too.
        /// </summary>
        [Test]
        public void EP_23_A_Block_that_comes_up_stops_the_hit()
        {
            var block = new BlockStage { Chance = 0.5f };
            var pipeline = PipelineOf(block, new MultiplyStage { By = 2f });
            var tried = new ValuePipelineTry { Value = -100f, From = 1, To = 1 };

            var missed = tried.Read(pipeline).Levels[0];
            Assert.IsNull(missed.StoppedBy, "off, the block misses");
            Assert.AreEqual(-200f, missed.Out, 0.0001f, "and the pipeline runs");

            tried.ChancesComeUp = true;
            var stopped = tried.Read(pipeline).Levels[0];
            Assert.AreSame(block, stopped.StoppedBy, "on, it stops the hit");
            Assert.AreEqual(0f, stopped.Out, "nothing of it lands");
            Assert.IsEmpty(stopped.Steps, "and the pipeline doesn't run");

            tried.Value = 100f;
            Assert.IsNull(tried.Read(pipeline).Levels[0].StoppedBy, "a gain lands past a block of losses");
            block.Stops = EStopImpacts.AnyImpact;
            Assert.AreSame(block, tried.Read(pipeline).Levels[0].StoppedBy, "and a block of any impact stops it");
        }

        /// <summary>
        /// An Absorb stage says what it soaks in a try, from the side's attribute typed there, though there's no entity for
        /// it to come off: the target's 30 Barrier takes 30 of a blow of 100.
        /// </summary>
        [Test]
        public void EP_23_An_Absorb_stage_soaks_in_a_try()
        {
            var barrier = NamedAttribute("Barrier");
            var pipeline = PipelineOf(new AbsorbStage { Into = barrier, On = NumberSide.Target });
            var tried = new ValuePipelineTry { Value = -100f, From = 1, To = 1 };

            Assert.AreEqual(-100f, tried.Read(pipeline).Levels[0].Out, 0.0001f, "no Barrier takes nothing");
            Assert.That(tried.Numbers.Target.Attributes.Select(row => row.Attribute), Is.EqualTo(new[] { barrier }), "it reads the target's Barrier");

            tried.Numbers.Target.Set(barrier, 30f);
            var run = tried.Read(pipeline).Levels[0];
            Assert.AreEqual(-70f, run.Out, 0.0001f, "30 Barrier takes 30 of the blow");
            Assert.AreEqual("30 to Barrier", run.Steps[0].Note, "and says so");
        }

        /// <summary>
        /// A pipeline's try is kept for the session: it comes back as it was left, its value, its chances, its range and its
        /// sides, and nothing of it is written into the asset.
        /// </summary>
        [Test]
        public void EP_23_A_try_is_kept_for_the_session_not_on_the_asset()
        {
            var barrier = NamedAttribute("Barrier");
            var pipeline = PipelineOf(new AbsorbStage { Into = barrier, On = NumberSide.Target });
            var before = EditorJsonUtility.ToJson(pipeline, true);
            try
            {
                var tried = ValuePipelineTry.For(pipeline);
                tried.Value = -40f;
                tried.ChancesComeUp = true;
                tried.To = 3;
                tried.Read(pipeline);
                tried.Numbers.Target.Set(barrier, 25f);
                tried.Keep();

                var again = ValuePipelineTry.For(pipeline);
                Assert.AreEqual((-40f, true, 3), (again.Value, again.ChancesComeUp, again.To), "its value, chances and range kept");
                Assert.AreEqual(25f, again.Numbers.Target.Of(barrier)?.Current ?? -1f, 0.0001f, "and its sides");
                Assert.AreEqual(before, EditorJsonUtility.ToJson(pipeline, true), "nothing written into the asset");
            }
            finally
            {
                ValuePipelineTry.Forget(pipeline);
            }
        }

        // A runtime panel to build an inspector in, as the Inspector holds one, so its bindings and events run
        private sealed class Host
        {
            private readonly PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            private readonly GameObject owner = new GameObject("EP-23") { hideFlags = HideFlags.HideAndDontSave };
            private UnityEditor.Editor editor;

            public UIDocument Document { get; }

            public Host()
            {
                Document = owner.AddComponent<UIDocument>();
                Document.panelSettings = settings;
            }

            public VisualElement Inspect(Object target)
            {
                editor = UnityEditor.Editor.CreateEditor(target);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "the inspector builds");
                Document.rootVisualElement.Add(root);
                root.Bind(editor.serializedObject);
                return root;
            }

            public void Destroy()
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static IEnumerator Frames()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        private static IEnumerable<string> OutCells(VisualElement view) => view.Q("TryOut")
            .Query<Label>(className: "pf-levels__cell").ToList().Select(cell => cell.text);

        /// <summary>
        /// A Value Pipeline's inspector has a Try section after its stages: a row of what comes out at each level, the steps
        /// behind the selected level under it, and a value typed in that runs at once, the section's line following it. As a
        /// Scaler asset's Try, it has no Import or Clear.
        /// </summary>
        [UnityTest]
        public IEnumerator EP_23_A_Value_Pipelines_inspector_tries_it()
        {
            var pipeline = PipelineOf(new MultiplyStage { By = 2f });
            var host = new Host();
            try
            {
                yield return null;
                Assume.That(host.Document.rootVisualElement?.panel, Is.Not.Null, "the document has a panel");
                var root = host.Inspect(pipeline);
                yield return Frames();

                var children = root.Children().Select(child => child.name).ToList();
                Assert.That(children.IndexOf("Try_CONTAINER"), Is.EqualTo(children.IndexOf("Stages") + 1),
                    $"its Try section after its stages: {string.Join(", ", children)}");
                var section = root.Q("Try_CONTAINER");
                Assert.IsNull(section.Q("TryImport"), "no Import, as it only reads the asset");
                Assert.IsNull(section.Q("TryClear"), "and no Clear");

                var view = section.Q("ValuePipelineTry");
                Assert.IsNotNull(view, "the try");
                Assert.That(OutCells(view), Is.EqualTo(Enumerable.Repeat("-200", 5)), "−100 in comes out −200 at each level");
                StringAssert.Contains("× 2", view.Q<Label>("TrySteps").text, "the steps behind level 1 say the stage");

                view.Q<FloatField>("TryIn").value = -50f;
                yield return Frames();
                Assert.That(OutCells(view), Is.EqualTo(Enumerable.Repeat("-100", 5)), "−50 typed in comes out −100");
                StringAssert.StartsWith("-50 in", section.Q<Label>(className: "pf-section__summary").text, "and the section's line follows it");
            }
            finally
            {
                host.Destroy();
                ValuePipelineTry.Forget(pipeline);
            }
        }
    }
}
