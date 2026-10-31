using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>A pair of lobbies chosen to merge.</summary>
    public readonly struct MergePair
    {
        public readonly Lobby A;
        public readonly Lobby B;
        public readonly double Score;

        public MergePair(Lobby a, Lobby b, double score)
        {
            A = a;
            B = b;
            Score = score;
        }
    }

    /// <summary>
    /// Picks which lobbies merge this round. Every allowed pair is scored, then pairs are taken
    /// best first, and each lobby merges at most once per round.
    /// </summary>
    public static class LobbyMatchmaker
    {
        public static List<MergePair> ChooseMerges(IReadOnlyList<Lobby> lobbies, WorldGrid grid, LobbyRules rules)
        {
            if (lobbies == null) throw new ArgumentNullException(nameof(lobbies));

            var candidates = new List<MergePair>();
            for (int i = 0; i < lobbies.Count; i++)
            {
                for (int j = i + 1; j < lobbies.Count; j++)
                {
                    var eval = MergeScorer.Evaluate(lobbies[i], lobbies[j], grid, rules);
                    if (eval.Allowed && eval.Score >= rules.MinMergeScore)
                    {
                        candidates.Add(new MergePair(lobbies[i], lobbies[j], eval.Score));
                    }
                }
            }

            // Best score first; ties broken by lobby ids so results are repeatable.
            candidates.Sort((x, y) =>
            {
                int byScore = y.Score.CompareTo(x.Score);
                if (byScore != 0) return byScore;
                int byA = x.A.Id.CompareTo(y.A.Id);
                return byA != 0 ? byA : x.B.Id.CompareTo(y.B.Id);
            });

            var used = new HashSet<int>();
            var chosen = new List<MergePair>();
            foreach (var pair in candidates)
            {
                if (used.Contains(pair.A.Id) || used.Contains(pair.B.Id)) continue;
                used.Add(pair.A.Id);
                used.Add(pair.B.Id);
                chosen.Add(pair);
            }
            return chosen;
        }
    }
}
