using Unity.Mathematics;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// Noise helpers on top of Unity.Mathematics. Sample in WORLD coordinates (not zone-local) so
    /// anything continuous lines up across zone borders. Offset by a seed-derived vector to vary layers.
    /// </summary>
    public static class ZoneNoise
    {
        /// <summary>Simplex noise in [-1, 1].</summary>
        public static float Simplex(float x, float z) => noise.snoise(new float2(x, z));

        public static float Simplex(Vector3 p) => noise.snoise((float3)p);

        /// <summary>A seed-derived offset to shift noise so different seeds look different.</summary>
        public static Vector2 Offset(uint seed, int purpose)
        {
            var r = ZoneRandom.For(seed, purpose);
            return new Vector2(r.Range(-5000f, 5000f), r.Range(-5000f, 5000f));
        }

        /// <summary>Fractal simplex in about [-1, 1]. Frequency in cycles per metre.</summary>
        public static float Fbm(float x, float z, float frequency, int octaves = 5, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            var p = new float2(x, z) * frequency;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * noise.snoise(p);
                norm += amp;
                amp *= gain;
                p = p * lacunarity + new float2(17.3f, -9.1f);
            }
            return sum / norm;
        }

        /// <summary>Ridged fractal in about [0, 1]: sharp crests, good for ridges and cliffs.</summary>
        public static float Ridged(float x, float z, float frequency, int octaves = 5, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f, weight = 1f;
            var p = new float2(x, z) * frequency;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - math.abs(noise.snoise(p));
                n *= n;
                n *= weight;
                weight = math.saturate(n * 2f);
                sum += amp * n;
                norm += amp;
                amp *= gain;
                p = p * lacunarity + new float2(-31.7f, 4.3f);
            }
            return sum / norm;
        }

        /// <summary>Domain-warped fbm: organic, swirly shapes.</summary>
        public static float Warped(float x, float z, float frequency, float warp, int octaves = 5)
        {
            float wx = Fbm(x + 31.4f, z - 12.7f, frequency, 3);
            float wz = Fbm(x - 47.2f, z + 88.1f, frequency, 3);
            return Fbm(x + wx * warp, z + wz * warp, frequency, octaves);
        }

        /// <summary>Cellular (Worley) noise: x = distance to nearest cell point, y = to second nearest.</summary>
        public static Vector2 Cellular(float x, float z, float frequency)
        {
            var c = noise.cellular(new float2(x, z) * frequency);
            return new Vector2(c.x, c.y);
        }

        /// <summary>Smooth terrace: rounds h to steps of <paramref name="step"/> with soft edges (0 = hard, 1 = none).</summary>
        public static float Terrace(float h, float step, float softness)
        {
            float k = h / step;
            float f = math.floor(k);
            float t = k - f;
            t = math.smoothstep(0.5f - 0.5f * softness, 0.5f + 0.5f * softness, t);
            return (f + t) * step;
        }
    }
}
