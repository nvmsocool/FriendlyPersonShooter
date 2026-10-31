using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>
    /// Decides whether a lobby has spread out enough to split, and how.
    /// Players are linked when they're within <see cref="LobbyRules.SplitDistance"/> zones of each other,
    /// and group members are always linked. Each connected cluster becomes its own lobby.
    /// </summary>
    public static class LobbySplitter
    {
        /// <summary>
        /// Returns the clusters the lobby's players fall into. One cluster means no split is needed.
        /// Clusters are ordered by their lowest player id so results are stable.
        /// </summary>
        public static List<List<LobbyPlayer>> FindClusters(Lobby lobby, WorldGrid grid, LobbyRules rules)
        {
            if (lobby == null) throw new ArgumentNullException(nameof(lobby));
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var players = lobby.Players;
            var parent = new int[players.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            for (int i = 0; i < players.Count; i++)
            {
                for (int j = i + 1; j < players.Count; j++)
                {
                    bool sameGroup = players[i].GroupId != 0 && players[i].GroupId == players[j].GroupId;
                    bool close = grid.Distance(players[i].Zone, players[j].Zone) <= rules.SplitDistance;
                    if (sameGroup || close) Union(parent, i, j);
                }
            }

            var byRoot = new Dictionary<int, List<LobbyPlayer>>();
            for (int i = 0; i < players.Count; i++)
            {
                int root = Find(parent, i);
                if (!byRoot.TryGetValue(root, out var cluster))
                {
                    cluster = new List<LobbyPlayer>();
                    byRoot[root] = cluster;
                }
                cluster.Add(players[i]);
            }

            var clusters = new List<List<LobbyPlayer>>(byRoot.Values);
            clusters.Sort((a, b) => MinId(a).CompareTo(MinId(b)));
            return clusters;
        }

        public static bool NeedsSplit(Lobby lobby, WorldGrid grid, LobbyRules rules)
        {
            return FindClusters(lobby, grid, rules).Count > 1;
        }

        private static int MinId(List<LobbyPlayer> cluster)
        {
            int min = int.MaxValue;
            foreach (var p in cluster) min = Math.Min(min, p.Id);
            return min;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }
    }
}
