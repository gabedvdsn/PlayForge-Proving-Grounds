using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// An entity's Time Scale attribute and a clock scale set in code compose (EP-56): the attribute overwrote the clock's
    /// scale each step, so a hit-stop set in code was undone a step later wherever an entity named the attribute. The
    /// clock reads code's scale times the attribute's, and a save keeps code's.
    /// </summary>
    public class EntityTimeScaleTests : ProvingGroundsTest
    {
        // An entity whose time runs at its Time Scale attribute, at the value given
        private RuntimeGameplayAbilitySystem Entity(ForgeAttribute time, float scale)
        {
            var identity = Make.RuntimeIdentity("Duelist", Make.Set(Make.Element(time, scale, 1f)));
            identity.TimeScaleAttribute = time;
            var gas = new RuntimeGameplayAbilitySystem(identity);
            World.Register(gas);
            return gas;
        }

        /// <summary>A clock scale of 0 set in code holds, where the entity names a Time Scale attribute (EP-56).</summary>
        [UnityTest]
        public IEnumerator EP_56_A_scale_set_in_code_holds_where_a_time_scale_attribute_is_named() => Live(async () =>
        {
            var gas = Entity(Make.Attribute("Time Scale"), 1f);
            await Frames.Step();

            gas.GetClock().TimeScale = 0f;
            await Frames.Step(2);

            Assert.AreEqual(0f, gas.GetClock().TimeScale, 0.0001f, "a hit-stop set in code holds, the attribute named");
            Assert.AreEqual(0f, gas.GetClock().DeltaTime, 0.0001f, "and the entity's time stands still");
        });

        /// <summary>The attribute's scale and code's compose: a half and a half read a quarter (EP-56).</summary>
        [UnityTest]
        public IEnumerator EP_56_The_attributes_scale_and_codes_compose() => Live(async () =>
        {
            var gas = Entity(Make.Attribute("Time Scale"), 0.5f);
            gas.GetClock().TimeScale = 0.5f;
            await Frames.Step(2);

            Assert.AreEqual(0.25f, gas.GetClock().TimeScale, 0.0001f, "a slow at half and code at half read a quarter");
        });

        /// <summary>A save keeps code's scale, not the two composed, so a load reads as before (EP-56).</summary>
        [UnityTest]
        public IEnumerator EP_56_A_save_keeps_the_scale_code_set() => Live(async () =>
        {
            var gas = Entity(Make.Attribute("Time Scale"), 0.5f);
            gas.GetClock().TimeScale = 0.5f;
            await Frames.Step(2);

            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            try
            {
                Assert.IsTrue(ForgeSave.Restore(gas, ForgeSave.Capture(gas, catalog), catalog), "a save of it loads");
                await Frames.Step(2);
                Assert.AreEqual(0.25f, gas.GetClock().TimeScale, 0.0001f, "and its time runs at a quarter still, not an eighth");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        });
    }
}
