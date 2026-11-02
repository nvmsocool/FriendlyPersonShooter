using System.Collections.Generic;
using UnityEngine;
using static FriendlyPersonShooter.ZoneArt.C.PrismGeo;

namespace FriendlyPersonShooter.ZoneArt.C
{
    public enum LandmarkKind { CrystalSpire, MonolithRing, RainbowArch, FloatingSpiral, GiantTree, Ziggurat }

    public sealed class TowerPlan
    {
        public Vector2 Local;
        public float R, BaseY, TopY, StairAngle, StairDir = 1f;
        public bool Cantilever;
        public float CantileverAngle;
        public int Style;
    }

    public sealed class LandmarkPlan
    {
        public LandmarkKind Kind;
        public Vector2 Local;     // the anchor
        public Vector2 Feature;   // where the set piece stands
        public float PadR;
        public float BaseY;
        public float Dir;
        public string Name;
    }

    /// <summary>Builds the zone's solid geometry: hex fields, towers with spiral stairs, bridges, landmarks and props.</summary>
    public sealed class PrismStructures
    {
        private readonly ZoneRequest _zone;
        private readonly PrismTerrain _t;
        private readonly PrismPalette _p;
        private readonly Transform _root;
        public readonly MeshBuilder Solid = new MeshBuilder();
        public readonly MeshBuilder Crystal = new MeshBuilder();
        public readonly MeshBuilder Decor = new MeshBuilder();     // no collider (floating, out of reach)
        public readonly MeshBuilder Plantable = new MeshBuilder(); // tower tops, ziggurats: collider, plants may grow
        public readonly MeshBuilder DecorRock = new MeshBuilder(); // floating rock, hex material, no collider
        public readonly List<(Vector3 pos, float height)> Heroes = new List<(Vector3, float)>();
        public float BandH, BandOffset;
        public int Towers, Bridges, Slabs, Crystals;

        public PrismStructures(ZoneRequest zone, PrismTerrain terrain, PrismPalette palette, Transform root)
        {
            _zone = zone;
            _t = terrain;
            _p = palette;
            _root = root;
            var r = ZoneRandom.For(zone.Seed, "bands");
            BandH = r.Range(1.6f, 3.2f);
            BandOffset = r.Range(0f, 10f);
        }

        private Vector3 W(Vector2 local, float y) => new Vector3(_zone.Origin.x + local.x, y, _zone.Origin.z + local.y);

        public Color StrataAt(float y)
        {
            int band = Mathf.FloorToInt((y + BandOffset) / BandH);
            int k = ((band % _p.Strata.Length) + _p.Strata.Length) % _p.Strata.Length;
            return _p.Strata[k];
        }

        // ------------------------------------------------------------------ hex column

        /// <summary>A beveled hex column with banded sides. sideMask bit d = draw side d.</summary>
        public void HexColumn(MeshBuilder mb, Vector3 c, float r, float top, float bottom, Color topCol, int sideMask = 63, float bevel = 0.14f, float[] sideBottom = null)
        {
            float ri = r * 0.8f;
            float rb = r * 0.97f;
            var inner = new Vector3[6];
            var outer = new Vector3[6];
            for (int k = 0; k < 6; k++)
            {
                float a = (30f + 60f * k) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                inner[k] = new Vector3(c.x, top, c.z) + d * ri;
                outer[k] = new Vector3(c.x, top - bevel, c.z) + d * rb;
            }
            var center = new Vector3(c.x, top, c.z);
            for (int k = 0; k < 6; k++) mb.FlatTriangle(center, inner[(k + 1) % 6], inner[k], topCol);
            var bevelCol = Color.Lerp(topCol, Color.white, 0.22f);
            for (int k = 0; k < 6; k++)
            {
                int j = (k + 1) % 6;
                Quad(mb, outer[k], inner[k], inner[j], outer[j], bevelCol);
            }
            for (int k = 0; k < 6; k++)
            {
                if ((sideMask & (1 << k)) == 0) continue;
                int j = (k + 1) % 6;
                float sb = sideBottom != null ? Mathf.Max(bottom, sideBottom[k]) : bottom;
                if (sb < top - bevel - 0.01f) SideBands(mb, outer[k], outer[j], top - bevel, sb);
            }
        }

