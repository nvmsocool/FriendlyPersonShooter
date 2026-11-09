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
    /// The first raid (MVP step 6) on the server. Players standing at the raid door pledge Blue, Purple and Green
    /// toward its key (carried first, then the party bank), strangers into the same key. With a full key,
    /// anyone at the door presses E to start a countdown; whoever stands at the door when it ends goes up together for a run (see
    /// <see cref="RaidRun"/> and ArenaServerRaidRooms): three random rooms, then a Bulwark scaled to the
    /// group on the raid arena (<see cref="RaidArena"/>). Beating it blows the boss apart and pays everyone
    /// inside a level's worth of XP and an item (raids never pay essence); they can stay up there, and each goes home
    /// through the exit portal (or all at once when the hang-out ends). A wipe or the time limit sends them back down.
    /// The layer has several doors, one open at a time (<see cref="RaidDoorRotation"/>): a clear spends the door it
    /// was entered by, and the next one opens somewhere else.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>Show the door's state on the HUD of anyone this close (metres).</summary>
        private const float RaidNoticeRadius = 45f;
        /// <summary>While the door gathers, point everyone this close at it.</summary>
        private const float RaidCallRadius = 300f;

        /// <summary>
        /// Debug and bot runs: fill the key, start the countdown, and take every player in at its end.
        /// Set it from the Editor, or start a build with -fps-raid-now.
        /// </summary>
        public static bool DebugStartRaid = System.Environment.CommandLine.Contains("-fps-raid-now");

        private readonly BulwarkSettings _bulwark = new BulwarkSettings();
        private RaidDoor _raid;
        private RaidDoorRotation _doors;
        /// <summary>The door the running raid was entered by; raiders come home to it.</summary>
        private Vector2 _raidEntry;
        private readonly List<int> _atDoor = new List<int>();
        private readonly List<int> _raidScratch = new List<int>();
        private int _raidWaveDue;
        private bool _raidBossDead;
        private bool _raidTakeEveryone;
        private int _raidsStarted, _raidsCleared, _raidsFailed, _raidPledged;
        private string _raidResults = "";
        private int _tick;

        private void CreateRaid()
        {
            _raid = new RaidDoor();
            _doors = new RaidDoorRotation(ExpeditionWorld.RaidDoors.Count);
            ExpeditionWorld.RaidDoorIndex = _doors.Active;
            ExpeditionWorld.RaidDoorsSpent = 0;
            RaidArena.Ensure();
            for (int i = 0; i < ExpeditionWorld.RaidDoors.Count; i++)
            {
                var door = ExpeditionWorld.RaidDoors[i];
                Debug.Log($"[Raid] door {i} at ({door.X:0},{door.Y:0}) {_map.Describe(door)}{(i == _doors.Active ? " (open)" : "")}");
            }
            Debug.Log($"[Raid] key {_raid.Settings.BlueCost} Blue + {_raid.Settings.PurpleCost} Purple + {_raid.Settings.GreenCost} Green");
        }

        private bool InsideRaid(int id) => _raid != null && _raid.IsInside(id);

        private Vector2 RaidDoorPoint => ExpeditionWorld.RaidDoor;

        /// <summary>Pledges, the countdown, and the run. Called with the expedition's status pass.</summary>
        private void StepRaid(EntityCommandBuffer ecb)
        {
            RaidArena.Ensure();
            var door = RaidDoorPoint;
            if (DebugStartRaid)
            {
                DebugStartRaid = false;
                if (_raid.Phase == RaidPhase.Collecting)
                {
                    _raid.FillKey();
                    _raidTakeEveryone = true;
                    if (_raid.Begin(_time)) AnnounceGather(0);
                }
            }

            _atDoor.Clear();
            foreach (var kv in _players)
            {
                var st = kv.Value;
                int id = st.Vitals.Id;
                if (st.Vitals.IsDown || _raid.IsInside(id)) continue;
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                bool atDoor = _map.Distance(ExpeditionWorld.Ground(pos), door) <= _raid.Settings.UseRadius && !RaidArena.InRaidSpace(pos);
                if (!atDoor && !_raidTakeEveryone) continue;
                _atDoor.Add(id);
                if (!atDoor) continue;
                int pledged = _raid.Pledge(WalletOf(id));
                if (pledged > 0)
                {
                    _raidPledged += pledged;
                    _announce.Add((AnnounceKind.RaidPledged, pledged, id, 0));
                }
            }
            _atDoor.Sort();

            switch (_raid.Update(_time, _atDoor))
            {
                case RaidDoorEvent.GatherCancelled:
                    Debug.Log("[Raid] nobody at the door when the countdown ended; the key waits");
                    break;
                case RaidDoorEvent.Started:
                    _raidTakeEveryone = false;
                    StartRaid();
                    break;
            }
            if (_raid.Phase == RaidPhase.Running) StepRaidRun(ecb);
        }

        /// <summary>E at the raid door (called from Interact): with a full key, starts the countdown.</summary>
        private bool TryStartRaid(int id, float3 feet)
        {
            if (_raid == null || !_raid.Ready || RaidArena.InRaidSpace(feet)) return false;
            if (_map.Distance(ExpeditionWorld.Ground(feet), RaidDoorPoint) > _raid.Settings.UseRadius) return false;
            if (!_raid.Begin(_time)) return false;
            AnnounceGather(id);
            return true;
        }

        /// <summary><paramref name="id"/> started the countdown (0 for a debug start).</summary>
        private void AnnounceGather(int id)
        {
            _announce.Add((AnnounceKind.RaidGathering, (int)_raid.Settings.GatherSeconds, id, 0));
            Debug.Log($"[Raid] player {id} started the countdown; gathering for {_raid.Settings.GatherSeconds:0}s");
        }

        private void StartRaid()
        {
            var inside = _raid.Participants;
            int n = inside.Count;
            _raidEntry = RaidDoorPoint;
            _run = new RaidRun(NextSeed(), n, null, DebugRaidRoom, DebugRaidModifier, DebugRaidBoss);
            _run.Begin(_time);
            _raidWaveDue = 0;
            _raidBossDead = false;
            _raidsStarted++;
            _announce.Add((AnnounceKind.RaidStarted, n, 0, 0));
            Debug.Log($"[Raid] started with {n} player(s): [{string.Join(",", inside)}], rooms {string.Join(" > ", _run.Rooms)} > {_run.Boss}, modifier {_run.Modifier}");
            EnterStage();
        }

        /// <summary>The Twins' full health each (for the add waves on their combined health, and revivals).</summary>
        private float _twinMaxHealth;

        /// <summary>
        /// The run's last stage on the raid arena: the Bulwark scaled to the group, or the Twins (two of them
        /// either side of the middle, each with a share of that health, turning opposite ways).
        /// </summary>
        private void SpawnRaidBoss(int players)
        {
            float health = BulwarkSettings.HealthFor(players, _combat);
            if (_run.Boss == RaidBossKind.Twins)
            {
                _twinMaxHealth = health * RaidTwins.HealthShare;
                SpawnTwin(0, 1f);
                SpawnTwin(1, 1f);
                Debug.Log($"[Raid] the Twins arrive with {_twinMaxHealth:0} health each");
                return;
            }
            RaidArena.SetBossFloor(0);
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            var state = new EnemyState { Family = EnemyFamily.Bulwark, MaxHealth = health, Raid = true };
            state.Boss = BulwarkBehavior.Create(ToVec(RaidArena.Center), random.NextFloat(0f, 360f), _bulwark);
            _spawned.Clear();
            SpawnEnemy(state, state.Boss.Position);
            _spawned.Clear();
            Debug.Log($"[Raid] {RaidRoomLayout.NameOf(_run.Boss)} arrives with {health:0} health");
        }

        private void SpawnTwin(int twin, float healthShare)
        {
            var home = RaidArena.Center + new float3(twin == 0 ? -RaidTwins.Apart : RaidTwins.Apart, 0f, 0f);
            var state = new EnemyState { Family = EnemyFamily.Bulwark, MaxHealth = _twinMaxHealth, Raid = true, Twin = twin };
            // The two plates start facing opposite ways, so the group has to split to cover both.
            state.Boss = BulwarkBehavior.Create(ToVec(home), twin * 180f, _bulwark);
            _spawned.Clear();
            SpawnEnemy(state, state.Boss.Position);
            _spawned.Clear();
            if (healthShare < 1f) state.Vitals.SetHealth(_twinMaxHealth * healthShare);
        }

        /// <summary>Both twins' health together, 0..1 (a fallen twin counts as empty).</summary>
        private float TwinsHealthFraction()
        {
            float sum = 0f;
            foreach (var s in _enemies.Values) if (s.Twin >= 0 && s.Raid) sum += s.Vitals.Health;
            return sum / System.Math.Max(1f, 2f * _twinMaxHealth);
        }

        private void StepRaidRun(EntityCommandBuffer ecb)
        {
            if (_raidBossDead)
            {
                _raidBossDead = false;
                WinRaid(ecb);
            }
            _raidScratch.Clear();
            _raidScratch.AddRange(_raid.Participants);
            _roomPlayers.Clear();
            var room = _run.Room;
            int up = 0;
            for (int i = 0; i < _raidScratch.Count; i++)
            {
                int id = _raidScratch[i];
                if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var st))
                {
                    _raid.Leave(id);
                    continue;
                }
                var pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                // Far below: they bled out to a shrine or reset their position down there.
                if (!RaidArena.InRaidSpace(pos))
                {
                    _raid.Leave(id);
                    continue;
                }
                // Fell off the edge, or somehow outside the current room: back onto its platform
                // (the gauntlet handles its own falls).
                bool fell = room != RaidRoomKind.Gauntlet && pos.y < RaidArena.RoomCenter(room).y - 8f;
                if (fell && _run.AtBoss && _run.Weaver != null)
                {
                    // Through the Weaver's floor: back up onto a tile, stung.
                    pos = WeaverLanding(pos);
                    TeleportPlayer(id, pos);
                    GauntletHurt(ecb, id, RaidWeaver.FallDamage);
                    _weaverFalls++;
                }
                else if (fell || !RaidArena.InRoom(room, pos, 60f, 60f))
                {
                    pos = RaidArena.RoomSpawn(room, i, _raidScratch.Count);
                    TeleportPlayer(id, pos);
                }
                if (!st.Vitals.IsDown) up++;
                _roomPlayers.Add((id, RaidArena.ToRoom(room, pos), !st.Vitals.IsDown));
            }
            if (_run.Won)
            {
                // The celebration: no more fighting; each raider goes home through the exit portal.
                StepExitPortal();
                var over = _raid.Check(_time, up);
                if (over != RaidOutcome.None) EndRaid(over, ecb);
                return;
            }
            if (!_run.AtBoss) StepRoom(ecb);
            else if (_run.Twins != null && _run.Twins.Step(_time, out int revived) == TwinEvent.Revived)
            {
                SpawnTwin(revived, RaidTwins.ReviveHealth);
                _announce.Add((AnnounceKind.TwinRevived, revived, 0, 0));
                Debug.Log($"[Raid] twin {revived} is back");
            }
            else if (_run.AtBoss && _run.Weaver != null) StepWeaver(ecb);

            if (_raidWaveDue > 0)
            {
                SpawnRaidAdds(_raidWaveDue);
                _announce.Add((AnnounceKind.RaidAdds, _raidWaveDue, 0, 0));
                _raidWaveDue = 0;
            }

            var outcome = _raid.Check(_time, up);
            if (outcome != RaidOutcome.None) EndRaid(outcome, ecb);
        }

        /// <summary>Skitters (and from the second wave a Mite swarm) climb onto the platform.</summary>
        private void SpawnRaidAdds(int wave)
        {
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            var c = RaidArena.Center;
            int skitters = wave >= 3 ? 3 : 2;
            _spawned.Clear();
            for (int i = 0; i < skitters; i++)
            {
                var o = random.NextFloat2Direction() * random.NextFloat(8f, RaidArena.HalfSize - 4f);
                for (int k = 0; k < _run.AddsMultiplier; k++) SpawnSkitter(new Vector3(c.x + o.x, c.y + _skitter.BodyRadius, c.z + o.y));
            }
            if (wave >= 2)
            {
                var o = random.NextFloat2Direction() * 12f;
                for (int k = 0; k < _run.AddsMultiplier; k++) SpawnSwarm(new Vector3(c.x + o.x, c.y + 1.5f, c.z + o.y));
            }
            foreach (var s in _spawned) s.Raid = true;
            _spawned.Clear();
            Debug.Log($"[Raid] wave {wave}: {skitters} Skitters{(wave >= 2 ? " + a Mite swarm" : "")}");
        }

        /// <summary>
        /// A boss died (called from KillEnemy, mid-shot): the run ends on the raid's next step, unless it was
        /// the first of the Twins, which starts their clock.
        /// </summary>
        private void RaidBossKilled(EnemyState s)
        {
            if (_raid == null || _raid.Phase != RaidPhase.Running || _run == null || !_run.AtBoss) return;
            if (_run.Twins == null || s.Twin < 0)
            {
                _raidBossDead = true;
                return;
            }
            switch (_run.Twins.Killed(s.Twin, _time))
            {
                case TwinEvent.FirstDown:
                    _announce.Add((AnnounceKind.TwinDown, (int)RaidTwins.Window, s.Twin, 0));
                    Debug.Log($"[Raid] twin {s.Twin} fell; {RaidTwins.Window:0}s for the other");
                    break;
                case TwinEvent.Beaten:
                    _raidBossDead = true;
                    break;
            }
        }

        /// <summary>
        /// The boss died: it blows apart (clients draw it), the fight's leftovers go, and everyone inside is paid
        /// on the spot (the downed get back up; each gets XP and a pick of three rare-or-better items).
        /// They stay up there until they leave through the exit portal.
        /// </summary>
        private void WinRaid(EntityCommandBuffer ecb)
        {
            float took = (float)(_time - _raid.StartedAt);
            if (!_raid.Win(_time) || !_run.MarkWon()) return;
            ClearRaidEnemies(ecb);
            RaidArena.SetBossFloor(0);
            _raidWaveDue = 0;
            _raidScratch.Clear();
            _raidScratch.AddRange(_raid.Participants);
            foreach (int id in _raidScratch)
            {
                if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var st)) continue;
                if (st.Revive != null)
                {
                    st.Vitals.Revive(_combat.Revive.ReviveHealthFraction);
                    st.Revive = null;
                }
                AddXp(id, _raid.Settings.ClearXp);
                GiveBossLoot(id);
            }
            SpendDoor();
            _raidsCleared++;
            _announce.Add((AnnounceKind.RaidCleared, (int)took, 0, 0));
            Debug.Log($"[Raid] Cleared after {took:0}s with {_raidScratch.Count} player(s) inside; the exit portal is open");
        }

        /// <summary>After the boss: whoever steps into the exit portal goes home to the door.</summary>
        private void StepExitPortal()
        {
            var portal = RaidArena.FromRoom(RaidRoomKind.Boss, _run.Layout.Portal);
            _raidScratch.Clear();
            _raidScratch.AddRange(_raid.Participants);
            for (int i = 0; i < _raidScratch.Count; i++)
            {
                int id = _raidScratch[i];
                if (!_playerById.TryGetValue(id, out var e)) continue;
                var pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                var d = pos - portal;
                if (d.x * d.x + d.z * d.z > RaidRoomLayout.PortalRadius * RaidRoomLayout.PortalRadius || math.abs(d.y) > 3f) continue;
                _raid.Leave(id);
                SendHome(id, i, _raidScratch.Count);
                Debug.Log($"[Raid] player {id} went home through the exit portal");
            }
        }

        /// <summary>The open door's raid was cleared: that door goes dark and the next one opens.</summary>
        private void SpendDoor()
        {
            int was = _doors.Active;
            ExpeditionWorld.RaidDoorIndex = _doors.Cleared();
            ExpeditionWorld.RaidDoorsSpent = _doors.SpentMask;
            var door = RaidDoorPoint;
            Debug.Log($"[Raid] door {was} is spent; door {_doors.Active} opened at ({door.X:0},{door.Y:0}) {_map.Describe(door)}");
        }

        /// <summary>Puts a raider back on the ground in a ring around the door they went in by.</summary>
        private float3 SendHome(int id, int index, int count)
        {
            float a = 2f * math.PI * index / math.max(1, count);
            var spot = ExpeditionWorld.ToFloat3(_raidEntry) + new float3(math.sin(a) * 4f, 0f, math.cos(a) * 4f);
            var landed = ExpeditionTerrain.OnGround(ExpeditionWorld.Wrap(spot));
            TeleportPlayer(id, landed);
            return landed;
        }

        private void EndRaid(RaidOutcome outcome, EntityCommandBuffer ecb)
        {
            float took = (float)(_time - _raid.StartedAt);
            // A won run was paid when the boss died; this just sends the last ones home.
            bool celebrated = _raid.Won;
            _raidScratch.Clear();
            _raidScratch.AddRange(_raid.Participants);
            _raid.Finish(_time, outcome);

            // Whatever is left of the fight goes away (no drops); the Weaver's floor comes back.
            ClearRaidEnemies(ecb);
            RaidArena.SetBossFloor(0);
            string reached = _run == null ? "" : $" at stage {_run.Stage + 1}/{_run.StageCount} ({_run.StageName})";
            _run = null;

            // Everyone inside comes back down by the door. On a clear the downed get back up and everyone
            // is paid; otherwise the downed keep bleeding out by the door, where others can revive them.
            for (int i = 0; i < _raidScratch.Count; i++)
            {
                int id = _raidScratch[i];
                if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var st)) continue;
                bool pay = outcome == RaidOutcome.Cleared && !celebrated;
                if (pay)
                {
                    if (st.Revive != null)
                    {
                        st.Vitals.Revive(_combat.Revive.ReviveHealthFraction);
                        st.Revive = null;
                    }
                    AddXp(id, _raid.Settings.ClearXp);
                }
                SendHome(id, i, _raidScratch.Count);
                // A clear also offers each raider three rare-or-better items to pick one from.
                if (pay) GiveBossLoot(id);
            }

            if (celebrated)
            {
                Debug.Log($"[Raid] the celebration ended after {took:0}s; {_raidScratch.Count} player(s) sent home");
                return;
            }
            if (outcome == RaidOutcome.Cleared)
            {
                SpendDoor();
                _raidsCleared++;
                _announce.Add((AnnounceKind.RaidCleared, (int)took, 0, 0));
            }
            else
            {
                _raidsFailed++;
                _announce.Add((AnnounceKind.RaidFailed, (int)outcome, 0, 0));
            }
            _raidResults += $"{outcome}:{_raidScratch.Count}p/{took:0}s ";
            Debug.Log($"[Raid] {outcome} after {took:0}s with {_raidScratch.Count} player(s) inside{reached}");
        }

        /// <summary>Moves a player (server-side, like Reset position) and tells movement and lag compensation it was a jump.</summary>
        private void TeleportPlayer(int id, float3 spot)
        {
            if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var st)) return;
            var t = EntityManager.GetComponentData<LocalTransform>(e);
            t.Position = spot;
            EntityManager.SetComponentData(e, t);
            if (EntityManager.HasComponent<ArenaMotion>(e)) EntityManager.SetComponentData(e, new ArenaMotion { Grounded = true });
            st.Position = ToVec(spot);
            st.History.Record(_tick, ToVec(spot), true, teleported: true);
        }

        /// <summary>The HUD objective while a player is inside the raid, at the door, or called to it.</summary>
        private bool TryRaidObjective(int id, Vector2 pos, ExpeditionPhase phase, out ExpeditionObjective objective)
        {
            objective = default;
            if (_raid == null) return false;
            var door = RaidDoorPoint;
            float toDoor = _map.Distance(pos, door);
            int left = (int)_raid.TimeLeft(_time);
            if (_raid.IsInside(id) && _run != null)
            {
                string text = RoomObjective(out bool hasTarget, out var target);
                objective = new ExpeditionObjective(phase, text, hasTarget, target, "portal");
                return true;
            }
            if (_raid.Phase == RaidPhase.Gathering && toDoor <= RaidCallRadius)
            {
                objective = new ExpeditionObjective(phase,
                    $"RAID DOOR OPEN: stand at the door to go in! {left}s ({toDoor:0} m)", true, door, "raid door");
                return true;
            }
            if (_raid.Ready && toDoor <= RaidNoticeRadius)
            {
                objective = new ExpeditionObjective(phase,
                    $"Raid key full: press E at the door to start the {_raid.Settings.GatherSeconds:0}s countdown", true, door, "raid door");
                return true;
            }
            if (_raid.Phase == RaidPhase.Collecting && toDoor <= RaidNoticeRadius)
            {
                objective = new ExpeditionObjective(phase,
                    $"Raid door: stand at it to pledge {RaidKey.ColorList()}. Key: Blue {_raid.Pledged(EssenceColor.Blue)}/{_raid.Cost(EssenceColor.Blue)}, " +
                    $"Purple {_raid.Pledged(EssenceColor.Purple)}/{_raid.Cost(EssenceColor.Purple)}, " +
                    $"Green {_raid.Pledged(EssenceColor.Green)}/{_raid.Cost(EssenceColor.Green)}", true, door, "raid door");
                return true;
            }
            // At a dark door: point at the open one.
            var doors = ExpeditionWorld.RaidDoors;
            for (int i = 0; i < doors.Count; i++)
            {
                if (i == _doors.Active || _map.Distance(pos, doors[i]) > RaidNoticeRadius) continue;
                string why = _doors.IsSpent(i) ? "This raid door is spent (its raid was cleared)" : "This raid door is dark for now";
                objective = new ExpeditionObjective(phase, $"{why}. The open raid door is {toDoor:0} m away", true, door, "raid door");
                return true;
            }
            return false;
        }

        private void WriteRaidStatus(int id, ref ExpeditionStatus status)
        {
            if (_raid == null) return;
            status.RaidPhase = _raid.Phase;
            status.InRaid = _raid.IsInside(id);
            status.Key = RaidKey.Pack(_raid);
            status.RaidTimer = (float)_raid.TimeLeft(_time);
            status.RaidDoorIndex = (byte)_doors.Active;
            status.RaidDoorsSpent = (byte)_doors.SpentMask;
            WriteRoomStatus(id, ref status);
        }

        private string RaidLog()
        {
            if (_raid == null) return "";
            float bossHealth = -1f;
            foreach (var s in _enemies.Values) if (s.Family == EnemyFamily.Bulwark && s.Raid) bossHealth = System.Math.Max(0f, bossHealth) + s.Vitals.Health;
            return $"phase={_raid.Phase} key=B{_raid.Pledged(EssenceColor.Blue)}/P{_raid.Pledged(EssenceColor.Purple)}/G{_raid.Pledged(EssenceColor.Green)} pledged={_raidPledged} " +
                   $"inside=[{(_raid.Phase == RaidPhase.Running ? string.Join(",", _raid.Participants) : "")}] boss={bossHealth:0} started={_raidsStarted} cleared={_raidsCleared} " +
                   $"failed={_raidsFailed} results=[{_raidResults}] {RunLog()}";
        }
    }
}
