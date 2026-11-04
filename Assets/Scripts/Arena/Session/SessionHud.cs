using FriendlyPersonShooter.Core.Progression;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The in-game session overlay: the host's join code (top right), Copy code and Leave game buttons while the
    /// cursor is free (Esc), and "can't reach the host" handling for joining players.
    /// </summary>
    public sealed class SessionHud : MonoBehaviour
    {
        /// <summary>How long a joining player waits for the host before giving up (seconds).</summary>
        public const float ConnectTimeout = 20f;

        private Text _code, _center;
        private GameObject _pause, _help, _skills;
        private bool _showSkills;
        private Text _skillsToggle, _points;
        private readonly Button[] _skillButtons = new Button[SkillTree.Count];
        private readonly Text[] _skillLabels = new Text[SkillTree.Count];
        private int _shownRanks = -1, _shownPoints = -1;
        private float _started;
        private bool _connected;
        private EntityQuery _id, _connection;
        private World _queryWorld;

        private void Start()
        {
            _started = Time.unscaledTime;
            var canvas = SessionUi.Canvas(transform, "Session", 50).transform;
            SessionUi.EventSystem(transform);

            var tr = new Vector2(1f, 1f);
            bool hasCode = PlaySession.JoinCode != null;
            if (hasCode)
            {
                GUIUtility.systemCopyBuffer = PlaySession.JoinCode;
                var box = SessionUi.Box(canvas, "Code", tr, new Vector2(-20, -20), new Vector2(380, 92), SessionUi.Panel).transform;
                _code = SessionUi.Label(box, "", 36, new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(360, 48), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                SessionUi.Label(box, "Join code (copied). Esc for options", 18, new Vector2(0.5f, 0f), new Vector2(0, 8), new Vector2(360, 26), TextAnchor.MiddleCenter, SessionUi.Muted);
                _code.text = PlaySession.JoinCode;
            }

            _center = SessionUi.Label(canvas, "", 34, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1200, 60), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _center.gameObject.AddComponent<Shadow>();

            var pause = SessionUi.Box(canvas, "Options", new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1100, 640), SessionUi.Panel);
            _pause = pause.gameObject;
            var p = pause.transform;
            var top = new Vector2(0.5f, 1f);
            SessionUi.Label(p, "The game keeps running while this is open. Click outside this box to play.", 22, top, new Vector2(0, -24), new Vector2(1040, 34), TextAnchor.MiddleCenter, SessionUi.Muted);

            var middle = new Vector2(0.5f, 0.5f);
            _help = SessionUi.Rect(p, "Help", middle, Vector2.zero, new Vector2(1100, 640)).gameObject;
            var help = new Vector2(0f, 1f);
            var controls = SessionUi.Label(_help.transform, Controls, 21, help, new Vector2(40, -78), new Vector2(480, 400), TextAnchor.UpperLeft);
            controls.supportRichText = true;
            var payloads = SessionUi.Label(_help.transform, PayloadGuide, 21, help, new Vector2(560, -78), new Vector2(510, 400), TextAnchor.UpperLeft);
            payloads.supportRichText = true;
            _skills = SessionUi.Rect(p, "Skills", middle, Vector2.zero, new Vector2(1100, 640)).gameObject;
            BuildSkillTree(_skills.transform);
            _skills.SetActive(false);

            // Bottom row: Skills, Reset position, (Copy join code), End/Leave game.
            var bottom = new Vector2(0.5f, 0f);
            int count = hasCode ? 4 : 3;
            float X(int i) => (i - (count - 1) * 0.5f) * 262f;
            var size = new Vector2(244, 74);
            int slot = 0;
            var toggle = SessionUi.Button(p, "Skills", bottom, new Vector2(X(slot++), 34), size, new Color(0.55f, 0.35f, 0.85f), ToggleSkills, 26);
            _skillsToggle = toggle.GetComponentInChildren<Text>();
            SessionUi.Button(p, "Reset position", bottom, new Vector2(X(slot++), 34), size, SessionUi.Quiet, ResetPosition, 26);
            if (hasCode)
            {
                SessionUi.Button(p, "Copy join code", bottom, new Vector2(X(slot++), 34), size, SessionUi.Secondary,
                    () => GUIUtility.systemCopyBuffer = PlaySession.JoinCode, 26);
            }
            SessionUi.Button(p, hasCode ? "End game" : "Leave game", bottom, new Vector2(X(slot), 34), size, SessionUi.Primary,
                () => PlaySession.Leave(), 26);
            _pause.SetActive(false);
            // Straight into play: the mouse is captured from the start (Esc frees it and shows the options).
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private const string Controls =
            "<b><size=26>Controls</size></b>\n" +
            "WASD  move          Space  jump\n" +
            "Left mouse  fire    Q  switch payload\n" +
            "Esc  free the mouse (this menu)\n" +
            "Click the game  capture the mouse\n\n" +
            "<b>Quick-chat</b>\n" +
            "1 Thanks      2 Need a res\n" +
            "3 Seen any Yellow?      4 Yellow here\n" +
            "5 Follow me      6 Group up?\n" +
            "7 Gate at...      K give kudos\n\n" +
            "<color=#B8B0D8>Stuck? Reset position takes you to the nearest shrine. You keep your essence, but you can't bank it for a minute.</color>";

        private const string PayloadGuide =
            "<b><size=26>Payloads</size></b>   shoot allies to buff them, enemies to weaken them\n\n" +
            "<color=#66FF8C><b>VIGOR</b></color>  allies: heal + shield.  enemies: wither\n" +
            "<color=#FF9A4D><b>MIGHT</b></color>  allies: +damage.  enemies: expose\n" +
            "<color=#66D9FF><b>HASTE</b></color>  allies: +30% speed, +20% jump.  enemies: slow\n" +
            "<color=#D98CFF><b>RELAY</b></color>  allies: their next 5 hits also carry Might and add a chain link.  enemies: tag (hits splash)\n\n" +
            "<b>Chains</b>  buffing different allies in a row builds a chain; an enemy hit cashes it for bonus damage.\n" +
            "<b>Down?</b>  allies shoot your soul fragments to revive you.\n\n" +
            "<b>Expedition</b>  gather Yellow, bank it at the raid door or the gate, pay the gate with banked Yellow and walk through (aim for under 20:00). Follow the light beam. Banked essence is shared by the party.\n" +
            "<b>Glimmers</b>  small yellow diamonds: one shot pops them for a little Yellow.\n" +
            "<b>Skills</b>  XP levels you up; spend points with the Skills button below.";

        private static readonly string[] BranchNames = { "<color=#FF9A4D>OFFENSE</color>", "<color=#66FF8C>SUPPORT</color>", "<color=#66D9FF>SURVIVAL</color>" };

        /// <summary>Three columns of three skills; each needs a rank in the one above it.</summary>
        private void BuildSkillTree(Transform parent)
        {
            var top = new Vector2(0.5f, 1f);
            _points = SessionUi.Label(parent, "", 26, top, new Vector2(0, -62), new Vector2(1000, 36), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _points.supportRichText = true;
            for (int b = 0; b < 3; b++)
            {
                float x = (b - 1) * 350f;
                var header = SessionUi.Label(parent, BranchNames[b], 24, top, new Vector2(x, -104), new Vector2(320, 34), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                header.supportRichText = true;
                for (int k = 0; k < SkillTree.PerBranch; k++)
                {
                    int index = b * SkillTree.PerBranch + k;
                    float y = -146f - k * 104f;
                    if (k > 0) SessionUi.Box(parent, "Link", top, new Vector2(x, y + 12f), new Vector2(6, 12), new Color(1f, 1f, 1f, 0.25f));
                    var id = (SkillId)index;
                    var button = SessionUi.Button(parent, "", top, new Vector2(x, y), new Vector2(320, 92), SessionUi.Quiet, () => BuySkill(id), 20);
                    _skillButtons[index] = button;
                    _skillLabels[index] = button.GetComponentInChildren<Text>();
                    _skillLabels[index].supportRichText = true;
                    _skillLabels[index].fontStyle = FontStyle.Normal;
                }
            }
            SessionUi.Label(parent, "Each skill needs a rank in the one above it. Max 3 ranks. Your level and skills last for this game.", 18,
                new Vector2(0.5f, 0f), new Vector2(0, 124), new Vector2(1000, 28), TextAnchor.MiddleCenter, SessionUi.Muted);
        }

        private void ToggleSkills()
        {
            _showSkills = !_showSkills;
            _shownRanks = -1;
        }

        private static void BuySkill(SkillId id) => ArenaFx.SkillBuys.Add((byte)id);

        private void RefreshSkills()
        {
            _help.SetActive(!_showSkills);
            _skills.SetActive(_showSkills);
            var progress = ArenaFx.LocalProgress;
            int points = ArenaFx.HasProgress ? progress.Points : 0;
            _skillsToggle.text = _showSkills ? "Help" : points > 0 ? $"Skills ({points})" : "Skills";
            if (!_showSkills || (progress.Ranks == _shownRanks && points == _shownPoints)) return;
            _shownRanks = progress.Ranks;
            _shownPoints = points;
            _points.text = ArenaFx.HasProgress
                ? $"Level {progress.Level}    {progress.Xp} / {progress.XpNext} XP    " +
                  (points > 0 ? $"<color=#FFD84D>{points} skill point{(points > 1 ? "s" : "")} to spend</color>" : "<color=#B8B0D8>no points: earn XP by killing, buffing allies, banking and reviving</color>")
                : "Not in the game yet";
            for (int i = 0; i < SkillTree.Count; i++)
            {
                var info = SkillTree.All[i];
                int rank = PlayerProgress.RankIn(progress.Ranks, info.Id);
                bool can = ArenaFx.HasProgress && PlayerProgress.CanBuy(progress.Ranks, points, info.Id);
                bool locked = info.TryGetRequirement(out var req) && PlayerProgress.RankIn(progress.Ranks, req) == 0;
                string pips = new string('#', rank) + new string('-', SkillTree.MaxRank - rank);
                _skillLabels[i].text = $"<b><size=23>{info.Name}</size></b>   [{pips}]\n{info.PerRank}" +
                                       (locked ? $"\n<color=#B8B0D8>needs {SkillTree.All[(int)req].Name}</color>" : "");
                _skillButtons[i].interactable = can;
                _skillButtons[i].image.color = rank >= SkillTree.MaxRank ? new Color(0.75f, 0.6f, 0.2f) : can ? new Color(0.1f, 0.52f, 0.62f) : SessionUi.Quiet;
            }
        }

        /// <summary>Asks the server to move this player to the nearest shrine, then goes back to playing.</summary>
        private static void ResetPosition()
        {
            ArenaLocalInputSystem.RequestUnstuck = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            _pause.SetActive(Cursor.lockState != CursorLockMode.Locked);
            if (_pause.activeSelf) RefreshSkills();
            if (PlaySession.IsHost || !PlaySession.InGame) return;

            var world = ClientServerBootstrap.ClientWorld;
            if (world == null) return;
            if (_queryWorld != world)
            {
                _queryWorld = world;
                _id = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                _connection = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
            }

            bool connected = !_id.IsEmpty;
            if (connected) _connected = true;
            if (!_connected)
            {
                float waited = Time.unscaledTime - _started;
                _center.text = "Connecting to your friend's game...";
                if (waited > ConnectTimeout || (waited > 3f && _connection.IsEmpty))
                {
                    PlaySession.Leave("Couldn't connect to the host. Check that they're still in the game, then try again.");
                }
                return;
            }
            _center.text = "";
            if (_connection.IsEmpty) PlaySession.Leave("Lost the connection to the host.");
        }
    }
}
