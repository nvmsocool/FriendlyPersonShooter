using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class PositionHistoryTests
    {
        [Test]
        public void Sample_InterpolatesBetweenTicks()
        {
            var h = new PositionHistory(8);
            h.Record(100, new Vector3(0, 0, 0));
            h.Record(101, new Vector3(10, 0, 0));
            Assert.That(h.Sample(100, 0.25f).X, Is.EqualTo(2.5f).Within(1e-5));
            Assert.That(h.Sample(101, 0f).X, Is.EqualTo(10));
        }

        [Test]
        public void Sample_InterpolatesAcrossGaps()
        {
            var h = new PositionHistory(8);
            h.Record(10, new Vector3(0, 0, 0));
            h.Record(14, new Vector3(0, 0, 8));
            Assert.That(h.Sample(12, 0.5f).Z, Is.EqualTo(5).Within(1e-5));
        }

        [Test]
        public void Sample_ClampsToOldestAndNewest()
        {
            var h = new PositionHistory(4);
            for (int t = 0; t < 10; t++) h.Record(t, new Vector3(t, 0, 0));
            Assert.That(h.Count, Is.EqualTo(4));
            Assert.That(h.OldestTick, Is.EqualTo(6));
            Assert.That(h.NewestTick, Is.EqualTo(9));
            Assert.That(h.Sample(2, 0.5f).X, Is.EqualTo(6));
            Assert.That(h.Sample(20, 0f).X, Is.EqualTo(9));
            Assert.That(h.Sample(7, 0.5f).X, Is.EqualTo(7.5f).Within(1e-5));
        }

        [Test]
        public void Record_IgnoresOlderAndOverwritesSameTick()
        {
            var h = new PositionHistory(4);
            h.Record(5, new Vector3(1, 0, 0));
            h.Record(4, new Vector3(99, 0, 0));
            h.Record(5, new Vector3(2, 0, 0));
            Assert.That(h.Count, Is.EqualTo(1));
            Assert.That(h.Sample(5, 0).X, Is.EqualTo(2));
        }

        [Test]
        public void Sample_DoesNotSlideAcrossTeleports()
        {
            var h = new PositionHistory(8);
            h.Record(1, new Vector3(0, 0, 0), hittable: false);
            h.Record(2, new Vector3(10, 0, 0), hittable: true, teleported: true);
            var mid = h.SampleState(1, 0.9f);
            Assert.That(mid.Position.X, Is.EqualTo(0));
            Assert.That(mid.Hittable, Is.False);
            var after = h.SampleState(2, 0f);
            Assert.That(after.Position.X, Is.EqualTo(10));
            Assert.That(after.Hittable, Is.True);
        }

        [Test]
        public void Sample_EmptyHistoryReturnsDefault()
        {
            Assert.That(new PositionHistory(4).Sample(3, 0.5f), Is.EqualTo(Vector3.Zero));
        }
    }
}
