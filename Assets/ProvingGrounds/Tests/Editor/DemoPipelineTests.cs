using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// FN-11: the Demo's blows go through its Value Pipelines, Physical Damage and Magic Damage, which do what its damage
    /// workers did (Decision 81): a blow harder by its dealer's Attack or Magic Amplification, then lessened by its target's
    /// Armor, 100 ÷ (100 + Armor) of it going through, or Magic Resistance, 1 − it between 0 and 1. The workers are gone.
    /// </summary>
    public class DemoPipelineTests
    {
        // The Demo's folder, found by its roster, which no other asset is named
        private static string DemoFolder()
        {
            var roster = AssetDatabase.FindAssets("Demo Roster").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith("/Demo Roster.asset"));
            Assume.That(roster, Is.Not.Null, "the Demo is imported");
            return roster.Substring(0, roster.LastIndexOf('/'));
        }

        private static List<T> All<T>(string folder) where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>).Where(asset => asset != null).ToList();

        // The pipeline a blow's kind takes: Physical Damage for a physical blow, Magic Damage for a magic one, none for pure
        private static string PipelineFor(List<Tag> types)
        {
            if (types == null) return null;
            if (types.Any(type => type.Name == "Damage.Physical")) return "Physical Damage";
            if (types.Any(type => type.Name == "Damage.Magic")) return "Magic Damage";
            return null;
        }

        /// <summary>
        /// Every blow of the Demo's, an impact on Health typed physical or magic, its effect's own or an additional one, goes
        /// through its kind's pipeline; a pure blow through none.
        /// </summary>
        [Test]
        public void FN_11_The_Demos_blows_go_through_its_pipelines()
        {
            var folder = DemoFolder();
            var wrong = new List<string>();
            int blows = 0;
            foreach (var effect in All<GameplayEffect>(folder))
            {
                var impact = effect.ImpactSpecification;
                if (impact == null) continue;
                Check(effect.name, "its impact", impact.AttributeTarget, impact.ImpactTypes, impact.Pipeline);
                foreach (var entry in impact.AdditionalImpacts ?? new List<AttributeImpactEntry>())
                    Check(effect.name, "an additional impact", entry?.AttributeTarget, entry?.ImpactTypes, entry?.Pipeline);
            }
            Assert.That(blows, Is.GreaterThanOrEqualTo(15), "the Demo's physical and magic blows, templates among them");
            Assert.That(wrong, Is.Empty, "blows that don't go through their kind's pipeline");

            void Check(string effect, string what, Attribute target, List<Tag> types, ValuePipeline pipeline)
            {
                if (target == null || target.Name != "Health") return;
                var expected = PipelineFor(types);
                if (expected != null) blows++;
                var actual = pipeline != null ? pipeline.name : null;
                if (expected != actual) wrong.Add($"{effect}, {what}: {actual ?? "none"}, where {expected ?? "none"}");
            }
        }

        /// <summary>
        /// The pipelines do what the workers did: the dealer's amplification as 1 + it, then the target's Armor as
        /// 1 ÷ (1 + 0.01 × Armor), or its Magic Resistance as 1 − it, kept between 0 and 1.
        /// </summary>
        [Test]
        public void FN_11_The_Demos_pipelines_do_what_its_workers_did()
        {
            var folder = DemoFolder();
            var pipelines = All<ValuePipeline>(folder);
            var physical = pipelines.FirstOrDefault(each => each.name == "Physical Damage");
            var magic = pipelines.FirstOrDefault(each => each.name == "Magic Damage");
            Assert.IsNotNull(physical, "the Demo has a Physical Damage pipeline");
            Assert.IsNotNull(magic, "and a Magic Damage pipeline");

            Assert.That(physical.Stages.Select(stage => stage.Label), Is.EqualTo(new[] { "Attack Amplification", "Armor" }), "Physical Damage's stages");
            Assert.That(magic.Stages.Select(stage => stage.Label), Is.EqualTo(new[] { "Magic Amplification", "Magic Resistance" }), "Magic Damage's stages");

            var amplified = Curve(physical.Stages[0]);
            Assert.That((amplified.Curve, amplified.Coefficient.Magnitude, amplified.Plus), Is.EqualTo((EStackingCurve.Linear, 1f, 1f)), "1 + the amplification");
            Read(amplified.Count, "Attack Amplification", ESourceTarget.Source);
            var armor = Curve(physical.Stages[1]);
            Assert.That((armor.Curve, armor.Coefficient.Magnitude, armor.Plus), Is.EqualTo((EStackingCurve.Reciprocal, 0.01f, 0f)), "100 ÷ (100 + Armor)");
            Read(armor.Count, "Armor", ESourceTarget.Target);

            Read(Curve(magic.Stages[0]).Count, "Magic Amplification", ESourceTarget.Source);
            var resisted = Curve(magic.Stages[1]);
            Assert.That((resisted.Curve, resisted.Coefficient.Magnitude, resisted.Plus), Is.EqualTo((EStackingCurve.Linear, -1f, 1f)), "1 − the resistance");
            var clamped = resisted.Count.Scaler as ClampedScaler;
            Assert.IsNotNull(clamped, "the resistance kept between bounds");
            Assert.That((clamped.MinValue.Magnitude, clamped.MaxValue.Magnitude), Is.EqualTo((0f, 1f)), "between 0 and 1");
            Read(clamped.Value, "Magic Resistance", ESourceTarget.Target);

            StackingCurveKind Curve(AbstractValueStage stage)
            {
                var by = (stage as MultiplyStage)?.By;
                Assert.IsNotNull(by, $"{stage?.Label} multiplies");
                Assert.AreEqual(EMagnitudeOperation.UseScaler, by.RealMagnitude, $"{stage.Label} reads its number");
                var curve = by.Scaler as StackingCurveKind;
                Assert.IsNotNull(curve, $"{stage.Label}, a stacking curve");
                return curve;
            }

            void Read(ScalableFloat number, string attribute, ESourceTarget from)
            {
                var read = number?.Scaler as AttributeBackedScaler;
                Assert.IsNotNull(read, $"a read of {attribute}");
                Assert.AreEqual(EMagnitudeOperation.UseScaler, number.RealMagnitude, $"{attribute} read as it is");
                Assert.AreEqual(attribute, read.CaptureAttribute != null ? read.CaptureAttribute.Name : null, "the attribute it reads");
                Assert.AreEqual(from, read.CaptureFrom, $"{attribute}, the {from}'s");
                Assert.AreEqual(EAttributeRead.AsIs, read.ReadAs, $"{attribute} as it is, so a missing one reads 0");
            }
        }

        /// <summary>The Demo's damage workers are gone from its attribute sets, the pipelines doing their work.</summary>
        [Test]
        public void FN_11_The_Demos_damage_workers_are_gone()
        {
            var folder = DemoFolder();
            var left = All<AttributeSet>(folder)
                .SelectMany(set => (set.WorkerGroup?.AttributeWorkers ?? new List<AbstractAttributeWorker>()).Where(worker => worker != null)
                    .Select(worker => (Set: set.name, Worker: worker.GetType().Name)))
                .Where(each => each.Worker is "MitigationWorker" or "AmplificationWorker")
                .Select(each => $"{each.Set}: {each.Worker}").ToList();
            Assert.That(left, Is.Empty, "the damage workers sets still carry");
        }
    }
}
