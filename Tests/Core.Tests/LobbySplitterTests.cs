using System.Linq;
using FriendlyPersonShooter.Core.Lobbies;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class LobbySplitterTests
    {
        private readonly WorldGrid _grid = new WorldGrid(16, 16, 100f);
        private readonly LobbyRules _rules = new LobbyRules();

        [Test]
        public void PlayersWithinThreeZones_StayTogether()
        {
            var lobby = new Lobby(1, 1, 7, new[]
            {
                new LobbyPlayer(1, new ZoneCoord(0, 0)),
                new LobbyPlayer(2, new ZoneCoord(3, 3)),
            });
            Assert.That(LobbySplitter.NeedsSplit(lobby, _grid, _rules), Is.False);
        }

        [Test]
        public void PlayerMoreThanThreeZonesAway_SplitsOff()
        {
            var lobby = new Lobby(1, 1, 7, new[]
            {
                new LobbyPlayer(1, new ZoneCoord(0, 0)),
                new LobbyPlayer(2, new ZoneCoord(1, 0)),
                new LobbyPlayer(3, new ZoneCoord(6, 0)),
            });
            var clusters = LobbySplitter.FindClusters(lobby, _grid, _rules);
            Assert.That(clusters.Count, Is.EqualTo(2));
            Assert.That(clusters[0].Select(p => p.Id), Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(clusters[1].Select(p => p.Id), Is.EquivalentTo(new[] { 3 }));
        }

        [Test]
        public void DistanceIsMeasuredAcrossTheWrap()
        {
            var lobby = new Lobby(1, 1, 7, new[]
            {
                new LobbyPlayer(1, new ZoneCoord(0, 0)),
                new LobbyPlayer(2, new ZoneCoord(14, 0)),
            });
            Assert.That(LobbySplitter.NeedsSplit(lobby, _grid, _rules), Is.False);
        }

        [Test]
        public void GroupMembers_NeverSplit()
        {
            var lobby = new Lobby(1, 1, 7, new[]
            {
                new LobbyPlayer(1, new ZoneCoord(0, 0), groupId: 5),
                new LobbyPlayer(2, new ZoneCoord(8, 8), groupId: 5),
            });
            Assert.That(LobbySplitter.NeedsSplit(lobby, _grid, _rules), Is.False);
        }

        [Test]
        public void AFarGroupMember_PullsNearbyStrangersAlong()
        {
            // Player 3 is a stranger near player 2; player 2 is grouped with far-away player 1.
            var lobby = new Lobby(1, 1, 7, new[]
            {
                new LobbyPlayer(1, new ZoneCoord(0, 0), groupId: 5),
                new LobbyPlayer(2, new ZoneCoord(8, 8), groupId: 5),
                new LobbyPlayer(3, new ZoneCoord(9, 8)),
            });
            Assert.That(LobbySplitter.FindClusters(lobby, _grid, _rules).Count, Is.EqualTo(1));
        }

        [Test]
        public void ChainOfClosePlayers_StaysOneLobby()
        {
            var lobby = new Lobby(1, 1, 7, new[]
            {
                new LobbyPlayer(1, new ZoneCoord(0, 0)),
                new LobbyPlayer(2, new ZoneCoord(3, 0)),
                new LobbyPlayer(3, new ZoneCoord(6, 0)),
            });
            Assert.That(LobbySplitter.NeedsSplit(lobby, _grid, _rules), Is.False);
        }
    }
}
