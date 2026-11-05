using System.Collections.Generic;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// Checks a built zone the way a player would walk it, using its colliders. It samples every
    /// column of a 1 m grid from the sky down, keeps every surface a player can stand on (not too
    /// steep, enough headroom, several per column for bridges and overhangs), then links neighbours
    /// a player can move between: up at most <see cref="MaxClimb"/> (walk up a slope or jump), down at most
    /// <see cref="MaxDrop"/>. "Two-way" ground is ground you can reach from the start and get back from.
    /// Player numbers match the game: 6.5 m/s, jump 6 m/s at 18 m/s^2 gravity (1.0 m jump).
    /// </summary>
    public static class Walkability
    {
        public const float Cell = 1f;
        public const float MaxClimb = 1.0f;
        public const float MaxDrop = 20f;
        public const float MaxSlope = 50f;
        public const float PlayerRadius = 0.3f;
        public const float PlayerHeight = 1.8f;
        private const int MaxLevels = 8;

        public sealed class Report
        {
            public int Columns;
            /// <summary>Standable surfaces found (all levels).</summary>
            public int Walkable;
            /// <summary>Surfaces reachable from the start and back.</summary>
            public int TwoWay;
            /// <summary>TwoWay / Walkable.</summary>
            public float TwoWayShare;
            /// <summary>Share of each border (N, E, S, W) with two-way ground within 2 m of the edge.</summary>
            /// <summary>Share of each outline edge (in outline order) reachable two-way from just inside it.</summary>
            public float[] EdgeReach = new float[0];
            /// <summary>Two-way surface heights: 10th and 90th percentile, min and max.</summary>
            public float P10, P90, Min, Max;
            /// <summary>Share of columns with two-way ground that have two or more two-way levels (bridges, ledges, overhangs).</summary>
            public float MultiLevelShare;
            public int AnchorsReachable, AnchorsTotal;
            public readonly List<string> UnreachableAnchors = new List<string>();
            /// <summary>Two-way surface points (for picking fair eye-level screenshots).</summary>
            public readonly List<Vector3> TwoWayPoints = new List<Vector3>();
            /// <summary>Top-down map: per column 0 = nothing standable, 1 = standable but not two-way, 2 = two-way.</summary>
            public byte[] Map;
            public int MapSize;

            public float Verticality => P90 - P10;

            public override string ToString() =>
                $"two-way {TwoWayShare * 100f:0}% of {Walkable} standable cells; edges {string.Join("/", System.Linq.Enumerable.Select(EdgeReach, e => (e * 100f).ToString("0")))}%; " +
                $"height p10..p90 {P10:0.0}..{P90:0.0} m (span {Verticality:0.0}), min {Min:0.0} max {Max:0.0}; multi-level {MultiLevelShare * 100f:0}%; " +
                $"anchors reachable {AnchorsReachable}/{AnchorsTotal}" + (UnreachableAnchors.Count > 0 ? $" (missing: {string.Join(", ", UnreachableAnchors)})" : "");
        }

        public static Report Analyze(ZoneRequest zone)
        {
            Physics.SyncTransforms();
            int n = Mathf.RoundToInt(zone.Size / Cell);
            var heights = new List<float>();
            var columnOf = new List<int>();
            var firstNode = new int[n * n + 1];
            float minNormalY = Mathf.Cos(MaxSlope * Mathf.Deg2Rad);

            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int col = z * n + x;
                    firstNode[col] = heights.Count;
                    float wx = zone.Origin.x + (x + 0.5f) * Cell, wz = zone.Origin.z + (z + 0.5f) * Cell;
                    // Only this zone's own ground counts (the build box overlaps its neighbours).
                    if (!zone.Contains(wx, wz)) continue;
                    var origin = new Vector3(wx, 800f, wz);
                    for (int level = 0; level < MaxLevels; level++)
                    {
                        if (!Physics.Raycast(origin, Vector3.down, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore)) break;
                        origin = hit.point + Vector3.down * 0.05f;
                        if (hit.normal.y < minNormalY) continue;
                        var p = hit.point;
                        var bottom = p + Vector3.up * (PlayerRadius + 0.35f);
                        var top = p + Vector3.up * (PlayerHeight - PlayerRadius);
                        if (Physics.CheckCapsule(bottom, top, PlayerRadius, ~0, QueryTriggerInteraction.Ignore)) continue;
                        heights.Add(p.y);
                        columnOf.Add(col);
                    }
                }
            }
            firstNode[n * n] = heights.Count;
            int count = heights.Count;

            var report = new Report { Columns = n * n, Walkable = count, MapSize = n, Map = new byte[n * n] };
            if (count == 0) return report;

            // Neighbours: 4-connected columns, any level pair within climb/drop limits.
            IEnumerable<int> Next(int node, bool reverse)
            {
                int col = columnOf[node];
                int cx = col % n, cz = col / n;
                float h = heights[node];
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int nc = nz * n + nx;
                    for (int k = firstNode[nc]; k < firstNode[nc + 1]; k++)
                    {
                        float rise = reverse ? h - heights[k] : heights[k] - h;
                        if (rise <= MaxClimb && rise >= -MaxDrop) yield return k;
                    }
                }
            }

            bool[] Flood(int start, bool reverse)
            {
                var seen = new bool[count];
                var stack = new Stack<int>();
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    foreach (int k in Next(cur, reverse))
                    {
                        if (seen[k]) continue;
                        seen[k] = true;
                        stack.Push(k);
                    }
                }
                return seen;
            }

            // Try a few natural starts (shrines, gate, centre, toward each corner) and keep the biggest two-way area.
            var starts = new List<Vector3>();
            foreach (var a in zone.Anchors) if (a.Kind == ZoneAnchorKind.Shrine || a.Kind == ZoneAnchorKind.Gate) starts.Add(a.World);
            starts.Add(zone.Center);
            foreach (var corner in zone.Outline)
            {
                starts.Add(Vector3.Lerp(zone.Center, new Vector3(corner.x, 0f, corner.y), 0.85f));
            }

            bool[] best = null;
            int bestCount = -1;
            var tried = new HashSet<int>();
            foreach (var s in starts)
            {
                int node = LowestNodeNear(s);
                if (node < 0 || !tried.Add(node) || (best != null && best[node])) continue;
                var fwd = Flood(node, false);
                var back = Flood(node, true);
                var both = new bool[count];
                int c = 0;
                for (int i = 0; i < count; i++)
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
            if (best == null) return report;

            int LowestNodeNear(Vector3 w)
            {
                int bx = Mathf.Clamp(Mathf.FloorToInt((w.x - zone.Origin.x) / Cell), 0, n - 1);
                int bz = Mathf.Clamp(Mathf.FloorToInt((w.z - zone.Origin.z) / Cell), 0, n - 1);
                for (int r = 0; r < 12; r++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                            int x = bx + dx, z = bz + dz;
                            if (x < 0 || z < 0 || x >= n || z >= n) continue;
                            int col = z * n + x;
                            int lo = -1;
                            for (int k = firstNode[col]; k < firstNode[col + 1]; k++) if (lo < 0 || heights[k] < heights[lo]) lo = k;
                            if (lo >= 0) return lo;
                        }
                    }
                }
                return -1;
            }

            var twoWayHeights = new List<float>();
            var levelsPerColumn = new int[n * n];
            for (int i = 0; i < count; i++)
            {
                int col = columnOf[i];
                if (best[i])
                {
                    twoWayHeights.Add(heights[i]);
                    levelsPerColumn[col]++;
                    report.Map[col] = 2;
                    report.TwoWayPoints.Add(new Vector3(zone.Origin.x + (col % n + 0.5f) * Cell, heights[i], zone.Origin.z + (col / n + 0.5f) * Cell));
                }
                else if (report.Map[col] == 0) report.Map[col] = 1;
            }
            report.TwoWay = twoWayHeights.Count;
            report.TwoWayShare = twoWayHeights.Count / (float)count;
            twoWayHeights.Sort();
            report.Min = twoWayHeights[0];
            report.Max = twoWayHeights[twoWayHeights.Count - 1];
            report.P10 = twoWayHeights[(int)(twoWayHeights.Count * 0.1f)];
            report.P90 = twoWayHeights[(int)(twoWayHeights.Count * 0.9f)];
            int withGround = 0, multi = 0;
            foreach (int l in levelsPerColumn)
            {
                if (l > 0) withGround++;
                if (l > 1) multi++;
            }
            report.MultiLevelShare = withGround > 0 ? multi / (float)withGround : 0f;

            // Each outline edge: sample a point every metre, 1.5 m inside it.
            var outline = zone.Outline;
            report.EdgeReach = new float[outline.Count];
            for (int side = 0; side < outline.Count; side++)
            {
                var a = outline[side];
                var b = outline[(side + 1) % outline.Count];
                var inward = (Vector2)(Quaternion.Euler(0f, 0f, 90f) * (b - a).normalized) * 1.5f;
                int samples = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude));
                int hitCount = 0;
                for (int i = 0; i < samples; i++)
                {
                    var p = Vector2.Lerp(a, b, (i + 0.5f) / samples) + inward;
                    int x = Mathf.FloorToInt((p.x - zone.Origin.x) / Cell), z = Mathf.FloorToInt((p.y - zone.Origin.z) / Cell);
                    if (x >= 0 && z >= 0 && x < n && z < n && report.Map[z * n + x] == 2) hitCount++;
                }
                report.EdgeReach[side] = hitCount / (float)samples;
            }

            foreach (var a in zone.Anchors)
            {
                report.AnchorsTotal++;
                int ax = Mathf.Clamp(Mathf.FloorToInt((a.World.x - zone.Origin.x) / Cell), 0, n - 1);
                int az = Mathf.Clamp(Mathf.FloorToInt((a.World.z - zone.Origin.z) / Cell), 0, n - 1);
                bool ok = false;
                for (int dz = -3; dz <= 3 && !ok; dz++)
                {
                    for (int dx = -3; dx <= 3 && !ok; dx++)
                    {
                        int x = ax + dx, z = az + dz;
                        if (x >= 0 && z >= 0 && x < n && z < n && report.Map[z * n + x] == 2) ok = true;
                    }
                }
                if (ok) report.AnchorsReachable++;
                else report.UnreachableAnchors.Add(a.Name);
            }
            return report;
        }
    }
}
