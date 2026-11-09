using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Several raid doors, one open at a time: a cleared door goes dark and the next one opens.</summary>
    public class RaidDoorRotationTests
    {
        [Test]
        public void LayerHasSeveralDoorsSpreadOut()
        {
            foreach (uint seed in new uint[] { 2026, 7, 99 })
            {
                var map = LayerGenerator.Generate(seed, LayerSettings.MvpTiling(), LayerSettings.ForPlaytest());
                var doors = RaidSite.DoorsFor(map);
                Assert.That(doors.Count, Is.EqualTo(RaidSite.DoorCount), $"seed {seed}");
                Assert.That(doors[0], Is.EqualTo(RaidSite.DoorFor(map)), "the first door is the one the platforms float above");
                for (int i = 0; i < doors.Count; i++)
                {
                    Assert.That(RaidSite.ClearOfSeam(map, doors[i]), Is.True);
                    for (int j = 0; j < i; j++) Assert.That(map.Distance(doors[i], doors[j]), Is.GreaterThan(RaidSite.MinApart), $"seed {seed}: doors {i} and {j}");
                }
                Assert.That(RaidSite.DoorsFor(map), Is.EqualTo(doors), "same map, same doors");
            }
        }

        [Test]
        public void ClearingSpendsTheDoorAndOpensTheNext()
        {
            var r = new RaidDoorRotation(4);
            Assert.That(r.Active, Is.EqualTo(0));
            var opened = new List<int> { r.Active };
            for (int i = 0; i < 3; i++)
            {
                int was = r.Active;
                int next = r.Cleared();
                Assert.That(r.IsSpent(was), Is.True, "the cleared door goes dark");
                Assert.That(next, Is.Not.EqualTo(was));
                Assert.That(r.IsSpent(next), Is.False);
                Assert.That(opened, Does.Not.Contain(next), "a new door each time until all are spent");
                opened.Add(next);
            }
            // All four cleared: they come back, except the one just cleared.
            int last = r.Active;
            int again = r.Cleared();
            Assert.That(again, Is.Not.EqualTo(last));
            Assert.That(r.IsSpent(last), Is.True);
            Assert.That(r.SpentMask, Is.EqualTo(1UL << last));
        }

        [Test]
        public void ASingleDoorStaysOpen()
        {
            var r = new RaidDoorRotation(1);
            Assert.That(r.Cleared(), Is.EqualTo(0));
            Assert.That(r.IsSpent(0), Is.False);
        }
    }
}
