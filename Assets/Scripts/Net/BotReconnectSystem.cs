using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Reconnects a bot whose connection failed or dropped. Netcode connects once at startup and
    /// never retries, so bots that time out while 100 worlds start together would sit idle forever.
    /// Only for bots talking to a server in another process; in-process bots use IPC and don't drop.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BotReconnectSystem : SystemBase
    {
        private EntityQuery _connections;
        private double _retryAt = -1;
        private Unity.Mathematics.Random _random;

        protected override void OnCreate()
        {
            _connections = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
            _random = new Unity.Mathematics.Random((uint)(World.SequenceNumber * 7919 + 1));
        }

        protected override void OnUpdate()
        {
            if (ClientServerBootstrap.ServerWorld != null) return;
            if (!_connections.IsEmpty)
            {
                _retryAt = -1;
                return;
            }

            double now = SystemAPI.Time.ElapsedTime;
            if (_retryAt < 0)
            {
                // Spread retries out so a crowd of bots doesn't stampede the server.
                _retryAt = now + _random.NextFloat(0.5f, 3f);
                return;
            }
            if (now < _retryAt) return;

            _retryAt = -1;
            // The new connection gets a new network id, so the wanderer rebuilds its input entity.
            EntityManager.DestroyEntity(GetEntityQuery(ComponentType.ReadOnly<WandererState>()));
            var endpoint = ClientServerBootstrap.DefaultConnectAddress;
            SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(EntityManager, endpoint);
            Debug.Log($"[Bots] {World.Name} reconnecting to {endpoint.Address}");
        }
    }
}
