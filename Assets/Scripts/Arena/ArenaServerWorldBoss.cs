using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The world boss on the server: on a timer the Colossus (a giant Bulwark, see <see cref="WorldBoss"/>)
    /// lands at a landmark near the players. Anyone can fight it, no key needed. It calls in Skitters and
    /// Mites at 75/50/25% like the raid boss. Everyone who hit it gets Yellow, Red and Green, XP and an item.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>Debug: land the Colossus now. Set it from the Editor, or start a build with -fps-boss-now.</summary>
        public static bool DebugWorldBossNow = System.Environment.CommandLine.Contains("-fps-boss-now");

        /// <summary>Its adds leave this long after they arrive, if nobody killed them.</summary>
        private const double WorldAddsLife = 180.0;

        private WorldBoss _world;
        private BulwarkSettings _worldShape;
        private Entity _worldBossEntity = Entity.Null;
        private int _worldWaveDue;
        private readonly List<Vector2> _worldPlayers = new List<Vector2>();

        /// <summary>The shape a Bulwark is hit and stepped with: the raid's, or the Colossus's scaled one.</summary>
        private BulwarkSettings ShapeOf(EnemyState s) => s.WorldBoss ? _worldShape : _bulwark;

        private void CreateWorldBoss()
        {
            _world = new WorldBoss(null, _time);
            _worldShape = WorldBoss.Shape(_bulwark, _world.Settings.Scale);
        }

        private void StepWorldBoss()
        {
            if (_world == null) return;
            if (DebugWorldBossNow)
            {
                DebugWorldBossNow = false;
                if (!_world.Alive) SpawnWorldBoss();
            }
            // Gone without being killed (it shouldn't happen): start the timer for the next one.
            if (_world.Alive && !_enemies.ContainsKey(_worldBossEntity))
            {
                Debug.LogWarning("[WorldBoss] the Colossus disappeared without being killed");
                _world.Killed(_time);
            }
            if (_world.Due(_time) && _players.Count > 0) SpawnWorldBoss();
            if (_worldWaveDue > 0)
            {
                SpawnWorldAdds(_worldWaveDue);
                _worldWaveDue = 0;
            }
        }

        private void SpawnWorldBoss()
        {
            _worldPlayers.Clear();
            foreach (var kv in _players)
            {
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                if (RaidArena.Contains(pos)) continue;
                _worldPlayers.Add(ExpeditionWorld.Ground(pos));
            }
            var site = _world.PickSite(_map, _worldPlayers, NextSeed());
            if (site == null) return;
            var at = site.Point.World;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            // Clear of the landmark, on the flattest of a few spots around it, so it isn't hidden in a crevice.
            float x = at.X, z = at.Y, best = float.MaxValue;
            float start = random.NextFloat(0f, 2f * math.PI);
            for (int i = 0; i < 8; i++)
            {
                float a = start + i * math.PI / 4f;
                float cx = at.X + math.sin(a) * 30f, cz = at.Y + math.cos(a) * 30f;
                float h = SpawnGround(cx, cz), rough = 0f;
                for (int j = 0; j < 4; j++)
                {
                    float b = j * math.PI / 2f;
                    rough += math.abs(SpawnGround(cx + math.sin(b) * 10f, cz + math.cos(b) * 10f) - h);
                }
                if (rough >= best) continue;
                best = rough;
                x = cx;
                z = cz;
            }
            var state = new EnemyState
            {
                Family = EnemyFamily.Bulwark,
                WorldBoss = true,
                MaxHealth = _world.Health(_players.Count, _combat),
            };
            state.Boss = BulwarkBehavior.Create(new Vector3(x, SpawnGround(x, z), z), random.NextFloat(0f, 360f), _worldShape);
            _spawned.Clear();
            _worldBossEntity = SpawnEnemy(state, state.Boss.Position);
            _spawned.Clear();
            _world.Spawned(site);
            _announce.Add((AnnounceKind.WorldBossLanded, site.Id, 0, 0));
            Debug.Log($"[WorldBoss] the Colossus landed {_map.Describe(new Vector2(x, z))}, health {state.MaxHealth:0}");
        }

        /// <summary>Skitters (and from the second wave a Mite swarm) around the Colossus.</summary>
        private void SpawnWorldAdds(int wave)
        {
            if (!_enemies.TryGetValue(_worldBossEntity, out var boss)) return;
            var home = boss.Boss.Home;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            _spawned.Clear();
            int skitters = 2 + wave;
            for (int i = 0; i < skitters; i++)
            {
                var o = random.NextFloat2Direction() * random.NextFloat(10f, 22f);
                float x = home.X + o.x, z = home.Z + o.y;
                SpawnSkitter(new Vector3(x, SpawnGround(x, z) + _skitter.BodyRadius, z));
            }
            if (wave >= 2)
            {
                var o = random.NextFloat2Direction() * 16f;
                float x = home.X + o.x, z = home.Z + o.y;
                SpawnSwarm(new Vector3(x, SpawnGround(x, z) + 1.5f, z));
            }
            // Tie them to a short spawn event so stragglers leave on their own.
            var key = new SpawnKey(-2, _world.Spawns * 10L + wave, false);
            _eventEnds[key] = _time + WorldAddsLife;
            foreach (var s in _spawned) s.Spawn = key;
            _spawned.Clear();
            Debug.Log($"[WorldBoss] wave {wave}: {skitters} Skitters{(wave >= 2 ? " + a Mite swarm" : "")}");
        }

        /// <summary>The Colossus fell (called from KillEnemy before the usual kill payout): pay everyone who hit it.</summary>
        private void WorldBossKilled(EnemyState s, EntityCommandBuffer ecb)
        {
            _world.Killed(_time);
            _worldBossEntity = Entity.Null;
            var hitters = new List<int>(_ledger.HittersOf(s.Vitals.Id));
            var awards = _world.Rewards(hitters);
            foreach (var a in awards) _essenceAwarded += a.Amount;
            DropLedger.Pay(awards, WalletOf);
            foreach (int id in hitters)
            {
                AddXp(id, _world.Settings.Xp);
                if (_playerById.TryGetValue(id, out var pe)) GiveBossLoot(id, EntityManager.GetComponentData<LocalTransform>(pe).Position, ecb);
            }
            _announce.Add((AnnounceKind.WorldBossDown, hitters.Count, 0, 0));
            Debug.Log($"[WorldBoss] the Colossus fell to {hitters.Count} player(s)");
        }

        /// <summary>Where the Colossus is and how hurt, for everyone's HUD.</summary>
        private void WriteWorldBossStatus(ref ExpeditionStatus status)
        {
            if (_world == null || !_world.Alive || !_enemies.TryGetValue(_worldBossEntity, out var s)) return;
            status.WorldBoss = true;
            status.WorldBossAt = new float2(s.Boss.Home.X, s.Boss.Home.Z);
            status.WorldBossHealth = s.Vitals.Health / math.max(1f, s.Vitals.MaxHealth);
        }

        private string WorldBossLog()
        {
            if (_world == null) return "";
            float health = _enemies.TryGetValue(_worldBossEntity, out var s) ? s.Vitals.Health : -1f;
            return $"alive={_world.Alive} health={health:0} spawns={_world.Spawns} kills={_world.Kills} next={_world.NextSpawnAt:0}";
        }
    }
}
