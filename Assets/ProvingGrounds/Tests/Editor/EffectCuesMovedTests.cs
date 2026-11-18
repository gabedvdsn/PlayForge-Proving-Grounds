using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// An effect keeps no cues of its own (EP-61): its Cues component plays them, a list for each moment, so the handlers'
    /// moment flags go too, and the samples' effects are saved so, re-authored, not migrated (Decision 83).
    /// </summary>
    public class EffectCuesMovedTests
    {
        /// <summary>An effect has no Cues list of its own, and no handler a flag for a moment its list says (EP-61).</summary>
        [Test]
        public void EP_61_An_effect_keeps_no_cues_of_its_own()
        {
            Assert.IsNull(typeof(GameplayEffect).GetField("Cues"), "an effect's cues are its Cues component's");
            Assert.IsNull(typeof(PrefabCue).GetField("WhileActive"), "a Prefab cue lasts where it's listed to last");
            Assert.IsNull(typeof(SoundCue).GetField("OnEachTick"), "a Sound cue plays each tick where it's listed for ticks");
            Assert.IsNull(typeof(CameraShakeCue).GetField("OnEachTick"), "and so does a Camera Shake cue");
        }

        /// <summary>
        /// The samples' effects play their cues from their Cues component, and keep no field the code no longer has: an
        /// effect's own list, a handler's moment flag, or a template's snapshot of the old list (EP-61).
        /// </summary>
        [Test]
        public void EP_61_The_sample_effects_play_their_cues_from_their_component()
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.faremerald.playforge/package.json");
            Assert.IsNotNull(package, "PlayForge is installed as a package");
            var samples = Path.Combine(package.resolvedPath, "Samples~");
            Assert.That(Directory.Exists(samples), Is.True, "with its samples");
            var assets = Directory.GetFiles(samples, "*.asset", SearchOption.AllDirectories);

            var stale = new Regex(@"^(  Cues:|\s+(WhileActive|OnEachTick): [01]\s*$|  - PropertyPath: Cues\s*$)", RegexOptions.Multiline);
            var held = assets.Where(path => stale.IsMatch(File.ReadAllText(path)))
                .Select(path => Path.GetRelativePath(samples, path)).ToList();
            Assert.That(held, Is.Empty, $"{held.Count} sample assets keep the old cues, as {held.FirstOrDefault()}");

            int components = assets.Count(path => File.ReadAllText(path).Contains("class: CuesEffectComponent"));
            Assert.That(components, Is.GreaterThanOrEqualTo(36), "each effect that had cues plays them from its component");
        }
    }
}
