using Unity.NetCode;
using Unity.Scenes;
using Unity.Networking.Transport;
using UnityEngine;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Creates the netcode worlds for the networking test. Thin client worlds are the bots: real
    /// clients that connect and send input through the network stack, without rendering or ghosts.
    /// The server world is created first so in-process clients use the fast IPC transport.
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
            if (NetTestSettings.IsFirstPerson && !NetTestSettings.PortGiven) NetTestSettings.Port = NetTestSettings.ArenaPort;
            AutoConnectPort = NetTestSettings.Port;

            if (NetworkEndpoint.TryParse(NetTestSettings.ConnectAddress, NetTestSettings.Port, out var endpoint))
            {
                DefaultConnectAddress = endpoint;
            }
            else
            {
                Debug.LogError($"[NetTest] Couldn't parse connect address '{NetTestSettings.ConnectAddress}'.");
            }

            if (NetTestSettings.IsFirstPerson) return InitializeArena(defaultWorldName);

            var mode = NetTestSettings.Mode;
            if (mode == NetMode.All || mode == NetMode.Server)
            {
                CreateServerWorld("ServerWorld");
            }
            if (mode == NetMode.All || mode == NetMode.Bots)
            {
                // Bots connect when their simulated player logs on, not when their world is created.
                // They're created before the human client, or they would copy its connection.
                ushort port = AutoConnectPort;
                AutoConnectPort = 0;
                BotDirector.Create(NetTestSettings.BotCount);
                for (int i = 0; i < NetTestSettings.BotCount; i++)
                {
                    var bot = CreateThinClientWorld();
                    // Bots never load scenes; drop the idle background loading worlds each one gets.
                    bot.GetExistingSystemManaged<SceneSectionStreamingSystem>().ConcurrentSectionStreamCount = 0;
                    bot.EntityManager.CreateSingleton(new BotSeat { Index = i });
                }
                AutoConnectPort = port;
            }
            if (mode == NetMode.All || mode == NetMode.Client)
            {
                CreateClientWorld("ClientWorld");
            }

            // A plain world for anything that isn't networked.
            CreateLocalWorld(defaultWorldName);

            Debug.Log($"[NetTest] Game={NetTestSettings.Game} Mode={mode} bots={NetTestSettings.BotCount} port={NetTestSettings.Port} " +
                      $"grid={NetTestSettings.GridSize}x{NetTestSettings.GridSize} zone={NetTestSettings.ZoneSize}m");
            return true;
        }

        /// <summary>
        /// The arena: a server, the human's client and a few bot allies. Bot allies are full clients
        /// (not thin clients) because they need to see enemies and players to aim.
        /// </summary>
        private bool InitializeArena(string defaultWorldName)
        {
            CreateArenaWorlds(NetTestSettings.Mode);
            CreateLocalWorld(defaultWorldName);
            return true;
        }

        /// <summary>
        /// Creates the first-person game's worlds for <paramref name="mode"/>: the server, the human's client and
        /// <see cref="NetTestSettings.ArenaBots"/> bot allies. Also used by the main menu when hosting or joining.
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