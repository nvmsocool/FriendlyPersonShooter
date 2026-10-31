using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace FriendlyPersonShooter.Net
{
    /// <summary>Client and bot side: once connected, ask the server to go in game.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
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
                ecb.AddComponent<GoInGameRequest>(request);
                ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
            }
            ecb.Playback(EntityManager);
        }
    }

    /// <summary>Server side: accept the request and spawn the connection's player at a random spot.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class GoInGameServerSystem : SystemBase
    {
        private Random _random;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerPrefab>();
            _random = new Random(0x5EED1234u);
        }

        protected override void OnUpdate()
        {
            var prefab = SystemAPI.GetSingleton<PlayerPrefab>().Value;
            var networkIds = SystemAPI.GetComponentLookup<NetworkId>(true);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            float worldSize = NetTestSettings.WorldSize;

            foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                         .WithAll<GoInGameRequest>().WithEntityAccess())
            {
                var connection = request.ValueRO.SourceConnection;
                ecb.DestroyEntity(requestEntity);
                if (!networkIds.HasComponent(connection)) continue;

                int networkId = networkIds[connection].Value;
                ecb.AddComponent<NetworkStreamInGame>(connection);

                var player = ecb.Instantiate(prefab);
                ecb.SetComponent(player, new GhostOwner { NetworkId = networkId });
                var spawn = new float3(_random.NextFloat(0f, worldSize), 0.5f, _random.NextFloat(0f, worldSize));
                ecb.SetComponent(player, LocalTransform.FromPosition(spawn));

                // Destroy the player with its connection, and route bot (thin client) input to it.
                ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = player });
                ecb.AddComponent(connection, new CommandTarget { targetEntity = player });
            }
            ecb.Playback(EntityManager);
        }
    }
}
