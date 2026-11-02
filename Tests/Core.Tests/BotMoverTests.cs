using System;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class BotMoverTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void MoveInput_ToWorldFollowsFacing()
        {
            var forward = new MoveInput { Vertical = 1 }.ToWorld(0);
            Assert.That(Vector3.Distance(forward, Vector3.UnitZ), Is.LessThan(1e-5f));
            var right = new MoveInput { Horizontal = 1 }.ToWorld(0);
            Assert.That(Vector3.Distance(right, Vector3.UnitX), Is.LessThan(1e-5f));
            var rightWhenFacingX = new MoveInput { Horizontal = 1 }.ToWorld(90);
            Assert.That(Vector3.Distance(rightWhenFacingX, -Vector3.UnitZ), Is.LessThan(1e-5f));
            Assert.That(new MoveInput().ToWorld(30), Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void Mover_StrafesBothWaysWithVariedTimingAndJumps()
        {
            var s = new BotMoveSettings();
            var m = BotMover.Create(3);
            int lefts = 0, rights = 0, jumps = 0, changes = 0, last = 0;
            var pos = new Vector3(8, 0, 0); // inside the band around the origin
            for (int i = 0; i < 60 * 30; i++)
            {
                var input = m.Step(Dt, pos, -90, Vector3.Zero, s);
                if (input.Horizontal < 0) lefts++;
                if (input.Horizontal > 0) rights++;
                if (input.Jump) jumps++;
                if (input.Horizontal != last) changes++;
                last = input.Horizontal;
            }
            Assert.That(lefts, Is.GreaterThan(300));
            Assert.That(rights, Is.GreaterThan(300));
            Assert.That(changes, Is.GreaterThan(30), "changes direction often");
            Assert.That(jumps, Is.InRange(3, 20));
        }

        [Test]
        public void Mover_KeepsWithinDistanceBand()
        {
            var s = new BotMoveSettings();
            var m = BotMover.Create(9);
            var anchor = new Vector3(0, 0, 0);
            var pos = new Vector3(30, 0, 0);
            float yaw = 37;
            const float speed = 6;
            for (int i = 0; i < 60 * 60; i++)
            {
                var input = m.Step(Dt, pos, yaw, anchor, s);
                pos += input.ToWorld(yaw) * speed * Dt;
                yaw += 20 * Dt; // the bot looks around while it moves
                if (i > 60 * 10)
                {
                    float d = new Vector2(pos.X, pos.Z).Length();
                    Assert.That(d, Is.InRange(s.MinDistance - 1.5f, s.MaxDistance + 1.5f));
                }
            }
        }

        [Test]
        public void Mover_IsDeterministicForASeed()
        {
            var s = new BotMoveSettings();
            var a = BotMover.Create(4);
            var b = BotMover.Create(4);
            for (int i = 0; i < 600; i++)
            {
                var ia = a.Step(Dt, new Vector3(6, 0, 0), 0, Vector3.Zero, s);
                var ib = b.Step(Dt, new Vector3(6, 0, 0), 0, Vector3.Zero, s);
                Assert.That(ia.Horizontal, Is.EqualTo(ib.Horizontal));
                Assert.That(ia.Vertical, Is.EqualTo(ib.Vertical));
                Assert.That(ia.Jump, Is.EqualTo(ib.Jump));
            }
        }
    }
}
