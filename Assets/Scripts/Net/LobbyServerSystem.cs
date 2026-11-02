using System.Collections.Generic;
using FriendlyPersonShooter.Core.Lobbies;
using FriendlyPersonShooter.Core.World;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Net
{
    /// <summary>Lobby numbers for debug displays. Written by the server, read by the in-process view.</summary>
    public static class LobbyStats
    {
        public static int Players;
        public static int Lobbies;
        public static float AverageSize;
        public static int LargestLobby;
        public static int TotalSplits;
        public static int TotalMerges;
        /// <summary>Times players moved lobby to join their party.</summary>
        public static int TotalGroupJoins;
        /// <summary>Parties with two or more members online, and the players in them.</summary>
        public static int Parties;
        public static int PlayersInParties;
        /// <summary>Zone simulation cost of the current lobbies; see <see cref="LobbyCost"/>.</summary>
        public static LobbyCostReport Cost = new LobbyCostReport();
        /// <summary>Pass-criteria measurements: pop-ins, encounters after merges, party splits.</summary>
        public static LobbyMetrics Metrics;
        /// <summary>Lobby id for each network id, so a client view can show its own lobby.</summary>
        public static readonly Dictionary<int, int> LobbyOfPlayer = new Dictionary<int, int>();

        /// <summary>The latest splits and merges, oldest first, for the lobby map.</summary>
        public static readonly List<LobbyEvent> RecentEvents = new List<LobbyEvent>();
        private const int MaxEvents = 40;

        public static void Record(LobbyEvent e)
        {
            e.Time = Time.unscaledTimeAsDouble;
            RecentEvents.Add(e);
            if (RecentEvents.Count > MaxEvents) RecentEvents.RemoveAt(0);
        }
    }

    public enum LobbyEventKind { Split, Merge, Group }

    /// <summary>
    /// One split, merge or party move. For a split, B is the new lobby; for a merge, B joined A;
    /// for a party move, players left B to join their party in A.
    /// </summary>
    public struct LobbyEvent
    {
        public LobbyEventKind Kind;
        public double Time;
        public int LobbyA;
        public int LobbyB;
        /// <summary>Where it happened: the zone of the first player who moved lobby.</summary>
        public ZoneCoord Zone;
        /// <summary>Players in A afterwards.</summary>
        public int PlayersA;
        /// <summary>Players in B afterwards (0 for a merge).</summary>
        public int PlayersB;
    }

    /// <summary>
    /// Runs the lobby rules on the server. Every player starts in a lobby of their own; lobbies split
    /// when players spread out and the matchmaker merges lobbies likely to meet. Visibility follows
    /// lobbies: each connection only receives the player ghosts in its own lobby.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class LobbyServerSystem : SystemBase
    {
        private WorldGrid _grid;
        private LobbyRules _rules;
        private readonly Dictionary<int, Lobby> _lobbies = new Dictionary<int, Lobby>();
        private readonly Dictionary<int, LobbyPlayer> _players = new Dictionary<int, LobbyPlayer>();
        private readonly Dictionary<int, int> _lobbyOf = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _ghostIdOf = new Dictionary<int, int>();
        private readonly Dictionary<int, Unity.Mathematics.float3> _posOf = new Dictionary<int, Unity.Mathematics.float3>();
        private readonly List<PlayerSample> _samples = new List<PlayerSample>();
        private LobbyMetrics _metrics;
        private readonly HashSet<int> _seen = new HashSet<int>();
        /// <summary>Lobbies that just split can't merge until this time, so a pair at the edge can't flicker.</summary>
        private readonly Dictionary<int, double> _noMergeUntil = new Dictionary<int, double>();
        private const double MergeCooldown = 10.0;
        private int _nextLobbyId = 1;
        private double _nextLobbyPass;
        private double _nextLog;

        protected override void OnCreate()
        {
            RequireForUpdate<GhostRelevancy>();
            _grid = NetTestSettings.CreateGrid();
            _rules = NetTestSettings.CreateRules();
            // Test zones are small, so a player in view is roughly a player within one zone.
            _metrics = new LobbyMetrics(NetTestSettings.WorldSize, NetTestSettings.WorldSize)
            {
                ViewDistance = NetTestSettings.ZoneSize,
                EncounterDistance = NetTestSettings.ZoneSize,
            };
            LobbyStats.Metrics = _metrics;
        }

        protected override void OnUpdate()
        {
            SyncPlayers();

            double now = SystemAPI.Time.ElapsedTime;
            if (now >= _nextLobbyPass)
            {
                _nextLobbyPass = now + NetTestSettings.LobbyInterval;
                GatherGroups(now);
                SplitLobbies(now);
                MergeLobbies(now);
                PublishStats();
                ObserveMetrics(now);
            }

            if (now >= _nextLog)
            {
                _nextLog = now + 5.0;
                Debug.Log($"[Lobbies] players={LobbyStats.Players} lobbies={LobbyStats.Lobbies} " +
                          $"avg={LobbyStats.AverageSize:0.0} max={LobbyStats.LargestLobby} " +
                          $"splits={LobbyStats.TotalSplits} merges={LobbyStats.TotalMerges} groupJoins={LobbyStats.TotalGroupJoins} " +
                          $"parties={LobbyStats.Parties} inParties={LobbyStats.PlayersInParties} " +
                          $"zoneCopies={LobbyStats.Cost.LoadedInstances} zones={LobbyStats.Cost.DistinctLoadedZones} " +
                          $"duplication={LobbyStats.Cost.Duplication:0.00} zonesPerPlayer={LobbyStats.Cost.LoadedZonesPerPlayer:0.0}");
                var m = _metrics;
                Debug.Log($"[Metrics] mergePopIns={m.MergePopIns} partyPopIns={m.PartyPopIns} loginPopIns={m.LoginPopIns} " +
                          $"mergesTracked={m.MergesTracked} met={m.MergesMet} missed={m.MergesMissed} " +
                          $"avgTimeToMeet={(m.MergesMet > 0 ? m.TotalTimeToMeet / m.MergesMet : 0):0.0} partySplits={m.PartySplits}");
            }

            UpdateRelevancy();
        }

        /// <summary>Adds new players, removes disconnected ones and records which zone everyone is in.</summary>
        private void SyncPlayers()
        {
            _seen.Clear();
            foreach (var (owner, transform, ghost, account) in SystemAPI
                         .Query<RefRO<GhostOwner>, RefRO<LocalTransform>, RefRO<GhostInstance>, RefRO<PlayerAccount>>()
                         .WithAll<NetPlayer>())
            {
                int id = owner.ValueRO.NetworkId;
                var pos = transform.ValueRO.Position;
                var zone = _grid.ZoneAt(pos.x, pos.z);
                _seen.Add(id);
                _posOf[id] = pos;
                _ghostIdOf[id] = ghost.ValueRO.ghostId;

                int group = account.ValueRO.GroupId;
                if (_players.TryGetValue(id, out var player))
                {
                    player.MoveTo(zone);
                    player.GroupId = group;
                }
                else
                {
                    player = new LobbyPlayer(id, zone, group);
                    _players[id] = player;
                    // A party member logs in next to their party, so they join its lobby straight away.
                    var lobby = GroupLobbyWithRoom(group) ?? NewLobby();
                    lobby.Players.Add(player);
                    _lobbyOf[id] = lobby.Id;
                }
            }

            if (_seen.Count == _players.Count) return;

            var gone = new List<int>();
            foreach (var id in _players.Keys)
            {
                if (!_seen.Contains(id)) gone.Add(id);
            }
            foreach (var id in gone)
            {
                var lobby = _lobbies[_lobbyOf[id]];
                lobby.Players.Remove(_players[id]);
                if (lobby.Players.Count == 0)
                {
                    _lobbies.Remove(lobby.Id);
                    _noMergeUntil.Remove(lobby.Id);
                }
                _players.Remove(id);
                _lobbyOf.Remove(id);
                _ghostIdOf.Remove(id);
                _posOf.Remove(id);
            }
        }

        private Lobby GroupLobbyWithRoom(int group)
        {
            if (group == 0) return null;
            foreach (var p in _players.Values)
            {
                if (p.GroupId != group || !_lobbyOf.TryGetValue(p.Id, out int lobbyId)) continue;
                var lobby = _lobbies[lobbyId];
                if (lobby.Players.Count < _rules.MaxPlayers) return lobby;
            }
            return null;
        }

        /// <summary>
        /// Party members always share a lobby. When a party spans lobbies (players teamed up across
        /// lobbies, or a lobby was full when they logged in), the members move to the lobby holding most
        /// of them; if it has no room, the whole party moves to a lobby of its own.
        /// </summary>
        private void GatherGroups(double now)
        {
            var groups = new Dictionary<int, List<LobbyPlayer>>();
            int parties = 0, inParties = 0;
            foreach (var p in _players.Values)
            {
                if (p.GroupId == 0) continue;
                if (!groups.TryGetValue(p.GroupId, out var members))
                {
                    members = new List<LobbyPlayer>();
                    groups[p.GroupId] = members;
                }
                members.Add(p);
            }

            var count = new Dictionary<int, int>();
            foreach (var members in groups.Values)
            {
                if (members.Count >= 2)
                {
                    parties++;
                    inParties += members.Count;
                }
                count.Clear();
                foreach (var p in members)
                {
                    int l = _lobbyOf[p.Id];
                    count[l] = count.TryGetValue(l, out int n) ? n + 1 : 1;
                }
                if (count.Count <= 1) continue;

                int best = -1, bestCount = -1;
                foreach (var kv in count)
                {
                    if (kv.Value > bestCount || (kv.Value == bestCount && kv.Key < best))
                    {
                        best = kv.Key;
                        bestCount = kv.Value;
                    }
                }
                var target = _lobbies[best];
                int moving = members.Count - bestCount;
                if (target.Players.Count + moving > _rules.MaxPlayers)
                {
                    target = NewLobby();
                    _noMergeUntil[target.Id] = now + MergeCooldown;
                }

                int from = 0;
                foreach (var p in members)
                {
                    int old = _lobbyOf[p.Id];
                    if (old == target.Id) continue;
                    from = old;
                    var oldLobby = _lobbies[old];
                    oldLobby.Players.Remove(p);
                    if (oldLobby.Players.Count == 0)
                    {
                        _lobbies.Remove(old);
                        _noMergeUntil.Remove(old);
                    }
                    target.Players.Add(p);
                    _lobbyOf[p.Id] = target.Id;
                    _metrics.RecordPartyMove(new[] { p.Id });
                    LobbyStats.TotalGroupJoins++;
                }
                LobbyStats.Record(new LobbyEvent
                {
                    Kind = LobbyEventKind.Group, LobbyA = target.Id, LobbyB = from,
                    Zone = members[0].Zone, PlayersA = target.Players.Count,
                });
            }
            LobbyStats.Parties = parties;
            LobbyStats.PlayersInParties = inParties;
        }

        private void SplitLobbies(double now)
        {
            foreach (var lobby in new List<Lobby>(_lobbies.Values))
            {
                var clusters = LobbySplitter.FindClusters(lobby, _grid, _rules);
                if (clusters.Count <= 1) continue;

                // The first cluster keeps the lobby; every other cluster gets a new one.
                lobby.Players.Clear();
                lobby.Players.AddRange(clusters[0]);
                _noMergeUntil[lobby.Id] = now + MergeCooldown;
                for (int i = 1; i < clusters.Count; i++)
                {
                    var split = NewLobby();
                    split.Players.AddRange(clusters[i]);
                    _noMergeUntil[split.Id] = now + MergeCooldown;
                    LobbyStats.Record(new LobbyEvent
                    {
                        Kind = LobbyEventKind.Split, LobbyA = lobby.Id, LobbyB = split.Id,
                        Zone = clusters[i][0].Zone, PlayersA = clusters[0].Count, PlayersB = clusters[i].Count,
                    });
                    foreach (var p in clusters[i]) _lobbyOf[p.Id] = split.Id;
                }
                LobbyStats.TotalSplits += clusters.Count - 1;
            }
        }

        private void MergeLobbies(double now)
        {
            var candidates = new List<Lobby>();
            foreach (var lobby in _lobbies.Values)
            {
                if (!_noMergeUntil.TryGetValue(lobby.Id, out double until) || now >= until) candidates.Add(lobby);
            }
            var merges = LobbyMatchmaker.ChooseMerges(candidates, _grid, _rules);
            foreach (var merge in merges)
            {
                var zone = merge.B.Players[0].Zone;
                _metrics.RecordMerge(now, Ids(merge.A), Ids(merge.B));
                foreach (var p in merge.B.Players)
                {
                    merge.A.Players.Add(p);
                    _lobbyOf[p.Id] = merge.A.Id;
                }
                _lobbies.Remove(merge.B.Id);
                _noMergeUntil.Remove(merge.B.Id);
                LobbyStats.Record(new LobbyEvent
                {
                    Kind = LobbyEventKind.Merge, LobbyA = merge.A.Id, LobbyB = merge.B.Id,
                    Zone = zone, PlayersA = merge.A.Players.Count,
                });
                LobbyStats.TotalMerges++;
            }
        }

        /// <summary>Each connection receives exactly the player ghosts in its own lobby, its own included.</summary>
        private void UpdateRelevancy()
        {
            ref var relevancy = ref SystemAPI.GetSingletonRW<GhostRelevancy>().ValueRW;
            relevancy.GhostRelevancyMode = GhostRelevancyMode.SetIsRelevant;
            var set = relevancy.GhostRelevancySet;
            set.Clear();

            foreach (var lobby in _lobbies.Values)
            {
                foreach (var viewer in lobby.Players)
                {
                    foreach (var target in lobby.Players)
                    {
                        if (_ghostIdOf.TryGetValue(target.Id, out int ghostId) && ghostId != 0)
                        {
                            set.TryAdd(new RelevantGhostForConnection(viewer.Id, ghostId), 1);
                        }
                    }
                }
            }
        }

        private static List<int> Ids(Lobby lobby)
        {
            var ids = new List<int>(lobby.Players.Count);
            foreach (var p in lobby.Players) ids.Add(p.Id);
            return ids;
        }

        private void ObserveMetrics(double now)
        {
            _samples.Clear();
            foreach (var p in _players.Values)
            {
                if (!_posOf.TryGetValue(p.Id, out var pos)) continue;
                _samples.Add(new PlayerSample(p.Id, pos.x, pos.z, _lobbyOf[p.Id], p.GroupId));
            }
            _metrics.Observe(now, _samples);
        }

        private void PublishStats()
        {
            int largest = 0;
            foreach (var lobby in _lobbies.Values)
            {
                if (lobby.Players.Count > largest) largest = lobby.Players.Count;
            }
            LobbyStats.Players = _players.Count;
            LobbyStats.Lobbies = _lobbies.Count;
            LobbyStats.AverageSize = _lobbies.Count == 0 ? 0f : (float)_players.Count / _lobbies.Count;
            LobbyStats.LargestLobby = largest;
            LobbyStats.Cost = LobbyCost.Estimate(_lobbies.Values, _grid);
            LobbyStats.LobbyOfPlayer.Clear();
            foreach (var kv in _lobbyOf) LobbyStats.LobbyOfPlayer[kv.Key] = kv.Value;
        }

        private Lobby NewLobby()
        {
            // Single layer and seed for the networking test.
            var lobby = new Lobby(_nextLobbyId++, layer: 1, seed: 1);
            _lobbies[lobby.Id] = lobby;
            return lobby;
        }
    }
}
