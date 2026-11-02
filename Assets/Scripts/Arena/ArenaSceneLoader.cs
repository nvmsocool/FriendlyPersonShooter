using FriendlyPersonShooter.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>In a build started with <c>-fps-game arena</c>, switches to the Arena scene, which holds the arena view.</summary>
    public static class ArenaSceneLoader
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LoadArenaIfNeeded()
        {
            if (NetTestSettings.Game == GameMode.Arena && SceneManager.GetActiveScene().name != "Arena")
            {
                SceneManager.LoadScene("Arena");
            }
        }
    }
}
