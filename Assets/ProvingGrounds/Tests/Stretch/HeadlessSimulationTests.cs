using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// The review's headless simulation, a stretch scenario (FEAT-10): auto-battles with no scene, for balance analysis.
    /// Three archetypes duel each other and themselves, each duel a pair of runtime entities in a world of its own, eight
    /// worlds side by side, stepped by hand in a loop: no ProcessControl steps them and no frame passes while they fight.
    /// A blow lands a critical hit by chance, and each side first strikes at a point within its cooldown, both drawn from
    /// a seeded random source, so the duels differ, and their win rates are a balance's numbers.
    /// </summary>
    [Category("Stretch")]
    public class HeadlessSimulationTests : ProvingGroundsTest
    {
        private const float StepSeconds = 1f / 20f;
        private const int StepsASecond = 20, MostSteps = 60 * StepsASecond, DuelsAPairing = 100, SideBySide = 8, Seed = 2026;

        private sealed class Archetype
        {
            public string Name;
            public AttributeSet Set;
            public Ability Attack;
            public float Cooldown;
        }

        private sealed class Duel
        {
            public Archetype LeftKind, RightKind;
            public ProcessWorld World;
            public RuntimeGameplayAbilitySystem Left, Right;
            // The step each side first strikes at, drawn within its cooldown, so two of a kind don't strike in step
            public int LeftFrom, RightFrom;
            public int Winner = -1;
            public int Steps;
            public bool Over;
        }

        private Attribute health;

        // An archetype: its health, and its attack, a blow on a cooldown with a chance of a second blow as hard, its critical
        private Archetype Kind(string name, float hp, float blow, float cooldown, float critical)
        {
            var set = Make.Set(Make.Element(health, hp, hp, EAttributeOverflowPolicy.ZeroToBase));
            set.WorkerGroup.AnalysisWorkers.Add(new DeathWatcherAnalysisWorker
            {
                Target = health, Threshold = 0f, Comparison = EThresholdComparison.LessThanOrEqual, TriggerOnce = true,
            });

            var hit = Make.Effect($"{name} Blow", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -blow });
            var crit = Make.Effect($"{name} Critical", new EffectConfig { Policy = EEffectDurationPolicy.Instant, Target = health, Magnitude = -blow });
            crit.ImpactSpecification.ChanceToApply = new ScalableFloat { Magnitude = critical, RealMagnitude = EMagnitudeOperation.UseMagnitude };
            var stage = Make.Stage(new ApplyEffectsAbilityTask { Effects = new List<GameplayEffect> { hit, crit } });
            stage.ApplyUsageEffects = true;
            var attack = Make.Ability($"{name} Attack", stage);
            attack.Cooldown = Make.Effect($"{name} Cooldown", new EffectConfig { Policy = EEffectDurationPolicy.Durational, Duration = cooldown, Target = null });
            return new Archetype { Name = name, Set = set, Attack = attack, Cooldown = cooldown };
        }

        private RuntimeGameplayAbilitySystem Enter(ProcessWorld world, Archetype kind, string side)
        {
            var gas = new RuntimeGameplayAbilitySystem(Make.RuntimeIdentity($"{side} {kind.Name}", kind.Set));
            Assert.IsTrue(ProcessControl.Register(gas).In(world).Run(), $"a {kind.Name} joins its duel's world");
            Assert.IsTrue(gas.GetAbilitySystem().GiveAbility(kind.Attack, 1, out _), $"and learns its attack");
            return gas;
        }

        private static void Strike(RuntimeGameplayAbilitySystem by, Ability attack, RuntimeGameplayAbilitySystem at)
        {
            var abilities = by.GetAbilitySystem();
            if (!abilities.TryGetAbilityContainer(attack, out var container)) return;
            var request = ProcessDataPacket.Default();
            request.SetPrimary(Tags.TARGET_REAL, (ITarget)at);
            abilities.TryActivateAbility(abilities.CreateActivationRequest(container.Index, request, EAbilityActivationPolicyExtended.ActivateIfIdle));
        }

        private Duel Begin(Archetype left, Archetype right)
        {
            var duel = new Duel
            {
                LeftKind = left, RightKind = right, World = new ProcessWorld(),
                LeftFrom = (int)(ForgeRandom.Value * left.Cooldown * StepsASecond),
                RightFrom = (int)(ForgeRandom.Value * right.Cooldown * StepsASecond),
            };
            duel.Left = Enter(duel.World, left, "Left");
            duel.Right = Enter(duel.World, right, "Right");
            duel.Left.GetCallbacks().OnEntityDied += _ => End(duel, winner: 1);
            duel.Right.GetCallbacks().OnEntityDied += _ => End(duel, winner: 0);
            return duel;
        }

        // The first to fall loses; two that fall in one step are a draw
        private static void End(Duel duel, int winner)
        {
            if (duel.Over && duel.Winner != winner) duel.Winner = -1;
            else if (!duel.Over) duel.Winner = winner;
            duel.Over = true;
        }

        // A step of a duel: each side strikes as its attack is ready, then the world steps, and the blows land at its end
        private static void Step(Duel duel)
        {
            if (duel.Steps >= duel.LeftFrom) Strike(duel.Left, duel.LeftKind.Attack, duel.Right);
            if (duel.Steps >= duel.RightFrom) Strike(duel.Right, duel.RightKind.Attack, duel.Left);
            duel.World.Step(StepSeconds);
            duel.Steps++;
            if (duel.Steps >= MostSteps) duel.Over = true;
        }

        private static void Dismiss(Duel duel)
        {
            if (duel.Left.ProcessRelay is { ProcessActive: true }) duel.Left.ProcessRelay.TerminateImmediate();
            if (duel.Right.ProcessRelay is { ProcessActive: true }) duel.Right.ProcessRelay.TerminateImmediate();
            duel.World.Step(StepSeconds);
        }

        /// <summary>
        /// A brute, a duelist and a caster fight each pairing of them, mirrors among them, a hundred duels a pairing, eight worlds
        /// side by side, with no scene: each duel ends with a winner within its minute, all within one frame; a mirror's sides
        /// win about as often as each other; and no duel lands anything in the scene's ProcessControl.
        /// </summary>
        [UnityTest]
        public IEnumerator Stretch_Duels_with_no_scene_give_a_balances_numbers() => Live(async () =>
        {
            var previous = ForgeRandom.Use(new SeededRandom(Seed));
            try
            {
                health = Make.Attribute("Health");
                var kinds = new[]
                {
                    Kind("Brute", hp: 300f, blow: 24f, cooldown: 2f, critical: 0.2f),
                    Kind("Duelist", hp: 220f, blow: 9f, cooldown: 0.7f, critical: 0.3f),
                    Kind("Caster", hp: 200f, blow: 16f, cooldown: 1.2f, critical: 0.25f),
                };
                var pairings = new List<(Archetype Left, Archetype Right)>();
                for (int i = 0; i < kinds.Length; i++)
                for (int j = i; j < kinds.Length; j++)
                    pairings.Add((kinds[i], kinds[j]));

                var queue = new Queue<(Archetype Left, Archetype Right)>();
                foreach (var pairing in pairings)
                    for (int n = 0; n < DuelsAPairing; n++) queue.Enqueue(pairing);

                int sceneProcesses = World.Control.FetchActiveProcesses().Count;
                int frame = UnityEngine.Time.frameCount;
                var clock = Stopwatch.StartNew();
                var done = new List<Duel>();
                var fighting = new List<Duel>();
                long steps = 0;
                while (queue.Count > 0 || fighting.Count > 0)
                {
                    while (fighting.Count < SideBySide && queue.Count > 0)
                    {
                        var (left, right) = queue.Dequeue();
                        fighting.Add(Begin(left, right));
                    }

                    // The worlds side by side, a step each in turn
                    foreach (var duel in fighting)
                    {
                        Step(duel);
                        steps++;
                    }

                    foreach (var duel in fighting.Where(each => each.Over).ToList())
                    {
                        Dismiss(duel);
                        fighting.Remove(duel);
                        done.Add(duel);
                    }
                }
                clock.Stop();

                var report = new StringBuilder("[Headless] ");
                report.AppendFormat(CultureInfo.InvariantCulture, "{0} duels, {1} world steps, in {2:0.00} s of wall time: ",
                    done.Count, steps, clock.Elapsed.TotalSeconds);
                foreach (var (left, right) in pairings)
                {
                    var these = done.Where(duel => duel.LeftKind == left && duel.RightKind == right).ToList();
                    int leftWins = these.Count(duel => duel.Winner == 0), rightWins = these.Count(duel => duel.Winner == 1);
                    float seconds = these.Average(duel => duel.Steps * StepSeconds);
                    report.AppendFormat(CultureInfo.InvariantCulture, "{0} v {1} {2}-{3} ({4} drawn, {5:0.0} s); ",
                        left.Name, right.Name, leftWins, rightWins, these.Count - leftWins - rightWins, seconds);
                }
                Debug.Log(report.ToString());

                Assert.AreEqual(frame, UnityEngine.Time.frameCount, "every duel within one frame");
                Assert.AreEqual(pairings.Count * DuelsAPairing, done.Count, "every duel fought");
                Assert.That(done.Count(duel => duel.Steps >= MostSteps), Is.Zero, "each within its minute");
                Assert.That(done.Count(duel => duel.Winner == -1), Is.LessThanOrEqualTo(done.Count / 20), "nearly all with a winner");
                foreach (var kind in kinds)
                {
                    var mirror = done.Where(duel => duel.LeftKind == kind && duel.RightKind == kind).ToList();
                    float leftShare = mirror.Count(duel => duel.Winner == 0) / (float)mirror.Count;
                    Assert.That(leftShare, Is.InRange(0.3f, 0.7f), $"a {kind.Name}'s mirror, whose sides win about as often as each other");
                }
                Assert.AreEqual(sceneProcesses, World.Control.FetchActiveProcesses().Count, "and nothing of them in the scene's ProcessControl");
            }
            finally
            {
                ForgeRandom.Use(previous);
            }

            await Frames.Step();
        });
    }
}
