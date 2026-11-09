using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// Rollback, a stretch scenario (the review's): a deterministic fixed tick, a snapshot each tick, a restore eight ticks
    /// back, and a re-simulation that lands where the first run did. Two duelists in a world of their own (FEAT-10) slash
    /// each other by a log of presses, each slash's critical a seeded draw (FEAT-13) and its bleed a lasting effect from the
    /// other, and both are saved after each tick (FEAT-9), in memory, as a rollback keeps its saves. Rolled back eight ticks
    /// and run again on the same presses, the duel ends in the same state, to the byte of its save; with a press that came
    /// late, it ends where that press leads, and there each time. It found GAS-60 and GAS-61.
    /// </summary>
    [Category("Stretch")]
    public class RollbackTests : ProvingGroundsTest
    {
        private const float Step = 1f / 30f;
        private const int Ticks = 90, Back = 8, Seed = 77, Quiet = 24;

        // The duelists' IDs, by which a save names the other as a bleed's source
        private sealed class Pair : IForgeEntityIds
        {
            public RuntimeGameplayAbilitySystem Left, Right;

            public string IdOf(IGameplayAbilitySystem entity) =>
                ReferenceEquals(entity, Left) ? "Left" : ReferenceEquals(entity, Right) ? "Right" : null;

            public IGameplayAbilitySystem Find(string id) => id == "Left" ? Left : id == "Right" ? Right : null;
        }

        private static float Health(IGameplayAbilitySystem gas, Attribute health) =>
            gas.TryGetAttributeValue(health, out var value) ? value.CurrentValue : float.NaN;

        private static bool Strike(RuntimeGameplayAbilitySystem by, Ability slash, RuntimeGameplayAbilitySystem at)
        {
            var abilities = by.GetAbilitySystem();
            if (!abilities.TryGetAbilityContainer(slash, out var container)) return false;
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)at);
            return abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, request, EAbilityActivationPolicyExtended.ActivateIfIdle));
        }

        /// <summary>
        /// Ninety ticks of a duel, saved after each. Rolled back eight ticks and run again on the same presses, it ends in
        /// the state it did, to the byte; with a press of Right's that came five ticks late, it ends elsewhere, and in the
        /// same place each time it's run.
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_Rollback_restores_eight_ticks_back_and_lands_where_it_did() => Live(async () =>
        {
            var previous = ForgeRandom.Use(new SeededRandom(Seed));
            var catalog = ScriptableObject.CreateInstance<ForgeAssetCatalog>();
            try
            {
                var health = Make.Attribute("Health");
                var set = Make.Set(Make.Element(health, 400f, 400f));
                var blow = Make.Effect("Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -12f });
                var critical = Make.Effect("Critical", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -12f });
                critical.ImpactSpecification.ChanceToApply = new ScalableFloat { Magnitude = 0.3f, RealMagnitude = EMagnitudeOperation.UseMagnitude };
                var bleed = Make.Effect("Bleed", new EffectConfig
                {
                    Policy = EEffectDurationPolicy.Durational, Duration = 2f, Target = health, Magnitude = -2f, PeriodicTicks = true, Ticks = 8,
                });
                var stage = Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { blow, critical, bleed } });
                stage.ApplyUsageEffects = true;
                var slash = Make.Ability("Slash", stage);
                var cooldown = Make.Effect("Slash Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = 0.5f });
                slash.Cooldown = cooldown;
                foreach (var (id, asset) in new (string, ScriptableObject)[]
                         {
                             ("Health", health), ("Set", set), ("Blow", blow), ("Critical", critical), ("Bleed", bleed), ("Slash", slash),
                             ("Slash Cooldown", cooldown),
                         })
                    Assert.IsTrue(catalog.Add(id, asset), $"the catalog names {id}");

                var world = new ProcessWorld();
                var pair = new Pair();
                RuntimeGameplayAbilitySystem Duelist(string name)
                {
                    var gas = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity(name, set));
                    Assert.IsTrue(ProcessControl.Register(gas).In(world).Run(), $"{name} joins the duel's world");
                    Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(slash, 1, out _), $"and learns Slash");
                    return gas;
                }
                pair.Left = Duelist("Left");
                pair.Right = Duelist("Right");

                // The presses: each side's at each tick, by its own seeded draws; Right is quiet at the end, so a late press
                // of its finds its slash ready
                var draw = new System.Random(Seed);
                var presses = new List<(bool Left, bool Right)>();
                for (int i = 0; i < Ticks; i++) presses.Add((draw.NextDouble() < 0.4, i < Ticks - Quiet && draw.NextDouble() < 0.4));

                // A tick: its own draws, its presses, then the world's fixed step
                int strikes = 0;
                void Tick(int i)
                {
                    ForgeRandom.Use(new SeededRandom(Seed * 1000 + i));
                    if (presses[i].Left && Strike(pair.Left, slash, pair.Right)) strikes++;
                    if (presses[i].Right && Strike(pair.Right, slash, pair.Left)) strikes++;
                    world.Step(Step);
                }

                // A save, kept in memory as a rollback keeps it, and its text to compare by. JsonUtility moves the last bit
                // of a double now and then, so a save that went through its text came back a hair off
                (ForgeSnapshot Left, ForgeSnapshot Right) Save() => (ForgeSave.Capture(pair.Left, catalog, pair), ForgeSave.Capture(pair.Right, catalog, pair));

                string Text((ForgeSnapshot Left, ForgeSnapshot Right) save) => JsonUtility.ToJson(save.Left) + "\n" + JsonUtility.ToJson(save.Right);

                void Load((ForgeSnapshot Left, ForgeSnapshot Right) save)
                {
                    Assert.IsTrue(ForgeSave.Restore(pair.Left, save.Left, catalog, pair), "Left loads");
                    Assert.IsTrue(ForgeSave.Restore(pair.Right, save.Right, catalog, pair), "Right loads");
                }

                var saves = new List<(ForgeSnapshot Left, ForgeSnapshot Right)>();
                for (int i = 0; i < Ticks; i++)
                {
                    Tick(i);
                    saves.Add(Save());
                }
                string first = Text(saves[Ticks - 1]);
                Assert.That(strikes, Is.GreaterThan(6), "the duelists slash each other");
                Assert.That(Health(pair.Left, health), Is.LessThan(400f), "and draw blood");
                Assert.That(Health(pair.Right, health), Is.LessThan(400f), "on both sides");

                // Rolled back eight ticks, and run again on the same presses
                Load(saves[Ticks - 1 - Back]);
                Assert.AreEqual(Text(saves[Ticks - 1 - Back]), Text(Save()), "loaded and saved again, the save is as it was");
                for (int i = Ticks - Back; i < Ticks; i++) Tick(i);
                Assert.AreEqual(first, Text(Save()), "rolled back eight ticks and run again, the duel ends where it did, to the byte of its save");

                // A press of Right's that came five ticks late: rolled back to before it, and run with it
                int late = Ticks - 5;
                Assert.IsFalse(presses[late].Right, "Right didn't press then");
                presses[late] = (presses[late].Left, true);
                Load(saves[late - 1]);
                for (int i = late; i < Ticks; i++) Tick(i);
                string corrected = Text(Save());
                Assert.AreNotEqual(first, corrected, "the late press changes where the duel ends");
                Load(saves[late - 1]);
                for (int i = late; i < Ticks; i++) Tick(i);
                Assert.AreEqual(corrected, Text(Save()), "and it ends there each time");

                if (pair.Left.ProcessRelay is { ProcessActive: true }) pair.Left.ProcessRelay.TerminateImmediate();
                if (pair.Right.ProcessRelay is { ProcessActive: true }) pair.Right.ProcessRelay.TerminateImmediate();
                world.Step(Step);
            }
            finally
            {
                ForgeRandom.Use(previous);
                Object.Destroy(catalog);
            }

            await Frames.Step();
        });
    }
}
