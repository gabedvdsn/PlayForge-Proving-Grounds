using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    public class TargetedProcessTests : ProvingGroundsTest
    {
        /// <summary>
        /// A shot registered with no target warned about it, then used the target anyway, and the throw refused
        /// the shot. It ends quietly instead, having nothing to fly at.
        /// </summary>
        [UnityTest, Category("Red")]
        public IEnumerator GAS_31_A_shot_with_no_target_ends() => Live(async () =>
        {
            var template = World.Template<ProbeShot>("Aimless");
            template.ProcessLifecycle = EProcessLifecycle.SelfTerminating;
            template.ProcessTiming = EProcessStepTiming.None;
            template.RunFrames = 2;

            var data = ProcessDataPacket.Default();
            data.SetPrimary(Tags.POSITION, new Vector3(0f, 0f, -5f));

            ProbeShot shot = null;
            Assert.DoesNotThrow(() => shot = World.Spawn(template, data), "it's accepted");

            await Frames.Until(() => !shot, 5, "and ends");
        });
    }
}
