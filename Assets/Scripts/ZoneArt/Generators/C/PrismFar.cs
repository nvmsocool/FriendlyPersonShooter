using System;
using UnityEngine;
using static FriendlyPersonShooter.ZoneArt.C.PrismGeo;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// Scenery beyond the zone (no colliders): terraced lands that meet the seam height at the border,
    /// rising into distant mesas, dotted with toy trees, giant crystals and floating islands, fading into fog.
    /// </summary>
    public static class PrismFar
    {
        public static Func<float, float, float> HeightFunction(ZoneRequest zone, PrismTerrain terrain, float waterLevel)
        {
            var o2 = ZoneNoise.Offset(zone.LayerSeed, 502);
            return (wx, wz) =>
            {
                float lx = wx - zone.Origin.x, lz = wz - zone.Origin.z;
                float dx = Mathf.Max(0f, Mathf.Max(-lx, lx - zone.Size));
                float dz = Mathf.Max(0f, Mathf.Max(-lz, lz - zone.Size));
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float border = ZoneEdges.BorderHeight(zone, wx, wz);
                float rise = PrismTerrain.Smooth(0f, 70f, d);
                float e = terrain.Elevation(lx, lz) * (1f + PrismTerrain.Smooth(200f, 900f, d) * 1.5f);
                float mesa = ZoneNoise.Ridged(wx + o2.x, wz + o2.y, 1f / 420f, 3);
                e += ZoneNoise.Terrace(mesa * mesa * 110f, 9f, 0.2f) * PrismTerrain.Smooth(250f, 900f, d);
                float h = border + e * rise;
                if (waterLevel > -900f) h = Mathf.Max(h, waterLevel - 1.1f);
                return h;
            };
        }

        public static void Build(ZoneRequest zone, PrismTerrain terrain, PrismPalette pal, Transform parent, Material mat, Material foliage, Material water, Material crystal,
            Func<Vector3, Vector3, Color> colorAt, float waterLevel, PrismStructures st, System.Collections.Generic.List<Species> species)
        {
            var root = new GameObject("Far lands");
            root.transform.SetParent(parent, false);
            var hf = HeightFunction(zone, terrain, waterLevel);
            var o = zone.Origin;
            Ring(root.transform, mat, hf, colorAt, o, -70f, 320f, 2.5f, 0f, 250f);
            Ring(root.transform, mat, hf, colorAt, o, -500f, 750f, 10f, -70f, 320f);
            Ring(root.transform, mat, hf, colorAt, o, -2200f, 2450f, 50f, -500f, 750f);

            // Toy forests near the zone.
            var rng = ZoneRandom.For(zone.Seed, "far");
            var trees = new MeshBuilder();
            var templates = new MeshBuilder[8];
            for (int i = 0; i < templates.Length; i++)
            {
                var t = new MeshBuilder();
                var c = pal.Foliage[i % pal.Foliage.Length];
                var bark = pal.Bark[i % pal.Bark.Length];
                if (i % 3 == 0)
                {
                    t.Tube(Vector3.zero, Vector3.up * 4f, 0.3f, 0.2f, 4, bark, bark);
                    Ball(t, Vector3.up * 5.5f, new Vector3(2.6f, 2.4f, 2.6f), Shade(c, 0.7f), Color.Lerp(c, Color.white, 0.25f), ref rng, 0.1f, 0, true);
                }
                else if (i % 3 == 1)
                {
                    t.Tube(Vector3.zero, Vector3.up * 1.5f, 0.3f, 0.2f, 4, bark, bark);
                    Cone(t, Vector3.up * 1f, 2.2f, 8f, 6, Shade(c, 0.7f), Color.Lerp(c, Color.white, 0.3f), 0f, 0.2f);
                }
                else
                {
                    for (int k = 0; k < 3; k++) Ball(t, new Vector3(rng.Range(-1.5f, 1.5f), 1.6f + k * 0.5f, rng.Range(-1.5f, 1.5f)), Vector3.one * rng.Range(1.4f, 2.2f), Shade(c, 0.7f), Color.Lerp(c, Color.white, 0.25f), ref rng, 0.1f, 0, true);
                }
                templates[i] = t;
            }
            var near = new System.Collections.Generic.List<MeshBuilder>();
            foreach (var sp in species) if (sp.Layer == Layer.Tree || sp.Type == Archetype.Puffball) near.Add(sp.Variants[0]);
            var oPatch = ZoneNoise.Offset(zone.Seed, 601);
            for (int i = 0; i < 6500; i++)
            {
                float lx = rng.Range(-450f, 700f), lz = rng.Range(-450f, 700f);
                if (lx > -4f && lz > -4f && lx < 254f && lz < 254f) continue;
                float wx = o.x + lx, wz = o.z + lz;
                float dx = Mathf.Max(0f, Mathf.Max(-lx, lx - 250f)), dz = Mathf.Max(0f, Mathf.Max(-lz, lz - 250f));
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (rng.Value() > Mathf.Lerp(1f, 0.25f, d / 500f)) continue;
                float patch = ZoneNoise.Fbm(wx + oPatch.x, wz + oPatch.y, 1f / 70f, 2);
                if (patch < (d < 120f ? -0.15f : 0.05f)) continue;
                float h = hf(wx, wz);
                if (waterLevel > -900f && h < waterLevel + 0.3f) continue;
                float slope = Mathf.Abs(hf(wx + 3f, wz + 3f) - h);
                if (slope > 1.6f) continue;
                float s = rng.Range(0.7f, 1.5f) * (1f + d / 400f);
                var tpl = d < 120f && near.Count > 0 ? near[rng.Range(0, near.Count)] : templates[rng.Range(0, templates.Length)];
                if (d < 120f) s = rng.Range(0.8f, 1.3f);
                trees.Append(tpl, Matrix4x4.TRS(new Vector3(wx, h - 0.3f, wz), rng.YawOnly(), Vector3.one * s), rng.Jitter(Color.white, 0.02f, 0.1f, 0.1f));
            }
            if (trees.VertexCount > 0) trees.ToGameObject("Far trees", root.transform, foliage, collider: false);

            // Giant distant crystals and floating islands on the skyline.
            var big = new MeshBuilder();
            var bigCrystal = new MeshBuilder();
            int giants = rng.Range(4, 8);
            for (int i = 0; i < giants; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), dist = rng.Range(420f, 1100f);
                float wx = zone.Center.x + Mathf.Cos(a) * dist, wz = zone.Center.z + Mathf.Sin(a) * dist;
                float h = hf(wx, wz);
                float size = rng.Range(12f, 30f);
                var ca = pal.Crystal[i % pal.Crystal.Length];
                for (int k = 0; k < 5; k++)
                {
                    float tilt = k == 0 ? rng.Range(0f, 8f) : rng.Range(15f, 40f);
                    var axis = Quaternion.Euler(0, rng.Range(0f, 360f), 0) * Quaternion.Euler(tilt, 0, 0) * Vector3.up;
                    float s = k == 0 ? 1f : rng.Range(0.35f, 0.65f);
                    Prism(bigCrystal, new Vector3(wx, h - 5f, wz), axis, size * 0.25f * s, size * 3.5f * s, 6, size * 0.5f * s, Shade(ca, 0.7f), Color.Lerp(ca, Color.white, 0.2f), rng.Range(0f, 1f), 0.6f);
                }
            }
            int isles = rng.Range(5, 10);
            for (int i = 0; i < isles; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), dist = rng.Range(300f, 900f);
                float wx = zone.Center.x + Mathf.Cos(a) * dist, wz = zone.Center.z + Mathf.Sin(a) * dist;
                float y = hf(wx, wz) + rng.Range(60f, 160f);
                float r = rng.Range(15f, 40f);
                var rock = st.StrataAt(y);
                Cone(big, new Vector3(wx, y, wz), r, -r * rng.Range(1.3f, 2.2f), rng.Range(6, 10), Shade(rock, 0.9f), Shade(st.StrataAt(y - 10f), 0.7f), rng.Range(0f, 6f));
                var grass = pal.Grass[i % pal.Grass.Length];
                for (int k = 0; k < 9; k++)
                {
                    float a0 = k / 9f * Mathf.PI * 2f, a1 = (k + 1) / 9f * Mathf.PI * 2f;
                    big.FlatTriangle(new Vector3(wx, y + 1f, wz), new Vector3(wx + Mathf.Cos(a1) * r, y + 0.1f, wz + Mathf.Sin(a1) * r), new Vector3(wx + Mathf.Cos(a0) * r, y + 0.1f, wz + Mathf.Sin(a0) * r), grass);
                }
                for (int k = 0; k < 4; k++)
                {
                    var t = templates[rng.Range(0, templates.Length)];
                    big.Append(t, Matrix4x4.TRS(new Vector3(wx + rng.Range(-0.5f, 0.5f) * r, y, wz + rng.Range(-0.5f, 0.5f) * r), rng.YawOnly(), Vector3.one * rng.Range(1.5f, 2.5f)), Color.white);
                }
            }
            big.ToGameObject("Skyline", root.transform, mat, collider: false);
            bigCrystal.ToGameObject("Skyline crystals", root.transform, crystal, collider: false);

            if (waterLevel > -900f)
            {
                var w = new MeshBuilder();
                float y = waterLevel;
                var col = new Color(0.6f, 0f, 0f, 1f);
                float a0 = -2200f, a1 = 2450f;
                // Four big quads around the zone (the zone has its own water).
                void Q(float x0, float z0, float x1, float z1)
                {
                    int i = w.Vertex(new Vector3(o.x + x0, y, o.z + z0), col, Vector3.up);
                    w.Vertex(new Vector3(o.x + x0, y, o.z + z1), col, Vector3.up);
                    w.Vertex(new Vector3(o.x + x1, y, o.z + z1), col, Vector3.up);
                    w.Vertex(new Vector3(o.x + x1, y, o.z + z0), col, Vector3.up);
                    w.Quad(i, i + 1, i + 2, i + 3);
                }
                Q(a0, a0, a1, 0f);
                Q(a0, 250f, a1, a1);
                Q(a0, 0f, 0f, 250f);
                Q(250f, 0f, a1, 250f);
                var go = w.ToGameObject("Far water", root.transform, water, collider: false, castShadows: false);
                _ = go;
            }
        }

        private static void Ring(Transform parent, Material mat, Func<float, float, float> hf, Func<Vector3, Vector3, Color> colorAt, Vector3 o,
            float min, float max, float cell, float holeMin, float holeMax)
        {
            int n = Mathf.RoundToInt((max - min) / cell);
            var mb = new MeshBuilder();
            var heights = new float[(n + 1) * (n + 1)];
            for (int z = 0; z <= n; z++)
            {
                for (int x = 0; x <= n; x++) heights[z * (n + 1) + x] = hf(o.x + min + x * cell, o.z + min + z * cell);
            }
            int w = n + 1;
            // Flat-shaded facets (like the zone itself), colored per face so cliffs and grass stay crisp.
            Vector3 P(int x, int z) => new Vector3(o.x + min + x * cell, heights[z * w + x], o.z + min + z * cell);
            void T(Vector3 a, Vector3 b, Vector3 c)
            {
                var nrm = Vector3.Cross(b - a, c - a).normalized;
                mb.FlatTriangle(a, b, c, colorAt((a + b + c) / 3f, nrm));
            }
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float lx0 = min + x * cell, lz0 = min + z * cell;
                    if (lx0 >= holeMin - 0.01f && lx0 + cell <= holeMax + 0.01f && lz0 >= holeMin - 0.01f && lz0 + cell <= holeMax + 0.01f) continue;
                    Vector3 a = P(x, z), bb = P(x, z + 1), c = P(x + 1, z + 1), d = P(x + 1, z);
                    if (((x + z) & 1) == 0)
                    {
                        T(a, bb, c);
                        T(a, c, d);
                    }
                    else
                    {
                        T(a, bb, d);
                        T(bb, c, d);
                    }
                }
            }
            mb.ToGameObject($"Far {cell}", parent, mat, collider: false);
        }

        /// <summary>Big puffy 3D clouds drifting around (never between the aerial camera and the zone).</summary>
        public static void Clouds(ZoneRequest zone, PrismPalette pal, Transform parent, Material mat)
        {
            var rng = ZoneRandom.For(zone.Seed, "clouds");
            var mb = new MeshBuilder();
            int count = rng.Range(10, 22);
            var lit = WithAlpha(Color.Lerp(pal.CloudLit, Color.white, 0.2f), 0.75f);
            var shade = WithAlpha(Color.Lerp(pal.CloudShade, pal.CloudLit, 0.25f), 0.85f);
            int placed = 0;
            for (int tries = 0; tries < 200 && placed < count; tries++)
            {
                float a = rng.Range(0f, 360f);
                // Keep the south-west sector (towards the aerial camera) clear.
                float da = Mathf.DeltaAngle(a, 225f);
                if (Mathf.Abs(da) < 75f) continue;
                float dist = rng.Range(230f, 900f);
                float y = rng.Range(55f, 150f) + dist * 0.06f;
                var c = zone.Center + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * dist, y, Mathf.Sin(a * Mathf.Deg2Rad) * dist);
                float size = rng.Range(14f, 32f) * (1f + dist / 900f);
                int puffs = rng.Range(5, 11);
                var stretch = Quaternion.Euler(0, rng.Range(0f, 360f), 0);
                for (int i = 0; i < puffs; i++)
                {
                    float t = puffs == 1 ? 0f : i / (float)(puffs - 1) - 0.5f;
                    var off = stretch * new Vector3(t * size * 2.2f, 0, rng.Range(-0.35f, 0.35f) * size);
                    float r = size * rng.Range(0.45f, 0.8f) * (1f - Mathf.Abs(t) * 0.8f);
                    off.y = r * rng.Range(0.1f, 0.5f);
                    Ball(mb, c + off, new Vector3(r, r * rng.Range(0.6f, 0.8f), r), shade, lit, ref rng, 0.05f, 2, false);
                }
                placed++;
            }
            if (mb.VertexCount > 0) mb.ToGameObject("Clouds", parent, mat, collider: false, castShadows: false);
        }

        private static void Tri(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Func<Vector3, Vector3, Color> colorAt)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            mb.FlatTriangle(a, b, c, colorAt((a + b + c) / 3f, n));
        }
    }
}
