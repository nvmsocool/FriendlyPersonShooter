namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// Turns a world seed plus a few numbers (zone, time slot, point...) into a fresh seed, so each
    /// part of the world gets its own independent random stream. The same inputs always give the
    /// same output on every machine.
    /// </summary>
    public static class SeedHash
    {
        // Salts keep the streams for different systems apart even with the same coordinates.
        public const int LayerSalt = 1;
        public const int ZoneSalt = 2;
        public const int GateSalt = 3;
        public const int SpawnSalt = 4;
        public const int BloomSalt = 5;

        public static uint Of(uint seed, int a, int b = 0, int c = 0, int d = 0, int e = 0, int f = 0)
        {
            uint h = Avalanche(seed ^ 0x2545F491u);
            h = Mix(h, (uint)a);
            h = Mix(h, (uint)b);
            h = Mix(h, (uint)c);
            h = Mix(h, (uint)d);
            h = Mix(h, (uint)e);
            h = Mix(h, (uint)f);
            return h;
        }

        /// <summary>Splits a long (for example a time-slot index) into two ints for <see cref="Of"/>.</summary>
        public static int Low(long value) => (int)(value & 0xFFFFFFFF);
        public static int High(long value) => (int)(value >> 32);

        private static uint Mix(uint h, uint v)
        {
            h ^= v + 0x9E3779B9u + (h << 6) + (h >> 2);
            return Avalanche(h);
        }

        private static uint Avalanche(uint z)
        {
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            return z ^ (z >> 16);
        }
    }
}
