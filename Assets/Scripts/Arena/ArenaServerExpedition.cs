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
    /// <summary>Identifies one spawn event: a spawn point's wave (blooms count separately).</summary>
    public readonly struct SpawnKey : System.IEquatable<SpawnKey>
    {
        public readonly int SpawnPointId;
        public readonly long Wave;
        public readonly bool Bloom;

        public SpawnKey(int spawnPointId, long wave, bool bloom)
        {
            SpawnPointId = spawnPointId;
            Wave = wave;
            Bloom = bloom;
        }

        public bool IsSet => SpawnPointId != 0 || Wave != 0 || Bloom;
        public bool Equals(SpawnKey o) => SpawnPointId == o.SpawnPointId && Wave == o.Wave && Bloom == o.Bloom;
        public override bool Equals(object obj) => obj is SpawnKey o && Equals(o);
        public override int GetHashCode() => (SpawnPointId * 397) ^ Wave.GetHashCode() ^ (Bloom ? 0x5555 : 0);
    }

    /// <summary>
    /// The expedition (MVP step 4) on the server, on top of the arena's combat:
    /// - Enemies spawn from the layer's seeded spawn schedule (and blooms) in zones near players.
    ///   Each spawn event spawns once; enemies it made leave when it ends.
    /// - Every player who hit an enemy gets its full essence drop, carried until they stand at the raid door
    ///   or a gate, which bank it (not shrines; see <see cref="Banking"/>). A reset blocks banking for a minute.
    /// - Standing at the gate with enough banked essence pays for access; walking through ends the expedition.
    /// - Bleeding out drops half the carried essence as a cache and respawns you at the nearest shrine (the rest stays carried).
    ///   Anyone else who walks over the cache sends it back to its owner (and earns kudos).
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
        private readonly Dictionary<int, EssenceWallet> _wallets = new Dictionary<int, EssenceWallet>();
        /// <summary>Banked essence is one pool for the whole party; carried essence stays personal.</summary>
        private readonly EssenceAmounts _partyBank = new EssenceAmounts();
        private readonly Dictionary<int, ExpeditionTracker> _trackers = new Dictionary<int, ExpeditionTracker>();
        private readonly HashSet<SpawnKey> _spawnedEvents = new HashSet<SpawnKey>();
        private readonly Dictionary<SpawnKey, double> _eventEnds = new Dictionary<SpawnKey, double>();
        private readonly Dictionary<int, Entity> _cacheGhosts = new Dictionary<int, Entity>();
        private readonly List<EnemyState> _spawned = new List<EnemyState>();
        private readonly List<int> _intScratch = new List<int>();
        private readonly List<SpawnKey> _keyScratch = new List<SpawnKey>();
        private double _nextStatus, _nextSpawnCheck;
        private int _essenceAwarded, _banked, _gateUnlocks, _completions, _cachesDropped, _cachesReturned, _eventsSpawned;

        private void CreateExpedition()
        {
            _map = ExpeditionWorld.Map;
            _blooms = new BloomSchedule(_map, new BloomSettings());
            _schedule = new SpawnSchedule(_map, _blooms, new SpawnSettings());
            _ledger = new DropLedger(seed: (uint)System.Environment.TickCount);
            _gate = new Gate(_map.Gates[0], NetTestSettings.WorldSeed);
            _caches = new CacheRegistry();
            CreateSocial();
            CreateRaid();
            CreateWorldBoss();
            // Doors bank, shrines don't: shrines are where bleeding out and Reset position put you.
            _banking = new Banking(_map);
            _banking.AddSite(RaidDoorPoint, _raid.Settings.UseRadius);
            foreach (var g in _map.Gates) _banking.AddSite(g.Point.World, ExpeditionWorld.GateUseRadius);
            Debug.Log($"[Expedition] seed={NetTestSettings.WorldSeed} grid={_map.Grid.Width}x{_map.Grid.Height} zone={_map.Grid.ZoneSize}m " +
                      $"start={_map.StartZone} gate={_map.Gates[0].Point.Zone} landmarks={_map.Landmarks.Count} shrines={_map.Shrines.Count}");
        }

        private EssenceWallet WalletOf(int id)
        {
            if (!_wallets.TryGetValue(id, out var w))
            {
                w = new EssenceWallet(id, _partyBank);
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

        private void ExpeditionKill(EnemyState s)
        {
            // Everyone who hit it gets the kill's XP.
            _intScratch.Clear();
            _intScratch.AddRange(_ledger.HittersOf(s.Vitals.Id));
            foreach (int hitter in _intScratch) AddXp(hitter, XpRewards.Kill(s.Family));
            var awards = _ledger.Killed(s.Vitals.Id, s.Family);
            foreach (var a in awards) _essenceAwarded += a.Amount;
            DropLedger.Pay(awards, WalletOf);
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
                StepGate();
                UpdateExpeditionPlayers();
                _caches.ExpireOld(_time);
                SyncCaches(ecb);
            }
        }

        /// <summary>Spawns events that are running in any zone within one of a player; removes enemies whose event ended.</summary>
        private void SpawnNearPlayers(EntityCommandBuffer ecb)
        {
            var zones = new HashSet<ZoneCoord>();
            foreach (var kv in _players)
            {
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                var zone = _map.Grid.ZoneAt(pos.x, pos.z);
                foreach (var z in _map.Grid.Neighborhood(zone, 1)) zones.Add(z);
            }

            foreach (var zone in zones)
            {
                // On generated ground, wait until the zone is built (it streams in around players) so enemies land on it.
                if (ExpeditionTerrain.Active && !ExpeditionTerrain.World.IsReady(zone)) continue;
                foreach (var ev in _schedule.ActiveAt(zone, _time))
                {
                    var key = new SpawnKey(ev.SpawnPointId, ev.Wave, ev.IsBloom);
                    if (!_spawnedEvents.Add(key)) continue;
                    _eventEnds[key] = ev.End;
                    _eventsSpawned++;
                    SpawnEvent(ev, key);
                }
            }

            // Events that ended take their surviving enemies with them.
            _keyScratch.Clear();
            foreach (var kv in _eventEnds) if (_time >= kv.Value) _keyScratch.Add(kv.Key);
            if (_keyScratch.Count == 0) return;
            var ended = new HashSet<SpawnKey>(_keyScratch);
            foreach (var key in _keyScratch)
            {
                _eventEnds.Remove(key);
                _spawnedEvents.Remove(key);
            }
            _scratch.Clear();
            foreach (var kv in _enemies)
            {
                if (kv.Value.Family != EnemyFamily.Glimmer && !kv.Value.Raid && !kv.Value.WorldBoss && ended.Contains(kv.Value.Spawn)) _scratch.Add(kv.Key);
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
        }

        /// <summary>The ground at a spawn spot (0 on the flat floor), building its zone first if needed.</summary>
        private static float SpawnGround(float x, float z) => ExpeditionTerrain.Active ? ExpeditionTerrain.OnGround(new float3(x, 0f, z)).y : 0f;

        private void SpawnEvent(SpawnEvent ev, SpawnKey key)
        {
            var random = new Unity.Mathematics.Random((uint)(key.GetHashCode() * 2654435761u) | 1u);
            var point = ev.Point.World;
            _spawned.Clear();
            switch (ev.Family)
            {
                case EnemyFamily.Wisp:
                    for (int i = 0; i < ev.Count; i++)
                    {
                        var o = random.NextFloat2Direction() * random.NextFloat(0f, 5f);
                        SpawnWisp(new Vector3(point.X + o.x, SpawnGround(point.X + o.x, point.Y + o.y) + 2.2f, point.Y + o.y));
                    }
                    break;
                case EnemyFamily.Skitter:
                    for (int i = 0; i < ev.Count; i++)
                    {
                        var o = random.NextFloat2Direction() * random.NextFloat(0f, 5f);
                        SpawnSkitter(new Vector3(point.X + o.x, SpawnGround(point.X + o.x, point.Y + o.y) + _skitter.BodyRadius, point.Y + o.y));
                    }
                    break;
                default:
                    SpawnSwarm(new Vector3(point.X, SpawnGround(point.X, point.Y) + 1.5f, point.Y));
                    break;
            }
            foreach (var s in _spawned) s.Spawn = key;
            _spawned.Clear();
        }

        /// <summary>Banking, the gate and caches for every player, then their HUD status.</summary>
        private void UpdateExpeditionPlayers()
        {
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
                if (!state.Vitals.IsDown && !inRaid)
                {
                    // At the raid door this runs after the key took its Red and Green (StepRaid goes first);
                    // at a gate it runs before paying, so freshly banked Yellow can open it straight away.
                    if (!RaidArena.Contains(transform.ValueRO.Position))
                    {
                        int banked = _banking.TryBank(wallet, pos, _time).Total;
                        if (banked > 0)
                        {
                            _banked += banked;
                            AddXp(id, banked * XpRewards.PerEssenceBanked);
                        }
                        lockedAtBank = !_banking.CanBank(id, _time) && _banking.IsAtBank(pos) && !wallet.CarriedAll().IsEmpty;
                    }

                    float toGate = _map.Distance(_gate.Site.Point.World, pos);
                    if (toGate <= ExpeditionWorld.GateUseRadius && !_gate.HasAccess(id) && _gate.Shortfall(wallet) == 0 &&
                        _gate.Unlock(wallet, _time) == GateUnlockOutcome.Unlocked)
                    {
                        _gateUnlocks++;
                        foreach (int member in _wallets.Keys) _gate.GrantAccess(member);
                        _announce.Add((AnnounceKind.GateUnlocked, _gate.Cost, id, 0));
                    }
                    if (toGate <= ExpeditionWorld.GatePassRadius && tracker.PassedGate(_time))
                    {
                        _completions++;
                        _announce.Add((AnnounceKind.ExpeditionComplete, (int)tracker.Elapsed(_time), id, 0));
                        Debug.Log($"[Expedition] player {id} completed in {tracker.TimerText(_time)}");
                    }

                    _intScratch.Clear();
                    foreach (var c in _caches.All)
                    {
                        if (_map.Distance(c.Position, pos) <= ExpeditionWorld.CachePickupRadius) _intScratch.Add(c.Id);
                    }
                    foreach (int cacheId in _intScratch)
                    {
                        var pickup = _caches.PickUp(cacheId, id, _time);
                        if (!pickup.Delivers) continue;
                        if (_wallets.TryGetValue(pickup.OwnerId, out var ownerWallet)) pickup.DeliverTo(ownerWallet);
                        if (pickup.Outcome == CachePickupOutcome.Returned)
                        {
                            _cachesReturned++;
                            SocialCacheReturn(id, pickup.OwnerId);
                            _announce.Add((AnnounceKind.CacheReturned, pickup.Contents.Total, pickup.OwnerId, id));
                        }
                    }
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
                    CarriedRed = wallet.Carried(EssenceColor.Red),
                    BankedRed = wallet.Banked(EssenceColor.Red),
                    CarriedGreen = wallet.Carried(EssenceColor.Green),
                    BankedGreen = wallet.Banked(EssenceColor.Green),
                    Goal = tracker.GoalAmount,
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

        /// <summary>
        /// The gate's opening ran out: it shuts for everyone and the next opening costs more. Players who
        /// went through start a new run toward it.
        /// </summary>
        private void StepGate()
        {
            if (!_gate.Step(_time)) return;
            foreach (var t in _trackers.Values) t.GateClosed(_time);
            _announce.Add((AnnounceKind.GateClosed, _gate.Cost, 0, 0));
            Debug.Log($"[Expedition] gate shut; next opening costs {_gate.Cost} {_gate.Color}");
        }

        private const float PreySearchRadius = 400f;

        private static string FamilyName(EssenceColor color) =>
            EssenceColors.TryGetFamily(color, out var f) ? (f == EnemyFamily.Mite ? "Mites" : f + "s") : "Prey";

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
            Debug.Log($"[WorldBoss] t={_time:0} {WorldBossLog()} gate={(_gate.IsOpen ? "open" : "shut")}/{_gate.Cost}");
        }
    }
}
