using FriendlyPersonShooter.Core.Arsenal;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Loot boxes offer three items to pick one from.</summary>
    public class LootBoxChoiceTests
    {
        [Test]
        public void BoxOffersThreeDifferentItems()
        {
            int guns = 0;
            for (uint seed = 1; seed < 500; seed++)
            {
                var items = LootRolls.BoxChoices(seed);
                Assert.That(items.Length, Is.EqualTo(LootRolls.BoxChoiceCount));
                for (int i = 0; i < items.Length; i++)
                {
                    Assert.That(items[i].IsNone, Is.False, $"seed {seed}: an empty choice");
                    Assert.That(LootItem.Unpack(items[i].Pack()).Pack(), Is.EqualTo(items[i].Pack()), "survives the network");
                    if (items[i].IsGun) guns++;
                    for (int j = 0; j < i; j++)
                        Assert.That(LootRolls.SameKind(items[i], items[j]), Is.False, $"seed {seed}: {items[i]} twice");
                }
            }
            Assert.That(guns, Is.GreaterThan(200), "guns still turn up in boxes");
        }

        [Test]
        public void BossRewardOffersThreeRareOrBetter()
        {
            for (uint seed = 1; seed < 300; seed++)
            {
                var items = LootRolls.BossChoices(seed);
                Assert.That(items.Length, Is.EqualTo(LootRolls.BoxChoiceCount));
                for (int i = 0; i < items.Length; i++)
                {
                    Assert.That(items[i].IsNone, Is.False);
                    if (!items[i].IsGun) Assert.That(items[i].Mod.Tier, Is.GreaterThanOrEqualTo(ModTier.Rare), $"seed {seed}: {items[i]}");
                    for (int j = 0; j < i; j++) Assert.That(LootRolls.SameKind(items[i], items[j]), Is.False);
                }
            }
        }

        [Test]
        public void BoxOfferIsTheSameForTheSameSeed()
        {
            var a = LootRolls.BoxChoices(42);
            var b = LootRolls.BoxChoices(42);
            for (int i = 0; i < a.Length; i++) Assert.That(a[i].Pack(), Is.EqualTo(b[i].Pack()));
        }
    }
}
