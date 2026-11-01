using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>How much zone simulation a set of lobbies needs.</summary>
    public sealed class LobbyCostReport
    {
        /// <summary>Distinct (lobby, zone) pairs where a player stands.</summary>
        public int OccupiedInstances { get; internal set; }

        /// <summary>Distinct (lobby, zone) pairs each lobby keeps loaded: every player's zone and its neighbors.</summary>
        public int LoadedInstances { get; internal set; }

        public int DistinctOccupiedZones { get; internal set; }
        public int DistinctLoadedZones { get; internal set; }

        /// <summary>Loaded zone copies per distinct loaded zone. 1.0 means no zone is simulated twice.</summary>
        public double Duplication { get; internal set; } = 1.0;

        public double LoadedZonesPerPlayer { get; internal set; }

        /// <summary>Zones loaded by two or more lobbies, so simulated more than once.</summary>
        public List<ZoneCoord> DuplicatedZones { get; } = new List<ZoneCoord>();
    }

    /// <summary>
    /// Estimates server cost from lobbies. Each lobby simulates its own copy of the zones its players
    /// have loaded, and enemies don't scale with players, so cost follows zone copies, not lobby count.
    /// Lobbies only add cost when they load the same zones.
    /// </summary>
    public static class LobbyCost
    {
        /// <summary>How many zones out from a player stay loaded (1 = their zone plus the ring around it).</summary>
        public const int LoadRadius = 1;

        public static LobbyCostReport Estimate(IEnumerable<Lobby> lobbies, WorldGrid grid)
        {
            if (lobbies == null) throw new ArgumentNullException(nameof(lobbies));
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            var report = new LobbyCostReport();
            var occupiedZones = new HashSet<ZoneCoord>();
            var loadCount = new Dictionary<ZoneCoord, int>();
            var lobbyOccupied = new HashSet<ZoneCoord>();
            var lobbyLoaded = new HashSet<ZoneCoord>();
            int players = 0;

            foreach (var lobby in lobbies)
            {
                lobbyOccupied.Clear();
                lobbyLoaded.Clear();
                foreach (var player in lobby.Players)
                {
                    players++;
                    lobbyOccupied.Add(player.Zone);
                    lobbyLoaded.UnionWith(grid.Neighborhood(player.Zone, LoadRadius));
                }

                report.OccupiedInstances += lobbyOccupied.Count;
                report.LoadedInstances += lobbyLoaded.Count;
                occupiedZones.UnionWith(lobbyOccupied);
                foreach (var zone in lobbyLoaded)
                {
                    loadCount[zone] = loadCount.TryGetValue(zone, out int n) ? n + 1 : 1;
                }
            }

            report.DistinctOccupiedZones = occupiedZones.Count;
            report.DistinctLoadedZones = loadCount.Count;
            if (loadCount.Count > 0) report.Duplication = (double)report.LoadedInstances / loadCount.Count;
            if (players > 0) report.LoadedZonesPerPlayer = (double)report.LoadedInstances / players;
            foreach (var kv in loadCount)
            {
                if (kv.Value > 1) report.DuplicatedZones.Add(kv.Key);
            }
            return report;
        }
    }
}