        /// <summary>A vertical wall quad from a..b (top at yTop) split into colored strata bands.</summary>
        public void SideBands(MeshBuilder mb, Vector3 a, Vector3 b, float yTop, float yBot, int maxBands = 1)
        {
            float y = yTop;
            int guard = 0;
            while (y > yBot + 0.01f && guard++ < maxBands)
            {
                float bandBase = Mathf.Floor((y - 0.001f + BandOffset) / BandH) * BandH - BandOffset;
                float y1 = Mathf.Max(yBot, bandBase);
                if (guard == maxBands) y1 = yBot;
                var col = StrataAt((y + y1) * 0.5f);
                var lo = Shade(col, 0.78f);
                QuadGrad(mb, new Vector3(a.x, y1, a.z), new Vector3(a.x, y, a.z), new Vector3(b.x, y, b.z), new Vector3(b.x, y1, b.z), lo, col);
                y = y1;
            }
        }

        // ------------------------------------------------------------------ hex fields

        public GameObject HexField(Material mat, List<Collider> plantable)
        {
            var g = _t.Hex;
            var rng = ZoneRandom.For(_zone.Seed, "hexcolor");
            var oC = ZoneNoise.Offset(_zone.Seed, 41);
            const int chunks = 4;
            var builders = new MeshBuilder[chunks * chunks];
            for (int i = 0; i < builders.Length; i++) builders[i] = new MeshBuilder();
            for (int i = 0; i < g.Count; i++)
            {
                if (!g.On[i]) continue;
                var c2 = g.Center(i);
                var c = W(c2, 0f);
                float top = g.Top[i];
                float bottom = g.Hmin[i] - 0.8f;
                var sideBottom = new float[6];
                for (int k = 0; k < 6; k++)
                {
                    int j = g.Neighbor(i, 5 - k);
                    sideBottom[k] = j >= 0 && g.On[j] ? g.Top[j] - 0.3f : bottom;
                }
                float n = ZoneNoise.Fbm(c.x + oC.x, c.z + oC.y, 1f / 40f, 2);
                int ti = Mathf.Clamp(Mathf.FloorToInt((n * 0.5f + 0.5f) * _p.HexTop.Length * 1.3f), 0, _p.HexTop.Length - 1);
                var topCol = _p.HexTop[ti];
                float lift = top - g.Hmax[i];
                if (lift < 0.5f && !g.Pillar[i] && n > 0.1f) topCol = Color.Lerp(topCol, _p.Grass[1], 0.7f);
                topCol = rng.Jitter(topCol, 0.012f, 0.06f, 0.07f);
                if (g.Pillar[i]) topCol = Color.Lerp(topCol, _p.Glow, 0.25f);
                int ci = Mathf.Clamp((int)(c2.y / 62.5f), 0, 3) * chunks + Mathf.Clamp((int)(c2.x / 62.5f), 0, 3);
                HexColumn(builders[ci], c, g.R, top, bottom, topCol, 63, Mathf.Min(0.14f, g.R * 0.1f), sideBottom);
            }
            var root = new GameObject("Hex columns");
            root.transform.SetParent(_root, false);
            for (int i = 0; i < builders.Length; i++)
            {
                if (builders[i].VertexCount == 0) continue;
                var go = builders[i].ToGameObject($"Hex {i}", root.transform, mat, collider: true);
                plantable.Add(go.GetComponent<Collider>());
            }
            return root;
        }

        // ------------------------------------------------------------------ towers

