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

        private readonly Text _expedition, _chat, _buffs, _team, _stats, _health, _payloadName, _payloadInfo, _chain;
        private readonly GameObject _expeditionBox, _teamBox, _vitalsBox;
        private readonly RectTransform _healthFill, _shieldFill;
        private readonly Image _healthImage, _payloadStripe;

        private const float BarWidth = 380f;

        public ArenaHud(Transform parent)
        {
            var canvas = SessionUi.Canvas(parent, "HUD", 10).transform;
            var tl = new Vector2(0f, 1f);
            var bl = new Vector2(0f, 0f);
            var br = new Vector2(1f, 0f);
            var tr = new Vector2(1f, 1f);
            var c = new Vector2(0.5f, 0.5f);
            var panel = new Color(0.05f, 0.04f, 0.12f, 0.55f);

            // Expedition, top left.
            var exp = SessionUi.Box(canvas, "Expedition", tl, new Vector2(20, -20), new Vector2(620, 190), panel);
            _expeditionBox = exp.gameObject;
            _expedition = Text(exp.transform, 22, tl, new Vector2(16, -12), new Vector2(590, 170), TextAnchor.UpperLeft);
            _chat = Text(canvas, 21, tl, new Vector2(24, -224), new Vector2(700, 260), TextAnchor.UpperLeft);

            // Health and shield, bottom left, with buffs above.
            var vitals = SessionUi.Box(canvas, "Vitals", bl, new Vector2(20, 20), new Vector2(BarWidth + 32, 92), panel);
            _vitalsBox = vitals.gameObject;
            SessionUi.Box(vitals.transform, "Health back", tl, new Vector2(16, -16), new Vector2(BarWidth, 34), new Color(0f, 0f, 0f, 0.5f));
            _healthImage = SessionUi.Box(vitals.transform, "Health", tl, new Vector2(16, -16), new Vector2(BarWidth, 34), new Color(0.35f, 0.9f, 0.45f));
            _healthFill = _healthImage.rectTransform;
            _health = Text(vitals.transform, 22, tl, new Vector2(16, -16), new Vector2(BarWidth, 34), TextAnchor.MiddleCenter, FontStyle.Bold);
            SessionUi.Box(vitals.transform, "Shield back", tl, new Vector2(16, -58), new Vector2(BarWidth, 16), new Color(0f, 0f, 0f, 0.5f));
            _shieldFill = SessionUi.Box(vitals.transform, "Shield", tl, new Vector2(16, -58), new Vector2(0, 16), new Color(0.35f, 0.85f, 1f)).rectTransform;
            _buffs = Text(canvas, 21, bl, new Vector2(24, 122), new Vector2(760, 150), TextAnchor.LowerLeft);

            // Payload, bottom centre, with the chain status above it.
            var pay = SessionUi.Box(canvas, "Payload", new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(560, 92), panel);
            _payloadStripe = SessionUi.Box(pay.transform, "Stripe", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(10, 92), Color.white);
            _payloadName = Text(pay.transform, 32, new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(520, 42), TextAnchor.MiddleCenter, FontStyle.Bold);
            _payloadInfo = Text(pay.transform, 19, new Vector2(0.5f, 0f), new Vector2(0, 8), new Vector2(530, 40), TextAnchor.MiddleCenter);
            _chain = Text(canvas, 24, new Vector2(0.5f, 0f), new Vector2(0, 118), new Vector2(900, 34), TextAnchor.MiddleCenter, FontStyle.Bold);

            // Team, right side (under the join code), and totals, bottom right.
            var team = SessionUi.Box(canvas, "Team", tr, new Vector2(-20, -130), new Vector2(380, 250), panel);
            _teamBox = team.gameObject;
            _team = Text(team.transform, 21, tl, new Vector2(14, -10), new Vector2(356, 236), TextAnchor.UpperLeft);
            _stats = Text(canvas, 19, br, new Vector2(-24, 20), new Vector2(560, 90), TextAnchor.LowerRight);

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

        public void SetPayload(PayloadKind payload, string chain)
        {
            _payloadStripe.color = PayloadColor(payload);
            _payloadName.text = $"<color=#{ColorUtility.ToHtmlStringRGB(PayloadColor(payload))}>{payload.ToString().ToUpperInvariant()}</color>   <size=18>Q to switch</size>";
            _payloadInfo.text = PayloadShort(payload);
            _chain.text = chain ?? "";
        }

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
                case PayloadKind.Relay: return "Allies: next 5 hits carry Might + chain     Enemies: tag (splash)";
                default: return "Allies: +damage     Enemies: expose";
            }
        }
    }
}
