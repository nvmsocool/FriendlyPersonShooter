using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.World
{
    /// <summary>
    /// The world's zones: a rhombitrihexagonal tiling (hexagons, squares and triangles, all with the same edge
    /// length) that wraps at its edges. The tiling repeats on a triangular lattice; each repeating cell holds one
    /// hexagon, three squares and two triangles. The world is <see cref="Width"/> by <see cref="Height"/> cells, a
    /// rhombus whose opposite sides are glued together, so walking off one side brings you back on the other.
    ///
    /// Positions are <see cref="Vector2"/> with X = world x and Y = world z, in metres. Every zone has one
    /// canonical copy (cell X in [0, Width), Y in [0, Height)); <see cref="WrapPoint"/> moves any position into the
    /// canonical copy of the zone it falls in. Distances are measured the short way around.
    /// </summary>
    public sealed class ZoneTiling
    {
        private static readonly double Sqrt3 = Math.Sqrt(3.0);

        public int Width { get; }
        public int Height { get; }
        /// <summary>Edge length shared by every tile, in metres.</summary>
        public float Edge { get; }
        /// <summary>Distance between neighbouring hexagon centres (the lattice spacing).</summary>
        public float Spacing { get; }
        /// <summary>The two lattice steps between hexagon centres: along x, and 60 degrees from it.</summary>
        public Vector2 StepA { get; }
        public Vector2 StepB { get; }
        /// <summary>The world repeats every <see cref="PeriodA"/> and every <see cref="PeriodB"/>.</summary>
        public Vector2 PeriodA { get; }
        public Vector2 PeriodB { get; }

        public int Count => _zones.Length;
        /// <summary>Every zone, in index order (cell by cell, row by row).</summary>
        public IReadOnlyList<ZoneCoord> Zones => _zones;

        private readonly double _d, _r;
        private readonly ZoneCoord[] _zones;
        private readonly Vector2[] _centres;
        private readonly Vector2[][] _outlines;
        private readonly ZoneCoord[][] _neighbors, _touching;
        private readonly Dictionary<int, int[]> _distances = new Dictionary<int, int[]>();

        // Each kind's centre offset from its cell's hexagon centre, in units of StepA / StepB.
        private static readonly double[,] KindOffset =
        {
            { 0, 0 }, { 0.5, 0 }, { 0, 0.5 }, { -0.5, 0.5 }, { 1.0 / 3, 1.0 / 3 }, { 2.0 / 3, 2.0 / 3 },
        };

        // Square on hexagon edge k (normal at 60k degrees): cell offset and kind.
        private static readonly int[,] SquareOnEdge = { { 0, 0, 1 }, { 0, 0, 2 }, { 0, 0, 3 }, { -1, 0, 1 }, { 0, -1, 2 }, { 1, -1, 3 } };
        // Triangle at hexagon corner m (at 30 + 60m degrees): cell offset and kind.
        private static readonly int[,] TriangleAtCorner = { { 0, 0, 4 }, { -1, 0, 5 }, { -1, 0, 4 }, { -1, -1, 5 }, { 0, -1, 4 }, { 0, -1, 5 } };

        public ZoneTiling(int width, int height, float edge)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (edge <= 0f) throw new ArgumentOutOfRangeException(nameof(edge));
            Width = width;
            Height = height;
            Edge = edge;
            _d = edge * (1.0 + Sqrt3);
            _r = _d / Sqrt3;
            Spacing = (float)_d;
            StepA = new Vector2((float)_d, 0f);
            StepB = new Vector2((float)(_d * 0.5), (float)(_d * Sqrt3 * 0.5));
            PeriodA = StepA * width;
            PeriodB = StepB * height;

            _zones = new ZoneCoord[width * height * ZoneCoord.KindsPerCell];
            _centres = new Vector2[_zones.Length];
            _outlines = new Vector2[_zones.Length][];
            for (int i = 0; i < _zones.Length; i++)
            {
                int kind = i % ZoneCoord.KindsPerCell, cell = i / ZoneCoord.KindsPerCell;
                var z = new ZoneCoord(cell % width, cell / width, kind);
                _zones[i] = z;
                _centres[i] = CellCentre(z.X + KindOffset[kind, 0], z.Y + KindOffset[kind, 1]);
                _outlines[i] = MakeOutline(_centres[i], kind);
            }
            _neighbors = new ZoneCoord[_zones.Length][];
            _touching = new ZoneCoord[_zones.Length][];
            for (int i = 0; i < _zones.Length; i++) FindNeighbors(i);
        }

        // ------------------------------------------------------------------ zones

        public int IndexOf(ZoneCoord zone)
        {
            var z = Wrap(zone);
            return (z.Y * Width + z.X) * ZoneCoord.KindsPerCell + z.Kind;
        }

        public ZoneCoord Wrap(ZoneCoord zone) => new ZoneCoord(Mod(zone.X, Width), Mod(zone.Y, Height), zone.Kind);

        /// <summary>True if the zone's cell is inside the world (no wrapping needed).</summary>
        public bool InRange(ZoneCoord zone) => zone.X >= 0 && zone.Y >= 0 && zone.X < Width && zone.Y < Height;

        /// <summary>The middle of a zone's canonical copy.</summary>
        public Vector2 Centre(ZoneCoord zone) => _centres[IndexOf(zone)];

        /// <summary>The corners of a zone's canonical copy, counter-clockwise (x right, z up).</summary>
        public IReadOnlyList<Vector2> Outline(ZoneCoord zone) => _outlines[IndexOf(zone)];

        /// <summary>The smallest box around a zone's canonical copy.</summary>
        public void Bounds(ZoneCoord zone, out Vector2 min, out Vector2 max)
        {
            var o = _outlines[IndexOf(zone)];
            min = o[0];
            max = o[0];
            for (int i = 1; i < o.Length; i++)
            {
                min = Vector2.Min(min, o[i]);
                max = Vector2.Max(max, o[i]);
            }
        }

        /// <summary>A zone's area in square metres.</summary>
        public float Area(ZoneCoord zone)
        {
            float s = Edge;
            switch (zone.Shape)
            {
                case ZoneShape.Hexagon: return (float)(1.5 * Sqrt3 * s * s);
                case ZoneShape.Square: return s * s;
                default: return (float)(Sqrt3 / 4.0 * s * s);
            }
        }

        /// <summary>A zone's area as a share of a hexagon's (1 for hexagons, about 0.38 for squares, 0.17 for triangles).</summary>
        public float AreaShare(ZoneCoord zone) => Area(zone) / Area(new ZoneCoord(0, 0, 0));

        /// <summary>
        /// Signed distance from a point to the zone's edge: positive inside, 0 on the edge, negative outside.
        /// The point is taken as is (in the frame of the zone's canonical copy), without wrapping.
        /// </summary>
        public float EdgeDistance(ZoneCoord zone, Vector2 p) => EdgeDistance(_outlines[IndexOf(zone)], p);

        /// <summary>Signed distance to a convex counter-clockwise outline's edge (positive inside).</summary>
        public static float EdgeDistance(IReadOnlyList<Vector2> outline, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var e = outline[(i + 1) % outline.Count] - a;
                var inward = Vector2.Normalize(new Vector2(-e.Y, e.X));
                best = Math.Min(best, Vector2.Dot(p - a, inward));
            }
            return best;
        }

        /// <summary>Zones sharing an edge with this one.</summary>
        public IReadOnlyList<ZoneCoord> Neighbors(ZoneCoord zone) => _neighbors[IndexOf(zone)];

        /// <summary>Zones sharing an edge or a corner with this one.</summary>
        public IReadOnlyList<ZoneCoord> Touching(ZoneCoord zone) => _touching[IndexOf(zone)];

        /// <summary>Zone steps between two zones (crossing edges), the short way around. Neighbours are 1 apart.</summary>
        public int Distance(ZoneCoord a, ZoneCoord b) => DistancesFrom(a)[IndexOf(b)];

        /// <summary>The zone and every zone within <paramref name="radius"/> steps of touching zones (edge or corner).</summary>
        public IReadOnlyCollection<ZoneCoord> Neighborhood(ZoneCoord center, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            var seen = new HashSet<ZoneCoord> { Wrap(center) };
            var frontier = new List<ZoneCoord> { Wrap(center) };
            for (int step = 0; step < radius; step++)
            {
                var next = new List<ZoneCoord>();
                foreach (var z in frontier)
                {
                    foreach (var n in Touching(z)) if (seen.Add(n)) next.Add(n);
                }
                frontier = next;
            }
            return seen;
        }

        // ------------------------------------------------------------------ positions

        /// <summary>The zone a world position falls in (any position; it wraps).</summary>
        public ZoneCoord ZoneAt(double x, double z)
        {
            Classify(x, z, out int i, out int j, out int kind);
            return new ZoneCoord(Mod(i, Width), Mod(j, Height), kind);
        }

        public ZoneCoord ZoneAt(Vector2 p) => ZoneAt(p.X, p.Y);

        /// <summary>The zone a position falls in, and the position's copy in that zone's canonical copy (one lookup for both).</summary>
        public ZoneCoord Locate(Vector2 p, out Vector2 canonical)
        {
            Classify(p.X, p.Y, out int i, out int j, out int kind);
            int ci = Mod(i, Width), cj = Mod(j, Height);
            canonical = ci == i && cj == j ? p : p + StepA * (ci - i) + StepB * (cj - j);
            return new ZoneCoord(ci, cj, kind);
        }

        /// <summary>The copy of a position that lies in the canonical copy of its zone.</summary>
        public Vector2 WrapPoint(Vector2 p)
        {
            Classify(p.X, p.Y, out int i, out int j, out _);
            int di = Mod(i, Width) - i, dj = Mod(j, Height) - j;
            if (di == 0 && dj == 0) return p;
            return p + StepA * di + StepB * dj;
        }

        /// <summary>True if a position is already in the canonical copy of its zone.</summary>
        public bool IsCanonical(Vector2 p)
        {
            Classify(p.X, p.Y, out int i, out int j, out _);
            return i >= 0 && j >= 0 && i < Width && j < Height;
        }

        /// <summary>The shortest step from one position to another, going around the wrap if shorter.</summary>
        public Vector2 Delta(Vector2 from, Vector2 to)
        {
            var v = to - from;
            double b = v.Y / PeriodB.Y;
            double a = (v.X - b * PeriodB.X) / PeriodA.X;
            var v0 = v - PeriodA * (float)Math.Round(a) - PeriodB * (float)Math.Round(b);
            var best = v0;
            float bestLength = v0.LengthSquared();
            for (int m = -1; m <= 1; m++)
            {
                for (int n = -1; n <= 1; n++)
                {
                    if (m == 0 && n == 0) continue;
                    var c = v0 + PeriodA * m + PeriodB * n;
                    float l = c.LengthSquared();
                    if (l < bestLength)
                    {
                        bestLength = l;
                        best = c;
                    }
                }
            }
            return best;
        }

        /// <summary>The copy of <paramref name="p"/> nearest <paramref name="reference"/>.</summary>
        public Vector2 Near(Vector2 p, Vector2 reference) => reference + Delta(reference, p);

        /// <summary>Metres between two positions, the short way around.</summary>
        public float DistanceBetween(Vector2 a, Vector2 b) => Delta(a, b).Length();

        /// <summary>Metres from a position to the nearest copy of a zone (0 inside it).</summary>
        public float DistanceToZone(Vector2 p, ZoneCoord zone)
        {
            int index = IndexOf(zone);
            var centre = _centres[index];
            var shift = Near(centre, p) - centre;
            var q = p - shift;
            var o = _outlines[index];
            if (EdgeDistance(o, q) >= 0f) return 0f;
            float best = float.MaxValue;
            for (int k = 0; k < o.Length; k++) best = Math.Min(best, SegmentDistance(q, o[k], o[(k + 1) % o.Length]));
            return best;
        }

        // ------------------------------------------------------------------ internals

        private Vector2 CellCentre(double i, double j) => new Vector2((float)(_d * (i + j * 0.5)), (float)(_d * Sqrt3 * 0.5 * j));

        private Vector2[] MakeOutline(Vector2 centre, int kind)
        {
            double s = Edge;
            double radius, start;
            int corners;
            if (kind == 0)
            {
                corners = 6;
                radius = s;
                start = 30;
            }
            else if (kind <= 3)
            {
                corners = 4;
                radius = s / Math.Sqrt(2.0);
                start = 60 * (kind - 1) + 45;
            }
            else
            {
                corners = 3;
                radius = s / Sqrt3;
                start = kind == 4 ? 90 : 30;
            }
            var outline = new Vector2[corners];
            for (int k = 0; k < corners; k++)
            {
                double a = (start + 360.0 / corners * k) * Math.PI / 180.0;
                outline[k] = centre + new Vector2((float)(Math.Cos(a) * radius), (float)(Math.Sin(a) * radius));
            }
            return outline;
        }

        /// <summary>Which tile a position falls in, before wrapping: cell (i, j) and kind.</summary>
        private void Classify(double x, double y, out int i, out int j, out int kind)
        {
            // Nearest hexagon centre: round on the triangular lattice (cube rounding).
            double q = (Sqrt3 / 3.0 * x - y / 3.0) / _r;
            double r = (2.0 / 3.0 * y) / _r;
            double cx = q, cz = r, cy = -cx - cz;
            double rx = Math.Round(cx), ry = Math.Round(cy), rz = Math.Round(cz);
            double dx = Math.Abs(rx - cx), dy = Math.Abs(ry - cy), dz = Math.Abs(rz - cz);
            if (dx > dy && dx > dz) rx = -ry - rz;
            else if (dy <= dz) rz = -rx - ry;
            i = (int)rx;
            j = (int)rz;

            double vx = x - _d * (i + j * 0.5), vy = y - _d * Sqrt3 * 0.5 * j;
            int k = 0;
            double best = double.MinValue;
            for (int e = 0; e < 6; e++)
            {
                double a = e * Math.PI / 3.0;
                double dot = vx * Math.Cos(a) + vy * Math.Sin(a);
                if (dot > best)
                {
                    best = dot;
                    k = e;
                }
            }
            double s = Edge;
            if (best <= s * Sqrt3 * 0.5)
            {
                kind = 0;
                return;
            }
            double ak = k * Math.PI / 3.0;
            double across = -vx * Math.Sin(ak) + vy * Math.Cos(ak);
            if (Math.Abs(across) <= s * 0.5)
            {
                i += SquareOnEdge[k, 0];
                j += SquareOnEdge[k, 1];
                kind = SquareOnEdge[k, 2];
                return;
            }
            int m = across > 0 ? k : (k + 5) % 6;
            i += TriangleAtCorner[m, 0];
            j += TriangleAtCorner[m, 1];
            kind = TriangleAtCorner[m, 2];
        }

        private void FindNeighbors(int index)
        {
            var o = _outlines[index];
            var self = _zones[index];
            float eps = Edge * 0.01f;
            var neighbors = new List<ZoneCoord>();
            var touching = new List<ZoneCoord>();
            for (int k = 0; k < o.Length; k++)
            {
                var a = o[k];
                var b = o[(k + 1) % o.Length];
                var e = b - a;
                var outward = Vector2.Normalize(new Vector2(e.Y, -e.X));
                var n = ZoneAt((a + b) * 0.5f + outward * eps);
                if (n != self && !neighbors.Contains(n)) neighbors.Add(n);
                for (int t = 0; t < 24; t++)
                {
                    double ang = t * Math.PI / 12.0 + 0.01;
                    var c = ZoneAt(a + new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * eps);
                    if (c != self && !touching.Contains(c)) touching.Add(c);
                }
            }
            foreach (var n in neighbors) if (!touching.Contains(n)) touching.Add(n);
            _neighbors[index] = neighbors.ToArray();
            _touching[index] = touching.ToArray();
        }

        private int[] DistancesFrom(ZoneCoord from)
        {
            int start = IndexOf(from);
            if (_distances.TryGetValue(start, out var cached)) return cached;
            var dist = new int[_zones.Length];
            for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;
            dist[start] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (var n in _neighbors[cur])
                {
                    int ni = IndexOf(n);
                    if (dist[ni] != int.MaxValue) continue;
                    dist[ni] = dist[cur] + 1;
                    queue.Enqueue(ni);
                }
            }
            _distances[start] = dist;
            return dist;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Math.Max(0f, Math.Min(1f, Vector2.Dot(p - a, ab) / ab.LengthSquared()));
            return Vector2.Distance(p, a + ab * t);
        }

        private static int Mod(int value, int m)
        {
            int r = value % m;
            return r < 0 ? r + m : r;
        }
    }
}
