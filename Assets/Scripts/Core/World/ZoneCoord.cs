using System;

namespace FriendlyPersonShooter.Core.World
{
    /// <summary>A zone's position on the world grid. Always stored wrapped (0..Width-1, 0..Height-1).</summary>
    public readonly struct ZoneCoord : IEquatable<ZoneCoord>
    {
        public readonly int X;
        public readonly int Y;

        public ZoneCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(ZoneCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is ZoneCoord other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public static bool operator ==(ZoneCoord a, ZoneCoord b) => a.Equals(b);
        public static bool operator !=(ZoneCoord a, ZoneCoord b) => !a.Equals(b);
        public override string ToString() => $"({X},{Y})";
    }
}
