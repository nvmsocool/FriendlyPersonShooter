using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// The zone's ground: a 1 m height grid shaped per tile kind (terraced plateaus, canyons, craters,
    /// lakes, rivers), basalt hex-column fields that step up in walkable increments, and a connectivity
    /// pass that carves ramps from every stranded shelf down (or up) to the main ground.
    /// Grid coordinates are zone-local metres.
    /// </summary>
    public sealed class PrismTerrain
    {
        public const int N = 251;
        public const int C = 250;
        public const float Climb = 0.75f;
        public const float MaxGrad = 0.95f;

        public readonly ZoneRequest Zone;
        public readonly float[] H = new float[N * N];
        public readonly float[] Ramp = new float[N * N];
        public readonly float[] NoHex = new float[N * N];
        public readonly float[] Pad = new float[N * N];
        public readonly float[] S = new float[C * C];
        public readonly int[] CellHex = new int[C * C];
        public readonly int[] Comp = new int[C * C];
        public readonly bool[] Steep = new bool[C * C];
        public int MainComp;
        public float WaterLevel = -999f;
        public bool HasWater;
        public HexGrid Hex;
        public int RampsCarved;
        public float StrandedShare;
        public readonly List<(Vector2 a, Vector2 b)> RampSegments = new List<(Vector2, Vector2)>();
        public readonly List<(Vector2 c, float r)> Pads = new List<(Vector2, float)>();

        // Protected discs (shrines, gate) that ramps must not cut through.
        private readonly List<(Vector2 c, float r)> _protect = new List<(Vector2, float)>();

        public PrismTerrain(ZoneRequest zone)
        {
            Zone = zone;
        }

        public float this[int x, int z] => H[Mathf.Clamp(z, 0, N - 1) * N + Mathf.Clamp(x, 0, N - 1)];

        public float HeightAt(float lx, float lz) => Bilinear(H, lx, lz);
        public float RampAt(float lx, float lz) => Bilinear(Ramp, lx, lz);
        public float PadAt(float lx, float lz) => Bilinear(Pad, lx, lz);

        private static float Bilinear(float[] a, float lx, float lz)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(lx), 0, N - 2), z = Mathf.Clamp(Mathf.FloorToInt(lz), 0, N - 2);
            float tx = Mathf.Clamp01(lx - x), tz = Mathf.Clamp01(lz - z);
            float h0 = Mathf.Lerp(a[z * N + x], a[z * N + x + 1], tx);
            float h1 = Mathf.Lerp(a[(z + 1) * N + x], a[(z + 1) * N + x + 1], tx);
            return Mathf.Lerp(h0, h1, tz);
        }

        /// <summary>Walk surface (hex top or ground) at a local position.</summary>
        public float SurfaceAt(float lx, float lz)
        {
            if (Hex != null && Hex.Locate(lx, lz, out int idx) && Hex.On[idx]) return Hex.Top[idx];
            return HeightAt(lx, lz);
        }

        public void Protect(Vector2 local, float radius) => _protect.Add((local, radius));

        // ------------------------------------------------------------------ shape

        public sealed class Shape
        {
            public float A, Step, Soft, Freq, Warp, Roll, Fall;
            public float HexFrac, HexR, LiftAmp, LiftStep, PillarChance;
            public Vector2 Feature;   // crater / lake centre (local)
            public float FeatureR;
            public float RiverAngle, RiverPhase, RiverWander;
        }

        public static Shape ShapeFor(ZoneRequest zone)
        {
            var r = ZoneRandom.For(zone.Seed, "shape");
            var s = new Shape
            {
                Freq = 1f / r.Range(85f, 135f), Warp = r.Range(25f, 60f), Roll = r.Range(1.2f, 2.6f), Fall = r.Range(28f, 42f),
                HexR = r.Range(1.15f, 1.9f), LiftStep = r.Range(0.6f, 0.82f), PillarChance = r.Range(0.006f, 0.02f),
                Feature = new Vector2(r.Range(95f, 155f), r.Range(95f, 155f)), FeatureR = r.Range(48f, 72f),
                RiverAngle = r.Range(0f, Mathf.PI), RiverPhase = r.Range(0f, 10f), RiverWander = r.Range(14f, 30f),
            };
            switch (zone.Tile)
            {
                case TileKind.Meadow: s.A = r.Range(12f, 20f); s.Step = r.Range(3.2f, 4.5f); s.Soft = r.Range(0.3f, 0.45f); s.HexFrac = r.Range(0.18f, 0.3f); s.LiftAmp = r.Range(4f, 8f); break;
                case TileKind.Forest: s.A = r.Range(14f, 24f); s.Step = r.Range(4f, 6f); s.Soft = r.Range(0.25f, 0.4f); s.HexFrac = r.Range(0.12f, 0.22f); s.LiftAmp = r.Range(4f, 8f); break;
                case TileKind.Ruins: s.A = r.Range(12f, 22f); s.Step = r.Range(3.5f, 5f); s.Soft = r.Range(0.1f, 0.2f); s.HexFrac = r.Range(0.3f, 0.42f); s.LiftAmp = r.Range(6f, 11f); break;
                case TileKind.Tower: s.A = r.Range(22f, 34f); s.Step = r.Range(6f, 9f); s.Soft = r.Range(0.12f, 0.25f); s.HexFrac = r.Range(0.3f, 0.45f); s.LiftAmp = r.Range(8f, 14f); break;
                case TileKind.Canyon: s.A = r.Range(20f, 30f); s.Step = r.Range(5f, 7.5f); s.Soft = r.Range(0.1f, 0.2f); s.HexFrac = r.Range(0.3f, 0.45f); s.LiftAmp = r.Range(6f, 12f); break;
                case TileKind.Lake: s.A = r.Range(12f, 20f); s.Step = r.Range(3.5f, 5f); s.Soft = r.Range(0.2f, 0.35f); s.HexFrac = r.Range(0.25f, 0.38f); s.LiftAmp = r.Range(4f, 9f); break;
                case TileKind.River: s.A = r.Range(14f, 22f); s.Step = r.Range(4f, 5.5f); s.Soft = r.Range(0.2f, 0.35f); s.HexFrac = r.Range(0.22f, 0.35f); s.LiftAmp = r.Range(4f, 9f); break;
                default: s.A = r.Range(10f, 18f); s.Step = r.Range(4f, 6f); s.Soft = r.Range(0.15f, 0.3f); s.HexFrac = r.Range(0.3f, 0.42f); s.LiftAmp = r.Range(6f, 12f); s.FeatureR = r.Range(62f, 82f); break;
            }
            s.A *= 1.35f;
            s.Step *= 1.15f;
            return s;
        }

        private Shape _shape;
        private Vector2 _o1, _o2, _o3;

        public void BuildHeights(Shape s)
        {
            var z = Zone;
            _shape = s;
            _o1 = ZoneNoise.Offset(z.Seed, 11);
            _o2 = ZoneNoise.Offset(z.Seed, 12);
            _o3 = ZoneNoise.Offset(z.Seed, 13);
            if (z.Tile == TileKind.Lake || z.Tile == TileKind.River)
            {
                float minB = float.MaxValue;
                for (int i = 0; i <= 50; i++)
                {
                    float t = i * 5f;
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z.LayerSeed, z.Origin.x + t, z.Origin.z));
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z.LayerSeed, z.Origin.x + t, z.Origin.z + 250f));
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z.LayerSeed, z.Origin.x, z.Origin.z + t));
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z.LayerSeed, z.Origin.x + 250f, z.Origin.z + t));
                }
                WaterLevel = minB - 0.4f;
                HasWater = true;
            }
            for (int iz = 0; iz < N; iz++)
            {
                for (int ix = 0; ix < N; ix++)
                {
                    float lx = ix, lz = iz;
                    float wx = z.Origin.x + lx, wz = z.Origin.z + lz;
                    float e = Mathf.Min(Mathf.Min(lx, 250f - lx), Mathf.Min(lz, 250f - lz));
                    float inner = Smooth(14f, 14f + s.Fall, e);
                    float border = ZoneEdges.BorderHeight(z.LayerSeed, wx, wz);
                    float h = border + Elevation(lx, lz) * inner;
                    h = ZoneEdges.Apply(z, wx, wz, h);
                    if (HasWater) h = Mathf.Max(h, WaterLevel - 1.1f);
                    H[iz * N + ix] = h;
                    // Hex fields stay out of the border strip.
                    NoHex[iz * N + ix] = Smooth(20f, 15f, e);
                }
            }
        }

        /// <summary>The zone's landform above the seam height at a local position (works outside the zone too).</summary>
        public float Elevation(float lx, float lz)
        {
            var s = _shape;
            var z = Zone;
            float wx = z.Origin.x + lx, wz = z.Origin.z + lz;
            float roll = ZoneNoise.Fbm(wx + _o1.x, wz + _o1.y, 1f / 55f, 4) * s.Roll;
            float n = ZoneNoise.Warped(wx + _o2.x, wz + _o2.y, s.Freq, s.Warp, 4);
            float p = Mathf.Clamp01(0.5f + n * 1.25f);
            float E = s.A * p;
            var lp = new Vector2(lx, lz);
            switch (z.Tile)
            {
                case TileKind.Canyon:
                {
                    float c = Mathf.Abs(ZoneNoise.Fbm(wx + _o3.x, wz + _o3.y, 1f / 170f, 3));
                    float canyon = 1f - Smooth(0.035f, 0.13f, c);
                    E = s.A * (0.6f + 0.4f * p) * (1f - canyon);
                    break;
                }
                case TileKind.Crater:
                {
                    float warp = ZoneNoise.Fbm(wx + _o3.x, wz + _o3.y, 1f / 60f, 3) * 0.12f;
                    float r = (lp - s.Feature).magnitude / s.FeatureR + warp;
                    float rim = Mathf.Exp(-Sq((r - 1f) / 0.2f)) * (s.A * 0.9f + 6f);
                    float bowl = r < 1f ? -(1f - r * r) * 7f : 0f;
                    E = E * 0.55f * Smooth(0.9f, 1.4f, r) + rim + bowl;
                    break;
                }
                case TileKind.Lake:
                {
                    float warp = ZoneNoise.Fbm(wx + _o3.x, wz + _o3.y, 1f / 50f, 3) * 0.25f;
                    float r = (lp - s.Feature).magnitude / s.FeatureR + warp;
                    float lake = Smooth(1.05f, 0.8f, r);
                    E = Mathf.Lerp(E * Smooth(0.9f, 1.6f, r), -12f, lake);
                    break;
                }
                case TileKind.River:
                {
                    var rdir = new Vector2(Mathf.Cos(s.RiverAngle), Mathf.Sin(s.RiverAngle));
                    var rperp = new Vector2(-rdir.y, rdir.x);
                    var q = lp - new Vector2(125f, 125f);
                    float along = Vector2.Dot(q, rdir);
                    float across = Vector2.Dot(q, rperp);
                    float path = Mathf.Sin(along / 38f + s.RiverPhase) * s.RiverWander + Mathf.Sin(along / 17f + s.RiverPhase * 2.3f) * 5f;
                    float d = Mathf.Abs(across - path);
                    float ch = Smooth(16f, 7f, d);
                    E = Mathf.Lerp(E * Smooth(10f, 40f, d), -10f, ch);
                    break;
                }
            }
            E = ZoneNoise.Terrace(E, s.Step, s.Soft);
            return roll + E;
        }


        /// <summary>Flattens a disc to <paramref name="height"/> (full within r, eased out to r + fall).</summary>
        public void Flatten(Vector2 c, float r, float fall, float height, bool noHex, float padValue = 1f)
        {
            if (padValue >= 1f) Pads.Add((c, r));
            int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - r - fall)), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(c.x + r + fall));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(c.y - r - fall)), z1 = Mathf.Min(N - 1, Mathf.CeilToInt(c.y + r + fall));
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    float d = new Vector2(ix - c.x, iz - c.y).magnitude;
                    float w = Smooth(r + fall, r, d);
                    if (w <= 0f) continue;
                    int k = iz * N + ix;
                    H[k] = Mathf.Lerp(H[k], height, w);
                    if (noHex && d < r + 2f) NoHex[k] = 1f;
                    Pad[k] = Mathf.Max(Pad[k], Smooth(r + 1f, r - 1f, d) * padValue);
                }
            }
        }

        // ------------------------------------------------------------------ hexes and surface

        public void BuildHexes(Shape s, float hexThreshold)
        {
            if (Hex == null) Hex = new HexGrid(s.HexR);
            var g = Hex;
            var z = Zone;
            var oMask = ZoneNoise.Offset(z.Seed, 21);
            var oLift = ZoneNoise.Offset(z.Seed, 22);
            var rng = ZoneRandom.For(z.Seed, "hexjitter");
            for (int i = 0; i < g.Count; i++)
            {
                var c = g.Center(i);
                float hmax = float.MinValue, hmin = float.MaxValue, nohex = 0f;
                for (int k = 0; k < 7; k++)
                {
                    var p = k == 0 ? c : c + g.Corner(k - 1);
                    float h = HeightAt(p.x, p.y);
                    hmax = Mathf.Max(hmax, h);
                    hmin = Mathf.Min(hmin, h);
                    nohex = Mathf.Max(nohex, Bilinear(NoHex, p.x, p.y));
                }
                g.Hmax[i] = hmax;
                g.Hmin[i] = hmin;
                float wx = z.Origin.x + c.x, wz = z.Origin.z + c.y;
                float m = ZoneNoise.Fbm(wx + oMask.x, wz + oMask.y, 1f / 65f, 3);
                bool inside = c.x > 2f && c.y > 2f && c.x < 248f && c.y < 248f;
                g.On[i] = inside && m > hexThreshold && nohex < 0.5f;
                float jitter = rng.Range(0, 3) * 0.2f;
                g.Pillar[i] = false;
                if (!g.On[i])
                {
                    g.Top[i] = hmax;
                    continue;
                }
                float liftN = ZoneNoise.Fbm(wx + oLift.x, wz + oLift.y, 1f / 48f, 3);
                float lift = Mathf.Max(0f, (liftN + 0.15f) * s.LiftAmp * 1.6f);
                lift = Mathf.Floor(lift / s.LiftStep) * s.LiftStep;
                // Deeper inside the mask, columns rise more.
                float depth = Mathf.Clamp01((m - hexThreshold) * 6f);
                g.Top[i] = hmax + lift * depth + jitter;
            }

            // Relax: every column at most one step above its lowest neighbour (stairs everywhere).
            float step = s.LiftStep;
            for (int sweep = 0; sweep < 120; sweep++)
            {
                bool changed = false;
                bool fwd = (sweep & 1) == 0;
                for (int n = 0; n < g.Count; n++)
                {
                    int i = fwd ? n : g.Count - 1 - n;
                    if (!g.On[i]) continue;
                    float lo = float.MaxValue;
                    for (int d = 0; d < 6; d++)
                    {
                        int j = g.Neighbor(i, d);
                        float t = j < 0 ? g.Hmax[i] : g.Top[j];
                        if (t < lo) lo = t;
                    }
                    float cand = Mathf.Max(g.Hmax[i], Mathf.Min(g.Top[i], lo + step));
                    if (cand < g.Top[i] - 1e-4f)
                    {
                        g.Top[i] = cand;
                        changed = true;
                    }
                }
                if (!changed) break;
            }

            // A few tall basalt organ pipes for drama.
            var prng = ZoneRandom.For(z.Seed, "pillars");
            var oP = ZoneNoise.Offset(z.Seed, 23);
            for (int i = 0; i < g.Count; i++)
            {
                if (!g.On[i]) continue;
                var c = g.Center(i);
                float cl = ZoneNoise.Fbm(z.Origin.x + c.x + oP.x, z.Origin.z + c.y + oP.y, 1f / 30f, 2);
                if (cl > 0.25f && prng.Chance(s.PillarChance * 6f))
                {
                    g.Pillar[i] = true;
                    g.Top[i] += Mathf.Round(prng.Range(2f, 7f) * 2f) / 2f;
                }
            }
        }

        public void ComputeSurface()
        {
            for (int iz = 0; iz < C; iz++)
            {
                for (int ix = 0; ix < C; ix++)
                {
                    float lx = ix + 0.5f, lz = iz + 0.5f;
                    int k = iz * C + ix;
                    if (Hex != null && Hex.Locate(lx, lz, out int idx) && Hex.On[idx])
                    {
                        S[k] = Hex.Top[idx];
                        CellHex[k] = idx;
                    }
                    else
                    {
                        S[k] = HeightAt(lx, lz);
                        CellHex[k] = -1;
                    }
                    if (CellHex[k] >= 0) Steep[k] = false;
                    else
                    {
                        float h00 = H[iz * N + ix], h10 = H[iz * N + ix + 1], h01 = H[(iz + 1) * N + ix], h11 = H[(iz + 1) * N + ix + 1];
                        float g = Mathf.Max(Mathf.Max(Mathf.Abs(h10 - h00), Mathf.Abs(h11 - h01)), Mathf.Max(Mathf.Abs(h01 - h00), Mathf.Abs(h11 - h10)));
                        g = Mathf.Max(g, Mathf.Max(Mathf.Abs(h11 - h00), Mathf.Abs(h10 - h01)) * 0.7071f);
                        Steep[k] = g > MaxGrad;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ connectivity

        /// <summary>Debug: writes the component map (main green, others orange, hex cells tinted) to a PNG.</summary>
        public void DebugPng(string path)
        {
            var tex = new Texture2D(C, C, TextureFormat.RGB24, false);
            var px = new Color32[C * C];
            for (int k = 0; k < C * C; k++)
            {
                bool hex = CellHex[k] >= 0;
                px[k] = Comp[k] == MainComp ? (hex ? new Color32(40, 140, 90, 255) : new Color32(60, 200, 90, 255)) : (hex ? new Color32(160, 90, 30, 255) : new Color32(230, 170, 40, 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private int Label(out int[] sizes, out int count)
        {
            for (int i = 0; i < Comp.Length; i++) Comp[i] = -1;
            var sizeList = new List<int>();
            var border = new List<int>();
            var stack = new Stack<int>();
            int id = 0;
            for (int start = 0; start < C * C; start++)
            {
                if (Comp[start] >= 0) continue;
                Comp[start] = id;
                stack.Push(start);
                int size = 0, nb = 0;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    size++;
                    int x = k % C, zz = k / C;
                    if (x < 3 || zz < 3 || x >= C - 3 || zz >= C - 3) nb++;
                    float h = S[k];
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = zz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= C || nz >= C) continue;
                        int j = nz * C + nx;
                        if (Comp[j] >= 0) continue;
                        if (Steep[k] || Steep[j]) continue;
                        if (Mathf.Abs(S[j] - h) > Climb) continue;
                        Comp[j] = id;
                        stack.Push(j);
                    }
                }
                sizeList.Add(size);
                border.Add(nb);
                id++;
            }
            int main = 0;
            for (int i = 1; i < id; i++) if (border[i] > border[main] || (border[i] == border[main] && sizeList[i] > sizeList[main])) main = i;
            sizes = sizeList.ToArray();
            count = id;
            MainComp = main;
            return main;
        }

        public void Connect(Shape s, float hexThreshold, int iterations = 8, int minSize = 10)
        {
            for (int it = 0; it < iterations; it++)
            {
                BuildHexes(s, hexThreshold);
                ComputeSurface();
                int main = Label(out var sizes, out int count);
                var order = new List<int>();
                for (int i = 0; i < count; i++) if (i != main && sizes[i] >= minSize) order.Add(i);
                if (order.Count == 0) break;
                order.Sort((a, b) => sizes[b].CompareTo(sizes[a]));
                // Boundary cells per component.
                var bounds = new Dictionary<int, List<int>>();
                foreach (int c in order) bounds[c] = new List<int>();
                for (int k = 0; k < C * C; k++)
                {
                    int c = Comp[k];
                    if (!bounds.TryGetValue(c, out var list)) continue;
                    int x = k % C, zz = k / C;
                    bool edge = false;
                    for (int d = 0; d < 4 && !edge; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = zz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= C || nz >= C) continue;
                        if (Comp[nz * C + nx] != c) edge = true;
                    }
                    if (edge) list.Add(k);
                }
                var merged = new HashSet<int>();
                int carved = 0;
                foreach (int c in order)
                {
                    if (carved > 150) break;
                    if (merged.Contains(c)) continue;
                    if (FindRamp(bounds[c], c, main, merged, out int from, out int to))
                    {
                        Carve(from, to);
                        carved++;
                        RampsCarved++;
                    }
                }
            }
            BuildHexes(s, hexThreshold);
            ComputeSurface();
            Label(out var finalSizes, out _);
            int stranded = 0, total = C * C;
            for (int k = 0; k < C * C; k++) if (Comp[k] != MainComp) stranded++;
            StrandedShare = stranded / (float)total;
        }

        private bool FindRamp(List<int> boundary, int comp, int main, HashSet<int> merged, out int from, out int to)
        {
            from = to = -1;
            if (boundary.Count == 0) return false;
            const float g = 0.5f;
            int stride = Mathf.Max(1, boundary.Count / 40);
            float best = float.MaxValue;
            for (int b = 0; b < boundary.Count; b += stride)
            {
                int s = boundary[b];
                int sx = s % C, sz = s / C;
                float hs = S[s];
                for (int d = 0; d < 32; d++)
                {
                    float ang = d / 32f * Mathf.PI * 2f;
                    float dx = Mathf.Cos(ang), dz = Mathf.Sin(ang);
                    for (int t = 2; t < 170; t++)
                    {
                        if (t >= best) break;
                        int qx = Mathf.RoundToInt(sx + dx * t), qz = Mathf.RoundToInt(sz + dz * t);
                        if (qx < 1 || qz < 1 || qx >= C - 1 || qz >= C - 1) break;
                        if (t > 3 && Protected(qx + 0.5f, qz + 0.5f)) break;
                        int q = qz * C + qx;
                        int cq = Comp[q];
                        if (cq == main || merged.Contains(cq))
                        {
                            if (Mathf.Abs(S[q] - hs) <= g * t)
                            {
                                best = t;
                                from = s;
                                to = q;
                                break;
                            }
                        }
                    }
                }
            }
            if (from < 0) return false;
            merged.Add(comp);
            return true;
        }

        private bool Protected(float x, float z)
        {
            foreach (var (c, r) in _protect)
            {
                if ((new Vector2(x, z) - c).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        private void Carve(int from, int to)
        {
            var a = new Vector2(from % C + 0.5f, from / C + 0.5f);
            var b = new Vector2(to % C + 0.5f, to / C + 0.5f);
            float ha = S[from], hb = S[to];
            var dir = (b - a).normalized;
            float len = (b - a).magnitude;
            const float halfW = 2.1f, fall = 2.6f;
            a -= dir * 1.5f;
            b += dir * 1.5f;
            float total = len + 3f;
            RampSegments.Add((a, b));
            float reach = halfW + fall + 1f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - reach)), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + reach));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - reach)), z1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + reach));
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    var p = new Vector2(ix, iz);
                    float u = Mathf.Clamp(Vector2.Dot(p - a, dir), 0f, total);
                    float dist = (p - (a + dir * u)).magnitude;
                    float w = Smooth(halfW + fall, halfW, dist);
                    if (w <= 0f) continue;
                    float t = Mathf.Clamp01((u - 1.5f) / Mathf.Max(len, 0.01f));
                    float rh = Mathf.Lerp(ha, hb, t);
                    int k = iz * N + ix;
                    H[k] = Mathf.Lerp(H[k], rh, w);
                    Ramp[k] = Mathf.Max(Ramp[k], w);
                    NoHex[k] = 1f;
                }
            }
        }

        // ------------------------------------------------------------------ mesh

        public void BuildMesh(Transform parent, Material material, System.Func<Vector3, Vector3, Color> colorAt, List<Collider> plantable)
        {
            var root = new GameObject("Terrain");
            root.transform.SetParent(parent, false);
            const int chunk = 50;
            var mb = new MeshBuilder();
            var o = Zone.Origin;
            for (int cz = 0; cz < C; cz += chunk)
            {
                for (int cx = 0; cx < C; cx += chunk)
                {
                    mb.Clear();
                    for (int z = cz; z < cz + chunk; z++)
                    {
                        for (int x = cx; x < cx + chunk; x++)
                        {
                            var a = new Vector3(o.x + x, this[x, z], o.z + z);
                            var b = new Vector3(o.x + x, this[x, z + 1], o.z + z + 1);
                            var c = new Vector3(o.x + x + 1, this[x + 1, z + 1], o.z + z + 1);
                            var d = new Vector3(o.x + x + 1, this[x + 1, z], o.z + z);
                            if (((x + z) & 1) == 0)
                            {
                                Tri(mb, a, b, c, colorAt);
                                Tri(mb, a, c, d, colorAt);
                            }
                            else
                            {
                                Tri(mb, a, b, d, colorAt);
                                Tri(mb, b, c, d, colorAt);
                            }
                        }
                    }
                    var go = mb.ToGameObject($"Terrain {cx / chunk},{cz / chunk}", root.transform, material, collider: true);
                    plantable.Add(go.GetComponent<Collider>());
                }
            }
        }

        private static void Tri(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, System.Func<Vector3, Vector3, Color> colorAt)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            mb.FlatTriangle(a, b, c, colorAt((a + b + c) / 3f, n));
        }

        public static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public static float Sq(float x) => x * x;
    }

    /// <summary>A pointy-top hex lattice over the zone (odd rows shifted right), zone-local metres.</summary>
    public sealed class HexGrid
    {
        public readonly float R;
        public readonly int Cols, Rows, Count;
        public readonly float[] Hmax, Hmin, Top;
        public readonly bool[] On, Pillar;
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);

        public HexGrid(float radius)
        {
            R = radius;
            Cols = Mathf.CeilToInt(250f / (R * Sqrt3)) + 2;
            Rows = Mathf.CeilToInt(250f / (R * 1.5f)) + 2;
            Count = Cols * Rows;
            Hmax = new float[Count];
            Hmin = new float[Count];
            Top = new float[Count];
            On = new bool[Count];
            Pillar = new bool[Count];
        }

        public Vector2 Center(int i)
        {
            int c = i % Cols, r = i / Cols;
            return new Vector2(R * Sqrt3 * (c + (r & 1) * 0.5f), 1.5f * R * r);
        }

        /// <summary>Corner k (0..5) offset from the centre.</summary>
        public Vector2 Corner(int k, float scale = 1f)
        {
            float a = (30f + 60f * k) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (R * scale);
        }

        private static readonly int[,] EvenN = { { 1, 0 }, { 0, -1 }, { -1, -1 }, { -1, 0 }, { -1, 1 }, { 0, 1 } };
        private static readonly int[,] OddN = { { 1, 0 }, { 1, -1 }, { 0, -1 }, { -1, 0 }, { 0, 1 }, { 1, 1 } };

        public int Neighbor(int i, int d)
        {
            int c = i % Cols, r = i / Cols;
            var t = (r & 1) == 0 ? EvenN : OddN;
            int nc = c + t[d, 0], nr = r + t[d, 1];
            if (nc < 0 || nr < 0 || nc >= Cols || nr >= Rows) return -1;
            return nr * Cols + nc;
        }

        public bool Locate(float x, float z, out int index)
        {
            float q = (Sqrt3 / 3f * x - z / 3f) / R;
            float r = (2f / 3f * z) / R;
            float cx = q, cz = r, cy = -cx - cz;
            float rx = Mathf.Round(cx), ry = Mathf.Round(cy), rz = Mathf.Round(cz);
            float dx = Mathf.Abs(rx - cx), dy = Mathf.Abs(ry - cy), dz = Mathf.Abs(rz - cz);
            if (dx > dy && dx > dz) rx = -ry - rz;
            else if (dy > dz) ry = -rx - rz;
            else rz = -rx - ry;
            int row = (int)rz;
            int col = (int)rx + (row - (row & 1)) / 2;
            if (col < 0 || row < 0 || col >= Cols || row >= Rows)
            {
                index = -1;
                return false;
            }
            index = row * Cols + col;
            return true;
        }
    }
}
