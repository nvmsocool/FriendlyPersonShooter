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
    /// The first raid (MVP step 6) on the server. Players standing at the raid door pledge Red and Green
    /// toward its key (carried first, then the party bank), strangers into the same key. A full key starts
    /// a countdown; whoever stands at the door when it ends goes up to the raid arena together (see
    /// <see cref="RaidArena"/>) to fight a Bulwark scaled to the group. Clearing it gives everyone inside
    /// a level's worth of XP and some Yellow; a wipe or the time limit sends them back down.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>Show the door's state on the HUD of anyone this close (metres).</summary>
        private const float RaidNoticeRadius = 45f;
        /// <summary>While the door gathers, point everyone this close at it.</summary>
        private const float RaidCallRadius = 300f;

        /// <summary>
        /// Debug and bot runs: fill the key and take every player in at the end of the countdown.
        /// Set it from the Editor, or start a build with -fps-raid-now.
        /// </summary>
        public static bool DebugStartRaid = System.Environment.CommandLine.Contains("-fps-raid-now");

        private readonly BulwarkSettings _bulwark = new BulwarkSettings();
        private RaidDoor _raid;
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
            RaidArena.Ensure();
            var door = ExpeditionWorld.RaidDoor;
            Debug.Log($"[Raid] door at ({door.X:0},{door.Y:0}) {_map.Describe(door)}; key {_raid.Settings.RedCost} Red + {_raid.Settings.GreenCost} Green");
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
                }
            }

            _atDoor.Clear();
            foreach (var kv in _players)
            {
                var st = kv.Value;
                int id = st.Vitals.Id;
                if (st.Vitals.IsDown || _raid.IsInside(id)) continue;
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                bool atDoor = _map.Distance(ExpeditionWorld.Ground(pos), door) <= _raid.Settings.UseRadius && !RaidArena.Contains(pos);
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
                case RaidDoorEvent.GatherStarted:
                    _announce.Add((AnnounceKind.RaidGathering, (int)_raid.Settings.GatherSeconds, 0, 0));
                    Debug.Log($"[Raid] key full; gathering for {_raid.Settings.GatherSeconds:0}s");
                    break;
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

        private void StartRaid()
        {
            var inside = _raid.Participants;
            int n = inside.Count;
            float health = BulwarkSettings.HealthFor(n, _combat);
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            var state = new EnemyState { Family = EnemyFamily.Bulwark, MaxHealth = health, Raid = true };
            state.Boss = BulwarkBehavior.Create(ToVec(RaidArena.Center), random.NextFloat(0f, 360f), _bulwark);
            _spawned.Clear();
            SpawnEnemy(state, state.Boss.Position);
            _spawned.Clear();
            _raidWaveDue = 0;
            _raidBossDead = false;
            for (int i = 0; i < n; i++) TeleportPlayer(inside[i], RaidArena.SpawnSpot(i, n));
            _raidsStarted++;
            _announce.Add((AnnounceKind.RaidStarted, n, 0, 0));
            Debug.Log($"[Raid] started with {n} player(s): [{string.Join(",", inside)}], Bulwark health {health:0}");
        }

        private void StepRaidRun(EntityCommandBuffer ecb)
        {
            if (_raidBossDead)
            {
                _raidBossDead = false;
                EndRaid(RaidOutcome.Cleared, ecb);
                return;
            }
            _raidScratch.Clear();
            _raidScratch.AddRange(_raid.Participants);
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
                if (!RaidArena.Contains(pos, 120f))
                {
                    _raid.Leave(id);
                    continue;
                }
                // Fell off the edge: back onto the platform.
                if (pos.y < RaidArena.FloorY - 8f) TeleportPlayer(id, RaidArena.SpawnSpot(i, _raidScratch.Count));
                if (!st.Vitals.IsDown) up++;
            }

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
                SpawnSkitter(new Vector3(c.x + o.x, c.y + _skitter.BodyRadius, c.z + o.y));
            }
            if (wave >= 2)
            {
                var o = random.NextFloat2Direction() * 12f;
                SpawnSwarm(new Vector3(c.x + o.x, c.y + 1.5f, c.z + o.y));
            }
            foreach (var s in _spawned) s.Raid = true;
            _spawned.Clear();
            Debug.Log($"[Raid] wave {wave}: {skitters} Skitters{(wave >= 2 ? " + a Mite swarm" : "")}");
        }

        /// <summary>The boss died (called from KillEnemy, mid-shot): the run ends on the raid's next step.</summary>
        private void RaidBossKilled()
        {
            if (_raid != null && _raid.Phase == RaidPhase.Running) _raidBossDead = true;
        }

        private void EndRaid(RaidOutcome outcome, EntityCommandBuffer ecb)
        {
            float took = (float)(_time - _raid.StartedAt);
            _raidScratch.Clear();
            _raidScratch.AddRange(_raid.Participants);
            _raid.Finish(_time, outcome);

            // Whatever is left of the fight goes away (no drops).
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

            // Everyone inside comes back down by the door. On a clear the downed get back up and everyone
            // is paid; otherwise the downed keep bleeding out by the door, where others can revive them.
            var door = RaidDoorPoint;
            for (int i = 0; i < _raidScratch.Count; i++)
            {
                int id = _raidScratch[i];
                if (!_playerById.TryGetValue(id, out var e) || !_players.TryGetValue(e, out var st)) continue;
                if (outcome == RaidOutcome.Cleared)
                {
                    if (st.Revive != null)
                    {
                        st.Vitals.Revive(_combat.Revive.ReviveHealthFraction);
                        st.Revive = null;
                    }
                    AddXp(id, _raid.Settings.ClearXp);
                    WalletOf(id).PickUp(EssenceColor.Yellow, _raid.Settings.ClearYellow);
                }
                float a = 2f * math.PI * i / math.max(1, _raidScratch.Count);
                var spot = ExpeditionWorld.ToFloat3(door) + new float3(math.sin(a) * 4f, 0f, math.cos(a) * 4f);
                TeleportPlayer(id, ExpeditionTerrain.OnGround(ExpeditionWorld.Wrap(spot)));
            }

            if (outcome == RaidOutcome.Cleared)
            {
                _raidsCleared++;
                _announce.Add((AnnounceKind.RaidCleared, (int)took, 0, 0));
            }
            else
            {
                _raidsFailed++;
                _announce.Add((AnnounceKind.RaidFailed, (int)outcome, 0, 0));
            }
            _raidResults += $"{outcome}:{_raidScratch.Count}p/{took:0}s ";
            Debug.Log($"[Raid] {outcome} after {took:0}s with {_raidScratch.Count} player(s) inside");
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

        /// <summary>Keeps a raid Skitter on the platform: a step past the walls turns it back.</summary>
        private static void KeepInArena(ref SkitterBehavior k, Vector3 before)
        {
            var c = RaidArena.Center;
            float limit = RaidArena.HalfSize - 1.2f;
            if (math.abs(k.Position.X - c.x) <= limit && math.abs(k.Position.Z - c.z) <= limit) return;
            k.Position = before;
            k.Heading = -k.Heading;
        }

        /// <summary>The HUD objective while a player is inside the raid, at the door, or called to it.</summary>
        private bool TryRaidObjective(int id, Vector2 pos, ExpeditionPhase phase, out ExpeditionObjective objective)
        {
            objective = default;
            if (_raid == null) return false;
            var door = RaidDoorPoint;
            float toDoor = _map.Distance(pos, door);
            int left = (int)_raid.TimeLeft(_time);
            if (_raid.IsInside(id))
            {
                objective = new ExpeditionObjective(phase,
                    $"RAID: break the Bulwark. Shoot its plate while it's open (gold). {left / 60}:{left % 60:00} left", false, default, "raid");
                return true;
            }
            if (_raid.Phase == RaidPhase.Gathering && toDoor <= RaidCallRadius)
            {
                objective = new ExpeditionObjective(phase,
                    $"RAID DOOR OPEN: stand at the door to go in! {left}s ({toDoor:0} m)", true, door, "raid door");
                return true;
            }
            if (_raid.Phase == RaidPhase.Collecting && toDoor <= RaidNoticeRadius)
            {
                objective = new ExpeditionObjective(phase,
                    $"Raid door: stand at it to pledge Red and Green. Key: Red {_raid.Pledged(EssenceColor.Red)}/{_raid.Cost(EssenceColor.Red)}, " +
                    $"Green {_raid.Pledged(EssenceColor.Green)}/{_raid.Cost(EssenceColor.Green)}", true, door, "raid door");
                return true;
            }
            return false;
        }

        private void WriteRaidStatus(int id, ref ExpeditionStatus status)
        {
            if (_raid == null) return;
            status.RaidPhase = _raid.Phase;
            status.InRaid = _raid.IsInside(id);
            status.KeyRed = _raid.Pledged(EssenceColor.Red);
            status.KeyGreen = _raid.Pledged(EssenceColor.Green);
            status.RaidTimer = (float)_raid.TimeLeft(_time);
        }

        private string RaidLog()
        {
            if (_raid == null) return "";
            float bossHealth = -1f;
            foreach (var s in _enemies.Values) if (s.Family == EnemyFamily.Bulwark) bossHealth = s.Vitals.Health;
            return $"phase={_raid.Phase} key=R{_raid.Pledged(EssenceColor.Red)}/G{_raid.Pledged(EssenceColor.Green)} pledged={_raidPledged} " +
                   $"inside=[{(_raid.Phase == RaidPhase.Running ? string.Join(",", _raid.Participants) : "")}] boss={bossHealth:0} started={_raidsStarted} cleared={_raidsCleared} " +
                   $"failed={_raidsFailed} results=[{_raidResults}]";
        }
    }
}
