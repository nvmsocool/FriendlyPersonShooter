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
        public static readonly List<(ArenaAnnounce Message, float Time)> Announcements = new List<(ArenaAnnounce, float)>();
        /// <summary>Running totals for the HUD: damage dealt and healing given by the human.</summary>
        public static float DamageDealt, HealingGiven;
        public static int Kills, AllyHits, AllyHitsRefused, ChainsCashed, FragmentHits;
        public static int BestChain;
        /// <summary>Enemy attacks that landed on anyone (bolts are drawn for all; the victim gets the warning).</summary>
        public static readonly List<(EnemyAttackFx Attack, float Time)> EnemyAttacks = new List<(EnemyAttackFx, float)>();
        /// <summary>The human's level and skills (set by the view each frame), for the Esc menu's skill tree.</summary>
        public static ArenaProgress LocalProgress;
        public static bool HasProgress;
        /// <summary>Skills the human chose to buy, sent to the server by the client system.</summary>
        public static readonly List<byte> SkillBuys = new List<byte>();
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
                if (h.ChainCashed != 0)
                {
                    ArenaFx.ChainsCashed++;
                    if (h.ChainLinks > ArenaFx.BestChain) ArenaFx.BestChain = h.ChainLinks;
                }
                if (h.Fragment != 0 && h.Refused == 0) ArenaFx.FragmentHits++;
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
            foreach (var (msg, entity) in SystemAPI.Query<RefRO<ArenaAnnounce>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.Announcements.Add((msg.ValueRO, now));
            }
            foreach (var (attack, entity) in SystemAPI.Query<RefRO<EnemyAttackFx>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.EnemyAttacks.Add((attack.ValueRO, now));
            }
            if (keep && ArenaFx.SkillBuys.Count > 0 && SystemAPI.HasSingleton<NetworkId>())
            {
                foreach (byte skill in ArenaFx.SkillBuys)
                {
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new SkillBuyRequest { Skill = skill });
                    ecb.AddComponent<SendRpcCommandRequest>(rpc);
                }
                ArenaFx.SkillBuys.Clear();
            }
            ecb.Playback(EntityManager);
        }
    }
}
