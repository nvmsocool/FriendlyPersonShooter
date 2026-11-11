using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Avatars;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
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
            /// <summary>The gun (Core.Arsenal; slot 1 stays empty), its firing state and final stats; null until the starting kit is handed out.</summary>
            public Loadout Loadout;
            public GunState Gun;
            public GunStats Stats;
            public ulong Packed;
            /// <summary>A bot already got its role's kit.</summary>
            public bool BotKit;
            /// <summary>The shooting gallery: the open one, or the last one (for its result).</summary>
            public GalleryState Gallery = new GalleryState();
            public byte GallerySerial;
            /// <summary>Fire was held last tick (gallery shots are one per click).</summary>
            public bool FireHeld;
            public double NextGalleryShot;
            /// <summary>Just after a gallery closes, a held trigger can't open the next thing.</summary>
            public double NextObjectShot;
            /// <summary>Ticks this player sees the world behind (their interpolation delay).</summary>
            public int AimDelay;
            /// <summary>Where they last looked (degrees).</summary>
            public float LastYaw;
            /// <summary>Boss rewards not yet claimed; each lands as a chest beside its player, one at a time.</summary>
            public System.Collections.Generic.Queue<LootItem[]> Rewards = new System.Collections.Generic.Queue<LootItem[]>();
            /// <summary>The reward chest waiting to be shot: where, its three items, and its card colours' seed.</summary>
            public bool ChestOut;
            public float3 ChestPos;
            public LootItem[] ChestItems;
            public uint ChestSeed;
            /// <summary>The payload capsule's flight while it's in the air.</summary>
            public CapsuleFlight Capsule;
            /// <summary>Bots: when the server catches their capsule for them (0 = it won't be).</summary>
            public double BotCatchAt;
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
            /// <summary>Expedition: the group this enemy came from (a spawn point, a bloom, a boss's adds), or unset.</summary>
            public SpawnGroupId Spawn;
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
            /// <summary>Never attacks (Wisp Storm targets: the room tests aim, the timer is the pressure).</summary>
            public bool Passive;
            /// <summary>Hold the Core: a Skitter that runs straight at the core and bursts on it.</summary>
            public bool CoreRunner;
            /// <summary>The Twins boss: which twin (0 or 1), else -1.</summary>
            public int Twin = -1;
            /// <summary>The Colossus, the open-world boss (Family Bulwark, but its own behavior).</summary>
            public bool WorldBoss;
            public ColossusBehavior Colossus;
            /// <summary>Difficulty tier (the layer it spawned on); raid and arena enemies are tier 1.</summary>
            public int Tier = 1;
            /// <summary>An elite (two families mixed), or None.</summary>
            public EliteKind Elite;
            /// <summary>A Bulwark roaming the world (not a raid boss, not the Colossus).</summary>
            public bool Roaming;
            public SentinelBehavior Sentinel;
            public DiverBehavior Diver;
            /// <summary>A Blinker elite (a Skitter): seconds to its next blink.</summary>
            public float BlinkTime;
        }

        private readonly CombatSettings _combat = new CombatSettings();
        private readonly WispSettings _wisp = new WispSettings();
        private readonly SkitterSettings _skitter = new SkitterSettings();
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
            RequireForUpdate<ArenaEnemyPrefab>();
            RequireForUpdate<NetworkTime>();
            _lobby = new LobbyCombat(_combat);
            if (ExpeditionWorld.Active) CreateExpedition();
        }

        private uint _avatarCounter = (uint)System.Environment.TickCount;

        /// <summary>Gives every newly joined player a random robot avatar.</summary>
        private void PickAvatars()
        {
            foreach (var avatar in SystemAPI.Query<RefRW<ArenaAvatar>>().WithAll<ArenaPlayer>())
            {
                if (avatar.ValueRO.Seed != 0) continue;
                avatar.ValueRW.Seed = FriendlyPersonShooter.Core.Avatars.RobotLook.SeedFrom(++_avatarCounter * 2654435761u);
            }
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
            PickAvatars();
            SyncPlayers(dt, tickIndex);
            StepEnemies(dt, tickIndex, ecb);
            StepBolts(dt, ecb);
            SyncFragments(tickIndex, ecb);
            Shoot(tickIndex, ecb);
            StepGalleries(ecb);
            StepOrbs(ecb);
            StepPings(ecb);
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
                          $"blasts={_blastCount} ricochets={_ricochets} own={_ownShots} reported={_reportedHits} unreported={_unreported} {GalleryLog()} {CapsuleLog()} {OrbLog()} hp=[{hp}]");
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
                if (input.ValueRO.Unstuck.IsSet && state.Revive == null && _time >= state.NextUnstuck)
                {
                    // Stuck somewhere: back to the nearest shrine (the arena's spawn ring). You keep your essence,
                    // but can't bank for a minute, so a reset is never a shortcut to a bank.
                    state.NextUnstuck = _time + UnstuckCooldown;
                    var random = new Unity.Mathematics.Random(NextSeed() | 1u);
                    bool inRaid = InsideRaid(id);
                    if (!inRaid && _banking != null) _banking.PositionReset(id, _time);
                    float3 spot = inRaid ? RaidArena.RoomSpawn(_run != null ? _run.Room : RaidRoomKind.Boss, random.NextInt(8), 8)
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
                    Health = v.Health, MaxHealth = v.MaxHealth, Shield = v.TotalShield,
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
                    GravityMultiplier = RaidGravity(id),
                    Glow = LanternGlow(id),
                    RelayHits = v.HasRelay ? v.RelayHitsLeft : 0,
                    RelayPayload = v.RelayPayload,
                    ChainLinks = holding ? chain.Links : 0,
                    ChainTimeLeft = holding ? chain.TimeLeft : 0f,
                    ChainPayloads = holding ? chain.Payloads : 0u,
                    Blitz = v.IsBlitzed,
                    FireRate = v.FireRateMultiplier,
                    FrenzyRemaining = v.Buffs.RemainingOf(EffectKind.Frenzied),
                    Ward = v.Ward,
                    WardRemaining = v.Buffs.RemainingOf(EffectKind.Warded),
                    Focus = v.WeakPointBonus,
                    FocusRemaining = v.Buffs.RemainingOf(EffectKind.Focused),
                    Combos = Combos.OnPlayer(v.Buffs),
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
                        OwnerNetworkId = p.Vitals.Id, Opacity = p.Revive.Opacity(frag), TargetId = key,
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
            if (state.Roaming && state.MaxHealth <= 0f) state.MaxHealth = _combat.WorldBulwarkHealth;
            if (state.Elite != EliteKind.None)
            {
                float baseHealth = state.MaxHealth > 0f ? state.MaxHealth : _combat.EnemyHealth(state.Family);
                state.MaxHealth = baseHealth * Elites.HealthMultiplier;
            }
            state.Vitals = state.MaxHealth > 0f
                ? new EnemyVitals(id, state.Family, _combat, state.MaxHealth)
                : new EnemyVitals(id, state.Family, _combat);
            state.AttackSettings = AttackFor(state);
            state.Attack = EnemyAttack.Create(NextSeed(), state.AttackSettings);
            EntityManager.SetComponentData(e, LocalTransform.FromPosition(ToFloat3(position)));
            EntityManager.SetComponentData(e, new ArenaEnemy
            {
                Family = state.Family, Health = state.Vitals.Health, MaxHealth = state.Vitals.Health, Hittable = true,
                Yaw = state.Boss.Yaw, World = state.WorldBoss, Boss = IsRaidBoss(state), Tier = (byte)state.Tier, Elite = state.Elite,
                LookSeed = BugLook.SeedFrom((uint)id), Id = id, WeakScale = 1f,
            });
            _enemies[e] = state;
            _enemyById[id] = e;
            _spawned.Add(state);
            return e;
        }

        private uint NextSeed() => _seed = _seed * 1664525u + 1013904223u;

        private void SpawnWisp(Vector3? home = null, int tier = 1)
        {
            var state = new EnemyState { Family = EnemyFamily.Wisp, Tier = EnemyTiers.Clamp(tier) };
            state.Wisp = WispBehavior.Create(home ?? RandomHome(2.2f), NextSeed(), WispOf(state));
            SpawnEnemy(state, state.Wisp.Position);
        }

        private void SpawnSkitter(Vector3? home = null, int tier = 1, EliteKind elite = EliteKind.None)
        {
            var state = new EnemyState { Family = EnemyFamily.Skitter, Tier = EnemyTiers.Clamp(tier), Elite = elite, BlinkTime = BlinkerInterval };
            state.Skitter = SkitterBehavior.Create(home ?? RandomHome(_skitter.BodyRadius), NextSeed(), SkitterOf(state));
            SpawnEnemy(state, state.Skitter.Position);
        }

        private void SpawnSwarm(Vector3? home = null, int tier = 1)
        {
            tier = EnemyTiers.Clamp(tier);
            var swarm = new MiteSwarm(home ?? RandomHome(1.5f), NextSeed(), _mitesTier[tier]);
            for (int i = 0; i < swarm.Mites.Length; i++)
            {
                var state = new EnemyState { Family = EnemyFamily.Mite, Swarm = swarm, MiteIndex = i, Tier = tier };
                SpawnEnemy(state, swarm.Mites[i].Position);
            }
        }

        /// <summary>A Skitter runs over the generated ground: up and down slopes and steps, turning back at walls and drops.</summary>
        private void FollowGround(ref SkitterBehavior k, Vector3 before, bool blinked = false)
        {
            float r = _skitter.BodyRadius;
            var p = k.Position;
            // A blink may land on a ledge well above or below: search further for the ground.
            float up = blinked ? 6f : 1.2f, down = blinked ? 8f : 2.5f;
            if (ExpeditionTerrain.SurfaceNear(p.X, p.Z, before.Y - r, up, down, out float ground))
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
                        teleported = s.Wisp.Step(moveDt, WispOf(s));
                        if (ExpeditionTerrain.Active) KeepAboveGround(ref s.Wisp.Position, 0.8f);
                        position = s.Wisp.Position;
                        hittable = s.Wisp.IsHittable;
                        break;
                    case EnemyFamily.Skitter:
                        var before = s.Skitter.Position;
                        if (s.CoreRunner) SteerRunner(ref s.Skitter);
                        s.Skitter.Step(moveDt, SkitterOf(s));
                        if (s.Elite == EliteKind.Blinker && BlinkSkitter(s, moveDt)) teleported = true;
                        if (ExpeditionTerrain.Active) FollowGround(ref s.Skitter, before, teleported);
                        if (s.Raid) KeepInRoom(ref s.Skitter, before);
                        if (s.CoreRunner && RunnerBurst(s.Skitter.Position)) _expired.Add(e);
                        position = s.Skitter.Position;
                        break;
                    case EnemyFamily.Sentinel:
                        position = StepSentinel(s, dt);
                        break;
                    case EnemyFamily.Diver:
                        position = StepDiver(ecb, s, dt, out teleported);
                        break;
                    case EnemyFamily.Glimmer:
                        position = GlimmerPosition(s);
                        if (_time - s.Born >= GlimmerLife) _expired.Add(e);
                        break;
                    case EnemyFamily.Bulwark:
                    {
                        if (s.WorldBoss)
                        {
                            position = StepColossus(ecb, s, dt);
                            break;
                        }
                        if (s.Roaming)
                        {
                            position = StepRoamingBulwark(s, dt);
                            break;
                        }
                        float fraction = s.Vitals.Health / System.Math.Max(1f, s.Vitals.MaxHealth);
                        s.Boss.Step(dt, fraction, s.Vitals.MoveMultiplier, _bulwark);
                        // The Weaver calls no adds (they'd fall through its floor): its threads are the pressure.
                        int wave = s.Raid && _run?.Weaver != null ? 0
                            : s.Twin >= 0 && _run?.Twins != null
                            ? _run.Twins.TakeWaveDue(TwinsHealthFraction(), _bulwark.AddWaveAt)
                            : s.Boss.TakeWaveDue(fraction, _bulwark);
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
                if (s.Family != EnemyFamily.Glimmer && !s.WorldBoss && !s.Passive)
                {
                    if (ExpeditionWorld.Active) NearCandidates(position);
                    // Enemies look from a little above their centre (a Skitter's centre is close to the ground).
                    var eye = position + new Vector3(0f, 0.3f, 0f);
                    // Jam slows its attack clock; Pinned stops it.
                    var attack = s.Attack.Step(dt * s.Vitals.AttackTimeScale, eye, _candidates, s.AttackSettings, SightTest);
                    if (attack.Kind == AttackEventKind.TelegraphStarted) s.Telegraph = true;
                    else if (attack.Kind != AttackEventKind.None) s.Telegraph = false;
                    // A Sentinel's eye is open for its wind-up and a moment after the shot.
                    if (s.Family == EnemyFamily.Sentinel) s.Sentinel.Step(0f, s.Telegraph, attack.Kind == AttackEventKind.Fired, null, SentinelOf(s));
                    if (attack.Kind == AttackEventKind.Fired)
                    {
                        var a = s.AttackSettings;
                        float boltSpeed = a.BoltSpeed * (s.Raid && _run != null ? _run.BoltSpeedMultiplier : 1f);
                        FireBolt(ecb, s.Family, eye, attack.Aim, boltSpeed, a.BoltRadius, attack.Damage, a.Range);
                    }
                    else if (attack.Kind == AttackEventKind.Hit) HurtPlayer(ecb, attack.TargetId, s.Family, attack.Damage, position, 0);
                }

                s.History.Record(tick, position, hittable, teleported);
                EntityManager.SetComponentData(e, LocalTransform.FromPosition(ToFloat3(position)));
                EntityManager.SetComponentData(e, new ArenaEnemy
                {
                    Family = s.Family, Health = s.Vitals.Health, MaxHealth = s.Vitals.MaxHealth,
                    Hittable = hittable, Telegraph = s.Telegraph || (s.Family == EnemyFamily.Diver && s.Diver.Telegraph),
                    Yaw = EnemyYaw(s),
                    PlateOpen = s.WorldBoss ? s.Colossus.Exposed : s.Family == EnemyFamily.Sentinel ? s.Sentinel.EyeOpen : s.Boss.Open,
                    PlateWarn = s.WorldBoss ? s.Colossus.Warning : s.Family == EnemyFamily.Bulwark && s.Boss.OpeningSoon(BulwarkOf(s)),
                    World = s.WorldBoss, Boss = IsRaidBoss(s), Tier = (byte)s.Tier, Elite = s.Elite, Pitch = EnemyPitch(s),
                    Withered = s.Vitals.Debuffs.Has(EffectKind.Wither), Exposed = s.Vitals.Debuffs.Has(EffectKind.Expose),
                    Slowed = s.Vitals.Debuffs.Has(EffectKind.Slow), Tagged = s.Vitals.IsTagged,
                    Jammed = s.Vitals.Debuffs.Has(EffectKind.Jam), Brittle = s.Vitals.Debuffs.Has(EffectKind.Brittle),
                    Marked = s.Vitals.Debuffs.Has(EffectKind.Mark), Frozen = s.Vitals.IsFrozen, Shattered = s.Vitals.IsShattered,
                    LookSeed = BugLook.SeedFrom((uint)s.Vitals.Id), Id = s.Vitals.Id, WeakScale = s.Vitals.WeakPointScale,
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
            var deathSpot = EntityManager.GetComponentData<LocalTransform>(e).Position;
            _enemies.Remove(e);
            _lobby.Combos.Forget(s.Vitals.Id, false);
            _enemyById.Remove(s.Vitals.Id);
            ecb.DestroyEntity(e);
            if (ExpeditionWorld.Active)
            {
                GroupMemberDied(s);
                if (s.Family == EnemyFamily.Mite) s.Swarm.Alive[s.MiteIndex] = false;
                if (s.Family == EnemyFamily.Bulwark)
                {
                    if (s.WorldBoss) WorldBossKilled(s, ecb);
                    else if (s.Raid) RaidBossKilled(s);
                }
                if (s.Raid && s.Family == EnemyFamily.Wisp && _run != null && _run.Room == RaidRoomKind.WispStorm) _run.Storm.Kill();
                if (s.Raid && InLanterns && !_run.Solved) _run.Lanterns.Killed();
                ExpeditionKill(s, deathSpot);
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

        /// <summary>
        /// Hit targets as the shooter saw them. In the wrapping expedition world each is moved to its copy nearest the shooter.
        /// <paramref name="seenFraction"/> &gt; 0 is the client's interpolation fraction: it drew enemies at
        /// <paramref name="tick"/> − 1 + fraction (measured: this matches the drawn positions exactly).
        /// </summary>
        private void BuildTargets(int tick, int shooterId, float3 eye, float seenFraction = 0f)
        {
            int sampleTick = tick;
            float sampleFraction = 0f;
            if (seenFraction > 0f)
            {
                sampleTick = tick - 1;
                sampleFraction = math.min(seenFraction, 1f);
            }
            _targets.Clear();
            foreach (var kv in _players)
            {
                if (kv.Value.Vitals.Id == shooterId || kv.Value.Vitals.IsDown) continue;
                var p = NearVec(kv.Value.History.Sample(sampleTick, sampleFraction), eye);
                var center = p + new Vector3(0f, ArenaWeapon.PlayerHitHeight, 0f);
                _targets.Add(HitTarget.Body(kv.Value.Vitals.Id, center, ArenaWeapon.PlayerHitRadius, isPlayer: true));
            }
            // Focus: the shooter's own weak points are larger.
            float focus = 1f;
            if (_playerById.TryGetValue(shooterId, out var shooterEntity) && _players.TryGetValue(shooterEntity, out var shooterState))
            {
                focus += shooterState.Vitals.WeakPointBonus;
            }
            foreach (var kv in _enemies)
            {
                var s = kv.Value;
                var past = s.History.SampleState(sampleTick, sampleFraction);
                past.Position = NearVec(past.Position, eye);
                var target = EnemyHitShapes.For(s.Vitals.Id, s.Family, s.Tier, s.Elite, s.WorldBoss, s.Roaming, past.Position,
                    EnemyYaw(s), past.Hittable);
                // Mark grows weak points for everyone.
                if (target.HasWeakPoint) target.WeakPointRadius *= focus * s.Vitals.WeakPointScale;
                _targets.Add(target);
            }
            // Soul fragments, rewound like everything else. A downed player can't shoot their own.
            foreach (var kv in _fragments)
            {
                int ownerId = (kv.Key - FragmentIdBase) / FragmentIdsPerPlayer;
                if (ownerId == shooterId) continue;
                var p = NearVec(kv.Value.History.Sample(sampleTick, sampleFraction), eye);
                _targets.Add(HitTarget.Body(kv.Key, p, _combat.Revive.FragmentRadius));
            }
        }

        private static Vector3 ToVec(float3 v) => new Vector3(v.x, v.y, v.z);

        private static Vector3 NearVec(Vector3 p, float3 reference) => ToVec(ExpeditionWorld.Near(ToFloat3(p), reference));
        private static float3 ToFloat3(Vector3 v) => new float3(v.X, v.Y, v.Z);
    }
}
