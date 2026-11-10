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
        /// <summary>Payload capsules in the air (removed when caught or when their flight ends).</summary>
        public static readonly List<(CapsuleFx Capsule, float Time)> Capsules = new List<(CapsuleFx, float)>();
        /// <summary>Capsules just shot out of the air (a flash where they were).</summary>
        public static readonly List<(CapsuleFx Capsule, float Time)> CapsuleCatches = new List<(CapsuleFx, float)>();

        /// <summary>Where a flying capsule is <paramref name="t"/> seconds after launch (Core.Arsenal.CapsuleFlight).</summary>
        public static Unity.Mathematics.float3 CapsulePosition(CapsuleFx c, float t) =>
            c.From + c.Velocity * t + new Unity.Mathematics.float3(0f, -0.5f * FriendlyPersonShooter.Core.Arsenal.CapsuleFlight.Gravity * t * t, 0f);
        public static readonly List<(ArenaAnnounce Message, float Time)> Announcements = new List<(ArenaAnnounce, float)>();
        /// <summary>Running totals for the HUD: damage dealt and healing given by the human.</summary>
        public static float DamageDealt, HealingGiven;
        public static int Kills, AllyHits, AllyHitsRefused, ChainsCashed, FragmentHits;
        public static int BestChain;
        /// <summary>Enemy attacks that landed on anyone (bolts are drawn for all; the victim gets the warning).</summary>
        public static readonly List<(EnemyAttackFx Attack, float Time)> EnemyAttacks = new List<(EnemyAttackFx, float)>();
        /// <summary>Enemy bolts in flight (drawn until they end or hit someone).</summary>
        public static readonly List<(EnemyBoltFx Bolt, float Time)> Bolts = new List<(EnemyBoltFx, float)>();
        /// <summary>Pings (middle mouse), newest last; each player's new ping replaces their old one.</summary>
        public static readonly List<(PingFx Ping, float Time)> Pings = new List<(PingFx, float)>();
        /// <summary>The Colossus's shockwave rings.</summary>
        public static readonly List<(ShockwaveFx Wave, float Time)> Waves = new List<(ShockwaveFx, float)>();
        /// <summary>The human's level and skills (set by the view each frame), for the Esc menu's skill tree.</summary>
        public static ArenaProgress LocalProgress;
        public static bool HasProgress;
        /// <summary>Skills the human chose to buy, sent to the server by the client system.</summary>
        public static readonly List<byte> SkillBuys = new List<byte>();
        /// <summary>Loot boxes the human has opened this session (drawn shut and dark).</summary>
        public static readonly HashSet<int> OpenedBoxes = new HashSet<int>();
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
            foreach (var (capsule, entity) in SystemAPI.Query<RefRO<CapsuleFx>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (!keep) continue;
                int who = capsule.ValueRO.ShooterNetworkId;
                if (capsule.ValueRO.Caught != 0)
                {
                    // A flash where it was caught (From = that spot).
                    int i = ArenaFx.Capsules.FindIndex(c => c.Capsule.ShooterNetworkId == who);
                    if (i >= 0)
                    {
                        var c = ArenaFx.Capsules[i];
                        var flash = c.Capsule;
                        flash.From = ArenaFx.CapsulePosition(c.Capsule, now - c.Time);
                        ArenaFx.CapsuleCatches.Add((flash, now));
                    }
                }
                ArenaFx.Capsules.RemoveAll(c => c.Capsule.ShooterNetworkId == who);
                if (capsule.ValueRO.Caught == 0) ArenaFx.Capsules.Add((capsule.ValueRO, now));
            }
            foreach (var (msg, entity) in SystemAPI.Query<RefRO<ArenaAnnounce>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.Announcements.Add((msg.ValueRO, now));
            }
            foreach (var (opened, entity) in SystemAPI.Query<RefRO<LootBoxOpened>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.OpenedBoxes.Add(opened.ValueRO.BoxId);
            }
            foreach (var (attack, entity) in SystemAPI.Query<RefRO<EnemyAttackFx>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.EnemyAttacks.Add((attack.ValueRO, now));
            }
            foreach (var (bolt, entity) in SystemAPI.Query<RefRO<EnemyBoltFx>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.Bolts.Add((bolt.ValueRO, now));
            }
            foreach (var (ping, entity) in SystemAPI.Query<RefRO<PingFx>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (!keep) continue;
                int who = ping.ValueRO.PlayerNetworkId;
                ArenaFx.Pings.RemoveAll(p => p.Ping.PlayerNetworkId == who);
                ArenaFx.Pings.Add((ping.ValueRO, now));
            }
            foreach (var (wave, entity) in SystemAPI.Query<RefRO<ShockwaveFx>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
                if (keep) ArenaFx.Waves.Add((wave.ValueRO, now));
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
