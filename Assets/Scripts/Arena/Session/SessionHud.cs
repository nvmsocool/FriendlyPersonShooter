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
        private GameObject _pause;
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

            var help = new Vector2(0f, 1f);
            var controls = SessionUi.Label(p, Controls, 21, help, new Vector2(40, -78), new Vector2(480, 400), TextAnchor.UpperLeft);
            controls.supportRichText = true;
            var payloads = SessionUi.Label(p, PayloadGuide, 21, help, new Vector2(560, -78), new Vector2(510, 400), TextAnchor.UpperLeft);
            payloads.supportRichText = true;

            var bottom = new Vector2(0.5f, 0f);
            SessionUi.Button(p, "Reset position", bottom, new Vector2(hasCode ? -350 : -175, 34), new Vector2(320, 74), SessionUi.Quiet, ResetPosition, 26);
            if (hasCode)
            {
                SessionUi.Button(p, "Copy join code", bottom, new Vector2(0, 34), new Vector2(320, 74), SessionUi.Secondary,
                    () => GUIUtility.systemCopyBuffer = PlaySession.JoinCode, 26);
            }
            SessionUi.Button(p, hasCode ? "End game" : "Leave game", bottom, new Vector2(hasCode ? 350 : 175, 34), new Vector2(320, 74), SessionUi.Primary,
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
            "<color=#B8B0D8>Stuck? Reset position takes you to the nearest shrine.</color>";

        private const string PayloadGuide =
            "<b><size=26>Payloads</size></b>   shoot allies to buff them, enemies to weaken them\n\n" +
            "<color=#66FF8C><b>VIGOR</b></color>  allies: heal + shield.  enemies: wither\n" +
            "<color=#FF9A4D><b>MIGHT</b></color>  allies: +damage.  enemies: expose\n" +
            "<color=#66D9FF><b>HASTE</b></color>  allies: +30% speed, +20% jump.  enemies: slow\n" +
            "<color=#D98CFF><b>RELAY</b></color>  allies: their next 5 hits also carry Might and add a chain link.  enemies: tag (hits splash)\n\n" +
            "<b>Chains</b>  buffing different allies in a row builds a chain; an enemy hit cashes it for bonus damage.\n" +
            "<b>Down?</b>  allies shoot your soul fragments to revive you.\n\n" +
            "<b>Expedition</b>  gather Yellow, bank it at shrines, pay the gate with banked Yellow and walk through (aim for under 20:00). Follow the light beam.";

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
