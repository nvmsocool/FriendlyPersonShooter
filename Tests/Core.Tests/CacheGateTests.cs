using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class CacheGateTests
    {
        [Test]
        public void Cache_ReturnedByAStrangerGoesToOwnersBank_AndEarnsKudos()
        {
            var registry = new CacheRegistry();
            var owner = new EssenceWallet(1);
            owner.PickUp(EssenceColor.Yellow, 20);
            var cache = registry.DropFrom(owner, new Vector2(10, 10), 0.5f, 100);
            Assert.That(cache.Contents[EssenceColor.Yellow], Is.EqualTo(10));
            Assert.That(owner.Carried(EssenceColor.Yellow), Is.EqualTo(10));

            var pickup = registry.PickUp(cache.Id, 2, 200);
            Assert.That(pickup.Outcome, Is.EqualTo(CachePickupOutcome.Returned));
            Assert.That(pickup.KudosEarned, Is.EqualTo(1));
            pickup.DeliverTo(owner);
            Assert.That(owner.Banked(EssenceColor.Yellow), Is.EqualTo(10));
            Assert.That(owner.Carried(EssenceColor.Yellow), Is.EqualTo(10));
            Assert.That(registry.KudosOf(2), Is.EqualTo(1));
            Assert.That(registry.ReturnsBy(2), Is.EqualTo(1));
            Assert.That(registry.KudosOf(1), Is.EqualTo(0));
            Assert.That(registry.PickUp(cache.Id, 3, 210).Outcome, Is.EqualTo(CachePickupOutcome.NotFound));
        }

        [Test]
        public void Cache_OwnerRecoversToCarried_WithoutKudos()
        {
            var registry = new CacheRegistry();
            var owner = new EssenceWallet(1);
            var cache = registry.Drop(1, Vector2.Zero, new EssenceAmounts(EssenceColor.Yellow, 6), 0);
            var pickup = registry.PickUp(cache.Id, 1, 10);
            Assert.That(pickup.Outcome, Is.EqualTo(CachePickupOutcome.Recovered));
            Assert.That(pickup.KudosEarned, Is.EqualTo(0));
            pickup.DeliverTo(owner);
            Assert.That(owner.Carried(EssenceColor.Yellow), Is.EqualTo(6));
            Assert.That(registry.KudosOf(1), Is.EqualTo(0));
        }

        [Test]
        public void Cache_ExpiresAfterItsLifetime()
        {
            var registry = new CacheRegistry(new CacheSettings { Lifetime = 60 });
            var a = registry.Drop(1, Vector2.Zero, new EssenceAmounts(EssenceColor.Yellow, 3), 0);
            var b = registry.Drop(1, Vector2.Zero, new EssenceAmounts(EssenceColor.Yellow, 3), 30);
            Assert.That(registry.ExpireOld(59), Is.Empty);
            var expired = registry.ExpireOld(60);
            Assert.That(expired.Count, Is.EqualTo(1));
            Assert.That(expired[0].Id, Is.EqualTo(a.Id));
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.PickUp(b.Id, 2, 95).Outcome, Is.EqualTo(CachePickupOutcome.Expired));
            Assert.That(registry.KudosOf(2), Is.EqualTo(0));
        }

        [Test]
        public void Cache_EmptyDropMakesNoCache()
        {
            var registry = new CacheRegistry();
            var wallet = new EssenceWallet(1);
            wallet.PickUp(EssenceColor.Yellow, 1);
            Assert.That(registry.DropFrom(wallet, Vector2.Zero, 0.5f, 0), Is.Null);
            Assert.That(registry.Count, Is.EqualTo(0));
        }

        private static GateSite Site() => new GateSite(0, new MapPoint(new ZoneCoord(2, 2), new Vector2(50, 50), 250f));

        [Test]
        public void Gate_UnlockPaysFromBankedAndGrantsPersonalAccess()
        {
            var gate = new Gate(Site(), 1);
            var a = new EssenceWallet(1);
            a.PickUp(EssenceColor.Yellow, 45);
            Assert.That(gate.Unlock(a), Is.EqualTo(GateUnlockOutcome.NotEnough), "carried doesn't count");
            Assert.That(gate.Shortfall(a), Is.EqualTo(40));
            a.BankAll();
            Assert.That(gate.Shortfall(a), Is.EqualTo(0));
            Assert.That(gate.Unlock(a), Is.EqualTo(GateUnlockOutcome.Unlocked));
            Assert.That(a.Banked(EssenceColor.Yellow), Is.EqualTo(5));
            Assert.That(gate.HasAccess(1));
            Assert.That(gate.HasAccess(2), Is.False);
            Assert.That(gate.Unlock(a), Is.EqualTo(GateUnlockOutcome.AlreadyOpen));
            Assert.That(a.Banked(EssenceColor.Yellow), Is.EqualTo(5));
        }

        [Test]
        public void Gate_GroupPassesOnlyWhenEveryoneHasAccess_GiftingFixesIt()
        {
            var gate = new Gate(Site(), 1, new GateSettings { Color = EssenceColor.Yellow, Cost = 40 });
            var a = new EssenceWallet(1);
            var b = new EssenceWallet(2);
            a.PickUp(EssenceColor.Yellow, 70);
            a.BankAll();
            b.PickUp(EssenceColor.Yellow, 10);
            b.BankAll();
            gate.Unlock(a);

            Assert.That(gate.CanGroupPass(new[] { 1, 2 }), Is.False);
            Assert.That(gate.MissingAccess(new[] { 1, 2 }), Is.EqualTo(new[] { 2 }));
            Assert.That(gate.Unlock(b), Is.EqualTo(GateUnlockOutcome.NotEnough));

            Assert.That(a.TryGift(b, EssenceColor.Yellow, gate.Shortfall(b), GiftSource.BankedOnly));
            Assert.That(gate.Unlock(b), Is.EqualTo(GateUnlockOutcome.Unlocked));
            Assert.That(gate.CanGroupPass(new[] { 1, 2 }));
            Assert.That(gate.CanGroupPass(new int[0]), Is.False);
            Assert.That(gate.CanGoUp(3), "going up is always free");
        }

        [Test]
        public void Gate_AccessResetsWithTheSeed()
        {
            var gate = new Gate(Site(), 1);
            var a = new EssenceWallet(1);
            a.PickUp(EssenceColor.Yellow, 80);
            a.BankAll();
            gate.Unlock(a);
            Assert.That(gate.TryGetAccessSeed(1, out uint bought) && bought == 1u);

            gate.Reseed(2);
            Assert.That(gate.HasAccess(1), Is.False);
            Assert.That(gate.CanGroupPass(new[] { 1 }), Is.False);
            Assert.That(gate.Unlock(a), Is.EqualTo(GateUnlockOutcome.Unlocked));
            Assert.That(a.Banked(EssenceColor.Yellow), Is.EqualTo(0));
            Assert.That(gate.HasAccess(1));
        }

        [Test]
        public void Gate_ShutsAfterItsOpenTime_AndCostsMoreEachTime()
        {
            var gate = new Gate(Site(), 1, new GateSettings { Cost = 40, CostStep = 20, OpenSeconds = 90 });
            var a = new EssenceWallet(1);
            a.PickUp(EssenceColor.Yellow, 200);
            a.BankAll();
            Assert.That(gate.IsOpen, Is.False);
            Assert.That(gate.Unlock(a, 10), Is.EqualTo(GateUnlockOutcome.Unlocked));
            Assert.That(gate.IsOpen);
            Assert.That(gate.OpenLeft(40), Is.EqualTo(60).Within(1e-9));
            gate.GrantAccess(2);
            Assert.That(gate.Step(99), Is.False);
            Assert.That(gate.HasAccess(2));

            Assert.That(gate.Step(100), Is.True);
            Assert.That(gate.IsOpen, Is.False);
            Assert.That(gate.HasAccess(1) || gate.HasAccess(2), Is.False, "everyone's access ends");
            Assert.That(gate.Cost, Is.EqualTo(60));
            Assert.That(gate.Step(500), Is.False, "a shut gate stays shut");

            Assert.That(gate.Unlock(a, 600), Is.EqualTo(GateUnlockOutcome.Unlocked));
            Assert.That(a.Banked(EssenceColor.Yellow), Is.EqualTo(100));
            Assert.That(gate.Step(690), Is.True);
            Assert.That(gate.Cost, Is.EqualTo(80));
        }
    }
}
