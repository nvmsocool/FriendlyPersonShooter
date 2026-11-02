using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// The human tester's client: once connected, ask the server to go in game. Bots send their own
    /// request with their account and party (see <see cref="BotSessionSystem"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class GoInGameClientSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<NetworkId>();
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                         .WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(connection);
                var request = ecb.CreateEntity();
                ecb.AddComponent(request, new GoInGameRequest());
                ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
            }
            ecb.Playback(EntityManager);
        }
    }

    /// <summary>Server-side memory of accounts, kept across their logins.</summary>
    public static class ServerAccounts
    {
        /// <summary>Where each account's player was when last seen, so a returning player resumes there.</summary>
        public static readonly Dictionary<int, float3> LastPosition = new Dictionary<int, float3>();
    }

    /// <summary>
    /// Server side: accept the request and spawn the connection's player. A party member spawns next to
    /// a party member who is already online; a returning account resumes where it logged off; anyone
    /// else starts at a random spot.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class GoInGameServerSystem : SystemBase
    {
        private Random _random;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerPrefab>();
            _random = new Random(0x5EED1234u);
            ServerAccounts.LastPosition.Clear();
        }

        protected override void OnUpdate()
        {
            var prefab = SystemAPI.GetSingleton<PlayerPrefab>().Value;
            var networkIds = SystemAPI.GetComponentLookup<NetworkId>(true);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (request, login, requestEntity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<GoInGameRequest>>().WithEntityAccess())
            {
                var connection = request.ValueRO.SourceConnection;
                ecb.DestroyEntity(requestEntity);
                if (!networkIds.HasComponent(connection)) continue;

                int networkId = networkIds[connection].Value;
                ecb.AddComponent<NetworkStreamInGame>(connection);

                var player = ecb.Instantiate(prefab);
                ecb.SetComponent(player, new GhostOwner { NetworkId = networkId });
                ecb.SetComponent(player, LocalTransform.FromPosition(ChooseSpawn(login.ValueRO)));
                ecb.AddComponent(player, new PlayerAccount
                {
                    AccountId = login.ValueRO.AccountId,
                    GroupId = login.ValueRO.GroupId,
                    IsBot = login.ValueRO.IsBot != 0,
                });

                // Destroy the player with its connection, and route bot (thin client) input to it.
                ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = player });
                ecb.AddComponent(connection, new CommandTarget { targetEntity = player });
            }
            ecb.Playback(EntityManager);
        }

        private float3 ChooseSpawn(GoInGameRequest login)
        {
            if (login.GroupId != 0)
            {
                foreach (var (account, transform) in SystemAPI.Query<RefRO<PlayerAccount>, RefRO<LocalTransform>>())
                {
                    if (account.ValueRO.GroupId != login.GroupId) continue;
                    var offset = _random.NextFloat2Direction() * 3f;
                    return Wrap(transform.ValueRO.Position + new float3(offset.x, 0f, offset.y));
                }
            }
            if (login.AccountId != 0 && ServerAccounts.LastPosition.TryGetValue(login.AccountId, out var last))
            {
                return last;
            }
            float worldSize = NetTestSettings.WorldSize;
            return new float3(_random.NextFloat(0f, worldSize), 0.5f, _random.NextFloat(0f, worldSize));
        }

        private static float3 Wrap(float3 p)
        {
            float size = NetTestSettings.WorldSize;
            p.x = (p.x % size + size) % size;
            p.z = (p.z % size + size) % size;
            return p;
        }
    }

    /// <summary>
    /// Server side bookkeeping for accounts: applies party changes sent mid-session, remembers where
    /// each account is, and tells each bot where it is and which lobby it's in.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LobbyServerSystem))]
    public partial class PlayerAccountServerSystem : SystemBase
    {
        private const double StatusInterval = 0.25;
        private double _nextStatus;
        private readonly Dictionary<int, Entity> _connectionOf = new Dictionary<int, Entity>();
        private readonly Dictionary<Entity, int> _groupRequests = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, float2> _teleports = new Dictionary<Entity, float2>();

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            _groupRequests.Clear();
            foreach (var (request, change, entity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SetGroupRequest>>().WithEntityAccess())
            {
                _groupRequests[request.ValueRO.SourceConnection] = change.ValueRO.GroupId;
                ecb.DestroyEntity(entity);
            }

            _teleports.Clear();
            foreach (var (request, teleport, entity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<TeleportRequest>>().WithEntityAccess())
            {
                _teleports[request.ValueRO.SourceConnection] = new float2(teleport.ValueRO.X, teleport.ValueRO.Z);
                ecb.DestroyEntity(entity);
            }

            _connectionOf.Clear();
            foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithEntityAccess())
            {
                _connectionOf[id.ValueRO.Value] = connection;
            }

            double now = SystemAPI.Time.ElapsedTime;
            bool sendStatus = now >= _nextStatus;
            if (sendStatus) _nextStatus = now + StatusInterval;

            foreach (var (account, owner, transform) in SystemAPI
                         .Query<RefRW<PlayerAccount>, RefRO<GhostOwner>, RefRW<LocalTransform>>())
            {
                int networkId = owner.ValueRO.NetworkId;
                if (!_connectionOf.TryGetValue(networkId, out var connection)) continue;

                if (_groupRequests.TryGetValue(connection, out int group)) account.ValueRW.GroupId = group;
                // Test only, so only bots may teleport.
                if (account.ValueRO.IsBot && _teleports.TryGetValue(connection, out var to))
                {
                    float size = NetTestSettings.WorldSize;
                    transform.ValueRW.Position = new float3((to.x % size + size) % size, 0.5f, (to.y % size + size) % size);
                }
                var pos = transform.ValueRO.Position;
                if (account.ValueRO.AccountId != 0) ServerAccounts.LastPosition[account.ValueRO.AccountId] = pos;

                if (!sendStatus || !account.ValueRO.IsBot) continue;
                LobbyStats.LobbyOfPlayer.TryGetValue(networkId, out int lobby);
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new BotStatus { X = pos.x, Z = pos.z, LobbyId = lobby });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
            ecb.Playback(EntityManager);
        }
    }
}
