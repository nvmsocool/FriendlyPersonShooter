using System.Collections.Generic;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// Worn trails that tie a zone together, so players can follow one to somewhere named.
    /// - Every edge of the zone gets a trail end at the edge's midpoint. The neighbour puts its own end at the same
    ///   spot, so trails carry on across the border.
    /// - Each trail end, landmark, shrine, gate and raid door is routed to a hub (the landmark nearest the middle) over
    ///   the walkable ground. A route stops as soon as it meets an earlier trail, so trails branch and merge
    ///   instead of running side by side.
    /// - Routes prefer gentle ground and avoid water, hex columns and towers.
    /// The result is <see cref="PrismTerrain.TrailDist"/>: plants and props keep off it, and the ground is painted.
    /// Pure and deterministic (one thread, fixed order), so headless servers and clients agree on what grows where.
    /// </summary>
    public static class PrismTrails
    {
        /// <summary>How far out <see cref="PrismTerrain.TrailDist"/> is filled in (metres); farther counts as far.</summary>
        public const float Reach = 9f;

        public struct Stats
        {
            public int Ends, Routed, Cells;
            public override string ToString() => $"trails {Routed}/{Ends} routed, {Cells} m";
        }

        public static Stats Build(PrismTerrain t, ZoneRequest zone, List<LandmarkPlan> landmarks, List<TowerPlan> towers)
        {
            var stats = new Stats();
            int C = t.C;
            var o = zone.Origin;
            var blocked = new bool[C * C];
            for (int k = 0; k < C * C; k++)
            {
                blocked[k] = t.CellEdge[k] < 0.5f || t.Steep[k] || t.Comp[k] != t.MainComp;
            }
            foreach (var tw in towers) Stamp(blocked, C, tw.Local, tw.R + 1.5f);

            // Hub: the landmark nearest the middle, or the middle itself.
            var hubPoint = t.Mid;
            float best = float.MaxValue;
            foreach (var lp in landmarks)
            {
                float d = (lp.Local - t.Mid).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    hubPoint = lp.Local;
                }
            }
            int hub = Snap(t, blocked, hubPoint, 20);
            if (hub < 0) return stats;

            // Trail ends: edge midpoints first (they form the spine), then the named places.
            var ends = new List<Vector2>();
            var outline = zone.Outline;
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                var mid = (a + b) * 0.5f;
                var e = b - a;
                var inward = new Vector2(-e.y, e.x).normalized;
                ends.Add(new Vector2(mid.x - o.x, mid.y - o.z) + inward * 1.5f);
            }
            foreach (var lp in landmarks) if ((lp.Local - hubPoint).sqrMagnitude > 1f) ends.Add(lp.Local);
            foreach (var an in zone.Anchors)
            {
                if (an.Kind == ZoneAnchorKind.Shrine || an.Kind == ZoneAnchorKind.Gate) ends.Add(new Vector2(an.World.x - o.x, an.World.z - o.z));
            }

            var wiggle = ZoneNoise.Offset(zone.LayerSeed, 0x7A11);
            var cost = new float[C * C];
            for (int k = 0; k < C * C; k++)
            {
                if (blocked[k]) continue;
                float wx = o.x + k % C + 0.5f, wz = o.z + k / C + 0.5f;
                float c = 1f + 0.7f * Mathf.Abs(ZoneNoise.Fbm(wx + wiggle.x, wz + wiggle.y, 1f / 25f, 2));
                if (t.CellHex[k] >= 0) c += 5f;
                if (t.HasWater && t.S[k] < t.WaterLevel + 0.2f) c += 4f;
                cost[k] = c;
            }

            var onTrail = new bool[C * C];
            onTrail[hub] = true;
            var dist = new float[C * C];
            var from = new int[C * C];
            var heap = new MinHeap(4096);
            foreach (var end in ends)
            {
                stats.Ends++;
                int start = Snap(t, blocked, end, 12);
                if (start < 0) continue;
                if (Route(t, blocked, cost, onTrail, start, dist, from, heap, out int reached))
                {
                    stats.Routed++;
                    for (int k = reached; k != start; k = from[k]) onTrail[k] = true;
                    onTrail[start] = true;
                }
            }

            // Distance from each cell to the nearest trail cell, out to Reach.
            var td = t.TrailDist;
            for (int k = 0; k < td.Length; k++) td[k] = Reach;
            int r = Mathf.CeilToInt(Reach);
            for (int k = 0; k < C * C; k++)
            {
                if (!onTrail[k]) continue;
                stats.Cells++;
                int x = k % C, z = k / C;
                for (int dz = -r; dz <= r; dz++)
                {
                    int zz = z + dz;
                    if (zz < 0 || zz >= C) continue;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= C) continue;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        int j = zz * C + xx;
                        if (d < td[j]) td[j] = d;
                    }
                }
            }
            return stats;
        }

        /// <summary>The cheapest walk from <paramref name="start"/> to any trail cell (Dijkstra, 8 neighbours).</summary>
        private static bool Route(PrismTerrain t, bool[] blocked, float[] cost, bool[] onTrail, int start, float[] dist, int[] from, MinHeap heap, out int reached)
        {
            int C = t.C;
            for (int k = 0; k < dist.Length; k++) dist[k] = float.MaxValue;
            heap.Clear();
            dist[start] = 0f;
            from[start] = start;
            heap.Push(start, 0f);
            reached = -1;
            while (heap.Count > 0)
            {
                heap.Pop(out int k, out float dk);
                if (dk > dist[k]) continue;
                if (onTrail[k])
                {
                    reached = k;
                    return true;
                }
                int x = k % C, z = k / C;
                for (int d = 0; d < 8; d++)
                {
                    int dx = DX[d], dz = DZ[d];
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= C || nz >= C) continue;
                    int j = nz * C + nx;
                    if (blocked[j]) continue;
                    // Diagonals only where both sides are open, so trails never squeeze between corners.
                    if (dx != 0 && dz != 0 && (blocked[z * C + nx] || blocked[nz * C + x])) continue;
                    float step = dx != 0 && dz != 0 ? 1.4142f : 1f;
                    float rise = Mathf.Abs(t.S[j] - t.S[k]);
                    if (rise > PrismTerrain.Climb * step) continue;
                    float c = step * (cost[j] + 6f * rise / step);
                    // Following an earlier trail is cheap, so routes merge into one another.
                    if (onTrail[j]) c *= 0.3f;
                    float nd = dk + c;
                    if (nd < dist[j])
                    {
                        dist[j] = nd;
                        from[j] = k;
                        heap.Push(j, nd);
                    }
                }
            }
            return false;
        }

        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>The open cell nearest a local point, within <paramref name="radius"/> metres, or -1.</summary>
        private static int Snap(PrismTerrain t, bool[] blocked, Vector2 p, int radius)
        {
            int C = t.C;
            int cx = Mathf.Clamp((int)p.x, 0, C - 1), cz = Mathf.Clamp((int)p.y, 0, C - 1);
            int bestK = -1;
            float best = float.MaxValue;
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= C || z >= C) continue;
                    int k = z * C + x;
                    if (blocked[k]) continue;
                    float d = dx * dx + dz * dz;
                    if (d < best && d <= radius * radius)
                    {
                        best = d;
                        bestK = k;
                    }
                }
            }
            return bestK;
        }

        private static void Stamp(bool[] cells, int C, Vector2 c, float r)
        {
            for (int z = Mathf.Max(0, (int)(c.y - r)); z <= Mathf.Min(C - 1, (int)(c.y + r)); z++)
                for (int x = Mathf.Max(0, (int)(c.x - r)); x <= Mathf.Min(C - 1, (int)(c.x + r)); x++)
                    if ((new Vector2(x + 0.5f, z + 0.5f) - c).sqrMagnitude <= r * r) cells[z * C + x] = true;
        }

        /// <summary>A binary min-heap of (cell, cost); ties go to the lower cell so routes never depend on luck.</summary>
        private sealed class MinHeap
        {
            private int[] _cells;
            private float[] _keys;
            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                _cells = new int[capacity];
                _keys = new float[capacity];
            }

            public void Clear() => Count = 0;

            private bool Less(int a, int b) => _keys[a] < _keys[b] || (_keys[a] == _keys[b] && _cells[a] < _cells[b]);

            public void Push(int cell, float key)
            {
                if (Count == _cells.Length)
                {
                    System.Array.Resize(ref _cells, Count * 2);
                    System.Array.Resize(ref _keys, Count * 2);
                }
                int i = Count++;
                _cells[i] = cell;
                _keys[i] = key;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (!Less(i, p)) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public void Pop(out int cell, out float key)
            {
                cell = _cells[0];
                key = _keys[0];
                Count--;
                if (Count == 0) return;
                _cells[0] = _cells[Count];
                _keys[0] = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < Count && Less(l, m)) m = l;
                    if (r < Count && Less(r, m)) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
            }

            private void Swap(int a, int b)
            {
                (_cells[a], _cells[b]) = (_cells[b], _cells[a]);
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
            }
        }
    }
}
