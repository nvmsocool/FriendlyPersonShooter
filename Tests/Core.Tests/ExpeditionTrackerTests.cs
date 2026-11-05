using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class ExpeditionTrackerTests
    {
        private LayerMap _map;
        private Gate _gate;
        private EssenceWallet _wallet;
        private BloomSchedule _blooms;
        private ExpeditionTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _map = LayerGenerator.Generate(2026, LayerSettings.MvpTiling(), LayerSettings.ForMvp());
            _gate = new Gate(_map.Gates[0], _map.Seed);
            _wallet = new EssenceWallet(1);
            _blooms = new BloomSchedule(_map);
            _tracker = new ExpeditionTracker(_map, _gate, _wallet, 0, _blooms);
        }

        [Test]
        public void WalksThroughEveryPhaseToTheEnding()
        {
            var start = _map.StartPoint.World;
            var ledger = new DropLedger();

            var o = _tracker.Update(0, start);
            Assert.That(o.Phase, Is.EqualTo(ExpeditionPhase.Gather));
            Assert.That(o.Text, Does.StartWith("Gather Yellow essence: 0/40"));
            Assert.That(o.HasTarget);

            // 40 Wisp kills, one every 20 seconds.
            for (int i = 0; i < 40; i++)
            {
                ledger.RecordHit(i, 1);
                DropLedger.Pay(ledger.Killed(i, EnemyFamily.Wisp), id => _wallet);
            }
            Assert.That(_tracker.Collected, Is.EqualTo(40));

            o = _tracker.Update(800, start);
            Assert.That(o.Phase, Is.EqualTo(ExpeditionPhase.Bank));
            Assert.That(o.Target, Is.EqualTo(_gate.Site.Point.World), "the gate banks, so head straight there");
            Assert.That(o.Text, Does.StartWith("Take your Yellow to the gate"));
            Assert.That(o.Text, Does.Contain("40 carried"));

            _wallet.BankAll();
            o = _tracker.Update(860, start);
            Assert.That(o.Phase, Is.EqualTo(ExpeditionPhase.Unlock));
            Assert.That(o.Target, Is.EqualTo(_gate.Site.Point.World));
            Assert.That(o.Text, Does.Contain(_map.Describe(_gate.Site.Point.World)));

            Assert.That(_tracker.PassedGate(900), Is.False, "can't finish without access");
            Assert.That(_gate.Unlock(_wallet), Is.EqualTo(GateUnlockOutcome.Unlocked));
            o = _tracker.Update(1000, _gate.Site.Point.World);
            Assert.That(o.Phase, Is.EqualTo(ExpeditionPhase.PassThrough));

            Assert.That(_tracker.PassedGate(1010), Is.True);
            Assert.That(_tracker.PassedGate(1020), Is.False);
            o = _tracker.Update(1100, _gate.Site.Point.World);
            Assert.That(o.Phase, Is.EqualTo(ExpeditionPhase.Complete));
            Assert.That(o.HasTarget, Is.False);
            Assert.That(o.Text, Does.StartWith("Expedition complete in 16:50"));
            Assert.That(_tracker.TimerText(5000), Is.EqualTo("16:50 / 20:00"));

            Assert.That(_tracker.PhaseReachedAt(ExpeditionPhase.Gather), Is.EqualTo(0));
            Assert.That(_tracker.PhaseReachedAt(ExpeditionPhase.Bank), Is.EqualTo(800));
            Assert.That(_tracker.PhaseReachedAt(ExpeditionPhase.Unlock), Is.EqualTo(860));
            Assert.That(_tracker.PhaseReachedAt(ExpeditionPhase.PassThrough), Is.EqualTo(1000));
            Assert.That(_tracker.PhaseReachedAt(ExpeditionPhase.Complete), Is.EqualTo(1010));
        }

        [Test]
        public void GateShuts_FinishersStartANewRunAtTheHigherPrice()
        {
            _wallet.PickUp(EssenceColor.Yellow, 40);
            _wallet.BankAll();
            Assert.That(_gate.Unlock(_wallet, 100), Is.EqualTo(GateUnlockOutcome.Unlocked));
            Assert.That(_tracker.Update(110, _gate.Site.Point.World).Text, Does.StartWith("Gate open for 80s"));
            Assert.That(_tracker.PassedGate(120));
            Assert.That(_tracker.Update(130, _gate.Site.Point.World).Text, Does.Contain("reopening costs 60 Yellow"));

            Assert.That(_gate.Step(190), Is.True);
            _tracker.GateClosed(190);
            Assert.That(_tracker.IsComplete, Is.False);
            Assert.That(_tracker.StartedAt, Is.EqualTo(190));
            var o = _tracker.Update(200, _map.StartPoint.World);
            Assert.That(o.Phase, Is.EqualTo(ExpeditionPhase.Gather));
            Assert.That(o.Text, Does.StartWith("Gather Yellow essence: 0/60"));
        }

        [Test]
        public void BleedingOutCanSendYouBackToGathering()
        {
            _wallet.PickUp(EssenceColor.Yellow, 41);
            Assert.That(_tracker.Update(10, _map.StartPoint.World).Phase, Is.EqualTo(ExpeditionPhase.Bank));

            var caches = new CacheRegistry();
            caches.DropFrom(_wallet, _map.StartPoint.World, new ReviveSettings().EssenceDropShare, 20);
            Assert.That(_tracker.Update(30, _map.StartPoint.World).Phase, Is.EqualTo(ExpeditionPhase.Gather));
            Assert.That(_tracker.PhaseReachedAt(ExpeditionPhase.Bank), Is.EqualTo(10), "first reach is kept");
        }

        [Test]
        public void Gathering_PointsAtWispsOrANearbyYellowBloom()
        {
            var here = _map.StartPoint.World;
            var o = _tracker.Update(0, here);
            var bloom = _blooms.NearestActive(here, EssenceColor.Yellow, 0, 1);
            var expected = bloom != null ? bloom.Point.World : _map.NearestSpawnPoint(here, EnemyFamily.Wisp).Point.World;
            Assert.That(o.Target, Is.EqualTo(expected));

            Assert.That(_blooms.TryGetNext(0, out var next, EssenceColor.Yellow));
            double mid = (next.Start + next.End) / 2;
            o = _tracker.Update(mid, next.Point.World + new Vector2(3, 3));
            Assert.That(o.Target, Is.EqualTo(_blooms.NearestActive(next.Point.World + new Vector2(3, 3), EssenceColor.Yellow, mid, 1).Point.World));
            Assert.That(o.TargetLabel, Does.StartWith("the Yellow bloom near "));
        }

        [Test]
        public void Banking_AfterAReset_SaysHowLongUntilYouCanBank()
        {
            var banking = new Banking(_map);
            var tracker = new ExpeditionTracker(_map, _gate, _wallet, 0, _blooms, null, banking);
            _wallet.PickUp(EssenceColor.Yellow, 40);
            banking.PositionReset(_wallet.PlayerId, 100);

            var o = tracker.Update(117.5, _map.StartPoint.World);
            Assert.That(o.Phase, Is.EqualTo(ExpeditionPhase.Bank));
            Assert.That(o.Text, Does.StartWith("Can't bank your essence for 43s (you reset)"));
            Assert.That(o.Target, Is.EqualTo(_gate.Site.Point.World));

            o = tracker.Update(160, _map.StartPoint.World);
            Assert.That(o.Text, Does.StartWith("Take your Yellow to the gate"));
        }

        [Test]
        public void TimerCountsTowardTwentyMinutes()
        {
            Assert.That(_tracker.TimerText(754.9), Is.EqualTo("12:34 / 20:00"));
            Assert.That(_tracker.Remaining(600), Is.EqualTo(600));
        }
    }
}
