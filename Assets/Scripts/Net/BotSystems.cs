using System.Text;
using FriendlyPersonShooter.Core.Bots;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// The shared brain for every bot in this process: one <see cref="BotPopulation"/> decides when
    /// each bot logs on and off, who it parties with and where it walks. Each bot client (thin client
    /// world) plays one seat of it through the real network stack.
    /// </summary>
    public static class BotDirector
    {
        public static BotPopulation Population { get; private set; }

        private static double _start;
        private static int _lastFrame = -1;
        private static double _nextLog;
        private static readonly int[] ActivityCounts = new int[6];

        /// <summary>Real seconds since the bots were created.</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble - _start;

        public static void Create(int seats)
        {
            Population = new BotPopulation(seats, NetTestSettings.CreateGrid(), NetTestSettings.CreateBotSettings(),
                NetTestSettings.BotSeed, NetTestSettings.AccountBase);
            _start = Time.realtimeSinceStartupAsDouble;
            _lastFrame = -1;
            _nextLog = 5;
        }

        /// <summary>Advances the population once per frame, however many bot worlds call it.</summary>
        public static void Tick()
        {
            if (Population == null || Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            double now = Now;
            Population.Tick(now);
            if (now >= _nextLog)
            {
                _nextLog = now + 5;
                Debug.Log(Summary(now));
            }
        }

        /// <summary>One line describing the population, for logs and the lobby map.</summary>
        public static string Summary(double now)
        {
            var pop = Population;
            System.Array.Clear(ActivityCounts, 0, ActivityCounts.Length);
            foreach (var a in pop.Accounts)
            {
                if (a.WantsOnline) ActivityCounts[(int)a.Activity]++;
            }
            int parties = pop.PartyCount(out int inParties);
            double hour = pop.HourAt(now);
            var s = pop.Stats;
            var sb = new StringBuilder();
            sb.Append($"[BotSim] t={now:0} hour={(int)hour:00}:{(int)(hour % 1 * 60):00} ");
            sb.Append($"online={pop.OnlineCount}/{pop.Accounts.Count} target={pop.TargetShare(now) * pop.Accounts.Count:0} ");
            sb.Append($"parties={parties} inParties={inParties} ");
            sb.Append($"logins={s.Logins} logouts={s.Logouts} drops={s.Drops} reconnects={s.Reconnects} ");
            sb.Append($"partyLogins={s.PartyLogins} friendJoins={s.FriendJoins} strangerParties={s.StrangerParties} partyLeaves={s.PartyLeaves} teleports={s.Teleports} ");
            sb.Append($"explore={ActivityCounts[(int)BotActivity.Explore]} hunt={ActivityCounts[(int)BotActivity.Hunt]} ");
            sb.Append($"hotspot={ActivityCounts[(int)BotActivity.Hotspot]} afk={ActivityCounts[(int)BotActivity.Afk]} ");
            sb.Append($"follow={ActivityCounts[(int)BotActivity.Follow]} starting={ActivityCounts[(int)BotActivity.Offline]}");
            return sb.ToString();
        }
    }

    /// <summary>Tags the local entity a bot writes its input into.</summary>
    public struct BotInputTag : IComponentData
    {
    }

    /// <summary>
    /// Connects and disconnects one bot client as its seat logs on and off, sends the login and party
    /// changes, and passes the server's position updates to the bot's brain.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BotSessionSystem : SystemBase
    {
        private EntityQuery _connections;
        private EntityQuery _inputs;
        private int _seat = -1;
        private int _sentGroup;
        private double _nextConnect;

        protected override void OnCreate()
        {
            RequireForUpdate<BotSeat>();
            _connections = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
            _inputs = GetEntityQuery(ComponentType.ReadOnly<BotInputTag>());
        }

        protected override void OnUpdate()
        {
            var pop = BotDirector.Population;
            if (pop == null) return;
            if (_seat < 0) _seat = SystemAPI.GetSingleton<BotSeat>().Index;
            BotDirector.Tick();
            var account = pop.Accounts[_seat];
            double now = BotDirector.Now;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (status, rpc) in SystemAPI.Query<RefRO<BotStatus>>()
                         .WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                pop.ReportPosition(_seat, status.ValueRO.X, status.ValueRO.Z, status.ValueRO.LobbyId);
                ecb.DestroyEntity(rpc);
            }

            if (!account.WantsOnline)
            {
                foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkStreamConnection>>()
                             .WithNone<NetworkStreamRequestDisconnect>().WithEntityAccess())
                {
                    ecb.AddComponent<NetworkStreamRequestDisconnect>(connection);
                }
                ecb.Playback(EntityManager);
                EntityManager.DestroyEntity(_inputs);
                return;
            }

            if (_connections.IsEmpty)
            {
                ecb.Playback(EntityManager);
                if (now < _nextConnect) return;
                _nextConnect = now + 3;
                // A new connection gets a new network id, so the input entity is rebuilt for it.
                EntityManager.DestroyEntity(_inputs);
                var endpoint = ClientServerBootstrap.DefaultConnectAddress.WithPort(NetTestSettings.Port);
                SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(EntityManager, endpoint);
                return;
            }

            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                         .WithNone<NetworkStreamInGame, NetworkStreamRequestDisconnect>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(connection);
                _sentGroup = account.PartyId;
                var request = ecb.CreateEntity();
                ecb.AddComponent(request, new GoInGameRequest
                {
                    AccountId = account.AccountId,
                    GroupId = account.PartyId,
                    IsBot = 1,
                });
                ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
            }

            if (pop.TryTakeTeleport(_seat, out double tx, out double tz))
            {
                foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                             .WithAll<NetworkStreamInGame>().WithNone<NetworkStreamRequestDisconnect>().WithEntityAccess())
                {
                    var request = ecb.CreateEntity();
                    ecb.AddComponent(request, new TeleportRequest { X = (float)tx, Z = (float)tz });
                    ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }

            if (account.PartyId != _sentGroup)
            {
                foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                             .WithAll<NetworkStreamInGame>().WithNone<NetworkStreamRequestDisconnect>().WithEntityAccess())
                {
                    _sentGroup = account.PartyId;
                    var request = ecb.CreateEntity();
                    ecb.AddComponent(request, new SetGroupRequest { GroupId = account.PartyId });
                    ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }
            ecb.Playback(EntityManager);
        }
    }

    /// <summary>
    /// Writes each bot's movement input. Thin clients don't receive ghosts, so the bot owns a local
    /// input entity and points its connection's CommandTarget at it; the input still travels to the
    /// server through the real network stack.
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class BotInputSystem : SystemBase
    {
        private int _seat = -1;

        protected override void OnCreate()
        {
            RequireForUpdate<BotSeat>();
            RequireForUpdate<NetworkId>();
        }

        protected override void OnUpdate()
        {
            var pop = BotDirector.Population;
            if (pop == null) return;
            if (_seat < 0) _seat = SystemAPI.GetSingleton<BotSeat>().Index;

            if (!SystemAPI.HasSingleton<BotInputTag>())
            {
                CreateInputEntity();
                return;
            }

            var (h, v) = pop.Steer(_seat, BotDirector.Now);
            foreach (var input in SystemAPI.Query<RefRW<PlayerInput>>().WithAll<BotInputTag>())
            {
                input.ValueRW = new PlayerInput { Horizontal = h, Vertical = v };
            }
        }

        private void CreateInputEntity()
        {
            var connection = SystemAPI.GetSingletonEntity<NetworkId>();
            int networkId = SystemAPI.GetSingleton<NetworkId>().Value;

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponent<BotInputTag>(entity);
            EntityManager.AddComponent<PlayerInput>(entity);
            EntityManager.AddBuffer<InputBufferData<PlayerInput>>(entity);
            // The input copy system skips entities whose owner isn't this connection.
            EntityManager.AddComponentData(entity, new GhostOwner { NetworkId = networkId });
            EntityManager.SetComponentData(connection, new CommandTarget { targetEntity = entity });
        }
    }
}
