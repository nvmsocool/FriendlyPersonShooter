using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>One player's state at a lobby pass, for <see cref="LobbyMetrics"/>.</summary>
    public struct PlayerSample
    {
        public int Id;
        public double X;
        public double Z;
        public int LobbyId;
        public int GroupId;

        public PlayerSample(int id, double x, double z, int lobbyId, int groupId = 0)
        {
            Id = id;
            X = x;
            Z = z;
            LobbyId = lobbyId;
            GroupId = groupId;
        }
    }

    /// <summary>
    /// Measures the networking test's pass criteria from lobby passes:
    /// - Pop-ins: a player becomes visible to another player who is close enough to notice.
    ///   Counted by cause: a lobby change (merge), a party move, or the player logging in.
    /// - Encounters: after a merge, whether players from the two sides actually meet in time.
    /// - Party splits: a party spread over two lobbies, which should never happen.
    /// Distances are measured the short way around the wrapping world.
    /// </summary>
    public sealed class LobbyMetrics
    {
        /// <summary>Players closer than this see each other appear (metres).</summary>
        public double ViewDistance { get; set; } = 30;

        /// <summary>Players from the two sides of a merge count as met within this distance (metres).</summary>
        public double EncounterDistance { get; set; } = 30;

        /// <summary>Seconds after a merge for its sides to meet before it counts as a miss.</summary>
        public double EncounterWindow { get; set; } = 30;

        public int MergePopIns { get; private set; }
        public int PartyPopIns { get; private set; }
        public int LoginPopIns { get; private set; }
        public int MergesTracked { get; private set; }
        public int MergesMet { get; private set; }
        public int MergesMissed { get; private set; }
        /// <summary>Lobby passes that found a party spread over more than one lobby.</summary>
        public int PartySplits { get; private set; }

        /// <summary>Seconds from merge to meeting, for merges that met.</summary>
        public double TotalTimeToMeet { get; private set; }

        private readonly double _worldWidth;
        private readonly double _worldHeight;
        private readonly Dictionary<int, PlayerSample> _previous = new Dictionary<int, PlayerSample>();
        private readonly Dictionary<int, PlayerSample> _current = new Dictionary<int, PlayerSample>();
        private readonly HashSet<int> _partyMoved = new HashSet<int>();
        private readonly List<PendingMerge> _pending = new List<PendingMerge>();

        private sealed class PendingMerge
        {
            public double Time;
            public int[] SideA;
            public int[] SideB;
        }

        public LobbyMetrics(double worldWidth, double worldHeight)
        {
            if (worldWidth <= 0) throw new ArgumentOutOfRangeException(nameof(worldWidth));
            if (worldHeight <= 0) throw new ArgumentOutOfRangeException(nameof(worldHeight));
            _worldWidth = worldWidth;
            _worldHeight = worldHeight;
        }

        /// <summary>Two lobbies merged: <paramref name="sideA"/> and <paramref name="sideB"/> are their player ids.</summary>
        public void RecordMerge(double now, IEnumerable<int> sideA, IEnumerable<int> sideB)
        {
            _pending.Add(new PendingMerge { Time = now, SideA = new List<int>(sideA).ToArray(), SideB = new List<int>(sideB).ToArray() });
            MergesTracked++;
        }

        /// <summary>These players changed lobby to join their party this pass.</summary>
        public void RecordPartyMove(IEnumerable<int> playerIds)
        {
            _partyMoved.UnionWith(playerIds);
        }

        /// <summary>Compares this pass with the previous one and updates the totals.</summary>
        public void Observe(double now, IReadOnlyList<PlayerSample> players)
        {
            _current.Clear();
            var byLobby = new Dictionary<int, List<PlayerSample>>();
            var lobbyOfGroup = new Dictionary<int, int>();
            var splitGroups = new HashSet<int>();
            foreach (var p in players)
            {
                _current[p.Id] = p;
                if (!byLobby.TryGetValue(p.LobbyId, out var list))
                {
                    list = new List<PlayerSample>();
                    byLobby[p.LobbyId] = list;
                }
                list.Add(p);

                if (p.GroupId == 0) continue;
                if (lobbyOfGroup.TryGetValue(p.GroupId, out int lobby))
                {
                    if (lobby != p.LobbyId) splitGroups.Add(p.GroupId);
                }
                else
                {
                    lobbyOfGroup[p.GroupId] = p.LobbyId;
                }
            }
            if (splitGroups.Count > 0) PartySplits++;

            foreach (var lobby in byLobby.Values)
            {
                foreach (var viewer in lobby)
                {
                    // A player who just logged in sees the world load around them; that's not a pop-in.
                    if (!_previous.TryGetValue(viewer.Id, out var viewerBefore)) continue;
                    foreach (var target in lobby)
                    {
                        if (target.Id == viewer.Id) continue;
                        if (Distance(viewer, target) > ViewDistance) continue;
                        if (!_previous.TryGetValue(target.Id, out var targetBefore))
                        {
                            LoginPopIns++;
                        }
                        else if (targetBefore.LobbyId != viewerBefore.LobbyId)
                        {
                            if (_partyMoved.Contains(target.Id) || _partyMoved.Contains(viewer.Id)) PartyPopIns++;
                            else MergePopIns++;
                        }
                    }
                }
            }

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var merge = _pending[i];
                if (SidesMet(merge))
                {
                    MergesMet++;
                    TotalTimeToMeet += now - merge.Time;
                    _pending.RemoveAt(i);
                }
                else if (now - merge.Time > EncounterWindow || !SidesStillTogether(merge))
                {
                    MergesMissed++;
                    _pending.RemoveAt(i);
                }
            }

            _previous.Clear();
            foreach (var kv in _current) _previous[kv.Key] = kv.Value;
            _partyMoved.Clear();
        }

        private bool SidesMet(PendingMerge merge)
        {
            foreach (int a in merge.SideA)
            {
                if (!_current.TryGetValue(a, out var pa)) continue;
                foreach (int b in merge.SideB)
                {
                    if (!_current.TryGetValue(b, out var pb)) continue;
                    if (pa.LobbyId == pb.LobbyId && Distance(pa, pb) <= EncounterDistance) return true;
                }
            }
            return false;
        }

        /// <summary>False once no player from one side shares a lobby with any player from the other.</summary>
        private bool SidesStillTogether(PendingMerge merge)
        {
            foreach (int a in merge.SideA)
            {
                if (!_current.TryGetValue(a, out var pa)) continue;
                foreach (int b in merge.SideB)
                {
                    if (_current.TryGetValue(b, out var pb) && pa.LobbyId == pb.LobbyId) return true;
                }
            }
            return false;
        }

        private double Distance(PlayerSample a, PlayerSample b)
        {
            double dx = Math.Abs(a.X - b.X) % _worldWidth;
            double dz = Math.Abs(a.Z - b.Z) % _worldHeight;
            dx = Math.Min(dx, _worldWidth - dx);
            dz = Math.Min(dz, _worldHeight - dz);
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
