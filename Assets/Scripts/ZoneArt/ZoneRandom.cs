using Unity.Mathematics;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>Deterministic randomness for generators. Never use UnityEngine.Random or System.Random's default seed.</summary>
    public struct ZoneRandom
    {
        private Unity.Mathematics.Random _r;

        public ZoneRandom(uint seed)
        {
            _r = new Unity.Mathematics.Random(seed == 0 ? 0x9E3779B9u : seed);
        }

        /// <summary>A new stream for one purpose ("trees", "rocks"...), so adding draws to one part doesn't reshuffle another.</summary>
        public static ZoneRandom For(uint seed, int purpose) => new ZoneRandom(Hash(seed, purpose));

        public static ZoneRandom For(uint seed, string purpose) => new ZoneRandom(Hash(seed, purpose.GetHashCodeStable()));

        /// <summary>Mixes a seed with up to four ints (stable across runs and machines).</summary>
        public static uint Hash(uint seed, int a, int b = 0, int c = 0, int d = 0)
        {
            uint h = seed ^ 0x2545F491u;
            h = Mix(h ^ (uint)a);
            h = Mix(h ^ (uint)b * 0x9E3779B1u);
            h = Mix(h ^ (uint)c * 0x85EBCA77u);
            h = Mix(h ^ (uint)d * 0xC2B2AE3Du);
            return h == 0 ? 1u : h;
        }

        private static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        public float Value() => _r.NextFloat();
        public float Range(float min, float max) => _r.NextFloat(min, max);
        /// <summary>Integer in [min, max).</summary>
        public int Range(int min, int max) => _r.NextInt(min, max);
        public bool Chance(float p) => _r.NextFloat() < p;
        public float Sign() => _r.NextBool() ? 1f : -1f;
        public Vector2 InsideCircle() => (Vector2)(_r.NextFloat2Direction() * math.sqrt(_r.NextFloat()));
        public Vector3 OnSphere() => (Vector3)_r.NextFloat3Direction();
        public Quaternion YawOnly() => Quaternion.Euler(0f, _r.NextFloat(0f, 360f), 0f);
        public T Pick<T>(T[] items) => items[_r.NextInt(0, items.Length)];

        /// <summary>Index picked with the given (non-negative) weights.</summary>
        public int Weighted(float[] weights)
        {
            float total = 0f;
            foreach (float w in weights) total += w;
            float x = _r.NextFloat() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x <= 0f) return i;
            }
            return weights.Length - 1;
        }

        /// <summary>A color jittered in HSV (hue in turns, saturation and value multiplicative).</summary>
        public Color Jitter(Color c, float hue, float sat, float val)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + Range(-hue, hue), 1f);
            s = Mathf.Clamp01(s * (1f + Range(-sat, sat)));
            v = Mathf.Clamp01(v * (1f + Range(-val, val)));
            var o = Color.HSVToRGB(h, s, v);
            o.a = c.a;
            return o;
        }
    }

    internal static class StableHashExtensions
    {
        /// <summary>FNV-1a; string.GetHashCode is randomized per process in .NET.</summary>
        public static int GetHashCodeStable(this string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s) h = (h ^ ch) * 16777619;
                return (int)h;
            }
        }
    }
}
