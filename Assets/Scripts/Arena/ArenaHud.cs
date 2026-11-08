using System.Text;
using FriendlyPersonShooter.Core.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The in-game HUD, built from code: the expedition panel (top left) with chat under it, health and shield bars
    /// with active buffs (bottom left), the payload (bottom centre), the team (right) and running totals (bottom
    /// right). The view fills the text builders each frame and calls <see cref="Apply"/>. How-to-play text lives in
    /// the Esc menu, not here.
    /// </summary>
    public sealed class ArenaHud
    {
        public readonly StringBuilder Expedition = new StringBuilder();
        public readonly StringBuilder Chat = new StringBuilder();
        public readonly StringBuilder Buffs = new StringBuilder();
        public readonly StringBuilder Team = new StringBuilder();
        public readonly StringBuilder Stats = new StringBuilder();

        public Text Crosshair, Center;

        private readonly Text _expedition, _chat, _buffs, _team, _stats, _health, _payloadName, _payloadInfo, _chain, _prompt;
        private readonly GameObject _expeditionBox, _teamBox, _vitalsBox;
        private readonly RectTransform _healthFill, _shieldFill;
        private readonly Image _healthImage, _payloadStripe;

        private const float BarWidth = 380f;
        private const float MapSize = 220f;
        private const int MapDots = 12;
        private const int Numbers = 24;

        private readonly RectTransform _canvasRect;
        private readonly RectTransform _xpFill;
        private readonly Text _xp, _hurtText;
        private readonly Image _hurtFlash;
        private readonly RectTransform _hurtArrow;
        private readonly Image[] _hurtArrowParts;
        private readonly GameObject _mapBox;
        private readonly Image[] _mapDots = new Image[MapDots];
        /// <summary>Each player dot's arrow nose, shown when the dot is pinned to the map's edge.</summary>
        private readonly Image[] _mapNoses = new Image[MapDots];
        private const int Pings = 8;
        private readonly Image[] _pingDots = new Image[Pings];
        private readonly RectTransform[] _pingMarks = new RectTransform[Pings];
        private readonly Image[] _pingDiamonds = new Image[Pings];
        private readonly Image[] _pingCores = new Image[Pings];
        private readonly Text[] _pingLabels = new Text[Pings];
        private const int EnemyDots = 48;
        private readonly Image[] _enemyDots = new Image[EnemyDots];
        private readonly Text[] _numbers = new Text[Numbers];

        public ArenaHud(Transform parent)
        {
            var canvas = SessionUi.Canvas(parent, "HUD", 10).transform;
            _canvasRect = (RectTransform)canvas;
            var tl = new Vector2(0f, 1f);
            var bl = new Vector2(0f, 0f);
            var br = new Vector2(1f, 0f);
            var tr = new Vector2(1f, 1f);
            var c = new Vector2(0.5f, 0.5f);
            var panel = new Color(0.05f, 0.04f, 0.12f, 0.55f);

            // Red edge flash when an enemy hits you (drawn first, behind the rest of the HUD).
            _hurtFlash = SessionUi.Box(canvas, "Hurt", c, Vector2.zero, Vector2.zero, new Color(1f, 0f, 0f, 0f));
            var flashRect = _hurtFlash.rectTransform;
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.sizeDelta = Vector2.zero;
            _hurtFlash.sprite = HurtVignette();

            // Expedition, top left.
            var exp = SessionUi.Box(canvas, "Expedition", tl, new Vector2(20, -20), new Vector2(620, 190), panel);
            _expeditionBox = exp.gameObject;
            _expedition = Text(exp.transform, 22, tl, new Vector2(16, -12), new Vector2(590, 170), TextAnchor.UpperLeft);
            _chat = Text(canvas, 21, tl, new Vector2(24, -224), new Vector2(700, 260), TextAnchor.UpperLeft);

            // Health and shield, bottom left, with buffs above.
            var vitals = SessionUi.Box(canvas, "Vitals", bl, new Vector2(20, 20), new Vector2(BarWidth + 32, 124), panel);
            _vitalsBox = vitals.gameObject;
            SessionUi.Box(vitals.transform, "Health back", tl, new Vector2(16, -16), new Vector2(BarWidth, 34), new Color(0f, 0f, 0f, 0.5f));
            _healthImage = SessionUi.Box(vitals.transform, "Health", tl, new Vector2(16, -16), new Vector2(BarWidth, 34), new Color(0.35f, 0.9f, 0.45f));
            _healthFill = _healthImage.rectTransform;
            _health = Text(vitals.transform, 22, tl, new Vector2(16, -16), new Vector2(BarWidth, 34), TextAnchor.MiddleCenter, FontStyle.Bold);
            SessionUi.Box(vitals.transform, "Shield back", tl, new Vector2(16, -58), new Vector2(BarWidth, 16), new Color(0f, 0f, 0f, 0.5f));
            _shieldFill = SessionUi.Box(vitals.transform, "Shield", tl, new Vector2(16, -58), new Vector2(0, 16), new Color(0.35f, 0.85f, 1f)).rectTransform;
            // Level and XP under the health and shield bars.
            SessionUi.Box(vitals.transform, "XP back", tl, new Vector2(16, -84), new Vector2(BarWidth, 26), new Color(0f, 0f, 0f, 0.5f));
            _xpFill = SessionUi.Box(vitals.transform, "XP", tl, new Vector2(16, -84), new Vector2(0, 26), new Color(0.75f, 0.55f, 1f, 0.85f)).rectTransform;
            _xp = Text(vitals.transform, 18, tl, new Vector2(16, -84), new Vector2(BarWidth, 26), TextAnchor.MiddleCenter, FontStyle.Bold);
            _buffs = Text(canvas, 21, bl, new Vector2(24, 154), new Vector2(760, 150), TextAnchor.LowerLeft);

            // The gun in hand, bottom centre (stripe in its payload's colour), with the chain status and the pickup prompt above it.
            var pay = SessionUi.Box(canvas, "Payload", new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(620, 112), panel);
            _payloadStripe = SessionUi.Box(pay.transform, "Stripe", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(10, 112), Color.white);
            _payloadName = Text(pay.transform, 28, new Vector2(0.5f, 1f), new Vector2(0, -4), new Vector2(590, 38), TextAnchor.MiddleCenter, FontStyle.Bold);
            _payloadInfo = Text(pay.transform, 18, new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(590, 66), TextAnchor.MiddleCenter);
            _chain = Text(canvas, 24, new Vector2(0.5f, 0f), new Vector2(0, 138), new Vector2(900, 34), TextAnchor.MiddleCenter, FontStyle.Bold);
            _prompt = Text(canvas, 22, new Vector2(0.5f, 0f), new Vector2(0, 176), new Vector2(1100, 100), TextAnchor.LowerCenter, FontStyle.Bold);

            // Team, right side (under the join code), and totals, bottom right.
            var team = SessionUi.Box(canvas, "Team", tr, new Vector2(-20, -130), new Vector2(380, 250), panel);
            _teamBox = team.gameObject;
            _team = Text(team.transform, 21, tl, new Vector2(14, -10), new Vector2(356, 236), TextAnchor.UpperLeft);
            _stats = Text(canvas, 19, br, new Vector2(-24, 20), new Vector2(560, 90), TextAnchor.LowerRight);

            // Minimap above the totals: players and enemies, turning with you (up is where you face).
            var map = SessionUi.Box(canvas, "Minimap", br, new Vector2(-24, 118), new Vector2(MapSize, MapSize), panel);
            _mapBox = map.gameObject;
            SessionUi.Box(map.transform, "Cross", c, Vector2.zero, new Vector2(2, MapSize - 16), new Color(1f, 1f, 1f, 0.12f));
            SessionUi.Box(map.transform, "Cross", c, Vector2.zero, new Vector2(MapSize - 16, 2), new Color(1f, 1f, 1f, 0.12f));
            Text(map.transform, 14, new Vector2(1f, 0f), new Vector2(-6, 4), new Vector2(120, 20), TextAnchor.LowerRight).text = $"<color=#B8B0D8>{MapRange:0} m</color>";
            // Enemies first, so player dots draw on top.
            for (int i = 0; i < EnemyDots; i++)
            {
                _enemyDots[i] = SessionUi.Box(map.transform, "Enemy", c, Vector2.zero, new Vector2(6, 6), Color.white);
                _enemyDots[i].rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
                _enemyDots[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MapDots; i++)
            {
                _mapDots[i] = SessionUi.Box(map.transform, "Dot", c, Vector2.zero, new Vector2(12, 12), Color.white);
                _mapNoses[i] = SessionUi.Box(_mapDots[i].transform, "Nose", c, new Vector2(0, 7), new Vector2(4, 9), Color.white);
                _mapDots[i].gameObject.SetActive(false);
            }
            // Pings: a hollow diamond in the pinger's color (pinned to the edge when far away).
            for (int i = 0; i < Pings; i++)
            {
                _pingDots[i] = SessionUi.Box(map.transform, "Ping", c, Vector2.zero, new Vector2(14, 14), Color.white);
                _pingDots[i].rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
                SessionUi.Box(_pingDots[i].transform, "Hole", c, Vector2.zero, new Vector2(7, 7), new Color(0.05f, 0.04f, 0.12f, 0.9f));
                _pingDots[i].gameObject.SetActive(false);
            }
            // You: a white diamond with a nose pointing up in the middle.
            SessionUi.Box(map.transform, "You", c, Vector2.zero, new Vector2(12, 12), Color.white).rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            SessionUi.Box(map.transform, "You nose", c, new Vector2(0, 11), new Vector2(4, 12), Color.white);

            // Floating damage numbers, positioned over the world each frame.
            for (int i = 0; i < Numbers; i++)
            {
                _numbers[i] = Text(canvas, 26, c, Vector2.zero, new Vector2(200, 40), TextAnchor.MiddleCenter, FontStyle.Bold);
                _numbers[i].gameObject.SetActive(false);
            }

            // Ping markers over the world (on top of everything): a diamond in the pinger's color and a distance.
            for (int i = 0; i < Pings; i++)
            {
                _pingMarks[i] = SessionUi.Rect(canvas, "Ping mark", c, Vector2.zero, new Vector2(40, 40));
                _pingDiamonds[i] = SessionUi.Box(_pingMarks[i], "Diamond", c, Vector2.zero, new Vector2(26, 26), Color.white);
                _pingDiamonds[i].rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
                _pingCores[i] = SessionUi.Box(_pingDiamonds[i].transform, "Core", c, Vector2.zero, new Vector2(12, 12), Color.white);
                _pingLabels[i] = Text(_pingMarks[i], 20, c, new Vector2(0, -32), new Vector2(220, 28), TextAnchor.MiddleCenter, FontStyle.Bold);
                _pingLabels[i].gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
                _pingMarks[i].gameObject.SetActive(false);
            }

            // Which way the hit came from: a red marker around the crosshair, and what hit you.
            _hurtArrow = SessionUi.Rect(canvas, "Hurt direction", c, Vector2.zero, new Vector2(10, 10));
            _hurtArrowParts = new[]
            {
                SessionUi.Box(_hurtArrow, "Bar", c, new Vector2(0, 150), new Vector2(120, 16), new Color(1f, 0.2f, 0.15f)),
                SessionUi.Box(_hurtArrow, "Tip", c, new Vector2(0, 170), new Vector2(56, 14), new Color(1f, 0.2f, 0.15f)),
                SessionUi.Box(_hurtArrow, "Point", c, new Vector2(0, 186), new Vector2(18, 12), new Color(1f, 0.2f, 0.15f)),
            };
            _hurtArrow.gameObject.SetActive(false);
            _hurtText = Text(canvas, 30, c, new Vector2(0, 230), new Vector2(900, 44), TextAnchor.MiddleCenter, FontStyle.Bold);

            Crosshair = Text(canvas, 30, c, Vector2.zero, new Vector2(700, 60), TextAnchor.MiddleCenter);
            Crosshair.text = "+";
            Center = Text(canvas, 28, c, new Vector2(0, -120), new Vector2(1400, 200), TextAnchor.UpperCenter, FontStyle.Bold);

            // The HUD never takes clicks: clicking anywhere captures the mouse.
            foreach (var g in canvas.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }

        private static Text Text(Transform parent, int size, Vector2 anchor, Vector2 pos, Vector2 box, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            var t = SessionUi.Label(parent, "", size, anchor, pos, box, align, Color.white, style);
            t.supportRichText = true;
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            return t;
        }

        public void Clear()
        {
            Expedition.Clear();
            Chat.Clear();
            Buffs.Clear();
            Team.Clear();
            Stats.Clear();
        }

        public void SetVitals(bool show, float health, float max, float shield)
        {
            _vitalsBox.SetActive(show);
            if (!show) return;
            float f = max > 0f ? Mathf.Clamp01(health / max) : 0f;
            _healthFill.sizeDelta = new Vector2(BarWidth * f, _healthFill.sizeDelta.y);
            _healthImage.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), new Color(0.35f, 0.9f, 0.45f), Mathf.InverseLerp(0.2f, 0.6f, f));
            _shieldFill.sizeDelta = new Vector2(max > 0f ? BarWidth * Mathf.Clamp01(shield / max) : 0f, _shieldFill.sizeDelta.y);
            _health.text = shield > 0f ? $"{health:0} / {max:0}   <color=#7FDBFF>+{shield:0}</color>" : $"{health:0} / {max:0}";
        }

        public const float MapRange = 120f;

        public void SetProgress(bool show, int level, int xp, int next, int points)
        {
            if (!show)
            {
                _xp.text = "";
                _xpFill.sizeDelta = new Vector2(0f, _xpFill.sizeDelta.y);
                return;
            }
            float f = next > 0 ? Mathf.Clamp01(xp / (float)next) : 1f;
            _xpFill.sizeDelta = new Vector2(BarWidth * f, _xpFill.sizeDelta.y);
            _xp.text = $"Level {level}   {xp} / {next} XP" + (points > 0 ? $"   <color=#FFD84D>{points} skill point{(points > 1 ? "s" : "")}: Esc</color>" : "");
        }

        /// <summary>
        /// A player dot on the minimap: <paramref name="offset"/> is metres from you, already turned so +y is the way
        /// you face. Dots past the edge sit on the edge.
        /// </summary>
        public void SetMapDot(int index, bool show, Vector2 offset = default, Color color = default)
        {
            if (index >= MapDots) return;
            var dot = _mapDots[index];
            dot.gameObject.SetActive(show);
            if (!show) return;
            float radius = MapSize * 0.5f - 10f;
            var p = offset / MapRange * radius;
            bool edge = p.magnitude > radius;
            if (edge) p = p.normalized * radius;
            dot.rectTransform.anchoredPosition = p;
            // Past the edge: a small arrow on the rim pointing their way.
            dot.rectTransform.sizeDelta = edge ? new Vector2(10, 10) : new Vector2(13, 13);
            dot.rectTransform.localRotation = edge ? Quaternion.Euler(0, 0, Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg - 90f) : Quaternion.identity;
            dot.color = color;
            _mapNoses[index].gameObject.SetActive(edge);
            _mapNoses[index].color = color;
        }

        public static int PingSlots => Pings;

        /// <summary>A ping on the minimap: <paramref name="offset"/> as for <see cref="SetMapDot"/>; pinned to the edge when far.</summary>
        public void SetPingDot(int index, bool show, Vector2 offset = default, Color color = default)
        {
            if (index >= Pings) return;
            var dot = _pingDots[index];
            dot.gameObject.SetActive(show);
            if (!show) return;
            float radius = MapSize * 0.5f - 10f;
            var p = offset / MapRange * radius;
            if (p.magnitude > radius) p = p.normalized * radius;
            dot.rectTransform.anchoredPosition = p;
            dot.color = color;
        }

        /// <summary>
        /// A ping marker at a screen point (already pulled onto the screen's edge when the spot is off screen), or
        /// hides slot <paramref name="index"/> when <paramref name="label"/> is null. <paramref name="pop"/> grows it briefly.
        /// </summary>
        public void SetPing(int index, string label, Vector2 screen = default, Color color = default, float alpha = 1f, float pop = 1f)
        {
            if (index >= Pings) return;
            var mark = _pingMarks[index];
            mark.gameObject.SetActive(label != null);
            if (label == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            mark.anchoredPosition = local;
            mark.localScale = Vector3.one * pop;
            _pingDiamonds[index].color = new Color(color.r, color.g, color.b, alpha);
            _pingCores[index].color = new Color(1f, 1f, 1f, alpha);
            _pingLabels[index].text = label;
            _pingLabels[index].color = new Color(1f, 1f, 1f, alpha);
        }

        public static int MapDotSlots => MapDots;
        public static int EnemyDotSlots => EnemyDots;

        /// <summary>A small enemy diamond on the minimap (hidden past the map's edge). <paramref name="size"/> in pixels.</summary>
        public bool SetEnemyDot(int index, bool show, Vector2 offset = default, Color color = default, float size = 6f)
        {
            if (index >= EnemyDots) return false;
            var dot = _enemyDots[index];
            float radius = MapSize * 0.5f - 6f;
            var p = offset / MapRange * radius;
            show &= p.magnitude <= radius;
            dot.gameObject.SetActive(show);
            if (!show) return false;
            dot.rectTransform.anchoredPosition = p;
            dot.rectTransform.sizeDelta = new Vector2(size, size);
            dot.color = color;
            return true;
        }

        public void ShowMap(bool show) => _mapBox.SetActive(show);

        /// <summary>A floating number at a screen point, or hides slot <paramref name="index"/> when <paramref name="text"/> is null.</summary>
        public void SetNumber(int index, string text, Vector2 screen = default, Color color = default, float scale = 1f)
        {
            if (index >= Numbers) return;
            var t = _numbers[index];
            t.gameObject.SetActive(text != null);
            if (text == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            t.rectTransform.anchoredPosition = local;
            t.rectTransform.localScale = Vector3.one * scale;
            t.text = text;
            t.color = color;
        }

        public static int NumberSlots => Numbers;

        /// <summary>
        /// Hit warning: <paramref name="flash"/> 0..1 reddens the screen edges; <paramref name="angle"/> (degrees,
        /// 0 = ahead, clockwise) points the marker at the attacker, null hides it.
        /// </summary>
        public void SetHurt(float flash, float? angle, float arrowAlpha, string text)
        {
            _hurtFlash.color = new Color(1f, 0.05f, 0.05f, 0.6f * Mathf.Clamp01(flash));
            bool arrow = angle.HasValue && arrowAlpha > 0f;
            _hurtArrow.gameObject.SetActive(arrow);
            if (arrow)
            {
                _hurtArrow.localRotation = Quaternion.Euler(0f, 0f, -angle.Value);
                foreach (var part in _hurtArrowParts) part.color = new Color(1f, 0.2f, 0.15f, Mathf.Clamp01(arrowAlpha));
            }
            _hurtText.text = text ?? "";
        }

        /// <summary>A soft frame: clear in the middle, opaque at the screen edges.</summary>
        private static Sprite HurtVignette()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - size * 0.5f) / (size * 0.5f);
                    float dy = Mathf.Abs(y + 0.5f - size * 0.5f) / (size * 0.5f);
                    float d = Mathf.Max(dx, dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, d));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>The gun panel: a title line (gun, payload, ammo), a detail block (mods, other gun) and the chain line.</summary>
        public void SetGun(PayloadKind payload, string title, string info, string chain)
        {
            _payloadStripe.color = PayloadColor(payload);
            _payloadName.text = title;
            _payloadInfo.text = info;
            _chain.text = chain ?? "";
        }

        /// <summary>What E would do here (open a box, take an item); empty for nothing.</summary>
        public void SetPrompt(string prompt) => _prompt.text = prompt ?? "";

        public void Apply()
        {
            _expedition.text = Expedition.ToString();
            _expeditionBox.SetActive(Expedition.Length > 0);
            Fit(_expeditionBox, _expedition);
            _chat.text = Chat.ToString();
            _buffs.text = Buffs.ToString();
            _team.text = Team.ToString();
            _teamBox.SetActive(Team.Length > 0);
            Fit(_teamBox, _team);
            // Under the host's join code when there is one, otherwise in the corner.
            var teamRect = (RectTransform)_teamBox.transform;
            teamRect.anchoredPosition = new Vector2(-20, PlaySession.JoinCode != null ? -130 : -20);
            _stats.text = Stats.ToString();
        }

        /// <summary>Sizes a panel to its text (plus padding).</summary>
        private static void Fit(GameObject box, Text text)
        {
            if (!box.activeSelf) return;
            var rect = (RectTransform)box.transform;
            float h = text.preferredHeight + 24f;
            if (Mathf.Abs(rect.sizeDelta.y - h) > 1f) rect.sizeDelta = new Vector2(rect.sizeDelta.x, h);
        }

        public static Color PayloadColor(PayloadKind p)
        {
            switch (p)
            {
                case PayloadKind.Vigor: return new Color(0.4f, 1f, 0.55f);
                case PayloadKind.Haste: return new Color(0.4f, 0.85f, 1f);
                case PayloadKind.Relay: return new Color(0.85f, 0.55f, 1f);
                case PayloadKind.None: return new Color(0.75f, 0.75f, 0.75f);
                default: return new Color(1f, 0.6f, 0.3f);
            }
        }

        /// <summary>What a payload does, one short line.</summary>
        public static string PayloadShort(PayloadKind p)
        {
            switch (p)
            {
                case PayloadKind.Vigor: return "Allies: heal + shield     Enemies: wither";
                case PayloadKind.Haste: return "Allies: +30% speed, +20% jump     Enemies: slow";
                case PayloadKind.Relay: return "Allies: next 5 hits carry your other payload     Enemies: tag (splash)";
                case PayloadKind.None: return "No payload: damages enemies, heals allies a tiny bit. Find a payload mod to add one.";
                default: return "Allies: +damage     Enemies: expose";
            }
        }
    }
}
