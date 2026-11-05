using System.Collections.Generic;
using System.Threading.Tasks;
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
        public const float MaxGrad = 0.85f;

        public readonly ZoneRequest Zone;
        public readonly float[] H = new float[N * N];
        public readonly float[] Ramp = new float[N * N];
        public readonly float[] NoHex = new float[N * N];
        public readonly float[] Pad = new float[N * N];
        // The seam height and how far each grid point is from it (0 on the zone edge, 1 from BorderWidth in).
        private readonly float[] _border = new float[N * N];
        private readonly float[] _seamBlend = new float[N * N];
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

        /// <summary>Marks a disc as path (keeps flora off causeways built after the ground mesh).</summary>
        public void MarkRamp(Vector2 c, float r)
        {
            for (int iz = Mathf.Max(0, (int)(c.y - r)); iz <= Mathf.Min(N - 1, (int)(c.y + r) + 1); iz++)
            {
                for (int ix = Mathf.Max(0, (int)(c.x - r)); ix <= Mathf.Min(N - 1, (int)(c.x + r) + 1); ix++)
                {
                    if ((new Vector2(ix, iz) - c).sqrMagnitude <= r * r) Ramp[iz * N + ix] = 1f;
                }
            }
        }
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

            /// <summary>Height scale for this zone (0.6..1.2): some zones are low and gentle, a few stay tall.</summary>
            public float Relief;
            /// <summary>Where the high ground goes (see <see cref="MassLayout"/>).</summary>
            public MassLayout Layout;
            /// <summary>How much of the landform remains away from the high ground (0.25..0.45).</summary>
            public float MassFloor;
            /// <summary>Hills: centre (local), radius and strength. Ridge: a point on it, its direction and half width.
            /// Rim: centre and ring radius and width. Tilt: the uphill direction.</summary>
            public Vector2[] HillAt;
            public float[] HillR, HillAmp;
            public Vector2 MassPoint, MassDir;
            public float MassR, MassW;
        }

        /// <summary>The high ground stands a bit taller than the old even landform, since the rest of the zone is lower.</summary>
        private const float MassBoost = 1.3f;

        /// <summary>How a zone's high ground is arranged, so zones don't all rise in the middle.</summary>
        public enum MassLayout
        {
            /// <summary>One to three hills anywhere in the zone.</summary>
            Hills,
            /// <summary>A ridge across the zone.</summary>
            Ridge,
            /// <summary>A raised ring around a low middle.</summary>
            Rim,
            /// <summary>One side high, sloping down to the other.</summary>
            Tilt,
            /// <summary>The old look: high in the middle.</summary>
            Centre,
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
            s.Step *= 1.15f;
            s.Relief = r.Range(0.6f, 1.2f);
            s.A *= s.Relief;
            s.LiftAmp *= 0.5f + 0.5f * s.Relief;

            s.MassFloor = r.Range(0.25f, 0.45f);
            float pick = r.Value();
            s.Layout = pick < 0.35f ? MassLayout.Hills : pick < 0.55f ? MassLayout.Ridge : pick < 0.7f ? MassLayout.Rim
                : pick < 0.9f ? MassLayout.Tilt : MassLayout.Centre;
            int hills = r.Range(1, 4);
            s.HillAt = new Vector2[hills];
            s.HillR = new float[hills];
            s.HillAmp = new float[hills];
            for (int i = 0; i < hills; i++)
            {
                s.HillAt[i] = new Vector2(r.Range(45f, 205f), r.Range(45f, 205f));
                s.HillR[i] = r.Range(40f, 75f);
                s.HillAmp[i] = i == 0 ? 1f : r.Range(0.5f, 0.9f);
            }
            float a = r.Range(0f, Mathf.PI * 2f);
            s.MassDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            s.MassPoint = new Vector2(r.Range(80f, 170f), r.Range(80f, 170f));
            s.MassR = r.Range(62f, 85f);
            s.MassW = r.Range(26f, 42f);
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
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z, z.Origin.x + t, z.Origin.z));
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z, z.Origin.x + t, z.Origin.z + 250f));
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z, z.Origin.x, z.Origin.z + t));
                    minB = Mathf.Min(minB, ZoneEdges.BorderHeight(z, z.Origin.x + 250f, z.Origin.z + t));
                }
                WaterLevel = minB - 0.4f;
                HasWater = true;
            }
            Parallel.For(0, N, iz =>
            {
                for (int ix = 0; ix < N; ix++)
                {
                    float lx = ix, lz = iz;
                    float wx = z.Origin.x + lx, wz = z.Origin.z + lz;
                    float e = Mathf.Min(Mathf.Min(lx, 250f - lx), Mathf.Min(lz, 250f - lz));
                    // A wobbly falloff width, so high ground doesn't end in a square outline.
                    float fall = s.Fall * (0.9f + 0.55f * ZoneNoise.Fbm(wx + _o3.y, wz + _o3.x, 1f / 70f, 2));
                    float inner = Smooth(14f, 14f + Mathf.Max(10f, fall), e);
                    float border = ZoneEdges.BorderHeight(z, wx, wz);
                    float blend = ZoneEdges.Blend(z, wx, wz);
                    _border[iz * N + ix] = border;
                    _seamBlend[iz * N + ix] = blend;
                    float h = border + Elevation(lx, lz) * inner;
                    h = Mathf.Lerp(border, h, blend);
                    if (HasWater) h = Mathf.Max(h, WaterLevel - 1.1f);
                    H[iz * N + ix] = h;
                    // Hex fields stay out of the border strip.
                    NoHex[iz * N + ix] = Smooth(20f, 15f, e);
                }
            });
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
            var lp = new Vector2(lx, lz);
            float mass = Mass(lp, wx, wz);
            float E = s.A * p * mass * MassBoost;
            switch (z.Tile)
            {
                case TileKind.Canyon:
                {
                    float c = Mathf.Abs(ZoneNoise.Fbm(wx + _o3.x, wz + _o3.y, 1f / 170f, 3));
                    float canyon = 1f - Smooth(0.035f, 0.13f, c);
                    E = s.A * (0.6f + 0.4f * p) * (1f - canyon) * mass * MassBoost;
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


        /// <summary>
        /// 0..1: how much of the landform's height a spot keeps, from the zone's <see cref="MassLayout"/>
        /// (high ground in different places per zone). Positions are clamped into the zone, so the far
        /// terrain outside it continues smoothly.
        /// </summary>
        private float Mass(Vector2 lp, float wx, float wz)
        {
            var s = _shape;
            // Bend the shapes with noise so hills, ridges and rims aren't perfect circles and lines.
            lp += new Vector2(ZoneNoise.Fbm(wx + _o1.y, wz + _o1.x, 1f / 90f, 2), ZoneNoise.Fbm(wx + _o2.y, wz + _o2.x, 1f / 90f, 2)) * 22f;
            lp = new Vector2(Mathf.Clamp(lp.x, 0f, 250f), Mathf.Clamp(lp.y, 0f, 250f));
            float m;
            switch (s.Layout)
            {
                case MassLayout.Hills:
                    m = 0f;
                    for (int i = 0; i < s.HillAt.Length; i++)
                    {
                        m = Mathf.Max(m, s.HillAmp[i] * Mathf.Exp(-Sq((lp - s.HillAt[i]).magnitude / s.HillR[i])));
                    }
                    break;
                case MassLayout.Ridge:
                {
                    var normal = new Vector2(-s.MassDir.y, s.MassDir.x);
                    m = Mathf.Exp(-Sq(Vector2.Dot(lp - s.MassPoint, normal) / s.MassW));
                    break;
                }
                case MassLayout.Rim:
                    m = Mathf.Exp(-Sq(((lp - new Vector2(125f, 125f)).magnitude - s.MassR) / s.MassW));
                    break;
                case MassLayout.Tilt:
                    m = Smooth(-0.55f, 0.75f, Vector2.Dot(lp - new Vector2(125f, 125f), s.MassDir) / 125f);
                    break;
                default:
                    m = 1f;
                    break;
            }
            return s.MassFloor + (1f - s.MassFloor) * m;
        }

        /// <summary>
        /// A height for grid point <paramref name="k"/> eased to the seam height near the zone edge, like the base
        /// ground. Pads and ramps that reach the border strip must not break the seam with the neighbouring zone.
        /// </summary>
        private float KeepSeam(int k, float h) => Mathf.Lerp(_border[k], h, _seamBlend[k]);

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
                    H[k] = KeepSeam(k, Mathf.Lerp(H[k], height, w));
                    if (noHex && d < r + 2f) NoHex[k] = 1f;
                    Pad[k] = Mathf.Max(Pad[k], Smooth(r + 1f, r - 1f, d) * padValue);
                }
            }
        }

        // ------------------------------------------------------------------ hexes and surface

        // Per-hex noise never changes between connectivity passes, so it is sampled once.
        private float[] _hexMask, _hexLift, _hexPillar;

        public void BuildHexes(Shape s, float hexThreshold)
        {
            if (Hex == null) Hex = new HexGrid(s.HexR);
            var g = Hex;
            var z = Zone;
            if (_hexMask == null)
            {
                var oMask = ZoneNoise.Offset(z.Seed, 21);
                var oLift = ZoneNoise.Offset(z.Seed, 22);
                var oP = ZoneNoise.Offset(z.Seed, 23);
                _hexMask = new float[g.Count];
                _hexLift = new float[g.Count];
                _hexPillar = new float[g.Count];
                Parallel.For(0, g.Rows, row =>
                {
                    for (int i = row * g.Cols; i < (row + 1) * g.Cols; i++)
                    {
                        var c = g.Center(i);
                        float wx = z.Origin.x + c.x, wz = z.Origin.z + c.y;
                        _hexMask[i] = ZoneNoise.Fbm(wx + oMask.x, wz + oMask.y, 1f / 65f, 3);
                        _hexLift[i] = ZoneNoise.Fbm(wx + oLift.x, wz + oLift.y, 1f / 48f, 3);
                        _hexPillar[i] = ZoneNoise.Fbm(wx + oP.x, wz + oP.y, 1f / 30f, 2);
                    }
                });
            }
            Parallel.For(0, g.Rows, row =>
            {
                for (int i = row * g.Cols; i < (row + 1) * g.Cols; i++)
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
                    bool inside = c.x > 2f && c.y > 2f && c.x < 248f && c.y < 248f;
                    g.On[i] = inside && _hexMask[i] > hexThreshold && nohex < 0.5f;
                }
            });
            var rng = ZoneRandom.For(z.Seed, "hexjitter");
            for (int i = 0; i < g.Count; i++)
            {
                float hmax = g.Hmax[i];
                float m = _hexMask[i];
                float jitter = rng.Range(0, 3) * 0.2f;
                g.Pillar[i] = false;
                if (!g.On[i])
                {
                    g.Top[i] = hmax;
                    continue;
                }
                float liftN = _hexLift[i];
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
            for (int i = 0; i < g.Count; i++)
            {
                if (!g.On[i]) continue;
                float cl = _hexPillar[i];
                if (cl > 0.25f && prng.Chance(s.PillarChance * 6f))
                {
                    g.Pillar[i] = true;
                    g.Top[i] += Mathf.Round(prng.Range(2f, 7f) * 2f) / 2f;
                }
            }
        }

        public void ComputeSurface()
        {
            Parallel.For(0, C, iz =>
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
                        // Exact gradients of the two mesh triangles in this cell (same split as BuildMesh).
                        float g1, g2;
                        if (((ix + iz) & 1) == 0)
                        {
                            g1 = Sq(h01 - h00) + Sq(h11 - h01);
                            g2 = Sq(h10 - h00) + Sq(h11 - h10);
                        }
                        else
                        {
                            g1 = Sq(h01 - h00) + Sq(h10 - h00);
                            g2 = Sq(h11 - h01) + Sq(h11 - h10);
                        }
                        Steep[k] = Mathf.Max(g1, g2) > MaxGrad * MaxGrad;
                    }
                }
            });
        }

        // ------------------------------------------------------------------ connectivity

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
                int carved = 0, failed = 0;
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
                    else failed++;
                }
            }
            BuildHexes(s, hexThreshold);
            ComputeSurface();
            Label(out var finalSizes, out _);
            int stranded = 0, total = C * C;
            for (int k = 0; k < C * C; k++) if (Comp[k] != MainComp) stranded++;
            StrandedShare = stranded / (float)total;
        }

        private bool FindRamp(List<int> boundary, int comp, int main, HashSet<int> merged, out int from, out int to, bool fromPad = false)
        {
            from = to = -1;
            if (boundary.Count == 0) return false;
            const float g = 0.5f;
            int stride = Mathf.Max(1, boundary.Count / 40);
            int samples = (boundary.Count + stride - 1) / stride;
            // Each boundary sample finds its own shortest ramp; the earliest sample with the overall shortest wins
            // (exactly what a sequential scan with an improving bound picks).
            var bestT = new int[samples];
            var bestQ = new int[samples];
            Parallel.For(0, samples, si =>
            {
                int s = boundary[si * stride];
                int sx = s % C, sz = s / C;
                int home = fromPad ? ProtectorOf(sx + 0.5f, sz + 0.5f) : -1;
                float hs = S[s];
                int best = int.MaxValue, bq = -1;
                for (int d = 0; d < 32; d++)
                {
                    float ang = d / 32f * Mathf.PI * 2f;
                    float dx = Mathf.Cos(ang), dz = Mathf.Sin(ang);
                    for (int t = 2; t < 170; t++)
                    {
                        if (t >= best) break;
                        int qx = Mathf.RoundToInt(sx + dx * t), qz = Mathf.RoundToInt(sz + dz * t);
                        if (qx < 1 || qz < 1 || qx >= C - 1 || qz >= C - 1) break;
                        if (t > 3 && (home < 0 ? Protected(qx + 0.5f, qz + 0.5f) : ProtectedOther(qx + 0.5f, qz + 0.5f, home))) break;
                        int q = qz * C + qx;
                        int cq = Comp[q];
                        if (cq == main || merged.Contains(cq))
                        {
                            if (Mathf.Abs(S[q] - hs) <= g * t && (fromPad || !CutsTrench(sx, sz, dx, dz, t, hs, S[q])))
                            {
                                best = t;
                                bq = q;
                                break;
                            }
                        }
                    }
                }
                bestT[si] = best;
                bestQ[si] = bq;
            });
            int win = -1;
            for (int si = 0; si < samples; si++) if (bestQ[si] >= 0 && (win < 0 || bestT[si] < bestT[win])) win = si;
            if (win < 0) return false;
            from = boundary[win * stride];
            to = bestQ[win];
            merged.Add(comp);
            return true;
        }

        /// <summary>True when a straight ramp would slice a deep trench through higher ground (which splits that ground in two).</summary>
        private bool CutsTrench(int sx, int sz, float dx, float dz, int t, float h0, float h1)
        {
            for (int i = 2; i < t; i++)
            {
                int qx = Mathf.RoundToInt(sx + dx * i), qz = Mathf.RoundToInt(sz + dz * i);
                float line = Mathf.Lerp(h0, h1, i / (float)t);
                if (S[qz * C + qx] > line + 1.6f) return true;
            }
            return false;
        }

        private int ProtectorOf(float x, float z)
        {
            for (int i = 0; i < _protect.Count; i++)
            {
                if ((new Vector2(x, z) - _protect[i].c).sqrMagnitude < _protect[i].r * _protect[i].r) return i;
            }
            return -1;
        }

        private bool ProtectedOther(float x, float z, int home)
        {
            for (int i = 0; i < _protect.Count; i++)
            {
                if (i != home && (new Vector2(x, z) - _protect[i].c).sqrMagnitude < _protect[i].r * _protect[i].r) return true;
            }
            return false;
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
                    if (Protected(ix, iz)) continue; // shrine and gate pads stay flat
                    float t = Mathf.Clamp01((u - 1.5f) / Mathf.Max(len, 0.01f));
                    float rh = Mathf.Lerp(ha, hb, t);
                    int k = iz * N + ix;
                    H[k] = KeepSeam(k, Mathf.Lerp(H[k], rh, w));
                    Ramp[k] = Mathf.Max(Ramp[k], w);
                    NoHex[k] = 1f;
                }
            }
        }

        // ------------------------------------------------------------------ mesh

        /// <summary>
        /// Second opinion from the real walk analysis: cells that are standable but not two-way get ramps
        /// carved to two-way ground, exactly like the model-based pass. Ground around <paramref name="anchors"/>
        /// (local positions of anchors players can't reach) is repaired first whatever its size, and may ramp out of its
        /// own pad. Returns the number of ramps carved.
        /// </summary>
        public int RepairFromWalk(byte[] map, Shape s, float hexThreshold, int minSize = 6, List<Vector2> anchors = null)
        {
            int main = MainComp;
            const int baseId = 1000000;
            for (int k = 0; k < C * C; k++) Comp[k] = map[k] == 2 ? main : map[k] == 1 ? -2 : -1;
            var sizes = new List<int>();
            var stack = new Stack<int>();
            for (int k = 0; k < C * C; k++)
            {
                if (Comp[k] != -2) continue;
                int id = baseId + sizes.Count;
                int size = 0;
                Comp[k] = id;
                stack.Push(k);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    size++;
                    int x = cur % C, z = cur / C;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= C || nz >= C) continue;
                        int q = nz * C + nx;
                        if (Comp[q] != -2) continue;
                        Comp[q] = id;
                        stack.Push(q);
                    }
                }
                sizes.Add(size);
            }
            var forced = new HashSet<int>();
            if (anchors != null)
            {
                foreach (var a in anchors)
                {
                    int ax = Mathf.Clamp(Mathf.FloorToInt(a.x), 0, C - 1), az = Mathf.Clamp(Mathf.FloorToInt(a.y), 0, C - 1);
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            int x = ax + dx, z = az + dz;
                            if (x >= 0 && z >= 0 && x < C && z < C && Comp[z * C + x] >= baseId) forced.Add(Comp[z * C + x]);
                        }
                    }
                }
            }
            var order = new List<int>();
            for (int i = 0; i < sizes.Count; i++) if (sizes[i] >= minSize || forced.Contains(baseId + i)) order.Add(baseId + i);
            order.Sort((a, b) =>
            {
                bool fa = forced.Contains(a), fb = forced.Contains(b);
                if (fa != fb) return fa ? -1 : 1;
                int bySize = sizes[b - baseId].CompareTo(sizes[a - baseId]);
                return bySize != 0 ? bySize : a.CompareTo(b);
            });
            var bounds = new Dictionary<int, List<int>>();
            foreach (int c in order) bounds[c] = new List<int>();
            for (int k = 0; k < C * C; k++)
            {
                if (!bounds.TryGetValue(Comp[k], out var list)) continue;
                int x = k % C, z = k / C;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= C || nz >= C) continue;
                    if (Comp[nz * C + nx] != Comp[k]) { list.Add(k); break; }
                }
            }
            var merged = new HashSet<int>();
            int carved = 0;
            foreach (int c in order)
            {
                if (carved > 60) break;
                if (FindRamp(bounds[c], c, main, merged, out int from, out int to, forced.Contains(c)))
                {
                    Carve(from, to);
                    carved++;
                    RampsCarved++;
                }
            }
            carved += FillPits(sizes, baseId, minSize, forced, main);
            BuildHexes(s, hexThreshold);
            ComputeSurface();
            Label(out _, out _);
            int stranded = 0;
            for (int k = 0; k < C * C; k++) if (Comp[k] != MainComp) stranded++;
            StrandedShare = stranded / (float)(C * C);
            return carved;
        }

        /// <summary>How far below the lowest walkable rim a filled pit's floor ends up (players step up to 1 m).</summary>
        private const float PitFillBelowRim = 0.6f;
        public int PitsFilled;

        /// <summary>
        /// Small stranded patches (too small to be worth a ramp) that sit below the walkable ground around them are
        /// holes players fall into and can't climb out of. Raise each, with its steep walls, to just under the lowest
        /// walkable rim, so one step gets you out. Returns how many were filled.
        /// </summary>
        private int FillPits(List<int> sizes, int baseId, int minSize, HashSet<int> forced, int main)
        {
            var cells = new Dictionary<int, List<int>>();
            for (int k = 0; k < C * C; k++)
            {
                int c = Comp[k];
                if (c < baseId || sizes[c - baseId] >= minSize || forced.Contains(c)) continue;
                if (!cells.TryGetValue(c, out var list)) cells[c] = list = new List<int>();
                list.Add(k);
            }
            var ids = new List<int>(cells.Keys);
            ids.Sort();
            int filled = 0;
            foreach (int c in ids)
            {
                var list = cells[c];
                float rim = float.MaxValue, floor = float.MinValue;
                foreach (int k in list)
                {
                    floor = Mathf.Max(floor, S[k]);
                    int x = k % C, z = k / C;
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= C || nz >= C) continue;
                            int q = nz * C + nx;
                            if (Comp[q] == main) rim = Mathf.Min(rim, S[q]);
                        }
                    }
                }
                if (rim == float.MaxValue) continue;
                float fill = rim - PitFillBelowRim;
                // Only real holes: a ledge or a shallow dip is left alone.
                if (fill <= floor + 0.3f) continue;
                foreach (int k in list)
                {
                    int x = k % C, z = k / C;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= C || nz >= C) continue;
                            int q = nz * C + nx;
                            // The pit and its unwalkable walls; walkable ground around it keeps its height.
                            if (Comp[q] != c && Comp[q] != -1) continue;
                            RaiseCell(nx, nz, fill);
                        }
                    }
                }
                filled++;
                PitsFilled++;
            }
            return filled;
        }

        private void RaiseCell(int x, int z, float h)
        {
            for (int vz = z; vz <= z + 1; vz++)
            {
                for (int vx = x; vx <= x + 1; vx++)
                {
                    int v = vz * N + vx;
                    if (H[v] < h) H[v] = KeepSeam(v, h);
                }
            }
        }

        public const int Chunk = 50;

        /// <summary>The collision mesh of one 50 m terrain chunk: shared grid vertices, the same triangles as the render mesh.</summary>
        public PrismCollider ChunkCollider(int cx, int cz)
        {
            var o = Zone.Origin;
            const int w = Chunk + 1;
            var pos = new Vector3[w * w];
            for (int z = 0; z <= Chunk; z++)
            for (int x = 0; x <= Chunk; x++)
                pos[z * w + x] = new Vector3(o.x + cx + x, this[cx + x, cz + z], o.z + cz + z);
            var idx = new int[Chunk * Chunk * 6];
            int n = 0;
            for (int z = 0; z < Chunk; z++)
            {
                for (int x = 0; x < Chunk; x++)
                {
                    int a = z * w + x, b = a + w, c = a + w + 1, d = a + 1;
                    if (((cx + x + cz + z) & 1) == 0)
                    {
                        idx[n++] = a; idx[n++] = b; idx[n++] = c;
                        idx[n++] = a; idx[n++] = c; idx[n++] = d;
                    }
                    else
                    {
                        idx[n++] = a; idx[n++] = b; idx[n++] = d;
                        idx[n++] = b; idx[n++] = c; idx[n++] = d;
                    }
                }
            }
            return new PrismCollider($"Terrain {cx / Chunk},{cz / Chunk}", pos, idx, PrismCollision.TagGround);
        }

        /// <summary>The flat-shaded render mesh of one terrain chunk (each triangle colored at its centre).</summary>
        public PrismMesh ChunkRender(int cx, int cz, System.Func<Vector3, Vector3, Color> colorAt)
        {
            var o = Zone.Origin;
            var mb = new PrismMesh($"Terrain {cx / Chunk},{cz / Chunk}", Chunk * Chunk * 6);
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                mb.FlatTriangle(a, b, c, colorAt((a + b + c) / 3f, n));
            }
            for (int z = cz; z < cz + Chunk; z++)
            {
                for (int x = cx; x < cx + Chunk; x++)
                {
                    var a = new Vector3(o.x + x, this[x, z], o.z + z);
                    var b = new Vector3(o.x + x, this[x, z + 1], o.z + z + 1);
                    var c = new Vector3(o.x + x + 1, this[x + 1, z + 1], o.z + z + 1);
                    var d = new Vector3(o.x + x + 1, this[x + 1, z], o.z + z);
                    if (((x + z) & 1) == 0)
                    {
                        Tri(a, b, c);
                        Tri(a, c, d);
                    }
                    else
                    {
                        Tri(a, b, d);
                        Tri(b, c, d);
                    }
                }
            }
            return mb;
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
