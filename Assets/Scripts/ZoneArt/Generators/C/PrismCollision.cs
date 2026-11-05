using System.Collections.Generic;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// A CPU copy of every collider triangle the zone will get, binned on a 1 m grid over the zone, so the build can
    /// ask "what is under this point", cast rays and test player capsules on worker threads without touching Physics
    /// (and without ever seeing another zone's or a player's collider). Queries follow PhysX conventions: rays only hit
    /// front faces (Unity winding, normal = Cross(b - a, c - a)). Read-only once <see cref="Build"/> has run, so any
    /// number of threads may query it.
    /// </summary>
    public sealed class PrismCollision
    {
        public const int TagGround = 1;    // plantable ground: terrain, hex columns, tower tops, causeways
        public const int TagSolid = 2;     // structures players stand on but plants don't
        public const int TagCrystal = 3;
        public const int TagTrunk = 4;

        private const float Margin = 16f;
        private const int Bins = 300; // 1 m bins over -16 .. 284 local (build boxes are at most about 255 m)

        private readonly Vector3 _origin;
        private readonly List<Vector3> _a = new List<Vector3>(), _b = new List<Vector3>(), _c = new List<Vector3>();
        private readonly List<byte> _tag = new List<byte>();
        private Vector3[] _ta, _tb, _tc, _tn;
        private float[] _minY, _maxY;
        private byte[] _tags;
        private bool[] _up;
        private int[] _binStart, _binTris;

        public int TriangleCount => _a.Count;

        public PrismCollision(Vector3 zoneOrigin)
        {
            _origin = zoneOrigin;
        }

        /// <summary>Adds every triangle of a builder (world-space vertices) with a tag.</summary>
        public void Add(MeshBuilder mb, int tag)
        {
            var v = mb.Vertices;
            var t = mb.Triangles;
            for (int i = 0; i < t.Count; i += 3) Add(v[t[i]], v[t[i + 1]], v[t[i + 2]], tag);
        }

        public void Add(Vector3[] verts, int[] tris, int tag)
        {
            for (int i = 0; i < tris.Length; i += 3) Add(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]], tag);
        }

        public void Add(Vector3 a, Vector3 b, Vector3 c, int tag)
        {
            _a.Add(a);
            _b.Add(b);
            _c.Add(c);
            _tag.Add((byte)tag);
        }

        /// <summary>Freezes the triangles into the query grid (call again after adding more).</summary>
        public void Build()
        {
            int n = _a.Count;
            _ta = _a.ToArray();
            _tb = _b.ToArray();
            _tc = _c.ToArray();
            _tags = _tag.ToArray();
            _tn = new Vector3[n];
            _minY = new float[n];
            _maxY = new float[n];
            _up = new bool[n];
            var x0 = new int[n];
            var x1 = new int[n];
            var z0 = new int[n];
            var z1 = new int[n];
            var counts = new int[Bins * Bins + 1];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = _ta[i], b = _tb[i], c = _tc[i];
                var nrm = Vector3.Cross(b - a, c - a);
                _tn[i] = nrm;
                _up[i] = nrm.y > 1e-7f;
                _minY[i] = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                _maxY[i] = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                x0[i] = Bin(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - _origin.x);
                x1[i] = Bin(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - _origin.x);
                z0[i] = Bin(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - _origin.z);
                z1[i] = Bin(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - _origin.z);
                for (int z = z0[i]; z <= z1[i]; z++)
                for (int x = x0[i]; x <= x1[i]; x++)
                    counts[z * Bins + x]++;
            }
            _binStart = new int[Bins * Bins + 1];
            int sum = 0;
            for (int k = 0; k < Bins * Bins; k++)
            {
                _binStart[k] = sum;
                sum += counts[k];
            }
            _binStart[Bins * Bins] = sum;
            _binTris = new int[sum];
            var fill = new int[Bins * Bins];
            for (int i = 0; i < n; i++)
            {
                for (int z = z0[i]; z <= z1[i]; z++)
                for (int x = x0[i]; x <= x1[i]; x++)
                {
                    int k = z * Bins + x;
                    _binTris[_binStart[k] + fill[k]++] = i;
                }
            }
        }

        private static int Bin(float local) => Mathf.Clamp(Mathf.FloorToInt(local + Margin), 0, Bins - 1);

        /// <summary>Height of an up-facing triangle at (wx, wz), or false if the vertical line misses it.</summary>
        private bool HeightOn(int i, float wx, float wz, out float y)
        {
            y = 0f;
            Vector3 a = _ta[i], b = _tb[i], c = _tc[i];
            // Edge functions in xz, inclusive with a little tolerance so shared edges never leave cracks.
            float e0 = (b.x - a.x) * (wz - a.z) - (b.z - a.z) * (wx - a.x);
            float e1 = (c.x - b.x) * (wz - b.z) - (c.z - b.z) * (wx - b.x);
            float e2 = (a.x - c.x) * (wz - c.z) - (a.z - c.z) * (wx - c.x);
            const float tol = 1e-5f;
            bool inside = (e0 >= -tol && e1 >= -tol && e2 >= -tol) || (e0 <= tol && e1 <= tol && e2 <= tol);
            if (!inside) return false;
            var nrm = _tn[i];
            y = a.y - (nrm.x * (wx - a.x) + nrm.z * (wz - a.z)) / nrm.y;
            return true;
        }

        /// <summary>The highest front-facing surface under (wx, wz) below <paramref name="fromY"/> (like a downward raycast).</summary>
        public bool Down(float wx, float wz, float fromY, out float y, out Vector3 normal, out int tag)
        {
            y = float.MinValue;
            normal = Vector3.up;
            tag = 0;
            int bx = Bin(wx - _origin.x), bz = Bin(wz - _origin.z);
            int k = bz * Bins + bx;
            int best = -1;
            for (int j = _binStart[k]; j < _binStart[k + 1]; j++)
            {
                int i = _binTris[j];
                if (!_up[i] || _minY[i] > fromY || _maxY[i] <= y) continue;
                if (!HeightOn(i, wx, wz, out float h) || h > fromY || h <= y) continue;
                y = h;
                best = i;
            }
            if (best < 0) return false;
            normal = _tn[best].normalized;
            tag = _tags[best];
            return true;
        }

        /// <summary>
        /// Every up-facing surface on the vertical line through (wx, wz), highest first, the way repeated downward
        /// raycasts see them (a surface within 0.05 m below the previous hit is skipped). At most <paramref name="max"/>.
        /// </summary>
        public int Column(float wx, float wz, float[] ys, Vector3[] normals, int max, List<(float y, int i)> buf)
        {
            int bx = Bin(wx - _origin.x), bz = Bin(wz - _origin.z);
            int k = bz * Bins + bx;
            int count = 0;
            buf.Clear();
            for (int j = _binStart[k]; j < _binStart[k + 1]; j++)
            {
                int i = _binTris[j];
                if (!_up[i]) continue;
                if (!HeightOn(i, wx, wz, out float h)) continue;
                buf.Add((h, i));
            }
            buf.Sort((p, q) => q.y.CompareTo(p.y) != 0 ? q.y.CompareTo(p.y) : p.i.CompareTo(q.i));
            float last = float.MaxValue;
            foreach (var (h, i) in buf)
            {
                if (h > last - 0.05f) continue;
                ys[count] = h;
                normals[count] = _tn[i].normalized;
                count++;
                last = h;
                if (count >= max) break;
            }
            return count;
        }

        /// <summary>True if a vertical capsule (sphere centres <paramref name="bottom"/>, <paramref name="top"/>) touches any triangle.</summary>
        public bool CapsuleHits(Vector3 bottom, Vector3 top, float radius)
        {
            float lo = bottom.y - radius, hi = top.y + radius;
            int x0 = Bin(bottom.x - radius - _origin.x), x1 = Bin(bottom.x + radius - _origin.x);
            int z0 = Bin(bottom.z - radius - _origin.z), z1 = Bin(bottom.z + radius - _origin.z);
            float r2 = radius * radius;
            for (int bz = z0; bz <= z1; bz++)
            {
                for (int bx = x0; bx <= x1; bx++)
                {
                    int k = bz * Bins + bx;
                    for (int j = _binStart[k]; j < _binStart[k + 1]; j++)
                    {
                        int i = _binTris[j];
                        if (_minY[i] > hi || _maxY[i] < lo) continue;
                        if (SegmentTriangleDistSq(bottom, top, _ta[i], _tb[i], _tc[i], _tn[i]) <= r2) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>First front-facing hit along a ray (like Physics.Raycast). Distance along the normalised direction.</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDistance, out float distance)
        {
            distance = maxDistance;
            dir.Normalize();
            bool hit = false;
            // Walk the bins the ray's xz projection crosses (2D DDA).
            float lx = origin.x - _origin.x + Margin, lz = origin.z - _origin.z + Margin;
            int bx = Mathf.FloorToInt(lx), bz = Mathf.FloorToInt(lz);
            float hx = dir.x, hz = dir.z;
            int stepX = hx > 0 ? 1 : -1, stepZ = hz > 0 ? 1 : -1;
            float tMaxX = Mathf.Abs(hx) < 1e-9f ? float.MaxValue : ((hx > 0 ? bx + 1 - lx : lx - bx) / Mathf.Abs(hx));
            float tMaxZ = Mathf.Abs(hz) < 1e-9f ? float.MaxValue : ((hz > 0 ? bz + 1 - lz : lz - bz) / Mathf.Abs(hz));
            float tDX = Mathf.Abs(hx) < 1e-9f ? float.MaxValue : 1f / Mathf.Abs(hx);
            float tDZ = Mathf.Abs(hz) < 1e-9f ? float.MaxValue : 1f / Mathf.Abs(hz);
            float tEnter = 0f;
            for (int guard = 0; guard < 4 * Bins; guard++)
            {
                if (tEnter > distance) break;
                if (bx >= 0 && bz >= 0 && bx < Bins && bz < Bins)
                {
                    int k = bz * Bins + bx;
                    for (int j = _binStart[k]; j < _binStart[k + 1]; j++)
                    {
                        int i = _binTris[j];
                        if (RayTriangle(origin, dir, i, out float t) && t < distance)
                        {
                            distance = t;
                            hit = true;
                        }
                    }
                }
                else if ((bx < 0 && stepX < 0) || (bz < 0 && stepZ < 0) || (bx >= Bins && stepX > 0) || (bz >= Bins && stepZ > 0)) break;
                if (tMaxX < tMaxZ)
                {
                    tEnter = tMaxX;
                    tMaxX += tDX;
                    bx += stepX;
                }
                else
                {
                    if (tMaxZ == float.MaxValue) break;
                    tEnter = tMaxZ;
                    tMaxZ += tDZ;
                    bz += stepZ;
                }
            }
            return hit;
        }

        public bool Linecast(Vector3 from, Vector3 to, out Vector3 point)
        {
            var d = to - from;
            float len = d.magnitude;
            point = to;
            if (len < 1e-6f || !Raycast(from, d / len, len, out float t)) return false;
            point = from + d / len * t;
            return true;
        }

        private bool RayTriangle(Vector3 o, Vector3 d, int i, out float t)
        {
            t = 0f;
            var n = _tn[i];
            if (Vector3.Dot(n, d) >= 0f) return false; // back face or parallel
            Vector3 a = _ta[i];
            Vector3 e1 = _tb[i] - a, e2 = _tc[i] - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t >= 0f;
        }

        // ------------------------------------------------------------------ segment / triangle distance (Ericson, Real-Time Collision Detection)

        private static float SegmentTriangleDistSq(Vector3 p0, Vector3 p1, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            // Segment crossing the triangle's plane inside the triangle: distance 0.
            float d0 = Vector3.Dot(p0 - a, n), d1 = Vector3.Dot(p1 - a, n);
            if ((d0 <= 0f && d1 >= 0f) || (d0 >= 0f && d1 <= 0f))
            {
                float den = d0 - d1;
                if (Mathf.Abs(den) > 1e-12f)
                {
                    var x = p0 + (p1 - p0) * (d0 / den);
                    if (Vector3.SqrMagnitude(ClosestPtPointTriangle(x, a, b, c) - x) < 1e-10f) return 0f;
                }
            }
            float best = Vector3.SqrMagnitude(ClosestPtPointTriangle(p0, a, b, c) - p0);
            best = Mathf.Min(best, Vector3.SqrMagnitude(ClosestPtPointTriangle(p1, a, b, c) - p1));
            best = Mathf.Min(best, SegSegDistSq(p0, p1, a, b));
            best = Mathf.Min(best, SegSegDistSq(p0, p1, b, c));
            best = Mathf.Min(best, SegSegDistSq(p0, p1, c, a));
            return best;
        }

        private static Vector3 ClosestPtPointTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            float v = vb * denom, w = vc * denom;
            return a + ab * v + ac * w;
        }

        private static float SegSegDistSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            const float eps = 1e-12f;
            if (a <= eps && e <= eps) return Vector3.SqrMagnitude(p1 - p2);
            if (a <= eps)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }
            return Vector3.SqrMagnitude(p1 + d1 * s - (p2 + d2 * t));
        }
    }
}
