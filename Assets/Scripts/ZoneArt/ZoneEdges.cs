using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// The seam contract between zones. Neighbouring zones may be built by different code paths (another
    /// tile theme, another seed), so they only line up if both meet the same ground at the border:
    /// within <see cref="BorderWidth"/> of a zone edge, the walkable ground must ease to
    /// <see cref="BorderHeight"/> (a gentle function of world position and the LAYER seed).
    /// Use <see cref="Blend"/> to do that: height = Lerp(BorderHeight, yourHeight, Blend(...)).
    /// Things may still overhang or tower above the border strip, as long as players can walk across it.
    /// </summary>
    public static class ZoneEdges
    {
        /// <summary>How far in from each edge the ground must have eased to the border height (metres).</summary>
        public const float BorderWidth = 14f;

        /// <summary>
        /// The ground height every zone meets at its borders (about 0..6 m, slowly rolling). It repeats every
        /// <see cref="ZoneRequest.WorldSize"/> metres so zones also meet across the wrapping world's edge.
        /// </summary>
        public static float BorderHeight(ZoneRequest zone, float worldX, float worldZ) => BorderHeight(zone.LayerSeed, zone.WorldSize, worldX, worldZ);

        /// <summary>The border height for a layer seed and world size (0 for a world that doesn't wrap).</summary>
        public static float BorderHeight(uint layerSeed, float worldSize, float worldX, float worldZ)
        {
            var o = ZoneNoise.Offset(layerSeed, 777);
            if (worldSize > 0f) return 3f + 3f * ZoneNoise.PeriodicFbm(worldX + o.x, worldZ + o.y, 1f / 220f, worldSize, 3);
            return 3f + 3f * ZoneNoise.Fbm(worldX + o.x, worldZ + o.y, 1f / 220f, 3);
        }

        /// <summary>0 on the zone's edge, rising smoothly to 1 at <paramref name="width"/> metres inside.</summary>
        public static float Blend(ZoneRequest zone, float worldX, float worldZ, float width = BorderWidth)
        {
            float lx = worldX - zone.Origin.x, lz = worldZ - zone.Origin.z;
            float d = Mathf.Min(Mathf.Min(lx, zone.Size - lx), Mathf.Min(lz, zone.Size - lz));
            float t = Mathf.Clamp01(d / width);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Your height eased to the border height near the edges.</summary>
        public static float Apply(ZoneRequest zone, float worldX, float worldZ, float height, float width = BorderWidth) =>
            Mathf.Lerp(BorderHeight(zone, worldX, worldZ), height, Blend(zone, worldX, worldZ, width));
    }
}
