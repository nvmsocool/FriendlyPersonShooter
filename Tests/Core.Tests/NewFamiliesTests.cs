using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Sentinels, Divers and roaming Bulwarks; difficulty tiers; elites.</summary>
    public class NewFamiliesTests
    {
        [Test]
        public void SentinelEyeOpensOnlyAroundItsShot()
        {
            var s = new SentinelSettings();
            var b = SentinelBehavior.Create(Vector3.Zero, 3, s);
            var target = new Vector3(20f, 1f, 0f);
            for (int i = 0; i < 60; i++) b.Step(0.05f, false, false, target, s);
            Assert.That(b.EyeOpen, Is.False);
            Assert.That(b.Yaw, Is.EqualTo(90f).Within(1f), "it turns to face what it watches");

            b.HitOutcome(true, s, out float mult, out bool weak);
            Assert.That(weak, Is.False, "a shut eye is armor");
            Assert.That(mult, Is.EqualTo(s.ArmorMultiplier));

            b.Step(0.05f, true, false, target, s);
            Assert.That(b.EyeOpen, Is.True, "the wind-up opens the eye");
            b.HitOutcome(true, s, out mult, out weak);
            Assert.That(weak, Is.True);
            Assert.That(mult, Is.EqualTo(1f));
            b.HitOutcome(false, s, out mult, out weak);
            Assert.That(weak, Is.False, "the body is armor even then");

            b.Step(0.05f, false, true, target, s);
            Assert.That(b.EyeOpen, Is.True, "it stays open just after firing");
            for (int i = 0; i < 20; i++) b.Step(0.05f, false, false, target, s);
            Assert.That(b.EyeOpen, Is.False);
        }

        [Test]
        public void SentinelEyeFacesTheTargetSoOnlyAFrontShotFindsIt()
        {
            var s = new SentinelSettings();
            var b = SentinelBehavior.Create(Vector3.Zero, 3, s);
            var target = new Vector3(0f, 1.6f, 30f);
            for (int i = 0; i < 80; i++) b.Step(0.05f, true, false, target, s);
            var t = b.ToHitTarget(1, s);
            var eye = b.EyeCenter(s);
            Assert.That(HitScan.Cast(target, eye - target, 100f, new[] { t }, out var front), Is.True);
            Assert.That(front.WeakPoint, Is.True, "from the front the eye is in reach");
            var behind = new Vector3(0f, eye.Y, -30f);
            Assert.That(HitScan.Cast(behind, eye - behind, 100f, new[] { t }, out var back), Is.True);
            Assert.That(back.WeakPoint, Is.False, "the body hides the eye from behind");
        }

        [Test]
        public void DiverCirclesWarnsDivesAndStrikes()
        {
            var s = new DiverSettings();
            var d = DiverBehavior.Create(Vector3.Zero, 7, s);
            Assert.That(d.Position.Y, Is.EqualTo(s.CircleHeight).Within(1e-3));
            var player = new AttackCandidate(4, new Vector3(3f, 1f, 3f));
            var players = new[] { player };
            var events = new List<DiverEventKind>();
            bool dived = false;
            for (int i = 0; i < 400; i++)
            {
                var e = d.Step(0.02f, players, s, out _);
                if (e.Kind != DiverEventKind.None) events.Add(e.Kind);
                if (d.Phase == DiverPhase.Dive) dived = true;
                if (e.Kind == DiverEventKind.Struck) break;
            }
            Assert.That(events.First(), Is.EqualTo(DiverEventKind.Warned), "it warns before diving");
            Assert.That(dived, Is.True);
            Assert.That(events, Does.Contain(DiverEventKind.Struck), "a player standing still gets hit");
            Assert.That(d.Phase, Is.EqualTo(DiverPhase.Climb));
        }

        [Test]
        public void DiverMissesAPlayerWhoMoves()
        {
            var s = new DiverSettings();
            var d = DiverBehavior.Create(Vector3.Zero, 7, s);
            var player = new AttackCandidate(4, new Vector3(3f, 1f, 3f));
            var players = new AttackCandidate[] { player };
            DiverEventKind last = DiverEventKind.None;
            for (int i = 0; i < 600 && last != DiverEventKind.Missed && last != DiverEventKind.Struck; i++)
            {
                // Step aside as soon as it commits to the dive.
                if (d.Phase == DiverPhase.Dive) players[0] = new AttackCandidate(4, new Vector3(9f, 1f, -6f));
                last = d.Step(0.02f, players, s, out _).Kind;
            }
            Assert.That(last, Is.EqualTo(DiverEventKind.Missed));
        }

        [Test]
        public void DiverIgnoresPlayersOutOfReachOrDown()
        {
            var s = new DiverSettings();
            var d = DiverBehavior.Create(Vector3.Zero, 7, s);
            var players = new[] { new AttackCandidate(1, new Vector3(80f, 1f, 0f)), new AttackCandidate(2, new Vector3(1f, 1f, 1f), false) };
            for (int i = 0; i < 1000; i++) Assert.That(d.Step(0.02f, players, s, out _).Kind, Is.EqualTo(DiverEventKind.None));
        }

        [Test]
        public void EitherWingIsAWeakPoint()
        {
            var s = new DiverSettings();
            var d = DiverBehavior.Create(Vector3.Zero, 7, s);
            var t = d.ToHitTarget(5, s);
            foreach (int side in new[] { -1, 1 })
            {
                var wing = d.Wing(side, s);
                var from = wing + new Vector3(0f, -20f, 0f);
                Assert.That(HitScan.Cast(from, wing - from, 50f, new[] { t }, out var hit), Is.True);
                Assert.That(hit.WeakPoint, Is.True, $"wing {side}");
            }
            var below = d.Position + new Vector3(0f, -20f, 0f);
            Assert.That(HitScan.Cast(below, d.Position - below, 50f, new[] { t }, out var body), Is.True);
            Assert.That(body.WeakPoint, Is.False, "the body between the wings isn't");
        }

        [Test]
        public void PhasingDiverBlinksRoundItsRing()
        {
            var s = new DiverSettings();
            var d = DiverBehavior.Create(Vector3.Zero, 7, s, phasing: true);
            int blinks = 0;
            for (int i = 0; i < 200; i++)
            {
                var before = d.Position;
                d.Step(0.02f, new AttackCandidate[0], s, out bool blinked);
                if (blinked)
                {
                    blinks++;
                    Assert.That(Vector3.Distance(before, d.Position), Is.GreaterThan(4f), "a blink is a jump, not a glide");
                }
            }
            Assert.That(blinks, Is.InRange(2, 4));
        }

        [Test]
        public void WorldBulwarkIsSmallerAndCallsNoAdds()
        {
            var world = BulwarkSettings.ForWorld();
            var raid = new BulwarkSettings();
            Assert.That(world.BodyRadius, Is.LessThan(raid.BodyRadius * 0.6f));
            Assert.That(world.AddWaveAt, Is.Empty);
            var b = BulwarkBehavior.Create(Vector3.Zero, 0f, world);
            for (int i = 0; i < 400; i++)
            {
                b.Step(0.05f, 0.1f, 1f, world);
                Assert.That(b.TakeWaveDue(0.1f, world), Is.EqualTo(0));
            }
        }

        [Test]
        public void HigherTiersAreSmallerFasterAndOpenForLess()
        {
            for (int tier = 2; tier <= 3; tier++)
            {
                Assert.That(EnemyTiers.Wisp(tier).BodyRadius, Is.LessThan(EnemyTiers.Wisp(tier - 1).BodyRadius));
                Assert.That(EnemyTiers.Wisp(tier).HittableWindow, Is.LessThan(EnemyTiers.Wisp(tier - 1).HittableWindow));
                Assert.That(EnemyTiers.Skitter(tier).Speed, Is.GreaterThan(EnemyTiers.Skitter(tier - 1).Speed));
                Assert.That(EnemyTiers.Skitter(tier).MaxTurnInterval, Is.LessThan(EnemyTiers.Skitter(tier - 1).MaxTurnInterval));
                Assert.That(EnemyTiers.Sentinel(tier).EyeOpenTime, Is.LessThan(EnemyTiers.Sentinel(tier - 1).EyeOpenTime));
                Assert.That(EnemyTiers.Diver(tier).DiveSpeed, Is.GreaterThan(EnemyTiers.Diver(tier - 1).DiveSpeed));
                Assert.That(EnemyTiers.Bulwark(tier).OpenTime, Is.LessThan(EnemyTiers.Bulwark(tier - 1).OpenTime));
                Assert.That(EnemyTiers.Mites(tier).MiteRadius, Is.LessThan(EnemyTiers.Mites(tier - 1).MiteRadius));
            }
            Assert.That(EnemyTiers.Wisp(1).BodyRadius, Is.EqualTo(new WispSettings().BodyRadius), "tier 1 is the family as designed");
            Assert.That(EnemyTiers.Size(9), Is.EqualTo(EnemyTiers.Size(3)), "tiers stop at 3");
        }

        [Test]
        public void ElitesMixTwoFamiliesAndDropBothColors()
        {
            Assert.That(Elites.For(EnemyFamily.Wisp), Is.EqualTo(EliteKind.None));
            Assert.That(Elites.For(EnemyFamily.Mite), Is.EqualTo(EliteKind.None));
            foreach (EliteKind kind in new[] { EliteKind.Blinker, EliteKind.Guarded, EliteKind.Soaring, EliteKind.Phasing })
            {
                var main = Elites.Primary(kind);
                Assert.That(Elites.For(main), Is.EqualTo(kind));
                Assert.That(Elites.Second(kind), Is.Not.EqualTo(main));

                var ledger = new DropLedger();
                ledger.RecordHit(1, 7);
                ledger.RecordHit(1, 8);
                var awards = ledger.Killed(1, main, kind);
                foreach (int player in new[] { 7, 8 })
                {
                    var mine = awards.Where(a => a.PlayerId == player).ToList();
                    int normal = (int)new EssenceDropSettings().DropFor(main);
                    Assert.That(mine.Where(a => a.Color == EssenceColors.Of(main)).Sum(a => a.Amount), Is.EqualTo(normal * 3), $"{kind}: more of the main color");
                    Assert.That(mine.Where(a => a.Color == EssenceColors.Of(Elites.Second(kind))).Sum(a => a.Amount), Is.EqualTo(Elites.SecondDrop),
                        $"{kind}: a little of the second color");
                }
            }
        }

        [Test]
        public void DeeperLayersHaveMoreElites()
        {
            var counts = new int[4];
            var totals = new int[4];
            for (int layer = 1; layer <= 3; layer++)
            {
                var settings = LayerSettings.ForLayer(layer);
                var map = LayerGenerator.Generate(2026, LayerSettings.MvpTiling(), settings);
                var schedule = new SpawnSchedule(map, null, settings.Spawns());
                // Clear every group 20 times over, with nobody around, so each point refills each time.
                for (int round = 0; round < 20; round++)
                {
                    foreach (var zone in map.Zones)
                    {
                        foreach (var e in schedule.Due(zone.Coord, round * 200.0, null))
                        {
                            for (int i = 0; i < e.Count; i++) schedule.Lost(e.Group, round * 200.0);
                            if (Elites.For(e.Family) == EliteKind.None) continue;
                            totals[layer]++;
                            if (e.Elite != EliteKind.None)
                            {
                                counts[layer]++;
                                Assert.That(Elites.Primary(e.Elite), Is.EqualTo(e.Family));
                            }
                        }
                    }
                }
            }
            float share1 = counts[1] / (float)totals[1], share2 = counts[2] / (float)totals[2], share3 = counts[3] / (float)totals[3];
            Assert.That(share1, Is.InRange(0.02f, 0.12f));
            Assert.That(share2, Is.GreaterThan(share1 * 2f));
            Assert.That(share3, Is.GreaterThan(share2));
        }

        [Test]
        public void DeepestLayerHasOnlyTheHardestFamilies()
        {
            var map = LayerGenerator.Generate(2026, LayerSettings.MvpTiling(), LayerSettings.ForLayer(3));
            var families = new HashSet<EnemyFamily>(map.Zones.SelectMany(z => z.SpawnPoints).Select(p => p.Family));
            Assert.That(families, Is.EquivalentTo(new[] { EnemyFamily.Skitter, EnemyFamily.Sentinel, EnemyFamily.Bulwark, EnemyFamily.Diver }));
            Assert.That(LayerSettings.ForLayer(3).Deepest, Is.True);
            Assert.That(LayerSettings.ForLayer(2).Deepest, Is.False);
            Assert.That(LayerSettings.ForLayer(3).BloomChance, Is.LessThan(LayerSettings.ForLayer(1).BloomChance), "rarer blooms");
        }

        [Test]
        public void NewFamiliesFightFromWhereTheyShould()
        {
            Assert.That(EnemyAttackSettings.ForSentinel().Range, Is.GreaterThan(EnemyAttackSettings.ForWisp().Range * 2f), "Sentinels shoot from far off");
            Assert.That(EnemyAttackSettings.ForSentinel().Ranged, Is.True);
            Assert.That(EnemyAttackSettings.ForSentinel().TelegraphTime, Is.EqualTo(new SentinelSettings().EyeOpenTime), "the eye is open for the wind-up");
            Assert.That(EnemyAttackSettings.ForWorldBulwark().Ranged, Is.True);
            Assert.That(EnemyAttackSettings.ForDiver().Damage, Is.EqualTo(0f), "Divers hurt by diving, not with a bolt");
        }
    }
}
