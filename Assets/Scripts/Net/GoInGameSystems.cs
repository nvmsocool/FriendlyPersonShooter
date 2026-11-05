using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace FriendlyPersonShooter.Net
{
    /// <summary>Client side: once connected, ask the server to go in game.</summary>
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

    /// <summary>Server-side spawn rule for new players.</summary>
    public static class ServerAccounts
    {
        /// <summary>Picks every new player's spawn point (the arena and expedition set this); null spawns at the origin.</summary>
        public static System.Func<float3> SpawnOverride;
    }

    /// <summary>Server side: accept the request and spawn the connection's player.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class GoInGameServerSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<PlayerPrefab>();
        }

        protected override void OnUpdate()
        {
            var prefab = SystemAPI.GetSingleton<PlayerPrefab>().Value;
            var networkIds = SystemAPI.GetComponentLookup<NetworkId>(true);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (request, _, requestEntity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<GoInGameRequest>>().WithEntityAccess())
            {
                var connection = request.ValueRO.SourceConnection;
                ecb.DestroyEntity(requestEntity);
                if (!networkIds.HasComponent(connection)) continue;

                ecb.AddComponent<NetworkStreamInGame>(connection);

                var player = ecb.Instantiate(prefab);
                ecb.SetComponent(player, new GhostOwner { NetworkId = networkIds[connection].Value });
                var spawn = ServerAccounts.SpawnOverride != null ? ServerAccounts.SpawnOverride() : float3.zero;
                ecb.SetComponent(player, LocalTransform.FromPosition(spawn));

                // Destroy the player with its connection, and route the connection's input to it.
                ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = player });
                ecb.AddComponent(connection, new CommandTarget { targetEntity = player });
            }
            ecb.Playback(EntityManager);
        }
    }
}
