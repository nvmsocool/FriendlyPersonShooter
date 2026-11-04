using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class BankingTests
    {
        private LayerMap _map;
        private Banking _banking;
        private Vector2 _door;
        private EssenceWallet _wallet;

        [SetUp]
        public void SetUp()
        {
            _map = LayerGenerator.Generate(2026, LayerSettings.MvpGrid(), LayerSettings.ForMvp());
            _door = RaidSite.DoorFor(_map);
            _banking = new Banking(_map);
            _banking.AddSite(_door, 7f);
            foreach (var g in _map.Gates) _banking.AddSite(g.Point.World, 6f);
            _wallet = new EssenceWallet(1);
            _wallet.PickUp(EssenceColor.Yellow, 12);
            _wallet.PickUp(EssenceColor.Red, 3);
        }

        [Test]
        public void RaidDoorBanksEverythingCarried()
        {
            var banked = _banking.TryBank(_wallet, _door + new Vector2(3, 0), 0);
            Assert.That(banked.Total, Is.EqualTo(15));
            Assert.That(_wallet.Banked(EssenceColor.Yellow), Is.EqualTo(12));
            Assert.That(_wallet.CarriedAll().IsEmpty);
        }

        [Test]
        public void GateBanks()
        {
            var gate = _map.Gates[0].Point.World;
            Assert.That(_banking.TryBank(_wallet, gate, 0).Total, Is.EqualTo(15));
            Assert.That(_wallet.Banked(EssenceColor.Yellow), Is.EqualTo(12));
        }

        [Test]
        public void ShrinesDontBank()
        {
            var shrine = _map.Shrines.First(s => !_banking.IsAtBank(s.Point.World)).Point.World;
            Assert.That(_banking.TryBank(_wallet, shrine, 0).IsEmpty);
            Assert.That(_wallet.Carried(EssenceColor.Yellow), Is.EqualTo(12));
            Assert.That(_wallet.Banked(EssenceColor.Yellow), Is.EqualTo(0));
        }

        [Test]
        public void FarFromEveryDoor_NothingBanks()
        {
            Assert.That(_banking.TryBank(_wallet, _door + new Vector2(8, 0), 0).IsEmpty);
        }

        [Test]
        public void AfterAReset_NoBankingForAMinute_AndYouKeepYourEssence()
        {
            _banking.PositionReset(1, 100);
            Assert.That(_banking.CanBank(1, 100), Is.False);
            Assert.That(_banking.LockedFor(1, 118), Is.EqualTo(42).Within(1e-9));
            Assert.That(_banking.TryBank(_wallet, _door, 159.9).IsEmpty);
            Assert.That(_wallet.Carried(EssenceColor.Yellow), Is.EqualTo(12));

            Assert.That(_banking.CanBank(1, 160));
            Assert.That(_banking.TryBank(_wallet, _door, 160).Total, Is.EqualTo(15));
        }

        [Test]
        public void ALockoutIsPersonal()
        {
            _banking.PositionReset(2, 0);
            Assert.That(_banking.CanBank(1, 0));
            Assert.That(_banking.TryBank(_wallet, _door, 1).Total, Is.EqualTo(15));
        }

        [Test]
        public void ResettingAgainRestartsTheLockout_AndForgetClearsIt()
        {
            _banking.PositionReset(1, 0);
            _banking.PositionReset(1, 50);
            Assert.That(_banking.CanBank(1, 100), Is.False);
            _banking.Forget(1);
            Assert.That(_banking.CanBank(1, 100));
        }

        [Test]
        public void LockoutTextRoundsUp()
        {
            Assert.That(Banking.LockoutText(41.2), Is.EqualTo("Can't bank your essence for 42s (you reset)"));
        }

        [Test]
        public void NothingCarried_BanksNothing()
        {
            var empty = new EssenceWallet(3);
            Assert.That(_banking.TryBank(empty, _door, 0).IsEmpty);
        }
    }
}
