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
    /// The world boss on the server: on a timer the Colossus (see <see cref="WorldBoss"/> and
    /// <see cref="ColossusBehavior"/>) lands near a landmark close to the players. Anyone can fight it, no key
    /// needed. It fires volleys to sidestep, slams out shockwaves to jump, and is only open to full damage
    /// while stunned after a slam. It calls in Skitters and Mites at 75/50/25%. Everyone who hit it gets
    /// Yellow, Red and Green, XP and an item.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>Debug: land the Colossus now. Set it from the Editor, or start a build with -fps-boss-now.</summary>
        public static bool DebugWorldBossNow = System.Environment.CommandLine.Contains("-fps-boss-now");

        /// <summary>Its adds leave this long after they arrive, if nobody killed them.</summary>
        private const double WorldAddsLife = 180.0;

        private WorldBoss _world;
        private readonly ColossusSettings _colossus = new ColossusSettings();
        private Entity _worldBossEntity = Entity.Null;
        private int _worldWaveDue;
        private readonly List<Vector2> _worldPlayers = new List<Vector2>();

        private void CreateWorldBoss()
        {
            _world = new WorldBoss(null, _time);
        }

        /// <summary>
        /// One tick of the Colossus: turn toward the nearest player, fire volleys at the nearest one it can
        /// see, slam out shockwaves, and call adds. Returns where it is.
        /// </summary>
        private Vector3 StepColossus(EntityCommandBuffer ecb, EnemyState s, float dt)
        {
            NearCandidates(s.Colossus.Position);
            int nearest = EnemyAttack.Nearest(s.Colossus.Position, _candidates, _colossus.VolleyRange * 1.5f);
            Vector3? face = nearest >= 0 ? _candidates[nearest].Position : (Vector3?)null;
            var events = s.Colossus.Step(dt, s.Vitals.MoveMultiplier, face, _colossus);
            var from = s.Colossus.Position;
            if ((events & ColossusEvent.Volley) != 0)
            {
                int target = EnemyAttack.Nearest(from, _candidates, _colossus.VolleyRange, SightTest);
                if (target >= 0)
                {
                    foreach (var aim in ColossusBehavior.VolleyAims(from, _candidates[target].Position, _colossus))
                    {
                        FireBolt(ecb, EnemyFamily.Bulwark, from, aim, _colossus.BoltSpeed, _colossus.BoltRadius, _colossus.BoltDamage, _colossus.VolleyRange);
                    }
                }
            }
            if ((events & ColossusEvent.Slammed) != 0) StartShockwave(ecb, s.Colossus.Home, _colossus);
            float fraction = s.Vitals.Health / math.max(1f, s.Vitals.MaxHealth);
            int wave = s.Colossus.TakeWaveDue(fraction, _colossus);
            if (wave > 0) _worldWaveDue = wave;
            // Its eye glows just before a volley.
            s.Telegraph = s.Colossus.Phase == ColossusPhase.Hover && s.Colossus.NextVolley < 0.6f;
            return from;
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
            state.Colossus = ColossusBehavior.Create(new Vector3(x, SpawnGround(x, z), z), random.NextFloat(0f, 360f), _colossus);
            _spawned.Clear();
            _worldBossEntity = SpawnEnemy(state, state.Colossus.Position);
            _spawned.Clear();
            _world.Spawned(site);
            _announce.Add((AnnounceKind.WorldBossLanded, site.Id, 0, 0));
            Debug.Log($"[WorldBoss] the Colossus landed {_map.Describe(new Vector2(x, z))}, health {state.MaxHealth:0}");
        }

        /// <summary>Skitters (and from the second wave a Mite swarm) around the Colossus.</summary>
        private void SpawnWorldAdds(int wave)
        {
            if (!_enemies.TryGetValue(_worldBossEntity, out var boss)) return;
            var home = boss.Colossus.Home;
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
            status.WorldBossAt = new float2(s.Colossus.Home.X, s.Colossus.Home.Z);
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
