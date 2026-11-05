using System;

namespace FriendlyPersonShooter.Core.World
{
    /// <summary>The three zone shapes of the rhombitrihexagonal tiling.</summary>
    public enum ZoneShape : byte
    {
        Hexagon = 0,
        Square = 1,
        Triangle = 2,
    }

    /// <summary>
    /// A zone of the <see cref="ZoneTiling"/>: the repeating cell (X, Y) it belongs to and which of the
    /// cell's six tiles it is (<see cref="Kind"/>: 0 hexagon, 1-3 squares, 4-5 triangles).
    /// Always stored wrapped (0..Width-1, 0..Height-1).
    /// </summary>
    public readonly struct ZoneCoord : IEquatable<ZoneCoord>
    {
        /// <summary>Tiles per repeating cell: one hexagon, three squares, two triangles.</summary>
        public const int KindsPerCell = 6;

        public readonly int X;
        public readonly int Y;
        public readonly byte Kind;

        public ZoneCoord(int x, int y, int kind = 0)
        {
            if (kind < 0 || kind >= KindsPerCell) throw new ArgumentOutOfRangeException(nameof(kind));
            X = x;
            Y = y;
            Kind = (byte)kind;
        }

        public ZoneShape Shape => Kind == 0 ? ZoneShape.Hexagon : Kind <= 3 ? ZoneShape.Square : ZoneShape.Triangle;

        /// <summary>One int that identifies the zone (for seeds and network messages); see <see cref="FromKey"/>.</summary>
        public int Key => (X & 0xFFF) | ((Y & 0xFFF) << 12) | (Kind << 24);

        public static ZoneCoord FromKey(int key) => new ZoneCoord(key & 0xFFF, (key >> 12) & 0xFFF, (key >> 24) & 0xFF);

        public bool Equals(ZoneCoord other) => X == other.X && Y == other.Y && Kind == other.Kind;
        public override bool Equals(object obj) => obj is ZoneCoord other && Equals(other);
        public override int GetHashCode() => Key;
        public static bool operator ==(ZoneCoord a, ZoneCoord b) => a.Equals(b);
        public static bool operator !=(ZoneCoord a, ZoneCoord b) => !a.Equals(b);

        /// <summary>A short name players can say: "2.1" for a hexagon, "2.1a" to "2.1c" for squares, "2.1d"/"2.1e" for triangles.</summary>
        public override string ToString() => Kind == 0 ? $"{X}.{Y}" : $"{X}.{Y}{(char)('a' + Kind - 1)}";
    }
}
