using System.Collections.Generic;
using FriendlyPersonShooter.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Hit markers and tracers waiting to be drawn by the human player's view.</summary>
    public static class ArenaFx
    {
        public static readonly List<(HitConfirm Hit, float Time)> Hits = new List<(HitConfirm, float)>();
        public static readonly List<(ShotFx Shot, float Time)> Shots = new List<(ShotFx, float)>();
        /// <summary>Running totals for the HUD: damage dealt and healing given by the human.</summary>
        public static float DamageDealt, HealingGiven;
        public static int Kills, AllyHits, AllyHitsRefused;
    }

    /// <summary>
    /// Client side: collects hit confirmations and other players' shots from the server. Only the
    /// human's client keeps them for drawing; bot allies just discard them.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ArenaClientFxSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            bool keep = !SystemAPI.HasSingleton<ArenaBotSeat>();
            float now = UnityEngine.Time.unscaledTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (hit, entity) in SystemAPI.Query<RefRO<HitConfirm>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (!keep) continue;
                var h = hit.ValueRO;
                ArenaFx.Hits.Add((h, now));
                ArenaFx.DamageDealt += h.Damage;
                ArenaFx.HealingGiven += h.Healed;
                if (h.Killed != 0) ArenaFx.Kills++;
                if (h.TargetIsPlayer != 0)
                {
                    ArenaFx.AllyHits++;
                    if (h.Refused != 0) ArenaFx.AllyHitsRefused++;
                }
            }
            foreach (var (shot, entity) in SystemAPI.Query<RefRO<ShotFx>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.Shots.Add((shot.ValueRO, now));
            }
            ecb.Playback(EntityManager);
        }
    }
}
