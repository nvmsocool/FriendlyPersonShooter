using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>Placement helpers: evenly spread points (no clumps or grid look), and ground snapping via raycasts.</summary>
    public static class Scatter
    {
        /// <summary>
        /// Poisson-disk points (no two closer than <paramref name="minDistance"/>) over the rectangle
        /// origin .. origin + size on x/z. Keep only those where <paramref name="accept"/> (optional) says yes:
        /// use it for density maps, e.g. p => random.Value() &lt; density(p).
        /// </summary>
        public static List<Vector2> Poisson(Vector2 origin, Vector2 size, float minDistance, ref ZoneRandom random, Func<Vector2, bool> accept = null, int tries = 20)
        {
            float cell = minDistance / Mathf.Sqrt(2f);
            int gw = Mathf.CeilToInt(size.x / cell), gh = Mathf.CeilToInt(size.y / cell);
            var grid = new int[gw * gh];
            for (int i = 0; i < grid.Length; i++) grid[i] = -1;
            var points = new List<Vector2>();
            var active = new List<int>();
            float minSq = minDistance * minDistance;

            bool Fits(Vector2 p)
            {
                if (p.x < 0 || p.y < 0 || p.x >= size.x || p.y >= size.y) return false;
                int gx = (int)(p.x / cell), gy = (int)(p.y / cell);
                for (int y = Mathf.Max(gy - 2, 0); y <= Mathf.Min(gy + 2, gh - 1); y++)
                {
                    for (int x = Mathf.Max(gx - 2, 0); x <= Mathf.Min(gx + 2, gw - 1); x++)
                    {
                        int k = grid[y * gw + x];
                        if (k >= 0 && (points[k] - p).sqrMagnitude < minSq) return false;
                    }
                }
                return true;
            }

            void Add(Vector2 p)
            {
                points.Add(p);
                active.Add(points.Count - 1);
                grid[(int)(p.y / cell) * gw + (int)(p.x / cell)] = points.Count - 1;
            }

            Add(new Vector2(random.Range(0f, size.x), random.Range(0f, size.y)));
            while (active.Count > 0)
            {
                int ai = random.Range(0, active.Count);
                var center = points[active[ai]];
                bool found = false;
                for (int t = 0; t < tries; t++)
                {
                    float ang = random.Range(0f, Mathf.PI * 2f);
                    float r = random.Range(minDistance, minDistance * 2f);
                    var p = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    if (!Fits(p)) continue;
                    Add(p);
                    found = true;
                    break;
                }
                if (!found) active.RemoveAt(ai);
            }

            var result = new List<Vector2>(points.Count);
            foreach (var p in points)
            {
                var w = origin + p;
                if (accept == null || accept(w)) result.Add(w);
            }
            return result;
        }

        /// <summary>Poisson points over a whole zone (inside its outline).</summary>
        public static List<Vector2> Poisson(ZoneRequest zone, float minDistance, ref ZoneRandom random, Func<Vector2, bool> accept = null) =>
            Poisson(new Vector2(zone.Origin.x, zone.Origin.z), new Vector2(zone.Size, zone.Size), minDistance, ref random,
                p => zone.Contains(p.x, p.y) && (accept == null || accept(p)));

        /// <summary>
        /// The highest collider surface under (x, z), or false if there's none. Call Physics.SyncTransforms()
        /// after creating colliders and before snapping.
        /// </summary>
        public static bool Ground(float x, float z, out RaycastHit hit, float fromHeight = 600f)
        {
            return Physics.Raycast(new Vector3(x, fromHeight, z), Vector3.down, out hit, fromHeight + 400f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>True if (x, z) is at least <paramref name="radius"/> from every anchor of the given kinds (pass none for all kinds).</summary>
        public static bool AwayFromAnchors(ZoneRequest zone, Vector2 p, float radius, params ZoneAnchorKind[] kinds)
        {
            foreach (var a in zone.Anchors)
            {
                if (kinds.Length > 0 && Array.IndexOf(kinds, a.Kind) < 0) continue;
                if ((new Vector2(a.World.x, a.World.z) - p).sqrMagnitude < radius * radius) return false;
            }
            return true;
        }
    }
}
