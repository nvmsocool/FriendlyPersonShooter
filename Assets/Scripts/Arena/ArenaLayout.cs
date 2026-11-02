using Unity.Mathematics;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Fixed arena layout: cover blocks players can duck behind. Shots don't pass through them.</summary>
    public static class ArenaLayout
    {
        public struct Box
        {
            /// <summary>Centre on the ground plane (x, z).</summary>
            public float2 Center;
            /// <summary>Width (x), height (y) and depth (z) in metres.</summary>
            public float3 Size;

            public Box(float x, float z, float width, float height, float depth)
            {
                Center = new float2(x, z);
                Size = new float3(width, height, depth);
            }
        }

        /// <summary>Cover for the current mode: the arena's blocks, or the expedition's landmarks and gate posts.</summary>
        public static Box[] Cover => ExpeditionWorld.Active ? ExpeditionWorld.Cover : ArenaCover;

        public static readonly Box[] ArenaCover =
        {
            new Box(-8f, 0f, 2f, 2.5f, 2f),
            new Box(8f, 0f, 2f, 2.5f, 2f),
            new Box(0f, 8f, 6f, 1.2f, 1f),
            new Box(-14f, 12f, 1.5f, 3f, 5f),
            new Box(14f, 12f, 1.5f, 3f, 5f),
            new Box(-15f, -8f, 4f, 1.2f, 1f),
            new Box(15f, -8f, 4f, 1.2f, 1f),
        };

        /// <summary>Distance along a ray to the nearest cover block, or float.MaxValue if none is hit.</summary>
        public static float RaycastCover(float3 origin, float3 dir, float maxDistance)
        {
            float best = float.MaxValue;
            bool wrap = ExpeditionWorld.Active;
            foreach (var b in Cover)
            {
                var half = b.Size * 0.5f;
                var center = new float3(b.Center.x, 0f, b.Center.y);
                if (wrap)
                {
                    // Use the copy of the box nearest the ray, and skip boxes too far to matter.
                    center = ExpeditionWorld.Near(center, origin);
                    if (math.distance(center.xz, origin.xz) > maxDistance + half.x + half.z) continue;
                }
                var min = new float3(center.x - half.x, 0f, center.z - half.z);
                var max = new float3(center.x + half.x, b.Size.y, center.z + half.z);
                if (RayBox(origin, dir, min, max, out float t) && t < best && t <= maxDistance) best = t;
            }
            return best;
        }

        private static bool RayBox(float3 o, float3 d, float3 min, float3 max, out float t)
        {
            float3 inv = 1f / d;
            float3 t0 = (min - o) * inv;
            float3 t1 = (max - o) * inv;
            float3 tmin = math.min(t0, t1);
            float3 tmax = math.max(t0, t1);
            float near = math.cmax(tmin);
            float far = math.cmin(tmax);
            t = near;
            return far >= math.max(near, 0f);
        }
    }
}
