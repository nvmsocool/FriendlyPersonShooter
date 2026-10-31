using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>The verdict on merging two lobbies.</summary>
    public readonly struct MergeEvaluation
    {
        public readonly bool Allowed;
        public readonly double Score;

        /// <summary>Why the merge isn't allowed, or null when it is.</summary>
        public readonly string RejectReason;

        private MergeEvaluation(bool allowed, double score, string rejectReason)
        {
            Allowed = allowed;
            Score = score;
            RejectReason = rejectReason;
        }

        public static MergeEvaluation Reject(string reason) => new MergeEvaluation(false, 0, reason);
        public static MergeEvaluation Accept(double score) => new MergeEvaluation(true, score, null);
    }

    /// <summary>
    /// Scores how likely two lobbies' players are to run into each other.
    /// Hard rules (same layer and seed, size cap, blocks, safe distance) reject a pair outright.
    /// Otherwise the score rewards lobbies that are close and that each sit in zones the other
    /// hasn't explored yet, as in the design doc's "zone 11 / zone 13" example.
    /// </summary>
    public static class MergeScorer
    {
        public static MergeEvaluation Evaluate(Lobby a, Lobby b, WorldGrid grid, LobbyRules rules)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            if (a.Id == b.Id) return MergeEvaluation.Reject("same lobby");
            if (a.Players.Count == 0 || b.Players.Count == 0) return MergeEvaluation.Reject("empty lobby");
            if (a.Layer != b.Layer) return MergeEvaluation.Reject("different layer");
            if (a.Seed != b.Seed) return MergeEvaluation.Reject("different seed");
            if (a.Players.Count + b.Players.Count > rules.MaxPlayers) return MergeEvaluation.Reject("too many players");
            if (HasBlock(a, b)) return MergeEvaluation.Reject("blocked player");

            // A merged lobby farther apart than the split distance would split on the next pass.
            int maxDistance = Math.Min(rules.MaxMergeDistance, rules.SplitDistance);
            int minDistance = MinDistance(a, b, grid);
            if (minDistance < rules.MinMergeGap) return MergeEvaluation.Reject("too close");
            if (minDistance > maxDistance) return MergeEvaluation.Reject("too far");

            // 1.0 at the minimum gap, falling toward 0 at the maximum distance.
            int span = Math.Max(1, maxDistance - rules.MinMergeGap + 1);
            double proximity = (double)(maxDistance - minDistance + 1) / span;

            // Share of each lobby's occupied zones that the other lobby hasn't visited, averaged.
            double discovery = (Novelty(b, a) + Novelty(a, b)) / 2.0;

            double score = rules.ProximityWeight * proximity + rules.DiscoveryWeight * discovery;
            if (a.Players.Count <= rules.SmallLobbySize || b.Players.Count <= rules.SmallLobbySize)
            {
                score += rules.SmallLobbyBonus;
            }

            return MergeEvaluation.Accept(score);
        }

        /// <summary>Fraction of the zones <paramref name="occupier"/> stands in that no player in <paramref name="explorer"/> has visited.</summary>
        private static double Novelty(Lobby occupier, Lobby explorer)
        {
            var visited = new HashSet<ZoneCoord>();
            foreach (var p in explorer.Players) visited.UnionWith(p.VisitedZones);

            var occupied = new HashSet<ZoneCoord>();
            foreach (var p in occupier.Players) occupied.Add(p.Zone);

            int fresh = 0;
            foreach (var zone in occupied)
            {
                if (!visited.Contains(zone)) fresh++;
            }
            return (double)fresh / occupied.Count;
        }

        private static int MinDistance(Lobby a, Lobby b, WorldGrid grid)
        {
            int min = int.MaxValue;
            foreach (var pa in a.Players)
            {
                foreach (var pb in b.Players)
                {
                    min = Math.Min(min, grid.Distance(pa.Zone, pb.Zone));
                }
            }
            return min;
        }

        private static bool HasBlock(Lobby a, Lobby b)
        {
            foreach (var pa in a.Players)
            {
                foreach (var pb in b.Players)
                {
                    if (pa.BlockedPlayers.Contains(pb.Id) || pb.BlockedPlayers.Contains(pa.Id)) return true;
                }
            }
            return false;
        }
    }
}
