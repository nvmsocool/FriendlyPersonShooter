using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class SlotBoardTests
    {
        [Test]
        public void ShootingAnEmptySlotLightsThatOne()
        {
            var b = new SlotBoard();
            b.Sync(40, 0);
            Assert.That(b.Light(17), Is.True);
            Assert.That(b.IsLit(17) && !b.IsLit(0), Is.True);
            Assert.That(b.Light(17), Is.False, "a lit slot can't be filled again");
            Assert.That(b.LitCount, Is.EqualTo(1));
            // The amount now matches, so syncing changes nothing.
            b.Sync(40, 1);
            Assert.That(b.IsLit(17), Is.True);
        }

        [Test]
        public void EssenceFromElsewhereLightsTheFirstEmptySlots()
        {
            var b = new SlotBoard();
            b.Sync(10, 0);
            b.Light(0);
            b.Light(5);
            b.Sync(10, 4);
            Assert.That(b.Bits(), Is.EqualTo(0b100111UL));
        }

        [Test]
        public void SpendingDimsTheLastLitSlots()
        {
            var b = new SlotBoard();
            b.Sync(40, 40);
            // The gate is paid and the next price is 60: the bank is empty.
            b.Sync(60, 0);
            Assert.That(b.Count, Is.EqualTo(60));
            Assert.That(b.LitCount, Is.EqualTo(0));
            b.Light(3);
            b.Light(9);
            b.Sync(60, 1);
            Assert.That(b.IsLit(3) && !b.IsLit(9), Is.True);
        }

        [Test]
        public void AmountIsClampedToTheSlots()
        {
            var b = new SlotBoard();
            b.Sync(8, 20);
            Assert.That(b.LitCount, Is.EqualTo(8));
            b.Sync(5, 20);
            Assert.That(b.LitCount, Is.EqualTo(5));
            Assert.That(b.Light(-1) || b.Light(5), Is.False);
        }

        [Test]
        public void BitsCoverSlotsPast64()
        {
            var b = new SlotBoard();
            b.Sync(80, 0);
            b.Light(70);
            Assert.That(b.Bits(0), Is.EqualTo(0UL));
            Assert.That(b.Bits(64), Is.EqualTo(1UL << 6));
        }
    }

    public class RaidKeySlotTests
    {
        [Test]
        public void ShootingAKeySlotPutsInOneEssence_CarriedFirst()
        {
            var bank = new EssenceAmounts();
            var w = new EssenceWallet(1, bank);
            w.PickUp(EssenceColor.Blue, 1);
            w.PickUp(EssenceColor.Blue, 1);
            w.BankSome(EssenceColor.Blue, 1);
            var door = new RaidDoor();
            Assert.That(door.PledgeOne(w, EssenceColor.Blue), Is.True);
            Assert.That(w.Carried(EssenceColor.Blue), Is.EqualTo(0), "carried goes first");
            Assert.That(w.Banked(EssenceColor.Blue), Is.EqualTo(1));
            Assert.That(door.PledgeOne(w, EssenceColor.Blue), Is.True);
            Assert.That(door.PledgeOne(w, EssenceColor.Blue), Is.False, "nothing left");
            Assert.That(door.Pledged(EssenceColor.Blue), Is.EqualTo(2));
        }

        [Test]
        public void AFullColorOrANonKeyColorTakesNothing()
        {
            var w = new EssenceWallet(1);
            w.PickUp(EssenceColor.Yellow, 5);
            w.PickUp(EssenceColor.Purple, 5);
            var door = new RaidDoor(new RaidSettings { PurpleCost = 2 });
            Assert.That(door.PledgeOne(w, EssenceColor.Yellow), Is.False);
            Assert.That(door.PledgeOne(w, EssenceColor.Purple) && door.PledgeOne(w, EssenceColor.Purple), Is.True);
            Assert.That(door.PledgeOne(w, EssenceColor.Purple), Is.False);
            Assert.That(w.Carried(EssenceColor.Purple), Is.EqualTo(3));
        }
    }
}
