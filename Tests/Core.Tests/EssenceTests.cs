using System.Linq;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class EssenceTests
    {
        [Test]
        public void Colors_MapToFamiliesBothWays()
        {
            Assert.That(EssenceColors.Of(EnemyFamily.Wisp), Is.EqualTo(EssenceColor.Yellow));
            Assert.That(EssenceColors.Of(EnemyFamily.Skitter), Is.EqualTo(EssenceColor.Red));
            Assert.That(EssenceColors.Of(EnemyFamily.Mite), Is.EqualTo(EssenceColor.Green));
            Assert.That(EssenceColors.TryGetFamily(EssenceColor.Red, out var f) && f == EnemyFamily.Skitter);
            Assert.That(EssenceColors.TryGetFamily(EssenceColor.Blue, out _), Is.False);
            Assert.That(EssenceColors.All.Count, Is.EqualTo(6));
        }

        [Test]
        public void Wallet_PickUpIsCarriedUntilBanked()
        {
            var w = new EssenceWallet(1);
            w.PickUp(EssenceColor.Yellow, 5);
            w.PickUp(EssenceColor.Red, 2);
            Assert.That(w.Carried(EssenceColor.Yellow), Is.EqualTo(5));
            Assert.That(w.Banked(EssenceColor.Yellow), Is.EqualTo(0));

            var moved = w.BankAll();
            Assert.That(moved[EssenceColor.Yellow], Is.EqualTo(5));
            Assert.That(moved[EssenceColor.Red], Is.EqualTo(2));
            Assert.That(w.Carried(EssenceColor.Yellow), Is.EqualTo(0));
            Assert.That(w.Banked(EssenceColor.Yellow), Is.EqualTo(5));
            Assert.That(w.Total(EssenceColor.Red), Is.EqualTo(2));
        }

        [Test]
        public void Wallet_SpendsOnlyBankedEssence()
        {
            var w = new EssenceWallet(1);
            w.PickUp(EssenceColor.Yellow, 50);
            Assert.That(w.TrySpend(EssenceColor.Yellow, 40), Is.False, "carried can't be spent");
            Assert.That(w.Carried(EssenceColor.Yellow), Is.EqualTo(50));

            w.BankAll();
            Assert.That(w.TrySpend(EssenceColor.Yellow, 40), Is.True);
            Assert.That(w.Banked(EssenceColor.Yellow), Is.EqualTo(10));
            Assert.That(w.TrySpend(EssenceColor.Yellow, 11), Is.False);
            Assert.That(w.Banked(EssenceColor.Yellow), Is.EqualTo(10));
        }

        [Test]
        public void Gift_CarriedFirstMovesPoolToPool()
        {
            var a = new EssenceWallet(1);
            var b = new EssenceWallet(2);
            a.PickUp(EssenceColor.Yellow, 10);
            a.BankAll();
            a.PickUp(EssenceColor.Yellow, 3);

            Assert.That(a.TryGift(b, EssenceColor.Yellow, 5), Is.True);
            Assert.That(a.Carried(EssenceColor.Yellow), Is.EqualTo(0));
            Assert.That(a.Banked(EssenceColor.Yellow), Is.EqualTo(8));
            Assert.That(b.Carried(EssenceColor.Yellow), Is.EqualTo(3), "carried arrives carried");
            Assert.That(b.Banked(EssenceColor.Yellow), Is.EqualTo(2), "banked arrives banked");
        }

        [Test]
        public void Gift_RespectsSourceAndIsAllOrNothing()
        {
            var a = new EssenceWallet(1);
            var b = new EssenceWallet(2);
            a.PickUp(EssenceColor.Yellow, 4);
            a.BankAll();
            a.PickUp(EssenceColor.Yellow, 2);

            Assert.That(a.TryGift(b, EssenceColor.Yellow, 3, GiftSource.CarriedOnly), Is.False);
            Assert.That(a.TryGift(b, EssenceColor.Yellow, 7), Is.False);
            Assert.That(a.TryGift(a, EssenceColor.Yellow, 1), Is.False, "no gifting yourself");
            Assert.That(a.Total(EssenceColor.Yellow), Is.EqualTo(6));

            Assert.That(a.TryGift(b, EssenceColor.Yellow, 4, GiftSource.BankedOnly), Is.True);
            Assert.That(a.Carried(EssenceColor.Yellow), Is.EqualTo(2));
            Assert.That(b.Banked(EssenceColor.Yellow), Is.EqualTo(4));
        }

        [Test]
        public void DropCache_TakesHalfOfCarriedRoundedDown_NeverBanked()
        {
            var w = new EssenceWallet(1);
            w.PickUp(EssenceColor.Yellow, 20);
            w.BankAll();
            w.PickUp(EssenceColor.Yellow, 9);
            w.PickUp(EssenceColor.Red, 1);

            var dropped = w.DropCache(new ReviveSettings().EssenceDropShare);
            Assert.That(dropped[EssenceColor.Yellow], Is.EqualTo(4));
            Assert.That(dropped[EssenceColor.Red], Is.EqualTo(0));
            Assert.That(w.Carried(EssenceColor.Yellow), Is.EqualTo(5));
            Assert.That(w.Carried(EssenceColor.Red), Is.EqualTo(1));
            Assert.That(w.Banked(EssenceColor.Yellow), Is.EqualTo(20));
        }

        [Test]
        public void Ledger_EveryHitterGetsTheFullDrop()
        {
            var ledger = new DropLedger();
            ledger.RecordHit(100, 7);
            ledger.RecordHit(100, 3);
            ledger.RecordHit(100, 7);
            ledger.RecordHit(100, 9);

            var awards = ledger.Killed(100, EnemyFamily.Wisp);
            Assert.That(awards.Select(a => a.PlayerId), Is.EqualTo(new[] { 7, 3, 9 }));
            Assert.That(awards.All(a => a.Color == EssenceColor.Yellow && a.Amount == 1));
            Assert.That(ledger.Killed(100, EnemyFamily.Wisp), Is.Empty, "the enemy is forgotten after dying");
        }

        [Test]
        public void Ledger_FractionalDropsBuildUpPerPlayer()
        {
            var ledger = new DropLedger();
            int paid = 0;
            for (int mite = 0; mite < 12; mite++)
            {
                ledger.RecordHit(mite, 1);
                foreach (var a in ledger.Killed(mite, EnemyFamily.Mite))
                {
                    Assert.That(a.Color, Is.EqualTo(EssenceColor.Green));
                    paid += a.Amount;
                }
            }
            Assert.That(paid, Is.EqualTo(3), "12 Mites at 0.25 each");
        }

        [Test]
        public void Ledger_PayAddsToCarried_AndForgetAwardsNothing()
        {
            var ledger = new DropLedger();
            var wallet = new EssenceWallet(5);
            ledger.RecordHit(1, 5);
            ledger.RecordHit(2, 5);
            ledger.Forget(2);
            DropLedger.Pay(ledger.Killed(1, EnemyFamily.Skitter), id => id == 5 ? wallet : null);
            Assert.That(wallet.Carried(EssenceColor.Red), Is.EqualTo(2));
            Assert.That(ledger.Killed(2, EnemyFamily.Skitter), Is.Empty);
        }
    }
}
