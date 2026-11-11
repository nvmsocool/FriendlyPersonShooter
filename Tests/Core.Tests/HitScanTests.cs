using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class HitScanTests
    {
        [Test]
        public void RaySphere_HitsFrontSurface()
        {
            Assert.That(HitGeometry.RaySphere(Vector3.Zero, Vector3.UnitZ, new Vector3(0, 0, 10), 1, out float d), Is.True);
            Assert.That(d, Is.EqualTo(9).Within(1e-5));
        }

        [Test]
        public void RaySphere_MissesBesideAndBehind()
        {
            Assert.That(HitGeometry.RaySphere(Vector3.Zero, Vector3.UnitZ, new Vector3(1.01f, 0, 10), 1, out _), Is.False);
            Assert.That(HitGeometry.RaySphere(Vector3.Zero, Vector3.UnitZ, new Vector3(0, 0, -10), 1, out _), Is.False);
        }

        [Test]
        public void RaySphere_InsideHitsAtZero()
        {
            Assert.That(HitGeometry.RaySphere(Vector3.Zero, Vector3.UnitZ, new Vector3(0, 0.5f, 0), 1, out float d), Is.True);
            Assert.That(d, Is.EqualTo(0));
        }

        [Test]
        public void RayCapsule_HitsSideAndCaps()
        {
            var a = new Vector3(0, 0, 10);
            var b = new Vector3(0, 2, 10);
            Assert.That(HitGeometry.RayCapsule(new Vector3(0, 1, 0), Vector3.UnitZ, a, b, 0.5f, out float side), Is.True);
            Assert.That(side, Is.EqualTo(9.5f).Within(1e-4));
            Assert.That(HitGeometry.RayCapsule(new Vector3(0, 2.4f, 0), Vector3.UnitZ, a, b, 0.5f, out float cap), Is.True);
            Assert.That(cap, Is.GreaterThan(9.5f).And.LessThan(10f));
            Assert.That(HitGeometry.RayCapsule(new Vector3(0, 2.6f, 0), Vector3.UnitZ, a, b, 0.5f, out _), Is.False);
            Assert.That(HitGeometry.RayCapsule(new Vector3(0.6f, 1, 0), Vector3.UnitZ, a, b, 0.5f, out _), Is.False);
        }

        [Test]
        public void Cast_PicksNearestTarget()
        {
            var targets = new List<HitTarget>
            {
                HitTarget.Body(1, new Vector3(0, 0, 20), 1),
                HitTarget.Body(2, new Vector3(0, 0, 10), 1),
                HitTarget.Body(3, new Vector3(5, 0, 5), 1),
            };
            Assert.That(HitScan.Cast(Vector3.Zero, Vector3.UnitZ, 100, targets, out var hit), Is.True);
            Assert.That(hit.TargetId, Is.EqualTo(2));
            Assert.That(hit.Index, Is.EqualTo(1));
            Assert.That(hit.Distance, Is.EqualTo(9).Within(1e-5));
            Assert.That(hit.Point.Z, Is.EqualTo(9).Within(1e-5));
        }

        [Test]
        public void Cast_RespectsMaxRangeHittableAndIgnore()
        {
            var near = HitTarget.Body(1, new Vector3(0, 0, 10), 1);
            var far = HitTarget.Body(2, new Vector3(0, 0, 30), 1, isPlayer: true);
            var targets = new List<HitTarget> { near, far };
            Assert.That(HitScan.Cast(Vector3.Zero, Vector3.UnitZ, 5, targets, out _), Is.False);

            near.Hittable = false;
            targets[0] = near;
            Assert.That(HitScan.Cast(Vector3.Zero, Vector3.UnitZ, 100, targets, out var hit), Is.True);
            Assert.That(hit.TargetId, Is.EqualTo(2));

            Assert.That(HitScan.Cast(Vector3.Zero, Vector3.UnitZ, 100, targets, out _, ignoreId: 2), Is.False);
        }

        [Test]
        public void Cast_IgnoresOnlyTheShootingPlayer_NotAnEnemyWithTheSameNumber()
        {
            // Player 3 shooting enemy 3: enemy and player ids are counted separately, so it must still hit.
            var enemy = HitTarget.Body(3, new Vector3(0, 0, 10), 1);
            var targets = new List<HitTarget> { enemy };
            Assert.That(HitScan.Cast(Vector3.Zero, Vector3.UnitZ, 100, targets, out var hit, ignoreId: 3), Is.True);
            Assert.That(hit.TargetId, Is.EqualTo(3));
        }

        [Test]
        public void Cast_DetectsWeakPointInsideBody()
        {
            var skitter = new SkitterSettings();
            var center = new Vector3(0, 1, 10);
            var t = HitTarget.WithWeakPoint(5, center, skitter.BodyRadius, center, skitter.CoreRadius);
            var targets = new List<HitTarget> { t };

            Assert.That(HitScan.Cast(new Vector3(0, 1, 0), Vector3.UnitZ, 100, targets, out var core), Is.True);
            Assert.That(core.WeakPoint, Is.True);
            Assert.That(core.Distance, Is.EqualTo(10 - skitter.BodyRadius).Within(1e-4), "distance is the body surface");

            Assert.That(HitScan.Cast(new Vector3(0, 1.4f, 0), Vector3.UnitZ, 100, targets, out var body), Is.True);
            Assert.That(body.WeakPoint, Is.False);
        }

        [Test]
        public void Cast_WeakPointStickingOutCountsOnItsOwn()
        {
            var t = HitTarget.WithWeakPoint(5, new Vector3(0, 0, 10), 0.5f, new Vector3(0, 0.8f, 10), 0.3f);
            Assert.That(HitScan.Cast(new Vector3(0, 0.9f, 0), Vector3.UnitZ, 100, new[] { t }, out var hit), Is.True);
            Assert.That(hit.WeakPoint, Is.True);
        }

        [Test]
        public void Cast_UnnormalizedDirectionWorks()
        {
            var targets = new[] { HitTarget.Body(1, new Vector3(0, 0, 10), 1) };
            Assert.That(HitScan.Cast(Vector3.Zero, new Vector3(0, 0, 5), 100, targets, out var hit), Is.True);
            Assert.That(hit.Distance, Is.EqualTo(9).Within(1e-5));
        }
    }
}
