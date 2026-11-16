using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FarEmerald.PlayForge.Testing;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.AdditionalContent.Tests
{
    /// <summary>
    /// AC-2, Decision 69: the sample's feedback. Components on an entity show its own hits with nothing set per effect,
    /// the floating numbers, the hit flash, the hit sound and the status bar; the camera shaker serves PlayForge's Camera
    /// Shake cue; and the sample's cues add what one effect wants of its own. Played on the test kit, as a game's tests
    /// would.
    /// </summary>
    public class FeedbackTests : ForgeTest
    {
        private ForgeAttribute health;

        // A fixture lives across its tests, and each test's world destroys the assets it made
        [SetUp]
        public void ForgetAssets() => health = null;

        [TearDown]
        public void RemoveLayer()
        {
            foreach (var layer in Object.FindObjectsByType<FloatingTextLayer>(FindObjectsSortMode.None)) Object.Destroy(layer.gameObject);
            foreach (var shaker in Object.FindObjectsByType<CameraShaker>(FindObjectsSortMode.None)) Object.Destroy(shaker.gameObject);
        }

        private GameplayAbilitySystem Dummy(string name = "Dummy")
        {
            health ??= Make.Attribute("Health");
            return World.MonoEntity(Make.Set(Make.Pool(health, 100f, 100f)), name);
        }

        private GameplayEffect Change(float amount, string name = "Change") =>
            Make.Effect(name, new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = amount });

        private T On<T>(GameplayAbilitySystem dummy) where T : EntityFeedback
        {
            var feedback = dummy.gameObject.AddComponent<T>();
            feedback.Attribute = health;
            return feedback;
        }

        private static FloatingTextLayer.Word[] Words(string text) =>
            FloatingTextLayer.Current.Words.Where(w => w.Text == text).ToArray();

        private static bool Same(Color a, Color b) =>
            Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b);

        [UnityTest]
        public IEnumerator AC_2_Floating_numbers_float_each_change_an_entity_takes() => Live(async () =>
        {
            var dummy = Dummy();
            var foe = Dummy("Foe");
            var numbers = On<FloatingNumbers>(dummy);
            await Frames.Step();
            Assert.IsTrue(numbers.Listening, "it hears its entity once the entity's registered");

            Assert.IsTrue(World.Hit(foe, dummy, Change(-30f)));
            var loss = Words("30");
            Assert.AreEqual(1, loss.Length, "a loss of 30 floats as 30");
            Assert.IsTrue(Same(numbers.LossColor, loss[0].Color), "in the loss color");
            Assert.Greater(loss[0].Position.y, dummy.transform.position.y + 1f, "above the entity");

            Assert.IsTrue(World.Hit(foe, dummy, Change(12f)));
            var gain = Words("12");
            Assert.AreEqual(1, gain.Length, "a gain floats too");
            Assert.IsTrue(Same(numbers.GainColor, gain[0].Color), "in the gain color");
            Assert.AreEqual(2, numbers.Floated);

            numbers.ShowGains = false;
            Assert.IsTrue(World.Hit(foe, dummy, Change(5f)));
            Assert.IsEmpty(Words("5"), "with gains off, a gain floats nothing");

            float risen = loss[0].Position.y;
            await Frames.Step(10);
            Assert.Greater(Words("30")[0].Position.y, risen, "the number rises");
            await Frames.Seconds(numbers.Style.Lifetime);
            Assert.IsEmpty(Words("30"), "and goes after its lifetime");
        });

        [UnityTest]
        public IEnumerator AC_2_Floating_numbers_show_only_their_attribute() => Live(async () =>
        {
            var dummy = Dummy();
            var numbers = On<FloatingNumbers>(dummy);
            var mana = Make.Attribute("Mana");
            await Frames.Step();
            var drain = Make.Effect("Drain", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = mana, Magnitude = -7f });
            World.Hit(Dummy("Foe"), dummy, drain);
            Assert.AreEqual(0, numbers.Floated, "a change to another attribute floats nothing");
        });

        [UnityTest]
        public IEnumerator AC_2_A_critical_hit_floats_larger_and_marked() => Live(async () =>
        {
            var dummy = Dummy();
            var numbers = On<FloatingNumbers>(dummy);
            await Frames.Step();
            var pipeline = ScriptableObject.CreateInstance<ValuePipeline>();
            try
            {
                pipeline.Stages = new List<AbstractValueStage> { new ChanceStage { Chance = 1f, Multiplier = 2f } };
                var strike = Change(-10f, "Strike");
                strike.ImpactSpecification.Pipeline = pipeline;
                World.Hit(Dummy("Foe"), dummy, strike);
                var marked = Words("20" + numbers.CriticalMark);
                Assert.AreEqual(1, marked.Length, "a hit a Chance stage came up on floats its number, marked");
                Assert.IsTrue(Same(numbers.CriticalColor, marked[0].Color), "in the critical color");
                Assert.AreEqual(numbers.Style.Size * numbers.CriticalScale, marked[0].Size, 0.001f, "and larger");
                Assert.AreEqual(1, numbers.Criticals);

                pipeline.Stages[0] = new ChanceStage { Chance = 0f, Multiplier = 2f };
                World.Hit(Dummy("Foe"), dummy, strike);
                Assert.AreEqual(1, Words("10").Length, "one it didn't come up on floats plain");
                Assert.AreEqual(1, numbers.Criticals);
            }
            finally
            {
                Object.Destroy(pipeline);
            }
        });

        [UnityTest]
        public IEnumerator AC_2_The_status_bar_shows_each_effect_that_shows() => Live(async () =>
        {
            var dummy = Dummy();
            var bar = On<StatusBar>(dummy);
            var foe = Dummy("Foe");
            await Frames.Step();
            Assert.IsEmpty(bar.Icons, "with no effect, no icon");

            var slow = Make.Effect("Slow", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 1f });
            var cooldown = Make.Effect("Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 1f });
            cooldown.Definition.Visibility = Tags.NOT_VISIBLE;
            World.Hit(foe, dummy, slow);
            World.Hit(foe, dummy, cooldown);
            await Frames.Step();
            Assert.AreEqual(1, bar.Icons.Count, "an icon for the effect that shows, none for one marked Not Visible");
            var icon = bar.Icons[0];
            Assert.AreSame(slow, icon.Effect);
            Assert.IsNull(icon.Image, "it has no icon of its own");
            Assert.AreEqual("Sl", icon.Letters, "so it shows the first letters of its name");
            Assert.AreEqual(1, icon.Stacks);
            Assert.Greater(icon.TimeLeft, 0.9f, "and nearly all its time");
            var shown = dummy.transform.Find("Status Bar");
            Assert.IsNotNull(shown, "on a canvas of its own over the entity");
            Assert.AreEqual(RenderMode.WorldSpace, shown.GetComponent<Canvas>().renderMode);

            await Frames.Seconds(0.5f);
            Assert.Less(bar.Icons[0].TimeLeft, 0.6f, "its time runs down");
            World.Hit(foe, dummy, slow);
            await Frames.Step();
            Assert.AreEqual(1, bar.Icons.Count, "a second of the same effect shares its icon");
            Assert.AreEqual(2, bar.Icons[0].Stacks, "as a second stack");
            Assert.Greater(bar.Icons[0].TimeLeft, 0.9f, "showing the longest time");

            await Frames.Seconds(1.1f);
            Assert.IsEmpty(bar.Icons, "and goes with its effect");
            Assert.IsFalse(shown.gameObject.activeSelf, "the bar hides when it has nothing to show");
        });

        [UnityTest]
        public IEnumerator AC_2_A_hit_flash_tints_its_renderers_and_fades_back() => Live(async () =>
        {
            var dummy = Dummy();
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(dummy.transform, false);
            var renderer = body.GetComponent<Renderer>();
            var flash = On<HitFlash>(dummy);
            flash.Color = Color.red;
            await Frames.Step();

            Assert.IsTrue(World.Hit(Dummy("Foe"), dummy, Change(-10f)));
            Assert.IsTrue(flash.Flashing, "a loss flashes it");
            Assert.AreEqual(1, flash.Flashes);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            int property = Shader.PropertyToID(renderer.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
            Assert.IsTrue(block.HasColor(property), "through a property block");
            Assert.IsTrue(Same(Color.red, block.GetColor(property)), "in the flash's color");

            World.Hit(Dummy("Healer"), dummy, Change(10f));
            Assert.AreEqual(1, flash.Flashes, "a gain doesn't flash, unless it's set to");

            await Frames.Seconds(flash.Duration + 0.05f);
            Assert.IsFalse(flash.Flashing, "it fades back");
            renderer.GetPropertyBlock(block);
            Assert.IsFalse(block.HasColor(property), "and gives the renderer back its own block");
        });

        [UnityTest]
        public IEnumerator AC_2_A_hit_sound_plays_once_for_a_burst_of_losses() => Live(async () =>
        {
            var dummy = Dummy();
            var sound = On<HitSound>(dummy);
            var foe = Dummy("Foe");
            await Frames.Step();

            World.Hit(foe, dummy, Change(-5f));
            World.Hit(foe, dummy, Change(-5f));
            Assert.AreEqual(1, sound.Played, "two losses in one frame play once");
            Assert.IsNotNull(dummy.GetComponent<AudioSource>(), "through an audio source of its own");

            await Frames.Seconds(sound.MinInterval + 0.02f);
            World.Hit(foe, dummy, Change(-5f));
            Assert.AreEqual(2, sound.Played, "a loss after its interval plays again");
            World.Hit(foe, dummy, Change(20f));
            Assert.AreEqual(2, sound.Played, "a gain plays nothing, unless it's set to");
            Assert.IsNotNull(HitSound.Click, "with no clip, it plays a click made in code");
        });

        [UnityTest]
        public IEnumerator AC_2_The_camera_shaker_serves_camera_shake_cues() => Live(async () =>
        {
            var camera = new GameObject("Camera", typeof(Camera));
            var shaker = camera.AddComponent<CameraShaker>();
            Assert.AreSame(shaker, ForgeCues.Shaker, "enabled, it's the project's shaker");
            var dummy = Dummy();
            await Frames.Step();

            var quake = Change(-1f, "Quake");
            quake.Cues.Add(new CameraShakeCue { Strength = 1f, Duration = 0.3f });
            World.Hit(Dummy("Foe"), dummy, quake);
            Assert.IsTrue(shaker.Shaking, "PlayForge's Camera Shake cue shakes it");
            await Frames.Step();
            Assert.Greater(shaker.Strength, 0f);
            Assert.Greater(shaker.Offset.magnitude + Mathf.Abs(shaker.Angle), 0f, "off its place");

            shaker.Shake(0.2f, 1f);
            Assert.Greater(shaker.Strength, 0.5f, "a weaker shake during a stronger one leaves the stronger");

            await Frames.Seconds(0.35f);
            Assert.IsFalse(shaker.Shaking, "it ends with its duration");
            Assert.AreEqual(Vector3.zero, shaker.Offset, "back in its place");

            Object.Destroy(shaker);
            await Frames.Step();
            Assert.IsNull(ForgeCues.Shaker, "gone, it's the project's shaker no longer");
        });

        [UnityTest]
        public IEnumerator AC_2_A_floating_word_cue_floats_its_word_over_its_target() => Live(async () =>
        {
            var dummy = Dummy();
            On<FloatingNumbers>(dummy);
            await Frames.Step();
            var slow = Make.Effect("Slow", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 2f });
            slow.Cues.Add(new FloatingWordCue { Text = "Slowed", Color = Color.cyan });
            World.Hit(Dummy("Foe"), dummy, slow);
            var words = Words("Slowed");
            Assert.AreEqual(1, words.Length, "the word floats as the effect lands");
            Assert.IsTrue(Same(Color.cyan, words[0].Color), "in the cue's color");

            var named = Make.Effect("Rooted", new EffectConfig { Policy = EEffectDurationPolicy.Instant });
            named.Cues.Add(new FloatingWordCue());
            var bare = Dummy("Bare");
            World.Hit(Dummy("Foe"), bare, named);
            Assert.AreEqual(1, Words(named.GetName()).Length, "with no word, the effect's name, over an entity with no numbers too");
        });

        [UnityTest]
        public IEnumerator AC_2_A_flash_cue_flashes_its_target_in_its_color() => Live(async () =>
        {
            var dummy = Dummy();
            GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(dummy.transform, false);
            await Frames.Step();
            var freeze = Change(-1f, "Freeze");
            freeze.Cues.Add(new FlashCue { Color = Color.blue, Duration = 0.2f });
            World.Hit(Dummy("Foe"), dummy, freeze);

            var flash = dummy.GetComponentInChildren<HitFlash>();
            Assert.IsNotNull(flash, "an entity with no Hit Flash gets one");
            Assert.IsTrue(flash.Flashing);
            Assert.IsTrue(Same(Color.blue, flash.Current), "flashing in the cue's color");
            Assert.IsFalse(flash.OnLosses, "which flashes only as cues ask");

            await Frames.Seconds(0.25f);
            flash.Attribute = health;
            World.Hit(Dummy("Foe"), dummy, Change(-5f));
            Assert.AreEqual(1, flash.Flashes, "a plain loss doesn't flash it");
        });
    }
}
