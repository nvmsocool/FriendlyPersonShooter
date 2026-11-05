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
            var tiling = new ZoneTiling(4, 3, 100f);
            var a = LayerGenerator.Generate(1234, tiling);
            var b = LayerGenerator.Generate(1234, new ZoneTiling(4, 3, 100f));
            Assert.That(Fingerprint(a), Is.EqualTo(Fingerprint(b)));
        }

        [Test]
        public void DifferentSeed_GivesDifferentMap()
        {
            var tiling = new ZoneTiling(4, 3, 100f);
            Assert.That(Fingerprint(LayerGenerator.Generate(1, tiling)), Is.Not.EqualTo(Fingerprint(LayerGenerator.Generate(2, tiling))));
        }

        [Test]
        public void StartZone_IsAHexagon([Range(0u, 10u)] uint seed)
        {
            var map = LayerGenerator.Generate(seed, LayerSettings.MvpTiling(), LayerSettings.ForMvp());
            Assert.That(map.StartZone.Shape, Is.EqualTo(ZoneShape.Hexagon));
        }

        [Test]
        public void Gate_IsInAFarZone_CountingTheWrap([Range(0u, 30u)] uint seed)
        {
            var tiling = LayerSettings.MvpTiling();
            var settings = LayerSettings.ForMvp();
            var map = LayerGenerator.Generate(seed, tiling, settings);
            Assert.That(map.Gates.Count, Is.EqualTo(1));
            var gateZone = map.Gates[0].Point.Zone;
            Assert.That(LayerGenerator.GateCandidates(tiling, settings, map.StartPoint.World), Does.Contain(gateZone));
            Assert.That(map.Zone(gateZone).HasGate);
            Assert.That(map.Zones.Count(z => z.HasGate), Is.EqualTo(1));
        }

        [Test]
        public void GateCandidates_AreFarAndPreferHexagons()
        {
            var tiling = new ZoneTiling(4, 4, 100f);
            var settings = new LayerSettings();
            var start = tiling.Centre(new ZoneCoord(0, 0));
            var candidates = LayerGenerator.GateCandidates(tiling, settings, start);
            float farthest = tiling.Zones.Max(z => tiling.DistanceBetween(start, tiling.Centre(z)));
            Assert.That(candidates, Is.Not.Empty);
            foreach (var z in candidates)
            {
                // Measured the short way around: no zone that is really close across the wrap.
                Assert.That(tiling.DistanceBetween(start, tiling.Centre(z)), Is.GreaterThanOrEqualTo(settings.GateDistanceShare * farthest - 0.01f));
            }
            if (candidates.Count(z => z.Shape == ZoneShape.Hexagon) >= settings.GateCount)
            {
                Assert.That(candidates.All(z => z.Shape == ZoneShape.Hexagon));
            }
        }

        [Test]
        public void LandmarkNames_AreUniqueAcrossTheLayer()
        {
            var map = LayerGenerator.Generate(77, new ZoneTiling(5, 5, 100f));
            var names = map.Landmarks.Select(l => l.Name).ToList();
            Assert.That(names.Count, Is.GreaterThanOrEqualTo(150));
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));
            Assert.That(names.All(n => n.Length > 3));
        }

        [Test]
        public void EveryZone_HasFeaturesInsideItsOutline_ScaledByArea()
        {
            var settings = new LayerSettings();
            var tiling = new ZoneTiling(3, 3, 125f);
            var map = LayerGenerator.Generate(5, tiling, settings);
            foreach (var z in map.Zones)
            {
                if (z.Coord.Shape == ZoneShape.Hexagon)
                {
                    Assert.That(z.Landmarks.Count, Is.InRange(1, 3));
                    Assert.That(z.Shrines.Count, Is.InRange(2, 3));
                    Assert.That(z.SpawnPoints.Count, Is.InRange(6, 10));
                }
                else
                {
                    Assert.That(z.Landmarks.Count, Is.InRange(1, 2));
                    Assert.That(z.Shrines.Count, Is.InRange(1, 2));
                    Assert.That(z.SpawnPoints.Count, Is.InRange(1, 4));
                }
                var points = z.Landmarks.Select(l => l.Point).Concat(z.Shrines.Select(s => s.Point)).Concat(z.SpawnPoints.Select(s => s.Point));
                foreach (var p in points)
                {
                    Assert.That(p.Zone, Is.EqualTo(z.Coord));
                    Assert.That(tiling.ZoneAt(p.World), Is.EqualTo(z.Coord));
                    Assert.That(tiling.IsCanonical(p.World));
                    Assert.That(tiling.EdgeDistance(z.Coord, p.World), Is.GreaterThan(5f));
                }
            }
            Assert.That(map.Shrines.Select(s => s.Id).Distinct().Count(), Is.EqualTo(map.Shrines.Count));
            Assert.That(map.StartPoint.Zone, Is.EqualTo(map.StartZone));
        }

        [Test]
        public void Zones_LeanTowardOneOrTwoColors()
        {
            var map = LayerGenerator.Generate(9, new ZoneTiling(4, 4, 100f));
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
            var map = LayerGenerator.Generate(3, LayerSettings.MvpTiling(), LayerSettings.ForMvp());
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
            var map = LayerGenerator.Generate(21, new ZoneTiling(5, 5, 100f));
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
            var map = LayerGenerator.Generate(11, new ZoneTiling(3, 3, 125f));
            var landmark = map.Landmarks[5];
            var nearby = landmark.Point.World + new Vector2(1f, 1f);
            Assert.That(map.Describe(nearby), Is.EqualTo($"near {landmark.Name} in zone {landmark.Point.Zone}"));
        }

        [Test]
        public void Distance_WrapsAroundTheWorldEdge()
        {
            var tiling = new ZoneTiling(3, 3, 125f);
            var map = LayerGenerator.Generate(11, tiling);
            var p = tiling.Centre(new ZoneCoord(1, 1, 0));
            Assert.That(map.Distance(p, p + tiling.PeriodA - new Vector2(10f, 0f)), Is.EqualTo(10f).Within(1e-2));
            var located = map.Locate(p + tiling.PeriodA - tiling.PeriodB * 2f + new Vector2(3f, 4f));
            Assert.That(located.Zone, Is.EqualTo(new ZoneCoord(1, 1, 0)));
            Assert.That(Vector2.Distance(located.World, p + new Vector2(3f, 4f)), Is.LessThan(1e-2f));
        }

        [Test]
        public void NearestLandmark_LooksAcrossTheWrap()
        {
            var tiling = new ZoneTiling(3, 3, 125f);
            var map = LayerGenerator.Generate(11, tiling);
            foreach (var landmark in map.Landmarks)
            {
                // The same spot one whole period over is still that landmark.
                var shifted = landmark.Point.World + tiling.PeriodA - tiling.PeriodB;
                Assert.That(map.NearestLandmark(shifted), Is.SameAs(landmark));
            }
        }
    }
}