        public void Tower(TowerPlan plan, ref ZoneRandom rng)
        {
            Towers++;
            var c = W(plan.Local, plan.BaseY);
            float rt = rng.Range(1.4f, 1.9f);
            float sqrt3 = Mathf.Sqrt(3f);
            int range = Mathf.CeilToInt(plan.R / rt) + 2;
            var cells = new HashSet<(int, int)>();
            var list = new List<(int q, int r, Vector3 p)>();
            for (int q = -range; q <= range; q++)
            {
                for (int r = -range; r <= range; r++)
                {
                    var off = new Vector2(rt * sqrt3 * (q + r * 0.5f), rt * 1.5f * r);
                    if (off.magnitude + rt > plan.R) continue;
                    cells.Add((q, r));
                    list.Add((q, r, c + new Vector3(off.x, 0, off.y)));
                }
            }
            // Axial neighbour for corner-side k of a pointy-top hex: side k spans corners k..k+1 (30+60k deg).
            int[,] sideN = { { 0, 1 }, { -1, 1 }, { -1, 0 }, { 0, -1 }, { 1, -1 }, { 1, 0 } };
            var topBase = _p.HexTop[rng.Range(0, _p.HexTop.Length)];
            foreach (var (q, r, p) in list)
            {
                int mask = 0;
                for (int k = 0; k < 6; k++)
                {
                    if (!cells.Contains((q + sideN[k, 0], r + sideN[k, 1]))) mask |= 1 << k;
                }
                var tc = rng.Jitter(topBase, 0.01f, 0.05f, 0.05f);
                HexColumn(Plantable, p, rt, plan.TopY, plan.BaseY - 3f, tc, mask, 0.1f);
            }

            // Spiral stair of floating slabs.
            float rise = 0.6f, arc = 1.5f;
            float r0 = plan.R - 0.25f, r1 = plan.R + 3.0f;
            float rMid = (r0 + r1) * 0.5f;
            float dA = arc / rMid;
            int n = Mathf.RoundToInt((plan.TopY - plan.BaseY) / rise) - 1;
            var stepA = _p.Stone[rng.Range(0, _p.Stone.Length)];
            var stepB = _p.Flowers[rng.Range(0, _p.Flowers.Length)];
            stepB = Color.Lerp(stepB, Color.white, 0.2f);
            float lastA = plan.StairAngle;
            for (int k = 0; k < n; k++)
            {
                float a = plan.StairAngle + k * dA * plan.StairDir;
                float lo = plan.StairDir > 0 ? a : a - dA * 1.04f;
                float hi = plan.StairDir > 0 ? a + dA * 1.04f : a;
                float y = plan.BaseY + (k + 1) * rise;
                var col = (k & 1) == 0 ? stepA : stepB;
                Sector(Solid, c, r0, r1, lo, hi, y, 0.45f, col, Shade(col, 0.85f));
                lastA = a + dA * plan.StairDir;
            }
            // Landing joins the last step to the top.
            {
                float lo = plan.StairDir > 0 ? lastA : lastA - dA * 2f;
                float hi = plan.StairDir > 0 ? lastA + dA * 2f : lastA;
                Sector(Solid, c, plan.R - 3f, r1, lo, hi, plan.TopY, 1.2f, stepA, Shade(stepA, 0.85f));
            }
            if (plan.Cantilever)
            {
                float a = plan.CantileverAngle;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float len = rng.Range(7f, 11f);
                var mid = c + dir * (plan.R - 2f + len * 0.5f);
                mid.y = plan.TopY - 0.4f;
                Box(Solid, mid, Quaternion.LookRotation(dir), new Vector3(3.2f, 0.8f, len), _p.Stone[0], StrataAt(plan.TopY));
                // Glowing tip lantern.
                var tip = c + dir * (plan.R - 2f + len) + Vector3.up * (plan.TopY + 0.9f);
                tip.y = plan.TopY + 0.9f;
                Prism(Crystal, tip - Vector3.up * 0.9f, Vector3.up, 0.25f, 0.9f, 6, 0.6f, _p.Crystal[0], _p.Crystal[1], 0f, 0.4f);
            }
            if (rng.Chance(0.5f))
            {
                // A crystal crown.
                var top = c + Vector3.up * (plan.TopY - plan.BaseY);
                CrystalCluster(top, rng.Range(2f, 4.5f), rng.Range(3, 7), ref rng, 0.6f);
            }
            Heroes.Add((c, plan.TopY - plan.BaseY));
        }

