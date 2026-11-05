using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// The seam contract between zones. Neighbouring zones may be built by different code paths (another
    /// tile theme, another seed), so they only line up if both meet the same ground at the border:
    /// within <see cref="BorderWidth"/> of a zone edge, the walkable ground must ease to
    /// <see cref="BorderHeight(ZoneRequest, float, float)"/> (a gentle function of world position and the LAYER seed),
    /// and within <see cref="SeamWidth"/> it must equal it exactly.
    /// Use <see cref="Blend"/> to do that: height = Lerp(BorderHeight, yourHeight, Blend(...)).
    /// Things may still overhang or tower above the border strip, as long as players can walk across it.
    /// Edges follow the zone's <see cref="ZoneRequest.Outline"/> (hexagon, square or triangle).
    /// </summary>
    public static class ZoneEdges
    {
        /// <summary>How far in from each edge the ground must have eased to the border height (metres).</summary>
        public const float BorderWidth = 14f;
        /// <summary>The strip along each edge where the ground is exactly the border height (so both sides agree).</summary>
        public const float SeamWidth = 2f;

        /// <summary>
        /// The ground height every zone meets at its borders (about 0..6 m, slowly rolling). It repeats on the
        /// world's wrap lattice (<see cref="ZoneRequest.WrapA"/>, <see cref="ZoneRequest.WrapB"/>) so zones also meet
        /// across the wrap seam.
        /// </summary>
        public static float BorderHeight(ZoneRequest zone, float worldX, float worldZ) =>
            BorderHeight(zone.LayerSeed, zone.WrapA, zone.WrapB, worldX, worldZ);

        /// <summary>The border height for a layer seed and wrap lattice (plain noise when the periods are zero).</summary>
        public static float BorderHeight(uint layerSeed, Vector2 wrapA, Vector2 wrapB, float worldX, float worldZ)
        {
            var o = ZoneNoise.Offset(layerSeed, 777);
            float det = wrapA.x * wrapB.y - wrapA.y * wrapB.x;
            if (Mathf.Abs(det) < 1f) return 3f + 3f * ZoneNoise.Fbm(worldX + o.x, worldZ + o.y, 1f / 220f, 3);
            // Lattice coordinates in metres along each period, so the noise repeats exactly on the lattice.
            float a = (worldX * wrapB.y - worldZ * wrapB.x) / det;
            float b = (wrapA.x * worldZ - wrapA.y * worldX) / det;
            float la = wrapA.magnitude, lb = wrapB.magnitude;
            return 3f + 3f * ZoneNoise.PeriodicFbm(a * la + o.x, b * lb + o.y, 1f / 220f, la, lb, 3);
        }

        /// <summary>
        /// 0 within <see cref="SeamWidth"/> of the zone's edge (and outside it), rising smoothly to 1 at
        /// <paramref name="width"/> metres inside.
        /// </summary>
        public static float Blend(ZoneRequest zone, float worldX, float worldZ, float width = BorderWidth)
        {
            float d = zone.EdgeDistance(worldX, worldZ);
            float t = Mathf.Clamp01((d - SeamWidth) / Mathf.Max(0.01f, width - SeamWidth));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Your height eased to the border height near the edges.</summary>
        public static float Apply(ZoneRequest zone, float worldX, float worldZ, float height, float width = BorderWidth) =>
            Mathf.Lerp(BorderHeight(zone, worldX, worldZ), height, Blend(zone, worldX, worldZ, width));
    }
}
