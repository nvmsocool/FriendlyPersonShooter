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
        /// <summary>Lobby id for each network id, so a client view can show its own lobby.</summary>
        public static readonly Dictionary<int, int> LobbyOfPlayer = new Dictionary<int, int>();
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
        }

        protected override void OnUpdate()
        {
            SyncPlayers();

            double now = SystemAPI.Time.ElapsedTime;
            if (now >= _nextLobbyPass)
            {
                _nextLobbyPass = now + NetTestSettings.LobbyInterval;
                SplitLobbies(now);
                MergeLobbies(now);
                PublishStats();
            }

            if (now >= _nextLog)
            {
                _nextLog = now + 5.0;
                Debug.Log($"[Lobbies] players={LobbyStats.Players} lobbies={LobbyStats.Lobbies} " +
                          $"avg={LobbyStats.AverageSize:0.0} max={LobbyStats.LargestLobby} " +
                          $"splits={LobbyStats.TotalSplits} merges={LobbyStats.TotalMerges}");
            }

            UpdateRelevancy();
        }

        /// <summary>Adds new players, removes disconnected ones and records which zone everyone is in.</summary>
        private void SyncPlayers()
        {
            _seen.Clear();
            foreach (var (owner, transform, ghost) in SystemAPI
                         .Query<RefRO<GhostOwner>, RefRO<LocalTransform>, RefRO<GhostInstance>>()
                         .WithAll<NetPlayer>())
            {
                int id = owner.ValueRO.NetworkId;
                var pos = transform.ValueRO.Position;
                var zone = _grid.ZoneAt(pos.x, pos.z);
                _seen.Add(id);
                _ghostIdOf[id] = ghost.ValueRO.ghostId;

                if (_players.TryGetValue(id, out var player))
                {
                    player.MoveTo(zone);
                }
                else
                {
                    player = new LobbyPlayer(id, zone);
                    _players[id] = player;
                    var lobby = NewLobby();
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
            }
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
                foreach (var p in merge.B.Players)
                {
                    merge.A.Players.Add(p);
                    _lobbyOf[p.Id] = merge.A.Id;
                }
                _lobbies.Remove(merge.B.Id);
                _noMergeUntil.Remove(merge.B.Id);
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
