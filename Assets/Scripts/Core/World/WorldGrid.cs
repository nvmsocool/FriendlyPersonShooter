using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.World
{
    /// <summary>
    /// A grid of square zones that wraps at every edge, so walking off one side
    /// brings you back on the other. Distances are measured the short way around.
    /// </summary>
    public sealed class WorldGrid
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>Side length of one zone in world units (metres).</summary>
        public float ZoneSize { get; }

        public WorldGrid(int width, int height, float zoneSize)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (zoneSize <= 0f) throw new ArgumentOutOfRangeException(nameof(zoneSize));
            Width = width;
            Height = height;
            ZoneSize = zoneSize;
        }

        public ZoneCoord Wrap(int x, int y) => new ZoneCoord(Mod(x, Width), Mod(y, Height));

        /// <summary>The zone containing a world-space point (x and z on the ground plane).</summary>
        public ZoneCoord ZoneAt(double worldX, double worldZ)
        {
            return Wrap((int)Math.Floor(worldX / ZoneSize), (int)Math.Floor(worldZ / ZoneSize));
        }

        /// <summary>
        /// Zones apart, counting diagonal steps as one, the short way around the wrap.
        /// Neighbours are 1 apart; the same zone is 0.
        /// </summary>
        public int Distance(ZoneCoord a, ZoneCoord b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            dx = Math.Min(dx, Width - dx);
            dy = Math.Min(dy, Height - dy);
            return Math.Max(dx, dy);
        }

        /// <summary>Every distinct zone within <paramref name="radius"/> of the centre, centre included.</summary>
        public IReadOnlyCollection<ZoneCoord> Neighborhood(ZoneCoord center, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            // A HashSet removes duplicates when the radius reaches around a small grid.
            var zones = new HashSet<ZoneCoord>();
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    zones.Add(Wrap(center.X + dx, center.Y + dy));
                }
            }
            return zones;
        }

        private static int Mod(int value, int m)
        {
            int r = value % m;
            return r < 0 ? r + m : r;
        }
    }
}
