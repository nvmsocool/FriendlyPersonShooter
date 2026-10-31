using System.Linq;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class WorldGridTests
    {
        [Test]
        public void Distance_CountsDiagonalsAsOneStep()
        {
            var grid = new WorldGrid(10, 10, 100f);
            Assert.That(grid.Distance(new ZoneCoord(2, 2), new ZoneCoord(4, 3)), Is.EqualTo(2));
            Assert.That(grid.Distance(new ZoneCoord(2, 2), new ZoneCoord(2, 2)), Is.EqualTo(0));
        }

        [Test]
        public void Distance_GoesTheShortWayAroundTheWrap()
        {
            var grid = new WorldGrid(10, 8, 100f);
            Assert.That(grid.Distance(new ZoneCoord(0, 0), new ZoneCoord(9, 0)), Is.EqualTo(1));
            Assert.That(grid.Distance(new ZoneCoord(0, 0), new ZoneCoord(0, 7)), Is.EqualTo(1));
            Assert.That(grid.Distance(new ZoneCoord(1, 1), new ZoneCoord(8, 6)), Is.EqualTo(3));
        }

        [Test]
        public void Wrap_HandlesNegativeAndOverflowingCoordinates()
        {
            var grid = new WorldGrid(5, 4, 100f);
            Assert.That(grid.Wrap(-1, -1), Is.EqualTo(new ZoneCoord(4, 3)));
            Assert.That(grid.Wrap(5, 9), Is.EqualTo(new ZoneCoord(0, 1)));
        }

        [Test]
        public void ZoneAt_MapsWorldPositionsAndWraps()
        {
            var grid = new WorldGrid(3, 3, 100f);
            Assert.That(grid.ZoneAt(50, 150), Is.EqualTo(new ZoneCoord(0, 1)));
            Assert.That(grid.ZoneAt(-1, 0), Is.EqualTo(new ZoneCoord(2, 0)));
            Assert.That(grid.ZoneAt(310, 0), Is.EqualTo(new ZoneCoord(0, 0)));
        }

        [Test]
        public void Neighborhood_HasNoDuplicatesOnTinyGrids()
        {
            var grid = new WorldGrid(3, 3, 100f);
            var zones = grid.Neighborhood(new ZoneCoord(0, 0), 2);
            Assert.That(zones.Count, Is.EqualTo(9));
            Assert.That(zones.Distinct().Count(), Is.EqualTo(9));
        }

        [Test]
        public void Neighborhood_RadiusOneIsNineZones()
        {
            var grid = new WorldGrid(8, 8, 100f);
            Assert.That(grid.Neighborhood(new ZoneCoord(0, 0), 1).Count, Is.EqualTo(9));
        }
    }
}
