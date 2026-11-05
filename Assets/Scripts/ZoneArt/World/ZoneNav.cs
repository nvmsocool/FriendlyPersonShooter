using System.Collections.Generic;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// Where a player can stand in one built zone, for bot pathfinding: a 1 m grid of columns, each with every
    /// standable level (ground, bridge, tower top...). Built from the zone's colliders with the same rules as
    /// <see cref="Walkability"/> (slope, body capsule).
    /// </summary>
    public sealed class ZoneNavGrid
    {
        public const float Cell = 1f;
        public readonly int N;
        public readonly Vector3 Origin;
        /// <summary>Nodes of column c are First[c] .. First[c + 1] - 1.</summary>
        public readonly int[] First;
        public readonly float[] Heights;

        public int Count => Heights.Length;

        private ZoneNavGrid(int n, Vector3 origin, int[] first, float[] heights)
        {
            N = n;
            Origin = origin;
            First = first;
            Heights = heights;
        }

        /// <summary>Builds the grid all at once (about 0.1 s for a hexagon zone). Covers the square Origin .. Origin + size.</summary>
        public static ZoneNavGrid Build(Vector3 origin, float size)
        {
            var b = new Builder(origin, size);
            while (!b.Step(float.MaxValue)) { }
            return b.Result;
        }

        /// <summary>Builds a grid a few rows at a time.</summary>
        public sealed class Builder
        {
            private readonly Vector3 _origin;
            private readonly int _n;
            private readonly List<float> _heights;
            private readonly int[] _first;
            private int _row;
            private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

            public ZoneNavGrid Result { get; private set; }

            public Builder(Vector3 origin, float size)
            {
                _origin = origin;
                _n = Mathf.RoundToInt(size / Cell);
                _heights = new List<float>(_n * _n + _n * 8);
                _first = new int[_n * _n + 1];
            }

            /// <summary>Does rows until <paramref name="budgetMs"/> is spent; true once the grid is done.</summary>
            public bool Step(float budgetMs)
            {
                if (Result != null) return true;
                if (_row == 0) Physics.SyncTransforms();
                _clock.Restart();
                float minNormalY = Mathf.Cos(Walkability.MaxSlope * Mathf.Deg2Rad);
                float r = Walkability.PlayerRadius;
                int n = _n;
                while (_row < n)
                {
                    int z = _row++;
                    for (int x = 0; x < n; x++)
                    {
                        _first[z * n + x] = _heights.Count;
                        var from = new Vector3(_origin.x + (x + 0.5f) * Cell, 800f, _origin.z + (z + 0.5f) * Cell);
                        for (int level = 0; level < 6; level++)
                        {
                            if (!Physics.Raycast(from, Vector3.down, out var hit, 2000f, ZoneWorld.CollisionMask, QueryTriggerInteraction.Ignore)) break;
                            from = hit.point + Vector3.down * 0.05f;
                            if (hit.normal.y < minNormalY) continue;
                            var p = hit.point;
                            if (Physics.CheckCapsule(p + Vector3.up * (r + 0.35f), p + Vector3.up * (Walkability.PlayerHeight - r), r,
                                    ZoneWorld.CollisionMask, QueryTriggerInteraction.Ignore)) continue;
                            _heights.Add(p.y);
                        }
                    }
                    if (_clock.Elapsed.TotalMilliseconds >= budgetMs) break;
                }
                if (_row < n) return false;
                _first[n * n] = _heights.Count;
                Result = new ZoneNavGrid(n, _origin, _first, _heights.ToArray());
                return true;
            }
        }

        /// <summary>The node in column (x, z) closest in height to y (or -1 if the column has none).</summary>
        public int NodeNear(int x, int z, float y)
        {
            int col = z * N + x, best = -1;
            float bestD = float.MaxValue;
            for (int k = First[col]; k < First[col + 1]; k++)
            {
                float d = Mathf.Abs(Heights[k] - y);
                if (y < Heights[k] - 0.6f) d += 2f; // prefer surfaces at or below the feet
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Weighted A* over the built zones' nav grids, across zone borders and the world's wrap. Paths respect
    /// what a player can do: climb at most <see cref="MaxClimb"/> (a jump), drop at most <see cref="MaxDrop"/>.
    /// Searches are capped; a capped search returns the path to the explored node closest to the goal.
    /// The search runs on a 1 m column grid that doesn't wrap, laid around the start: each column looks up the
    /// zone under its (wrapped) position and that zone's nav grid.
    /// </summary>
    public sealed class ZoneNavigator
    {
        public const float MaxClimb = 1.0f;
        public const float MaxDrop = 6f;
        public int MaxExpansions = 25000;
        public float HeuristicWeight = 2f;

        private readonly ZoneWorld _world;
        private readonly Dictionary<long, float> _g = new Dictionary<long, float>();
        private readonly Dictionary<long, long> _from = new Dictionary<long, long>();
        private readonly List<(float f, long id)> _heap = new List<(float, long)>();

        public ZoneNavigator(ZoneWorld world) => _world = world;

        /// <summary>
        /// A path of world positions (feet height) from <paramref name="start"/> toward <paramref name="goal"/>,
        /// empty if the start isn't on navigable ground. Positions run on from the start's copy of the world without
        /// wrapping, so they may lie past the wrap seam: move them to the copy nearest the bot before steering.
        /// </summary>
        public List<Vector3> FindPath(Vector3 start, Vector3 goal, out bool complete)
        {
            complete = false;
            var path = new List<Vector3>();
            if (!Locate(start, out long s)) return path;
            // The goal's copy nearest the start, so the search heads the short way round.
            var g2 = _world.Tiling.Near(new System.Numerics.Vector2(goal.x, goal.z), new System.Numerics.Vector2(start.x, start.z));
            goal = new Vector3(g2.X, goal.y, g2.Y);
            Locate(goal, out long goalId);
            int gx = Mathf.FloorToInt(goal.x / ZoneNavGrid.Cell), gz = Mathf.FloorToInt(goal.z / ZoneNavGrid.Cell);

            _g.Clear();
            _from.Clear();
            _heap.Clear();
            _g[s] = 0f;
            Push(H(s, gx, gz, goal.y), s);
            long best = s;
            float bestH = float.MaxValue;
            int expansions = 0;
            while (_heap.Count > 0 && expansions < MaxExpansions)
            {
                var (_, cur) = Pop();
                expansions++;
                Decode(cur, out int cx, out int cz, out float cy, out var grid, out int node);
                float h = Heuristic(cx, cz, gx, gz) + Mathf.Abs(cy - goal.y) * 0.5f;
                if (h < bestH)
                {
                    bestH = h;
                    best = cur;
                }
                if (cur == goalId || (cx == gx && cz == gz))
                {
                    best = cur;
                    complete = true;
                    break;
                }
                float gCur = _g[cur];
                for (int d = 0; d < 8; d++)
                {
                    int dx = DX[d], dz = DZ[d];
                    int nx = cx + dx, nz = cz + dz;
                    if (!Column(nx, nz, out var ng, out int lx, out int lz)) continue;
                    // Diagonals only when both side columns are open at about this height (no corner cutting).
                    if (dx != 0 && dz != 0 && (!Standable(cx + dx, cz, cy) || !Standable(cx, cz + dz, cy))) continue;
                    int col = lz * ng.N + lx;
                    for (int k = ng.First[col]; k < ng.First[col + 1]; k++)
                    {
                        float rise = ng.Heights[k] - cy;
                        if (rise > MaxClimb || rise < -MaxDrop) continue;
                        float step = (dx != 0 && dz != 0 ? 1.414f : 1f) + (rise > 0.45f ? 2f : 0f) + (rise < -1.5f ? -rise * 0.3f : 0f);
                        long id = Encode(nx, nz, k);
                        float g = gCur + step;
                        if (_g.TryGetValue(id, out float old) && old <= g) continue;
                        _g[id] = g;
                        _from[id] = cur;
                        Push(g + HeuristicWeight * (Heuristic(nx, nz, gx, gz) + Mathf.Abs(ng.Heights[k] - goal.y) * 0.5f), id);
                    }
                }
            }

            for (long at = best; ; at = _from[at])
            {
                Decode(at, out int x, out int z, out float y, out _, out _);
                path.Add(new Vector3((x + 0.5f) * ZoneNavGrid.Cell, y, (z + 0.5f) * ZoneNavGrid.Cell));
                if (!_from.ContainsKey(at)) break;
            }
            path.Reverse();
            return Simplify(path);
        }

        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Keeps every few points plus every height change, so bots get steady waypoints.</summary>
        private static List<Vector3> Simplify(List<Vector3> path)
        {
            if (path.Count <= 2) return path;
            var outPath = new List<Vector3> { path[0] };
            for (int i = 1; i < path.Count - 1; i++)
            {
                bool climb = Mathf.Abs(path[i + 1].y - path[i].y) > 0.45f || Mathf.Abs(path[i].y - path[i - 1].y) > 0.45f;
                if (climb || i % 3 == 0) outPath.Add(path[i]);
            }
            outPath.Add(path[path.Count - 1]);
            return outPath;
        }

        private float H(long id, int gx, int gz, float gy)
        {
            Decode(id, out int x, out int z, out float y, out _, out _);
            return HeuristicWeight * (Heuristic(x, z, gx, gz) + Mathf.Abs(y - gy) * 0.5f);
        }

        private static float Heuristic(int x, int z, int gx, int gz)
        {
            int dx = Mathf.Abs(x - gx), dz = Mathf.Abs(z - gz);
            return Mathf.Max(dx, dz) + 0.414f * Mathf.Min(dx, dz);
        }

        private bool Standable(int x, int z, float y)
        {
            if (!Column(x, z, out var grid, out int lx, out int lz)) return false;
            int col = lz * grid.N + lx;
            for (int k = grid.First[col]; k < grid.First[col + 1]; k++)
            {
                if (Mathf.Abs(grid.Heights[k] - y) <= MaxClimb) return true;
            }
            return false;
        }

        /// <summary>
        /// The nav grid holding column (x, z) (any copy of the world), with the column's coordinates in that grid.
        /// False if the zone isn't built (or has no grid yet).
        /// </summary>
        private bool Column(int x, int z, out ZoneNavGrid grid, out int lx, out int lz)
        {
            var p = new System.Numerics.Vector2((x + 0.5f) * ZoneNavGrid.Cell, (z + 0.5f) * ZoneNavGrid.Cell);
            var zone = _world.Tiling.Locate(p, out var canonical);
            grid = _world.NavGrid(zone);
            lx = lz = 0;
            if (grid == null) return false;
            lx = Mathf.FloorToInt((canonical.X - grid.Origin.x) / ZoneNavGrid.Cell);
            lz = Mathf.FloorToInt((canonical.Y - grid.Origin.z) / ZoneNavGrid.Cell);
            if (lx < 0 || lz < 0 || lx >= grid.N || lz >= grid.N)
            {
                grid = null;
                return false;
            }
            return true;
        }

        private bool Locate(Vector3 p, out long id)
        {
            id = -1;
            int x = Mathf.FloorToInt(p.x / ZoneNavGrid.Cell), z = Mathf.FloorToInt(p.z / ZoneNavGrid.Cell);
            for (int r = 0; r < 4; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int cx = x + dx, cz = z + dz;
                        if (!Column(cx, cz, out var grid, out int lx, out int lz)) continue;
                        int k = grid.NodeNear(lx, lz, p.y);
                        if (k < 0 || Mathf.Abs(grid.Heights[k] - p.y) > 2.5f) continue;
                        id = Encode(cx, cz, k);
                        return true;
                    }
                }
            }
            return false;
        }

        // Node ids: column x and z (16 bits each, offset so -32768..32767 fit), node index inside its zone grid (32 bits).
        private const int ColumnOffset = 32768;

        private static long Encode(int x, int z, int node) =>
            ((long)((x + ColumnOffset) & 0xFFFF) << 48) | ((long)((z + ColumnOffset) & 0xFFFF) << 32) | (uint)node;

        private void Decode(long id, out int x, out int z, out float y, out ZoneNavGrid grid, out int node)
        {
            x = (int)((id >> 48) & 0xFFFF) - ColumnOffset;
            z = (int)((id >> 32) & 0xFFFF) - ColumnOffset;
            node = (int)(id & 0xFFFFFFFF);
            Column(x, z, out grid, out _, out _);
            y = grid != null && node < grid.Count ? grid.Heights[node] : 0f;
        }

        private void Push(float f, long id)
        {
            _heap.Add((f, id));
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_heap[parent].f <= _heap[i].f) break;
                (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
                i = parent;
            }
        }

        private (float f, long id) Pop()
        {
            var top = _heap[0];
            var last = _heap[_heap.Count - 1];
            _heap.RemoveAt(_heap.Count - 1);
            if (_heap.Count > 0)
            {
                _heap[0] = last;
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _heap.Count && _heap[l].f < _heap[m].f) m = l;
                    if (r < _heap.Count && _heap[r].f < _heap[m].f) m = r;
                    if (m == i) break;
                    (_heap[m], _heap[i]) = (_heap[i], _heap[m]);
                    i = m;
                }
            }
            return top;
        }
    }
}
