using System;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class BotAimTests
    {
        private const float Dt = 1f / 60f;

        private static BotAimSettings Noiseless(float reaction = 0.25f)
        {
            return new BotAimSettings
            {
                ReactionTime = reaction,
                Prediction = 0f,
                TrackingErrorDegrees = 0f,
                TrackingSpeed = 30f,
                FlickTime = 0.2f,
                FlickOvershoot = 0.2f,
                FireConeFactor = 1f,
            };
        }

        [Test]
        public void AimMath_YawZeroLooksAlongZAndPositiveYawTurnsTowardX()
        {
            Assert.That(Vector3.Distance(AimMath.Direction(0, 0), Vector3.UnitZ), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(AimMath.Direction(90, 0), Vector3.UnitX), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(AimMath.Direction(0, 90), Vector3.UnitY), Is.LessThan(1e-5f));
            AimMath.ToYawPitch(new Vector3(-1, 1, 0), out float yaw, out float pitch);
            Assert.That(yaw, Is.EqualTo(-90).Within(1e-4));
            Assert.That(pitch, Is.EqualTo(45).Within(1e-4));
        }

        [Test]
        public void Aim_ReactsOnlyAfterReactionTime()
        {
            var aim = new BotAim(Noiseless(0.25f), 1);
            var eye = Vector3.Zero;
            var a = new Vector3(0, 0, 10);
            var b = new Vector3(10, 0, 10);

            for (int i = 0; i < 120; i++) aim.Step(Dt, eye, 1, a, 0.5f);
            Assert.That(aim.Yaw, Is.EqualTo(0).Within(0.1));

            // Target jumps sideways; for the reaction time the bot still aims where it was.
            float t = 0;
            while (t < 0.2f)
            {
                aim.Step(Dt, eye, 1, b, 0.5f);
                t += Dt;
            }
            Assert.That(aim.Yaw, Is.LessThan(2f), "still aiming at the old spot");

            for (int i = 0; i < 60; i++) aim.Step(Dt, eye, 1, b, 0.5f);
            Assert.That(aim.Yaw, Is.EqualTo(45).Within(0.5));
        }

        [Test]
        public void Aim_FlickOvershootsThenSettles()
        {
            var settings = Noiseless(0.2f);
            var aim = new BotAim(settings, 1);
            var eye = Vector3.Zero;
            for (int i = 0; i < 60; i++) aim.Step(Dt, eye, 1, new Vector3(0, 0, 10), 0.5f);

            float maxYaw = 0;
            bool sawReacting = false, sawFlick = false;
            for (int i = 0; i < 90; i++)
            {
                var o = aim.Step(Dt, eye, 2, new Vector3(10, 0, 10), 0.5f);
                sawReacting |= o.Reacting;
                sawFlick |= o.Flicking;
                if (o.Reacting) Assert.That(o.Fire, Is.False, "no firing before reacting");
                maxYaw = Math.Max(maxYaw, aim.Yaw);
            }
            Assert.That(sawReacting && sawFlick, Is.True);
            Assert.That(maxYaw, Is.GreaterThan(45 * 1.1f), "overshoots");
            Assert.That(aim.Yaw, Is.EqualTo(45).Within(0.5), "settles");
        }

        [Test]
        public void Aim_FiresOnlyWhenOnTarget()
        {
            var aim = new BotAim(Noiseless(), 1);
            var eye = Vector3.Zero;
            var target = new Vector3(3, 0, 10);
            var first = aim.Step(Dt, eye, 1, target, 0.5f);
            Assert.That(first.Fire, Is.False);
            AimOutput o = first;
            for (int i = 0; i < 120; i++) o = aim.Step(Dt, eye, 1, target, 0.5f);
            Assert.That(o.Fire, Is.True);
            Assert.That(o.TrueErrorDegrees, Is.LessThan(0.5f));

            // A tiny target far away with a wobbly aim: rarely on target.
            var shaky = new BotAim(new BotAimSettings { TrackingErrorDegrees = 5f, FireConeFactor = 1f }, 3);
            int fired = 0;
            for (int i = 0; i < 600; i++)
                if (shaky.Step(Dt, eye, 1, new Vector3(0, 0, 50), 0.15f).Fire) fired++;
            Assert.That(fired, Is.LessThan(60));
        }

        [Test]
        public void Aim_StrongBotTracksAMovingTargetBetterThanWeak()
        {
            float StrongErr() => MeanTrackingError(BotAimSettings.Strong());
            float WeakErr() => MeanTrackingError(BotAimSettings.Weak());
            Assert.That(StrongErr(), Is.LessThan(WeakErr()));
        }

        [Test]
        public void Aim_IsDeterministicForASeed()
        {
            var a = new BotAim(BotAimSettings.Weak(), 77);
            var b = new BotAim(BotAimSettings.Weak(), 77);
            for (int i = 0; i < 300; i++)
            {
                var target = new Vector3(MathF.Sin(i * 0.05f) * 5, 1, 10);
                var oa = a.Step(Dt, Vector3.Zero, 1, target, 0.5f);
                var ob = b.Step(Dt, Vector3.Zero, 1, target, 0.5f);
                Assert.That(oa.Yaw, Is.EqualTo(ob.Yaw));
                Assert.That(oa.Pitch, Is.EqualTo(ob.Pitch));
                Assert.That(oa.Fire, Is.EqualTo(ob.Fire));
            }
        }

        private static float MeanTrackingError(BotAimSettings settings)
        {
            var aim = new BotAim(settings, 5);
            float sum = 0;
            int n = 0;
            for (int i = 0; i < 60 * 10; i++)
            {
                float t = i * Dt;
                // Strafing side to side at about 5 m/s, 12 m away.
                var target = new Vector3(MathF.Sin(t * 1.5f) * 3.3f, 0, 12);
                var o = aim.Step(Dt, Vector3.Zero, 1, target, 0.4f);
                if (t > 1f)
                {
                    sum += o.TrueErrorDegrees;
                    n++;
                }
            }
            return sum / n;
        }
    }
}
