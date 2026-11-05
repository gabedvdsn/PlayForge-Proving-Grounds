using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// A lab in a scene (Decision 33). It plays the lab's bot, the rounds its tests play, slowly enough to watch. Each
    /// round runs as its test does, in a world of its own, and ends passed, failed with the reason, or skipped. Each
    /// entity in the scene gets a capsule and a label, and every entity a card of its attributes, effects and tags. The
    /// editor's Pause and Step go a frame at a time, and the Debugger shows the rest.
    /// </summary>
    [AddComponentMenu("PlayForge/Proving Grounds/Lab Scene")]
    public sealed class LabScene : MonoBehaviour
    {
        /// <summary>The eight labs of Phase 5, in the order they came. A scene stores its lab by number, so add at the end.</summary>
        public enum ELab
        {
            StatusEffect,
            Moba,
            Duel,
            BulletHeaven,
            Roguelite,
            Summoner,
            Tactics,
            Combat,
            Battle,
        }

        public enum ERoundState
        {
            Waiting,
            Playing,
            Passed,
            Failed,
            Skipped,
        }

        /// <summary>One of the bot's rounds: one of its tests, as the scene plays it.</summary>
        public sealed class Round
        {
            public readonly MethodInfo Method;
            public readonly string Name;

            public ERoundState State { get; internal set; }

            /// <summary>Why it failed or was skipped.</summary>
            public string Message { get; internal set; }

            internal Round(MethodInfo method)
            {
                Method = method;
                var name = method.Name.StartsWith("Lab_", StringComparison.Ordinal) ? method.Name.Substring(4) : method.Name;
                Name = name.Replace('_', ' ');
            }
        }

        [Tooltip("The lab whose bot plays here")]
        public ELab Lab;

        [Tooltip("Frames a second, slow enough to watch. 0 plays as fast as it can")]
        [Min(0)] public int FramesPerSecond = 12;

        [Tooltip("Seconds a round's end shows before the next round begins")]
        [Min(0f)] public float SecondsBetweenRounds = 2f;

        [Tooltip("The hub a round that takes one plays on. Mono gives each entity a place in the scene")]
        public Hub Hub = Hub.Mono;

        [Tooltip("Plays the rounds again once they're all done")]
        public bool Loop;

        [Tooltip("A capsule for each entity in the scene. Off for a lab that draws its own")]
        public bool ShowBodies = true;

        [Tooltip("A card for each entity's attributes, effects and tags")]
        public bool ShowCards = true;

        [Tooltip("How far back the camera stands, as a share of how much it frames")]
        [Min(1f)] public float Framing = 2.4f;

        /// <summary>Plays as fast as it can, with no wait between rounds, as the scenes' test does.</summary>
        internal static bool Fast;

        /// <summary>Whether a scene plays its rounds to watch, slowly, as a test's run doesn't: a round can vary each time then.</summary>
        internal static bool Watching { get; private set; }

        private const int MaxCards = 12;
        private static readonly int[] Paces = { 2, 4, 8, 12, 24, 60, 0 };

        private readonly List<Round> rounds = new();
        private readonly List<IGameplayAbilitySystem> entities = new();
        private readonly Dictionary<IGameplayAbilitySystem, Transform> bodies = new();
        private Round playing;
        private ProvingGroundsTest fixture;
        private int previousFrameRate, previousVSync;
        private Vector2 cardsScroll;
        private Styles styles;

        /// <summary>The bot's rounds, in the order its tests are written.</summary>
        public IReadOnlyList<Round> Rounds => rounds;

        /// <summary>Whether every round has played.</summary>
        public bool Finished { get; private set; }

        /// <summary>The lab's name, as the scenes and the panel give it.</summary>
        public static string Title(ELab lab) => lab switch
        {
            ELab.StatusEffect => "Status Effect",
            ELab.Moba => "MOBA",
            ELab.Duel => "Duel",
            ELab.BulletHeaven => "Bullet Heaven",
            ELab.Roguelite => "Roguelite",
            ELab.Summoner => "Summoner",
            ELab.Tactics => "Tactics",
            ELab.Combat => "Combat",
            ELab.Battle => "Battle",
            _ => lab.ToString(),
        };

        /// <summary>Where the lab's scene is.</summary>
        public static string ScenePath(ELab lab) => $"Assets/ProvingGrounds/Scenes/Labs/{Title(lab)} Lab.unity";

        // The lab's bot: the test class whose rounds the scene plays
        private static Type BotOf(ELab lab) => lab switch
        {
            ELab.StatusEffect => typeof(StatusEffectLabTests),
            ELab.Moba => typeof(MobaLabTests),
            ELab.Duel => typeof(DuelLabTests),
            ELab.BulletHeaven => typeof(BulletHeavenLabTests),
            ELab.Roguelite => typeof(RogueliteLabTests),
            ELab.Summoner => typeof(SummonerLabTests),
            ELab.Tactics => typeof(TacticsLabTests),
            ELab.Combat => typeof(CombatLabTests),
            ELab.Battle => typeof(BattleLabTests),
            _ => throw new ArgumentOutOfRangeException(nameof(lab), lab, null),
        };

        private static IEnumerable<MethodInfo> RoundsOf(Type bot) => bot
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<UnityTestAttribute>() != null && method.ReturnType == typeof(IEnumerator))
            .OrderBy(method => method.MetadataToken);

        // ═══════════════════════════════════════════════════════════════════════════
        // Playing the rounds
        // ═══════════════════════════════════════════════════════════════════════════

        private IEnumerator Start()
        {
            previousFrameRate = Application.targetFrameRate;
            previousVSync = QualitySettings.vSyncCount;
            Watching = !Fast;
            QualitySettings.vSyncCount = 0;
            ApplyPace();

            var bot = BotOf(Lab);
            rounds.Clear();
            foreach (var method in RoundsOf(bot)) rounds.Add(new Round(method));

            do
            {
                foreach (var round in rounds)
                {
                    round.State = ERoundState.Waiting;
                    round.Message = null;
                }

                foreach (var round in rounds)
                {
                    yield return Play(bot, round);
                    if (!Fast && SecondsBetweenRounds > 0f) yield return new WaitForSecondsRealtime(SecondsBetweenRounds);
                }
            } while (Loop && !Fast);

            Finished = true;
        }

        // Plays one round as its test does: a world of its own, the round's body, then the world taken away
        private IEnumerator Play(Type bot, Round round)
        {
            playing = round;
            round.State = ERoundState.Playing;
            fixture = (ProvingGroundsTest)Activator.CreateInstance(bot);
            IEnumerator body = null;
            try
            {
                fixture.BuildWorld();
                body = (IEnumerator)round.Method.Invoke(fixture, ArgumentsFor(round.Method));
            }
            catch (Exception exception)
            {
                End(round, exception);
            }

            while (body != null)
            {
                object step;
                try
                {
                    if (!body.MoveNext()) break;
                    step = body.Current;
                }
                catch (Exception exception)
                {
                    End(round, exception);
                    break;
                }

                yield return step;
            }

            if (round.State == ERoundState.Playing) round.State = ERoundState.Passed;
            var ending = fixture;
            fixture = null;
            yield return ending.EndWorldOutsideTests().ToCoroutine();
            playing = null;
        }

        /// <summary>
        /// Stops the rounds and ends the world of the one playing, if one is, as a test that loaded the scene tears down.
        /// </summary>
        internal IEnumerator StopAndEnd()
        {
            StopAllCoroutines();
            if (fixture == null) yield break;
            var ending = fixture;
            fixture = null;
            playing = null;
            yield return ending.EndWorldOutsideTests().ToCoroutine();
        }

        // What a round takes: the scene's hub for a hub, and each other parameter's first value
        private object[] ArgumentsFor(MethodInfo method) => method.GetParameters().Select(parameter =>
        {
            var type = parameter.ParameterType;
            if (type == typeof(Hub)) return (object)Hub;
            if (type == typeof(bool)) return false;
            if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }).ToArray();

        // How a round ended early: a check it couldn't make, one that failed, or something that broke
        private void End(Round round, Exception exception)
        {
            while (exception is TargetInvocationException { InnerException: { } inner }) exception = inner;
            if (exception is InconclusiveException or IgnoreException)
            {
                round.State = ERoundState.Skipped;
                round.Message = exception.Message.Trim();
                return;
            }

            round.State = ERoundState.Failed;
            round.Message = exception is AssertionException ? exception.Message.Trim() : $"{exception.GetType().Name}: {exception.Message}";
            Debug.LogWarning($"{Title(Lab)} lab: {round.Name} failed. {round.Message}", this);
        }

        private void ApplyPace() => Application.targetFrameRate = Fast || FramesPerSecond <= 0 ? -1 : FramesPerSecond;

        private void Pace(int step)
        {
            int at = Array.IndexOf(Paces, FramesPerSecond);
            if (at < 0) at = Array.IndexOf(Paces, 12);
            FramesPerSecond = Paces[Mathf.Clamp(at + step, 0, Paces.Length - 1)];
            ApplyPace();
        }

        private void OnDestroy()
        {
            Watching = false;
            Application.targetFrameRate = previousFrameRate;
            QualitySettings.vSyncCount = previousVSync;
            styles?.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The entities, in the scene
        // ═══════════════════════════════════════════════════════════════════════════

        private void LateUpdate()
        {
            ReadEntities();
            if (ShowBodies) PlaceBodies();
            FrameCamera();
        }

        // The entities ProcessControl holds, of either kind, as the Debugger lists them, but the Game Root
        private void ReadEntities()
        {
            entities.Clear();
            var control = ProcessControl.Instance;
            if (control == null) return;
            foreach (var pcb in control.FetchActiveProcesses().Values)
            {
                if (pcb?.Wrapper == null || !pcb.Wrapper.TryGetProcess<IGameplayAbilitySystem>(out var gas)) continue;
                if (gas is GameRoot || !Alive(gas) || entities.Contains(gas)) continue;
                entities.Add(gas);
            }
        }

        private static bool Alive(IGameplayAbilitySystem gas) => gas is Object unity ? unity != null : gas != null;

        // A capsule for each entity in the scene, beside any other standing in the same place, since many labs put theirs
        // all at the origin. It's only to be seen: it has no collider for the labs' own reach checks to find
        private void PlaceBodies()
        {
            foreach (var gone in bodies.Keys.Where(gas => !entities.Contains(gas)).ToList())
            {
                if (bodies[gone] != null) Destroy(bodies[gone].gameObject);
                bodies.Remove(gone);
            }

            var standing = new Dictionary<Vector3Int, int>();
            foreach (var gas in entities)
            {
                var host = gas.ToGASObject();
                if (host == null) continue;
                if (!bodies.TryGetValue(gas, out var body) || body == null)
                {
                    var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    capsule.name = "Lab Body";
                    DestroyImmediate(capsule.GetComponent<Collider>());
                    capsule.transform.SetParent(host.transform, false);
                    bodies[gas] = body = capsule.transform;
                }

                var place = Vector3Int.RoundToInt(host.transform.position);
                standing.TryGetValue(place, out int beside);
                standing[place] = beside + 1;
                body.localPosition = new Vector3(beside * 1.5f, 1f, 0f);
            }
        }

        // Keeps every capsule in view, or every entity in the scene where the lab draws its own, from above and in front
        private void FrameCamera()
        {
            var eye = Camera.main;
            if (eye == null) return;
            var bounds = new Bounds(Vector3.up, Vector3.one * 4f);
            bool first = true;
            void Take(Vector3 at)
            {
                if (first)
                {
                    bounds = new Bounds(at, Vector3.one * 2f);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(at);
                }
            }

            if (ShowBodies)
            {
                foreach (var body in bodies.Values)
                    if (body != null) Take(body.position);
            }
            else
            {
                foreach (var gas in entities)
                {
                    var host = gas.ToGASObject();
                    if (host != null) Take(host.transform.position);
                }
            }

            float distance = Mathf.Max(8f, bounds.extents.magnitude * Framing);
            var at = bounds.center + new Vector3(0f, 0.75f, -1f).normalized * distance;
            float blend = Fast ? 1f : 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            eye.transform.position = Vector3.Lerp(eye.transform.position, at, blend);
            eye.transform.rotation = Quaternion.Slerp(eye.transform.rotation, Quaternion.LookRotation(bounds.center - at), blend);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // The panel and the cards
        // ═══════════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (Application.isBatchMode) return;
            styles ??= new Styles();
            DrawLabels();
            DrawRounds();
            if (ShowCards) DrawCards();
        }

        // Each capsule's name, and its Health where it has one
        private void DrawLabels()
        {
            var eye = Camera.main;
            if (eye == null) return;
            foreach (var pair in bodies)
            {
                if (pair.Value == null) continue;
                var point = eye.WorldToScreenPoint(pair.Value.position + Vector3.up * 1.4f);
                if (point.z <= 0f) continue;
                var rect = new Rect(point.x - 70f, Screen.height - point.y - 24f, 140f, 18f);
                GUI.Label(rect, NameOf(pair.Key), styles.Centered);
                if (TryHealth(pair.Key, out var health)) Bar(new Rect(rect.x + 20f, rect.yMax + 1f, 100f, 6f), health.ClampedRatio);
            }
        }

        // The lab, its rounds and how each went, and the pace
        private void DrawRounds()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 400f, Screen.height - 24f));
            GUILayout.BeginVertical(styles.Panel);
            GUILayout.Label($"{Title(Lab)} lab", styles.Title);
            int done = rounds.Count(round => round.State is ERoundState.Passed or ERoundState.Failed or ERoundState.Skipped);
            GUILayout.Label(Finished ? $"{rounds.Count} rounds played" : $"Round {Mathf.Min(done + 1, rounds.Count)} of {rounds.Count}", styles.Dim);
            GUILayout.Space(4f);
            foreach (var round in rounds)
            {
                GUILayout.Label($"{Mark(round.State)}  {round.Name}", round == playing ? styles.Playing : styles.Text);
                if (!string.IsNullOrEmpty(round.Message) && round.State is ERoundState.Failed or ERoundState.Skipped)
                    GUILayout.Label(round.Message, styles.Message);
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Fast || FramesPerSecond <= 0 ? "As fast as it can" : $"{FramesPerSecond} frames a second", styles.Dim);
            if (GUILayout.Button("Slower", GUILayout.Width(64f))) Pace(-1);
            if (GUILayout.Button("Faster", GUILayout.Width(64f))) Pace(+1);
            GUILayout.EndHorizontal();
            GUILayout.Label("The editor's Pause and Step go a frame at a time. Window > PlayForge > Debugger shows more.", styles.Dim);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private static string Mark(ERoundState state) => state switch
        {
            ERoundState.Playing => "<color=#ffffff>playing</color>",
            ERoundState.Passed => "<color=#8fd18f>passed</color>",
            ERoundState.Failed => "<color=#f08a80>failed</color>",
            ERoundState.Skipped => "<color=#d9c36a>skipped</color>",
            _ => "<color=#8a8a8a>waiting</color>",
        };

        // A card for each entity: its attributes, its effects and its tags
        private void DrawCards()
        {
            const float width = 320f;
            GUILayout.BeginArea(new Rect(Screen.width - width - 12f, 12f, width, Screen.height - 24f));
            cardsScroll = GUILayout.BeginScrollView(cardsScroll);
            if (entities.Count == 0) GUILayout.Label(playing != null ? "No entities yet" : "Between rounds", styles.Dim);
            for (int i = 0; i < entities.Count; i++)
            {
                if (i == MaxCards)
                {
                    GUILayout.Label($"and {entities.Count - MaxCards} more", styles.Dim);
                    break;
                }

                DrawCard(entities[i]);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCard(IGameplayAbilitySystem gas)
        {
            GUILayout.BeginVertical(styles.Panel);
            GUILayout.Label($"<b>{NameOf(gas)}</b>  <color=#9a9a9a>{(gas.ToGASObject() != null ? "Mono" : "Runtime")}</color>", styles.Text);

            var cache = gas.GetAttributeSystem()?.GetAttributeCache();
            if (cache != null)
            {
                foreach (var pair in cache)
                {
                    var value = pair.Value.ActiveValue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(pair.Key.GetName(), styles.Text, GUILayout.Width(96f));
                    var rect = GUILayoutUtility.GetRect(100f, 18f, GUILayout.Width(100f));
                    Bar(new Rect(rect.x, rect.y + 6f, rect.width, 6f), value.ClampedRatio);
                    GUILayout.Label($"{Number(value.CurrentValue)} / {Number(value.BaseValue)}", styles.Dim);
                    GUILayout.EndHorizontal();
                }
            }

            var shelf = gas.GetEffectSystem()?.Shelf;
            if (shelf != null)
            {
                foreach (var container in shelf)
                {
                    var effect = container?.Spec?.Base;
                    if (effect == null) continue;
                    int stacks = container is AbstractStackingEffectContainer stacking ? stacking.Stacks : 1;
                    var left = effect.DurationSpecification?.DurationPolicy == EEffectDurationPolicy.Infinite
                        ? "on"
                        : $"{Number(container.DurationRemaining)} s left";
                    GUILayout.Label($"{effect.GetName()}{(stacks > 1 ? $" ×{stacks}" : "")}  <color=#9a9a9a>{left}</color>", styles.Text);
                }
            }

            var tags = gas.GetTagCache()?.GetAppliedTags();
            if (tags is { Count: > 0 })
            {
                var shown = string.Join(", ", tags.Take(6).Select(tag => tag.Name));
                GUILayout.Label(tags.Count > 6 ? $"{shown} and {tags.Count - 6} more" : shown, styles.Dim);
            }

            GUILayout.EndVertical();
            GUILayout.Space(6f);
        }

        private void Bar(Rect rect, float ratio)
        {
            GUI.DrawTexture(rect, styles.Track);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), styles.Fill);
        }

        private static bool TryHealth(IGameplayAbilitySystem gas, out AttributeValue health)
        {
            health = default;
            var cache = gas.GetAttributeSystem()?.GetAttributeCache();
            if (cache == null) return false;
            foreach (var pair in cache)
            {
                if (pair.Key.GetName() != "Health") continue;
                health = pair.Value.ActiveValue;
                return true;
            }
            return false;
        }

        private static string NameOf(IGameplayAbilitySystem gas)
        {
            var host = gas.ToGASObject();
            if (host != null) return host.name;
            var name = gas.GetName();
            return string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
        }

        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // The panel's look: dark cards with light text, and a bar's track and fill
        private sealed class Styles : IDisposable
        {
            public readonly GUIStyle Panel, Title, Text, Dim, Message, Playing, Centered;
            public readonly Texture2D Fill, Track;
            private readonly Texture2D background;

            public Styles()
            {
                Fill = Solid(new Color(0.42f, 0.74f, 0.47f));
                Track = Solid(new Color(0.2f, 0.2f, 0.2f, 0.9f));
                background = Solid(new Color(0.09f, 0.09f, 0.09f, 0.82f));

                Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8), alignment = TextAnchor.UpperLeft };
                Panel.normal.background = background;
                Text = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 };
                Text.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
                Title = new GUIStyle(Text) { fontSize = 17, fontStyle = FontStyle.Bold };
                Dim = new GUIStyle(Text) { fontSize = 12, wordWrap = true };
                Dim.normal.textColor = new Color(0.62f, 0.62f, 0.62f);
                Message = new GUIStyle(Dim);
                Message.normal.textColor = new Color(0.94f, 0.6f, 0.55f);
                Playing = new GUIStyle(Text) { fontStyle = FontStyle.Bold };
                Centered = new GUIStyle(Text) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            }

            private static Texture2D Solid(Color color)
            {
                var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                texture.SetPixel(0, 0, color);
                texture.Apply();
                return texture;
            }

            public void Dispose()
            {
                Object.Destroy(Fill);
                Object.Destroy(Track);
                Object.Destroy(background);
            }
        }
    }
}
