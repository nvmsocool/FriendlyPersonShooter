using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Gallery;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class BankRunTests
    {
        private static EssenceWallet Wallet(EssenceColor color, int carried)
        {
            var w = new EssenceWallet(1);
            w.PickUp(color, carried);
            return w;
        }

        [Test]
        public void HitsBankAndMissesAreLost()
        {
            var w = Wallet(EssenceColor.Blue, 12);
            w.PickUp(EssenceColor.Red, 4);
            var run = new BankRun(EssenceColor.Blue, w.Carried(EssenceColor.Blue));
            Assert.That(run.Targets, Is.EqualTo(3));
            Assert.That(run.Hit(0, w), Is.EqualTo(5));
            Assert.That(run.Miss(1, w), Is.EqualTo(5));
            Assert.That(run.Hit(2, w), Is.EqualTo(2));
            Assert.That(w.Banked(EssenceColor.Blue), Is.EqualTo(7));
            Assert.That(w.Carried(EssenceColor.Blue), Is.EqualTo(0));
            Assert.That(run.Banked, Is.EqualTo(7));
            Assert.That(run.Lost, Is.EqualTo(5));
            Assert.That(w.Carried(EssenceColor.Red), Is.EqualTo(4), "other colors are untouched");
        }

        [Test]
        public void EachTargetCountsOnce()
        {
            var w = Wallet(EssenceColor.Green, 10);
            var run = new BankRun(EssenceColor.Green, 10);
            run.Hit(0, w);
            Assert.That(run.Hit(0, w), Is.EqualTo(0));
            Assert.That(run.Miss(0, w), Is.EqualTo(0));
            Assert.That(w.Banked(EssenceColor.Green), Is.EqualTo(5));
        }

        [Test]
        public void EssencePickedUpMeanwhileIsKept()
        {
            var w = Wallet(EssenceColor.Yellow, 5);
            var run = new BankRun(EssenceColor.Yellow, 5);
            w.PickUp(EssenceColor.Yellow, 3);
            Assert.That(run.Targets, Is.EqualTo(1));
            run.Miss(0, w);
            Assert.That(w.Carried(EssenceColor.Yellow), Is.EqualTo(3));
        }

        [Test]
        public void NeverTakesMoreThanCarried()
        {
            var w = Wallet(EssenceColor.Orange, 8);
            var run = new BankRun(EssenceColor.Orange, 8);
            w.LoseCarried(EssenceColor.Orange, 6);
            Assert.That(run.Hit(0, w), Is.EqualTo(2));
            Assert.That(run.Miss(1, w), Is.EqualTo(0));
            Assert.That(w.Carried(EssenceColor.Orange), Is.EqualTo(0));
        }

        [Test]
        public void ARunPlaysAsASequenceGallery()
        {
            var w = Wallet(EssenceColor.Purple, 14);
            var run = new BankRun(EssenceColor.Purple, 14);
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Bank, 1, run.Targets), 3);
            var missed = new List<int>();
            var o = g.Offset(0, 0.5f);
            var shot = g.Shoot(o.X, o.Y, o.Y, 0.5f);
            if (shot.Counted) run.Hit(shot.Target, w);
            g.Step(100f, missed);
            foreach (int i in missed) run.Miss(i, w);
            Assert.That(g.Over, Is.True);
            Assert.That(w.Banked(EssenceColor.Purple), Is.EqualTo(5));
            Assert.That(w.Carried(EssenceColor.Purple), Is.EqualTo(0));
            Assert.That(run.Lost, Is.EqualTo(9));
        }
    }
}
