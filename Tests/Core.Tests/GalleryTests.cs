using System.Collections.Generic;
using FriendlyPersonShooter.Core.Gallery;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class GalleryTests
    {
        private static GalleryShot ShootAt(GalleryRound g, int i, float t)
        {
            var o = g.Offset(i, t);
            return g.Shoot(o.X, o.Y, o.Y, t);
        }

        [Test]
        public void SameSeedGivesSamePaths()
        {
            var spec = GalleryDifficulty.For(GalleryKind.Box, 1);
            var a = new GalleryRound(spec, 1234);
            var b = new GalleryRound(spec, 1234);
            var c = new GalleryRound(spec, 999);
            for (float t = 0; t < 10f; t += 0.37f)
            {
                for (int i = 0; i < 3; i++) Assert.That(a.Offset(i, t), Is.EqualTo(b.Offset(i, t)));
            }
            Assert.That(a.Offset(0, 2f), Is.Not.EqualTo(c.Offset(0, 2f)));
        }

        [Test]
        public void TargetsMoveButStayInView()
        {
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Gate, 3), 77);
            float moved = 0f;
            for (float t = 0; t < 10f; t += 0.1f)
            {
                for (int i = 0; i < g.Count; i++)
                {
                    var o = g.Offset(i, t);
                    Assert.That(System.Math.Abs(o.X), Is.LessThanOrEqualTo(GalleryRound.MaxYaw));
                    Assert.That(System.Math.Abs(o.Y), Is.LessThanOrEqualTo(GalleryRound.MaxPitch));
                    moved += (o - g.Offset(i, t + 0.1f)).Length();
                }
            }
            Assert.That(moved, Is.GreaterThan(50f));
        }

        [Test]
        public void ChooseTakesTheTargetHit()
        {
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Box, 1), 5);
            g.Step(2f);
            var shot = ShootAt(g, 2, 2f);
            Assert.That(shot.Hit && shot.Counted, Is.True);
            Assert.That(g.Outcome, Is.EqualTo(GalleryOutcome.Won));
            Assert.That(g.Chosen, Is.EqualTo(2));
        }

        [Test]
        public void BoxMissLosesTheBox()
        {
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Box, 1), 5);
            var shot = g.Shoot(0f, GalleryRound.MaxPitch + 10f, 0f, 1f);
            Assert.That(shot.Hit, Is.False);
            Assert.That(g.Outcome, Is.EqualTo(GalleryOutcome.Lost));
        }

        [Test]
        public void StarterHasNoLimitAndForgivesMisses()
        {
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Starter, 3), 5);
            g.Shoot(0f, GalleryRound.MaxPitch + 10f, 0f, 1f);
            g.Step(10000f);
            Assert.That(g.Over, Is.False);
            Assert.That(float.IsPositiveInfinity(g.TimeLeft(10000f)), Is.True);
            ShootAt(g, 1, 10000f);
            Assert.That(g.Chosen, Is.EqualTo(1));
        }

        [Test]
        public void TimeRunsOut()
        {
            var spec = GalleryDifficulty.For(GalleryKind.RaidStart, 1);
            var g = new GalleryRound(spec, 5);
            g.Step(spec.Seconds - 0.01f);
            Assert.That(g.Over, Is.False);
            g.Step(spec.Seconds);
            Assert.That(g.Outcome, Is.EqualTo(GalleryOutcome.Lost));
            Assert.That(ShootAt(g, 0, spec.Seconds).Hit, Is.False);
        }

        [Test]
        public void OrderedNeedsTheRightOrder()
        {
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Gate, 1), 11);
            Assert.That(g.Count, Is.EqualTo(3));
            // Targets can overlap at a given moment; find times where each stands alone.
            float t = 0.5f;
            var wrong = ShootAt(g, 1, t);
            if (wrong.Target == 1)
            {
                Assert.That(wrong.Counted, Is.False);
                Assert.That(g.Over, Is.False);
            }
            for (int i = 0; i < 3; i++)
            {
                t += 0.5f;
                Assert.That(ShootAt(g, i, t).Counted, Is.True, $"target {i}");
                Assert.That(g.Visible(i, t), Is.False);
            }
            Assert.That(g.Outcome, Is.EqualTo(GalleryOutcome.Won));
        }

        [Test]
        public void CacheNeedsEveryTargetInAnyOrder()
        {
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Cache, 2), 3);
            Assert.That(g.Count, Is.EqualTo(5));
            float t = 1f;
            foreach (int i in new[] { 4, 0, 3, 1 })
            {
                Assert.That(ShootAt(g, i, t += 0.3f).Counted, Is.True);
                Assert.That(g.Over, Is.False);
            }
            ShootAt(g, 2, t + 0.3f);
            Assert.That(g.Outcome, Is.EqualTo(GalleryOutcome.Won));
            Assert.That(g.HitCount, Is.EqualTo(5));
        }

        [Test]
        public void SequenceDealsTargetsOneAtATime()
        {
            int amount = 23;
            int n = GalleryDifficulty.BankTargets(amount);
            Assert.That(n, Is.EqualTo(5));
            var spec = GalleryDifficulty.For(GalleryKind.Bank, 1, n);
            var g = new GalleryRound(spec, 8);
            Assert.That(g.Visible(0, 0f) && !g.Visible(1, 0f), Is.True);

            // Hit the first quickly: the second comes up at once.
            Assert.That(ShootAt(g, 0, 1f).Counted, Is.True);
            Assert.That(g.Next, Is.EqualTo(1));
            Assert.That(g.Visible(1, 1f), Is.True);

            // Let the second run out: missed, the third comes up.
            var missed = new List<int>();
            g.Step(1f + spec.PerTarget, missed);
            Assert.That(missed, Is.EqualTo(new[] { 1 }));
            Assert.That(g.Next, Is.EqualTo(2));

            // Let the rest run out: the gallery ends.
            g.Step(100f, missed);
            Assert.That(missed, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(g.Over, Is.True);
            Assert.That(g.HitCount, Is.EqualTo(1));
        }

        [Test]
        public void BankChunksAddUp()
        {
            int total = 0;
            for (int i = 0; i < GalleryDifficulty.BankTargets(23); i++) total += GalleryDifficulty.BankChunkAmount(23, i);
            Assert.That(total, Is.EqualTo(23));
            Assert.That(GalleryDifficulty.BankChunkAmount(23, 4), Is.EqualTo(3));
            Assert.That(GalleryDifficulty.BankTargets(0), Is.EqualTo(0));
        }

        [Test]
        public void DeeperLayersAreHarder()
        {
            var a = GalleryDifficulty.For(GalleryKind.Box, 1);
            var c = GalleryDifficulty.For(GalleryKind.Box, 3);
            Assert.That(c.Radius, Is.LessThan(a.Radius));
            Assert.That(c.Speed, Is.GreaterThan(a.Speed));
            Assert.That(c.Seconds, Is.LessThan(a.Seconds));
            Assert.That(GalleryDifficulty.For(GalleryKind.Gate, 3).Targets, Is.GreaterThan(GalleryDifficulty.For(GalleryKind.Gate, 1).Targets));
            Assert.That(GalleryDifficulty.For(GalleryKind.Starter, 3).Seconds, Is.EqualTo(0f));
        }

        [Test]
        public void NearMissMissesAndEdgeHits()
        {
            var g = new GalleryRound(GalleryDifficulty.For(GalleryKind.Travel, 1), 2);
            var o = g.Offset(0, 1f);
            float r = g.Spec.Radius;
            Assert.That(g.Shoot(o.X, o.Y + r * 1.05f, 0f, 1f).Hit, Is.False);
            Assert.That(g.Shoot(o.X, o.Y + r * 0.95f, 0f, 1f).Hit, Is.True);
        }

        [Test]
        public void ColorOrderIsAShuffleAndStable()
        {
            var seen = new HashSet<string>();
            for (uint s = 1; s < 60; s++)
            {
                var a = GalleryRound.ColorOrder(s);
                Assert.That(a, Is.EquivalentTo(new[] { 0, 1, 2 }));
                Assert.That(GalleryRound.ColorOrder(s), Is.EqualTo(a));
                seen.Add(string.Join(",", a));
            }
            Assert.That(seen.Count, Is.EqualTo(6));
        }
    }
}
