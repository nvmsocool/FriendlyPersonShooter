using System.Text;
using FriendlyPersonShooter.Core.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The in-game HUD, built from code and nearly free of text: an hourglass for the run's time left and a cluster of
    /// <see cref="Core.Expedition.EssenceWallet.CarryCap"/> slots per essence color (top left) with chat under it,
    /// health, shield and XP bars with a row of buff icons above them (bottom left), the chain you hold hanging from the top right (one
    /// link per payload that built it, in that payload's colour) and the minimap. The gun, the team and running
    /// totals show only beside the Esc menu. The view fills the text builders each frame and calls <see cref="Apply"/>.
    /// </summary>
    public sealed class ArenaHud
    {
        /// <summary>Expedition and raid lines the view still writes; no longer shown (the HUD is text-free).</summary>
        public readonly StringBuilder Expedition = new StringBuilder();
        public readonly StringBuilder Chat = new StringBuilder();
        public readonly StringBuilder Team = new StringBuilder();
        public readonly StringBuilder Stats = new StringBuilder();

        public Text Crosshair, Center;

        private readonly Text _chat, _team, _stats, _payloadName, _payloadInfo, _prompt;
        private readonly GameObject _teamBox, _vitalsBox, _gunBox, _statsBox, _clockBox;
        private readonly Image _sandTop, _sandBottom, _sandStream;
        private readonly Image[,] _slots = new Image[Core.Expedition.EssenceColors.Count, Core.Expedition.EssenceWallet.CarryCap];
        private readonly Image _xpImage;
        private readonly RectTransform _chainRoot;
        private readonly Image[] _links = new Image[Core.Combat.ChainTracker.PackedLinks];
        private readonly Image[] _linkShadows = new Image[Core.Combat.ChainTracker.PackedLinks];
        private int _shownLinks;

        /// <summary>The buffs shown as icons, each its payload's colour and its own shape.</summary>
        public enum BuffIcon { Might, Haste, Frenzy, Ward, Focus, Relay }
        public const int BuffIconCount = 6;
        private const int RelayPips = 5;
        private const float IconSize = 34f;
        private readonly RectTransform[] _buffIcons = new RectTransform[BuffIconCount];
        private readonly Image[] _buffGold = new Image[BuffIconCount];
        private readonly Image[] _buffFill = new Image[BuffIconCount];
        private readonly Image[,] _relayPips = new Image[BuffIconCount, RelayPips];
        /// <summary>Each buff's full length, learnt from the time left when it's (re)applied.</summary>
        private readonly float[] _buffFull = new float[BuffIconCount];
        private readonly float[] _buffLeft = new float[BuffIconCount];
        private float _linkAddedAt = -10f;
        private readonly RectTransform _healthFill, _shieldFill;
        private readonly Image _healthImage, _payloadStripe;

        private const float BarWidth = 380f;
        private const float MapSize = 220f;
        private const int MapDots = 12;
        private const int Numbers = 24;

        private readonly RectTransform _canvasRect;
        private readonly RectTransform _xpFill;
        private readonly Text _hurtText;
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

            // Top left: an hourglass (time left on the run), then a cluster of slots per essence color (carried).
            var clock = SessionUi.Box(canvas, "Expedition", tl, new Vector2(20, -20), new Vector2(ClockWidth, 88), panel);
            _clockBox = clock.gameObject;
            var glass = new Color(1f, 1f, 1f, 0.16f);
            var sand = new Color(1f, 0.85f, 0.4f);
            SessionUi.Box(clock.transform, "Glass", tl, new Vector2(16, -12), new Vector2(40, 64), glass).sprite = Hourglass(40, 64);
            _sandTop = SandBulb(clock.transform, new Vector2(16, -12), true, sand);
            _sandBottom = SandBulb(clock.transform, new Vector2(16, -44), false, sand);
            _sandStream = SessionUi.Box(clock.transform, "Stream", tl, new Vector2(35, -42), new Vector2(2, 28), sand);
            SessionUi.Box(clock.transform, "Cap", tl, new Vector2(12, -8), new Vector2(48, 5), new Color(0.85f, 0.75f, 0.6f));
            SessionUi.Box(clock.transform, "Cap", tl, new Vector2(12, -75), new Vector2(48, 5), new Color(0.85f, 0.75f, 0.6f));
            var dot = Circle();
            for (int ci = 0; ci < Core.Expedition.EssenceColors.Count; ci++)
            {
                var centre = new Vector2(ClockFirstCluster + ci * ClusterStep, -44);
                for (int k = 0; k < Core.Expedition.EssenceWallet.CarryCap; k++)
                {
                    // The centre slot fills first, then the ring clockwise from the top.
                    var offset = k == 0 ? Vector2.zero
                        : new Vector2(Mathf.Sin((k - 1) * Mathf.PI / 3f), Mathf.Cos((k - 1) * Mathf.PI / 3f)) * SlotRing;
                    var slot = SessionUi.Box(clock.transform, "Slot", tl, centre + offset, new Vector2(SlotSize, SlotSize), Color.white);
                    slot.sprite = dot;
                    slot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    _slots[ci, k] = slot;
                }
            }
            _chat = Text(canvas, 21, tl, new Vector2(24, -120), new Vector2(700, 260), TextAnchor.UpperLeft);

            // Health and shield, bottom left, with buffs above.
            var vitals = SessionUi.Box(canvas, "Vitals", bl, new Vector2(20, 20), new Vector2(BarWidth + 32, 124), panel);
            _vitalsBox = vitals.gameObject;
            SessionUi.Box(vitals.transform, "Health back", tl, new Vector2(16, -16), new Vector2(BarWidth, 34), new Color(0f, 0f, 0f, 0.5f));
            _healthImage = SessionUi.Box(vitals.transform, "Health", tl, new Vector2(16, -16), new Vector2(BarWidth, 34), new Color(0.35f, 0.9f, 0.45f));
            _healthFill = _healthImage.rectTransform;
            SessionUi.Box(vitals.transform, "Shield back", tl, new Vector2(16, -58), new Vector2(BarWidth, 16), new Color(0f, 0f, 0f, 0.5f));
            _shieldFill = SessionUi.Box(vitals.transform, "Shield", tl, new Vector2(16, -58), new Vector2(0, 16), new Color(0.35f, 0.85f, 1f)).rectTransform;
            // Level and XP under the health and shield bars.
            SessionUi.Box(vitals.transform, "XP back", tl, new Vector2(16, -84), new Vector2(BarWidth, 26), new Color(0f, 0f, 0f, 0.5f));
            _xpImage = SessionUi.Box(vitals.transform, "XP", tl, new Vector2(16, -84), new Vector2(0, 26), XpColor);
            _xpFill = _xpImage.rectTransform;
            // Buff icons in a row above the bars: a dim shape, the same shape filling clockwise with the time left,
            // and a gold backing when the buff is part of a combo. Relay shows dots for its hits left instead.
            var dotSprite = Circle();
            for (int i = 0; i < BuffIconCount; i++)
            {
                var kind = (BuffIcon)i;
                var color = PayloadColor(IconPayload(kind));
                var shape = IconShape(kind);
                var icon = SessionUi.Rect(canvas, "Buff " + kind, bl, Vector2.zero, new Vector2(IconSize, IconSize));
                _buffIcons[i] = icon;
                _buffGold[i] = SessionUi.Box(icon, "Combo", c, Vector2.zero, new Vector2(IconSize + 12, IconSize + 12), new Color(1f, 0.82f, 0.25f, 0.85f));
                _buffGold[i].sprite = dotSprite;
                SessionUi.Box(icon, "Back", c, Vector2.zero, new Vector2(IconSize, IconSize), new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.8f)).sprite = shape;
                var fill = SessionUi.Box(icon, "Fill", c, Vector2.zero, new Vector2(IconSize, IconSize), color);
                fill.sprite = shape;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = false;
                _buffFill[i] = fill;
                for (int k = 0; k < RelayPips; k++)
                {
                    var pip = SessionUi.Box(icon, "Pip", c, new Vector2((k - (RelayPips - 1) * 0.5f) * 8f, -IconSize * 0.5f - 7f), new Vector2(6, 6), color);
                    pip.sprite = dotSprite;
                    pip.gameObject.SetActive(false);
                    _relayPips[i, k] = pip;
                }
                icon.gameObject.SetActive(false);
            }

            // The chain you hold: links hanging from a hook in the top right, growing down the side of the screen.
            // Links alternate face-on and side-on, like a real chain.
            _chainRoot = SessionUi.Rect(canvas, "Chain", tr, new Vector2(-40, -16), new Vector2(LinkWidth, 40));
            SessionUi.Box(_chainRoot, "Hook", new Vector2(0.5f, 1f), new Vector2(0, 0), new Vector2(46, 8), new Color(0.75f, 0.72f, 0.8f, 0.9f));
            SessionUi.Box(_chainRoot, "Hook", new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(8, 14), new Color(0.75f, 0.72f, 0.8f, 0.9f));
            var ring = Ring((int)LinkWidth, (int)LinkHeight, 9f);
            var edge = Ring(24, (int)LinkHeight, 8f);
            for (int i = 0; i < _links.Length; i++)
            {
                bool side = i % 2 == 1;
                var size = side ? new Vector2(24, LinkHeight) : new Vector2(LinkWidth, LinkHeight);
                var pos = new Vector2(0, -16 - i * LinkStep);
                _linkShadows[i] = SessionUi.Box(_chainRoot, "Link shadow", new Vector2(0.5f, 1f), pos + new Vector2(2, -2), size, new Color(0f, 0f, 0f, 0.5f));
                _linkShadows[i].sprite = side ? edge : ring;
                _links[i] = SessionUi.Box(_chainRoot, "Link", new Vector2(0.5f, 1f), pos, size, Color.white);
                _links[i].sprite = side ? edge : ring;
                _links[i].rectTransform.pivot = new Vector2(0.5f, 1f);
            }
            _chainRoot.gameObject.SetActive(false);

            // The shoot prompt, bottom centre.
            _prompt = Text(canvas, 22, new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(1100, 100), TextAnchor.LowerCenter, FontStyle.Bold);

            // Beside the Esc menu only: the gun in hand (stripe in its payload's colour) and your totals on the left,
            // the team on the right (under the join code).
            var left = new Vector2(0f, 0.5f);
            var pay = SessionUi.Box(canvas, "Payload", left, new Vector2(20, 90), new Vector2(EscColumn, 210), panel);
            _gunBox = pay.gameObject;
            _payloadStripe = SessionUi.Box(pay.transform, "Stripe", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(10, 210), Color.white);
            _payloadName = Text(pay.transform, 28, new Vector2(0.5f, 1f), new Vector2(6, -8), new Vector2(EscColumn - 30, 38), TextAnchor.MiddleCenter, FontStyle.Bold);
            _payloadInfo = Text(pay.transform, 18, new Vector2(0.5f, 1f), new Vector2(6, -50), new Vector2(EscColumn - 30, 150), TextAnchor.UpperCenter);
            var stats = SessionUi.Box(canvas, "Totals", left, new Vector2(20, -100), new Vector2(EscColumn, 150), panel);
            _statsBox = stats.gameObject;
            _stats = Text(stats.transform, 19, tl, new Vector2(14, -10), new Vector2(EscColumn - 28, 130), TextAnchor.UpperLeft);
            var team = SessionUi.Box(canvas, "Team", tr, new Vector2(-20, -130), new Vector2(EscColumn, 250), panel);
            _teamBox = team.gameObject;
            _team = Text(team.transform, 21, tl, new Vector2(14, -10), new Vector2(EscColumn - 24, 236), TextAnchor.UpperLeft);

            // Minimap above the totals: players and enemies, turning with you (up is where you face).
            var map = SessionUi.Box(canvas, "Minimap", br, new Vector2(-24, 24), new Vector2(MapSize, MapSize), panel);
            _mapBox = map.gameObject;
            SessionUi.Box(map.transform, "Cross", c, Vector2.zero, new Vector2(2, MapSize - 16), new Color(1f, 1f, 1f, 0.12f));
            SessionUi.Box(map.transform, "Cross", c, Vector2.zero, new Vector2(MapSize - 16, 2), new Color(1f, 1f, 1f, 0.12f));
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
        }

        public const float MapRange = 120f;

        public void SetProgress(bool show, int level, int xp, int next, int points)
        {
            if (!show)
            {
                _xpFill.sizeDelta = new Vector2(0f, _xpFill.sizeDelta.y);
                return;
            }
            float f = next > 0 ? Mathf.Clamp01(xp / (float)next) : 1f;
            _xpFill.sizeDelta = new Vector2(BarWidth * f, _xpFill.sizeDelta.y);
            // Gold while there are skill points to spend (Esc).
            _xpImage.color = points > 0 ? new Color(1f, 0.82f, 0.3f, 0.95f) : XpColor;
        }

        /// <summary>
        /// The top-left clock and essence: <paramref name="timeLeft"/> 0..1 of the run left in the hourglass, and how much
        /// of each color is carried (by color index). Hidden when <paramref name="show"/> is false.
        /// </summary>
        public void SetExpedition(bool show, float timeLeft, int[] carried, Color[] colors)
        {
            _clockBox.SetActive(show);
            if (!show) return;
            timeLeft = Mathf.Clamp01(timeLeft);
            // The last tenth runs red.
            var sand = timeLeft < 0.1f ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.85f, 0.4f);
            _sandTop.fillAmount = timeLeft;
            _sandBottom.fillAmount = 1f - timeLeft;
            _sandTop.color = _sandBottom.color = _sandStream.color = sand;
            _sandStream.enabled = timeLeft > 0f;
            for (int ci = 0; ci < colors.Length && ci < _slots.GetLength(0); ci++)
            {
                int n = carried[ci];
                var full = colors[ci];
                var empty = new Color(full.r, full.g, full.b, 0.18f);
                for (int k = 0; k < _slots.GetLength(1); k++) _slots[ci, k].color = k < n ? full : empty;
            }
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

        /// <summary>
        /// One buff icon: <paramref name="left"/> seconds left (0 hides it; for Relay, 1 while it has hits), whether
        /// it's part of a combo, and Relay's hits left as dots.
        /// </summary>
        public void SetBuff(BuffIcon kind, float left, bool combo, int pips = 0)
        {
            int i = (int)kind;
            // A refresh (more time than last frame) sets the full length the fill drains from.
            if (left > _buffLeft[i] + 0.05f || _buffFull[i] <= 0f) _buffFull[i] = Mathf.Max(left, 0.01f);
            _buffLeft[i] = left;
            _buffGold[i].enabled = combo;
            _buffFill[i].fillAmount = kind == BuffIcon.Relay ? 1f : Mathf.Clamp01(left / _buffFull[i]);
            for (int k = 0; k < RelayPips; k++) _relayPips[i, k].gameObject.SetActive(k < pips);
        }

        /// <summary>The active buff icons, left to right above the bars.</summary>
        private void LayOutBuffs()
        {
            int shown = 0;
            for (int i = 0; i < BuffIconCount; i++)
            {
                bool on = _buffLeft[i] > 0f;
                _buffIcons[i].gameObject.SetActive(on);
                if (!on) continue;
                _buffIcons[i].anchoredPosition = new Vector2(24 + shown * (IconSize + 16f), 162);
                shown++;
            }
        }

        private static PayloadKind IconPayload(BuffIcon kind)
        {
            switch (kind)
            {
                case BuffIcon.Might: return PayloadKind.Might;
                case BuffIcon.Haste: return PayloadKind.Haste;
                case BuffIcon.Frenzy: return PayloadKind.Frenzy;
                case BuffIcon.Ward: return PayloadKind.Ward;
                case BuffIcon.Focus: return PayloadKind.Focus;
                default: return PayloadKind.Relay;
            }
        }

        /// <summary>A shape per buff, so they differ by more than colour.</summary>
        private static Sprite IconShape(BuffIcon kind)
        {
            const int s = 32;
            const float h = s * 0.5f;
            switch (kind)
            {
                // Might: an upward triangle.
                case BuffIcon.Might: return Triangle(s, s, false);
                // Haste: a diamond.
                case BuffIcon.Haste: return Shape(s, s, (x, y) => Mathf.Abs(x - h) + Mathf.Abs(y - h) <= h - 1f);
                // Frenzy: a hexagon.
                case BuffIcon.Frenzy: return Shape(s, s, (x, y) =>
                {
                    float dx = Mathf.Abs(x - h), dy = Mathf.Abs(y - h);
                    return dy <= h * 0.866f - 1f && dx * 0.866f + dy * 0.5f <= h * 0.866f - 1f;
                });
                // Ward: a shield (square top, pointed bottom).
                case BuffIcon.Ward: return Shape(s, s, (x, y) => y >= h ? Mathf.Abs(x - h) <= h - 2f : Mathf.Abs(x - h) <= (y / h) * (h - 2f));
                // Focus: a ring with a dot (a target).
                case BuffIcon.Focus: return Shape(s, s, (x, y) =>
                {
                    float d = Mathf.Sqrt((x - h) * (x - h) + (y - h) * (y - h));
                    return d <= 4.5f || (d >= h - 6f && d <= h - 1f);
                });
                // Relay: a circle.
                default: return Circle();
            }
        }

        /// <summary>The gun panel (Esc menu): a title line (gun, payload, ammo) and a detail block (mods).</summary>
        public void SetGun(PayloadKind payload, string title, string info)
        {
            _payloadStripe.color = PayloadColor(payload);
            _payloadName.text = title;
            _payloadInfo.text = info;
        }

        /// <summary>
        /// The chain you hold: <paramref name="links"/> links (0 hides it), coloured by the packed payloads. A new link
        /// pops in; the whole chain blinks in its last 1.5 s.
        /// </summary>
        public void SetChain(int links, uint payloads, float timeLeft)
        {
            float now = Time.unscaledTime;
            links = Mathf.Clamp(links, 0, _links.Length);
            if (links > _shownLinks) _linkAddedAt = now;
            _shownLinks = links;
            _chainRoot.gameObject.SetActive(links > 0);
            if (links == 0) return;
            float alpha = timeLeft < 1.5f ? 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(now * 9f)) : 1f;
            float pop = 1f + 0.5f * Mathf.Clamp01(1f - (now - _linkAddedAt) / 0.25f);
            for (int i = 0; i < _links.Length; i++)
            {
                bool on = i < links;
                _links[i].gameObject.SetActive(on);
                _linkShadows[i].gameObject.SetActive(on);
                if (!on) continue;
                var c = PayloadColor(Core.Combat.ChainTracker.PayloadAt(payloads, i));
                _links[i].color = new Color(c.r, c.g, c.b, alpha);
                _linkShadows[i].color = new Color(0f, 0f, 0f, 0.5f * alpha);
                _links[i].rectTransform.localScale = Vector3.one * (i == links - 1 ? pop : 1f);
            }
        }

        /// <summary>What E would do here (open a box, take an item); empty for nothing.</summary>
        public void SetPrompt(string prompt) => _prompt.text = prompt ?? "";

        public void Apply()
        {
            _chat.text = Chat.ToString();
            LayOutBuffs();
            // The gun, totals and team only show beside the Esc menu (while the mouse is free).
            bool menu = Cursor.lockState != CursorLockMode.Locked;
            _gunBox.SetActive(menu);
            _statsBox.SetActive(menu);
            _team.text = Team.ToString();
            _teamBox.SetActive(menu && Team.Length > 0);
            // The chain hangs where the join code and team show in the menu.
            if (menu) _chainRoot.gameObject.SetActive(false);
            Fit(_teamBox, _team);
            // Under the host's join code when there is one, otherwise in the corner.
            var teamRect = (RectTransform)_teamBox.transform;
            teamRect.anchoredPosition = new Vector2(-20, PlaySession.JoinCode != null ? -130 : -20);
            _stats.text = Stats.ToString();
        }

        private const float EscColumn = 380f;
        private const float LinkWidth = 54f;
        private const float LinkHeight = 86f;
        /// <summary>Links overlap: each starts this far below the last.</summary>
        private const float LinkStep = 62f;
        private const float SlotSize = 11f;
        private const float SlotRing = 12.5f;
        private const float ClusterStep = 44f;
        private const float ClockFirstCluster = 96f;
        private const float ClockWidth = ClockFirstCluster + ClusterStep * (Core.Expedition.EssenceColors.Count - 1) + 34f;
        private static readonly Color XpColor = new Color(0.75f, 0.55f, 1f, 0.85f);

        /// <summary>One bulb of sand, a triangle that fills from the bottom (the top bulb points down to the neck).</summary>
        private static Image SandBulb(Transform parent, Vector2 pos, bool down, Color color)
        {
            var image = SessionUi.Box(parent, down ? "Sand top" : "Sand bottom", new Vector2(0f, 1f), pos, new Vector2(40, 32), color);
            image.sprite = Triangle(40, 32, down);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Vertical;
            image.fillOrigin = (int)Image.OriginVertical.Bottom;
            return image;
        }

        private static Sprite Triangle(int w, int h, bool down) =>
            Shape(w, h, (x, y) => Mathf.Abs(x - w * 0.5f) <= (down ? y / h : 1f - y / h) * w * 0.5f - 1f);

        /// <summary>Two triangles meeting at a neck: the glass behind the sand.</summary>
        private static Sprite Hourglass(int w, int h) =>
            Shape(w, h, (x, y) => Mathf.Abs(x - w * 0.5f) <= Mathf.Max(2.5f, Mathf.Abs(y - h * 0.5f) / (h * 0.5f) * w * 0.5f));

        /// <summary>An oval ring: a chain link seen face-on, or (drawn narrow and thick) seen side-on.</summary>
        private static Sprite Ring(int w, int h, float thickness)
        {
            float rx = w * 0.5f, ry = h * 0.5f;
            return Shape(w, h, (x, y) =>
            {
                float dx = (x - rx) / rx, dy = (y - ry) / ry;
                float outer = dx * dx + dy * dy;
                float ix = (x - rx) / (rx - thickness), iy = (y - ry) / (ry - thickness);
                return outer <= 1f && ix * ix + iy * iy >= 1f;
            });
        }

        private static Sprite Circle() => Shape(32, 32, (x, y) => (x - 16f) * (x - 16f) + (y - 16f) * (y - 16f) <= 15f * 15f);

        /// <summary>A white sprite whose pixel centres inside the shape are opaque.</summary>
        private static Sprite Shape(int w, int h, System.Func<float, float, bool> inside)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)(inside(x + 0.5f, y + 0.5f) ? 255 : 0));
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
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
                case PayloadKind.Frenzy: return new Color(1f, 0.35f, 0.6f);
                case PayloadKind.Ward: return new Color(0.82f, 0.9f, 1f);
                case PayloadKind.Focus: return new Color(1f, 0.9f, 0.3f);
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
                case PayloadKind.Frenzy: return "Allies: +30% fire rate and capsule regrowth     Enemies: jam (attack less)";
                case PayloadKind.Ward: return "Allies: 20-point shield     Enemies: brittle (armor weakens)";
                case PayloadKind.Focus: return "Allies: bigger weak points for them     Enemies: mark (weak points grow for all)";
                case PayloadKind.None: return "No payload: damages enemies, heals allies a tiny bit. Find a payload mod to add one.";
                default: return "Allies: +damage     Enemies: expose";
            }
        }
    }
}
