using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Progression;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Each player decides their own hits. Every tick the trigger is (or was just) held, the player's game works out
    /// where that tick's shots would land, against exactly what it draws: the enemies, other players and soul
    /// fragments where they're shown, the same hit shapes (<see cref="EnemyHitShapes"/>), the zones' own colliders.
    /// The shots' spread comes from a seed both sides know (<see cref="Seed"/>), so the host fires them the same way
    /// and just takes the player's word for what they hit (<see cref="ArenaInput.ShotTick"/>).
    /// The host still decides crystals, world objects (orbs, boxes, the capsule, the gate) and everything for bots.
    /// </summary>
    public static class LocalShots
    {
        /// <summary>Shots per tick a player reports (a tick rarely fires more than two).</summary>
        public const int Claims = 3;

        /// <summary>What a reported shot hit.</summary>
        public const int Miss = 0, Enemy = 1, EnemyWeak = 2, Player = 3, Fragment = 4;

        /// <summary>The spread seed for a player's shots in one tick (shot n is the n-th draw).</summary>
        public static uint Seed(int playerId, int tick) => SeedHash.Of(0x5A0751u, playerId, tick) | 1u;

        private static readonly List<HitTarget> Targets = new List<HitTarget>();
        private static readonly CombatSettings Combat = new CombatSettings();
        private static ulong _statsFor = ulong.MaxValue;
        private static GunStats _stats;

        /// <summary>The players' hit spheres (for heals and buffs), centred this high above their feet.</summary>
        private static Vector3 PlayerCentre(float3 feet) => new Vector3(feet.x, feet.y + ArenaWeapon.PlayerHitHeight, feet.z);

        /// <summary>
        /// Fills <paramref name="input"/>'s shot reports for <paramref name="tick"/>, as the local player
        /// (<paramref name="local"/>) sees the world. Leaves them empty if the player has no gun.
        /// </summary>
        public static void Report(EntityManager em, Entity local, int tick, ref ArenaInput input)
        {
            input.ShotTick = 0;
            if (!em.HasComponent<ArenaLoadout>(local)) return;
            var loadout = em.GetComponentData<ArenaLoadout>(local);
            ulong packed = loadout.Gun;
            var gun = Gun.Unpack(packed);
            if (gun.IsEmpty) return;
            if (packed != _statsFor)
            {
                _stats = GunStats.For(gun);
                _statsFor = packed;
            }
            int myId = em.GetComponentData<GhostOwner>(local).NetworkId;
            var feet = em.GetComponentData<LocalTransform>(local).Position;
            var eye = feet + new float3(0f, ArenaSettings.EyeHeight, 0f);
            int ranks = em.HasComponent<ArenaProgress>(local) ? em.GetComponentData<ArenaProgress>(local).Ranks : 0;
            float focus = 1f + (em.HasComponent<ArenaBuffs>(local) ? em.GetComponentData<ArenaBuffs>(local).Focus : 0f);
            float reach = _stats.Range * PlayerProgress.RangeFor(ranks);

            BuildTargets(em, myId, eye, focus);

            var aim = ArenaSettings.Forward(input.Yaw, input.Pitch);
            var origin = new Vector3(eye.x, eye.y, eye.z);
            var rng = new CombatRandom(Seed(myId, tick));
            float spread = _stats.SpreadFor(input.Zoom != 0);
            for (int n = 0; n < Claims; n++)
            {
                var d = FireControl.Spread(new Vector3(aim.x, aim.y, aim.z), spread, ref rng);
                float range = math.min(reach, ArenaLayout.RaycastCover(eye, new float3(d.X, d.Y, d.Z), reach));
                int kind = Miss, id = 0;
                float dist = range;
                if (HitScan.Cast(origin, d, range, Targets, out var hit, myId))
                {
                    id = hit.TargetId;
                    dist = hit.Distance;
                    kind = hit.TargetId >= FragmentIdBase ? Fragment : hit.IsPlayer ? Player : hit.WeakPoint ? EnemyWeak : Enemy;
                }
                input.SetShot(n, kind, id, dist);
            }
            input.ShotTick = tick;
        }

        /// <summary>Soul fragment target ids start here (as on the host).</summary>
        public const int FragmentIdBase = 1 << 28;

        private static void BuildTargets(EntityManager em, int myId, float3 eye, float focus)
        {
            Targets.Clear();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaEnemy>(), ComponentType.ReadOnly<LocalTransform>()))
            using (var enemies = q.ToComponentDataArray<ArenaEnemy>(Allocator.Temp))
            using (var at = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    var e = enemies[i];
                    if (e.Id == 0) continue;
                    var p = ExpeditionWorld.Near(at[i].Position, eye);
                    bool roaming = e.Family == EnemyFamily.Bulwark && !e.World && !e.Boss;
                    var t = EnemyHitShapes.For(e.Id, e.Family, e.Tier, e.Elite, e.World, roaming, new Vector3(p.x, p.y, p.z), e.Yaw, e.Hittable);
                    if (t.HasWeakPoint) t.WeakPointRadius *= focus * math.max(1f, e.WeakScale);
                    Targets.Add(t);
                }
            }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                       ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<ArenaVitals>()))
            using (var owners = q.ToComponentDataArray<GhostOwner>(Allocator.Temp))
            using (var at = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var vitals = q.ToComponentDataArray<ArenaVitals>(Allocator.Temp))
            {
                for (int i = 0; i < owners.Length; i++)
                {
                    if (owners[i].NetworkId == myId || vitals[i].Health <= 0f) continue;
                    var p = ExpeditionWorld.Near(at[i].Position, eye);
                    Targets.Add(HitTarget.Body(owners[i].NetworkId, PlayerCentre(p), ArenaWeapon.PlayerHitRadius, isPlayer: true));
                }
            }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaFragment>(), ComponentType.ReadOnly<LocalTransform>()))
            using (var frags = q.ToComponentDataArray<ArenaFragment>(Allocator.Temp))
            using (var at = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < frags.Length; i++)
                {
                    // A downed player can't shoot their own fragments.
                    if (frags[i].OwnerNetworkId == myId || frags[i].TargetId == 0) continue;
                    var p = ExpeditionWorld.Near(at[i].Position, eye);
                    Targets.Add(HitTarget.Body(frags[i].TargetId, new Vector3(p.x, p.y, p.z), Combat.Revive.FragmentRadius));
                }
            }
        }
    }
}
