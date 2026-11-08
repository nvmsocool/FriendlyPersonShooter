using FriendlyPersonShooter.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>In a build started with <c>-fps-game arena</c> or <c>expedition</c>, switches to that scene, which holds the first-person view.</summary>
    public static class ArenaSceneLoader
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LoadArenaIfNeeded()
        {
            // At the main menu no game runs yet; the menu loads the game's scene when the player hosts or joins.
            if (NetTestBootstrap.StartsAtMenu) return;
            string wanted = NetTestSettings.Game == GameMode.Arena ? "Arena" : NetTestSettings.Game == GameMode.Expedition ? "Expedition" : null;
            if (wanted != null && SceneManager.GetActiveScene().name != wanted) SceneManager.LoadScene(wanted);
        }
    }
}
