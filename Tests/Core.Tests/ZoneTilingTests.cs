using System;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class ZoneTilingTests
    {
        private static readonly ZoneTiling Tiling = new ZoneTiling(3, 3, 125f);

        [Test]
        public void HasSixZonesPerCell_OneHexagonThreeSquaresTwoTriangles()
        {
            Assert.That(Tiling.Count, Is.EqualTo(54));
            Assert.That(Tiling.Zones.Count(z => z.Shape == ZoneShape.Hexagon), Is.EqualTo(9));
            Assert.That(Tiling.Zones.Count(z => z.Shape == ZoneShape.Square), Is.EqualTo(27));
            Assert.That(Tiling.Zones.Count(z => z.Shape == ZoneShape.Triangle), Is.EqualTo(18));
            for (int i = 0; i < Tiling.Count; i++) Assert.That(Tiling.IndexOf(Tiling.Zones[i]), Is.EqualTo(i));
        }

        [Test]
        public void Key_RoundTrips()
        {
            foreach (var z in Tiling.Zones) Assert.That(ZoneCoord.FromKey(z.Key), Is.EqualTo(z));
            Assert.That(new ZoneCoord(2, 1).ToString(), Is.EqualTo("2.1"));
            Assert.That(new ZoneCoord(2, 1, 1).ToString(), Is.EqualTo("2.1a"));
            Assert.That(new ZoneCoord(2, 1, 5).ToString(), Is.EqualTo("2.1e"));
        }

        [Test]
        public void ZoneAreas_FillTheWholeWorld()
        {
            float world = Math.Abs(Tiling.PeriodA.X * Tiling.PeriodB.Y - Tiling.PeriodA.Y * Tiling.PeriodB.X);
            float sum = Tiling.Zones.Sum(z => Tiling.Area(z));
            Assert.That(sum, Is.EqualTo(world).Within(world * 1e-4));
            Assert.That(Tiling.AreaShare(new ZoneCoord(0, 0, 1)), Is.EqualTo(0.385f).Within(0.01f));
            Assert.That(Tiling.AreaShare(new ZoneCoord(0, 0, 4)), Is.EqualTo(0.167f).Within(0.01f));
        }

        [Test]
        public void Centres_AreInsideTheirOwnZone_AtTheShapesInnerRadius()
        {
            float s = Tiling.Edge;
            foreach (var z in Tiling.Zones)
            {
                var c = Tiling.Centre(z);
                Assert.That(Tiling.ZoneAt(c), Is.EqualTo(z), z.ToString());
                Assert.That(Tiling.IsCanonical(c), z.ToString());
                float inner = z.Shape == ZoneShape.Hexagon ? s * MathF.Sqrt(3f) / 2f
                    : z.Shape == ZoneShape.Square ? s / 2f
                    : s / (2f * MathF.Sqrt(3f));
                Assert.That(Tiling.EdgeDistance(z, c), Is.EqualTo(inner).Within(0.01f), z.ToString());
            }
        }

        [Test]
        public void Neighbors_MatchTheShape_AndAreSymmetric()
        {
            foreach (var z in Tiling.Zones)
            {
                var n = Tiling.Neighbors(z);
                int expected = z.Shape == ZoneShape.Hexagon ? 6 : z.Shape == ZoneShape.Square ? 4 : 3;
                Assert.That(n.Count, Is.EqualTo(expected), z.ToString());
                foreach (var other in n)
                {
                    Assert.That(Tiling.Neighbors(other), Does.Contain(z));
                    Assert.That(Tiling.Distance(z, other), Is.EqualTo(1));
                    // Squares sit between hexagons and triangles; no two same shapes share an edge.
                    Assert.That(other.Shape, Is.Not.EqualTo(z.Shape));
                }
            }
        }

        [Test]
        public void RandomPoints_WrapIntoTheirZone_AndRepeatEveryPeriod()
        {
            var random = new Random(7);
            for (int i = 0; i < 2000; i++)
            {
                var p = new Vector2((float)(random.NextDouble() * 3000 - 1500), (float)(random.NextDouble() * 3000 - 1500));
                var zone = Tiling.ZoneAt(p);
                Assert.That(Tiling.InRange(zone));
                Assert.That(Tiling.ZoneAt(p + Tiling.PeriodA), Is.EqualTo(zone));
                Assert.That(Tiling.ZoneAt(p - Tiling.PeriodB), Is.EqualTo(zone));
                var w = Tiling.WrapPoint(p);
                Assert.That(Tiling.IsCanonical(w));
                Assert.That(Tiling.ZoneAt(w), Is.EqualTo(zone));
                Assert.That(Tiling.DistanceBetween(p, w), Is.LessThan(0.05f));
                Assert.That(Tiling.EdgeDistance(zone, w), Is.GreaterThanOrEqualTo(-0.05f));
            }
        }

        [Test]
        public void Delta_IsTheShortestCopy()
        {
            var random = new Random(3);
            for (int i = 0; i < 500; i++)
            {
                var a = new Vector2((float)(random.NextDouble() * 1000), (float)(random.NextDouble() * 1000));
                var b = new Vector2((float)(random.NextDouble() * 1000), (float)(random.NextDouble() * 1000));
                float d = Tiling.Delta(a, b).Length();
                for (int m = -2; m <= 2; m++)
                {
                    for (int n = -2; n <= 2; n++)
                    {
                        float other = (b + Tiling.PeriodA * m + Tiling.PeriodB * n - a).Length();
                        Assert.That(d, Is.LessThanOrEqualTo(other + 0.01f));
                    }
                }
            }
        }

        [Test]
        public void EdgeMidpoints_AreSharedWithTheNeighbourAcrossThem()
        {
            foreach (var z in Tiling.Zones)
            {
                var outline = Tiling.Outline(z);
                var c = Tiling.Centre(z);
                for (int i = 0; i < outline.Count; i++)
                {
                    var mid = (outline[i] + outline[(i + 1) % outline.Count]) * 0.5f;
                    var outward = Vector2.Normalize(mid - c);
                    var across = Tiling.ZoneAt(mid + outward * 0.5f);
                    Assert.That(Tiling.ZoneAt(mid - outward * 0.5f), Is.EqualTo(z));
                    Assert.That(Tiling.Neighbors(z), Does.Contain(across));
                }
            }
        }

        [Test]
        public void Neighborhood_GrowsByTouchingZones()
        {
            var hex = new ZoneCoord(1, 1, 0);
            Assert.That(Tiling.Neighborhood(hex, 0), Is.EquivalentTo(new[] { hex }));
            var ring = Tiling.Neighborhood(hex, 1);
            // A hexagon touches six squares on its edges and six triangles at its corners.
            Assert.That(ring.Count, Is.EqualTo(13));
            Assert.That(Tiling.DistanceToZone(Tiling.Centre(hex) + Tiling.PeriodA, hex), Is.EqualTo(0f).Within(1e-3));
        }
    }
}
