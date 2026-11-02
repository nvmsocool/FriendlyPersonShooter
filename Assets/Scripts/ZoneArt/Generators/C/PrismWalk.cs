using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// The framework's <see cref="Walkability"/> analysis replayed on the generator's own collider triangles
    /// (<see cref="PrismCollision"/>) on worker threads, so the build can check and repair the ground it is about to
    /// create without Physics. Same rules: 1 m columns sampled at their centres, up to 8 surfaces per column, 50 degree
    /// slope limit, a player capsule of headroom, 4-neighbour moves up at most 1.0 m and down at most 20 m, the biggest
    /// two-way set from the same natural starts.
    /// </summary>
    public sealed class PrismWalk
    {
        public const int N = 250;
        private const int MaxLevels = 8;

        public float[] Heights;     // per node
        public int[] ColumnOf;      // per node
        public int[] FirstNode;     // per column, N*N + 1
        public bool[] TwoWay;       // per node
        public byte[] Map;          // per column: 0 nothing, 1 standable, 2 two-way
        public int Walkable, TwoWayCount;
        public float TwoWayShare;
        public readonly float[] EdgeReach = new float[4];
        public readonly List<int> UnreachableAnchors = new List<int>(); // indices into zone.Anchors

        public float MinEdge => Mathf.Min(Mathf.Min(EdgeReach[0], EdgeReach[1]), Mathf.Min(EdgeReach[2], EdgeReach[3]));

        public override string ToString() =>
            $"two-way {TwoWayShare * 100f:0.0}% of {Walkable}, edges {EdgeReach[0] * 100f:0}/{EdgeReach[1] * 100f:0}/{EdgeReach[2] * 100f:0}/{EdgeReach[3] * 100f:0}%, unreachable anchors {UnreachableAnchors.Count}";

        public static PrismWalk Analyze(ZoneRequest zone, PrismCollision world)
        {
            var o = zone.Origin;
            float minNormalY = Mathf.Cos(Walkability.MaxSlope * Mathf.Deg2Rad);
            float r = Walkability.PlayerRadius;
            var rowHeights = new List<float>[N];
            var rowCols = new List<int>[N];
            Parallel.For(0, N, z =>
            {
                var hs = new List<float>(N * 2);
                var cs = new List<int>(N * 2);
                var ys = new float[MaxLevels];
                var ns = new Vector3[MaxLevels];
                var buf = new List<(float, int)>(32);
                for (int x = 0; x < N; x++)
                {
                    float wx = o.x + x + 0.5f, wz = o.z + z + 0.5f;
                    int count = world.Column(wx, wz, ys, ns, MaxLevels, buf);
                    for (int l = 0; l < count; l++)
                    {
                        if (ns[l].y < minNormalY) continue;
                        var p = new Vector3(wx, ys[l], wz);
                        var bottom = p + Vector3.up * (r + 0.35f);
                        var top = p + Vector3.up * (Walkability.PlayerHeight - r);
                        if (world.CapsuleHits(bottom, top, r)) continue;
                        hs.Add(ys[l]);
                        cs.Add(z * N + x);
                    }
                }
                rowHeights[z] = hs;
                rowCols[z] = cs;
            });

            var w = new PrismWalk { FirstNode = new int[N * N + 1], Map = new byte[N * N] };
            int total = 0;
            for (int z = 0; z < N; z++) total += rowHeights[z].Count;
            w.Heights = new float[total];
            w.ColumnOf = new int[total];
            int idx = 0;
            for (int z = 0; z < N; z++)
            {
                var hs = rowHeights[z];
                var cs = rowCols[z];
                for (int i = 0; i < hs.Count; i++)
                {
                    w.Heights[idx] = hs[i];
                    w.ColumnOf[idx] = cs[i];
                    idx++;
                }
            }
            // FirstNode per column (nodes are already in column order).
            {
                int k = 0;
                for (int col = 0; col < N * N; col++)
                {
                    w.FirstNode[col] = k;
                    while (k < total && w.ColumnOf[k] == col) k++;
                }
                w.FirstNode[N * N] = total;
            }
            w.Walkable = total;
            w.TwoWay = new bool[total];
            if (total == 0) return w;

            var starts = new List<Vector3>();
            foreach (var a in zone.Anchors) if (a.Kind == ZoneAnchorKind.Shrine || a.Kind == ZoneAnchorKind.Gate) starts.Add(a.World);
            starts.Add(zone.Center);
            float hsz = zone.Size * 0.5f;
            starts.Add(zone.Center + new Vector3(hsz - 2f, 0f, 0f));
            starts.Add(zone.Center + new Vector3(-hsz + 2f, 0f, 0f));
            starts.Add(zone.Center + new Vector3(0f, 0f, hsz - 2f));
            starts.Add(zone.Center + new Vector3(0f, 0f, -hsz + 2f));

            bool[] best = null;
            int bestCount = -1;
            var tried = new HashSet<int>();
            foreach (var s in starts)
            {
                int node = w.LowestNodeNear(zone, s);
                if (node < 0 || !tried.Add(node) || (best != null && best[node])) continue;
                var fwd = w.Flood(node, false);
                var back = w.Flood(node, true);
                var both = new bool[total];
                int c = 0;
                for (int i = 0; i < total; i++)
                {
                    both[i] = fwd[i] && back[i];
                    if (both[i]) c++;
                }
                if (c > bestCount)
                {
                    bestCount = c;
                    best = both;
                }
            }
            if (best == null) return w;
            w.TwoWay = best;
            w.TwoWayCount = bestCount;
            w.TwoWayShare = bestCount / (float)total;
            for (int i = 0; i < total; i++)
            {
                int col = w.ColumnOf[i];
                if (best[i]) w.Map[col] = 2;
                else if (w.Map[col] == 0) w.Map[col] = 1;
            }
            for (int side = 0; side < 4; side++)
            {
                int hit = 0;
                for (int i = 0; i < N; i++)
                {
                    bool ok = false;
                    for (int d = 0; d < 2 && !ok; d++)
                    {
                        int x = side == 1 ? N - 1 - d : side == 3 ? d : i;
                        int z = side == 0 ? N - 1 - d : side == 2 ? d : i;
                        ok = w.Map[z * N + x] == 2;
                    }
                    if (ok) hit++;
                }
                w.EdgeReach[side] = hit / (float)N;
            }
            for (int ai = 0; ai < zone.Anchors.Count; ai++)
            {
                if (!w.AnchorReachable(zone, zone.Anchors[ai].World)) w.UnreachableAnchors.Add(ai);
            }
            return w;
        }

        public bool AnchorReachable(ZoneRequest zone, Vector3 world)
        {
            int ax = Mathf.Clamp(Mathf.FloorToInt(world.x - zone.Origin.x), 0, N - 1);
            int az = Mathf.Clamp(Mathf.FloorToInt(world.z - zone.Origin.z), 0, N - 1);
            for (int dz = -3; dz <= 3; dz++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = ax + dx, z = az + dz;
                    if (x >= 0 && z >= 0 && x < N && z < N && Map[z * N + x] == 2) return true;
                }
            }
            return false;
        }

        private int LowestNodeNear(ZoneRequest zone, Vector3 wpos)
        {
            int bx = Mathf.Clamp(Mathf.FloorToInt(wpos.x - zone.Origin.x), 0, N - 1);
            int bz = Mathf.Clamp(Mathf.FloorToInt(wpos.z - zone.Origin.z), 0, N - 1);
            for (int r = 0; r < 12; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int x = bx + dx, z = bz + dz;
                        if (x < 0 || z < 0 || x >= N || z >= N) continue;
                        int col = z * N + x;
                        int lo = -1;
                        for (int k = FirstNode[col]; k < FirstNode[col + 1]; k++) if (lo < 0 || Heights[k] < Heights[lo]) lo = k;
                        if (lo >= 0) return lo;
                    }
                }
            }
            return -1;
        }

        private bool[] Flood(int start, bool reverse)
        {
            int count = Heights.Length;
            var seen = new bool[count];
            var stack = new Stack<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                int col = ColumnOf[cur];
                int cx = col % N, cz = col / N;
                float h = Heights[cur];
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= N || nz >= N) continue;
                    int nc = nz * N + nx;
                    for (int k = FirstNode[nc]; k < FirstNode[nc + 1]; k++)
                    {
                        if (seen[k]) continue;
                        float rise = reverse ? h - Heights[k] : Heights[k] - h;
                        if (rise <= Walkability.MaxClimb && rise >= -Walkability.MaxDrop)
                        {
                            seen[k] = true;
                            stack.Push(k);
                        }
                    }
                }
            }
            return seen;
        }
    }
}
