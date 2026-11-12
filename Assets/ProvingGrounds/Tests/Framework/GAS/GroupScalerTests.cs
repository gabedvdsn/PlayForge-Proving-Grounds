using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The Group scaler, as the user's notes of 2026-11-11 and Decision 44 left it (ED-80): a Group with no members is no
    /// kind at all, so its field reads its number, and a Group shows no values per level.
    /// </summary>
    public class GroupScalerTests : ProvingGroundsTest
    {
        private static float Current(IGameplayAbilitySystem gas, ForgeAttribute attribute)
        {
            Assert.That(gas.TryGetAttributeValue(attribute, out var value), $"the entity has {attribute.Name}");
            return value.CurrentValue;
        }

        // A Group whose one member holds no kind yet, as the members list's "+" leaves it
        private static ScalerGroup WithAnEmptyMember() => new()
        {
            Calculations = new[] { new MagnitudeModifierGroupMember { RelativeOperation = EGroupOperation.Add } },
        };

        /// <summary>
        /// A Group with no members read values per level of its own, which its inspector hid (ED-51). It reads as no kind,
        /// so its field reads its number wherever the field is read: an effect's damage, its duration, a number read in a
        /// context, and an attribute set element's side. A Group whose members hold no kind is none too, and so is a member
        /// that is an empty Group (ED-80, Decision 44).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_80_A_group_with_no_members_reads_as_no_kind([Values] Hub hub) => Live(async () =>
        {
            var health = Make.Attribute("Health");
            var caster = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Caster");
            var target = World.Entity(hub, Make.Set(Make.Element(health, 100f, 100f)), "Target");
            await Frames.Step();

            var strike = Make.Effect("Strike", new EffectConfig
            {
                Target = health, Magnitude = -10f, MagnitudeOp = EMagnitudeOperation.UseScaler, MagnitudeScaler = new ScalerGroup(),
            });
            Assert.IsTrue(World.Apply(caster, target, strike), "the strike lands");
            Assert.AreEqual(90f, Current(target, health), 0.01f, "its damage's empty Group reads as no kind: the field's number, -10");

            var timed = Make.Effect("Timed", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 1f });
            timed.DurationSpecification.DurationOperation = new ScalableFloat { Magnitude = 5f, RealMagnitude = EMagnitudeOperation.UseScaler, Scaler = new ScalerGroup() };
            Assert.IsTrue(World.Apply(caster, target, timed), "the timed effect lands");
            Assert.IsTrue(target.TryGetEffectContainer(timed, out var container), "it's on the shelf");
            Assert.AreEqual(5f, container.TotalDuration, 0.01f, "its duration's empty Group reads as no kind: 5 s");

            var context = NumberContext.For(caster, target, 1);
            Assert.AreEqual(25f, new ScalableFloat(new ScalerGroup(), 25f, EMagnitudeOperation.UseScaler).Evaluate(context), 0.01f,
                "a number read in a context reads its number");
            Assert.AreEqual(25f, new ScalableFloat(new ScalerGroup(), 25f, EMagnitudeOperation.MultiplyWithScaler).Evaluate(context), 0.01f,
                "through any Combine: it doesn't multiply its number by nothing");
            Assert.AreEqual(25f, new ScalableFloat(WithAnEmptyMember(), 25f, EMagnitudeOperation.UseScaler).Evaluate(context), 0.01f,
                "a Group whose members hold no kind is none too");

            var five = new ScalerGroup();
            five.AddMember(new ConstantScaler { Value = 5f }, ECalculationOperation.Add);
            five.AddMember(new ScalerGroup(), ECalculationOperation.Add);
            Assert.AreEqual(5f, new ScalableFloat(five, 1f, EMagnitudeOperation.UseScaler).Evaluate(context), 0.01f,
                "a member that is an empty Group adds nothing");

            var mana = Make.Attribute("Mana");
            var startsAt = new AttributeMagnitudeSpec { Magnitude = 50f, RealMagnitude = EMagnitudeOperation.UseScaler, Scaling = new CachedScalerGroup() };
            var casting = World.Entity(hub, Make.Set(Make.Element(mana, 50f, 50f, currentSpecOverride: startsAt)), "Caster of Mana");
            await Frames.Step();
            Assert.AreEqual(50f, Current(casting, mana), 0.01f, "an attribute set element's empty Group reads as no kind: it starts at 50");
        });

        /// <summary>
        /// The cached Group, an attribute set element's, threw on its Ignore policy as it worked out its element; it sets
        /// its overriding members aside, and the others join as they would without them, as the Group's does (ED-80).
        /// </summary>
        [UnityTest]
        public IEnumerator ED_80_A_cached_group_that_ignores_overrides_joins_the_others([Values] Hub hub) => Live(async () =>
        {
            CachedMagnitudeModifierGroupMember Member(float value, EGroupOperation joins) => new()
            {
                Calculation = new CachedSimpleScaler { LevelValues = new[] { value }, StretchOverLevels = false },
                RelativeOperation = joins,
            };
            var ignoring = new CachedScalerGroup
            {
                OverrideMemberCollisionPolicy = EValueCollisionPolicy.Ignore,
                Calculations = new[] { Member(30f, EGroupOperation.Add), Member(70f, EGroupOperation.Override) },
            };
            var stamina = Make.Attribute("Stamina");
            var startsAt = new AttributeMagnitudeSpec { Magnitude = 1f, RealMagnitude = EMagnitudeOperation.UseScaler, Scaling = ignoring };
            var tiring = World.Entity(hub, Make.Set(Make.Element(stamina, 0f, 0f, currentSpecOverride: startsAt)), "Tiring");
            await Frames.Step();
            Assert.AreEqual(30f, Current(tiring, stamina), 0.01f, "it sets the member that overrides aside, and reads the one that adds");
        });

        /// <summary>
        /// A Group's members join as the sum of those that add, times each that multiplies, plus each flat bonus, in any
        /// order, so its first member needn't add (the user's question, ED-80): a Multiply first and an Add after read as
        /// an Add first does.
        /// </summary>
        [UnityTest]
        public IEnumerator ED_80_A_groups_members_join_in_any_order([Values] Hub hub) => Live(async () =>
        {
            var caster = World.Entity(hub, name: "Caster");
            await Frames.Step();
            var first = new ScalerGroup();
            first.AddMember(new ConstantScaler { Value = 3f }, ECalculationOperation.Multiply);
            first.AddMember(new ConstantScaler { Value = 4f }, ECalculationOperation.Add);
            first.AddMember(new ConstantScaler { Value = 1f }, ECalculationOperation.FlatBonus);
            var context = NumberContext.For(caster, caster, 1);
            Assert.AreEqual(13f, new ScalableFloat(first, 1f, EMagnitudeOperation.UseScaler).Evaluate(context), 0.01f, "4 × 3 + 1");
        });

        /// <summary>A Group reads its members alone, so it shows no values per level, with members or without (ED-80).</summary>
        [Test]
        public void ED_80_A_group_shows_no_values_per_level()
        {
            Assert.AreEqual(EScalerParts.None, new ScalerGroup().Parts, "an empty Group shows none");
            var full = new ScalerGroup();
            full.AddMember(new ConstantScaler { Value = 3f }, ECalculationOperation.Add);
            Assert.AreEqual(EScalerParts.None, full.Parts, "nor does one with members");
        }
    }
}