        public bool Bridge(TowerPlan a, TowerPlan b, ref ZoneRandom rng)
        {
            var d = b.Local - a.Local;
            float dist = d.magnitude;
            var dir = d / dist;
            var start = a.Local + dir * (a.R - 1.5f);
            var end = b.Local - dir * (b.R - 1.5f);
            float len = (end - start).magnitude;
            if (len < 8f) return false;
            float dh = b.TopY - a.TopY;
            if (Mathf.Abs(dh) > len * 0.4f) return false;
            // Clearance below the deck.
            for (float s = 0.1f; s < 0.9f; s += 0.05f)
            {
                var p = Vector2.Lerp(start, end, s);
                float deck = Mathf.Lerp(a.TopY, b.TopY, s);
                if (_t.SurfaceAt(p.x, p.y) > deck - 4f) return false;
            }
            int segs = Mathf.CeilToInt(len / 2f);
            var colA = _p.Stone[rng.Range(0, _p.Stone.Length)];
            var colB = Color.Lerp(_p.Flowers[rng.Range(0, _p.Flowers.Length)], Color.white, 0.25f);
            for (int i = 0; i < segs; i++)
            {
                float s0 = i / (float)segs, s1 = (i + 1) / (float)segs;
                var p0 = W(Vector2.Lerp(start, end, s0), Mathf.Lerp(a.TopY, b.TopY, s0));
                var p1 = W(Vector2.Lerp(start, end, s1), Mathf.Lerp(a.TopY, b.TopY, s1));
                var mid = (p0 + p1) * 0.5f + Vector3.down * 0.35f;
                var f = (p1 - p0).normalized;
                var col = (i & 1) == 0 ? colA : colB;
                Box(Solid, mid, Quaternion.LookRotation(f), new Vector3(3f, 0.7f, (p1 - p0).magnitude + 0.05f), col, Shade(col, 0.8f));
                if (i % 3 == 1)
                {
                    var side = Vector3.Cross(Vector3.up, f).normalized;
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        var post = mid + side * sgn * 1.35f + Vector3.up * 0.35f;
                        Prism(Solid, post, Vector3.up, 0.12f, 1.0f, 6, 0f, colA, colA);
                        Ball(Crystal, post + Vector3.up * 1.15f, Vector3.one * 0.2f, _p.Crystal[0], _p.Crystal[1], ref rng, 0.05f, 1, true, 0.35f);
                    }
                }
            }
            Bridges++;
            return true;
        }

        // ------------------------------------------------------------------ props

        public void CrystalCluster(Vector3 basePos, float size, int count, ref ZoneRandom rng, float glowAlpha = 0.55f)
        {
            Crystals++;
            var ca = _p.Crystal[rng.Range(0, _p.Crystal.Length)];
            var cb = _p.Crystal[rng.Range(0, _p.Crystal.Length)];
            for (int i = 0; i < count; i++)
            {
                float s = i == 0 ? 1f : rng.Range(0.35f, 0.75f);
                float tilt = i == 0 ? rng.Range(0f, 10f) : rng.Range(15f, 45f);
                float az = rng.Range(0f, 360f);
                var axis = Quaternion.Euler(0, az, 0) * Quaternion.Euler(tilt, 0, 0) * Vector3.up;
                var off = i == 0 ? Vector3.zero : Quaternion.Euler(0, az, 0) * Vector3.forward * size * rng.Range(0.15f, 0.4f);
                float len = size * s * rng.Range(1.6f, 2.6f);
                float rad = size * s * rng.Range(0.18f, 0.28f);
                var b = basePos + off - axis * size * 0.3f;
                Prism(Crystal, b, axis, rad, len, rng.Chance(0.6f) ? 6 : 5, rad * rng.Range(1.2f, 2.2f), Shade(ca, 0.75f), Color.Lerp(ca, cb, 0.6f), rng.Range(0f, 1f), glowAlpha, rng.Range(0.75f, 1f));
            }
        }

