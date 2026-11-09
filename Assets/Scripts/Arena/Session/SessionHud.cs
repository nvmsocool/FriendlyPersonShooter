using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Progression;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The in-game session overlay: the starting gun and loot box picks, the host's join code (top right), Copy code and Leave
    /// game buttons while the cursor is free (Esc), and "can't reach the host" handling for joining players.
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
        private EntityQuery _id, _connection, _loadout;
        private World _queryWorld, _loadoutWorld;
        private GameObject _pick;
        private readonly Text[] _pickLabels = new Text[LootRolls.StartingChoiceCount];
        private readonly Image[] _pickStripes = new Image[LootRolls.StartingChoiceCount];
        private readonly ulong[] _pickShown = new ulong[LootRolls.StartingChoiceCount];
        private Text _pickTitle, _pickSub;
        private GameObject _pickLeave;
        private ulong _pickKit;
        private byte _pickSerial;
        /// <summary>The pick this player last answered (it stays hidden until the server opens a new one).</summary>
        private int _answered = -1;
        /// <summary>The last frame a pick was up (Esc then closes it rather than opening the options).</summary>
        public static int PickFrame = -10;

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
            BuildPick(canvas);
            // Straight into play: the mouse is captured from the start (Esc frees it and shows the options).
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private const string Controls =
            "<b><size=26>Controls</size></b>\n" +
            "WASD  move          Space  jump\n" +
            "Shift  sprint (forward)\n" +
            "Left mouse  fire    Right mouse  zoom\n" +
            "Middle mouse  ping a spot or an enemy\n" +
            "Q  switch gun       R  reload\n" +
            "E  open a loot box / take an item\n" +
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

        /// <summary>
        /// The pick screen, for the starting gun and for loot boxes: a card per choice over a dimmed screen (which also
        /// keeps clicks off the game, so the mouse stays free until a card is picked).
        /// </summary>
        private void BuildPick(Transform canvas)
        {
            var dim = SessionUi.Box(canvas, "Pick", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.02f, 0.01f, 0.06f, 0.7f));
            dim.rectTransform.anchorMin = Vector2.zero;
            dim.rectTransform.anchorMax = Vector2.one;
            _pick = dim.gameObject;
            var c = new Vector2(0.5f, 0.5f);
            _pickTitle = SessionUi.Label(_pick.transform, "", 52, c, new Vector2(0, 250), new Vector2(1400, 70), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _pickTitle.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);
            _pickSub = SessionUi.Label(_pick.transform, "", 22, c, new Vector2(0, 196), new Vector2(1400, 34), TextAnchor.MiddleCenter, SessionUi.Muted);
            for (int i = 0; i < _pickLabels.Length; i++)
            {
                int index = i;
                float x = (i - (_pickLabels.Length - 1) * 0.5f) * 420f;
                var card = SessionUi.Button(_pick.transform, "", c, new Vector2(x, -30), new Vector2(390, 360), new Color(0.12f, 0.1f, 0.26f, 0.95f), () => Pick(index), 24);
                _pickStripes[i] = SessionUi.Box(card.transform, "Payload", new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(374, 10), Color.white);
                _pickLabels[i] = card.GetComponentInChildren<Text>();
                _pickLabels[i].supportRichText = true;
                _pickLabels[i].fontStyle = FontStyle.Normal;
                _pickLabels[i].alignment = TextAnchor.MiddleCenter;
                _pickLabels[i].rectTransform.sizeDelta = new Vector2(360, 320);
                _pickLabels[i].rectTransform.anchoredPosition = new Vector2(0, -6);
            }
            _pickLeave = SessionUi.Button(_pick.transform, "Leave it for now  (E / Esc)", c, new Vector2(0, -260), new Vector2(420, 56), SessionUi.Quiet,
                () => Pick(PickRequest.Leave), 24).gameObject;
            _pick.SetActive(false);
        }

        private static string FrameBlurb(FrameKind frame)
        {
            switch (frame)
            {
                case FrameKind.Pistol: return "Accurate single shots";
                case FrameKind.Rifle: return "Three-round bursts";
                case FrameKind.Smg: return "Fast fire, short range";
                case FrameKind.Marksman: return "Slow, hard-hitting, long range";
                case FrameKind.Beam: return "A steady beam that ramps up";
                case FrameKind.Launcher: return "Shells that burst where they land";
                default: return "";
            }
        }

        private static string PayloadBlurb(PayloadKind payload)
        {
            switch (payload)
            {
                case PayloadKind.Vigor: return "allies: heal + shield\nenemies: wither";
                case PayloadKind.Might: return "allies: +damage\nenemies: expose";
                case PayloadKind.Haste: return "allies: +speed, +jump\nenemies: slow";
                case PayloadKind.Relay: return "allies: their hits carry Might\nenemies: hits splash";
                case PayloadKind.Frenzy: return "allies: fire + reload faster\nenemies: attack less";
                case PayloadKind.Ward: return "allies: damage shield\nenemies: armor weakens";
                case PayloadKind.Focus: return "allies: bigger weak points\nenemies: weak points grow";
                default: return "";
            }
        }

        /// <summary>Answers the open pick (a card, or PickRequest.Leave for a box) and goes back to playing.</summary>
        private void Pick(int index)
        {
            ArenaFx.PickChoice = index;
            ArenaFx.PickSerial = _pickSerial;
            _answered = _pickSerial;
            _pick.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Shows the open pick while the server has one this player hasn't answered. True while it's up.</summary>
        private bool RefreshPick()
        {
            var world = ClientServerBootstrap.ClientWorld;
            bool show = false;
            ArenaLoadout loadout = default;
            if (world != null && world.IsCreated)
            {
                if (_loadoutWorld != world)
                {
                    _loadoutWorld = world;
                    _loadout = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ArenaLoadout>(), ComponentType.ReadOnly<ArenaPlayer>(),
                        ComponentType.ReadOnly<GhostOwnerIsLocal>());
                }
                if (_loadout.CalculateEntityCount() == 1)
                {
                    loadout = _loadout.GetSingleton<ArenaLoadout>();
                    show = loadout.Pick != PickKind.None && loadout.Choice0 != 0 && loadout.PickSerial != _answered;
                }
            }
            bool wasShown = _pick.activeSelf;
            if (show && !wasShown)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (!show && wasShown)
            {
                // The server closed it (walked away, went down, or E): back to playing.
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            _pick.SetActive(show);
            if (!show) return false;

            PickFrame = Time.frameCount;
            bool box = loadout.Pick == PickKind.Box;
            bool reward = loadout.Pick == PickKind.Reward;
            _pickSerial = loadout.PickSerial;
            _pickLeave.SetActive(box);
            var keyboard = Keyboard.current;
            if (box && wasShown && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Pick(PickRequest.Leave);
                return false;
            }
            _pickTitle.text = box ? "LOOT BOX: PICK ONE" : reward ? "BOSS REWARD: PICK ONE" : "PICK YOUR STARTING GUN";
            _pickSub.text = box
                ? "Take one of the three. Walk away or leave it and the box stays shut, with the same three inside."
                : reward
                ? "You earned it: take one of the three, rare or better."
                : "Each comes with a payload: shoot allies to buff them, enemies to weaken them. You'll find more guns and mods in loot boxes.";
            var choices = new[] { loadout.Choice0, loadout.Choice1, loadout.Choice2 };
            // A box card shows what it would swap out, which changes with the loadout.
            bool items = box || reward;
            ulong kit = loadout.GunA ^ (loadout.GunB * 31) ^ loadout.Active ^ (items ? 1UL << 40 : 0);
            for (int i = 0; i < _pickLabels.Length; i++)
            {
                var card = _pickLabels[i].transform.parent.gameObject;
                card.SetActive(choices[i] != 0);
                if (choices[i] == 0 || (choices[i] == _pickShown[i] && kit == _pickKit)) continue;
                _pickShown[i] = choices[i];
                var item = LootItem.Unpack(choices[i]);
                if (items)
                {
                    _pickStripes[i].color = ArenaView.ItemTierColor(item);
                    _pickLabels[i].text = ArenaView.BoxCard(loadout, item);
                    continue;
                }
                var gun = item.Gun;
                var color = ArenaHud.PayloadColor(gun.Payload);
                _pickStripes[i].color = color;
                _pickLabels[i].text = $"<b><size=40>{GunFrames.Name(gun.Frame).ToUpperInvariant()}</size></b>\n" +
                                      $"<color=#B8B0D8>{FrameBlurb(gun.Frame)}</color>\n\n" +
                                      $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b><size=32>{gun.Payload.ToString().ToUpperInvariant()}</size></b></color>\n" +
                                      $"{PayloadBlurb(gun.Payload)}";
            }
            _pickKit = kit;
            return true;
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
            bool picking = RefreshPick();
            if (picking && Cursor.lockState == CursorLockMode.Locked)
            {
                // The pick stays up (and the mouse free) until a card is chosen.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            _pause.SetActive(!picking && Cursor.lockState != CursorLockMode.Locked);
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
