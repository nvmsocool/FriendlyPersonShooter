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
    /// applies payload buffs and debuffs through <see cref="LobbyCombat"/> (chains and combos),
    /// splashes Tagged hits, spreads Plague, and runs the downed/revive drill: a downed player bursts
    /// soul fragments that anyone can shoot to revive them before they bleed out. Replicates the
    /// results to ghost components so clients can draw them.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    public partial class ArenaServerSystem : SystemBase
    {
        // The expedition half (MVP step 4) is in ArenaServerExpedition.cs.
        private const int WispCount = 3;
        private const int SkitterCount = 3;
        private const float EnemyRespawnDelay = 4f;
        /// <summary>Fragment hit-target ids start here so they never clash with enemy ids.</summary>
        private const int FragmentIdBase = 1 << 28;
        private const int FragmentIdsPerPlayer = 1 << 16;

        private sealed class PlayerState
        {
            public PlayerVitals Vitals;
            public PositionHistory History = new PositionHistory(64);
            public double NextFire;
            public ReviveState Revive;
            public Vector3 Position;
        }

        private sealed class FragmentGhost
        {
            public Entity Entity;
            public PositionHistory History = new PositionHistory(64);
            public bool Seen;
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
            /// <summary>Expedition: the spawn event this enemy came from.</summary>
            public SpawnKey Spawn;
        }

        private readonly CombatSettings _combat = new CombatSettings();
        private readonly WispSettings _wisp = new WispSettings();
        private readonly SkitterSettings _skitter = new SkitterSettings();
        private readonly MiteSwarmSettings _mites = new MiteSwarmSettings();
        private LobbyCombat _lobby;
        private readonly Dictionary<int, FragmentGhost> _fragments = new Dictionary<int, FragmentGhost>();
        private readonly List<int> _fragmentScratch = new List<int>();
        private readonly List<ComboEvent> _combos = new List<ComboEvent>();
        private readonly List<int> _splash = new List<int>();
        private readonly List<EnemyVitals> _plagueVitals = new List<EnemyVitals>();
        private readonly List<Vector3> _plaguePositions = new List<Vector3>();
        private readonly List<(AnnounceKind Kind, int Value, int Player, int Other)> _announce =
            new List<(AnnounceKind, int, int, int)>();

        private readonly Dictionary<Entity, PlayerState> _players = new Dictionary<Entity, PlayerState>();
        private readonly Dictionary<Entity, EnemyState> _enemies = new Dictionary<Entity, EnemyState>();
        private readonly Dictionary<int, Entity> _playerById = new Dictionary<int, Entity>();
        private readonly Dictionary<int, Entity> _enemyById = new Dictionary<int, Entity>();
        private readonly List<double> _wispRespawns = new List<double>();
        private readonly List<double> _skitterRespawns = new List<double>();
        private double _swarmRespawnAt = -1;
        private bool _seeded;
        private uint _seed = 12345;
        private int _nextEnemyId = 1;
        private double _time;
        private double _nextLog = 10f;
        private int _shots, _enemyHits, _weakHits, _allyHits, _kills, _downs;
        private int _revives, _bleedOuts, _fragmentHits, _chainsCashed, _chainLinksCashed, _combosFormed, _splashHits, _plagueSpreads;
        private float _damageDealt, _damageTaken, _healing, _chainDamage, _splashDamage;

        private readonly List<HitTarget> _targets = new List<HitTarget>();
        private readonly List<AttackCandidate> _candidates = new List<AttackCandidate>();
        private readonly HashSet<Entity> _seen = new HashSet<Entity>();
        private readonly List<Entity> _scratch = new List<Entity>();

        protected override void OnCreate()
        {
            if (!NetTestSettings.IsFirstPerson)
            {
                Enabled = false;
                return;
            }
            RequireForUpdate<ArenaEnemyPrefab>();
            RequireForUpdate<NetworkTime>();
            _lobby = new LobbyCombat(_combat);
            if (ExpeditionWorld.Active) CreateExpedition();
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
                if (!ExpeditionWorld.Active)
                {
                    for (int i = 0; i < WispCount; i++) SpawnWisp();
                    for (int i = 0; i < SkitterCount; i++) SpawnSkitter();
                    SpawnSwarm();
                }
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            SyncPlayers(dt, tickIndex);
            StepEnemies(dt, tickIndex, ecb);
            SyncFragments(tickIndex, ecb);
            Shoot(tickIndex, ecb);
            if (ExpeditionWorld.Active) StepExpedition(ecb);
            else RespawnEnemies();
            SendAnnouncements(ecb);
            ecb.Playback(EntityManager);

            if (_time >= _nextLog)
            {
                _nextLog = _time + 10f;
                var hp = new System.Text.StringBuilder();
                foreach (var p in _players.Values) hp.Append($"{p.Vitals.Id}:{p.Vitals.Health:0}/{p.Vitals.Shield:0} ");
                Debug.Log($"[Arena] t={_time:0} players={_players.Count} enemies={_enemies.Count} shots={_shots} " +
                          $"enemyHits={_enemyHits} weakHits={_weakHits} allyHits={_allyHits} kills={_kills} " +
                          $"damageDealt={_damageDealt:0} damageTaken={_damageTaken:0} healing={_healing:0} downs={_downs} revives={_revives} bleedOuts={_bleedOuts} " +
                          $"fragmentHits={_fragmentHits} chainsCashed={_chainsCashed} avgChainLinks={(_chainsCashed > 0 ? (float)_chainLinksCashed / _chainsCashed : 0f):0.0} " +
                          $"chainDamage={_chainDamage:0} combos={_combosFormed} splashHits={_splashHits} splashDamage={_splashDamage:0} " +
                          $"plagueSpreads={_plagueSpreads} hp=[{hp}]");
                if (ExpeditionWorld.Active) LogExpedition();
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

                if (input.ValueRO.SwapPayload.IsSet) loadout.ValueRW.Payload = NextPayload(loadout.ValueRO.Payload);

                state.Vitals.Tick(dt);
                state.Position = ToVec(transform.ValueRO.Position);
                if (state.Revive != null)
                {
                    state.Revive.BodyCenter = state.Position + new Vector3(0f, ArenaWeapon.PlayerHitHeight, 0f);
                    var status = state.Revive.Step(dt);
                    if (status == ReviveStatus.Revived)
                    {
                        _revives++;
                        state.Revive = null;
                    }
                    else if (status == ReviveStatus.BledOut)
                    {
                        // Back at a shrine (here: the spawn ring). Half the carried essence would drop here.
                        _bleedOuts++;
                        _announce.Add((AnnounceKind.BledOut, 0, id, 0));
                        state.Revive = null;
                        float3 spawn;
                        if (ExpeditionWorld.Active) spawn = BledOutInExpedition(id, transform.ValueRO.Position);
                        else spawn = ServerAccounts.SpawnOverride != null ? ServerAccounts.SpawnOverride() : float3.zero;
                        transform.ValueRW.Position = spawn;
                        state.Position = ToVec(spawn);
                        state.History.Record(tick, ToVec(spawn), true, teleported: true);
                    }
                }

                var pos = transform.ValueRO.Position;
                state.History.Record(tick, ToVec(pos));

                var v = state.Vitals;
                var revive = state.Revive;
                vitals.ValueRW = new ArenaVitals
                {
                    Health = v.Health, MaxHealth = v.MaxHealth, Shield = v.Shield,
                    RespawnIn = revive != null ? revive.BleedOutLeft : 0f,
                    ReviveProgress = revive != null ? revive.ProgressFraction : 0f,
                };
                bool holding = _lobby.Chains.TryGetHeld(id, _time, out var chain);
                buffs.ValueRW = new ArenaBuffs
                {
                    Empower = v.DamageBonus,
                    EmpowerRemaining = v.Buffs.RemainingOf(EffectKind.Empower),
                    ShieldRemaining = v.Buffs.RemainingOf(EffectKind.Shield),
                    HasteRemaining = v.Buffs.RemainingOf(EffectKind.Hasted),
                    MoveMultiplier = v.MoveSpeedMultiplier,
                    JumpMultiplier = v.JumpMultiplier,
                    RelayHits = v.HasRelay ? v.RelayHitsLeft : 0,
                    RelayPayload = v.RelayPayload,
                    ChainLinks = holding ? chain.Links : 0,
                    ChainTimeLeft = holding ? chain.TimeLeft : 0f,
                    Blitz = v.IsBlitzed,
                };
            }

            if (_seen.Count == _players.Count) return;
            _scratch.Clear();
            foreach (var e in _players.Keys) if (!_seen.Contains(e)) _scratch.Add(e);
            foreach (var e in _scratch)
            {
                _lobby.PlayerGone(_players[e].Vitals.Id);
                if (ExpeditionWorld.Active) ExpeditionPlayerGone(_players[e].Vitals.Id);
                _playerById.Remove(_players[e].Vitals.Id);
                _players.Remove(e);
            }
        }

        /// <summary>The payload swap key cycles Vigor, Might, Haste, Relay.</summary>
        public static PayloadKind NextPayload(PayloadKind p)
        {
            switch (p)
            {
                case PayloadKind.Vigor: return PayloadKind.Might;
                case PayloadKind.Might: return PayloadKind.Haste;
                case PayloadKind.Haste: return PayloadKind.Relay;
                default: return PayloadKind.Vigor;
            }
        }

        private void GoDown(PlayerState state)
        {
            _downs++;
            _lobby.PlayerGone(state.Vitals.Id);
            var body = state.Position + new Vector3(0f, ArenaWeapon.PlayerHitHeight, 0f);
            state.Revive = new ReviveState(state.Vitals, body, NextSeed(), _combat.Revive);
        }

        // ---------- Soul fragments ----------

        private static int FragmentKey(int ownerId, int fragmentId) => FragmentIdBase + ownerId * FragmentIdsPerPlayer + fragmentId;

        /// <summary>Mirrors every downed player's fragments as ghosts, and records where they were for lag compensation.</summary>
        private void SyncFragments(int tick, EntityCommandBuffer ecb)
        {
            foreach (var f in _fragments.Values) f.Seen = false;
            var prefab = SystemAPI.GetSingleton<ArenaFragmentPrefab>().Value;
            foreach (var p in _players.Values)
            {
                if (p.Revive == null) continue;
                var frags = p.Revive.Fragments;
                for (int i = 0; i < frags.Count; i++)
                {
                    var frag = frags[i];
                    int key = FragmentKey(p.Vitals.Id, frag.Id);
                    if (!_fragments.TryGetValue(key, out var ghost))
                    {
                        ghost = new FragmentGhost { Entity = EntityManager.Instantiate(prefab) };
                        _fragments[key] = ghost;
                    }
                    ghost.Seen = true;
                    ghost.History.Record(tick, frag.Position);
                    EntityManager.SetComponentData(ghost.Entity, LocalTransform.FromPosition(ToFloat3(frag.Position)));
                    EntityManager.SetComponentData(ghost.Entity, new ArenaFragment
                    {
                        OwnerNetworkId = p.Vitals.Id, Opacity = p.Revive.Opacity(frag),
                    });
                }
            }
            _fragmentScratch.Clear();
            foreach (var kv in _fragments) if (!kv.Value.Seen) _fragmentScratch.Add(kv.Key);
            foreach (int key in _fragmentScratch)
            {
                ecb.DestroyEntity(_fragments[key].Entity);
                _fragments.Remove(key);
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
            _spawned.Add(state);
            return e;
        }

        private uint NextSeed() => _seed = _seed * 1664525u + 1013904223u;

        private void SpawnWisp(Vector3? home = null)
        {
            var state = new EnemyState { Family = EnemyFamily.Wisp };
            state.Wisp = WispBehavior.Create(home ?? RandomHome(2.2f), NextSeed(), _wisp);
            SpawnEnemy(state, state.Wisp.Position);
        }

        private void SpawnSkitter(Vector3? home = null)
        {
            var state = new EnemyState { Family = EnemyFamily.Skitter };
            state.Skitter = SkitterBehavior.Create(home ?? RandomHome(_skitter.BodyRadius), NextSeed(), _skitter);
            SpawnEnemy(state, state.Skitter.Position);
        }

        private void SpawnSwarm(Vector3? home = null)
        {
            var swarm = new MiteSwarm(home ?? RandomHome(1.5f), NextSeed(), _mites);
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
                // Slow makes enemies move (and so dodge and chase) slower.
                float moveDt = dt * s.Vitals.MoveMultiplier;
                switch (s.Family)
                {
                    case EnemyFamily.Wisp:
                        teleported = s.Wisp.Step(moveDt, _wisp);
                        position = s.Wisp.Position;
                        hittable = s.Wisp.IsHittable;
                        break;
                    case EnemyFamily.Skitter:
                        s.Skitter.Step(moveDt, _skitter);
                        position = s.Skitter.Position;
                        break;
                    default:
                        if (stepped.Add(s.Swarm)) s.Swarm.Step(moveDt);
                        position = s.Swarm.Mites[s.MiteIndex].Position;
                        break;
                }

                float wither = s.Vitals.Tick(dt);
                if (ExpeditionWorld.Active) NearCandidates(position);
                var attack = s.Attack.Step(dt, position, _candidates, s.AttackSettings);
                if (attack.Kind == AttackEventKind.TelegraphStarted) s.Telegraph = true;
                else if (attack.Kind != AttackEventKind.None) s.Telegraph = false;
                if (attack.Kind == AttackEventKind.Hit && _playerById.TryGetValue(attack.TargetId, out var victim) &&
                    _players.TryGetValue(victim, out var victimState))
                {
                    _damageTaken += victimState.Vitals.TakeEnemyDamage(attack.Damage, out bool wentDown);
                    if (wentDown) GoDown(victimState);
                }

                s.History.Record(tick, position, hittable, teleported);
                EntityManager.SetComponentData(e, LocalTransform.FromPosition(ToFloat3(position)));
                EntityManager.SetComponentData(e, new ArenaEnemy
                {
                    Family = s.Family, Health = s.Vitals.Health, MaxHealth = _combat.EnemyHealth(s.Family),
                    Hittable = hittable, Telegraph = s.Telegraph,
                    Withered = s.Vitals.Debuffs.Has(EffectKind.Wither), Exposed = s.Vitals.Debuffs.Has(EffectKind.Expose),
                    Slowed = s.Vitals.Debuffs.Has(EffectKind.Slow), Tagged = s.Vitals.IsTagged,
                });
                if (wither > 0f && s.Vitals.IsDead) _scratch.Add(e);
            }
            foreach (var e in _scratch) KillEnemy(e, ecb);

            // Plague: Withered + Tagged enemies spread their Wither to neighbours.
            _plagueVitals.Clear();
            _plaguePositions.Clear();
            foreach (var kv in _enemies)
            {
                _plagueVitals.Add(kv.Value.Vitals);
                _plaguePositions.Add(ToVec(EntityManager.GetComponentData<LocalTransform>(kv.Key).Position));
            }
            _plagueSpreads += Plague.Step(dt, _plagueVitals, _plaguePositions, _combat);
            _combos.Clear();
            foreach (var v in _plagueVitals) _lobby.Combos.Check(v, _combos);
            QueueCombos();
        }

        private void KillEnemy(Entity e, EntityCommandBuffer ecb)
        {
            if (!_enemies.TryGetValue(e, out var s)) return;
            _enemies.Remove(e);
            _lobby.Combos.Forget(s.Vitals.Id, false);
            _enemyById.Remove(s.Vitals.Id);
            ecb.DestroyEntity(e);
            if (ExpeditionWorld.Active)
            {
                if (s.Family == EnemyFamily.Mite) s.Swarm.Alive[s.MiteIndex] = false;
                ExpeditionKill(s);
                return;
            }
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

                BuildTargets(seenTick, shooter.Vitals.Id, eye);
                var end = eye + dir * range;
                var payload = loadout.ValueRO.Payload;
                if (HitScan.Cast(ToVec(eye), ToVec(dir), range, _targets, out var hit, shooter.Vitals.Id))
                {
                    end = ToFloat3(hit.Point);
                    _combos.Clear();
                    var confirm = new HitConfirm { Point = end };
                    if (hit.IsPlayer)
                    {
                        var target = _players[_playerById[hit.TargetId]];
                        var result = _lobby.HitPlayer(shooter.Vitals, target.Vitals, payload, _time, _combos);
                        SocialBuff(shooter.Vitals.Id, target.Vitals.Id, result);
                        _allyHits++;
                        _healing += result.Healed + result.ShieldGained;
                        confirm.TargetIsPlayer = 1;
                        confirm.Refused = (byte)(result.Refused != RefuseReason.None ? 1 : 0);
                        confirm.Healed = result.Healed + result.ShieldGained;
                        bool chained = result.ChainEvent == ChainEvent.Started || result.ChainEvent == ChainEvent.Grew;
                        confirm.ChainLinks = (byte)(chained ? result.ChainLinks : 0);
                    }
                    else if (hit.TargetId >= FragmentIdBase)
                    {
                        int ownerId = (hit.TargetId - FragmentIdBase) / FragmentIdsPerPlayer;
                        int fragmentId = (hit.TargetId - FragmentIdBase) % FragmentIdsPerPlayer;
                        confirm.Fragment = 1;
                        confirm.TargetIsPlayer = 1;
                        if (_playerById.TryGetValue(ownerId, out var ownerEntity) && _players.TryGetValue(ownerEntity, out var downed) &&
                            downed.Revive != null)
                        {
                            var revive = downed.Revive;
                            var r = _lobby.HitFragment(shooter.Vitals, revive, fragmentId, payload);
                            confirm.Refused = (byte)(r.Accepted ? 0 : 1);
                            confirm.ReviveProgress = revive.ProgressFraction;
                            if (r.Accepted)
                            {
                                _fragmentHits++;
                                SocialFragment(shooter.Vitals.Id, ownerId, revive, r.Revived);
                            }
                            if (r.Revived)
                            {
                                _revives++;
                                downed.Revive = null;
                                _announce.Add((AnnounceKind.Revived, 0, ownerId, shooter.Vitals.Id));
                            }
                        }
                        else confirm.Refused = 1;
                    }
                    else
                    {
                        var enemyEntity = _enemyById[hit.TargetId];
                        var enemy = _enemies[enemyEntity];
                        var result = _lobby.HitEnemy(shooter.Vitals, enemy.Vitals, payload, ArenaWeapon.BaseDamage, hit.WeakPoint, _time, _combos);
                        _ledger?.RecordHit(enemy.Vitals.Id, shooter.Vitals.Id);
                        _enemyHits++;
                        if (result.WeakPoint) _weakHits++;
                        _damageDealt += result.DamageDealt;
                        if (result.ChainEvent == ChainEvent.Cashed && result.ChainLinks >= 2)
                        {
                            _chainsCashed++;
                            _chainLinksCashed += result.ChainLinks;
                            _chainDamage += result.DamageDealt * result.ChainBonus / (1f + result.ChainBonus);
                            confirm.ChainCashed = 1;
                            confirm.ChainLinks = (byte)result.ChainLinks;
                            confirm.ChainBonus = result.ChainBonus;
                            _announce.Add((AnnounceKind.ChainCashed, result.ChainLinks, shooter.Vitals.Id, 0));
                        }
                        if (result.SplashDamage > 0f) Splash(enemy, hit.TargetId, result.SplashDamage, ecb);
                        if (result.Killed)
                        {
                            KillEnemy(enemyEntity, ecb);
                            _kills++;
                        }
                        confirm.WeakPoint = (byte)(result.WeakPoint ? 1 : 0);
                        confirm.Killed = (byte)(result.Killed ? 1 : 0);
                        confirm.Damage = result.DamageDealt;
                    }
                    QueueCombos();

                    if (connections.TryGetValue(owner.ValueRO.NetworkId, out var shooterConnection))
                    {
                        var rpc = ecb.CreateEntity();
                        ecb.AddComponent(rpc, confirm);
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

        /// <summary>A hit on a Tagged enemy splashes plain damage to enemies close to it.</summary>
        private void Splash(EnemyState source, int hitId, float amount, EntityCommandBuffer ecb)
        {
            Vector3 center = default;
            foreach (var t in _targets)
            {
                if (!t.IsPlayer && t.Id == hitId) center = t.BodyCenter;
            }
            _splash.Clear();
            _lobby.Hits.SplashTargets(center, hitId, _targets, _splash);
            foreach (int id in _splash)
            {
                if (!_enemyById.TryGetValue(id, out var e) || !_enemies.TryGetValue(e, out var s) || s.Vitals.IsDead) continue;
                _splashDamage += _lobby.Hits.ApplySplash(s.Vitals, amount);
                _splashHits++;
                if (s.Vitals.IsDead)
                {
                    KillEnemy(e, ecb);
                    _kills++;
                }
            }
        }

        private void QueueCombos()
        {
            foreach (var c in _combos)
            {
                _combosFormed++;
                if (c.FirstDiscovery) _announce.Add((AnnounceKind.ComboDiscovered, (int)c.Kind, c.TargetIsPlayer ? c.TargetId : 0, 0));
            }
            _combos.Clear();
        }

        private void SendAnnouncements(EntityCommandBuffer ecb)
        {
            foreach (var a in _announce)
            {
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new ArenaAnnounce { Kind = a.Kind, Value = a.Value, PlayerNetworkId = a.Player, OtherNetworkId = a.Other });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = Entity.Null });
            }
            _announce.Clear();
        }

        /// <summary>Hit targets as the shooter saw them. In the wrapping expedition world each is moved to its copy nearest the shooter.</summary>
        private void BuildTargets(int tick, int shooterId, float3 eye)
        {
            _targets.Clear();
            foreach (var kv in _players)
            {
                if (kv.Value.Vitals.Id == shooterId || kv.Value.Vitals.IsDown) continue;
                var p = NearVec(kv.Value.History.Sample(tick, 0f), eye);
                var center = p + new Vector3(0f, ArenaWeapon.PlayerHitHeight, 0f);
                _targets.Add(HitTarget.Body(kv.Value.Vitals.Id, center, ArenaWeapon.PlayerHitRadius, isPlayer: true));
            }
            foreach (var kv in _enemies)
            {
                var s = kv.Value;
                var past = s.History.SampleState(tick, 0f);
                past.Position = NearVec(past.Position, eye);
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
            // Soul fragments, rewound like everything else. A downed player can't shoot their own.
            foreach (var kv in _fragments)
            {
                int ownerId = (kv.Key - FragmentIdBase) / FragmentIdsPerPlayer;
                if (ownerId == shooterId) continue;
                var p = NearVec(kv.Value.History.Sample(tick, 0f), eye);
                _targets.Add(HitTarget.Body(kv.Key, p, _combat.Revive.FragmentRadius));
            }
        }

        private static Vector3 ToVec(float3 v) => new Vector3(v.x, v.y, v.z);

        private static Vector3 NearVec(Vector3 p, float3 reference) => ToVec(ExpeditionWorld.Near(ToFloat3(p), reference));
        private static float3 ToFloat3(Vector3 v) => new float3(v.X, v.Y, v.Z);
    }
}
