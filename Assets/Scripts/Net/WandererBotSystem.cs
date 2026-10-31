using Unity.Entities;
using Unity.NetCode;

namespace FriendlyPersonShooter.Net
{
    /// <summary>Per-bot wandering state, kept on the bot's input entity.</summary>
    public struct WandererState : IComponentData
    {
        public Unity.Mathematics.Random Random;
        public double NextTurnTime;
    }

    /// <summary>
    /// Wanderer bot profile: walks in a random direction, turning every few seconds.
    /// Runs in each bot (thin client) world. Thin clients don't receive ghosts, so the bot
    /// owns a local input entity and points its connection's CommandTarget at it; the input
    /// still travels to the server through the real network stack.
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class WandererBotSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<NetworkId>();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.HasSingleton<WandererState>())
            {
                CreateInputEntity();
                return;
            }

            double now = SystemAPI.Time.ElapsedTime;
            foreach (var (input, state) in SystemAPI.Query<RefRW<PlayerInput>, RefRW<WandererState>>())
            {
                if (now < state.ValueRO.NextTurnTime) continue;

                ref var random = ref state.ValueRW.Random;
                // Mostly walk; sometimes stand still for a moment.
                bool idle = random.NextFloat() < 0.15f;
                int h = idle ? 0 : random.NextInt(-1, 2);
                int v = idle ? 0 : random.NextInt(-1, 2);
                if (!idle && h == 0 && v == 0) v = 1;

                input.ValueRW = new PlayerInput { Horizontal = h, Vertical = v };
                state.ValueRW.NextTurnTime = now + random.NextFloat(3f, 10f);
            }
        }

        private void CreateInputEntity()
        {
            var connection = SystemAPI.GetSingletonEntity<NetworkId>();
            int networkId = SystemAPI.GetSingleton<NetworkId>().Value;

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponent<PlayerInput>(entity);
            EntityManager.AddBuffer<InputBufferData<PlayerInput>>(entity);
            // The input copy system skips entities whose owner isn't this connection.
            EntityManager.AddComponentData(entity, new GhostOwner { NetworkId = networkId });
            EntityManager.AddComponentData(entity, new WandererState
            {
                Random = new Unity.Mathematics.Random((uint)(networkId * 7919 + 17)),
                NextTurnTime = 0,
            });

            EntityManager.SetComponentData(connection, new CommandTarget { targetEntity = entity });
        }
    }
}
