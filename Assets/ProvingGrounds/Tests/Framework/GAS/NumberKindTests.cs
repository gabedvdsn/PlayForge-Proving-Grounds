using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>An object whose numbers read one another, as a Clamped scaler's Max can read its Min (NUM-9).</summary>
    public class RelativeOwner
    {
        public ScalableFloat Min = new(new SimpleScaler { LevelValues = new[] { 10f, 20f }, StretchOverLevels = false }, 0f, EMagnitudeOperation.UseScaler);
        public ScalableFloat Max = new(new RelativeToFieldKind { Field = nameof(Min) }, 2f);
        public float Plain = 7f;
        public ScalableFloat PlusPlain = new(new RelativeToFieldKind { Field = nameof(Plain) }, 3f, EMagnitudeOperation.AddScaler);
        public ScalableInt Count = 4;
        public ScalableFloat OfCount = new(new RelativeToFieldKind { Field = nameof(Count) }, 0.5f);
        public ScalableFloat Gone = new(new RelativeToFieldKind { Field = "NoSuchField" }, 6f);
        public ScalableFloat LoopA = new(new RelativeToFieldKind { Field = nameof(LoopB) }, 1f);
        public ScalableFloat LoopB = new(new RelativeToFieldKind { Field = nameof(LoopA) }, 1f);
    }

    /// <summary>
    /// A curse's worker (NUM-12): it notes its target's health as the curse lands and counts the curse's ticks, and on
    /// every Nth tick answers a share of the health the target has lost since, from every source. On the other ticks it
    /// answers 0. What it keeps is kept for each application of the curse.
    /// </summary>
    public class MaledictWorker : AbstractEffectWorker, INumberWorker
    {
        public Attribute Health;
        public Tag Key;
        public float Share = 0.25f;
        public int Every = 4;

        public class State
        {
            public float HealthAtStart;
            public int Ticks;
        }

        public override IEnumerable<IRootAction> OnEffectApplication(EffectWorkerContext ctx)
        {
            var state = ctx.GetState<State>(this);
            if (state != null && ctx.System.TryGetAttributeValue(Health, out var health)) state.HealthAtStart = health.CurrentValue;
            return base.OnEffectApplication(ctx);
        }

        public override IEnumerable<IRootAction> OnEffectTick(EffectWorkerContext ctx)
        {
            var state = ctx.ExecuteTicks > 0 ? ctx.GetState<State>(this) : null;
            if (state != null) state.Ticks += ctx.ExecuteTicks;
            return base.OnEffectTick(ctx);
        }

        public bool TryGetNumber(Tag key, in NumberContext context, out float value)
        {
            value = 0f;
            if (key != Key) return false;
            var state = (context.Carrier as GameplayEffectSpec)?.GetWorkerState<State>(this);
            if (state == null) return false;
            if (state.Ticks > 0 && state.Ticks % Every == 0) value = Share * (state.HealthAtStart - context.Attribute(NumberSide.Target, Health));
            return true;
        }
    }

    /// <summary>An entity's worker, one its worker group gives it, that answers a number under a key (NUM-12).</summary>
    public class AnsweringTagWorker : AbstractTagWorker, INumberWorker
    {
        public Tag Key;
        public float Value;

        public bool TryGetNumber(Tag key, in NumberContext context, out float value)
        {
            value = Value;
            return key == Key;
        }
    }

    /// <summary>A worker of a project's own, which code gives an entity, that answers a number under a key (NUM-12).</summary>
    public class AnsweringWorker : INumberWorker
    {
        public Tag Key;
        public float Value;

        public bool TryGetNumber(Tag key, in NumberContext context, out float value)
        {
            value = Value;
            return key == Key;
        }
    }

    /// <summary>
    /// A scaler that isn't a kind and holds another, as a project's own scaler written before kinds can: it reads its
    /// inner scaler against the carrier it's given (NUM-16).
    /// </summary>
    [System.Serializable]
    public class HoldingScaler : AbstractScaler
    {
        [SerializeReference] public AbstractScaler Inner;

        public override void Initialize(IAttributeImpactCarrier deriv) => Inner?.Initialize(deriv);
        public override float Evaluate(IAttributeImpactCarrier deriv) => Inner?.Evaluate(deriv) ?? 0f;
        public override EScalerParts Parts => EScalerParts.None;
    }

    /// <summary>
    /// FEAT-29 phase 2: the new kinds of number, each written on the public base a project's own kinds use: Local Data,
    /// Relative to a field, Tracked Impact, Group Impact, Worker and Asset Part; and the Group, a kind like them.
    /// </summary>
    public class NumberKindTests : ProvingGroundsTest
    {
        private static float Health(IGameplayAbilitySystem gas, Attribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        private static DataWrapper FloatData(Tag key, float value) => new() { Key = key, Type = EDataWrapperType.Float, floatValue = value };

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-8: Local Data
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Local Data number reads a value under a key in its ability's Local Data, in a task as it starts and in an
        /// effect the ability applies, and its default where the key holds nothing (NUM-8).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_8_Local_Data_reads_the_abilitys_value([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            var range = Make.NewTag("Data.Range");
            var power = Make.NewTag("Data.Power");
            var missing = Make.NewTag("Data.Missing");

            var blow = Make.Effect("Blow", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -1f,
                MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = new LocalDataKind { Key = power, Default = 1f },
            });
            ReadNumberTask.Read.Clear();
            var strike = Make.Ability("Strike", Make.Stage(
                new ReadNumberTask { Number = new ScalableFloat(new LocalDataKind { Key = range, Default = 2f }, 1f) },
                new ReadNumberTask { Number = new ScalableFloat(new LocalDataKind { Key = missing, Default = 3f }, 1f) },
                new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { blow } }));
            strike.LocalData.Add(FloatData(range, 12f));
            strike.LocalData.Add(FloatData(power, 25f));
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(strike, 1, out int index), "the caster knows Strike");
            await Frames.Step();

            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            Assert.IsTrue(abilities.TryActivateAbility(abilities.CreateActivationRequest(index, data)), "Strike activates");
            await Frames.Until(() => !abilities.IsExecuting, 10, "and ends");

            Assert.AreEqual(new List<float> { 12f, 3f }, ReadNumberTask.Read, "its task read its range, and the default where it has none");
            Assert.AreEqual(75f, Health(target, health), "its effect read the ability's power, 25");
        });

        /// <summary>
        /// A Local Data number reads an entity's own Local Data too: the caster's or the target's, as it holds it at runtime,
        /// and an entity's own for a number read in its context (NUM-8).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_8_Local_Data_reads_an_entitys_value([Values] Hub hub) => Live(async () =>
        {
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            var reach = Make.NewTag("Data.Reach");
            caster.SetLocalData(reach, FloatData(reach, 4f));
            target.SetLocalData(reach, FloatData(reach, 9f));
            await Frames.Step();

            var ofCaster = new ScalableFloat(new LocalDataKind { Key = reach, From = ELocalDataFrom.Caster }, 1f);
            var ofTarget = new ScalableFloat(new LocalDataKind { Key = reach, From = ELocalDataFrom.Target }, 1f);
            var own = new ScalableFloat(new LocalDataKind { Key = reach }, 1f);
            var context = NumberContext.For(caster, target, 1);
            Assert.AreEqual(4f, ofCaster.Evaluate(context), "the caster's");
            Assert.AreEqual(9f, ofTarget.Evaluate(context), "the target's");
            Assert.AreEqual(4f, own.Evaluate(NumberContext.Of(caster)), "an entity's own, where the number is the entity's");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-9: Relative to a field
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A number relative to another field of its object reads that field, times or plus its own number, through the
        /// owner its object's code gives it: a Max as 2 × a Min that scales by level, a plain float plus 3, half a whole
        /// number (NUM-9).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_9_A_number_relative_to_another_field_follows_it() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            await Frames.Step();
            var owner = new RelativeOwner();

            Assert.AreEqual(20f, owner.Max.Evaluate(NumberContext.For(caster, null, 1), owner), "2 × Min at level 1, 10");
            Assert.AreEqual(40f, owner.Max.Evaluate(NumberContext.For(caster, null, 2), owner), "and at level 2, 20");
            Assert.AreEqual(10f, owner.PlusPlain.Evaluate(NumberContext.For(caster, null, 1), owner), "a plain float, 7, plus 3");
            Assert.AreEqual(2f, owner.OfCount.Evaluate(NumberContext.For(caster, null, 1), owner), "half a whole number, 4");
        });

        /// <summary>
        /// Where it can't read its field, a relative number reads its own number: with no owner given, with a field name
        /// its object no longer has, and in a loop of numbers that read each other, which stops (NUM-9).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_9_A_relative_number_that_cant_read_its_field_reads_its_number() => Live(async () =>
        {
            var caster = World.Entity(Hub.Runtime, name: "Caster");
            await Frames.Step();
            var owner = new RelativeOwner();
            var context = NumberContext.For(caster, null, 1);

            Assert.AreEqual(2f, owner.Max.Evaluate(context), "with no owner, its number");
            Assert.AreEqual(6f, owner.Gone.Evaluate(context, owner), "with a field that's gone, its number");
            float loop = owner.LoopA.Evaluate(context, owner);
            Assert.AreEqual(1f, loop, "a loop stops, and reads a number");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-16: a kind inside a plain scaler
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A kind inside a scaler that isn't one reads the context its number was read in, its owner among it, where the
        /// context has an effect's carrier as where it has none. The scaler was read through the carrier, so the kind
        /// built its context from the carrier alone, which has no owner, and couldn't be worked out (NUM-16).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_16_A_kind_inside_a_plain_scaler_keeps_its_context([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)));
            var spec = gas.GenerateEffectSpec(IEffectOrigin.GenerateSourceDerivation(gas), Make.Effect("Hit", new EffectConfig { Target = health, Magnitude = -1f }));
            await Frames.Step();
            var owner = new RelativeOwner();
            var held = new ScalableFloat(new HoldingScaler { Inner = new RelativeToFieldKind { Field = nameof(RelativeOwner.Plain) } }, 2f);

            Assert.AreEqual(14f, held.Evaluate(NumberContext.For(gas, null, 1), owner), "2 × its owner's Plain, 7, with no carrier");
            Assert.AreEqual(14f, held.Evaluate(NumberContext.Of(spec), owner), "and with an effect's");

            // What is read apart from that number, for the same effect, reads the effect's context alone
            var apart = new ScalableFloat(new RelativeToFieldKind { Field = nameof(RelativeOwner.Plain) }, 2f);
            Assert.AreEqual(2f, apart.Evaluate(NumberContext.Of(spec)), "a number read with no owner has none");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-10: Tracked Impact
        // ═══════════════════════════════════════════════════════════════════════════

        // −8 × (1 + 0.25 × this effect's count): a burn that grows each tick
        private static AbstractScaler GrowingByCount()
        {
            var quarterCount = new ScalerGroup();
            quarterCount.AddMember(new ConstantScaler { Value = 0.25f }, ECalculationOperation.Add);
            quarterCount.AddMember(new TrackedImpactKind { What = ETrackedImpactPart.Count }, ECalculationOperation.Multiply);
            var grows = new ScalerGroup();
            grows.AddMember(new ConstantScaler { Value = 1f }, ECalculationOperation.Add);
            grows.AddMember(quarterCount, ECalculationOperation.Add);
            return grows;
        }

        /// <summary>
        /// A periodic burn's magnitude can read its own impact count, so each tick burns more: −8, then −10, then −12
        /// (NUM-10).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_10_A_burn_grows_with_its_own_count([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var gas = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)));
            var burn = Make.Effect("Growing Burn", new EffectConfig
            {
                Target = health, Magnitude = -8f, MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler,
                MagnitudeScaler = GrowingByCount(), PeriodicTicks = true, FixedDeltaTime = 1f,
            });
            Assert.IsTrue(World.Apply(gas, gas, burn), "the burn lands");
            Assert.IsTrue(gas.TryGetEffectContainer(burn, out var container), "it's on the shelf");
            await Frames.Until(() => container.Spec.TrackedImpact.Count >= 3, 20, "it ticks three times");

            int ticks = container.Spec.TrackedImpact.Count;
            float burned = 8f * ticks + ticks * (ticks - 1);
            Assert.AreEqual(1000f - burned, Health(gas, health), 0.01f, $"each of its {ticks} ticks burned 2 more than the one before");
        });

        /// <summary>
        /// A number can read what an effect on the target has dealt, by its asset, its containers summed, as a combustion
        /// consumes a burn's total, by its size; or every effect there tagged with a tag, summed. An effect that isn't there
        /// reads 0 (NUM-10).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_10_A_number_reads_what_effects_on_the_target_dealt([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)), "Target");
            var fire = Make.NewTag("Test.Fire");
            var burning = Make.Effect("Burning", new EffectConfig { Target = health, Magnitude = -5f, PeriodicTicks = true, FixedDeltaTime = 1f });
            var scorched = Make.Effect("Scorched", new EffectConfig { Target = health, Magnitude = -2f, PeriodicTicks = true, FixedDeltaTime = 1f });
            var frozen = Make.Effect("Frozen", new EffectConfig { Target = health, Magnitude = -1f });
            burning.Tags.ContextTags.Add(fire);
            scorched.Tags.ContextTags.Add(fire);
            Assert.IsTrue(World.Apply(caster, target, burning) && World.Apply(caster, target, scorched), "two fires land");
            Assert.IsTrue(target.TryGetEffectContainer(burning, out var burningOn), "Burning is on the shelf");
            Assert.IsTrue(target.TryGetEffectContainer(scorched, out var scorchedOn), "and Scorched");
            await Frames.Until(() => burningOn.Spec.TrackedImpact.Count >= 3, 20, "Burning ticks three times");
            await Frames.Step();

            var context = NumberContext.For(caster, target, 1);
            var combustion = new ScalableFloat(new TrackedImpactKind
            {
                What = ETrackedImpactPart.Total, Of = ETrackedImpactOf.EffectOnTarget, Effect = burning, BySize = true,
            }, 1f);
            Assert.AreEqual(5f * burningOn.Spec.TrackedImpact.Count, combustion.Evaluate(context), 0.01f, "the size of Burning's total");

            var everyFire = new ScalableFloat(new TrackedImpactKind
            {
                What = ETrackedImpactPart.Total, Of = ETrackedImpactOf.TaggedOnTarget, Tagged = fire,
            }, 1f);
            float dealt = -5f * burningOn.Spec.TrackedImpact.Count - 2f * scorchedOn.Spec.TrackedImpact.Count;
            Assert.AreEqual(dealt, everyFire.Evaluate(context), 0.01f, "every fire's total, as dealt");

            var count = new ScalableFloat(new TrackedImpactKind { What = ETrackedImpactPart.Count, Of = ETrackedImpactOf.EffectOnTarget, Effect = burning }, 1f);
            Assert.AreEqual(burningOn.Spec.TrackedImpact.Count, count.Evaluate(context), "Burning's impact count");

            var absent = new ScalableFloat(new TrackedImpactKind { What = ETrackedImpactPart.Total, Of = ETrackedImpactOf.EffectOnTarget, Effect = frozen }, 1f);
            Assert.AreEqual(0f, absent.Evaluate(context), "an effect that isn't there reads 0");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-11: Group Impact
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Group Impact number reads one retention group's part of an attribute: the Strength items give, and none of
        /// the hero's own (NUM-11).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_11_Group_Impact_reads_one_groups_part_of_an_attribute([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var caster = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(strength, 30f, 30f)), "Target");
            var bonus = Make.NewTag("Test.Bonus");
            var ring = Make.Effect("Ring", new EffectConfig { Target = strength, Magnitude = 8f });
            ring.Definition.RetentionGroup = bonus;
            Assert.IsTrue(World.Apply(caster, caster, ring), "the ring's bonus lands");
            await Frames.Step(2);
            Assert.AreEqual(18f, Health(caster, strength), "the caster has 18 Strength in all");

            var fromItems = new ScalableFloat(new GroupImpactKind { Attribute = strength, Group = bonus }, 2f);
            var context = NumberContext.For(caster, target, 1);
            Assert.AreEqual(16f, fromItems.Evaluate(context), 0.01f, "2 × the caster's Strength from its bonus, 8");
            Assert.AreEqual($"2 × Caster's Strength ({bonus})", fromItems.ToString(), "and says so, with the group by its display name");

            var targets = new ScalableFloat(new GroupImpactKind { Attribute = strength, Group = bonus, Of = NumberSide.Target }, 2f);
            Assert.AreEqual(0f, targets.Evaluate(context), "the target has no bonus Strength");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-12: Workers answer numbers
        // ═══════════════════════════════════════════════════════════════════════════

        // The health a curse leaves after its ticks: 5 a tick, and on every fourth a quarter of the health lost before it
        private static float AfterCurse(float health, int ticks)
        {
            float left = health;
            for (int tick = 1; tick <= ticks; tick++)
            {
                float lost = health - left;
                left -= 5f + (tick % 4 == 0 ? 0.25f * lost : 0f);
            }
            return left;
        }

        /// <summary>
        /// A curse that deals 5 a second, and every fourth second a quarter of the health its target has lost since it
        /// landed, is one effect: its worker keeps the health as it landed and counts the ticks, and its magnitude asks
        /// the worker (NUM-12). It took three effects, and none could read the health lost. Each application of the
        /// curse keeps its own state.
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_12_A_curse_reads_its_worker_for_the_health_lost([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)), "Target");
            var other = World.Entity(hub, Make.Set(Make.Element(health, 500f, 500f)), "Other");
            var burst = Make.NewTag("Data.Burst");
            var worker = new MaledictWorker { Health = health, Key = burst };
            var damage = new ScalerGroup();
            damage.AddMember(new ConstantScaler { Value = 5f }, ECalculationOperation.Add);
            damage.AddMember(new WorkerNumberKind { Key = burst }, ECalculationOperation.Add);
            var maledict = Make.Effect("Maledict", new EffectConfig
            {
                Target = health, Magnitude = -1f, MagnitudeOp = EMagnitudeOperation.MultiplyWithScaler, MagnitudeScaler = damage,
                PeriodicTicks = true, FixedDeltaTime = 1f, Workers = { worker },
            });
            Assert.IsTrue(World.Apply(caster, target, maledict) && World.Apply(caster, other, maledict), "the curse lands on two");
            Assert.IsTrue(target.TryGetEffectContainer(maledict, out var onTarget), "it's on the target's shelf");
            Assert.IsTrue(other.TryGetEffectContainer(maledict, out var onOther), "and the other's");
            var state = onTarget.Spec.GetWorkerState<MaledictWorker.State>(worker);
            var otherState = onOther.Spec.GetWorkerState<MaledictWorker.State>(worker);
            Assert.AreEqual(1000f, state.HealthAtStart, "its worker noted the target's health as it landed");
            Assert.AreEqual(500f, otherState.HealthAtStart, "and the other's, apart: one worker keeps a state for each application");
            await Frames.Until(() => state.Ticks >= 8 && otherState.Ticks >= 8, 30, "it ticks eight times");

            Assert.AreEqual(AfterCurse(1000f, state.Ticks), Health(target, health), 0.01f,
                $"after {state.Ticks} ticks, every fourth dealt a quarter of the health lost on top of its 5");
            Assert.AreEqual(AfterCurse(500f, otherState.Ticks), Health(other, health), 0.01f, "and the same on the other");
            Assert.That(Health(target, health), Is.LessThan(1000f - 5f * state.Ticks - 4f), "which is more than 5 a tick alone");
        });

        /// <summary>
        /// A number can ask the caster's or the target's own workers, those its worker groups gave it and those code gave
        /// it, and reads its default where none answers the key (NUM-12).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_12_A_number_asks_an_entitys_workers([Values] Hub hub) => Live(async () =>
        {
            var rage = Make.NewTag("Data.Rage");
            var focus = Make.NewTag("Data.Focus");
            var unknown = Make.NewTag("Data.Unknown");
            var set = Make.UnusedSet();
            set.WorkerGroup.TagWorkers.Add(new AnsweringTagWorker { Key = rage, Value = 7f });
            var caster = World.Entity(hub, set, "Caster");
            var target = World.Entity(hub, name: "Target");
            await Frames.Step(2);

            var context = NumberContext.For(caster, target, 1);
            var asksCaster = new ScalableFloat(new WorkerNumberKind { Key = rage, From = EWorkerNumberFrom.Caster, Default = 1f }, 2f);
            Assert.AreEqual(14f, asksCaster.Evaluate(context), "2 × the 7 the caster's worker answers");
            Assert.AreEqual($"2 × {rage} from the caster's workers, else 1", asksCaster.ToString(), "and says so, with the key by its display name");
            var asksTarget = new ScalableFloat(new WorkerNumberKind { Key = rage, From = EWorkerNumberFrom.Target, Default = 1f }, 2f);
            Assert.AreEqual(2f, asksTarget.Evaluate(context), "the target has no such worker: 2 × its default");
            var asksUnknown = new ScalableFloat(new WorkerNumberKind { Key = unknown, From = EWorkerNumberFrom.Caster, Default = 1f }, 2f);
            Assert.AreEqual(2f, asksUnknown.Evaluate(context), "no worker knows the key: 2 × its default");

            var given = new AnsweringWorker { Key = focus, Value = 3f };
            NumberWorkers.Provide(target, given);
            var asksGiven = new ScalableFloat(new WorkerNumberKind { Key = focus, From = EWorkerNumberFrom.Target, Default = 1f }, 2f);
            Assert.AreEqual(6f, asksGiven.Evaluate(context), "a worker code gave the target answers");
            NumberWorkers.Remove(target, given);
            Assert.AreEqual(2f, asksGiven.Evaluate(context), "and no longer, once it's taken back");
        });

        /// <summary>
        /// The same curse with no code: an Attribute Change worker answers the health its target has lost since the effect
        /// began, on every fourth tick, and a second impact of the effect deals a quarter of it (NUM-12).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_12_An_attribute_change_worker_answers_the_health_lost([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)), "Target");
            var lost = Make.NewTag("Data.Lost");
            var worker = new AttributeChangeEffectWorker { Key = lost, Attribute = health, EveryTicks = 4 };
            var maledict = Make.Effect("Maledict", new EffectConfig
            {
                Target = health, Magnitude = -5f, PeriodicTicks = true, FixedDeltaTime = 1f, Workers = { worker },
            });
            maledict.ImpactSpecification.AdditionalImpacts.Add(new AttributeImpactEntry
            {
                AttributeTarget = health, Delivery = EImpactDelivery.Periodic,
                Magnitude = new ScalableFloat(new WorkerNumberKind { Key = lost }, -0.25f),
            });
            Assert.IsTrue(World.Apply(caster, target, maledict), "the curse lands");
            Assert.IsTrue(target.TryGetEffectContainer(maledict, out var container), "it's on the shelf");
            var state = container.Spec.GetWorkerState<AttributeChangeEffectWorker.State>(worker);
            Assert.IsTrue(state.Began, "its worker noted the health as it landed");
            Assert.AreEqual(1000f, state.AtStart, "which was 1000");
            await Frames.Until(() => state.Ticks >= 8, 30, "it ticks eight times");

            Assert.AreEqual(AfterCurse(1000f, state.Ticks), Health(target, health), 0.01f,
                $"after {state.Ticks} ticks, every fourth dealt a quarter of the health lost before it on top of its 5");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-13: Asset Part
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A number finds the effects on an entity by a tag, their asset tag, a context tag or a tag they grant, and reads
        /// a part of them: how many there are, their stacks, what they dealt, their time left, or what their workers
        /// answer. Several read as one as it says, a count reads 0 where none matches, and any other part its default
        /// (NUM-13).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_13_A_number_reads_a_part_of_the_effects_on_an_entity([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 1000f, 1000f)), "Target");
            var fire = Make.NewTag("Test.Fire");
            var ice = Make.NewTag("Test.Ice");
            var lost = Make.NewTag("Data.Lost");
            var burning = Make.Effect("Burning", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 100f, FixedDeltaTime = 1f, Target = null,
                Reapply = EEffectReApplicationPolicy.StackExistingContainers,
                StackPolicy = EEffectStackPolicy.StacksShareOneDuration,
                Interaction = EEffectInteractionPolicy.RefreshContainerDuration, StackAmount = 1,
            });
            var scorched = Make.Effect("Scorched", new EffectConfig
            {
                Target = health, Magnitude = -2f, PeriodicTicks = true, FixedDeltaTime = 1f,
                Workers = { new AttributeChangeEffectWorker { Key = lost, Attribute = health } },
            });
            burning.Tags.ContextTags.Add(fire);
            scorched.Tags.ContextTags.Add(fire);
            for (int i = 0; i < 3; i++) Assert.IsTrue(World.Apply(caster, target, burning), "Burning lands, three times");
            Assert.IsTrue(World.Apply(caster, target, scorched), "Scorched lands");
            Assert.IsTrue(target.TryGetEffectContainer(burning, out var burningOn), "Burning is on the shelf");
            Assert.IsTrue(target.TryGetEffectContainer(scorched, out var scorchedOn), "and Scorched");
            Assert.AreEqual(3, ((AbstractStackingEffectContainer)burningOn).Stacks, "Burning is at three stacks");
            await Frames.Until(() => scorchedOn.Spec.TrackedImpact.Count >= 3, 20, "Scorched ticks three times");
            await Frames.Step();

            var context = NumberContext.For(caster, target, 1);
            float Read(AssetPartKind kind) => new ScalableFloat(kind, 1f).Evaluate(context);

            Assert.AreEqual(2f, Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.Count }), "two fire effects on the target");
            Assert.AreEqual(4f, Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.Stacks }), "their stacks, added up: 3 and 1");
            Assert.AreEqual(3f, Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.Stacks, Among = EAssetPartAmong.Highest }), "the highest");
            Assert.AreEqual(1f, Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.Stacks, Among = EAssetPartAmong.Lowest }), "the lowest");
            Assert.AreEqual(3f, Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.Stacks, Among = EAssetPartAmong.First }), "the first, Burning's");
            Assert.AreEqual(3f, Read(new AssetPartKind { Tagged = burning.Tags.AssetTag, EffectPart = EEffectPart.Stacks }), "by its asset tag, Burning alone");

            int ticks = scorchedOn.Spec.TrackedImpact.Count;
            Assert.AreEqual(-2f * ticks, Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.TotalImpact }), 0.01f, "what the fires dealt, as dealt");
            Assert.AreEqual(-2f, Read(new AssetPartKind { Tagged = scorched.Tags.AssetTag, EffectPart = EEffectPart.LastImpact }), 0.01f, "Scorched's last impact");
            Assert.AreEqual(ticks, Read(new AssetPartKind { Tagged = scorched.Tags.AssetTag, EffectPart = EEffectPart.ImpactCount }), "and how many it dealt");
            Assert.AreEqual(burningOn.DurationRemaining, Read(new AssetPartKind { Tagged = burning.Tags.AssetTag, EffectPart = EEffectPart.DurationRemaining }), 0.01f, "Burning's time left");
            Assert.That(burningOn.DurationRemaining, Is.LessThan(100f).And.GreaterThan(0f), "which has run down");
            Assert.AreEqual(100f, Read(new AssetPartKind { Tagged = burning.Tags.AssetTag, EffectPart = EEffectPart.Duration }), 0.01f, "and its whole duration");
            Assert.AreEqual(1000f - Health(target, health), Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.WorkerValue, WorkerKey = lost }), 0.01f,
                "what the fires' workers answer: the health lost since Scorched began, from the one whose worker answers");

            Assert.AreEqual(0f, Read(new AssetPartKind { Tagged = ice, EffectPart = EEffectPart.Count, Default = 9f }), "no ice effects: a count reads 0");
            Assert.AreEqual(9f, Read(new AssetPartKind { Tagged = ice, EffectPart = EEffectPart.Stacks, Default = 9f }), "and any other part its default");
            Assert.AreEqual(9f, Read(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.Stacks, On = NumberSide.Caster, Default = 9f }), "the caster has no fires");

            var stacks = new ScalableFloat(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.Stacks }, 10f);
            Assert.AreEqual($"10 × the total stacks of {fire} effects on the target", stacks.ToString(), "it says what it reads, with the tag by its display name");
        });

        /// <summary>
        /// A number finds the abilities an entity knows and the items it holds by a tag, and reads a part of them: how
        /// many, their level, whether they're active or equipped, an item's stacks (NUM-13).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_13_A_number_reads_a_part_of_abilities_and_items([Values] Hub hub) => Live(async () =>
        {
            var caster = World.Entity(hub, name: "Caster");
            var target = World.Entity(hub, name: "Target");
            var spell = Make.NewTag("Test.Spell");
            var gear = Make.NewTag("Test.Gear");
            var fireball = Make.Ability("Fireball", Make.Stage());
            var frost = Make.Ability("Frost", Make.Stage());
            fireball.Tags.ContextTags.Add(spell);
            frost.Tags.ContextTags.Add(spell);
            var abilities = caster.GetAbilitySystem();
            Assert.IsTrue(abilities.GiveAbility(fireball, 3, out _), "the caster learns Fireball, at level 3");
            Assert.IsTrue(abilities.GiveAbility(frost, 2, out _), "and Frost, at level 2");

            var potion = Make.Item("Potion");
            potion.Definition.MaxStackSize = 5;
            potion.Tags.ContextTags.Add(gear);
            var ring = Make.Item("Ring");
            ring.Tags.ContextTags.Add(gear);
            var items = caster.GetItemSystem();
            Assert.AreEqual(3, items.GiveItem(potion, 3), "three potions, in one stack");
            Assert.IsTrue(items.GiveAndEquipItem(ring, out _), "and a ring, equipped");
            await Frames.Step(2);

            var context = NumberContext.For(caster, target, 1);
            float Read(AssetPartKind kind) => new ScalableFloat(kind, 1f).Evaluate(context);
            AssetPartKind Spells(EAbilityPart part, EAssetPartAmong among = EAssetPartAmong.Sum) =>
                new() { What = EAssetPartOf.Ability, Tagged = spell, On = NumberSide.Caster, AbilityPart = part, Among = among, Default = 9f };
            AssetPartKind Gear(EItemPart part, EAssetPartAmong among = EAssetPartAmong.Sum) =>
                new() { What = EAssetPartOf.Item, Tagged = gear, On = NumberSide.Caster, ItemPart = part, Among = among, Default = 9f };

            Assert.AreEqual(2f, Read(Spells(EAbilityPart.Count)), "two spells");
            Assert.AreEqual(5f, Read(Spells(EAbilityPart.Level)), "their levels, added up: 3 and 2");
            Assert.AreEqual(3f, Read(Spells(EAbilityPart.Level, EAssetPartAmong.Highest)), "the highest");
            Assert.AreEqual(0f, Read(Spells(EAbilityPart.IsActive)), "none is running");
            Assert.AreEqual(0f, Read(Spells(EAbilityPart.IsOnCooldown)), "nor cooling down");
            Assert.AreEqual(3f, Read(new AssetPartKind { What = EAssetPartOf.Ability, Tagged = fireball.Tags.AssetTag, On = NumberSide.Caster, AbilityPart = EAbilityPart.Level }),
                "by its asset tag, Fireball's level");

            Assert.AreEqual(2f, Read(Gear(EItemPart.Count)), "two items: a stack counts once");
            Assert.AreEqual(4f, Read(Gear(EItemPart.Stacks)), "their stacks, added up: 3 potions and 1 ring");
            Assert.AreEqual(3f, Read(Gear(EItemPart.Stacks, EAssetPartAmong.Highest)), "the highest");
            Assert.AreEqual(1f, Read(Gear(EItemPart.IsEquipped)), "one is equipped");
            Assert.AreEqual(3f, Read(new AssetPartKind { What = EAssetPartOf.Item, Tagged = potion.Tags.AssetTag, On = NumberSide.Caster, ItemPart = EItemPart.Stacks }),
                "by its asset tag, the potions");

            var onTarget = Spells(EAbilityPart.Level);
            onTarget.On = NumberSide.Target;
            Assert.AreEqual(9f, Read(onTarget), "the target knows no spells: its default");
            var targetsGear = Gear(EItemPart.Count);
            targetsGear.On = NumberSide.Target;
            Assert.AreEqual(0f, Read(targetsGear), "and holds no gear: a count reads 0");

            Assert.AreEqual($"how many {gear} items on the caster are equipped", new ScalableFloat(Gear(EItemPart.IsEquipped), 1f).ToString(),
                "it says what it reads, with the tag by its display name");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // NUM-14: Group
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A Group is a kind of number like the others, whose members are numbers of any kind: it adds its Add members,
        /// multiplies by its Multiply members, and where members override, reads those as its policy says. Its Ignore
        /// policy threw; it ignores them (NUM-14).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_14_A_group_joins_its_members([Values] Hub hub) => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var caster = World.Entity(hub, Make.Set(Make.Element(strength, 10f, 10f)), "Caster");
            await Frames.Step();
            var context = NumberContext.For(caster, caster, 1);
            var group = new ScalerGroup();
            group.AddMember(new ConstantScaler { Value = 5f }, ECalculationOperation.Add);
            group.AddMember(new AttributeBackedScaler { CaptureAttribute = strength, CaptureFrom = ESourceTarget.Source }, ECalculationOperation.Add);
            group.AddMember(new ConstantScaler { Value = 2f }, ECalculationOperation.Multiply);
            var number = new ScalableFloat(group, 1f);
            Assert.AreEqual(30f, number.Evaluate(context), 0.01f, "(5 + the caster's 10 Strength) × 2");

            group.AddMember(new ConstantScaler { Value = 40f }, ECalculationOperation.Override);
            group.AddMember(new ConstantScaler { Value = 60f }, ECalculationOperation.Override);
            float With(EValueCollisionPolicy policy)
            {
                group.OverrideMemberCollisionPolicy = policy;
                return number.Evaluate(context);
            }
            Assert.AreEqual(60f, With(EValueCollisionPolicy.UseMaximum), "the highest of its overriding members");
            Assert.AreEqual(40f, With(EValueCollisionPolicy.UseMinimum), "the lowest");
            Assert.AreEqual(50f, With(EValueCollisionPolicy.UseAverage), "their average");
            Assert.AreEqual(40f, With(EValueCollisionPolicy.UseFirst), "the first");
            Assert.AreEqual(60f, With(EValueCollisionPolicy.UseLast), "the last");
            Assert.AreEqual(30f, With(EValueCollisionPolicy.Ignore), 0.01f, "and with Ignore, none of them: its other members");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // Their cost
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reading the new kinds allocates nothing: Local Data, Group Impact, Tracked Impact of an effect on the target,
        /// and a relative number, of a number field and of a plain float (NUM-8 to NUM-11).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_8_to_11_Reading_the_new_kinds_allocates_nothing() => Live(async () =>
        {
            var strength = Make.Attribute("Strength");
            var caster = World.Entity(Hub.Runtime, Make.Set(Make.Element(strength, 10f, 10f)), "Caster");
            var target = World.Entity(Hub.Runtime, Make.Set(Make.Element(strength, 1000f, 1000f)), "Target");
            var bonus = Make.NewTag("Test.Bonus");
            var key = Make.NewTag("Data.Key");
            caster.SetLocalData(key, FloatData(key, 3f));
            var ring = Make.Effect("Ring", new EffectConfig { Target = strength, Magnitude = 8f });
            ring.Definition.RetentionGroup = bonus;
            var burn = Make.Effect("Burn", new EffectConfig { Target = strength, Magnitude = -1f, PeriodicTicks = true, FixedDeltaTime = 1f });
            World.Apply(caster, caster, ring);
            World.Apply(caster, target, burn);
            await Frames.Step(5);

            var localData = new ScalableFloat(new LocalDataKind { Key = key, From = ELocalDataFrom.Caster }, 1f);
            var group = new ScalableFloat(new GroupImpactKind { Attribute = strength, Group = bonus }, 1f);
            var tracked = new ScalableFloat(new TrackedImpactKind { What = ETrackedImpactPart.Total, Of = ETrackedImpactOf.EffectOnTarget, Effect = burn, BySize = true }, 1f);
            var owner = new RelativeOwner();
            var context = NumberContext.For(caster, target, 1);
            float sum = 0f;

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() =>
            {
                sum += localData.Evaluate(context);
                sum += group.Evaluate(context);
                sum += tracked.Evaluate(context);
                sum += owner.Max.Evaluate(context, owner);
                sum += owner.PlusPlain.Evaluate(context, owner);
            }, runs: 500, warmup: 20);

            Assert.That(sum, Is.GreaterThan(0f), "it read");
            Assert.That(bytes, Is.EqualTo(0f), "bytes per read");
        });

        /// <summary>
        /// Reading the newer kinds allocates nothing: a worker's answer, an entity's and an effect's; a part of the
        /// effects, the abilities and the items on an entity; and a Group of members (NUM-12 to NUM-14).
        /// </summary>
        [UnityTest]
        public IEnumerator NUM_12_to_14_Reading_workers_parts_and_groups_allocates_nothing() => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var rage = Make.NewTag("Data.Rage");
            var lost = Make.NewTag("Data.Lost");
            var fire = Make.NewTag("Test.Fire");
            var spell = Make.NewTag("Test.Spell");
            var gear = Make.NewTag("Test.Gear");
            var set = Make.Set(Make.Element(health, 1000f, 1000f));
            set.WorkerGroup.TagWorkers.Add(new AnsweringTagWorker { Key = rage, Value = 7f });
            var caster = World.Entity(Hub.Runtime, set, "Caster");
            var target = World.Entity(Hub.Runtime, Make.Set(Make.Element(health, 1000f, 1000f)), "Target");
            var burn = Make.Effect("Burn", new EffectConfig
            {
                Target = health, Magnitude = -1f, PeriodicTicks = true, FixedDeltaTime = 1f,
                Workers = { new AttributeChangeEffectWorker { Key = lost, Attribute = health } },
            });
            burn.Tags.ContextTags.Add(fire);
            World.Apply(caster, target, burn);
            var fireball = Make.Ability("Fireball", Make.Stage());
            fireball.Tags.ContextTags.Add(spell);
            caster.GetAbilitySystem().GiveAbility(fireball, 2, out _);
            var ring = Make.Item("Ring");
            ring.Tags.ContextTags.Add(gear);
            caster.GetItemSystem().GiveItem(ring, out _);
            await Frames.Step(5);

            var asks = new ScalableFloat(new WorkerNumberKind { Key = rage, From = EWorkerNumberFrom.Caster }, 1f);
            var effects = new ScalableFloat(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.ImpactCount }, 1f);
            var answered = new ScalableFloat(new AssetPartKind { Tagged = fire, EffectPart = EEffectPart.WorkerValue, WorkerKey = lost }, 1f);
            var levels = new ScalableFloat(new AssetPartKind { What = EAssetPartOf.Ability, Tagged = spell, On = NumberSide.Caster, AbilityPart = EAbilityPart.Level }, 1f);
            var cooling = new ScalableFloat(new AssetPartKind { What = EAssetPartOf.Ability, Tagged = spell, On = NumberSide.Caster, AbilityPart = EAbilityPart.IsOnCooldown }, 1f);
            var held = new ScalableFloat(new AssetPartKind { What = EAssetPartOf.Item, Tagged = gear, On = NumberSide.Caster, ItemPart = EItemPart.Count }, 1f);
            var members = new ScalerGroup();
            members.AddMember(new ConstantScaler { Value = 5f }, ECalculationOperation.Add);
            members.AddMember(new WorkerNumberKind { Key = rage, From = EWorkerNumberFrom.Caster }, ECalculationOperation.Add);
            members.AddMember(new ConstantScaler { Value = 2f }, ECalculationOperation.Multiply);
            var group = new ScalableFloat(members, 1f);
            var context = NumberContext.For(caster, target, 1);
            Assert.AreEqual(24f, group.Evaluate(context), "(5 + the worker's 7) × 2");
            Assert.AreEqual(2f, levels.Evaluate(context), "Fireball's level");
            Assert.AreEqual(1f, held.Evaluate(context), "one ring");
            float sum = 0f;

            using var probe = new AllocationProbe();
            float bytes = probe.MeasureEach(() =>
            {
                sum += asks.Evaluate(context);
                sum += effects.Evaluate(context);
                sum += answered.Evaluate(context);
                sum += levels.Evaluate(context);
                sum += cooling.Evaluate(context);
                sum += held.Evaluate(context);
                sum += group.Evaluate(context);
            }, runs: 500, warmup: 20);

            Assert.That(sum, Is.GreaterThan(0f), "it read");
            Assert.That(bytes, Is.EqualTo(0f), "bytes per read");
        });
    }
}
