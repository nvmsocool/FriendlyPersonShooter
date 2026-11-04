using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// Something a bullet can hit: a body sphere and an optional weak-point sphere.
    /// The weak point may sit inside the body (a Skitter's core) or stick out of it.
    /// </summary>
    public struct HitTarget
    {
        public int Id;
        public bool IsPlayer;
        public Vector3 BodyCenter;
        public float BodyRadius;
        public bool HasWeakPoint;
        public Vector3 WeakPointCenter;
        public float WeakPointRadius;
        /// <summary>False while the target can't be hit (a Wisp between blink windows). Bullets pass through.</summary>
        public bool Hittable;
        /// <summary>
        /// The weak point counts only when the ray reaches it before (or about where) it enters the body:
        /// a plate on one side of a shell can't be shot through the shell from behind.
        /// </summary>
        public bool WeakPointOnSurface;

        public static HitTarget Body(int id, Vector3 center, float radius, bool isPlayer = false)
        {
            return new HitTarget { Id = id, IsPlayer = isPlayer, BodyCenter = center, BodyRadius = radius, Hittable = true };
        }

        public static HitTarget WithWeakPoint(int id, Vector3 center, float radius, Vector3 weakCenter, float weakRadius)
        {
            return new HitTarget
            {
                Id = id,
                BodyCenter = center,
                BodyRadius = radius,
                HasWeakPoint = true,
                WeakPointCenter = weakCenter,
                WeakPointRadius = weakRadius,
                Hittable = true,
            };
        }
    }

    /// <summary>The nearest thing a hitscan ray hit.</summary>
    public struct ScanHit
    {
        /// <summary>Index into the list passed to <see cref="HitScan.Cast"/>.</summary>
        public int Index;
        public int TargetId;
        public bool IsPlayer;
        public float Distance;
        public Vector3 Point;
        /// <summary>True when the ray passed through the weak point.</summary>
        public bool WeakPoint;
    }

    /// <summary>
    /// Instant bullets: finds the nearest target along a ray. World geometry isn't known here, so the
    /// caller limits <c>maxRange</c> to the distance of the first wall its physics raycast found.
    /// </summary>
    public static class HitScan
    {
        /// <summary>How far past the body's surface a surface weak point still counts (metres).</summary>
        public const float SurfaceSlack = 0.5f;

        /// <summary>
        /// Casts a ray (direction need not be normalized) and returns true with the nearest hit
        /// within <paramref name="maxRange"/>. Skips <paramref name="ignoreId"/> (the shooter).
        /// </summary>
        public static bool Cast(Vector3 origin, Vector3 direction, float maxRange, IReadOnlyList<HitTarget> targets,
            out ScanHit hit, int ignoreId = int.MinValue)
        {
            hit = default;
            if (direction.LengthSquared() < 1e-12f) return false;
            Vector3 dir = Vector3.Normalize(direction);
            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (!t.Hittable || t.Id == ignoreId) continue;

                bool bodyHit = HitGeometry.RaySphere(origin, dir, t.BodyCenter, t.BodyRadius, out float bodyDist) &&
                               bodyDist <= maxRange;
                float weakDist = 0f;
                bool weakHit = t.HasWeakPoint &&
                               HitGeometry.RaySphere(origin, dir, t.WeakPointCenter, t.WeakPointRadius, out weakDist) &&
                               weakDist <= maxRange;
                if (weakHit && t.WeakPointOnSurface && bodyHit && weakDist > bodyDist + SurfaceSlack) weakHit = false;
                if (!bodyHit && !weakHit) continue;

                // The hit point is where the ray first touches the target. A pass through the weak
                // point counts as a weak-point hit even when the weak point sits inside the body.
                float dist = bodyHit ? bodyDist : weakDist;
                if (weakHit && weakDist < dist) dist = weakDist;
                if (dist >= best) continue;

                best = dist;
                found = true;
                hit = new ScanHit
                {
                    Index = i,
                    TargetId = t.Id,
                    IsPlayer = t.IsPlayer,
                    Distance = dist,
                    Point = origin + dir * dist,
                    WeakPoint = weakHit,
                };
            }
            return found;
        }
    }
}
