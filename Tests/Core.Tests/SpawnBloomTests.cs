using System.Linq;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class SpawnBloomTests
    {
        private static LayerMap Map(uint seed = 42) => LayerGenerator.Generate(seed, new WorldGrid(6, 6, 250f));

        [Test]
        public void SpawnSchedule_IsTheSameForTheSameSeedAndClock()
        {
            var a = new SpawnSchedule(Map(), new BloomSchedule(Map()));
            var b = new SpawnSchedule(Map(), new BloomSchedule(Map()));
            var zone = new ZoneCoord(2, 3);
            for (double t = 0; t < 3600; t += 97)
            {
                Assert.That(a.ActiveAt(zone, t).Select(e => e.ToString()), Is.EqualTo(b.ActiveAt(zone, t).Select(e => e.ToString())));
            }
        }

        [Test]
        public void SpawnSchedule_ActivatesSomePointsEachWaveAndChangesBetweenWaves()
        {
            var map = Map();
            var schedule = new SpawnSchedule(map);
            var zone = map.Zones[7];
            var w0 = schedule.ActiveAt(zone.Coord, 10).Select(e => e.SpawnPointId).ToList();
            var w0Later = schedule.ActiveAt(zone.Coord, 50).Select(e => e.SpawnPointId).ToList();
            Assert.That(w0, Is.EqualTo(w0Later), "same wave, same points");

            bool changed = false;
            int totalActive = 0, waves = 0;
            for (int wave = 0; wave < 50; wave++)
            {
                var active = schedule.ActiveAt(zone.Coord, wave * 60 + 1);
                totalActive += active.Count;
                waves++;
                if (!active.Select(e => e.SpawnPointId).SequenceEqual(w0)) changed = true;
                foreach (var e in active)
                {
                    var point = zone.SpawnPoints[e.SpawnPointId];
                    Assert.That(e.Family, Is.EqualTo(point.Family));
                    Assert.That(e.Count, Is.InRange(schedule.Settings.MinCount(e.Family), schedule.Settings.MaxCount(e.Family)));
                    Assert.That(e.Start, Is.EqualTo(wave * 60.0));
                }
            }
            Assert.That(changed);
            double share = totalActive / (double)(waves * zone.SpawnPoints.Count);
            Assert.That(share, Is.InRange(0.3, 0.7));
        }

        [Test]
        public void SpawnSchedule_WindowCoversEveryOverlappingWave()
        {
            var map = Map();
            var schedule = new SpawnSchedule(map);
            var zone = map.Zones[0].Coord;
            var window = schedule.Between(zone, 30, 150);
            Assert.That(window.Select(e => e.Wave).Distinct().OrderBy(w => w), Is.EqualTo(new long[] { 0, 1, 2 }));
        }

        [Test]
        public void Blooms_AreDeterministicAndLastAFewMinutes()
        {
            var a = new BloomSchedule(Map(7));
            var b = new BloomSchedule(Map(7));
            int found = 0;
            for (long slot = 0; slot < 40; slot++)
            {
                foreach (var zone in Map(7).Zones)
                {
                    var x = a.BloomIn(zone.Coord, slot);
                    var y = b.BloomIn(zone.Coord, slot);
                    Assert.That(x?.ToString(), Is.EqualTo(y?.ToString()));
                    if (x == null) continue;
                    found++;
                    Assert.That(x.Duration, Is.InRange(180.0, 300.0));
                    Assert.That(x.Start, Is.GreaterThanOrEqualTo(slot * 360.0));
                    Assert.That(x.End, Is.LessThanOrEqualTo((slot + 1) * 360.0));
                    Assert.That(zone.Bias.LeansToward(x.Color));
                    Assert.That(x.Point.Zone, Is.EqualTo(zone.Coord));
                }
            }
            Assert.That(found, Is.GreaterThan(0));
        }

        [Test]
        public void Blooms_ActiveAtAndNextAgree()
        {
            var blooms = new BloomSchedule(Map(3));
            Assert.That(blooms.TryGetNext(0, out var next), Is.True);
            Assert.That(blooms.ActiveAt(next.Start).Any(b => b.ToString() == next.ToString()));
            Assert.That(blooms.ActiveAt(next.End).Any(b => b.Zone == next.Zone && b.Start == next.Start), Is.False);
            Assert.That(blooms.ActiveIn(next.Zone, next.Start + 1)?.Start, Is.EqualTo(next.Start));

            // Clock starts at 0, so nothing can be running before the first bloom starts.
            for (double t = 0; t < next.Start; t += 5)
            {
                Assert.That(blooms.ActiveAt(t), Is.Empty);
            }
        }

        [Test]
        public void Blooms_NeverWhenChanceIsZero()
        {
            var blooms = new BloomSchedule(Map(), new BloomSettings { ChancePerZonePerSlot = 0f, MaxSearchSlots = 5 });
            Assert.That(blooms.TryGetNext(0, out _), Is.False);
            Assert.That(blooms.ActiveAt(100), Is.Empty);
        }

        [Test]
        public void Blooms_AddHeavySpawnsOfTheirColorWhileRunning()
        {
            var map = Map(3);
            var blooms = new BloomSchedule(map);
            var schedule = new SpawnSchedule(map, blooms);
            Assert.That(blooms.TryGetNext(0, out var bloom));

            double mid = (bloom.Start + bloom.End) / 2;
            var during = schedule.ActiveAt(bloom.Zone, mid).Where(e => e.IsBloom).ToList();
            Assert.That(during.Count, Is.EqualTo(blooms.Settings.SpawnGroups));
            Assert.That(during.All(e => e.Family == bloom.Family && e.Count >= schedule.Settings.MaxCount(bloom.Family)));
            Assert.That(during.All(e => e.Zone == bloom.Zone));

            Assert.That(schedule.ActiveAt(bloom.Zone, bloom.End + 1).Any(e => e.IsBloom && e.Start >= bloom.Start && e.End <= bloom.End), Is.False);
        }
    }
}
