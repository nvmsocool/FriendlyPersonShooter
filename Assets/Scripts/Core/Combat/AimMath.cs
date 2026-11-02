using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// Yaw and pitch conventions shared by aiming code. Angles are in degrees. Y is up.
    /// Yaw 0 looks along +Z and grows toward +X (clockwise seen from above, like Unity's Y rotation).
    /// Pitch 0 is level and positive looks up (Unity's camera X rotation is the negative of this).
    /// </summary>
    public static class AimMath
    {
        public const float Rad2Deg = 180f / MathF.PI;
        public const float Deg2Rad = MathF.PI / 180f;

        public static Vector3 Direction(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Deg2Rad, p = pitchDeg * Deg2Rad;
            float cp = MathF.Cos(p);
            return new Vector3(MathF.Sin(y) * cp, MathF.Sin(p), MathF.Cos(y) * cp);
        }

        /// <summary>Yaw and pitch that look along <paramref name="dir"/> (any length, not zero).</summary>
        public static void ToYawPitch(Vector3 dir, out float yawDeg, out float pitchDeg)
        {
            float flat = MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            yawDeg = MathF.Atan2(dir.X, dir.Z) * Rad2Deg;
            pitchDeg = MathF.Atan2(dir.Y, flat) * Rad2Deg;
        }

        /// <summary>Shortest signed difference to - from, in (-180, 180].</summary>
        public static float DeltaAngle(float fromDeg, float toDeg)
        {
            float d = (toDeg - fromDeg) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        /// <summary>Half the angle a sphere of <paramref name="radius"/> covers at <paramref name="distance"/>, in degrees.</summary>
        public static float AngularRadius(float radius, float distance)
        {
            if (distance <= radius) return 90f;
            return MathF.Asin(radius / distance) * Rad2Deg;
        }
    }
}
