using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Essence orbs: a kill's essence bursts out as orbs around where the enemy died, one per player who earned some,
    /// and each player collects their own by shooting it. They float slowly upward and fade after <see cref="OrbLife"/> seconds. Bots are paid
    /// straight away (they can't see orbs).
    /// </summary>
    public partial class ArenaServerSystem
    {
        public const float OrbLife = 8f;
        public const float OrbHitRadius = 0.45f;
        /// <summary>Orbs drift upward this fast (metres per second): about 2.4 m over their life.</summary>
        public const float OrbRiseSpeed = 0.3f;

        private sealed class Orb
        {
            public int Owner;
            public EssenceColor Color;
            public int Amount;
            public double Expires;
            public float3 Position;
        }

        private readonly Dictionary<Entity, Orb> _orbs = new Dictionary<Entity, Orb>();
        private readonly List<Orb> _orbSpawns = new List<Orb>();
        private readonly List<Entity> _orbScratch = new List<Entity>();
        private int _orbsDropped, _orbsShot, _orbsFaded;

        /// <summary>A kill's essence: orbs for players, straight into the wallet for bots.</summary>
        private void DropEssence(IReadOnlyList<EssenceAward> awards, float3 at)
        {
            var random = new Random(NextSeed() | 1u);
            foreach (var a in awards)
            {
                if (!_playerById.TryGetValue(a.PlayerId, out var e) || !_players.TryGetValue(e, out var p) || p.BotKit)
                {
                    WalletOf(a.PlayerId)?.PickUp(a.Color, a.Amount);
                    continue;
                }
                float angle = random.NextFloat(0f, 2f * math.PI);
                float r = random.NextFloat(1f, 2.5f);
                var spot = ExpeditionWorld.Wrap(at + new float3(math.cos(angle) * r, 0f, math.sin(angle) * r));
                float ground = ExpeditionTerrain.Active ? ExpeditionTerrain.GroundY(spot.x, spot.z, at.y) : at.y;
                spot.y = math.max(ground, at.y - 3f) + random.NextFloat(1f, 1.8f);
                _orbSpawns.Add(new Orb { Owner = a.PlayerId, Color = a.Color, Amount = a.Amount, Expires = _time + OrbLife, Position = spot });
            }
        }

        /// <summary>New orbs appear; old ones fade (their essence is gone).</summary>
        private void StepOrbs(EntityCommandBuffer ecb)
        {
            if (_orbSpawns.Count > 0)
            {
                var prefab = SystemAPI.GetSingleton<ArenaEssenceOrbPrefab>().Value;
                foreach (var o in _orbSpawns)
                {
                    var e = EntityManager.Instantiate(prefab);
                    EntityManager.SetComponentData(e, LocalTransform.FromPosition(o.Position));
                    EntityManager.SetComponentData(e, new ArenaEssenceOrb { Owner = o.Owner, Color = (byte)o.Color, Amount = o.Amount });
                    _orbs[e] = o;
                    _orbsDropped++;
                }
                _orbSpawns.Clear();
            }
            _orbScratch.Clear();
            float rise = OrbRiseSpeed * SystemAPI.Time.DeltaTime;
            foreach (var kv in _orbs)
            {
                if (_time >= kv.Value.Expires) { _orbScratch.Add(kv.Key); continue; }
                kv.Value.Position.y += rise;
                EntityManager.SetComponentData(kv.Key, LocalTransform.FromPosition(kv.Value.Position));
            }
            foreach (var e in _orbScratch)
            {
                _orbs.Remove(e);
                ecb.DestroyEntity(e);
                _orbsFaded++;
            }
        }

        /// <summary>The nearest of this player's orbs a shot meets before <paramref name="maxRange"/>.</summary>
        private bool OrbOnShot(int id, float3 eye, float3 dir, float maxRange, out Entity orb, out float at)
        {
            orb = Entity.Null;
            at = maxRange;
            foreach (var kv in _orbs)
            {
                if (kv.Value.Owner != id) continue;
                var c = ExpeditionWorld.Near(kv.Value.Position, eye);
                if (!RaySphere(eye, dir, c, OrbHitRadius, at, out float d)) continue;
                at = d;
                orb = kv.Key;
            }
            return orb != Entity.Null;
        }

        private void ShootOrb(PlayerState s, Entity e, EntityCommandBuffer ecb)
        {
            if (!_orbs.TryGetValue(e, out var o)) return;
            _orbs.Remove(e);
            ecb.DestroyEntity(e);
            WalletOf(s.Vitals.Id)?.PickUp(o.Color, o.Amount);
            _orbsShot++;
        }

        /// <summary>Leaving the layer: orbs nobody shot are collected for their owners (they earned them).</summary>
        private void ClearOrbs(EntityCommandBuffer ecb)
        {
            foreach (var kv in _orbs)
            {
                ecb.DestroyEntity(kv.Key);
                WalletOf(kv.Value.Owner)?.PickUp(kv.Value.Color, kv.Value.Amount);
            }
            _orbs.Clear();
            foreach (var o in _orbSpawns) WalletOf(o.Owner)?.PickUp(o.Color, o.Amount);
            _orbSpawns.Clear();
        }

        private string OrbLog() => $"orbs={_orbsDropped} shot={_orbsShot} faded={_orbsFaded}";
    }
}
