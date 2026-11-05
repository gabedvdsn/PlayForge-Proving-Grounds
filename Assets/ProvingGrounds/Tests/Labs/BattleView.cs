using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// What the Battle lab draws over its fighters: a health bar over each one's head in its side's color, and above it a
    /// small box for each effect on it. Positive effects line up from the bar's left, in their colors, and negative ones
    /// from its right, each with a dark edge. A scoreboard says who stands and for how long the battle has gone, and a
    /// legend names each box's color.
    /// </summary>
    public sealed class BattleView : MonoBehaviour
    {
        private const float BarWidth = 56f, BarHeight = 7f, Box = 9f, Gap = 2f;

        public BattleLab Lab;

        private readonly List<(BattleLab.Badge Badge, int Count)> good = new(), bad = new();
        private Texture2D white;
        private GUIStyle score, legend, count;

        private void OnGUI()
        {
            if (Application.isBatchMode || Lab == null) return;
            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
                score = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
                legend = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
                legend.normal.textColor = new Color(0.88f, 0.88f, 0.88f);
                count = new GUIStyle(GUI.skin.label) { fontSize = 8, alignment = TextAnchor.MiddleCenter };
                count.normal.textColor = Color.black;
            }

            var eye = Camera.main;
            if (eye != null)
            {
                foreach (var fighter in Lab.Fighters)
                {
                    if (fighter == null || !fighter.Alive) continue;
                    DrawOver(eye, fighter);
                }
            }

            DrawScore();
            DrawLegend();
        }

        // A fighter's bar over its head, and its effects' boxes over the bar
        private void DrawOver(Camera eye, BattleFighter fighter)
        {
            // Over the head and its mark
            float height = fighter.Body != null ? fighter.Body.lossyScale.y * 2.6f : 2.6f;
            var point = eye.WorldToScreenPoint(fighter.Position + Vector3.up * height);
            if (point.z <= 0f) return;
            float width = fighter.Role == BattleLab.EClass.Imp ? BarWidth * 0.6f : BarWidth;
            var bar = new Rect(point.x - width / 2f, Screen.height - point.y, width, BarHeight);

            Fill(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), new Color(0f, 0f, 0f, 0.75f));
            Fill(bar, new Color(0.2f, 0.2f, 0.2f, 0.9f));
            float share = Mathf.Clamp01(fighter.HealthShare);
            Fill(new Rect(bar.x, bar.y, bar.width * share, bar.height), BattleLab.TeamColors[fighter.Team]);

            ReadEffects(fighter);
            float y = bar.y - Box - 3f;
            float x = bar.x;
            foreach (var (badge, stacks) in good)
            {
                DrawBox(new Rect(x, y, Box, Box), badge.Color, new Color(1f, 1f, 1f, 0.9f), stacks);
                x += Box + Gap;
            }
            x = bar.xMax - Box;
            foreach (var (badge, stacks) in bad)
            {
                DrawBox(new Rect(x, y, Box, Box), badge.Color, new Color(0f, 0f, 0f, 0.9f), stacks);
                x -= Box + Gap;
            }
        }

        // The fighter's effects that show, each once with how many of it there are
        private void ReadEffects(BattleFighter fighter)
        {
            good.Clear();
            bad.Clear();
            var shelf = fighter.Gas.GetEffectSystem()?.Shelf;
            if (shelf == null) return;
            foreach (var container in shelf)
            {
                var effect = container?.Spec?.Base;
                if (effect == null || !Lab.Badges.TryGetValue(effect, out var badge)) continue;
                var list = badge.Positive ? good : bad;
                int at = list.FindIndex(each => each.Badge.Name == badge.Name);
                if (at >= 0) list[at] = (badge, list[at].Count + 1);
                else list.Add((badge, 1));
            }
        }

        private void DrawBox(Rect rect, Color color, Color edge, int stacks)
        {
            Fill(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), edge);
            Fill(rect, color);
            if (stacks > 1) GUI.Label(rect, stacks.ToString(CultureInfo.InvariantCulture), count);
        }

        private void DrawScore()
        {
            var blue = ColorUtility.ToHtmlStringRGB(BattleLab.TeamColors[0]);
            var red = ColorUtility.ToHtmlStringRGB(BattleLab.TeamColors[1]);
            string line = $"<color=#{blue}>Blue {Lab.Standing(0)}</color>   {Lab.Elapsed.ToString("0.0", CultureInfo.InvariantCulture)} s   <color=#{red}>{Lab.Standing(1)} Red</color>";
            if (Lab.Over)
            {
                line += Lab.Winner switch
                {
                    0 => $"\n<color=#{blue}>Blue wins</color>",
                    1 => $"\n<color=#{red}>Red wins</color>",
                    _ => "\nNo one stands",
                };
            }

            var rect = new Rect(Screen.width / 2f - 200f, 10f, 400f, Lab.Over ? 56f : 30f);
            Fill(rect, new Color(0.08f, 0.08f, 0.08f, 0.75f));
            GUI.Label(rect, line, score);
        }

        private void DrawLegend()
        {
            var shown = new List<BattleLab.Badge>();
            foreach (var badge in Lab.Badges.Values)
                if (!shown.Exists(each => each.Name == badge.Name)) shown.Add(badge);

            float height = 20f + shown.Count * 15f;
            var area = new Rect(Screen.width / 2f - 90f, Screen.height - height - 10f, 180f, height);
            Fill(area, new Color(0.08f, 0.08f, 0.08f, 0.75f));
            GUI.Label(new Rect(area.x + 8f, area.y + 3f, area.width, 16f), "Over the bar: good from the left, bad from the right", legend);
            float y = area.y + 20f;
            foreach (var badge in shown)
            {
                DrawBox(new Rect(area.x + 10f, y + 2f, Box, Box), badge.Color, badge.Positive ? Color.white : Color.black, 1);
                GUI.Label(new Rect(area.x + 26f, y - 1f, area.width - 30f, 16f), $"{badge.Name}  <color=#9a9a9a>{(badge.Positive ? "good" : "bad")}</color>", legend);
                y += 15f;
            }
        }

        private void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            if (white != null) Destroy(white);
        }
    }

    /// <summary>A ring that spreads and fades where something happens: a burst, a nova, a heal, a summons or a fall.</summary>
    public sealed class BattleFlash : MonoBehaviour
    {
        private const int Points = 40;
        private static readonly List<BattleFlash> live = new();
        private static Material material;

        private LineRenderer line;
        private Color color;
        private float radius, life, age;

        /// <summary>A ring at a place, spreading to its radius over its life.</summary>
        public static void Ring(Vector3 at, float radius, Color color, float life)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            var host = new GameObject("Battle Flash");
            host.transform.position = at + Vector3.up * 0.08f;
            var flash = host.AddComponent<BattleFlash>();
            flash.color = color;
            flash.radius = radius;
            flash.life = life;
            flash.line = host.AddComponent<LineRenderer>();
            flash.line.sharedMaterial = material;
            flash.line.loop = true;
            flash.line.positionCount = Points;
            flash.line.useWorldSpace = false;
            flash.line.widthMultiplier = 0.12f;
            flash.Shape(0.1f);
            live.Add(flash);
        }

        /// <summary>Takes away every ring still there, as the battle ends.</summary>
        public static void ClearAll()
        {
            foreach (var flash in live.ToArray()) if (flash != null) Destroy(flash.gameObject);
            live.Clear();
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / life);
            Shape(Mathf.Lerp(0.15f, 1f, Mathf.Sqrt(t)));
            if (age < life) return;
            live.Remove(this);
            Destroy(gameObject);
        }

        private void Shape(float spread)
        {
            float fade = 1f - Mathf.Clamp01(age / life);
            var faded = new Color(color.r, color.g, color.b, fade);
            line.startColor = faded;
            line.endColor = faded;
            for (int i = 0; i < Points; i++)
            {
                float angle = i * Mathf.PI * 2f / Points;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radius * spread));
            }
        }
    }
}
