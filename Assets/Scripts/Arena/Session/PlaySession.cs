using System;
using System.Threading.Tasks;
using FriendlyPersonShooter.Net;
using FriendlyPersonShooter.ZoneArt;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Hosting and joining a game from the main menu, through Unity Relay. The host gets a short join code to
    /// share; friends type it in. Nobody needs to know an IP address or open a port.
    /// </summary>
    public static class PlaySession
    {
        public const string MenuScene = "Menu";
        public const string GameScene = "Expedition";

        /// <summary>Friends who can join one host (the host's own bots don't count: they connect in-process).</summary>
        public const int MaxFriends = 7;

        /// <summary>The code friends type to join this host, or null when not hosting (or playing solo).</summary>
        public static string JoinCode { get; private set; }

        /// <summary>True when this computer runs the game's server (hosting or solo); false when joined to a friend.</summary>
        public static bool IsHost { get; private set; }

        /// <summary>True from hosting or joining until leaving the game.</summary>
        public static bool InGame { get; private set; }

        /// <summary>Why the last game ended, shown on the menu (null after a normal leave).</summary>
        public static string LastMessage;

        /// <summary>Signs in, reserves a relay server, starts the server, own client and bots, and loads the game.</summary>
        public static async Task HostAsync(int bots)
        {
            await SignInAsync();
            Allocation allocation;
            try
            {
                allocation = await RelayService.Instance.CreateAllocationAsync(MaxFriends);
                JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            }
            catch (Exception e)
            {
                throw new SessionException("Couldn't reserve a relay server: " + Short(e), e);
            }

            NetworkStreamReceiveSystem.DriverConstructor = new RelayDriverConstructor(allocation.ToRelayServerData("dtls"), default);
            StartLocalGame(bots);
            IsHost = true;
            Enter();
            Debug.Log($"[Session] Hosting with join code {JoinCode} ({NetTestSettings.ArenaBots} bots).");
        }

        /// <summary>A game on this computer only (you and your bots): no sign-in, no relay, no internet needed.</summary>
        public static void PlaySolo(int bots)
        {
            // In-process only: no relay, and the client and bots use IPC whatever the Editor's play mode type.
            NetworkStreamReceiveSystem.DriverConstructor = new RelayDriverConstructor(default, default);
            StartLocalGame(bots);
            JoinCode = null;
            IsHost = true;
            Enter();
            Debug.Log($"[Session] Playing solo ({NetTestSettings.ArenaBots} bots).");
        }

        /// <summary>The server, this player's client and the bots, in this process.</summary>
        private static void StartLocalGame(int bots)
        {
            NetTestSettings.Game = GameMode.Expedition;
            NetTestSettings.ArenaBots = Mathf.Clamp(bots, 0, 3);
            // No auto listen/connect: the relay driver must bind to port 0, so the server listens on Any:0 and the
            // in-process client and bots connect to whatever port its IPC driver got.
            ClientServerBootstrap.AutoConnectPort = 0;
            NetTestBootstrap.CreateArenaWorlds(NetMode.All);

            var server = ClientServerBootstrap.ServerWorld;
            NetworkEndpoint local = default;
            using (var query = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                ref var driver = ref query.GetSingletonRW<NetworkStreamDriver>().ValueRW;
                if (!driver.Listen(NetworkEndpoint.AnyIpv4)) throw new SessionException("Couldn't start the game's server.");
                ref var store = ref driver.DriverStore;
                for (int i = store.FirstDriver; i < store.LastDriver; i++)
                {
                    if (store.GetDriverType(i) == TransportType.IPC) local = NetworkEndpoint.LoopbackIpv4.WithPort(driver.GetLocalEndPoint(i).Port);
                }
            }
            if (!local.IsValid) throw new SessionException("Couldn't start the game's server (no in-process driver).");

            var clients = ClientServerBootstrap.ClientWorlds;
            for (int i = 0; i < clients.Count; i++)
            {
                var em = clients[i].EntityManager;
                using var query = em.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
                query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(em, local);
            }
        }

        /// <summary>Signs in, finds the host's relay server by join code, starts a client and loads the game.</summary>
        public static async Task JoinAsync(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) throw new SessionException("Type the host's join code first.");
            await SignInAsync();
            JoinAllocation allocation;
            try
            {
                allocation = await RelayService.Instance.JoinAllocationAsync(code);
            }
            catch (Exception e)
            {
                throw new SessionException($"Couldn't find a game with code {code}. Check the code, and that the host is still in the game.", e);
            }

            var relay = allocation.ToRelayServerData("dtls");
            NetworkStreamReceiveSystem.DriverConstructor = new RelayDriverConstructor(default, relay);
            NetTestSettings.Game = GameMode.Expedition;
            // Auto-connect would point a client in a normal build at localhost: connect to the relay explicitly.
            ClientServerBootstrap.AutoConnectPort = 0;
            NetTestBootstrap.CreateArenaWorlds(NetMode.Client);
            var client = ClientServerBootstrap.ClientWorld;
            using (var query = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, relay.Endpoint);
            }
            JoinCode = null;
            IsHost = false;
            Enter();
            Debug.Log($"[Session] Joining game {code}.");
        }

        private static void Enter()
        {
            InGame = true;
            LastMessage = null;
            var hud = new GameObject("Session HUD", typeof(SessionHud));
            UnityEngine.Object.DontDestroyOnLoad(hud);
            SceneManager.LoadScene(GameScene);
        }

        /// <summary>Back to the main menu (with <paramref name="message"/> shown there, if any). The worlds close once the menu is up.</summary>
        public static void Leave(string message = null)
        {
            LastMessage = message;
            InGame = false;
            SceneManager.LoadScene(MenuScene);
        }

        /// <summary>
        /// Closes the game's netcode worlds and zones. The menu calls this when it opens, after the game's views are
        /// gone, so nothing draws from a closed world.
        /// </summary>
        public static void Shutdown()
        {
            InGame = false;
            JoinCode = null;
            IsHost = false;
            ZoneWorld.Close();
            var closing = new System.Collections.Generic.List<World>();
            for (int i = 0; i < World.All.Count; i++)
            {
                var world = World.All[i];
                if (world.IsCreated && (world.IsServer() || world.IsClient() || world.IsThinClient())) closing.Add(world);
            }
            foreach (var world in closing) world.Dispose();
            NetworkStreamReceiveSystem.DriverConstructor = null;
            ClientServerBootstrap.AutoConnectPort = 0;
        }

        private static async Task SignInAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
                    if (!string.IsNullOrEmpty(NetTestSettings.Profile)) options.SetProfile(NetTestSettings.Profile);
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch (Exception e)
            {
                throw new SessionException("Couldn't sign in to Unity's online services: " + Short(e) +
                                           (Application.cloudProjectId == "" ? " (this build isn't linked to a Unity Cloud project)" : ""), e);
            }
        }

        private static string Short(Exception e)
        {
            string m = e.Message;
            int nl = m.IndexOf('\n');
            return nl > 0 ? m.Substring(0, nl) : m;
        }
    }

    /// <summary>A hosting or joining failure, with a message meant for the player.</summary>
    public sealed class SessionException : Exception
    {
        public SessionException(string message, Exception inner = null) : base(message, inner) { }
    }
}
