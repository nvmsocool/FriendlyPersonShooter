using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Pings (middle mouse): the server finds what the player is pointing at (an enemy, else the ground or a
    /// wall, else a spot far ahead) and tells everyone, who draw a marker there and on their minimap.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>How far a ping reaches (metres).</summary>
        private const float PingRange = 300f;
        /// <summary>Pointing this close to an enemy's middle pings the enemy.</summary>
        private const float PingEnemyRadius = 2.5f;
        /// <summary>Seconds between one player's pings.</summary>
        private const double PingCooldown = 0.4;

        private readonly Dictionary<int, double> _nextPing = new Dictionary<int, double>();

        private void StepPings(EntityCommandBuffer ecb)
        {
            foreach (var (input, transform, owner) in SystemAPI.Query<RefRO<ArenaInput>, RefRO<LocalTransform>, RefRO<GhostOwner>>().WithAll<ArenaPlayer>())
            {
                if (!input.ValueRO.Ping.IsSet) continue;
                int id = owner.ValueRO.NetworkId;
                if (_nextPing.TryGetValue(id, out double next) && _time < next) continue;
                _nextPing[id] = _time + PingCooldown;

                var eye = transform.ValueRO.Position + new float3(0f, ArenaSettings.EyeHeight, 0f);
                var dir = math.normalize(ArenaSettings.Forward(input.ValueRO.Yaw, input.ValueRO.Pitch));
                float reach = math.min(PingRange, ArenaLayout.RaycastCover(eye, dir, PingRange));
                bool hitWorld = reach < PingRange;
                var at = eye + dir * reach;
                byte kind = hitWorld ? PingFx.Spot : PingFx.Far;

                // An enemy in front of the wall wins.
                foreach (var kv in _enemies)
                {
                    if (!EntityManager.Exists(kv.Key)) continue;
                    var center = ExpeditionWorld.Near(EntityManager.GetComponentData<LocalTransform>(kv.Key).Position, eye);
                    float along = math.dot(center - eye, dir);
                    if (along <= 0f || along >= reach) continue;
                    float radius = kv.Value.Family == Core.Combat.EnemyFamily.Bulwark ? 5f : PingEnemyRadius;
                    if (math.lengthsq(eye + dir * along - center) > radius * radius) continue;
                    reach = along;
                    at = center;
                    kind = PingFx.Enemy;
                }

                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new PingFx { PlayerNetworkId = id, Position = at, Kind = kind });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = Entity.Null });
            }
        }
    }
}
