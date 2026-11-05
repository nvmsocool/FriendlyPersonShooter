using System.Collections.Generic;
using FriendlyPersonShooter.Core.Avatars;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class RobotLookTests
    {
        [Test]
        public void SameSeed_SameRobot()
        {
            var a = new RobotLook(1234);
            var b = new RobotLook(1234);
            Assert.That(a.Head, Is.EqualTo(b.Head));
            Assert.That(a.Ears, Is.EqualTo(b.Ears));
            Assert.That(a.HeadWidth, Is.EqualTo(b.HeadWidth));
            Assert.That(a.GlowHue, Is.EqualTo(b.GlowHue));
        }

        [Test]
        public void ManySeeds_CoverEveryShape()
        {
            var heads = new HashSet<RobotHead>();
            var ears = new HashSet<RobotEars>();
            var eyes = new HashSet<RobotEyes>();
            var bodies = new HashSet<RobotBody>();
            var mouths = new HashSet<RobotMouth>();
            for (uint i = 1; i <= 200; i++)
            {
                var r = new RobotLook(RobotLook.SeedFrom(i));
                heads.Add(r.Head);
                ears.Add(r.Ears);
                eyes.Add(r.Eyes);
                bodies.Add(r.Body);
                mouths.Add(r.Mouth);
            }
            Assert.That(heads.Count, Is.EqualTo(4));
            Assert.That(ears.Count, Is.EqualTo(6));
            Assert.That(eyes.Count, Is.EqualTo(4));
            Assert.That(bodies.Count, Is.EqualTo(4));
            Assert.That(mouths.Count, Is.EqualTo(4));
        }

        [Test]
        public void NeighbouringSeeds_LookDifferent()
        {
            // Players who join one after another shouldn't get near-identical robots.
            int same = 0;
            for (uint i = 1; i <= 50; i++)
            {
                var a = new RobotLook(RobotLook.SeedFrom(i));
                var b = new RobotLook(RobotLook.SeedFrom(i + 1));
                if (a.Head == b.Head && a.Ears == b.Ears && a.Eyes == b.Eyes && a.Body == b.Body) same++;
            }
            Assert.That(same, Is.LessThan(3));
        }

        [Test]
        public void Sizes_StayInRange()
        {
            for (uint i = 1; i <= 300; i++)
            {
                var r = new RobotLook(RobotLook.SeedFrom(i));
                Assert.That(r.HeadWidth, Is.InRange(0.9f, 1.45f));
                Assert.That(r.HeadHeight, Is.InRange(0.8f, 1.35f));
                Assert.That(r.BodyWidth, Is.InRange(0.75f, 1.25f));
                Assert.That(r.HandSize, Is.InRange(0.9f, 1.5f));
                Assert.That(r.Neck, Is.InRange(0.02f, 0.24f));
                Assert.That(r.ShellLight, Is.InRange(0.8f, 1f));
            }
        }

        [Test]
        public void SeedFrom_NeverZero()
        {
            for (uint i = 0; i < 10000; i++) Assert.That(RobotLook.SeedFrom(i), Is.Not.EqualTo(0u));
        }
    }
}
