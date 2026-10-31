using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Moves players from their input. Runs on the server and, predicted, on the owning client.
    /// Positions wrap at the world edge so the grid has no borders.
    /// </summary>
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class PlayerMovementSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            float speed = NetTestSettings.PlayerSpeed;
            float size = NetTestSettings.WorldSize;

            foreach (var (input, transform) in SystemAPI.Query<RefRO<PlayerInput>, RefRW<LocalTransform>>()
                         .WithAll<NetPlayer, Simulate>())
            {
                var move = new float3(input.ValueRO.Horizontal, 0f, input.ValueRO.Vertical);
                if (math.lengthsq(move) > 0f) move = math.normalize(move);

                var pos = transform.ValueRO.Position + move * speed * dt;
                pos.x = Wrap(pos.x, size);
                pos.z = Wrap(pos.z, size);
                transform.ValueRW.Position = pos;
            }
        }

        private static float Wrap(float value, float size)
        {
            float r = value % size;
            return r < 0f ? r + size : r;
        }
    }
}
