using FriendlyPersonShooter.Net;
using FriendlyPersonShooter.ZoneArt;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The expedition's generated ground: which generator builds the zones, and how players, enemies,
    /// spawns and shots meet it. Everything here queries the zone colliders (see <see cref="ZoneWorld"/>),
    /// which the server and every client build identically from the layer seed.
    /// Start a build with <c>-fps-flat</c> to play on the old flat floor instead.
    /// </summary>
    public static class ExpeditionTerrain
    {
        public const string Generator = "prismatica";

        /// <summary>Highest ledge a player walks up without jumping (metres).</summary>
        public const float StepHeight = 0.45f;
        /// <summary>The player's collision capsule (matches the walkability analysis the zones are checked with).</summary>
        public const float BodyRadius = 0.3f;
        public const float BodyHeight = 1.8f;
        /// <summary>
        /// Steepest walkable slope (degrees). A little more forgiving than the 50 degrees zones and bot paths are
        /// checked against, since faceted ground has steeper patches between the 1 m spots those checks sample.
        /// </summary>
        public const float MaxSlope = 58f;
        /// <summary>How far a grounded player sticks to the ground going down a slope or step.</summary>
        public const float SnapDown = 0.45f;

        private static readonly float MinGroundNormalY = math.cos(math.radians(MaxSlope));

        /// <summary>False when the build was started with -fps-flat.</summary>
        public static bool Enabled = !System.Environment.CommandLine.Contains("-fps-flat");

        public static bool Active => ExpeditionWorld.Active && Enabled;

        private static bool Headless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        /// <summary>The live zone world for the current layer (null outside the expedition).</summary>
        public static ZoneWorld World => Active ? ZoneWorld.Open(ExpeditionWorld.Map, Generator, Headless) : null;

        /// <summary>
        /// A standing spot at (x, z): the lowest surface there with room for a body, building the zone first if needed.
        /// Used for spawns, where nothing above the ground matters (shrines and spawn points sit on open, flattened ground).
        /// </summary>
        public static float3 OnGround(float3 p)
        {
            var w = World;
            if (w == null) return p;
            w.EnsureReady(p.x, p.z);
            p.y = StandY(w, p.x, p.z, p.y);
            return p;
        }

        /// <summary>The height of the standing surface at (x, z); <paramref name="fallback"/> when the zone isn't built.</summary>
        public static float GroundY(float x, float z, float fallback = 0f)
        {
            var w = World;
            if (w == null || !w.IsReady(x, z)) return fallback;
            return StandY(w, x, z, fallback);
        }

        private static float StandY(ZoneWorld w, float x, float z, float fallback)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 800f, z), Vector3.down, 2000f, ZoneWorld.CollisionMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.normal.y < MinGroundNormalY || h.point.y >= best) continue;
                var feet = h.point;
                if (w.CheckCapsule(feet + Vector3.up * (StepHeight + BodyRadius), feet + Vector3.up * (BodyHeight - BodyRadius), BodyRadius)) continue;
                best = h.point.y;
            }
            if (best < float.MaxValue) return best;
            return hits.Length > 0 ? w.TopAt(x, z, fallback) : fallback;
        }

        /// <summary>The surface under (x, z) within <paramref name="climb"/> above or <paramref name="drop"/> below y, if any.</summary>
        public static bool SurfaceNear(float x, float z, float y, float climb, float drop, out float surface)
        {
            surface = y;
            var w = World;
            if (w == null || !w.IsReady(x, z)) return false;
            if (!w.GroundBelow(x, z, y + climb, out var hit, climb + drop)) return false;
            surface = hit.point.y;
            return true;
        }

        /// <summary>Distance along a ray to the ground, a zone structure or foliage, or float.MaxValue.</summary>
        public static float SightRaycast(float3 origin, float3 direction, float maxDistance)
        {
            var w = World;
            if (w == null) return float.MaxValue;
            return w.SightRaycast(origin, direction, maxDistance, out var hit) ? hit.distance : float.MaxValue;
        }

        /// <summary>Distance along a ray to the ground or a zone structure, or float.MaxValue.</summary>
        public static float Raycast(float3 origin, float3 direction, float maxDistance)
        {
            var w = World;
            if (w == null) return float.MaxValue;
            return w.Raycast(origin, direction, maxDistance, out var hit) ? hit.distance : float.MaxValue;
        }

        /// <summary>
        /// Moves a player one tick over the zones: walk with collisions and slope limits, step up small ledges,
        /// stick to the ground going down, fall and land. Holds still while the zone underfoot isn't built yet.
        /// <paramref name="velocity"/> is the horizontal walking velocity; <paramref name="m"/> carries gravity and jumping.
        /// </summary>
        public static void Move(ref float3 pos, ref ArenaMotion m, float3 velocity, float gravity, float dt)
        {
            var w = World;
            w.AddInterest(pos);
            if (!w.IsReady(pos.x, pos.z))
            {
                m.VerticalSpeed = 0f;
                return;
            }

            // Walk: the whole step, else slide along x or z. A body already overlapping something
            // (wedged on a cliff face, spawned against a trunk) may always move, so it can get out.
            var delta = velocity * dt;
            if (math.lengthsq(delta.xz) > 1e-8f)
            {
                bool wedged = Overlaps(w, pos);
                if (!TryWalk(w, ref pos, delta, m.Grounded, wedged) &&
                    !TryWalk(w, ref pos, new float3(delta.x, 0f, 0f), m.Grounded, wedged))
                {
                    TryWalk(w, ref pos, new float3(0f, 0f, delta.z), m.Grounded, wedged);
                }
            }

            // Fall, land, or stick to the ground.
            bool wasGrounded = m.Grounded;
            m.VerticalSpeed -= gravity * dt;
            float newY = pos.y + m.VerticalSpeed * dt;
            if (m.VerticalSpeed > 0f)
            {
                var head = new Vector3(pos.x, pos.y + BodyHeight - BodyRadius, pos.z);
                if (w.SphereCast(head, BodyRadius * 0.9f, Vector3.up, m.VerticalSpeed * dt + 0.05f, out _))
                {
                    m.VerticalSpeed = 0f;
                    newY = pos.y;
                }
            }
            float top = math.max(pos.y, newY) + StepHeight;
            float reach = top - newY + (wasGrounded && m.VerticalSpeed <= 0f ? SnapDown : 0f);
            if (m.VerticalSpeed <= 0f && w.GroundBelow(pos.x, pos.z, top, out var hit, reach))
            {
                pos.y = hit.point.y;
                m.VerticalSpeed = 0f;
                m.Grounded = true;
                if (hit.normal.y < MinGroundNormalY)
                {
                    // Too steep to stand on: slide off downhill (and no jumping up it).
                    var downhill = math.normalizesafe(new float3(hit.normal.x, 0f, hit.normal.z));
                    TryWalk(w, ref pos, downhill * SlideSpeed * dt, false, true);
                    m.Grounded = false;
                }
            }
            else
            {
                pos.y = newY;
                m.Grounded = false;
                // Under the ground (a spawn inside a slope, a step through a seam): climb back on top.
                if (!w.GroundBelow(pos.x, pos.z, pos.y + 0.1f, out _, 400f) && w.GroundBelow(pos.x, pos.z, pos.y + 60f, out var above, 60f))
                {
                    pos.y = above.point.y;
                    m.VerticalSpeed = 0f;
                    m.Grounded = true;
                }
            }
            pos = ExpeditionWorld.Wrap(pos);
        }

        /// <summary>Sliding speed off slopes steeper than <see cref="MaxSlope"/> (m/s).</summary>
        public const float SlideSpeed = 5f;

        private static bool Overlaps(ZoneWorld w, float3 pos)
        {
            var feet = (Vector3)pos;
            return w.CheckCapsule(feet + Vector3.up * (StepHeight + BodyRadius), feet + Vector3.up * math.max(StepHeight + BodyRadius, BodyHeight - BodyRadius), BodyRadius);
        }

        private static bool TryWalk(ZoneWorld w, ref float3 pos, float3 delta, bool grounded, bool ignoreBody)
        {
            var target = ExpeditionWorld.Wrap(pos + new float3(delta.x, 0f, delta.z));
            if (!w.IsReady(target.x, target.z))
            {
                w.AddInterest(target);
                return false;
            }
            if (!ignoreBody && Overlaps(w, target)) return false;
            // No walking up slopes steeper than the limit (going down them is fine: you slide off the edge).
            if (w.GroundBelow(target.x, target.z, target.y + StepHeight, out var hit, StepHeight + SnapDown) &&
                hit.point.y > pos.y + 0.02f && hit.normal.y < MinGroundNormalY)
            {
                return false;
            }
            pos = new float3(target.x, pos.y, target.z);
            return true;
        }
    }
}
