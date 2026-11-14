using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// FN-14, Decision 77: an ability's Cast Range, the one range it has, read at its level. An activation handed a target
    /// or a point beyond it is refused as out of range, so nothing of it runs; one whose targeting picks a target beyond it
    /// ends there; and a targeting task with a range of its own searches within it, from the caster.
    /// </summary>
    public class CastRangeTests : ProvingGroundsTest
    {
        private ForgeAttribute health;
        private Tag red, blue, marked;

        [SetUp]
        public void Sides()
        {
            health = Make.Attribute("Health");
            red = Make.NewTag("Test.FN14.Red");
            blue = Make.NewTag("Test.FN14.Blue");
            marked = Make.NewTag("Test.FN14.Marked");
        }

        // An entity of a side, at a point along x, on either hub
        private IGameplayAbilitySystem At(Hub hub, string name, Tag side, float x)
        {
            var set = Make.Set(Make.Element(health, 100f, 100f, EAttributeOverflowPolicy.ZeroToBase));
            IGameplayAbilitySystem gas;
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = new List<Tag> { side };
                var runtime = new RuntimeGameplayAbilitySystem(identity);
                World.Register(runtime);
                World.Invariants.Observe(runtime);
                gas = runtime;
            }
            else
            {
                var mono = World.NewMonoEntity(set, name);
                mono.EntityData.Affiliation = new List<Tag> { side };
                World.Register(mono);
                World.Invariants.Observe(mono);
                gas = mono;
            }
            gas.GetTargetingPacket().position = new Vector3(x, 0f, 0f);
            return gas;
        }

        private bool Marked(IGameplayAbilitySystem gas) => gas.GetTagCache().GetWeight(marked) > 0;

        // An ability whose one stage marks each of its targets, with a Cast Range and the targeting given
        private Ability Ranged(string name, ScalableFloat range, AbstractTargetingAbilityTask targeting = null)
        {
            var mark = Make.Effect(name + " Mark", new EffectConfig
            {
                Policy = EEffectDurationPolicy.Durational, Duration = 5f, GrantedTags = new List<Tag> { marked },
            });
            var ability = Make.Ability(name, Make.Stage(new ProbeAbilityTask(World, name), new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { mark } }));
            if (targeting != null) ability.Behaviour.Targeting = targeting;
            ability.Components.Add(new CastRangeAbilityComponent { Range = range });
            return ability;
        }

        private static ScalableFloat ByLevel(params float[] values) =>
            new(new SimpleScaler { LevelValues = values, StretchOverLevels = false }, 1f, EMagnitudeOperation.UseScaler);

        private static EActivationRefusal Activate(IGameplayAbilitySystem caster, Ability ability, ProcessDataPacket data)
        {
            caster.GetAbilitySystem().TryActivateAbility(ability, out var refusal, data, EAbilityActivationPolicyExtended.AlwaysActivate);
            return refusal.Reason;
        }

        private static ProcessDataPacket Handing(ITarget target)
        {
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.TARGET_REAL, target);
            return data;
        }

        private static ProcessDataPacket HandingPoint(Vector3 point)
        {
            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.POSITION, point);
            return data;
        }

        /// <summary>
        /// An activation handed a target or a point beyond its Cast Range is refused as out of range, and never runs; one
        /// handed a target within it starts. The ability says what refused it, so an AI can walk closer.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_A_target_beyond_the_cast_range_is_refused([Values] Hub hub) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, 0f);
            var near = At(hub, "Near", blue, 4f);
            var far = At(hub, "Far", blue, 8f);
            var bolt = Ranged("Bolt", 5f, new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL });
            await Frames.Step();
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(bolt, 1, out _), "known");

            Assert.AreEqual(EActivationRefusal.Range, Activate(caster, bolt, Handing(far)), "a target 8 away, beyond its 5");
            Assert.AreEqual(EActivationRefusal.Range, Activate(caster, bolt, HandingPoint(new Vector3(0f, 0f, 6f))), "and a point 6 away");
            await Frames.Step(3);
            Assert.AreEqual(0, Ledger.Prepares("Bolt"), "it never ran");

            Assert.AreEqual(EActivationRefusal.None, Activate(caster, bolt, Handing(near)), "one 4 away, within it");
            await Frames.Until(() => Ledger.Cleans("Bolt") == 1, 30, "it ran");
            Assert.IsTrue(Marked(near), "at its target");
        });

        /// <summary>Its range reads at the ability's level, as a number that scales by it: 3 at level 1, 10 at level 2.</summary>
        [UnityTest]
        public IEnumerator FN_14_The_cast_range_reads_at_the_abilitys_level([Values] Hub hub) => Live(async () =>
        {
            var low = At(hub, "Low", red, 0f);
            var high = At(hub, "High", red, 0f);
            var far = At(hub, "Far", blue, 6f);
            var bolt = Ranged("Bolt", ByLevel(3f, 10f), new AutoTargetFromDataTask { SourceKey = Tags.TARGET_REAL });
            bolt.MaxLevel = 2;
            await Frames.Step();
            Assert.IsTrue(low.GetAbilitySystem().GiveAbility(bolt, 1, out _) && high.GetAbilitySystem().GiveAbility(bolt, 2, out _), "known");

            Assert.AreEqual(EActivationRefusal.Range, Activate(low, bolt, Handing(far)), "at level 1 it reaches 3, short of 6");
            Assert.AreEqual(EActivationRefusal.None, Activate(high, bolt, Handing(far)), "at level 2, 10");
            await Frames.Until(() => Ledger.Cleans("Bolt") == 1, 30, "it ran");
        });

        /// <summary>
        /// Targeting that picks a target beyond the Cast Range ends the activation there, whichever task picked it: its
        /// stages never run. Here the target comes from a key the start doesn't read, so only the pick is beyond.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_Targeting_that_picks_a_target_beyond_the_cast_range_ends_it([Values] Hub hub) => Live(async () =>
        {
            var picked = Make.NewTag("Test.FN14.Picked");
            var caster = At(hub, "Caster", red, 0f);
            var far = At(hub, "Far", blue, 8f);
            var near = At(hub, "Near", blue, 2f);
            var bolt = Ranged("Bolt", 5f, new AutoTargetFromDataTask { SourceKey = picked });
            await Frames.Step();
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(bolt, 1, out _), "known");

            var data = ProcessDataPacket.Default();
            data.SetPrimary(picked, (ITarget)far);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, bolt, data), "it starts, its target not yet picked");
            await Frames.Step(5);
            Assert.AreEqual(0, Ledger.Prepares("Bolt"), "picked 8 away, beyond its 5, its stage never runs");
            Assert.IsFalse(Marked(far), "and nothing lands");

            data = ProcessDataPacket.Default();
            data.SetPrimary(picked, (ITarget)near);
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, bolt, data));
            await Frames.Until(() => Ledger.Cleans("Bolt") == 1, 30, "picked 2 away, it runs");
            Assert.IsTrue(Marked(near), "at the one picked");
        });

        /// <summary>
        /// An auto-targeting task searches within the Cast Range from the caster, where Use Cast Range is on, its own Range
        /// set aside: the farthest it finds is the farthest within the Cast Range. Off, it searches its own Range, and the
        /// farthest there, beyond the Cast Range, is refused.
        /// </summary>
        [UnityTest]
        public IEnumerator FN_14_Auto_targeting_searches_within_the_cast_range([Values] Hub hub, [Values] bool byAffinity) => Live(async () =>
        {
            var caster = At(hub, "Caster", red, 0f);
            var inside = At(hub, "Inside", blue, 4f);
            var outside = At(hub, "Outside", blue, 8f);
            await Frames.Step();
            AbstractTargetingAbilityTask Search(bool useCastRange) => byAffinity
                ? new AutoTargetByAffinityTask { Range = 20f, Mode = EAutoTargetMode.Farthest, AffinityFilter = EAffinityFilter.Enemies, UseCastRange = useCastRange }
                : new AutoTargetTask { Range = 20f, Mode = EAutoTargetMode.Farthest, UseCastRange = useCastRange };

            var within = Ranged("Within", 5f, Search(true));
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(within, 1, out _), "known");
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, within, null));
            await Frames.Until(() => Ledger.Cleans("Within") == 1, 30, "it runs");
            Assert.IsTrue(Marked(inside), "the farthest within its Cast Range of 5");
            Assert.IsFalse(Marked(outside), "not the one at 8, within its own Range of 20");

            var own = Ranged("Own", 5f, Search(false));
            Assert.IsTrue(caster.GetAbilitySystem().GiveAbility(own, 1, out _), "known");
            Assert.AreEqual(EActivationRefusal.None, Activate(caster, own, null));
            await Frames.Step(5);
            Assert.AreEqual(0, Ledger.Prepares("Own"), "its own Range finds the one at 8, beyond its Cast Range, and it ends");
            Assert.IsFalse(Marked(outside), "with nothing landed");
        });
    }
}
