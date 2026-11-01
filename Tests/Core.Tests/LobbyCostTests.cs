using FriendlyPersonShooter.Core.Lobbies;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class LobbyCostTests
    {
        private readonly WorldGrid _grid = new WorldGrid(16, 16, 100f);

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
        public void SinglePlayer_LoadsTheirZoneAndNeighbors_WithNoDuplication()
        {
            var report = LobbyCost.Estimate(new[] { LobbyAt(1, new ZoneCoord(5, 5)) }, _grid);
            Assert.That(report.OccupiedInstances, Is.EqualTo(1));
            Assert.That(report.LoadedInstances, Is.EqualTo(9));
            Assert.That(report.DistinctLoadedZones, Is.EqualTo(9));
            Assert.That(report.Duplication, Is.EqualTo(1.0));
            Assert.That(report.LoadedZonesPerPlayer, Is.EqualTo(9.0));
            Assert.That(report.DuplicatedZones, Is.Empty);
        }

        [Test]
        public void DistantLobbies_CostTheSameAsIfTheyWereOneLobby()
        {
            var report = LobbyCost.Estimate(new[]
            {
                LobbyAt(1, new ZoneCoord(2, 2)),
                LobbyAt(2, new ZoneCoord(10, 10)),
            }, _grid);
            Assert.That(report.LoadedInstances, Is.EqualTo(18));
            Assert.That(report.Duplication, Is.EqualTo(1.0));
        }

        [Test]
        public void LobbiesInNeighboringZones_SimulateTheOverlapTwice()
        {
            // Zones (5,5) and (6,5): their 3x3 areas share 6 zones.
            var report = LobbyCost.Estimate(new[]
            {
                LobbyAt(1, new ZoneCoord(5, 5)),
                LobbyAt(2, new ZoneCoord(6, 5)),
            }, _grid);
            Assert.That(report.LoadedInstances, Is.EqualTo(18));
            Assert.That(report.DistinctLoadedZones, Is.EqualTo(12));
            Assert.That(report.Duplication, Is.EqualTo(1.5));
            Assert.That(report.DuplicatedZones.Count, Is.EqualTo(6));
        }

        [Test]
        public void PlayersSideBySideInOneLobby_ShareTheirZones()
        {
            var report = LobbyCost.Estimate(new[] { LobbyAt(1, new ZoneCoord(5, 5), new ZoneCoord(6, 5)) }, _grid);
            Assert.That(report.OccupiedInstances, Is.EqualTo(2));
            Assert.That(report.LoadedInstances, Is.EqualTo(12));
            Assert.That(report.Duplication, Is.EqualTo(1.0));
            Assert.That(report.LoadedZonesPerPlayer, Is.EqualTo(6.0));
        }

        [Test]
        public void NoPlayers_ReportsZeroWithoutDividingByZero()
        {
            var report = LobbyCost.Estimate(new Lobby[0], _grid);
            Assert.That(report.LoadedInstances, Is.EqualTo(0));
            Assert.That(report.Duplication, Is.EqualTo(1.0));
            Assert.That(report.LoadedZonesPerPlayer, Is.EqualTo(0.0));
        }
    }
}
