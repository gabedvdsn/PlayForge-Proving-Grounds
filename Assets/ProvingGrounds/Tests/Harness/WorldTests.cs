using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class WorldTests : ProvingGroundsTest
    {
        [UnityTest]
        public IEnumerator The_Bootstrapper_brings_up_a_live_framework() => Live(async () =>
        {
            Assert.That(ProcessControl.Instance, Is.SameAs(World.Control));
            Assert.That(GameRoot.Instance, Is.SameAs(World.Root));
            Assert.That(World.Root.gameObject.scene.IsValid(), "GameRoot is the clone, not the template");
            Assert.That(World.Control.State, Is.EqualTo(EProcessControlState.Ready));
            Assert.That(World.Control.Active, Is.EqualTo(1), "GameRoot is the only process");

            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator Every_world_starts_from_nothing() => Live(async () =>
        {
            Assert.That(World.Control.Created, Is.EqualTo(1), "only GameRoot has ever registered here");
            Assert.That(AttributeRegistry.GetAll(), Has.Count.EqualTo(1), "only GameRoot's attribute is known");

            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator A_tag_does_not_outlive_its_world([Values(1, 2)] int run) => Live(async () =>
        {
            Assert.That(TagRegistry.Exists("Proving.Leftover"), Is.False, "whichever run comes second proves it");

            Tag.Generate("Proving.Leftover");

            Assert.That(TagRegistry.Exists("Proving.Leftover"));
            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator ProcessControl_steps_a_process_once_per_frame_in_each_timing() => Live(async () =>
        {
            var probe = World.Probe("Stepped", EProcessLifecycle.Synchronous, EProcessStepTiming.UpdateFixedAndLate);

            await Frames.Step(5);

            Assert.That(probe.Recorder[ProbeHook.Update], Is.EqualTo(5));
            Assert.That(probe.Recorder[ProbeHook.FixedUpdate], Is.EqualTo(5));
            Assert.That(probe.Recorder[ProbeHook.LateUpdate], Is.EqualTo(5));
        });

        [UnityTest]
        public IEnumerator A_run_of_N_frames_ends_in_exactly_N_frames() => Live(async () =>
        {
            var probe = World.Probe("Timed", runFrames: 3);

            await Frames.ExactlyIn(3, () => probe.Relay.State == EProcessState.Terminated, "the run ends");

            Assert.That(probe.Recorder.RunsCompleted, Is.EqualTo(1));
            Assert.That(World.Control.IsRegistered(probe.Relay.CacheIndex), Is.False, "its block is gone");
        });

        [UnityTest]
        public IEnumerator A_scene_process_runs_in_place() => Live(async () =>
        {
            var probe = World.MonoProbe("In Scene", runFrames: 2);

            await Frames.Until(() => probe == null, 5, "its object is destroyed");
        });

        [UnityTest]
        public IEnumerator A_template_is_cloned_like_a_prefab() => Live(async () =>
        {
            var template = World.MonoProbeTemplate("Template", EProcessLifecycle.Synchronous);

            Assert.That(template.gameObject.scene.IsValid(), Is.False, "outside any scene, as a prefab is");

            var clone = World.Spawn(template);
            await Frames.Step(2);

            Assert.That(clone, Is.Not.SameAs(template));
            Assert.That(clone.Recorder[ProbeHook.Update], Is.EqualTo(2), "the clone runs");
            Assert.That(template.Recorder, Is.Null, "the template does not");
        });

        [UnityTest]
        public IEnumerator A_runtime_GAS_runs_its_end_of_frame() => Live(async () =>
        {
            var gas = World.RuntimeEntity();
            int frames = 0;
            gas.Callbacks.OnFrameComplete += _ => frames++;

            await Frames.Step(4);

            Assert.That(frames, Is.EqualTo(4));
        });

        [UnityTest]
        public IEnumerator A_mono_GAS_runs_its_end_of_frame() => Live(async () =>
        {
            var gas = World.MonoEntity();
            int frames = 0;
            gas.Callbacks.OnFrameComplete += _ => frames++;

            await Frames.Step(4);

            Assert.That(frames, Is.EqualTo(4));
        });

        [UnityTest]
        public IEnumerator An_effect_lands_on_a_live_entity() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.RuntimeEntity(Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase)));
            var hit = Make.Effect("Hit", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -30f, Slot = EAttributeTarget.Current,
            });

            Assert.That(World.Apply(gas, gas, hit), "applied");
            await Frames.Step();

            Assert.That(gas.GetAttributeSystem().TryGetValue(health, out var value), "the attribute is defined");
            Assert.That(value.CurrentValue, Is.EqualTo(70f).Within(0.001f));
        });

        [UnityTest]
        public IEnumerator An_ability_runs_its_stages_under_ProcessControl() => Live(async () =>
        {
            var first = new ProbeAbilityTask(World, "First", frames: 2);
            var second = new ProbeAbilityTask(World, "Second", frames: 1);
            var ability = Make.Ability("Combo", Make.Stage(first), Make.Stage(second));

            var gas = World.MonoEntity();
            var abilities = gas.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "granted");
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "activated");

            await Frames.Until(() => Ledger.Cleans("Second") == 1, 30, "the last stage is cleaned");

            Assert.That(Ledger.Prepares("First"), Is.EqualTo(1));
            Assert.That(Ledger.Cleans("First"), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator An_added_scene_is_unloaded_with_the_world() => Live(async () =>
        {
            var scene = World.AddScene("Side");

            Assert.That(scene.isLoaded);
            await Frames.Step();
        });

        [UnityTest]
        public IEnumerator A_scene_change_keeps_ProcessControl_stepping() => Live(async () =>
        {
            var probe = World.Probe("Survivor", EProcessLifecycle.Synchronous);
            await Frames.Step();

            await World.ChangeScene();
            int before = probe.Recorder[ProbeHook.Update];
            await Frames.Step(2);

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Empty"));
            Assert.That(World.Control != null, "ProcessControl is DontDestroyOnLoad");
            Assert.That(probe.Recorder[ProbeHook.Update] - before, Is.EqualTo(2));
        });
    }
}
