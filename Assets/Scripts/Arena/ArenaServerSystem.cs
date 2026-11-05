using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Progression;
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
        /// <summary>Seconds between "Reset position" uses.</summary>
        private const float UnstuckCooldown = 3f;
        /// <summary>Fragment hit-target ids start here so they never clash with enemy ids.</summary>
        private const int FragmentIdBase = 1 << 28;
        private const int FragmentIdsPerPlayer = 1 << 16;

        private sealed class PlayerState
        {
            public PlayerVitals Vitals;
            public PositionHistory History = new PositionHistory(64);
            /// <summary>Two guns (Core.Arsenal), their firing state and final stats; null until the starting kit is handed out.</summary>
            public Loadout Loadout;
            public GunState[] Guns = new GunState[2];
            public GunStats[] Stats = new GunStats[2];
            public ulong[] Packed = new ulong[2];
            /// <summary>A bot already got its role's kit.</summary>
            public bool BotKit;
            public double NextUnstuck;
            public ReviveState Revive;
            public Vector3 Position;
            public PlayerProgress Progress = new PlayerProgress();
            /// <summary>When an enemy last hurt this player (Second Wind waits a while after it).</summary>
            public double LastHurt = -100;
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
            /// <summary>Glimmers: where it hovers, when it appeared, and its bob phase.</summary>
            public Vector3 Home;
            public double Born;
            public float Phase;
            /// <summary>Raid boss behavior (Family Bulwark).</summary>
            public BulwarkBehavior Boss;
            /// <summary>Max health when it isn't the family's default (the boss, scaled to the group).</summary>
            public float MaxHealth;
            /// <summary>Part of the raid fight: removed (without drops) when the run ends.</summary>
            public bool Raid;
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
            _tick = tickIndex;

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
            StepLoot(ecb);
            if (ExpeditionWorld.Active) StepExpedition(ecb);
            else RespawnEnemies();
            StepSkillRequests();
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
                          $"plagueSpreads={_plagueSpreads} glimmers={_glimmersSpawned} levelUps={_levelUps} boxes={_boxesOpened} itemsTaken={_itemsTaken} " +
                          $"blasts={_blastCount} ricochets={_ricochets} hp=[{hp}]");
                if (ExpeditionWorld.Active) LogExpedition();
                LogGuns();
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

                EnsureKit(state);
                if (!state.BotKit) _usage.Held(state.Loadout.InHand, dt);
                if (input.ValueRO.SwapGun.IsSet && state.Revive == null) SwapGun(state);
                if (input.ValueRO.Unstuck.IsSet && state.Revive == null && _time >= state.NextUnstuck)
                {
                    // Stuck somewhere: back to the nearest shrine (the arena's spawn ring). You keep your essence,
                    // but can't bank for a minute, so a reset is never a shortcut to a bank.
                    state.NextUnstuck = _time + UnstuckCooldown;
                    var random = new Unity.Mathematics.Random(NextSeed() | 1u);
                    bool inRaid = InsideRaid(id);
                    if (!inRaid && _banking != null) _banking.PositionReset(id, _time);
                    float3 spot = inRaid ? RaidArena.SpawnSpot(random.NextInt(8), 8)
                        : ExpeditionWorld.Active ? ExpeditionWorld.ShrineSpawn(transform.ValueRO.Position, ref random)
                        : ServerAccounts.SpawnOverride != null ? ServerAccounts.SpawnOverride() : float3.zero;
                    transform.ValueRW.Position = spot;
                    if (SystemAPI.HasComponent<ArenaMotion>(entity)) SystemAPI.SetComponent(entity, new ArenaMotion { Grounded = true });
                    state.History.Record(tick, ToVec(spot), true, teleported: true);
                }

                state.Vitals.Tick(dt);
                StepRegen(state, dt);
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
                loadout.ValueRW = LoadoutGhost(state);
                WriteProgress(entity, state);
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
            state.Vitals = state.MaxHealth > 0f
                ? new EnemyVitals(id, state.Family, _combat, state.MaxHealth)
                : new EnemyVitals(id, state.Family, _combat);
            state.AttackSettings = EnemyAttackSettings.For(state.Family);
            state.Attack = EnemyAttack.Create(NextSeed(), state.AttackSettings);
            EntityManager.SetComponentData(e, LocalTransform.FromPosition(ToFloat3(position)));
            EntityManager.SetComponentData(e, new ArenaEnemy
            {
                Family = state.Family, Health = state.Vitals.Health, MaxHealth = state.Vitals.Health, Hittable = true,
                Yaw = state.Boss.Yaw,
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

        /// <summary>A Skitter runs over the generated ground: up and down slopes and steps, turning back at walls and drops.</summary>
        private void FollowGround(ref SkitterBehavior k, Vector3 before)
        {
            float r = _skitter.BodyRadius;
            var p = k.Position;
            if (ExpeditionTerrain.SurfaceNear(p.X, p.Z, before.Y - r, 1.2f, 2.5f, out float ground))
            {
                k.Position = new Vector3(p.X, ground + r, p.Z);
            }
            else
            {
                k.Position = before;
                k.Heading = -k.Heading;
            }
        }

        /// <summary>Lifts a hovering enemy out of the ground if a blink put it inside a hill.</summary>
        private static void KeepAboveGround(ref Vector3 p, float clearance)
        {
            if (ExpeditionTerrain.SurfaceNear(p.X, p.Z, p.Y, 6f, 0f, out float ground) && p.Y < ground + clearance)
            {
                p = new Vector3(p.X, ground + clearance, p.Z);
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
                        if (ExpeditionTerrain.Active) KeepAboveGround(ref s.Wisp.Position, 0.8f);
                        position = s.Wisp.Position;
                        hittable = s.Wisp.IsHittable;
                        break;
                    case EnemyFamily.Skitter:
                        var before = s.Skitter.Position;
                        s.Skitter.Step(moveDt, _skitter);
                        if (ExpeditionTerrain.Active) FollowGround(ref s.Skitter, before);
                        if (s.Raid) KeepInArena(ref s.Skitter, before);
                        position = s.Skitter.Position;
                        break;
                    case EnemyFamily.Glimmer:
                        position = GlimmerPosition(s);
                        if (_time - s.Born >= GlimmerLife) _expired.Add(e);
                        break;
                    case EnemyFamily.Bulwark:
                    {
                        float fraction = s.Vitals.Health / System.Math.Max(1f, s.Vitals.MaxHealth);
                        s.Boss.Step(dt, fraction, s.Vitals.MoveMultiplier, _bulwark);
                        int wave = s.Boss.TakeWaveDue(fraction, _bulwark);
                        if (wave > 0) _raidWaveDue = wave;
                        position = s.Boss.Position;
                        break;
                    }
                    default:
                        if (stepped.Add(s.Swarm)) s.Swarm.Step(moveDt);
                        position = s.Swarm.Mites[s.MiteIndex].Position;
                        break;
                }

                float wither = s.Vitals.Tick(dt);
                if (s.Family != EnemyFamily.Glimmer)
                {
                    if (ExpeditionWorld.Active) NearCandidates(position);
                    var attack = s.Attack.Step(dt, position, _candidates, s.AttackSettings);
                    if (attack.Kind == AttackEventKind.TelegraphStarted) s.Telegraph = true;
                    else if (attack.Kind != AttackEventKind.None) s.Telegraph = false;
                    if (attack.Kind == AttackEventKind.Hit && _playerById.TryGetValue(attack.TargetId, out var victim) &&
                        _players.TryGetValue(victim, out var victimState))
                    {
                        _damageTaken += victimState.Vitals.TakeEnemyDamage(attack.Damage, out bool wentDown);
                        victimState.LastHurt = _time;
                        SendAttackFx(ecb, s.Family, attack.Damage, position, victimState);
                        if (wentDown) GoDown(victimState);
                    }
                }

                s.History.Record(tick, position, hittable, teleported);
                EntityManager.SetComponentData(e, LocalTransform.FromPosition(ToFloat3(position)));
                EntityManager.SetComponentData(e, new ArenaEnemy
                {
                    Family = s.Family, Health = s.Vitals.Health, MaxHealth = s.Vitals.MaxHealth,
                    Hittable = hittable, Telegraph = s.Telegraph,
                    Yaw = s.Boss.Yaw, PlateOpen = s.Boss.Open, PlateWarn = s.Family == EnemyFamily.Bulwark && s.Boss.OpeningSoon(_bulwark),
                    Withered = s.Vitals.Debuffs.Has(EffectKind.Wither), Exposed = s.Vitals.Debuffs.Has(EffectKind.Expose),
                    Slowed = s.Vitals.Debuffs.Has(EffectKind.Slow), Tagged = s.Vitals.IsTagged,
                });
                if (wither > 0f && s.Vitals.IsDead) _scratch.Add(e);
            }
            foreach (var e in _scratch) KillEnemy(e, ecb);
            RemoveExpiredGlimmers(ecb);

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
                if (s.Family == EnemyFamily.Bulwark) RaidBossKilled();
                ExpeditionKill(s);
                return;
            }
            switch (s.Family)
            {
                case EnemyFamily.Wisp: _wispRespawns.Add(_time + EnemyRespawnDelay); break;
                case EnemyFamily.Skitter: _skitterRespawns.Add(_time + EnemyRespawnDelay); break;
                case EnemyFamily.Glimmer: break;
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

        // Shoot (every frame of gun) is in ArenaServerGuns.cs.

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
                    case EnemyFamily.Glimmer:
                        target = HitTarget.Body(s.Vitals.Id, past.Position, GlimmerRadius);
                        break;
                    case EnemyFamily.Bulwark:
                        target = HitTarget.WithWeakPoint(s.Vitals.Id, past.Position, _bulwark.BodyRadius,
                            BulwarkBehavior.PlateCenterOf(past.Position, s.Boss.Yaw, _bulwark), _bulwark.PlateRadius);
                        target.WeakPointOnSurface = true;
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
