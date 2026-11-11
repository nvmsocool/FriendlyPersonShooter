using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Progression;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using FriendlyPersonShooter.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The expedition (MVP step 4) on the server, on top of the arena's combat:
    /// - Enemies spawn at the layer's spawn points (and blooms) in zones near players, and stay until killed. A
    ///   cleared spawn point refills 2 minutes later with a new random enemy, once no player is within 100 m
    ///   (see <see cref="SpawnSchedule"/>). Bloom groups and boss adds leave after their time, once nobody is near.
    /// - Every player who hit an enemy gets its full essence drop as orbs to shoot, carried until banked by shooting the
    ///   gate's empty slots (bots bank by walking up; see <see cref="Banking"/>). A reset blocks banking for a minute.
    /// - Filling the gate's last slot opens it and starts the group's countdown down a layer (or, on the deepest
    ///   layer, to the end of the run).
    /// - Bleeding out drops half the carried essence as a cache and respawns you at the nearest shrine (the rest stays carried).
    ///   Anyone else who claims the cache sends it back to its owner (and earns kudos).
    /// </summary>
    public partial class ArenaServerSystem
    {
        private const float StatusInterval = 0.1f;
        private const float SpawnCheckInterval = 0.5f;

        private LayerMap _map;
        private SpawnSchedule _schedule;
        private BloomSchedule _blooms;
        private DropLedger _ledger;
        private Gate _gate;
        private CacheRegistry _caches;
        private Banking _banking;
        /// <summary>Which slots on the gate's tablet and the raid door are lit (banking is shooting an empty one).</summary>
        private readonly SlotBoard _gateSlots = new SlotBoard();
        private readonly Dictionary<EssenceColor, SlotBoard> _keySlots = new Dictionary<EssenceColor, SlotBoard>();
        private readonly Dictionary<int, EssenceWallet> _wallets = new Dictionary<int, EssenceWallet>();
        /// <summary>Banked essence is one pool for the whole party; carried essence stays personal.</summary>
        private readonly EssenceAmounts _partyBank = new EssenceAmounts();
        private readonly Dictionary<int, ExpeditionTracker> _trackers = new Dictionary<int, ExpeditionTracker>();
        /// <summary>Groups with a time limit (bloom groups, boss adds): when they may leave.</summary>
        private readonly Dictionary<SpawnGroupId, double> _groupEnds = new Dictionary<SpawnGroupId, double>();
        private readonly List<Vector2> _playerSpots = new List<Vector2>();
        private readonly HashSet<SpawnGroupId> _groupsLeft = new HashSet<SpawnGroupId>();
        private readonly Dictionary<int, Entity> _cacheGhosts = new Dictionary<int, Entity>();
        private readonly List<EnemyState> _spawned = new List<EnemyState>();
        private readonly List<int> _intScratch = new List<int>();
        private readonly List<SpawnGroupId> _groupScratch = new List<SpawnGroupId>();
        private double _nextStatus, _nextSpawnCheck;
        private int _essenceAwarded, _banked, _gateUnlocks, _completions, _cachesDropped, _cachesReturned, _eventsSpawned;

        private void CreateExpedition()
        {
            // Every session starts at the top (the static outlives play mode with domain reload off).
            ExpeditionWorld.Layer = 1;
            _travel = new LayerTravel(1);
            _map = ExpeditionWorld.Map;
            var layer = ExpeditionWorld.Settings;
            _blooms = new BloomSchedule(_map, layer.Blooms());
            _schedule = new SpawnSchedule(_map, _blooms, layer.Spawns());
            _ledger = new DropLedger(seed: (uint)System.Environment.TickCount);
            _gate = new Gate(_map.Gates[0], layer.Gate());
            _caches = new CacheRegistry();
            CreateSocial();
            CreateRaid();
            CreateWorldBoss();
            // Doors bank, shrines don't: shrines are where bleeding out and Reset position put you.
            _banking = new Banking(_map);
            foreach (var door in ExpeditionWorld.RaidDoors) _banking.AddSite(door, _raid.Settings.UseRadius);
            foreach (var g in _map.Gates) _banking.AddSite(g.Point.World, ExpeditionWorld.GateUseRadius);
            Debug.Log($"[Expedition] seed={NetTestSettings.WorldSeed} tiling={_map.Tiling.Width}x{_map.Tiling.Height} cells, {_map.Tiling.Count} zones, edge={_map.Tiling.Edge}m " +
                      $"start={_map.StartZone} gate={_map.Gates[0].Point.Zone} landmarks={_map.Landmarks.Count} shrines={_map.Shrines.Count}");
        }

        private EssenceWallet WalletOf(int id)
        {
            if (!_wallets.TryGetValue(id, out var w))
            {
                w = new EssenceWallet(id, _partyBank, EssenceWallet.CarryCap);
                _wallets[id] = w;
                // While the gate is open it is open for the whole party, including players who join later.
                if (_gate.IsOpen) _gate.GrantAccess(id);
            }
            return w;
        }

        private ExpeditionTracker TrackerOf(int id)
        {
            if (!_trackers.TryGetValue(id, out var t))
            {
                t = new ExpeditionTracker(_map, _gate, WalletOf(id), _time, _blooms, null, _banking);
                _trackers[id] = t;
            }
            return t;
        }

        private void ExpeditionPlayerGone(int id)
        {
            _ledger.PlayerGone(id);
            _raid?.Leave(id);
            _banking?.Forget(id);
            _wallets.Remove(id);
            _trackers.Remove(id);
        }

        /// <summary>Enemy attack candidates as seen from one enemy: each player moved to their copy nearest it.</summary>
        private void NearCandidates(Vector3 enemyPosition)
        {
            _candidates.Clear();
            var reference = ToFloat3(enemyPosition);
            foreach (var kv in _players)
            {
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                var near = ExpeditionWorld.Near(pos, reference);
                _candidates.Add(new AttackCandidate(kv.Value.Vitals.Id, ToVec(near + new float3(0f, ArenaWeapon.PlayerHitHeight, 0f)),
                    !kv.Value.Vitals.IsDown));
            }
        }

        private void ExpeditionKill(EnemyState s, float3 at)
        {
            // Everyone who hit it gets the kill's XP.
            _intScratch.Clear();
            _intScratch.AddRange(_ledger.HittersOf(s.Vitals.Id));
            // Bosses pay through their own rewards (a reward chest), not kill XP or drops.
            bool boss = s.WorldBoss || IsRaidBoss(s);
            int xp = boss ? 0 : XpRewards.Kill(s.Family) * (s.Elite != EliteKind.None ? 2 : 1);
            foreach (int hitter in _intScratch) AddXp(hitter, xp);
            // Raids pay XP and loot, never essence.
            if (s.Raid || boss)
            {
                _ledger.Killed(s.Vitals.Id, s.Family, s.Elite);
                return;
            }
            var awards = _ledger.Killed(s.Vitals.Id, s.Family, s.Elite);
            foreach (var a in awards) _essenceAwarded += a.Amount;
            // Players shoot their orbs to collect them; bots are paid at once.
            DropEssence(awards, at);
        }

        /// <summary>Drops half the carried essence where the player fell; returns where they respawn.</summary>
        private float3 BledOutInExpedition(int id, float3 body)
        {
            var cache = _caches.DropFrom(WalletOf(id), ExpeditionWorld.Ground(body), _combat.Revive.EssenceDropShare, _time);
            if (cache != null) _cachesDropped++;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            return ExpeditionWorld.ShrineSpawn(body, ref random);
        }

        private void StepExpedition(EntityCommandBuffer ecb)
        {
            StepSocial(ecb);
            StepGlimmers(ecb);
            if (_time >= _nextSpawnCheck)
            {
                _nextSpawnCheck = _time + SpawnCheckInterval;
                SpawnNearPlayers(ecb);
            }
            if (_time >= _nextStatus)
            {
                _nextStatus = _time + StatusInterval;
                StepRaid(ecb);
                StepWorldBoss();
                StepDebugFamilies();
                StepGate();
                StepLayers(ecb);
                UpdateExpeditionPlayers();
                _caches.ExpireOld(_time);
                SyncCaches(ecb);
            }
        }

        /// <summary>
        /// Spawns the groups that are due in any zone within one of a player (first fills, refills, blooms). Then
        /// sends away survivors of timed groups (bloom groups, boss adds) whose time is up, once no player is near.
        /// </summary>
        private void SpawnNearPlayers(EntityCommandBuffer ecb)
        {
            var zones = new HashSet<ZoneCoord>();
            _playerSpots.Clear();
            foreach (var kv in _players)
            {
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                _playerSpots.Add(new Vector2(pos.x, pos.z));
                var zone = _map.Tiling.ZoneAt(pos.x, pos.z);
                foreach (var z in _map.Tiling.Neighborhood(zone, 1)) zones.Add(z);
            }

            foreach (var zone in zones)
            {
                // On generated ground, wait until the zone is built (it streams in around players) so enemies land on it.
                if (ExpeditionTerrain.Active && !ExpeditionTerrain.World.IsReady(zone)) continue;
                foreach (var ev in _schedule.Due(zone, _time, _playerSpots))
                {
                    _eventsSpawned++;
                    SpawnEvent(ev);
                }
            }

            if (_groupEnds.Count == 0) return;
            float radius = _schedule.Settings.ClearRadius;
            _scratch.Clear();
            _groupsLeft.Clear();
            foreach (var kv in _enemies)
            {
                var s = kv.Value;
                if (!s.Spawn.IsSet || s.Raid || s.WorldBoss || !_groupEnds.TryGetValue(s.Spawn, out double end)) continue;
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                if (_time >= end && !_schedule.AnyPlayerWithin(new Vector2(pos.x, pos.z), _playerSpots, radius)) _scratch.Add(kv.Key);
                else _groupsLeft.Add(s.Spawn);
            }
            foreach (var e in _scratch)
            {
                var s = _enemies[e];
                _enemies.Remove(e);
                _enemyById.Remove(s.Vitals.Id);
                _lobby.Combos.Forget(s.Vitals.Id, false);
                _ledger.Forget(s.Vitals.Id);
                ecb.DestroyEntity(e);
            }
            // Forget timed groups that are over and have nobody left.
            _groupScratch.Clear();
            foreach (var kv in _groupEnds) if (_time >= kv.Value && !_groupsLeft.Contains(kv.Key)) _groupScratch.Add(kv.Key);
            foreach (var key in _groupScratch) _groupEnds.Remove(key);
        }

        /// <summary>A spawn group's enemy died: the schedule counts it, and a cleared spawn point starts its wait.</summary>
        private void GroupMemberDied(EnemyState s)
        {
            if (_schedule != null && s.Spawn.IsSet && s.Spawn.Source != SpawnSource.Adds) _schedule.Lost(s.Spawn, _time);
        }

        /// <summary>The ground at a spawn spot (0 on the flat floor), building its zone first if needed.</summary>
        private static float SpawnGround(float x, float z) => ExpeditionTerrain.Active ? ExpeditionTerrain.OnGround(new float3(x, 0f, z)).y : 0f;

        private void SpawnEvent(SpawnEvent ev)
        {
            var random = new Unity.Mathematics.Random((uint)((ev.Group.GetHashCode() + ev.Generation * 7919) * 2654435761u) | 1u);
            var point = ev.Point.World;
            int tier = ExpeditionWorld.Settings.Tier;
            _spawned.Clear();
            switch (ev.Family)
            {
                case EnemyFamily.Wisp:
                    for (int i = 0; i < ev.Count; i++)
                    {
                        var o = random.NextFloat2Direction() * random.NextFloat(0f, 5f);
                        SpawnWisp(new Vector3(point.X + o.x, SpawnGround(point.X + o.x, point.Y + o.y) + 2.2f, point.Y + o.y), tier);
                    }
                    break;
                case EnemyFamily.Skitter:
                    for (int i = 0; i < ev.Count; i++)
                    {
                        var o = random.NextFloat2Direction() * random.NextFloat(0f, 5f);
                        // One Skitter of an elite group is the elite.
                        SpawnSkitter(new Vector3(point.X + o.x, SpawnGround(point.X + o.x, point.Y + o.y) + _skitter.BodyRadius, point.Y + o.y),
                            tier, i == 0 ? ev.Elite : EliteKind.None);
                    }
                    break;
                case EnemyFamily.Sentinel:
                    for (int i = 0; i < ev.Count; i++)
                    {
                        var o = random.NextFloat2Direction() * (i == 0 ? 0f : random.NextFloat(6f, 10f));
                        SpawnSentinel(new Vector3(point.X + o.x, SpawnGround(point.X + o.x, point.Y + o.y), point.Y + o.y),
                            tier, i == 0 ? ev.Elite : EliteKind.None);
                    }
                    break;
                case EnemyFamily.Diver:
                    for (int i = 0; i < ev.Count; i++)
                    {
                        var o = random.NextFloat2Direction() * random.NextFloat(0f, 6f);
                        SpawnDiver(new Vector3(point.X + o.x, SpawnGround(point.X + o.x, point.Y + o.y), point.Y + o.y),
                            tier, i == 0 ? ev.Elite : EliteKind.None);
                    }
                    break;
                case EnemyFamily.Bulwark:
                    SpawnWorldBulwark(new Vector3(point.X, SpawnGround(point.X, point.Y), point.Y), tier, ev.Elite);
                    break;
                default:
                    SpawnSwarm(new Vector3(point.X, SpawnGround(point.X, point.Y) + 1.5f, point.Y), tier);
                    break;
            }
            foreach (var s in _spawned) s.Spawn = ev.Group;
            _schedule.Spawned(ev.Group, _spawned.Count, _time);
            if (!double.IsInfinity(ev.End)) _groupEnds[ev.Group] = ev.End;
            _spawned.Clear();
        }

        /// <summary>
        /// Keeps the lit slots matching what's in: the party bank of the gate's color (all lit while it's open) and
        /// the raid key. A slot that was shot stays lit; essence from elsewhere lights the first empty ones.
        /// </summary>
        private void SyncSlots()
        {
            if (_gate != null) _gateSlots.Sync(_gate.Cost, _gate.IsOpen ? _gate.Cost : _partyBank[_gate.Color]);
            if (_raid == null) return;
            foreach (var c in RaidDoor.KeyColors)
            {
                if (!_keySlots.TryGetValue(c, out var board)) _keySlots[c] = board = new SlotBoard();
                int have = _raid.Phase == RaidPhase.Collecting ? _raid.Pledged(c) : _raid.Phase == RaidPhase.Gathering ? _raid.Cost(c) : 0;
                board.Sync(_raid.Cost(c), have);
            }
        }

        private ulong KeySlotBits()
        {
            ulong bits = 0;
            foreach (var kv in _keySlots) bits |= kv.Value.Bits() << RaidKey.SlotOffset(kv.Key);
            return bits;
        }

        /// <summary>Banking, the gate and caches for every player, then their HUD status.</summary>
        private void UpdateExpeditionPlayers()
        {
            OpenFullGate();
            SyncSlots();
            ulong keySlots = KeySlotBits();
            foreach (var (owner, transform, status, entity) in SystemAPI
                         .Query<RefRO<GhostOwner>, RefRO<LocalTransform>, RefRW<ExpeditionStatus>>()
                         .WithAll<ArenaPlayer>().WithEntityAccess())
            {
                int id = owner.ValueRO.NetworkId;
                if (!_players.TryGetValue(entity, out var state)) continue;
                var wallet = WalletOf(id);
                var tracker = TrackerOf(id);
                var pos = ExpeditionWorld.Ground(transform.ValueRO.Position);

                bool inRaid = InsideRaid(id);
                bool lockedAtBank = false;
                if (!state.Vitals.IsDown && !inRaid && !RaidArena.InRaidSpace(transform.ValueRO.Position))
                {
                    lockedAtBank = !_banking.CanBank(id, _time) && _banking.IsAtBank(pos) && !wallet.CarriedAll().IsEmpty;
                }
                // Players do all of this by shooting (ArenaServerGallery; banking is shooting the gate's empty
                // slots); bots, who can't, by walking up.
                if (state.BotKit && !state.Vitals.IsDown && !inRaid)
                {
                    // At the raid door this runs after the key took its Red and Green (StepRaid goes first);
                    // at a gate it runs before paying, so freshly banked Yellow can open it straight away.
                    if (!RaidArena.InRaidSpace(transform.ValueRO.Position))
                    {
                        int banked = _banking.TryBank(wallet, pos, _time).Total;
                        if (banked > 0) Banked(id, banked);
                    }

                    // The gate opens itself once the bank fills it (OpenFullGate). A bot walking into a gate that's
                    // open to it starts the countdown, as a player's shot does.
                    if (_map.Distance(_gate.Site.Point.World, pos) <= ExpeditionWorld.GatePassRadius) BeginGateTravel(id);

                    _intScratch.Clear();
                    foreach (var c in _caches.All)
                    {
                        if (_map.Distance(c.Position, pos) <= ExpeditionWorld.CachePickupRadius) _intScratch.Add(c.Id);
                    }
                    foreach (int cacheId in _intScratch) PickUpCache(cacheId, id);
                }

                var objective = tracker.Update(_time, pos);
                var color = tracker.GoalColor;
                // While gathering, point at live prey: the nearest enemy that drops the goal color
                // (the tracker only knows spawn points, and half of them are quiet each wave).
                if (objective.Phase == ExpeditionPhase.Gather && TryNearestPrey(pos, color, out var prey))
                {
                    objective = new ExpeditionObjective(objective.Phase,
                        $"Gather {color}: {tracker.Collected}/{tracker.GoalAmount}. {FamilyName(color)} spotted {_map.Describe(prey)}",
                        true, prey, "prey");
                }
                if (TryRaidObjective(id, pos, objective.Phase, out var raidObjective)) objective = raidObjective;
                // Standing at a door that won't bank yet (after a reset): say why nothing happens.
                string lockout = lockedAtBank ? Banking.LockoutText(_banking.LockedFor(id, _time)) : null;
                if (lockout != null && !objective.Text.StartsWith(lockout))
                {
                    objective = new ExpeditionObjective(objective.Phase, $"{lockout}. {objective.Text}",
                        objective.HasTarget, objective.Target, objective.TargetLabel);
                }
                var next = new ExpeditionStatus
                {
                    Carried = wallet.Carried(color),
                    Banked = wallet.Banked(color),
                    CarriedAll = EssencePack.Pack(wallet.CarriedAll()),
                    BankedAll = EssencePack.Pack(wallet.BankedAll()),
                    Goal = tracker.GoalAmount,
                    GoalColor = color,
                    GateSlots0 = _gateSlots.Bits(0),
                    GateSlots1 = _gateSlots.Bits(64),
                    KeySlots = keySlots,
                    Layer = (byte)ExpeditionWorld.Layer,
                    LayerCountdown = _travel != null ? _travel.SecondsLeft(_time) : 0f,
                    LayerNext = (byte)(_travel != null && _travel.Pending ? _travel.Target : 0),
                    Phase = tracker.Phase,
                    HasTarget = objective.HasTarget,
                    Target = new float2(objective.Target.X, objective.Target.Y),
                    Elapsed = (float)tracker.Elapsed(_time),
                    Complete = tracker.IsComplete,
                    Kudos = _caches.KudosOf(id),
                    BloomNear = _blooms.NearestActive(pos, color, _time, 1) != null,
                    Objective = Truncate(objective.Text),
                    Location = Truncate(inRaid ? "in the raid arena, high above the raid door" : _map.Describe(pos)),
                };
                WriteRaidStatus(id, ref next);
                WriteWorldBossStatus(ref next);
                status.ValueRW = next;
            }
        }

        /// <summary>Essence went into the bank: XP for it.</summary>
        private void Banked(int id, int amount)
        {
            _banked += amount;
            _lastBanker = id;
            AddXp(id, amount * XpRewards.PerEssenceBanked);
        }

        /// <summary>Who banked last: the gate's opening is announced as theirs.</summary>
        private int _lastBanker;

        /// <summary>
        /// The gate's last slot lit (the party bank can pay): it opens and the whole group's countdown starts at once.
        /// It waits while a raid is on or another move is pending, and doesn't fire for a group that came back up
        /// (the gate is already open to them; they shoot it to go down).
        /// </summary>
        private void OpenFullGate()
        {
            if (_gate == null || _travel == null || _gate.IsOpen || _travel.Pending || !RaidAllowsTravel) return;
            if (_partyBank[_gate.Color] < _gate.Cost) return;
            int id = _lastBanker;
            if (WalletOf(id) == null || _gate.HasAccess(id))
            {
                id = 0;
                foreach (int member in _wallets.Keys)
                {
                    if (_gate.HasAccess(member) || !_playerById.ContainsKey(member)) continue;
                    id = member;
                    break;
                }
                if (id == 0) return;
            }
            if (UnlockGate(id)) BeginGateTravel(id);
        }

        /// <summary>
        /// Starts the whole group's countdown through the gate: down a layer, or on the deepest layer to the end of
        /// the run. Needs the gate open to <paramref name="id"/>.
        /// </summary>
        private void BeginGateTravel(int id)
        {
            if (_travel == null || _gate == null || !_gate.HasAccess(id)) return;
            if (_travel.CanGoDown)
            {
                TryGoDown(id);
                return;
            }
            if (RaidAllowsTravel && _travel.BeginFinish(_time, id))
                _announce.Add((AnnounceKind.LayerTravel, (int)LayerTravel.Countdown, id, _travel.Target));
        }

        /// <summary>The deepest gate's countdown ran out: everyone's run is complete, wherever they are.</summary>
        private void FinishExpedition()
        {
            foreach (var kv in _trackers)
            {
                _gate.GrantAccess(kv.Key);
                if (!kv.Value.PassedGate(_time)) continue;
                _completions++;
                _announce.Add((AnnounceKind.ExpeditionComplete, (int)kv.Value.Elapsed(_time), kv.Key, 0));
                Debug.Log($"[Expedition] player {kv.Key} completed in {kv.Value.TimerText(_time)}");
            }
        }

        /// <summary>Unlocks the gate with this player's party bank, if it isn't open to them and the bank can pay.</summary>
        private bool UnlockGate(int id)
        {
            var wallet = WalletOf(id);
            if (wallet == null || _gate.HasAccess(id) || _gate.Shortfall(wallet) != 0) return false;
            if (_gate.Unlock(wallet, _time) != GateUnlockOutcome.Unlocked) return false;
            _gateUnlocks++;
            foreach (int member in _wallets.Keys) _gate.GrantAccess(member);
            _announce.Add((AnnounceKind.GateUnlocked, _gate.Cost, id, (int)_gate.Color));
            return true;
        }

        /// <summary>Claims a dropped cache: your own back into your pocket, someone else's into their bank (kudos to you).</summary>
        private void PickUpCache(int cacheId, int id)
        {
            var pickup = _caches.PickUp(cacheId, id, _time);
            if (!pickup.Delivers) return;
            if (_wallets.TryGetValue(pickup.OwnerId, out var ownerWallet)) pickup.DeliverTo(ownerWallet);
            if (pickup.Outcome == CachePickupOutcome.Returned)
            {
                _cachesReturned++;
                SocialCacheReturn(id, pickup.OwnerId);
                _announce.Add((AnnounceKind.CacheReturned, pickup.Contents.Total, pickup.OwnerId, id));
            }
        }

        /// <summary>
        /// The gate's opening ran out: it shuts for everyone and the next opening costs more. Players who
        /// went through start a new run toward it.
        /// </summary>
        private void StepGate()
        {
            if (!_gate.Step(_time)) return;
            foreach (var t in _trackers.Values) t.GateClosed(_time);
            _announce.Add((AnnounceKind.GateClosed, _gate.Cost, 0, (int)_gate.Color));
            Debug.Log($"[Expedition] gate shut; next opening costs {_gate.Cost} {_gate.Color}");
        }

        private const float PreySearchRadius = 400f;

        private static string FamilyName(EssenceColor color) =>
            EssenceColors.TryGetFamily(color, out var f) ? EssenceColors.Plural(f) : "Prey";

        /// <summary>The nearest live enemy whose drop is <paramref name="color"/>, within reach.</summary>
        private bool TryNearestPrey(Vector2 from, EssenceColor color, out Vector2 prey)
        {
            prey = default;
            if (!EssenceColors.TryGetFamily(color, out var family)) return false;
            float best = PreySearchRadius;
            bool found = false;
            foreach (var kv in _enemies)
            {
                if (kv.Value.Family != family) continue;
                var p = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                var g = ExpeditionWorld.Ground(p);
                float d = _map.Distance(g, from);
                if (d >= best) continue;
                best = d;
                prey = g;
                found = true;
            }
            return found;
        }

        private static FixedString128Bytes Truncate(string text)
        {
            var s = new FixedString128Bytes();
            if (string.IsNullOrEmpty(text)) return s;
            if (text.Length > 120) text = text.Substring(0, 120);
            s.CopyFromTruncated(text);
            return s;
        }

        /// <summary>Mirrors dropped caches as ghosts.</summary>
        private void SyncCaches(EntityCommandBuffer ecb)
        {
            var prefab = SystemAPI.GetSingleton<ArenaCachePrefab>().Value;
            _intScratch.Clear();
            foreach (var c in _caches.All)
            {
                _intScratch.Add(c.Id);
                if (_cacheGhosts.ContainsKey(c.Id)) continue;
                var e = EntityManager.Instantiate(prefab);
                EntityManager.SetComponentData(e, LocalTransform.FromPosition(ExpeditionWorld.ToFloat3(c.Position, ExpeditionTerrain.GroundY(c.Position.X, c.Position.Y) + 0.3f)));
                EntityManager.SetComponentData(e, new ArenaCache { OwnerNetworkId = c.OwnerId, Amount = c.Contents.Total });
                _cacheGhosts[c.Id] = e;
            }
            var live = new HashSet<int>(_intScratch);
            _intScratch.Clear();
            foreach (var kv in _cacheGhosts) if (!live.Contains(kv.Key)) _intScratch.Add(kv.Key);
            foreach (int id in _intScratch)
            {
                ecb.DestroyEntity(_cacheGhosts[id]);
                _cacheGhosts.Remove(id);
            }
        }

        private void LogExpedition()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _trackers)
            {
                var w = WalletOf(kv.Key);
                sb.Append($"{kv.Key}:{kv.Value.Phase}/{w.Carried(kv.Value.GoalColor)}c{w.Banked(kv.Value.GoalColor)}b");
                if (_playerById.TryGetValue(kv.Key, out var pe))
                {
                    var pos = EntityManager.GetComponentData<LocalTransform>(pe).Position;
                    float nearest = float.MaxValue;
                    foreach (var en in _enemies.Keys)
                    {
                        var ep = ExpeditionWorld.Near(EntityManager.GetComponentData<LocalTransform>(en).Position, pos);
                        nearest = math.min(nearest, math.distance(ep, pos));
                    }
                    var obj = kv.Value.Objective;
                    float toGoal = obj.HasTarget ? _map.Distance(obj.Target, ExpeditionWorld.Ground(pos)) : -1f;
                    sb.Append($"@({pos.x:0},{pos.z:0}) enemy {nearest:0}m goal {toGoal:0}m");
                }
                sb.Append(' ');
            }
            Debug.Log($"[Expedition] t={_time:0} events={_eventsSpawned} essence={_essenceAwarded} banked={_banked} unlocks={_gateUnlocks} " +
                      $"completions={_completions} cachesDropped={_cachesDropped} cachesReturned={_cachesReturned} players=[{sb}]");
            Debug.Log($"[Social] t={_time:0} {SocialLog()}");
            Debug.Log($"[Raid] t={_time:0} {RaidLog()}");
            Debug.Log($"[Bolts] t={_time:0} {BoltLog()}");
            Debug.Log($"[WorldBoss] t={_time:0} {WorldBossLog()} gate={(_gate.IsOpen ? "open" : "shut")}/{_gate.Cost} {_gate.Color} {LayerLog()}");
        }
    }
}
