using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class SpawnBloomTests
    {
        private static LayerMap Map(uint seed = 42) => LayerGenerator.Generate(seed, new ZoneTiling(3, 3, 125f));

        private static readonly Vector2[] Nobody = new Vector2[0];

        /// <summary>Kills every enemy of a group.</summary>
        private static void Clear(SpawnSchedule schedule, SpawnEvent e, double time)
        {
            for (int i = 0; i < e.Count; i++) schedule.Lost(e.Group, time);
        }

        [Test]
        public void SpawnSchedule_IsTheSameForTheSameSeed()
        {
            var a = new SpawnSchedule(Map(), new BloomSchedule(Map()));
            var b = new SpawnSchedule(Map(), new BloomSchedule(Map()));
            var zone = new ZoneCoord(2, 2);
            for (int round = 0; round < 10; round++)
            {
                double t = round * 130;
                var x = a.Due(zone, t, Nobody);
                var y = b.Due(zone, t, Nobody);
                Assert.That(x.Select(e => e.ToString()), Is.EqualTo(y.Select(e => e.ToString())));
                foreach (var e in x) Clear(a, e, t);
                foreach (var e in y) Clear(b, e, t);
            }
        }

        [Test]
        public void SpawnSchedule_FillsAShareOfPointsOnceWithTheirOwnFamily()
        {
            var map = Map();
            var schedule = new SpawnSchedule(map);
            int used = 0, total = 0;
            foreach (var zone in map.Zones)
            {
                var first = schedule.Due(zone.Coord, 0, Nobody);
                foreach (var e in first)
                {
                    var point = zone.SpawnPoints[e.SpawnPointId];
                    Assert.That(schedule.IsActive(zone.Coord, point.Id));
                    Assert.That(e.Family, Is.EqualTo(point.Family));
                    Assert.That(e.Count, Is.InRange(schedule.Settings.MinCount(e.Family), schedule.Settings.MaxCount(e.Family)));
                    Assert.That(e.Generation, Is.EqualTo(0));
                    Assert.That(double.IsPositiveInfinity(e.End), "spawn point groups never time out");
                }
                used += first.Count;
                total += zone.SpawnPoints.Count;
                // Nobody died, so nothing more comes, however long it's been.
                Assert.That(schedule.Due(zone.Coord, 5000, Nobody), Is.Empty);
            }
            Assert.That(used / (double)total, Is.InRange(0.3, 0.7));
        }

        [Test]
        public void SpawnSchedule_SurvivorsHoldTheirPoint()
        {
            var map = Map();
            var schedule = new SpawnSchedule(map);
            var zone = map.Zones.First(z => schedule.Due(z.Coord, 0, Nobody).Any(e => e.Count >= 2)).Coord;
            var fresh = new SpawnSchedule(map);
            var group = fresh.Due(zone, 0, Nobody).First(e => e.Count >= 2);
            for (int i = 0; i < group.Count - 1; i++) fresh.Lost(group.Group, 10);
            Assert.That(fresh.AliveIn(group.Group), Is.EqualTo(1));
            Assert.That(fresh.Due(zone, 10000, Nobody).Any(e => e.Group.Equals(group.Group)), Is.False, "one is still alive");
        }

        [Test]
        public void SpawnSchedule_ClearedPointRefillsAfterTwoMinutesWhenNobodyIsNear()
        {
            var map = Map();
            var schedule = new SpawnSchedule(map);
            var zone = map.Zones[7].Coord;
            var group = schedule.Due(zone, 0, Nobody).First();
            Clear(schedule, group, 30);
            var spot = group.Point.World;
            bool Refills(double t, params Vector2[] players) => schedule.Due(zone, t, players).Any(e => e.Group.Equals(group.Group));

            Assert.That(Refills(30 + 119, new Vector2[0]), Is.False, "still cooling down");
            Assert.That(Refills(30 + 120, spot + new Vector2(60, 0)), Is.False, "a player 60 m away");
            Assert.That(Refills(30 + 200, spot + new Vector2(0, 99)), Is.False, "a player 99 m away");
            Assert.That(Refills(30 + 300, spot + new Vector2(150, 0)), Is.True, "the nearest player is 150 m away");
            Assert.That(schedule.AliveIn(group.Group), Is.GreaterThan(0));
        }

        [Test]
        public void SpawnSchedule_PlayerDistanceGoesAroundTheWrap()
        {
            var map = Map();
            var schedule = new SpawnSchedule(map);
            var spot = map.Zones[0].SpawnPoints[0].Point.World;
            var copy = spot + map.Tiling.PeriodA + new Vector2(30, 0);
            Assert.That(schedule.AnyPlayerWithin(spot, new[] { copy }, 100f), Is.True);
            Assert.That(schedule.AnyPlayerWithin(spot, new[] { spot + new Vector2(0, 101) }, 100f), Is.False);
        }

        [Test]
        public void SpawnSchedule_RefillsAreNewRandomEnemiesFromTheZonesMix()
        {
            var map = Map(5);
            var schedule = new SpawnSchedule(map);
            var seen = new HashSet<EnemyFamily>();
            var mixed = map.Zones.First(z => z.Bias.HasSecondary && z.SpawnPoints.Any(p => schedule.IsActive(z.Coord, p.Id)));
            for (int round = 0; round < 40; round++)
            {
                double t = round * 121.0;
                foreach (var e in schedule.Due(mixed.Coord, t, Nobody))
                {
                    Assert.That(mixed.FamilyWeight(e.Family), Is.GreaterThan(0f), $"{e.Family} isn't in the zone's mix");
                    Assert.That(e.Family, Is.Not.EqualTo(EnemyFamily.Glimmer));
                    if (e.Generation > 0) seen.Add(e.Family);
                    Clear(schedule, e, t);
                }
            }
            Assert.That(seen.Count, Is.GreaterThan(1), "refills vary");
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

            Assert.That(schedule.Due(bloom.Zone, bloom.Start - 1, Nobody).Any(e => e.IsBloom), Is.False, "not before it starts");
            double mid = (bloom.Start + bloom.End) / 2;
            var during = schedule.Due(bloom.Zone, mid, Nobody).Where(e => e.IsBloom).ToList();
            Assert.That(during.Count, Is.EqualTo(blooms.Settings.SpawnGroups));
            Assert.That(during.All(e => e.Family == bloom.Family && e.Count >= schedule.Settings.MaxCount(bloom.Family)));
            Assert.That(during.All(e => e.Zone == bloom.Zone && e.End == bloom.End));
            Assert.That(schedule.Due(bloom.Zone, mid + 1, Nobody).Any(e => e.IsBloom), Is.False, "the groups are still out");

            // A cleared bloom group comes back quickly while the bloom runs, players or not.
            Clear(schedule, during[0], mid);
            var near = new[] { during[0].Point.World };
            Assert.That(schedule.Due(bloom.Zone, mid + 5, near).Any(e => e.Group.Equals(during[0].Group)), Is.False);
            Assert.That(schedule.Due(bloom.Zone, mid + schedule.Settings.BloomRefillSeconds, near).Count(e => e.Group.Equals(during[0].Group)), Is.EqualTo(1));

            // Once it's over, nothing more of it comes.
            foreach (var e in during) Clear(schedule, e, mid + 30);
            Assert.That(schedule.Due(bloom.Zone, bloom.End + 1, Nobody).Any(e => e.IsBloom), Is.False);
        }
    }
}
