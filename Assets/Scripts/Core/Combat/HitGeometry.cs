using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// Ray intersection tests. Rays start at <c>origin</c> and go along <c>dir</c>, which must be
    /// normalized; distances are in metres along the ray. A ray starting inside a shape hits it at 0.
    /// </summary>
    public static class HitGeometry
    {
        public static bool RaySphere(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float distance)
        {
            Vector3 m = origin - center;
            float c = Vector3.Dot(m, m) - radius * radius;
            if (c <= 0f)
            {
                distance = 0f;
                return true;
            }
            float b = Vector3.Dot(m, dir);
            // Outside and pointing away.
            if (b > 0f)
            {
                distance = 0f;
                return false;
            }
            float disc = b * b - c;
            if (disc < 0f)
            {
                distance = 0f;
                return false;
            }
            distance = -b - MathF.Sqrt(disc);
            return true;
        }

        /// <summary>Ray against a capsule: the segment a-b swept by <paramref name="radius"/>.</summary>
        public static bool RayCapsule(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, float radius, out float distance)
        {
            Vector3 ab = b - a;
            float abLen2 = Vector3.Dot(ab, ab);
            if (abLen2 < 1e-8f) return RaySphere(origin, dir, a, radius, out distance);

            // Inside check: closest point on the segment to the origin.
            float tOrigin = Math.Clamp(Vector3.Dot(origin - a, ab) / abLen2, 0f, 1f);
            if (Vector3.DistanceSquared(origin, a + ab * tOrigin) <= radius * radius)
            {
                distance = 0f;
                return true;
            }

            // Infinite cylinder around the axis, then the end caps.
            Vector3 ao = origin - a;
            float abDotD = Vector3.Dot(ab, dir);
            float abDotAo = Vector3.Dot(ab, ao);
            float qa = abLen2 - abDotD * abDotD;
            float qb = abLen2 * Vector3.Dot(ao, dir) - abDotAo * abDotD;
            float qc = abLen2 * Vector3.Dot(ao, ao) - abDotAo * abDotAo - radius * radius * abLen2;

            float best = float.MaxValue;
            if (qa > 1e-8f)
            {
                float disc = qb * qb - qa * qc;
                if (disc >= 0f)
                {
                    float t = (-qb - MathF.Sqrt(disc)) / qa;
                    float along = abDotAo + t * abDotD;
                    if (t >= 0f && along >= 0f && along <= abLen2) best = t;
                }
            }
            if (RaySphere(origin, dir, a, radius, out float ta) && ta < best) best = ta;
            if (RaySphere(origin, dir, b, radius, out float tb) && tb < best) best = tb;

            distance = best == float.MaxValue ? 0f : best;
            return best != float.MaxValue;
        }

        /// <summary>Angle in degrees between two directions (any length, not zero).</summary>
        public static float AngleBetween(Vector3 a, Vector3 b)
        {
            float dot = Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b));
            return MathF.Acos(Math.Clamp(dot, -1f, 1f)) * (180f / MathF.PI);
        }
    }
}
