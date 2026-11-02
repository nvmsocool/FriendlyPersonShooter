using FriendlyPersonShooter.Core.Lobbies;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class MatchmakingTests
    {
        private readonly WorldGrid _grid = new WorldGrid(16, 16, 100f);
        private readonly LobbyRules _rules = new LobbyRules();

        private static Lobby LobbyAt(int id, params ZoneCoord[] zones)
        {
            var lobby = new Lobby(id, 1, 7);
            for (int i = 0; i < zones.Length; i++)
            {
                lobby.Players.Add(new LobbyPlayer(id * 100 + i, zones[i]));
            }
            return lobby;
        }

        [Test]
        public void LobbiesInEachOthersUnvisitedZones_ScoreHigherThanOnesThatWereThere()
        {
            // Fresh pair: neither lobby has been where the other is.
            var a = LobbyAt(1, new ZoneCoord(2, 2));
            var b = LobbyAt(2, new ZoneCoord(4, 2));

            // Stale pair: same positions, but lobby D already explored C's zone and vice versa.
            var c = LobbyAt(3, new ZoneCoord(2, 2));
            var d = LobbyAt(4, new ZoneCoord(4, 2));
            c.Players[0].VisitedZones.Add(new ZoneCoord(4, 2));
            d.Players[0].VisitedZones.Add(new ZoneCoord(2, 2));

            var fresh = MergeScorer.Evaluate(a, b, _grid, _rules);
            var stale = MergeScorer.Evaluate(c, d, _grid, _rules);

            Assert.That(fresh.Allowed && stale.Allowed, Is.True);
            Assert.That(fresh.Score, Is.GreaterThan(stale.Score));
        }

        [Test]
        public void CloserLobbies_ScoreHigher()
        {
            var a = LobbyAt(1, new ZoneCoord(2, 2));
            var near = LobbyAt(2, new ZoneCoord(4, 2));
            var far = LobbyAt(3, new ZoneCoord(5, 2));
            Assert.That(MergeScorer.Evaluate(a, near, _grid, _rules).Score,
                Is.GreaterThan(MergeScorer.Evaluate(a, far, _grid, _rules).Score));
        }

        [TestCase(0, "too close")]
        [TestCase(1, "too close")]
        [TestCase(4, "too far")]
        [TestCase(6, "too far")]
        public void DistanceLimits_RejectMerges(int dx, string reason)
        {
            var a = LobbyAt(1, new ZoneCoord(2, 2));
            var b = LobbyAt(2, new ZoneCoord(2 + dx, 2));
            var eval = MergeScorer.Evaluate(a, b, _grid, _rules);
            Assert.That(eval.Allowed, Is.False);
            Assert.That(eval.RejectReason, Is.EqualTo(reason));
        }

        [Test]
        public void MergeThatWouldSplitAgain_IsRejected_EvenWithLargeMergeRange()
        {
            // Lobbies 4 zones apart would split on the next pass, so merging them only causes churn.
            var rules = new LobbyRules { MaxMergeDistance = 6 };
            var a = LobbyAt(1, new ZoneCoord(2, 2));
            var b = LobbyAt(2, new ZoneCoord(6, 2));
            var eval = MergeScorer.Evaluate(a, b, _grid, rules);
            Assert.That(eval.Allowed, Is.False);
            Assert.That(eval.RejectReason, Is.EqualTo("too far"));
        }

        [Test]
        public void MergeThatWouldExceedSixteenPlayers_IsRejected()
        {
            var a = new Lobby(1, 1, 7);
            var b = new Lobby(2, 1, 7);
            for (int i = 0; i < 9; i++)
            {
                a.Players.Add(new LobbyPlayer(i, new ZoneCoord(0, 0)));
                b.Players.Add(new LobbyPlayer(100 + i, new ZoneCoord(3, 0)));
            }
            Assert.That(MergeScorer.Evaluate(a, b, _grid, _rules).RejectReason, Is.EqualTo("too many players"));
        }

        [Test]
        public void BlockedPlayers_NeverMerge()
        {
            var a = LobbyAt(1, new ZoneCoord(2, 2));
            var b = LobbyAt(2, new ZoneCoord(4, 2));
            b.Players[0].BlockedPlayers.Add(a.Players[0].Id);
            Assert.That(MergeScorer.Evaluate(a, b, _grid, _rules).RejectReason, Is.EqualTo("blocked player"));
        }

        [Test]
        public void DifferentLayersOrSeeds_NeverMerge()
        {
            var a = new Lobby(1, 1, 7, new[] { new LobbyPlayer(1, new ZoneCoord(0, 0)) });
            var otherLayer = new Lobby(2, 2, 7, new[] { new LobbyPlayer(2, new ZoneCoord(2, 0)) });
            var otherSeed = new Lobby(3, 1, 8, new[] { new LobbyPlayer(3, new ZoneCoord(2, 0)) });
            Assert.That(MergeScorer.Evaluate(a, otherLayer, _grid, _rules).RejectReason, Is.EqualTo("different layer"));
            Assert.That(MergeScorer.Evaluate(a, otherSeed, _grid, _rules).RejectReason, Is.EqualTo("different seed"));
        }

        [Test]
        public void Matchmaker_PairsEachLobbyAtMostOnce_BestFirst()
        {
            // B sits between A and C; A-B is the best pair, so C is left waiting.
            var a = LobbyAt(1, new ZoneCoord(0, 0));
            var b = LobbyAt(2, new ZoneCoord(2, 0));
            var c = LobbyAt(3, new ZoneCoord(5, 0));
            var merges = LobbyMatchmaker.ChooseMerges(new[] { a, b, c }, _grid, _rules);
            Assert.That(merges.Count, Is.EqualTo(1));
            Assert.That(merges[0].A.Id, Is.EqualTo(1));
            Assert.That(merges[0].B.Id, Is.EqualTo(2));
        }

        [Test]
        public void Matchmaker_FormsSeveralIndependentPairs()
        {
            var a = LobbyAt(1, new ZoneCoord(0, 0));
            var b = LobbyAt(2, new ZoneCoord(2, 0));
            var c = LobbyAt(3, new ZoneCoord(8, 8));
            var d = LobbyAt(4, new ZoneCoord(10, 8));
            var merges = LobbyMatchmaker.ChooseMerges(new[] { a, b, c, d }, _grid, _rules);
            Assert.That(merges.Count, Is.EqualTo(2));
        }

        private static Lobby Walker(int id, int zoneX, double x, double vx)
        {
            var lobby = LobbyAt(id, new ZoneCoord(zoneX, 2));
            var p = lobby.Players[0];
            p.X = x;
            p.Z = 250;
            p.VelocityX = vx;
            return lobby;
        }

        [Test]
        public void LobbiesHeadingTowardEachOther_ScoreHigherThanOnesMovingApart()
        {
            // Zones 2 and 4 on a grid of 100 m zones: players at x=250 and x=450.
            var closing = MergeScorer.Evaluate(Walker(1, 2, 250, 6), Walker(2, 4, 450, -6), _grid, _rules);
            var standing = MergeScorer.Evaluate(Walker(3, 2, 250, 0), Walker(4, 4, 450, 0), _grid, _rules);
            var parting = MergeScorer.Evaluate(Walker(5, 2, 250, -6), Walker(6, 4, 450, 6), _grid, _rules);
            Assert.That(closing.Score, Is.GreaterThan(standing.Score));
            Assert.That(standing.Score, Is.GreaterThan(parting.Score));
            Assert.That(closing.Score - standing.Score, Is.EqualTo(_rules.ApproachWeight).Within(1e-9));
        }

        [Test]
        public void Approach_IsMeasuredTheShortWayAroundTheWrap()
        {
            // World is 1600 m wide: x=1550 heading +x is closing on x=50, 100 m away across the edge.
            var a = Walker(1, 15, 1550, 6);
            var b = Walker(2, 0, 50, 0);
            Assert.That(MergeScorer.Approach(a, b, _grid, _rules), Is.EqualTo(1.0).Within(1e-9));
        }
    }
}