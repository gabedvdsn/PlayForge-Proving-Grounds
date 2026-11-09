using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// What an ability spawns: it takes the ability as its origin (GAS-65), and it carries attributes of its own, as a
    /// projectile's speed (FEAT-24, Decision 39).
    /// </summary>
    public class SpawnedProcessTests : ProvingGroundsTest
    {
        /// <summary>A process that keeps the origin it took, and ends a frame after it starts.</summary>
        public sealed class OriginProbe : AbstractGameplayMonoProcess
        {
            public static OriginProbe Last;
            public IEffectOrigin TakenOrigin => Origin;

            public override void WhenInitialize()
            {
                base.WhenInitialize();
                Last = this;
            }

            public override async UniTask RunProcess(CancellationToken token) => await ForgeTick.NextFrame(token);
        }

        private OriginProbe ProbeTemplate()
        {
            OriginProbe.Last = null;
            var probe = World.Template<OriginProbe>("Probe");
            probe.ProcessTiming = EProcessStepTiming.None;
            return probe;
        }

        private static void Activate(IGameplayAbilitySystem caster, Ability ability, int level = 1)
        {
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, level, out int index), "the ability is granted");
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index)), "and activates");
        }

        /// <summary>
        /// A process an ability spawned fell back to GameRoot as its origin, since nothing passed it the ability, so a
        /// projectile read GameRoot's speed and applied GameRoot's effects (GAS-65).
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_65_A_spawned_process_takes_its_ability_as_its_origin([Values] Hub hub) => Live(async () =>
        {
            var caster = World.Entity(hub, name: "Caster");
            var ability = Make.Ability("Spawn", Make.Stage(new CreateMonoProcessAbilityTask
            {
                InternalIsOverride = true,
                InternalMonoProcesses = new List<AbstractMonoProcess> { ProbeTemplate() },
            }));
            Activate(caster, ability);
            await Frames.Until(() => OriginProbe.Last, 5, "the process spawns");

            var origin = OriginProbe.Last.TakenOrigin;
            Assert.That(origin, Is.InstanceOf<AbilitySpec>(), "it takes the ability as its origin");
            Assert.That(origin.GetSource() as IGameplayAbilitySystem, Is.SameAs(caster), "whose source is the caster, not GameRoot");
        });

        /// <summary>
        /// The task spawned its own processes only with Internal Is Override on, so a task given processes and nothing
        /// else spawned none (GAS-65). With none in the data, it spawns its own.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_65_A_task_spawns_its_own_processes_when_the_data_holds_none() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            var ability = Make.Ability("Spawn", Make.Stage(new CreateMonoProcessAbilityTask
            {
                InternalMonoProcesses = new List<AbstractMonoProcess> { ProbeTemplate() },
            }));
            Activate(caster, ability);
            await Frames.Until(() => OriginProbe.Last, 5, "the task's own process spawns");
        });

        /// <summary>
        /// A process the task spawned appeared where its prefab stood unless the data held a Position, so a projectile
        /// left the world's origin (GAS-67). Spawn At puts it at the caster or at the ability's target, and leaves the
        /// ability's data as it was.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_67_A_spawned_process_appears_at_the_caster_or_the_target(
            [Values(CreateMonoProcessAbilityTask.ESpawnPoint.Caster, CreateMonoProcessAbilityTask.ESpawnPoint.Target)]
            CreateMonoProcessAbilityTask.ESpawnPoint at) => Live(async () =>
        {
            var caster = World.MonoEntity(name: "Caster");
            caster.transform.position = new Vector3(5f, 0f, 0f);
            var target = World.MonoEntity(name: "Target");
            target.transform.position = new Vector3(0f, 0f, 9f);

            var ability = Make.Ability("Spawn", Make.Stage(new CreateMonoProcessAbilityTask
            {
                SpawnAt = at,
                InternalMonoProcesses = new List<AbstractMonoProcess> { ProbeTemplate() },
            }));
            ability.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "the ability is granted");
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, data)), "and activates at its target");
            await Frames.Until(() => OriginProbe.Last, 5, "the process spawns");

            var expected = at == CreateMonoProcessAbilityTask.ESpawnPoint.Caster ? caster.transform.position : target.transform.position;
            Assert.That(Vector3.Distance(OriginProbe.Last.transform.position, expected), Is.LessThan(0.01f), "where Spawn At says");
            Assert.IsFalse(OriginProbe.Last.Data.Contains(Tags.POSITION), "and the ability's data keeps no Position of it");
        });

        /// <summary>
        /// A projectile's effects took none of the set-by-caller magnitudes its ability's caller set, though the ability's
        /// other effects take them, so the magnitude was lost on the way (GAS-68). They take them.
        /// </summary>
        [UnityTest]
        public IEnumerator GAS_68_A_projectiles_effects_take_its_abilitys_set_by_caller_magnitudes() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.MonoEntity(name: "Caster");
            var target = World.MonoEntity(Make.Set(Make.Element(health, 100f, 100f)), "Target");
            target.transform.position = new Vector3(0f, 0f, 3f);

            var power = Make.NewTag("Test.Power");
            var hit = Make.Effect("Hit", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health,
                MagnitudeScaler = new SetByCallerScaler { Key = power, Default = -1f }, MagnitudeOp = EMagnitudeOperation.UseScaler,
            });
            var arrow = World.Template<ProjectileMonoProcess>("Arrow");
            arrow.ProcessTiming = EProcessStepTiming.None;
            arrow.Effects = new List<GameplayEffect> { hit };
            arrow.Attributes = new List<ProcessAttribute> { Speed(200f) };

            var ability = Make.Ability("Shoot", Make.Stage(new CreateMonoProcessAbilityTask
            {
                SpawnAt = CreateMonoProcessAbilityTask.ESpawnPoint.Caster,
                InternalMonoProcesses = new List<AbstractMonoProcess> { arrow },
            }));
            ability.Behaviour.Targeting = new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL };
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, 1, out int index), "the ability is granted");
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            data.SetByCaller(power, -30f);
            Assert.That(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, data)), "and shoots");

            await Frames.Until(() => target.TryGetAttributeValue(health, out var now) && now.CurrentValue < 100f, 20, "the arrow lands");
            target.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(70f).Within(1e-3f), "for the magnitude its caller set, not the default");
        });

        /// <summary>
        /// A process's own attributes reach the effects it applies, each under its role as a set-by-caller magnitude, so an
        /// effect can scale by what the process carries, as a fire's burn by its heat. Its own value wins over the one its
        /// caller set, as the nearer. Only its caller's reached them, so what it carried stopped at the process (FEAT-28).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_28_A_processs_attributes_reach_the_effects_it_applies() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.MonoEntity(name: "Caster");
            var target = World.MonoEntity(Make.Set(Make.Element(health, 100f, 100f)), "Target");
            target.transform.position = new Vector3(0f, 0f, 3f);

            var heat = Make.NewTag("Test.Heat");
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health,
                MagnitudeScaler = new SetByCallerScaler { Key = heat, Default = -1f }, MagnitudeOp = EMagnitudeOperation.UseScaler,
            });
            var arrow = World.Template<ProjectileMonoProcess>("Arrow");
            arrow.ProcessTiming = EProcessStepTiming.None;
            arrow.Effects = new List<GameplayEffect> { burn };
            arrow.Attributes = new List<ProcessAttribute>
            {
                Speed(200f),
                new() { Role = heat, StartsFrom = EProcessAttributeStart.Own, Value = new ScalableFloat { Magnitude = -25f, RealMagnitude = EMagnitudeOperation.UseMagnitude } },
            };

            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            data.SetPrimary(Tags.DERIVATION, IEffectOrigin.GenerateSourceDerivation(caster));
            data.SetByCaller(heat, -5f);
            World.Spawn(arrow, data);

            await Frames.Until(() => target.TryGetAttributeValue(health, out var now) && now.CurrentValue < 100f, 20, "the arrow lands");
            target.TryGetAttributeValue(health, out var value);
            Assert.That(value.CurrentValue, Is.EqualTo(75f).Within(1e-3f), "for the heat it carries, over its caller's");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // A projectile's own attributes (FEAT-24)
        // ═══════════════════════════════════════════════════════════════════════════

        private const float Far = 1000f;

        // An arrow whose speed is its own attribute, in the ProjectileSpeed role
        private ProjectileMonoProcess Arrow(ProcessAttribute speed)
        {
            var arrow = World.Template<ProjectileMonoProcess>("Arrow");
            arrow.ProcessTiming = EProcessStepTiming.None;
            arrow.Effects = new List<GameplayEffect>();
            arrow.Attributes = new List<ProcessAttribute> { speed };
            return arrow;
        }

        private static ProcessAttribute Speed(float value, EProcessAttributeStart from = EProcessAttributeStart.Own, Attribute attribute = null, AbstractScaler scaler = null)
        {
            return new ProcessAttribute
            {
                Attribute = attribute,
                Role = Tags.PROJECTILE_SPEED,
                StartsFrom = from,
                Value = new ScalableFloat
                {
                    Magnitude = value,
                    RealMagnitude = scaler == null ? EMagnitudeOperation.UseMagnitude : EMagnitudeOperation.UseScaler,
                    Scaler = scaler,
                },
            };
        }

        // Fires an arrow from the caster's origin at a target far off, and returns it
        private ProjectileMonoProcess Fire(ProjectileMonoProcess template, IEffectOrigin origin, ProcessDataPacket data = null)
        {
            var target = World.MonoEntity(name: "Target");
            target.transform.position = new Vector3(0f, 0f, Far);
            data ??= ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            data.SetPrimary(Tags.DERIVATION, origin);
            return World.Spawn(template, data);
        }

        // How far an arrow at a speed gets in a number of frames, within a frame's travel
        private void AssertFlewAt(ProjectileMonoProcess arrow, float speed, int frames, string what)
        {
            float step = speed * Frames.DeltaTime;
            Assert.That(arrow.transform.position.z, Is.InRange(step * (frames - 1) - 0.01f, step * (frames + 1) + 0.01f), what);
        }

        /// <summary>
        /// A projectile read its speed from its caster's attribute alone, and failed where the caster had none
        /// (FEAT-24). It flies at the speed it carries.
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_24_A_projectile_flies_at_the_speed_it_carries() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            var arrow = Fire(Arrow(Speed(20f)), IEffectOrigin.GenerateSourceDerivation(caster));
            await Frames.Step(10);
            AssertFlewAt(arrow, 20f, 10, "it flies at its own speed, though its caster has none");
        });

        /// <summary>
        /// A process attribute can start as its origin's, as a projectile at its caster's Shot Speed, and with the
        /// origin's, else its own, at its own where the caster has none (FEAT-24).
        /// </summary>
        [UnityTest]
        public IEnumerator FEAT_24_A_process_attribute_can_start_as_its_origins() => Live(async () =>
        {
            var shotSpeed = Make.Attribute("Shot Speed");
            var archer = World.Entity(Hub.Runtime, Make.Set(Make.Element(shotSpeed, 30f, 30f)), "Archer");
            var mage = World.Entity(Hub.Runtime, name: "Mage");

            var fromArcher = Fire(Arrow(Speed(10f, EProcessAttributeStart.Origin, shotSpeed)), IEffectOrigin.GenerateSourceDerivation(archer));
            var fallsBack = Fire(Arrow(Speed(10f, EProcessAttributeStart.OriginElseOwn, shotSpeed)), IEffectOrigin.GenerateSourceDerivation(mage));
            await Frames.Step(10);
            AssertFlewAt(fromArcher, 30f, 10, "it starts at its archer's Shot Speed");
            AssertFlewAt(fallsBack, 10f, 10, "and at its own where the caster has none");
        });

        /// <summary>A process attribute's scaler reads the level of what spawned it (FEAT-24).</summary>
        [UnityTest]
        public IEnumerator FEAT_24_A_process_attribute_reads_its_scaler_at_the_level_that_spawned_it() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            // Per level, as the inspector makes a new scaler
            var byLevel = new SimpleScaler { LevelValues = new[] { 10f, 20f, 30f, 40f }, MaxLevel = 4, StretchOverLevels = false };
            var origin = IEffectOrigin.GenerateLevelerDerivation(caster, new IntValuePairClamped(3, 1, 4));
            var arrow = Fire(Arrow(Speed(0f, scaler: byLevel)), origin);
            await Frames.Step(10);
            AssertFlewAt(arrow, 30f, 10, "at level 3 of 4, it flies at the scaler's third value");
        });

        /// <summary>What spawns a process can set its attribute in its data, under the attribute's role (FEAT-24).</summary>
        [UnityTest]
        public IEnumerator FEAT_24_What_spawns_a_process_can_set_its_attribute() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.PROJECTILE_SPEED, 50f);
            var arrow = Fire(Arrow(Speed(20f)), IEffectOrigin.GenerateSourceDerivation(caster), data);
            await Frames.Step(10);
            AssertFlewAt(arrow, 50f, 10, "the data's speed comes before its own");
        });

        /// <summary>A process's code changes its attribute as it runs, and hears that it changed (FEAT-24).</summary>
        [UnityTest]
        public IEnumerator FEAT_24_A_process_attribute_changes_as_the_process_runs() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            var arrow = Fire(Arrow(Speed(20f)), IEffectOrigin.GenerateSourceDerivation(caster));
            var heard = new List<float>();
            arrow.AttributeChanged += (_, value) => heard.Add(value);
            await Frames.Step(5);

            float before = arrow.transform.position.z;
            Assert.That(arrow.SetRole(Tags.PROJECTILE_SPEED, 60f), "it carries a speed to set");
            Assert.That(arrow.TryGetRole(Tags.PROJECTILE_SPEED, out var now) && Mathf.Approximately(now, 60f), "which reads as set");
            await Frames.Step(5);
            float step = 60f * Frames.DeltaTime;
            Assert.That(arrow.transform.position.z - before, Is.InRange(step * 4 - 0.01f, step * 6 + 0.01f), "it flies on at the new speed");
            Assert.That(heard, Is.EqualTo(new[] { 60f }), "and its listeners hear of it once");
        });

        /// <summary>A role with no own attribute still reads the origin's attribute its pair names, as the labs' shots do.</summary>
        [UnityTest]
        public IEnumerator FEAT_24_A_role_with_no_own_attribute_reads_the_origins() => Live(async () =>
        {
            var shotSpeed = Make.Attribute("Shot Speed");
            var archer = World.Entity(Hub.Runtime, Make.Set(Make.Element(shotSpeed, 25f, 25f)), "Archer");
            var template = World.Template<ProjectileMonoProcess>("Arrow");
            template.ProcessTiming = EProcessStepTiming.None;
            template.Effects = new List<GameplayEffect>();
            template.AttributePairs = new List<TagAttributePair> { new(Tags.PROJECTILE_SPEED, shotSpeed) };
            var arrow = Fire(template, IEffectOrigin.GenerateSourceDerivation(archer));
            await Frames.Step(10);
            AssertFlewAt(arrow, 25f, 10, "it flies at its archer's Shot Speed");
        });
    }
}
