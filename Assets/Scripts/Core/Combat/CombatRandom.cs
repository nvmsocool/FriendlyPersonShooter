using System;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// A tiny random number generator kept as a plain struct, so enemy and bot state can be copied
    /// into ECS components and gives the same numbers on every runtime for the same seed.
    /// (System.Random is a class and its sequence is not promised to match across runtimes.)
    /// </summary>
    public struct CombatRandom
    {
        private uint _state;

        public CombatRandom(uint seed)
        {
            // Mix the seed so nearby seeds (1, 2, 3...) give unrelated sequences. Zero is not allowed.
            uint z = seed + 0x9E3779B9u;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            z ^= z >> 16;
            _state = z == 0 ? 0x6D2B79F5u : z;
        }

        /// <summary>Next raw 32-bit value (xorshift32).</summary>
        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>A float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>A float in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>An int in [0, max).</summary>
        public int NextInt(int max)
        {
            if (max <= 0) return 0;
            return (int)(NextUInt() % (uint)max);
        }

        /// <summary>True with the given chance (0-1).</summary>
        public bool Chance(float chance)
        {
            return NextFloat() < chance;
        }

        /// <summary>A random angle in radians, [0, 2π).</summary>
        public float Angle()
        {
            return NextFloat() * 2f * MathF.PI;
        }

        /// <summary>A roughly normal value with mean 0 and standard deviation 1 (sum of uniforms).</summary>
        public float Gaussian()
        {
            float sum = 0f;
            for (int i = 0; i < 6; i++) sum += NextFloat();
            // Six uniforms have variance 0.5; scale to 1.
            return (sum - 3f) * 1.41421356f;
        }
    }
}
