using Unity.NetCode;
using Unity.Scenes;
using Unity.Networking.Transport;
using UnityEngine;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Creates the netcode worlds: one player hosts (server plus their client) and friends join as clients.
    /// Bot allies are full client worlds in the host's process.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class NetTestBootstrap : ClientServerBootstrap
    {
        /// <summary>
        /// True when the game starts at the main menu: no netcode worlds until the player hosts or joins
        /// (see <see cref="CreateArenaWorlds"/>). The playtest build starts there unless given -fps-game;
        /// in the Editor, playing the scene named "Menu" does.
        /// </summary>
        public static bool StartsAtMenu { get; private set; }

        public override bool Initialize(string defaultWorldName)
        {
            NetTestSettings.ParseCommandLine();
#if UNITY_EDITOR
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName == "Arena") NetTestSettings.Game = GameMode.Arena;
            if (sceneName == "Expedition") NetTestSettings.Game = GameMode.Expedition;
            StartsAtMenu = sceneName == "Menu";
#elif FPS_PLAYTEST
            StartsAtMenu = !NetTestSettings.GameGiven;
#endif
            if (StartsAtMenu)
            {
                AutoConnectPort = 0;
                CreateLocalWorld(defaultWorldName);
                Debug.Log("[NetTest] Starting at the main menu.");
                return true;
            }

            AutoConnectPort = NetTestSettings.Port;
            if (NetworkEndpoint.TryParse(NetTestSettings.ConnectAddress, NetTestSettings.Port, out var endpoint))
            {
                DefaultConnectAddress = endpoint;
            }
            else
            {
                Debug.LogError($"[NetTest] Couldn't parse connect address '{NetTestSettings.ConnectAddress}'.");
            }

            CreateArenaWorlds(NetTestSettings.Mode);
            // A plain world for anything that isn't networked.
            CreateLocalWorld(defaultWorldName);
            return true;
        }

        /// <summary>
        /// Creates the game's worlds for <paramref name="mode"/>: the server, the human's client and
        /// <see cref="NetTestSettings.ArenaBots"/> bot allies. Also used by the main menu when hosting or joining.
        /// Bot allies are full clients (not thin clients) because they need to see enemies and players to aim.
        /// </summary>
        public static void CreateArenaWorlds(NetMode mode)
        {
            if (mode == NetMode.All || mode == NetMode.Server) CreateServerWorld("ServerWorld");
            if (mode == NetMode.All || mode == NetMode.Client) CreateClientWorld("ClientWorld");
            if (mode == NetMode.All || mode == NetMode.Bots)
            {
                for (int i = 0; i < NetTestSettings.ArenaBots; i++)
                {
                    var bot = CreateClientWorld($"ArenaBot{i}");
                    bot.GetExistingSystemManaged<SceneSectionStreamingSystem>().ConcurrentSectionStreamCount = 0;
                    bot.EntityManager.CreateSingleton(new ArenaBotSeat { Index = i });
                }
            }
            Debug.Log($"[NetTest] Game={NetTestSettings.Game} Mode={mode} arenaBots={NetTestSettings.ArenaBots} port={AutoConnectPort}");
        }
    }
}
