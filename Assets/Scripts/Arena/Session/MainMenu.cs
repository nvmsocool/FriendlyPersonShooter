using System;
using FriendlyPersonShooter.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The main menu: host a game (and get a join code to share) or join a friend's with their code.
    /// Lives in the Menu scene; the netcode worlds only exist once the player hosts or joins.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        private const string BotsPref = "fps.menu.bots";
        private const string CodePref = "fps.menu.lastCode";

        private Button _host, _solo, _join, _quit, _less, _more;
        private InputField _code;
        private Text _bots, _status;
        private int _botCount;
        private bool _busy;

        private void Awake()
        {
            // Started with -fps-game (no menu): the game's worlds already exist, so go straight to its scene.
            if (!NetTestBootstrap.StartsAtMenu)
            {
                enabled = false;
                UnityEngine.SceneManagement.SceneManager.LoadScene(NetTestSettings.Game == GameMode.Arena ? "Arena" : PlaySession.GameScene);
                return;
            }
            // Coming back from a game: its overlay goes, and its worlds close now that its views are gone.
            foreach (var hud in FindObjectsByType<SessionHud>(FindObjectsInactive.Include)) Destroy(hud.gameObject);
            PlaySession.Shutdown();
        }

        private void Start()
        {
            if (!NetTestBootstrap.StartsAtMenu) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _botCount = Mathf.Clamp(PlayerPrefs.GetInt(BotsPref, 2), 0, 3);
            Build();
            _code.text = PlayerPrefs.GetString(CodePref, "");
            if (!string.IsNullOrEmpty(PlaySession.LastMessage)) Show(PlaySession.LastMessage, true);
            if (!string.IsNullOrEmpty(NetTestSettings.JoinCode) && string.IsNullOrEmpty(PlaySession.LastMessage))
            {
                _code.text = NetTestSettings.JoinCode;
                NetTestSettings.JoinCode = null;
                OnJoin();
            }
            if (!string.IsNullOrEmpty(NetTestSettings.Shot)) StartCoroutine(TakeShot(NetTestSettings.Shot));
            var start = NetTestSettings.Start;
            NetTestSettings.Start = null;
            if (start == "host") OnHost();
            else if (start == "solo") OnSolo();
        }

        private void Build()
        {
            var canvas = SessionUi.Canvas(transform, "Menu", 0).transform;
            SessionUi.EventSystem(transform);
            SessionUi.Backdrop(canvas, new Color(0.36f, 0.16f, 0.5f), new Color(0.05f, 0.05f, 0.14f));

            var c = new Vector2(0.5f, 0.5f);
            SessionUi.Label(canvas, "FRIENDLY PERSON SHOOTER", 76, c, new Vector2(0, 380), new Vector2(1600, 110), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold)
                .gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4, -4);
            SessionUi.Label(canvas, $"Playtest build {Application.version}", 26, c, new Vector2(0, 310), new Vector2(800, 40), TextAnchor.MiddleCenter, SessionUi.Muted);

            // Host
            var host = SessionUi.Box(canvas, "Host", c, new Vector2(-330, 30), new Vector2(600, 420), SessionUi.Panel).transform;
            SessionUi.Label(host, "Host a game", 40, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(560, 60), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            SessionUi.Label(host, "Start a game on this computer and share the join code with your friends.", 24, new Vector2(0.5f, 1f), new Vector2(0, -115), new Vector2(520, 70), TextAnchor.MiddleCenter, SessionUi.Muted);
            SessionUi.Label(host, "Bot allies", 28, new Vector2(0.5f, 0.5f), new Vector2(-120, -5), new Vector2(220, 50), TextAnchor.MiddleLeft, Color.white);
            _less = SessionUi.Button(host, "-", new Vector2(0.5f, 0.5f), new Vector2(60, -5), new Vector2(60, 56), SessionUi.Quiet, () => ChangeBots(-1), 36);
            _bots = SessionUi.Label(host, "", 34, new Vector2(0.5f, 0.5f), new Vector2(130, -5), new Vector2(70, 56), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _more = SessionUi.Button(host, "+", new Vector2(0.5f, 0.5f), new Vector2(200, -5), new Vector2(60, 56), SessionUi.Quiet, () => ChangeBots(1), 36);
            _host = SessionUi.Button(host, "Host game", new Vector2(0.5f, 0f), new Vector2(-75, 36), new Vector2(330, 90), SessionUi.Primary, OnHost, 36);
            _solo = SessionUi.Button(host, "Play solo", new Vector2(0.5f, 0f), new Vector2(170, 36), new Vector2(140, 90), SessionUi.Quiet, OnSolo, 24);

            // Join
            var join = SessionUi.Box(canvas, "Join", c, new Vector2(330, 30), new Vector2(600, 420), SessionUi.Panel).transform;
            SessionUi.Label(join, "Join a friend", 40, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(560, 60), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            SessionUi.Label(join, "Type the join code your friend sees in their game.", 24, new Vector2(0.5f, 1f), new Vector2(0, -115), new Vector2(520, 70), TextAnchor.MiddleCenter, SessionUi.Muted);
            _code = SessionUi.Input(join, "Join code", new Vector2(0.5f, 0.5f), new Vector2(0, -5), new Vector2(480, 76));
            _code.onEndEdit.AddListener(_ =>
            {
                var kb = Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) OnJoin();
            });
            _join = SessionUi.Button(join, "Join game", new Vector2(0.5f, 0f), new Vector2(0, 36), new Vector2(480, 90), SessionUi.Secondary, OnJoin, 36);

            _status = SessionUi.Label(canvas, "", 28, c, new Vector2(0, -250), new Vector2(1300, 90), TextAnchor.MiddleCenter, Color.white);
            _quit = SessionUi.Button(canvas, "Quit", c, new Vector2(0, -380), new Vector2(240, 70), SessionUi.Quiet, OnQuit, 30);
            ChangeBots(0);
        }

        private static System.Collections.IEnumerator TakeShot(string path)
        {
            yield return new WaitForSecondsRealtime(3f);
            ScreenCapture.CaptureScreenshot(path);
        }

        private void ChangeBots(int delta)
        {
            _botCount = Mathf.Clamp(_botCount + delta, 0, 3);
            _bots.text = _botCount.ToString();
            PlayerPrefs.SetInt(BotsPref, _botCount);
        }

        private async void OnHost()
        {
            if (_busy) return;
            SetBusy(true, "Starting a game...");
            try
            {
                await PlaySession.HostAsync(_botCount);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void OnSolo()
        {
            if (_busy) return;
            SetBusy(true, "Starting...");
            try
            {
                PlaySession.PlaySolo(_botCount);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private async void OnJoin()
        {
            if (_busy) return;
            SetBusy(true, "Finding your friend's game...");
            try
            {
                PlayerPrefs.SetString(CodePref, _code.text.Trim().ToUpperInvariant());
                await PlaySession.JoinAsync(_code.text);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception e)
        {
            Debug.LogException(e);
            // A half-started game (worlds created, scene not loaded yet) is closed again.
            PlaySession.Shutdown();
            if (this == null) return;
            SetBusy(false, null);
            Show(e is SessionException ? e.Message : "Something went wrong: " + e.Message, true);
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            foreach (var b in new[] { _host, _solo, _join, _quit, _less, _more }) b.interactable = !busy;
            _code.interactable = !busy;
            if (message != null) Show(message, false);
        }

        private void Show(string message, bool error)
        {
            _status.text = message;
            _status.color = error ? SessionUi.Error : Color.white;
        }
    }
}
