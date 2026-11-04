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

            var pause = SessionUi.Box(canvas, "Options", new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(460, hasCode ? 340 : 250), SessionUi.Panel);
            _pause = pause.gameObject;
            var p = pause.transform;
            SessionUi.Label(p, "The game keeps running while this is open.\nClick outside this box to play.", 22, new Vector2(0.5f, 1f), new Vector2(0, -55), new Vector2(420, 70), TextAnchor.MiddleCenter, SessionUi.Muted);
            if (hasCode)
            {
                SessionUi.Button(p, "Copy join code", new Vector2(0.5f, 0f), new Vector2(0, 124), new Vector2(380, 74), SessionUi.Secondary,
                    () => GUIUtility.systemCopyBuffer = PlaySession.JoinCode, 28);
            }
            SessionUi.Button(p, hasCode ? "End game" : "Leave game", new Vector2(0.5f, 0f), new Vector2(0, 34), new Vector2(380, 74), SessionUi.Primary,
                () => PlaySession.Leave(), 28);
            _pause.SetActive(false);
            // Straight into play: the mouse is captured from the start (Esc frees it and shows the options).
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
