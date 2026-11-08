using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The rooms of a raid run on the server (Docs/RaidDevPlan.md): which room the group is in, feeding
    /// its rules (<see cref="RaidRun"/>), adds, room pay, and the portal to the next stage.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>Debug: make the next run start with this room (Boss = random), and use this modifier.</summary>
        public static RaidRoomKind DebugRaidRoom = RaidRoomKind.Boss;
        public static RaidModifier DebugRaidModifier = RaidModifier.None;
        /// <summary>Debug: the next run's boss (null = random).</summary>
        public static RaidBossKind? DebugRaidBoss;
        /// <summary>Debug: solve the current room on the next raid step.</summary>
        public static bool DebugSolveRoom;

        private RaidRun _run;
        private double _roomStepAt;
        private double _nextRoomAdds;
        private int _roomAddWave;
        private int _roomsSolved, _setbacks, _crystalHits, _sweeperHits, _falls, _runeHits, _coreBursts;
        private readonly List<(int Id, Vector3 Feet, bool Up)> _roomPlayers = new List<(int, Vector3, bool)>();
        private readonly List<int> _swept = new List<int>();
        private readonly List<int> _fell = new List<int>();
        private readonly List<Vector3> _standing = new List<Vector3>();

        /// <summary>Moves everyone inside to the current stage and sets it up (the boss arrives on the last).</summary>
        private void EnterStage()
        {
            var inside = _raid.Participants;
            int n = inside.Count;
            _roomStepAt = _time;
            _nextRoomAdds = _time + (_run.Room == RaidRoomKind.Lanterns ? 4.0 : 10.0);
            _roomAddWave = 0;
            if (_run.AtBoss) SpawnRaidBoss(n);
            // This run's shape of the room: its colliders before anyone lands in it.
            else RaidArena.UseRoom(_run.Room, _run.RoomSeed);
            for (int i = 0; i < n; i++) TeleportPlayer(inside[i], RaidArena.RoomSpawn(_run.Room, i, n));
            _announce.Add((AnnounceKind.RaidStage, (int)_run.Room, _run.Stage, (int)_run.Modifier));
            Debug.Log($"[Raid] stage {_run.Stage + 1}/{_run.StageCount}: {_run.StageName} seed {_run.RoomSeed} ({_run.Modifier})");
        }

        /// <summary>The current room, at the raid's 10 Hz step. <see cref="_roomPlayers"/> holds who is inside.</summary>
        private void StepRoom(EntityCommandBuffer ecb)
        {
            double t0 = _run.StageTime(_roomStepAt), t1 = _run.StageTime(_time);
            float dt = (float)(_time - _roomStepAt);
            _roomStepAt = _time;
            _run.Tick(_time);
            if (DebugSolveRoom)
            {
                DebugSolveRoom = false;
                RoomSolved(ecb);
            }

            if (_run.Solved)
            {
                bool someone = false;
                var portal = _run.Layout.Portal;
                foreach (var (_, feet, up) in _roomPlayers)
                {
                    float dx = feet.X - portal.X, dz = feet.Z - portal.Z;
                    if (up && dx * dx + dz * dz <= RaidRoomLayout.PortalRadius * RaidRoomLayout.PortalRadius && System.Math.Abs(feet.Y - portal.Y) < 3f) someone = true;
                }
                if (_run.StepPortal(_time, someone)) MoveOn();
                return;
            }

            int upCount = 0;
            foreach (var p in _roomPlayers) if (p.Up) upCount++;
            switch (_run.Room)
            {
                case RaidRoomKind.ColorLocks:
                    if (_run.Locks.Step(_time)) Setback(1, 0);
                    if (_run.Locks.Solved) RoomSolved(ecb);
                    else RoomAdds(30.0, 1 + _run.Players / 4, false);
                    break;
                case RaidRoomKind.HoldPlates:
                    _standing.Clear();
                    foreach (var p in _roomPlayers) if (p.Up) _standing.Add(p.Feet);
                    if (_run.Plates.Step(dt, HoldPlates.OccupancyOf(_run.Layout, _run.Plates.Plates, _standing))) RoomSolved(ecb);
                    else RoomAdds(14.0, 1 + _run.Players / 3, true);
                    break;
                case RaidRoomKind.Gauntlet:
                    _swept.Clear();
                    _fell.Clear();
                    bool home = _run.Gauntlet.Step(t0, t1, _roomPlayers, _swept, _fell);
                    foreach (int id in _swept)
                    {
                        _sweeperHits++;
                        GauntletHurt(ecb, id, GauntletRun.SweeperDamage);
                    }
                    for (int i = 0; i < _fell.Count; i++)
                    {
                        _falls++;
                        TeleportPlayer(_fell[i], RaidArena.RoomSpawn(RaidRoomKind.Gauntlet, _fell[i], 8));
                        GauntletHurt(ecb, _fell[i], GauntletRun.FallDamage);
                    }
                    if (home) RoomSolved(ecb);
                    break;
                case RaidRoomKind.WispStorm:
                    if (_run.Storm.Step(_time)) Setback(2, 0);
                    if (_run.Storm.Solved) RoomSolved(ecb);
                    else KeepStormUp();
                    break;
                case RaidRoomKind.EchoRunes:
                    if (_run.Runes.Step(_time)) Setback(4, 0);
                    if (_run.Runes.Solved) RoomSolved(ecb);
                    else RoomAdds(26.0, 1 + _run.Players / 4, false);
                    break;
                case RaidRoomKind.Lanterns:
                    if (_run.Lanterns.Solved) RoomSolved(ecb);
                    else KeepShadesUp();
                    break;
                case RaidRoomKind.SentinelEye:
                    _run.Eye.Step(_time);
                    if (_run.Eye.Solved)
                    {
                        RoomSolved(ecb);
                        break;
                    }
                    if (_run.Eye.TakeBoltDue(_time)) EyeBolt(ecb);
                    RoomAdds(30.0, 1 + _run.Players / 4, false);
                    break;
                case RaidRoomKind.HoldCore:
                    switch (_run.Core.Step(_time))
                    {
                        case CoreEvent.Wave:
                            SpawnRunners(_run.Core.WaveSize);
                            break;
                        case CoreEvent.Broke:
                            ClearRaidEnemies(ecb);
                            Setback(5, 0);
                            break;
                        case CoreEvent.Solved:
                            RoomSolved(ecb);
                            break;
                    }
                    break;
            }
        }

        /// <summary>Hold the Core: a wave of runners, spread over the three gates.</summary>
        private void SpawnRunners(int count)
        {
            var targets = _run.Layout.Targets;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            _spawned.Clear();
            int first = random.NextInt(HoldCore.Gates);
            for (int i = 0; i < count; i++)
            {
                var gate = targets[1 + (first + i) % HoldCore.Gates];
                var o = random.NextFloat2Direction() * random.NextFloat(0f, 1.5f);
                var at = RaidArena.FromRoom(RaidRoomKind.HoldCore, new Vector3(gate.X + o.x, gate.Y, gate.Z + o.y));
                SpawnSkitter(new Vector3(at.x, at.y + _skitter.BodyRadius, at.z));
            }
            foreach (var s in _spawned)
            {
                s.Raid = true;
                s.CoreRunner = true;
            }
            _spawned.Clear();
        }

        /// <summary>A runner heads for the core, weaving a little so it isn't a straight-line shot.</summary>
        private void SteerRunner(ref SkitterBehavior k)
        {
            k.TimeToTurn = 99f;
            // Skitters are leashed to where they spawned; a runner's leash goes with it.
            k.Home = k.Position;
            if (_run == null || _run.Room != RaidRoomKind.HoldCore) return;
            var c = RaidArena.RoomCenter(RaidRoomKind.HoldCore);
            float dx = c.x - k.Position.X, dz = c.z - k.Position.Z;
            float len = math.sqrt(dx * dx + dz * dz);
            if (len < 0.01f) return;
            float weave = 0.45f * math.sin((float)_time * 2.3f + _run.Core.Waves * 1.7f);
            float cs = math.cos(weave), sn = math.sin(weave);
            float hx = dx / len, hz = dz / len;
            k.Heading = new Vector3(hx * cs - hz * sn, 0f, hx * sn + hz * cs);
        }

        /// <summary>True when a runner reached the core: it bursts (the caller removes it) and cracks the core.</summary>
        private bool RunnerBurst(Vector3 p)
        {
            if (_run == null || _run.Room != RaidRoomKind.HoldCore || _run.Core == null || _run.Solved) return false;
            var c = RaidArena.RoomCenter(RaidRoomKind.HoldCore);
            float dx = p.X - c.x, dz = p.Z - c.z;
            if (dx * dx + dz * dz > HoldCore.BurstRadius * HoldCore.BurstRadius) return false;
            _coreBursts++;
            _run.Core.Burst();
            return true;
        }

        /// <summary>A sweeper or a fall: stings, but never takes anyone below <see cref="GauntletRun.HealthFloor"/>.</summary>
        private void GauntletHurt(EntityCommandBuffer ecb, int id, float damage)
        {
            if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var st)) return;
            damage = System.Math.Min(damage * _run.DamageTakenMultiplier, st.Vitals.Health - GauntletRun.HealthFloor);
            if (damage <= 0f) return;
            HurtPlayer(ecb, id, EnemyFamily.Bulwark, damage / _run.DamageTakenMultiplier, PlayerFeet(id), 0);
        }

        private Vector3 PlayerFeet(int id) =>
            _playerById.TryGetValue(id, out var e) ? ToVec(EntityManager.GetComponentData<LocalTransform>(e).Position) : Vector3.Zero;

        private void Setback(int why, int player)
        {
            _setbacks++;
            _announce.Add((AnnounceKind.RaidSetback, (int)_run.Room, player, why));
        }

        /// <summary>Opens the portal, pays everyone inside, and clears the room's enemies (no drops).</summary>
        private void RoomSolved(EntityCommandBuffer ecb)
        {
            if (!_run.MarkSolved()) return;
            _roomsSolved++;
            int xp = _run.RoomXp, yellow = _run.RoomYellow;
            foreach (int id in _raid.Participants)
            {
                AddXp(id, xp);
                WalletOf(id).PickUp(EssenceColor.Yellow, yellow);
            }
            ClearRaidEnemies(ecb);
            _announce.Add((AnnounceKind.RaidRoomSolved, (int)_run.Room, xp, yellow));
            Debug.Log($"[Raid] solved {RaidRoomLayout.NameOf(_run.Room)} after {_run.StageTime(_time):0}s");
        }

        /// <summary>The portal's countdown ended: everyone moves on fresh (the downed get up, everyone is healed).</summary>
        private void MoveOn()
        {
            foreach (int id in _raid.Participants)
            {
                if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var st)) continue;
                if (st.Revive != null)
                {
                    st.Vitals.Revive(1f);
                    st.Revive = null;
                }
                else st.Vitals.SetHealth(st.Vitals.MaxHealth);
            }
            _run.Advance(_time);
            EnterStage();
        }

        /// <summary>Removes every raid enemy (no drops): when a room is solved or the run ends.</summary>
        private void ClearRaidEnemies(EntityCommandBuffer ecb)
        {
            _scratch.Clear();
            foreach (var kv in _enemies) if (kv.Value.Raid) _scratch.Add(kv.Key);
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

        private int RaidEnemyCount(EnemyFamily? family = null)
        {
            int n = 0;
            foreach (var s in _enemies.Values) if (s.Raid && (family == null || s.Family == family)) n++;
            return n;
        }

        /// <summary>Skitters (and every other wave a Mite swarm) climb on every <paramref name="every"/> seconds.</summary>
        private void RoomAdds(double every, int skitters, bool swarms)
        {
            if (_time < _nextRoomAdds) return;
            _nextRoomAdds = _time + every;
            _roomAddWave++;
            if (RaidEnemyCount() >= 6 + 2 * _run.Players) return;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            var spots = _run.Layout.AddSpots;
            _spawned.Clear();
            int count = skitters * _run.AddsMultiplier;
            for (int i = 0; i < count; i++)
            {
                var at = RaidArena.FromRoom(_run.Room, spots[random.NextInt(spots.Count)]);
                SpawnSkitter(new Vector3(at.x, at.y + _skitter.BodyRadius, at.z));
            }
            if (swarms && _roomAddWave % 2 == 0)
            {
                for (int k = 0; k < _run.AddsMultiplier; k++)
                {
                    var at = RaidArena.FromRoom(_run.Room, spots[random.NextInt(spots.Count)]);
                    SpawnSwarm(new Vector3(at.x, at.y + 1.5f, at.z));
                }
            }
            foreach (var s in _spawned) s.Raid = true;
            _spawned.Clear();
        }

        /// <summary>Wisp Storm: keeps the room's Wisps topped up, spread over the platform.</summary>
        private void KeepStormUp()
        {
            int missing = _run.Storm.MaxAlive - RaidEnemyCount(EnemyFamily.Wisp);
            if (missing <= 0) return;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            _spawned.Clear();
            for (int i = 0; i < missing; i++)
            {
                var o = random.NextFloat2Direction() * random.NextFloat(4f, 17f);
                var at = RaidArena.FromRoom(RaidRoomKind.WispStorm, new Vector3(o.x, random.NextFloat(1.6f, 4.5f), o.y));
                SpawnWisp(new Vector3(at.x, at.y, at.z));
            }
            foreach (var s in _spawned)
            {
                s.Raid = true;
                s.Passive = true;
            }
            _spawned.Clear();
        }

        /// <summary>
        /// Room targets on a shot: the distance along it to the nearest crystal, rune, open eye or thread it
        /// passes through (and which), or -1 for none. Called before the shot looks for enemies, so a target
        /// in front stops it. The Sentinel's shell stops shots too (shortening the distance, no target).
        /// </summary>
        private int CrystalOnShot(int shooterId, float3 eye, float3 dir, float range, out float distance)
        {
            distance = range;
            if (_run == null || _run.Solved || !InsideRaid(shooterId)) return -1;
            var room = _run.Room;
            if (_run.AtBoss) return _run.Weaver != null ? TetherOnShot(eye, dir, ref distance) : -1;
            if (room == RaidRoomKind.SentinelEye) return EyeOnShot(eye, dir, ref distance);
            if (room != RaidRoomKind.ColorLocks && room != RaidRoomKind.EchoRunes) return -1;
            float radius = room == RaidRoomKind.EchoRunes ? RaidRoomLayout.RuneRadius : RaidRoomLayout.CrystalRadius;
            int best = -1;
            var targets = _run.Layout.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                var c = RaidArena.FromRoom(room, targets[i]);
                var oc = c - eye;
                float along = math.dot(oc, dir);
                if (along < 0f || along > distance) continue;
                float miss2 = math.lengthsq(oc - dir * along);
                float r2 = radius * radius;
                if (miss2 > r2) continue;
                float d = along - math.sqrt(r2 - miss2);
                if (d < distance)
                {
                    distance = math.max(0f, d);
                    best = i;
                }
            }
            return best;
        }

        /// <summary>A shot reached a room target. Returns 1 to show a hit marker, 2 for a refused one, 0 for none.</summary>
        private int CrystalHit(int crystal, int shooterId)
        {
            if (_run.AtBoss || _run.Room == RaidRoomKind.SentinelEye) return StrangeHit(crystal, shooterId);
            if (_run.Room == RaidRoomKind.EchoRunes)
            {
                _runeHits++;
                var hit = _run.Runes.Hit(crystal / EchoRunes.RunesPerSet, crystal % EchoRunes.RunesPerSet, _time);
                if (hit == RuneHit.Wrong) Setback(3, shooterId);
                return 0;
            }
            int up = 0;
            foreach (int id in _raid.Participants)
            {
                if (_playerById.TryGetValue(id, out var e) && _players.TryGetValue(e, out var st) && !st.Vitals.IsDown) up++;
            }
            _crystalHits++;
            if (_run.Locks.Hit(crystal, shooterId, up, _time) == LockHit.Wrong) Setback(0, shooterId);
            return 0;
        }

        /// <summary>Keeps a raid Skitter in its room: a step past the walls turns it back.</summary>
        private void KeepInRoom(ref SkitterBehavior k, Vector3 before)
        {
            var room = _run != null ? _run.Room : RaidRoomKind.Boss;
            var c = RaidArena.RoomCenter(room);
            float limit = (room == RaidRoomKind.Boss ? RaidArena.HalfSize : RaidRoomLayout.SquareHalf) - 1.2f;
            if (math.abs(k.Position.X - c.x) <= limit && math.abs(k.Position.Z - c.z) <= limit) return;
            k.Position = before;
            k.Heading = -k.Heading;
        }

        private void WriteRoomStatus(int id, ref ExpeditionStatus status)
        {
            status.RaidStageCount = 0;
            if (_run == null || !_raid.IsInside(id)) return;
            status.RaidStage = _run.Stage;
            status.RaidStageCount = _run.StageCount;
            status.RaidRoom = _run.Room;
            status.RaidRoomSeed = _run.AtBoss ? 0u : _run.RoomSeed;
            status.RaidBoss = _run.Boss;
            status.RaidModifier = _run.Modifier;
            status.RaidRoomProgress = _run.Progress;
            status.RaidRoomClock = (float)_run.StageTime(_time);
            status.RaidPortal = !_run.Solved ? -2f : _run.PortalAt < 0 ? -1f : (float)_run.PortalLeft(_time);
            status.RaidRoomTimer = 0f;
            status.RaidYouHitLast = false;
            status.RaidTilesFallen = 0;
            status.RaidTilesCracking = 0;
            status.RaidTethers = 0;
            switch (_run.Room)
            {
                case RaidRoomKind.ColorLocks:
                    status.RaidRoomData = _run.Locks.Pack();
                    status.RaidYouHitLast = _run.Locks.LastHitter == id && _run.Players > 1;
                    break;
                case RaidRoomKind.HoldPlates:
                    status.RaidRoomData = _run.Plates.Plates | (_run.Plates.Occupied << 4);
                    break;
                case RaidRoomKind.Gauntlet:
                    status.RaidRoomData = _run.Gauntlet.Furthest;
                    break;
                case RaidRoomKind.WispStorm:
                    status.RaidRoomData = _run.Storm.Kills | (_run.Storm.Target << 8);
                    status.RaidRoomTimer = (float)_run.Storm.TimeLeft(_time);
                    break;
                case RaidRoomKind.EchoRunes:
                    status.RaidRoomData = _run.Runes.Pack(_time);
                    break;
                case RaidRoomKind.HoldCore:
                    status.RaidRoomData = _run.Core.Pack(_time);
                    status.RaidRoomTimer = (float)_run.Core.TimeLeft(_time);
                    break;
                case RaidRoomKind.Lanterns:
                    status.RaidRoomData = _run.Lanterns.Pack();
                    status.RaidRoomTimer = _run.Lanterns.GlowLeft(id, _time);
                    break;
                case RaidRoomKind.SentinelEye:
                    status.RaidRoomData = _run.Eye.Pack();
                    break;
                default:
                    if (_run.Weaver != null)
                    {
                        status.RaidTilesFallen = _run.Weaver.Fallen;
                        status.RaidTilesCracking = _run.Weaver.Cracking;
                        status.RaidTethers = _run.Weaver.PackTethers();
                        status.RaidRoomData = 0;
                        break;
                    }
                    // The Twins: bit i set while twin i is down, and the time left to kill the other.
                    status.RaidRoomData = _run.Twins == null ? 0 : (_run.Twins.IsDown(0) ? 1 : 0) | (_run.Twins.IsDown(1) ? 2 : 0);
                    status.RaidRoomTimer = _run.Twins == null ? 0f : (float)System.Math.Max(0, _run.Twins.TimeLeft(_time));
                    break;
            }
        }

        /// <summary>The HUD objective inside a run.</summary>
        private string RoomObjective(out bool hasTarget, out System.Numerics.Vector2 target)
        {
            hasTarget = false;
            target = default;
            string stage = $"RAID {_run.Stage + 1}/{_run.StageCount}";
            if (_run.Won)
            {
                var exit = RaidArena.FromRoom(RaidRoomKind.Boss, _run.Layout.Portal);
                hasTarget = true;
                target = new System.Numerics.Vector2(exit.x, exit.z);
                return $"VICTORY! Step into the portal when you're ready to go home";
            }
            if (_run.Solved)
            {
                var p = RaidArena.FromRoom(_run.Room, _run.Layout.Portal);
                hasTarget = true;
                target = new System.Numerics.Vector2(p.x, p.z);
                return _run.PortalAt < 0 ? $"{stage} solved! Step into the portal to move on" : $"{stage} solved! Moving on in {_run.PortalLeft(_time):0}s";
            }
            switch (_run.Room)
            {
                case RaidRoomKind.ColorLocks:
                    return $"{stage} Color Locks: shoot the crystals in the altar's order, each by a different player";
                case RaidRoomKind.HoldPlates:
                    return $"{stage} Hold the Plates: stand on all {_run.Plates.Plates} plates at once until the bar fills";
                case RaidRoomKind.Gauntlet:
                    return $"{stage} Sweeper Gauntlet: jump the stones and the sweepers. Everyone up must reach the far pad";
                case RaidRoomKind.WispStorm:
                    return $"{stage} Wisp Storm: kill {_run.Storm.Target} Wisps before the timer runs out";
                case RaidRoomKind.EchoRunes:
                    return $"{stage} Echo Runes: watch each wall's runes flash, then shoot them back in order. Split up: one wall each side";
                case RaidRoomKind.HoldCore:
                    return $"{stage} Hold the Core: keep the core alive for {HoldCore.Duration:0}s. Kill the Skitters running at it";
                case RaidRoomKind.Lanterns:
                    return $"{stage} Lanterns: payload hits make allies glow; shades only take hits near a glowing player";
                case RaidRoomKind.SentinelEye:
                    return $"{stage} Sentinel Eye: spread around the shell and shoot the eye while it's open ({_run.Eye.Target} hits)";
                default:
                    if (_run.Boss == RaidBossKind.Weaver)
                        return $"{stage} Break the Weaver: shoot its gold plate, cut the threads tying others, stay off cracking tiles";
                    return _run.Boss == RaidBossKind.Twins
                        ? $"{stage} Break the Twins: shoot their gold plates, and kill both within {RaidTwins.Window:0}s of each other"
                        : $"{stage} Break the Bulwark: shoot its plate while it's open (gold)";
            }
        }

        private string RunLog() => _run == null ? "run=none" :
            $"run=[{string.Join(">", _run.Rooms)}] mod={_run.Modifier} stage={_run.Stage}/{_run.StageCount} solved={_run.Solved} progress={_run.Progress:0.00} " +
            $"boss={_run.Boss} seed={_run.RoomSeed} roomsSolved={_roomsSolved} setbacks={_setbacks} crystalHits={_crystalHits} sweeperHits={_sweeperHits} " +
            $"falls={_falls} runeHits={_runeHits} coreBursts={_coreBursts}" +
            (_run.Core != null ? $" core={_run.Core.HealthFraction:0.00}" : "") +
            (_run.Twins != null ? $" twinsDown={(_run.Twins.IsDown(0) ? 1 : 0) + (_run.Twins.IsDown(1) ? 1 : 0)} revivals={_run.Twins.Revivals}" : "") +
            StrangeLog();
    }
}