        public void Slab(Vector2 local, float groundY, ref ZoneRandom rng)
        {
            Slabs++;
            float len = rng.Range(7f, 13f), wid = rng.Range(3.5f, 6f), th = rng.Range(0.8f, 1.4f);
            float pitch = rng.Range(10f, 22f);
            float yaw = rng.Range(0f, 360f);
            var rot = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-pitch, 0, 0);
            var fwd = rot * Vector3.forward;
            // Low end buried at the ground, high end raised.
            var center = W(local, groundY) + Vector3.up * (Mathf.Sin(pitch * Mathf.Deg2Rad) * len * 0.5f - th * 0.5f - 0.15f);
            var top = Color.Lerp(_p.Stone[rng.Range(0, _p.Stone.Length)], _p.HexTop[rng.Range(0, _p.HexTop.Length)], 0.4f);
            Box(Solid, center, rot, new Vector3(wid, th, len), top, StrataAt(groundY + 2f));
            // A glowing seam on top.
            var seamC = center + rot * new Vector3(0, th * 0.5f + 0.01f, 0);
            Box(Crystal, seamC, rot, new Vector3(0.18f, 0.04f, len * 0.85f), WithAlpha(_p.Glow, 0.4f), WithAlpha(_p.Glow, 0.4f));
            _ = fwd;
        }

        public void Boulder(Vector3 pos, float size, ref ZoneRandom rng)
        {
            var col = StrataAt(pos.y + rng.Range(0f, 6f));
            Ball(Solid, pos + Vector3.up * size * 0.25f, new Vector3(size, size * rng.Range(0.55f, 0.9f), size * rng.Range(0.8f, 1.2f)), Shade(col, 0.8f), Color.Lerp(col, Color.white, 0.15f), ref rng, 0.22f, 1, true);
        }

        public void FloatingIsland(Vector3 pos, float r, ref ZoneRandom rng, PrismFloraHook flora)
        {
            // An inverted stepped stack of hex prisms (strata via the hex material), a grass cap and hanging vines.
            float y = 0f, rad = r;
            int layers = rng.Range(3, 5);
            float rot = rng.Range(0f, 1f);
            for (int i = 0; i < layers; i++)
            {
                float len = (i == 0 ? 1.4f : rng.Range(1.6f, 2.8f)) * (r / 6f);
                var col = StrataAt(pos.y - y - len * 0.5f);
                Prism(DecorRock, pos + Vector3.down * y, Vector3.down, rad, len, 6, i == layers - 1 ? rad * rng.Range(1.2f, 2f) : 0f, col, Shade(col, 0.8f), rot + i * rng.Range(0.2f, 0.6f));
                y += len;
                rad *= rng.Range(0.58f, 0.75f);
            }
            var grass = _p.Grass[0];
            int sides = 6;
            for (int i = 0; i < sides; i++)
            {
                float a0 = rot + i / (float)sides * Mathf.PI * 2f, a1 = rot + (i + 1) / (float)sides * Mathf.PI * 2f;
                var p0 = pos + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r * 1.02f + Vector3.up * 0.02f;
                var p1 = pos + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r * 1.02f + Vector3.up * 0.02f;
                DecorRock.FlatTriangle(pos + Vector3.up * 0.25f, p1, p0, grass);
            }
            int vines = rng.Range(10, 22);
            var vc = _p.Foliage[rng.Range(0, _p.Foliage.Length)];
            var tip = _p.Flowers[rng.Range(0, _p.Flowers.Length)];
            for (int i = 0; i < vines; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                // Hex rim radius at this angle (approximate the hexagon by its inradius..radius).
                var p = pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * 0.9f;
                float len = rng.Range(1.5f, 2.5f + r * 0.5f);
                PrismFlora.Blade1(Decor, p, p + Vector3.down * len + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.3f, rng.Range(0.2f, 0.4f), Shade(vc, 0.8f), Color.Lerp(vc, tip, 0.6f));
            }
            flora?.Invoke(Decor, pos + Vector3.up * 0.2f, r);
            int k = rng.Range(1, 3);
            for (int i = 0; i < k; i++)
            {
                var b = pos + Vector3.down * (y * rng.Range(0.3f, 0.7f)) + new Vector3(rng.Range(-0.3f, 0.3f) * r, 0, rng.Range(-0.3f, 0.3f) * r);
                Prism(Crystal, b, Vector3.down + new Vector3(rng.Range(-0.4f, 0.4f), 0, rng.Range(-0.4f, 0.4f)), r * 0.1f, r * rng.Range(0.3f, 0.6f), 5, r * 0.2f, _p.Crystal[0], _p.Crystal[1], 0f, 0.5f);
            }
        }

        // ------------------------------------------------------------------ landmarks

        public void Landmark(LandmarkPlan lp, ref ZoneRandom rng, PrismFloraHook giantTree)
        {
            var anchor = W(lp.Local, lp.BaseY);
            var feat = W(lp.Feature, lp.BaseY);
            switch (lp.Kind)
            {
                case LandmarkKind.CrystalSpire:
                {
                    float size = rng.Range(4f, 6.5f);
                    CrystalCluster(feat, size, rng.Range(7, 12), ref rng, 0.5f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = rng.Range(0f, Mathf.PI * 2f), d = rng.Range(8f, 13f);
                        var p = feat + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                        p.y = _t.SurfaceAt(p.x - _zone.Origin.x, p.z - _zone.Origin.z);
                        CrystalCluster(p, rng.Range(1f, 2.2f), rng.Range(3, 6), ref rng, 0.55f);
                    }
                    Heroes.Add((feat, size * 2.4f));
                    break;
                }
                case LandmarkKind.MonolithRing:
                {
                    float R = rng.Range(9f, 12.5f);
                    int n = rng.Range(7, 12);
                    var tops = new Vector3[n];
                    float h0 = rng.Range(6f, 10f);
                    for (int i = 0; i < n; i++)
                    {
                        float a = lp.Dir + i / (float)n * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        float h = h0 * rng.Range(0.8f, 1.2f);
                        var p = anchor + dir * R;
                        var rot = Quaternion.LookRotation(-dir) * Quaternion.Euler(rng.Range(-4f, 4f), 0, rng.Range(-4f, 4f));
                        var col = _p.Stone[i % _p.Stone.Length];
                        var side = Color.Lerp(StrataAt(lp.BaseY + i * BandH), col, 0.35f);
                        Box(Solid, p + Vector3.up * (h * 0.5f - 0.8f), rot, new Vector3(2.2f, h, 1.1f), col, side);
                        // Rune stripe on the inner face.
                        Box(Crystal, p + Vector3.up * (h * 0.45f) - dir * 0.56f, rot, new Vector3(0.35f, h * 0.6f, 0.05f), WithAlpha(_p.Glow, 0.3f), WithAlpha(_p.Glow, 0.3f));
                        tops[i] = p + Vector3.up * (h - 0.8f);
                    }
                    for (int i = 0; i < n; i += 2)
                    {
                        if (i + 1 >= n) break;
                        var a = tops[i];
                        var b = tops[i + 1];
                        var mid = (a + b) * 0.5f + Vector3.up * 0.5f;
                        mid.y = Mathf.Max(a.y, b.y) + 0.5f;
                        var f = b - a;
                        f.y = 0;
                        Box(Solid, mid, Quaternion.LookRotation(f.normalized), new Vector3(1.4f, 1f, f.magnitude + 2.4f), _p.Stone[0], StrataAt(mid.y));
                    }
                    // Floating heart crystal.
                    var heart = anchor + Vector3.up * (h0 + 3f);
                    Prism(Crystal, heart, Vector3.up, 1.2f, 0.01f, 6, 2.6f, _p.Crystal[0], _p.Crystal[1], 0f, 0.3f);
                    Prism(Crystal, heart, Vector3.down, 1.2f, 0.01f, 6, 2.6f, _p.Crystal[0], _p.Crystal[1], 0f, 0.3f);
                    Heroes.Add((anchor, h0 + 4f));
                    break;
                }
                case LandmarkKind.RainbowArch:
                {
                    var dir = new Vector3(Mathf.Cos(lp.Dir), 0, Mathf.Sin(lp.Dir));
                    float half = rng.Range(9f, 12f);
                    float pillarH = rng.Range(5f, 9f);
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        var p = anchor + dir * half * sgn;
                        for (int k = 0; k < 3; k++)
                        {
                            float a = k * 2.1f;
                            var off = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.1f;
                            HexColumn(Solid, p + off, 1.3f, lp.BaseY + pillarH - k * 0.9f, lp.BaseY - 2f, _p.HexTop[k % _p.HexTop.Length]);
                        }
                        HexColumn(Solid, p, 1.4f, lp.BaseY + pillarH + 0.4f, lp.BaseY - 2f, _p.HexTop[0]);
                    }
                    int segs = 15;
                    var rainbow = new List<Color>(_p.Flowers);
                    rainbow.AddRange(_p.Crystal);
                    var arcC = anchor + Vector3.up * (pillarH + 0.4f);
                    for (int i = 0; i < segs; i++)
                    {
                        float a0 = Mathf.PI * i / segs, a1 = Mathf.PI * (i + 1) / segs;
                        var p0 = arcC + dir * Mathf.Cos(a0) * half + Vector3.up * Mathf.Sin(a0) * half * 0.9f;
                        var p1 = arcC + dir * Mathf.Cos(a1) * half + Vector3.up * Mathf.Sin(a1) * half * 0.9f;
                        var f = (p1 - p0);
                        var col = rainbow[i % rainbow.Count];
                        var radial = ((p0 + p1) * 0.5f - arcC).normalized;
                        Box(Solid, (p0 + p1) * 0.5f, Quaternion.LookRotation(f.normalized, radial), new Vector3(2.6f, 1.3f, f.magnitude + 0.3f), col, Shade(col, 0.85f));
                    }
                    Heroes.Add((anchor, pillarH + half));
                    break;
                }
                case LandmarkKind.FloatingSpiral:
                {
                    float Ht = Mathf.Round(rng.Range(14f, 22f) / 0.6f) * 0.6f;
                    float platformR = rng.Range(4f, 5.2f);
                    Prism(Solid, feat + Vector3.down * 1f, Vector3.up, 1.3f, Ht + 1f, 8, 0f, StrataAt(lp.BaseY), StrataAt(lp.BaseY + Ht));
                    // Platform disc.
                    var pc = _p.HexTop[rng.Range(0, _p.HexTop.Length)];
                    Prism(Plantable, feat + Vector3.up * (Ht - 1f), Vector3.up, platformR, 1f, 16, 0f, Shade(pc, 0.8f), pc);
                    float rise = 0.6f, arc = 1.5f;
                    float r0 = platformR + 0.1f, r1 = platformR + 3f;
                    float dA = arc / ((r0 + r1) * 0.5f);
                    int n = Mathf.RoundToInt(Ht / rise) - 1;
                    var stepA = _p.Stone[0];
                    var stepB = Color.Lerp(_p.Flowers[0], Color.white, 0.2f);
                    float last = lp.Dir;
                    for (int k = 0; k < n; k++)
                    {
                        float a = lp.Dir + k * dA;
                        Sector(Solid, feat, r0, r1, a, a + dA * 1.04f, lp.BaseY + (k + 1) * rise, 0.4f, (k & 1) == 0 ? stepA : stepB, Shade(stepA, 0.85f));
                        last = a + dA;
                    }
                    Sector(Solid, feat, platformR - 1.5f, r1, last, last + dA * 1.5f, lp.BaseY + Ht, 0.9f, stepA, Shade(stepA, 0.85f));
                    CrystalCluster(feat + Vector3.up * Ht, 2.2f, 5, ref rng, 0.45f);
                    // Hanging glow orbs under the platform.
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * Mathf.PI * 2f;
                        var p = feat + new Vector3(Mathf.Cos(a) * platformR * 0.8f, Ht - 2.2f, Mathf.Sin(a) * platformR * 0.8f);
                        Ball(Crystal, p, Vector3.one * 0.35f, _p.Crystal[1], _p.Crystal[0], ref rng, 0.05f, 1, false, 0.3f);
                    }
                    Heroes.Add((feat, Ht));
                    break;
                }
                case LandmarkKind.GiantTree:
                {
                    giantTree?.Invoke(Solid, feat, 1f);
                    Heroes.Add((feat, 22f));
                    break;
                }
                case LandmarkKind.Ziggurat:
                {
                    float rt = rng.Range(1.3f, 1.5f);
                    int rings = rng.Range(4, 8);
                    float step = 0.8f;
                    float sqrt3 = Mathf.Sqrt(3f);
                    var colors = _p.HexTop;
                    for (int q = -rings; q <= rings; q++)
                    {
                        for (int r = -rings; r <= rings; r++)
                        {
                            int s = -q - r;
                            int dist = Mathf.Max(Mathf.Abs(q), Mathf.Max(Mathf.Abs(r), Mathf.Abs(s)));
                            if (dist > rings) continue;
                            var off = new Vector3(rt * sqrt3 * (q + r * 0.5f), 0, rt * 1.5f * r);
                            float top = lp.BaseY + (rings - dist + 1) * step;
                            var col = Color.Lerp(colors[dist % colors.Length], StrataAt(top), 0.25f);
                            HexColumn(Plantable, feat + off, rt, top, lp.BaseY - 2f, col);
                        }
                    }
                    var apex = feat + Vector3.up * ((rings + 1) * step);
                    CrystalCluster(apex, 2.8f, 7, ref rng, 0.45f);
                    Heroes.Add((feat, (rings + 1) * step + 6f));
                    break;
                }
            }
        }

        public void Finish(Material solid, Material hex, Material crystal, Material decor, List<Collider> plantable)
        {
            var root = new GameObject("Structures");
            root.transform.SetParent(_root, false);
            if (Solid.VertexCount > 0) Solid.ToGameObject("Solid", root.transform, solid, collider: true);
            if (Plantable.VertexCount > 0) plantable.Add(Plantable.ToGameObject("Tops", root.transform, hex, collider: true).GetComponent<Collider>());
            if (Crystal.VertexCount > 0) Crystal.ToGameObject("Crystals", root.transform, crystal, collider: true);
            if (Decor.VertexCount > 0) Decor.ToGameObject("Floating", root.transform, decor, collider: false);
            if (DecorRock.VertexCount > 0) DecorRock.ToGameObject("Floating rock", root.transform, hex, collider: false);
        }
    }

    public delegate void PrismFloraHook(MeshBuilder mb, Vector3 pos, float size);
}
