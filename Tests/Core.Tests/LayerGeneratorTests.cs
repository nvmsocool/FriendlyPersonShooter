using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class LayerGeneratorTests
    {
        private static string Fingerprint(LayerMap map)
        {
            var parts = new List<string> { map.StartZone.ToString(), map.StartPoint.ToString() };
            foreach (var z in map.Zones)
            {
                parts.Add($"{z.Coord}|{z.Tile}|{z.Bias}|{z.Bias.SecondaryShare}");
                parts.AddRange(z.Landmarks.Select(l => $"{l.Name}@{l.Point}"));
                parts.AddRange(z.Shrines.Select(s => $"S{s.Id}@{s.Point}"));
                parts.AddRange(z.SpawnPoints.Select(p => $"{p.Family}@{p.Point}"));
            }
            parts.AddRange(map.Gates.Select(g => $"G@{g.Point}"));
            return string.Join("\n", parts);
        }

        [Test]
        public void SameSeed_GivesIdenticalMap()
        {
            var grid = new WorldGrid(6, 5, 200f);
            var a = LayerGenerator.Generate(1234, grid);
            var b = LayerGenerator.Generate(1234, new WorldGrid(6, 5, 200f));
            Assert.That(Fingerprint(a), Is.EqualTo(Fingerprint(b)));
        }

        [Test]
        public void DifferentSeed_GivesDifferentMap()
        {
            var grid = new WorldGrid(6, 5, 200f);
            Assert.That(Fingerprint(LayerGenerator.Generate(1, grid)), Is.Not.EqualTo(Fingerprint(LayerGenerator.Generate(2, grid))));
        }

        [Test]
        public void Gate_IsHalfTheGridAwayFromStart_CountingTheWrap([Range(0u, 30u)] uint seed)
        {
            var grid = LayerSettings.MvpGrid();
            var map = LayerGenerator.Generate(seed, grid, LayerSettings.ForMvp());
            Assert.That(map.Gates.Count, Is.EqualTo(1));
            var gateZone = map.Gates[0].Point.Zone;
            Assert.That(grid.Distance(map.StartZone, gateZone), Is.EqualTo(2));
            Assert.That(map.Zone(gateZone).HasGate);
            Assert.That(map.Zones.Count(z => z.HasGate), Is.EqualTo(1));
        }

        [Test]
        public void MinGateDistance_UsesTheWrappedFarthestZone()
        {
            var settings = new LayerSettings();
            Assert.That(LayerGenerator.MinGateDistance(new WorldGrid(8, 8, 100f), settings), Is.EqualTo(4));
            Assert.That(LayerGenerator.MinGateDistance(new WorldGrid(5, 5, 100f), settings), Is.EqualTo(2));
            settings.GateDistanceShare = 0.5f;
            Assert.That(LayerGenerator.MinGateDistance(new WorldGrid(8, 8, 100f), settings), Is.EqualTo(2));
        }

        [Test]
        public void Gate_OnAWrappedGrid_IsNeverInAZoneThatLooksFarButIsAcrossTheEdge()
        {
            // On 8x8 a gate at x distance 7 is really 1 zone away; the generator must not place it there.
            var grid = new WorldGrid(8, 8, 100f);
            for (uint seed = 0; seed < 40; seed++)
            {
                var map = LayerGenerator.Generate(seed, grid);
                Assert.That(grid.Distance(map.StartZone, map.Gates[0].Point.Zone), Is.EqualTo(4));
            }
        }

        [Test]
        public void LandmarkNames_AreUniqueAcrossTheLayer()
        {
            var map = LayerGenerator.Generate(77, new WorldGrid(12, 12, 200f));
            var names = map.Landmarks.Select(l => l.Name).ToList();
            Assert.That(names.Count, Is.GreaterThan(144));
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));
            Assert.That(names.All(n => n.Length > 3));
        }

        [Test]
        public void EveryZone_HasLandmarksShrinesAndSpawnPointsInRange()
        {
            var settings = new LayerSettings();
            var grid = new WorldGrid(6, 6, 250f);
            var map = LayerGenerator.Generate(5, grid, settings);
            foreach (var z in map.Zones)
            {
                Assert.That(z.Landmarks.Count, Is.InRange(1, 3));
                Assert.That(z.Shrines.Count, Is.InRange(2, 3));
                Assert.That(z.SpawnPoints.Count, Is.InRange(6, 10));
                foreach (var p in z.Landmarks.Select(l => l.Point).Concat(z.Shrines.Select(s => s.Point)))
                {
                    Assert.That(p.Zone, Is.EqualTo(z.Coord));
                    Assert.That(p.Local.X, Is.InRange(0f, grid.ZoneSize));
                    Assert.That(p.Local.Y, Is.InRange(0f, grid.ZoneSize));
                }
            }
            Assert.That(map.Shrines.Select(s => s.Id).Distinct().Count(), Is.EqualTo(map.Shrines.Count));
            Assert.That(map.StartPoint.Zone, Is.EqualTo(map.StartZone));
        }

        [Test]
        public void Zones_LeanTowardOneOrTwoColors()
        {
            var map = LayerGenerator.Generate(9, new WorldGrid(10, 10, 200f));
            Assert.That(map.Zones.Any(z => z.Bias.HasSecondary));
            Assert.That(map.Zones.Any(z => !z.Bias.HasSecondary));
            foreach (var z in map.Zones.Where(z => z.Bias.HasSecondary))
            {
                Assert.That(z.Bias.Secondary, Is.Not.EqualTo(z.Bias.Primary));
            }
        }

        [Test]
        public void MvpSettings_ForceYellowAndOnlyWisps()
        {
            var map = LayerGenerator.Generate(3, LayerSettings.MvpGrid(), LayerSettings.ForMvp());
            foreach (var z in map.Zones)
            {
                Assert.That(z.Bias.Primary, Is.EqualTo(EssenceColor.Yellow));
                Assert.That(z.Bias.HasSecondary, Is.False);
                Assert.That(z.FamilyWeight(EnemyFamily.Wisp), Is.EqualTo(1f));
                Assert.That(z.SpawnPoints.All(p => p.Family == EnemyFamily.Wisp));
            }
        }

        [Test]
        public void ColorBias_DrivesFamilyWeights()
        {
            var settings = new LayerSettings { OffBiasWeight = 0f };
            var w = LayerGenerator.FamilyWeights(settings, new ZoneColorBias(EssenceColor.Red, EssenceColor.Green, 0.3f));
            Assert.That(w[(int)EnemyFamily.Skitter], Is.EqualTo(0.7f).Within(1e-5));
            Assert.That(w[(int)EnemyFamily.Mite], Is.EqualTo(0.3f).Within(1e-5));
            Assert.That(w[(int)EnemyFamily.Wisp], Is.EqualTo(0f));

            settings.OffBiasWeight = 0.1f;
            w = LayerGenerator.FamilyWeights(settings, new ZoneColorBias(EssenceColor.Yellow));
            Assert.That(w[(int)EnemyFamily.Wisp], Is.EqualTo(1f / 1.2f).Within(1e-5));
            Assert.That(w.Sum(), Is.EqualTo(1f).Within(1e-5));
        }

        [Test]
        public void SpawnPoints_FollowTheLeanAcrossALayer()
        {
            var map = LayerGenerator.Generate(21, new WorldGrid(12, 12, 200f));
            int matching = 0, total = 0;
            foreach (var z in map.Zones.Where(z => !z.Bias.HasSecondary))
            {
                EssenceColors.TryGetFamily(z.Bias.Primary, out var family);
                matching += z.SpawnPoints.Count(p => p.Family == family);
                total += z.SpawnPoints.Count;
            }
            Assert.That(matching / (double)total, Is.GreaterThan(0.85));
        }

        [Test]
        public void Describe_NamesTheNearestLandmarkAndItsZone()
        {
            var map = LayerGenerator.Generate(11, new WorldGrid(4, 4, 250f));
            var landmark = map.Landmarks[5];
            var nearby = landmark.Point.World + new Vector2(1f, 1f);
            Assert.That(map.Describe(nearby), Is.EqualTo($"near {landmark.Name} in zone {landmark.Point.Zone}"));
        }

        [Test]
        public void Distance_WrapsAroundTheWorldEdge()
        {
            var map = LayerGenerator.Generate(11, new WorldGrid(4, 4, 250f));
            Assert.That(map.Distance(new Vector2(5f, 5f), new Vector2(995f, 5f)), Is.EqualTo(10f).Within(1e-3));
            var located = map.Locate(new Vector2(-10f, 1010f));
            Assert.That(located.Zone, Is.EqualTo(new ZoneCoord(3, 0)));
            Assert.That(located.Local.X, Is.EqualTo(240f).Within(1e-3));
        }

        [Test]
        public void NearestLandmark_LooksAcrossTheWrap()
        {
            var map = LayerGenerator.Generate(11, new WorldGrid(4, 4, 250f));
            foreach (var landmark in map.Landmarks)
            {
                // The same spot one whole world-width over is still that landmark.
                var shifted = landmark.Point.World + new Vector2(1000f, -1000f);
                Assert.That(map.NearestLandmark(shifted), Is.SameAs(landmark));
            }
        }
    }
}
