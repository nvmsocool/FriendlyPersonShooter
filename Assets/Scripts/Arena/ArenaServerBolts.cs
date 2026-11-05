using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Enemy bolts and shockwaves on the server. A ranged enemy's attack fires a bolt that flies where the
    /// target stood (see <see cref="EnemyBolt"/>), so players can dodge it; terrain, structures, tree trunks
    /// and foliage stop it. Enemies only start an attack on someone they can see. The Colossus's slam sends
    /// a ring along the ground (see <see cref="Shockwave"/>) that players jump.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>Sight lines are drawn this far short of the target, so a player's own feet and nearby ground don't block them.</summary>
        private const float SightSlack = 0.6f;

        private readonly List<EnemyBolt> _bolts = new List<EnemyBolt>();
        private readonly List<Shockwave> _waves = new List<Shockwave>();
        private readonly List<WaveTarget> _waveTargets = new List<WaveTarget>();
        private readonly List<int> _waveCaught = new List<int>();
        private System.Func<Vector3, Vector3, bool> _canSee;
        private int _nextBoltId = 1, _nextWaveId = 1;
        private int _boltsFired, _boltHits, _waveHits, _blockedSights;

        /// <summary>True when nothing solid or leafy lies between two points (enemy eyes to a player's body).</summary>
        private bool CanSee(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.Length();
            if (len < SightSlack * 2f) return true;
            var dir = d / len;
            float blocked = ArenaLayout.SightCover(ToFloat3(from), ToFloat3(dir), len);
            bool clear = blocked >= len - SightSlack;
            if (!clear) _blockedSights++;
            return clear;
        }

        private System.Func<Vector3, Vector3, bool> SightTest => _canSee ?? (_canSee = CanSee);

        /// <summary>Fires a bolt from an enemy toward a point and tells every client to draw it.</summary>
        private void FireBolt(EntityCommandBuffer ecb, EnemyFamily family, Vector3 from, Vector3 aim, float speed, float radius,
            float damage, float range)
        {
            var d = aim - from;
            float len = d.Length();
            var dir = len > 1e-4f ? d / len : Vector3.UnitZ;
            float reach = range * EnemyBolt.OvershootRange;
            float clear = ArenaLayout.SightCover(ToFloat3(from), ToFloat3(dir), reach);
            var bolt = EnemyBolt.Fire(_nextBoltId++, family, from, aim, speed, radius, damage, range, clear);
            _bolts.Add(bolt);
            _boltsFired++;
            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, new EnemyBoltFx
            {
                Id = bolt.Id, Family = family, From = ToFloat3(from), Velocity = ToFloat3(bolt.Velocity),
                Life = bolt.Life, Radius = radius,
            });
            ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = Entity.Null });
        }

        /// <summary>A slam's ring, starting at <paramref name="center"/> (the ground under the Colossus).</summary>
        private void StartShockwave(EntityCommandBuffer ecb, Vector3 center, ColossusSettings s)
        {
            var wave = new Shockwave(_nextWaveId++, center, s.WaveSpeed, s.WaveRange, s.WaveWidth, s.WaveDamage);
            _waves.Add(wave);
            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, new ShockwaveFx { Center = ToFloat3(center), Speed = s.WaveSpeed, Range = s.WaveRange, Width = s.WaveWidth });
            ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = Entity.Null });
        }

        /// <summary>Moves every bolt and ring and hurts whoever they catch.</summary>
        private void StepBolts(float dt, EntityCommandBuffer ecb)
        {
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                if (ExpeditionWorld.Active) NearCandidates(b.Position);
                int hit = b.Step(dt, _candidates);
                if (hit >= 0) HurtPlayer(ecb, _candidates[hit].Id, b.Family, b.Damage, b.Origin, b.Id);
                if (hit >= 0) _boltHits++;
                if (b.Done) _bolts.RemoveAt(i);
                else _bolts[i] = b;
            }

            if (_waves.Count == 0) return;
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i];
                var center = ToFloat3(w.Center);
                _waveTargets.Clear();
                foreach (var kv in _players)
                {
                    var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                    if (ExpeditionWorld.Active) pos = ExpeditionWorld.Near(pos, center);
                    bool grounded = !EntityManager.HasComponent<ArenaMotion>(kv.Key) || EntityManager.GetComponentData<ArenaMotion>(kv.Key).Grounded;
                    int id = kv.Value.Vitals.Id;
                    _waveTargets.Add(new WaveTarget(id, ToVec(pos), grounded, !kv.Value.Vitals.IsDown && !InsideRaid(id)));
                }
                _waveCaught.Clear();
                w.Step(dt, _waveTargets, _waveCaught);
                foreach (int id in _waveCaught)
                {
                    HurtPlayer(ecb, id, EnemyFamily.Bulwark, w.Damage, w.Center, 0);
                    _waveHits++;
                }
                if (w.Done) _waves.RemoveAt(i);
            }
        }

        private void HurtPlayer(EntityCommandBuffer ecb, int id, EnemyFamily family, float damage, Vector3 from, int boltId)
        {
            if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var victim) || victim.Vitals.IsDown) return;
            if (_run != null && InsideRaid(id)) damage *= _run.DamageTakenMultiplier;
            _damageTaken += victim.Vitals.TakeEnemyDamage(damage, out bool wentDown);
            victim.LastHurt = _time;
            SendAttackFx(ecb, family, damage, from, victim, boltId);
            if (wentDown) GoDown(victim);
        }

        private string BoltLog() =>
            $"boltsFired={_boltsFired} boltHits={_boltHits} waveHits={_waveHits} sightsBlocked={_blockedSights} inFlight={_bolts.Count}";
    }
}
