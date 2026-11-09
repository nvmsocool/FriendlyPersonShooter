using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class WorldBossTests
    {
        private static readonly LayerMap Map = LayerGenerator.Generate(2026, LayerSettings.MvpTiling(), LayerSettings.ForPlaytest());

        [Test]
        public void LandsOnATimer_OneAtATime_AndAgainAfterItFalls()
        {
            var boss = new WorldBoss(new WorldBossSettings { FirstSpawn = 240, RespawnDelay = 480 }, 10);
            Assert.That(boss.Due(249), Is.False);
            Assert.That(boss.Due(250));
            boss.Spawned(Map.Landmarks[0]);
            Assert.That(boss.Due(5000), Is.False, "one at a time");
            boss.Killed(600);
            Assert.That(boss.Alive, Is.False);
            Assert.That(boss.Due(1079), Is.False);
            Assert.That(boss.Due(1080));
            Assert.That(boss.Kills, Is.EqualTo(1));
        }

        [Test]
        public void HealthHasAFloor_AndGrowsWithPlayers()
        {
            var combat = new CombatSettings();
            var boss = new WorldBoss();
            float raidFor3 = BulwarkSettings.HealthFor(3, combat);
            Assert.That(boss.Health(1, combat), Is.EqualTo(raidFor3 * 1.5f));
            Assert.That(boss.Health(6, combat), Is.EqualTo(raidFor3 * 3f));
        }

        [Test]
        public void EveryHitterGetsAllThreeColors()
        {
            var boss = new WorldBoss(new WorldBossSettings { EssenceEach = 12 });
            var awards = boss.Rewards(new[] { 4, 9 });
            Assert.That(awards.Count, Is.EqualTo(6));
            foreach (int id in new[] { 4, 9 })
            {
                var colors = awards.Where(a => a.PlayerId == id).Select(a => a.Color).ToList();
                Assert.That(colors, Is.EquivalentTo(new[] { EssenceColor.Yellow, EssenceColor.Red, EssenceColor.Green }));
            }
            Assert.That(awards.All(a => a.Amount == 12));
        }

        [Test]
        public void PicksALandmarkInRangeOfThePlayers()
        {
            var boss = new WorldBoss();
            var player = Map.StartPoint.World;
            for (uint seed = 1; seed <= 20; seed++)
            {
                var site = boss.PickSite(Map, new List<Vector2> { player }, seed);
                Assert.That(site, Is.Not.Null);
                float d = Map.Distance(site.Point.World, player);
                Assert.That(d, Is.GreaterThanOrEqualTo(boss.Settings.MinDistance));
            }
            Assert.That(boss.PickSite(Map, new List<Vector2>(), 3), Is.Not.Null);
        }

        [Test]
        public void PlaytestLayer_SpawnsEveryFamily()
        {
            var families = new HashSet<EnemyFamily>();
            foreach (var z in Map.Zones)
            {
                foreach (var p in z.SpawnPoints) families.Add(p.Family);
            }
            Assert.That(families, Is.EquivalentTo(new[]
            {
                EnemyFamily.Wisp, EnemyFamily.Skitter, EnemyFamily.Mite, EnemyFamily.Sentinel, EnemyFamily.Bulwark, EnemyFamily.Diver,
            }));
        }
    }
}
