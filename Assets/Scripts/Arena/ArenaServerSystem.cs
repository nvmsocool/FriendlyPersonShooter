using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Runs the arena on the server, using the engine-free combat rules in Core.Combat:
    /// keeps Wisps, Skitters and a Mite swarm alive, steps their movement and telegraphed attacks,
    /// resolves every shot with lag compensation (targets are rewound to what the shooter saw),
    /// applies payload buffs and debuffs, and respawns downed players. Replicates the results to
    /// ghost components so clients can draw them.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    public partial class ArenaServerSystem : SystemBase
    {
        private const int WispCount = 3;
        private const int SkitterCount = 3;
        private const float EnemyRespawnDelay = 4f;
        private const float PlayerRespawnDelay = 3f;

        private sealed class PlayerState
        {
            public PlayerVitals Vitals;
            public PositionHistory History = new PositionHistory(64);
            public float NextFire;
            public float RespawnIn;
        }

        private sealed class EnemyState
        {
            public EnemyFamily Family;
            public EnemyVitals Vitals;
            public EnemyAttack Attack;
            public EnemyAttackSettings AttackSettings;
            public PositionHistory History = new PositionHistory(64);
            public WispBehavior Wisp;
            public SkitterBehavior Skitter;
            public MiteSwarm Swarm;
            public int MiteIndex;
            public bool Telegraph;
        }

        private readonly CombatSettings _combat = new CombatSettings();
        private readonly WispSettings _wisp = new WispSettings();
        private readonly SkitterSettings _skitter = new SkitterSettings();
        private readonly MiteSwarmSettings _mites = new MiteSwarmSettings();
        private HitResolver _resolver;

        private readonly Dictionary<Entity, PlayerState> _players = new Dictionary<Entity, PlayerState>();
        private readonly Dictionary<Entity, EnemyState> _enemies = new Dictionary<Entity, EnemyState>();
        private readonly Dictionary<int, Entity> _playerById = new Dictionary<int, Entity>();
        private readonly Dictionary<int, Entity> _enemyById = new Dictionary<int, Entity>();
        private readonly List<float> _wispRespawns = new List<float>();
        private readonly List<float> _skitterRespawns = new List<float>();
        private float _swarmRespawnAt = -1;
        private bool _seeded;
        private uint _seed = 12345;
        private int _nextEnemyId = 1;
        private float _time;
        private float _nextLog = 10f;
        private int _shots, _enemyHits, _weakHits, _allyHits, _kills, _downs;
        private float _damageDealt, _damageTaken, _healing;

        private readonly List<HitTarget> _targets = new List<HitTarget>();
        private readonly List<AttackCandidate> _candidates = new List<AttackCandidate>();
        private readonly HashSet<Entity> _seen = new HashSet<Entity>();
        private readonly List<Entity> _scratch = new List<Entity>();

        protected override void OnCreate()
        {
            if (NetTestSettings.Game != GameMode.Arena)
            {
                Enabled = false;
                return;
            }
            RequireForUpdate<ArenaEnemyPrefab>();
            RequireForUpdate<NetworkTime>();
            _resolver = new HitResolver(_combat);
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            _time += dt;
            var tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            int tickIndex = tick.IsValid ? (int)tick.TickIndexForValidTick : 0;

            if (!_seeded)
            {
                _seeded = true;
                for (int i = 0; i < WispCount; i++) SpawnWisp();
                for (int i = 0; i < SkitterCount; i++) SpawnSkitter();
                SpawnSwarm();
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            SyncPlayers(dt, tickIndex);
            StepEnemies(dt, tickIndex, ecb);
            Shoot(tickIndex, ecb);
            RespawnEnemies();
            ecb.Playback(EntityManager);

            if (_time >= _nextLog)
            {
                _nextLog = _time + 10f;
                var hp = new System.Text.StringBuilder();
                foreach (var p in _players.Values) hp.Append($"{p.Vitals.Id}:{p.Vitals.Health:0}/{p.Vitals.Shield:0} ");
                Debug.Log($"[Arena] t={_time:0} players={_players.Count} enemies={_enemies.Count} shots={_shots} " +
                          $"enemyHits={_enemyHits} weakHits={_weakHits} allyHits={_allyHits} kills={_kills} " +
                          $"damageDealt={_damageDealt:0} damageTaken={_damageTaken:0} healing={_healing:0} downs={_downs} hp=[{hp}]");
            }
        }

        // ---------- Players ----------

        private void SyncPlayers(float dt, int tick)
        {
            _seen.Clear();
            foreach (var (owner, transform, vitals, buffs, loadout, input, entity) in SystemAPI
                         .Query<RefRO<GhostOwner>, RefRW<LocalTransform>, RefRW<ArenaVitals>, RefRW<ArenaBuffs>,
                             RefRW<ArenaLoadout>, RefRO<ArenaInput>>()
                         .WithAll<ArenaPlayer>().WithEntityAccess())
            {
                _seen.Add(entity);
                int id = owner.ValueRO.NetworkId;
                if (!_players.TryGetValue(entity, out var state))
                {
                    state = new PlayerState { Vitals = new PlayerVitals(id, _combat) };
                    _players[entity] = state;
                }
                _playerById[id] = entity;

                if (input.ValueRO.SwapPayload.IsSet)
                {
                    loadout.ValueRW.Payload = loadout.ValueRO.Payload == PayloadKind.Vigor ? PayloadKind.Might : PayloadKind.Vigor;
                }

                state.Vitals.Tick(dt);
                if (state.Vitals.IsDown)
                {
                    if (state.RespawnIn <= 0f) state.RespawnIn = PlayerRespawnDelay;
                    state.RespawnIn -= dt;
                    if (state.RespawnIn <= 0f)
                    {
                        state.RespawnIn = 0f;
                        state.Vitals.SetHealth(state.Vitals.MaxHealth);
                        state.Vitals.Buffs.Clear();
                        var spawn = ServerAccounts.SpawnOverride != null ? ServerAccounts.SpawnOverride() : float3.zero;
                        transform.ValueRW.Position = spawn;
                        state.History.Record(tick, ToVec(spawn), true, teleported: true);
                    }
                }

                var pos = transform.ValueRO.Position;
                state.History.Record(tick, ToVec(pos));

                var v = state.Vitals;
                vitals.ValueRW = new ArenaVitals { Health = v.Health, MaxHealth = v.MaxHealth, Shield = v.Shield, RespawnIn = state.RespawnIn };
                buffs.ValueRW = new ArenaBuffs
                {
                    Empower = v.DamageBonus,
                    EmpowerRemaining = v.Buffs.RemainingOf(EffectKind.Empower),
                    ShieldRemaining = v.Buffs.RemainingOf(EffectKind.Shield),
                };
            }

            if (_seen.Count == _players.Count) return;
            _scratch.Clear();
            foreach (var e in _players.Keys) if (!_seen.Contains(e)) _scratch.Add(e);
            foreach (var e in _scratch)
            {
                _playerById.Remove(_players[e].Vitals.Id);
                _players.Remove(e);
            }
        }

        // ---------- Enemies ----------

        private Vector3 RandomHome(float height)
        {
            var r = new Unity.Mathematics.Random(_seed = _seed * 1664525u + 1013904223u);
            var p = r.NextFloat2(new float2(-18f, 4f), new float2(18f, 20f));
            return new Vector3(p.x, height, p.y);
        }

        private Entity SpawnEnemy(EnemyState state, Vector3 position)
        {
            var prefab = SystemAPI.GetSingleton<ArenaEnemyPrefab>().Value;
            var e = EntityManager.Instantiate(prefab);
            int id = _nextEnemyId++;
            state.Vitals = new EnemyVitals(id, state.Family, _combat);
            state.AttackSettings = EnemyAttackSettings.For(state.Family);
            state.Attack = EnemyAttack.Create(NextSeed(), state.AttackSettings);
            EntityManager.SetComponentData(e, LocalTransform.FromPosition(ToFloat3(position)));
            EntityManager.SetComponentData(e, new ArenaEnemy
            {
                Family = state.Family, Health = state.Vitals.Health, MaxHealth = state.Vitals.Health, Hittable = true,
            });
            _enemies[e] = state;
            _enemyById[id] = e;
            return e;
        }

        private uint NextSeed() => _seed = _seed * 1664525u + 1013904223u;

        private void SpawnWisp()
        {
            var state = new EnemyState { Family = EnemyFamily.Wisp };
            state.Wisp = WispBehavior.Create(RandomHome(2.2f), NextSeed(), _wisp);
            SpawnEnemy(state, state.Wisp.Position);
        }

        private void SpawnSkitter()
        {
            var state = new EnemyState { Family = EnemyFamily.Skitter };
            state.Skitter = SkitterBehavior.Create(RandomHome(_skitter.BodyRadius), NextSeed(), _skitter);
            SpawnEnemy(state, state.Skitter.Position);
        }

        private void SpawnSwarm()
        {
            var swarm = new MiteSwarm(RandomHome(1.5f), NextSeed(), _mites);
            for (int i = 0; i < swarm.Mites.Length; i++)
            {
                var state = new EnemyState { Family = EnemyFamily.Mite, Swarm = swarm, MiteIndex = i };
                SpawnEnemy(state, swarm.Mites[i].Position);
            }
        }

        private void StepEnemies(float dt, int tick, EntityCommandBuffer ecb)
        {
            _candidates.Clear();
            foreach (var kv in _players)
            {
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                _candidates.Add(new AttackCandidate(kv.Value.Vitals.Id, ToVec(pos + new float3(0f, ArenaWeapon.PlayerHitHeight, 0f)),
                    !kv.Value.Vitals.IsDown));
            }

            // Each swarm steps once, however many of its mites are alive.
            var stepped = new HashSet<MiteSwarm>();
            _scratch.Clear();
            foreach (var kv in _enemies)
            {
                var e = kv.Key;
                var s = kv.Value;
                bool teleported = false;
                Vector3 position;
                bool hittable = true;
                switch (s.Family)
                {
                    case EnemyFamily.Wisp:
                        teleported = s.Wisp.Step(dt, _wisp);
                        position = s.Wisp.Position;
                        hittable = s.Wisp.IsHittable;
                        break;
                    case EnemyFamily.Skitter:
                        s.Skitter.Step(dt, _skitter);
                        position = s.Skitter.Position;
                        break;
                    default:
                        if (stepped.Add(s.Swarm)) s.Swarm.Step(dt);
                        position = s.Swarm.Mites[s.MiteIndex].Position;
                        break;
                }

                float wither = s.Vitals.Tick(dt);
                var attack = s.Attack.Step(dt, position, _candidates, s.AttackSettings);
                if (attack.Kind == AttackEventKind.TelegraphStarted) s.Telegraph = true;
                else if (attack.Kind != AttackEventKind.None) s.Telegraph = false;
                if (attack.Kind == AttackEventKind.Hit && _playerById.TryGetValue(attack.TargetId, out var victim) &&
                    _players.TryGetValue(victim, out var victimState))
                {
                    bool wasUp = !victimState.Vitals.IsDown;
                    _damageTaken += victimState.Vitals.TakeEnemyDamage(attack.Damage);
                    if (wasUp && victimState.Vitals.IsDown) _downs++;
                }

                s.History.Record(tick, position, hittable, teleported);
                EntityManager.SetComponentData(e, LocalTransform.FromPosition(ToFloat3(position)));
                EntityManager.SetComponentData(e, new ArenaEnemy
                {
                    Family = s.Family, Health = s.Vitals.Health, MaxHealth = _combat.EnemyHealth(s.Family),
                    Hittable = hittable, Telegraph = s.Telegraph,
                    Withered = s.Vitals.Debuffs.Has(EffectKind.Wither), Exposed = s.Vitals.Debuffs.Has(EffectKind.Expose),
                });
                if (wither > 0f && s.Vitals.IsDead) _scratch.Add(e);
            }
            foreach (var e in _scratch) KillEnemy(e, ecb);
        }

        private void KillEnemy(Entity e, EntityCommandBuffer ecb)
        {
            if (!_enemies.TryGetValue(e, out var s)) return;
            _enemies.Remove(e);
            _enemyById.Remove(s.Vitals.Id);
            ecb.DestroyEntity(e);
            switch (s.Family)
            {
                case EnemyFamily.Wisp: _wispRespawns.Add(_time + EnemyRespawnDelay); break;
                case EnemyFamily.Skitter: _skitterRespawns.Add(_time + EnemyRespawnDelay); break;
                default:
                    s.Swarm.Alive[s.MiteIndex] = false;
                    if (s.Swarm.AliveCount == 0) _swarmRespawnAt = _time + EnemyRespawnDelay;
                    break;
            }
        }

        private void RespawnEnemies()
        {
            for (int i = _wispRespawns.Count - 1; i >= 0; i--)
            {
                if (_time < _wispRespawns[i]) continue;
                _wispRespawns.RemoveAt(i);
                SpawnWisp();
            }
            for (int i = _skitterRespawns.Count - 1; i >= 0; i--)
            {
                if (_time < _skitterRespawns[i]) continue;
                _skitterRespawns.RemoveAt(i);
                SpawnSkitter();
            }
            if (_swarmRespawnAt >= 0 && _time >= _swarmRespawnAt)
            {
                _swarmRespawnAt = -1;
                SpawnSwarm();
            }
        }

        // ---------- Shooting ----------

        private void Shoot(int tick, EntityCommandBuffer ecb)
        {
            var connections = new Dictionary<int, Entity>();
            foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithEntityAccess())
            {
                connections[id.ValueRO.Value] = connection;
            }

            foreach (var (input, transform, loadout, owner, entity) in SystemAPI
                         .Query<RefRO<ArenaInput>, RefRO<LocalTransform>, RefRO<ArenaLoadout>, RefRO<GhostOwner>>()
                         .WithAll<ArenaPlayer>().WithEntityAccess())
            {
                if (input.ValueRO.Fire == 0 || !_players.TryGetValue(entity, out var shooter)) continue;
                if (shooter.Vitals.IsDown || _time < shooter.NextFire) continue;
                shooter.NextFire = _time + ArenaWeapon.FireInterval;
                _shots++;

                // Rewind everyone else to the tick this player was looking at when they fired.
                int delay = 0;
                if (SystemAPI.HasComponent<CommandDataInterpolationDelay>(entity))
                {
                    delay = (int)SystemAPI.GetComponent<CommandDataInterpolationDelay>(entity).Delay;
                }
                int seenTick = tick - delay;

                var eye = transform.ValueRO.Position + new float3(0f, ArenaSettings.EyeHeight, 0f);
                var dir = math.normalize(ArenaSettings.Forward(input.ValueRO.Yaw, input.ValueRO.Pitch));
                float range = math.min(ArenaWeapon.Range, ArenaLayout.RaycastCover(eye, dir, ArenaWeapon.Range));

                BuildTargets(seenTick, shooter.Vitals.Id);
                var end = eye + dir * range;
                var payload = loadout.ValueRO.Payload;
                if (HitScan.Cast(ToVec(eye), ToVec(dir), range, _targets, out var hit, shooter.Vitals.Id))
                {
                    end = ToFloat3(hit.Point);
                    HitResult result;
                    if (hit.IsPlayer)
                    {
                        var target = _players[_playerById[hit.TargetId]];
                        result = _resolver.HitPlayer(shooter.Vitals, target.Vitals, payload);
                        _allyHits++;
                        _healing += result.Healed + result.ShieldGained;
                    }
                    else
                    {
                        var enemyEntity = _enemyById[hit.TargetId];
                        var enemy = _enemies[enemyEntity];
                        result = _resolver.HitEnemy(shooter.Vitals, enemy.Vitals, payload, ArenaWeapon.BaseDamage, hit.WeakPoint);
                        if (result.Killed) KillEnemy(enemyEntity, ecb);
                        _enemyHits++;
                        if (result.WeakPoint) _weakHits++;
                        if (result.Killed) _kills++;
                        _damageDealt += result.DamageDealt;
                    }

                    if (connections.TryGetValue(owner.ValueRO.NetworkId, out var shooterConnection))
                    {
                        var rpc = ecb.CreateEntity();
                        ecb.AddComponent(rpc, new HitConfirm
                        {
                            TargetIsPlayer = (byte)(result.TargetIsPlayer ? 1 : 0),
                            WeakPoint = (byte)(result.WeakPoint ? 1 : 0),
                            Killed = (byte)(result.Killed ? 1 : 0),
                            Refused = (byte)(result.Refused != RefuseReason.None ? 1 : 0),
                            Damage = result.DamageDealt,
                            Healed = result.Healed + result.ShieldGained,
                            Point = end,
                        });
                        ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = shooterConnection });
                    }
                }

                // Everyone else draws the tracer; the shooter already drew their own.
                var fx = ecb.CreateEntity();
                ecb.AddComponent(fx, new ShotFx
                {
                    ShooterNetworkId = owner.ValueRO.NetworkId, From = eye - new float3(0f, 0.2f, 0f), To = end, Payload = payload,
                });
                ecb.AddComponent(fx, new SendRpcCommandRequest { TargetConnection = Entity.Null });
            }
        }

        private void BuildTargets(int tick, int shooterId)
        {
            _targets.Clear();
            foreach (var kv in _players)
            {
                if (kv.Value.Vitals.Id == shooterId || kv.Value.Vitals.IsDown) continue;
                var p = kv.Value.History.Sample(tick, 0f);
                var center = p + new Vector3(0f, ArenaWeapon.PlayerHitHeight, 0f);
                _targets.Add(HitTarget.Body(kv.Value.Vitals.Id, center, ArenaWeapon.PlayerHitRadius, isPlayer: true));
            }
            foreach (var kv in _enemies)
            {
                var s = kv.Value;
                var past = s.History.SampleState(tick, 0f);
                HitTarget target;
                switch (s.Family)
                {
                    case EnemyFamily.Wisp:
                        target = HitTarget.Body(s.Vitals.Id, past.Position, _wisp.BodyRadius);
                        break;
                    case EnemyFamily.Skitter:
                        target = HitTarget.WithWeakPoint(s.Vitals.Id, past.Position, _skitter.BodyRadius,
                            past.Position + _skitter.CoreOffset, _skitter.CoreRadius);
                        break;
                    default:
                        target = HitTarget.Body(s.Vitals.Id, past.Position, _mites.MiteRadius);
                        break;
                }
                target.Hittable = past.Hittable;
                _targets.Add(target);
            }
        }

        private static Vector3 ToVec(float3 v) => new Vector3(v.x, v.y, v.z);
        private static float3 ToFloat3(Vector3 v) => new float3(v.X, v.Y, v.Z);
    }
}
