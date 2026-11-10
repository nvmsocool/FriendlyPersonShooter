using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;
using static FriendlyPersonShooter.ZoneArt.C.PrismGeo;

namespace FriendlyPersonShooter.ZoneArt.C
{
    public enum Archetype
    {
        Lollipop, ConeTree, Palm, CloudTree, Willow, GiantMushroom, Bamboo, Cactus,
        Puffball, Succulent, Mushroom, OrbPlant, Fern, Flowers, Grass, Reeds,
        // Added later: append only (the value seeds each species' random stream).
        SpiralBloom, JellyCap, ShelfSpire, BulbPod, CrystalTree, GlowCoral, Anemone,
    }

    public enum Layer { Tree, Shrub, Flower, Grass, Reed }

    /// <summary>One plant species of a zone: a few template variants and where it likes to grow.</summary>
    public sealed class Species
    {
        public Archetype Type;
        public Layer Layer;
        public readonly List<MeshBuilder> Variants = new List<MeshBuilder>();
        public float Spacing, ScaleMin = 0.8f, ScaleMax = 1.25f, Tilt = 4f;
        public float TrunkR, TrunkH;
        public float HeightPref = 0.5f, HeightWidth = 1f, MoistPref = 0.5f, MoistWidth = 1f;
        public float PatchFreq = 1f / 45f, PatchThr = 0f, Density = 1f;
        public Vector2 PatchOff;
        public bool GroundTint;
        /// <summary>Height of each variant at scale 1 (metres).</summary>
        public float[] VariantHeight;
        public int Placed, Tris;
        public override string ToString() => $"{Type}x{Placed}";
    }

    /// <summary>Procedural plant templates (local space, base at the origin) and ecosystem scattering.</summary>
    public static class PrismFlora
    {
        // ------------------------------------------------------------------ species selection

        public static List<Species> PickSpecies(ZoneRequest zone, PrismPalette pal, bool water)
        {
            var rng = ZoneRandom.For(zone.Seed, "species");
            var list = new List<Species>();
            var trees = new[] { Archetype.Lollipop, Archetype.ConeTree, Archetype.Palm, Archetype.CloudTree, Archetype.Willow, Archetype.GiantMushroom, Archetype.Bamboo, Archetype.Cactus };
            var shrubs = new[] { Archetype.Puffball, Archetype.Succulent, Archetype.Mushroom, Archetype.OrbPlant, Archetype.Fern };
            float[] tw = { 3, 2.5f, 1.5f, 2, 1.5f, 1, 1, 0.6f };
            float[] sw = { 2.5f, 2, 1.2f, 1.2f, 1.5f };
            switch (zone.Tile)
            {
                case TileKind.Forest: tw = new float[] { 3, 3, 1, 3, 2.5f, 1.5f, 2, 0.2f }; sw = new float[] { 3, 1, 2, 1, 3 }; break;
                case TileKind.Canyon: tw = new float[] { 1.5f, 1.5f, 3, 1.5f, 0.5f, 0.6f, 0.6f, 3 }; sw = new float[] { 1.5f, 3.5f, 0.6f, 1.2f, 0.6f }; break;
                case TileKind.Lake:
                case TileKind.River: tw = new float[] { 2.5f, 1.5f, 3, 1.5f, 3, 0.8f, 2, 0.3f }; sw = new float[] { 2.5f, 1, 1.2f, 1.5f, 2.5f }; break;
                case TileKind.Crater: tw = new float[] { 1.5f, 1.2f, 0.8f, 1.2f, 1, 3.5f, 0.8f, 1.2f }; sw = new float[] { 1.5f, 2, 3, 3, 1 }; break;
                case TileKind.Meadow: tw = new float[] { 3.5f, 2, 1.2f, 2.5f, 1.5f, 0.6f, 0.8f, 0.4f }; sw = new float[] { 3, 1.5f, 1.2f, 1.5f, 1.5f }; break;
            }
            switch (pal.Biome)
            {
                case PrismBiome.Wonderwood:
                    trees = new[] { Archetype.SpiralBloom, Archetype.JellyCap, Archetype.ShelfSpire, Archetype.GiantMushroom, Archetype.Willow, Archetype.CloudTree };
                    tw = new float[] { 8, 3, 2.5f, 2, 1.5f, 1 };
                    shrubs = new[] { Archetype.BulbPod, Archetype.Puffball, Archetype.Mushroom, Archetype.Fern, Archetype.OrbPlant };
                    sw = new float[] { 3, 2, 2, 1.5f, 1.5f };
                    break;
                case PrismBiome.Glowdeep:
                    trees = new[] { Archetype.CrystalTree, Archetype.JellyCap, Archetype.GiantMushroom, Archetype.Willow, Archetype.SpiralBloom, Archetype.CloudTree };
                    tw = new float[] { 8, 3, 2.5f, 1.5f, 0.8f, 1 };
                    shrubs = new[] { Archetype.GlowCoral, Archetype.Anemone, Archetype.OrbPlant, Archetype.Mushroom, Archetype.Succulent };
                    sw = new float[] { 3, 2.5f, 2, 2, 1 };
                    break;
            }
            int treeCount = zone.Tile == TileKind.Forest ? 5 : rng.Range(3, 5);
            int shrubCount = rng.Range(3, 5);
            var colors = new List<Color>(pal.Foliage);
            int colorIndex = rng.Range(0, colors.Count);
            Color NextColor()
            {
                var c = colors[colorIndex % colors.Count];
                colorIndex++;
                return c;
            }

            var pickedT = PickDistinct(tw, treeCount, ref rng);
            foreach (int i in pickedT) list.Add(Make(trees[i], NextColor(), pal, ref rng));
            var pickedS = PickDistinct(sw, shrubCount, ref rng);
            foreach (int i in pickedS) list.Add(Make(shrubs[i], NextColor(), pal, ref rng));
            int flowerKinds = rng.Range(2, 4);
            for (int i = 0; i < flowerKinds; i++) list.Add(Make(Archetype.Flowers, pal.Flowers[(i * 2 + rng.Range(0, 2)) % pal.Flowers.Length], pal, ref rng));
            list.Add(Make(Archetype.Grass, Color.white, pal, ref rng));
            if (water) list.Add(Make(Archetype.Reeds, NextColor(), pal, ref rng));
            foreach (var s in list)
            {
                s.VariantHeight = new float[s.Variants.Count];
                for (int v = 0; v < s.Variants.Count; v++)
                {
                    float top = 0f;
                    foreach (var vert in s.Variants[v].Vertices) top = Mathf.Max(top, vert.y);
                    s.VariantHeight[v] = top;
                }
            }

            // Niches: each species gets its own patch field and preferred height and moisture.
            foreach (var s in list)
            {
                s.PatchOff = new Vector2(rng.Range(-4000f, 4000f), rng.Range(-4000f, 4000f));
                s.HeightPref = rng.Range(0.1f, 0.9f);
                s.HeightWidth = rng.Range(0.35f, 0.7f);
                s.MoistPref = rng.Range(0.2f, 0.8f);
                s.MoistWidth = rng.Range(0.35f, 0.7f);
                s.PatchFreq = 1f / rng.Range(30f, 60f);
                s.PatchThr = s.Layer == Layer.Tree ? rng.Range(-0.25f, 0.02f) : rng.Range(-0.15f, 0.12f);
            }
            return list;
        }

        private static List<int> PickDistinct(float[] weights, int count, ref ZoneRandom rng)
        {
            var w = (float[])weights.Clone();
            var res = new List<int>();
            for (int k = 0; k < count; k++)
            {
                int i = rng.Weighted(w);
                if (w[i] <= 0f) break;
                res.Add(i);
                w[i] = 0f;
            }
            return res;
        }

        public static Species Make(Archetype type, Color main, PrismPalette pal, ref ZoneRandom rng, int variants = 3)
        {
            var s = new Species { Type = type };
            var accent = pal.Flowers[rng.Range(0, pal.Flowers.Length)];
            var second = pal.Foliage[rng.Range(0, pal.Foliage.Length)];
            var bark = pal.Bark[rng.Range(0, pal.Bark.Length)];
            switch (type)
            {
                case Archetype.Lollipop: s.Layer = Layer.Tree; s.Spacing = 7f; s.TrunkR = 0.3f; s.TrunkH = 4f; break;
                case Archetype.ConeTree: s.Layer = Layer.Tree; s.Spacing = 6f; s.TrunkR = 0.3f; s.TrunkH = 3f; break;
                case Archetype.Palm: s.Layer = Layer.Tree; s.Spacing = 8f; s.TrunkR = 0.25f; s.TrunkH = 5f; break;
                case Archetype.CloudTree: s.Layer = Layer.Tree; s.Spacing = 8f; s.TrunkR = 0.35f; s.TrunkH = 4f; break;
                case Archetype.Willow: s.Layer = Layer.Tree; s.Spacing = 9f; s.TrunkR = 0.4f; s.TrunkH = 3.5f; break;
                case Archetype.GiantMushroom: s.Layer = Layer.Tree; s.Spacing = 9f; s.TrunkR = 0.6f; s.TrunkH = 5f; break;
                case Archetype.Bamboo: s.Layer = Layer.Tree; s.Spacing = 6f; s.TrunkR = 0.45f; s.TrunkH = 4f; break;
                case Archetype.Cactus: s.Layer = Layer.Tree; s.Spacing = 6f; s.TrunkR = 0.4f; s.TrunkH = 2.5f; break;
                case Archetype.Puffball: s.Layer = Layer.Shrub; s.Spacing = 4.4f; s.Tilt = 8f; s.ScaleMin = 1.1f; s.ScaleMax = 1.5f; break;
                case Archetype.Succulent: s.Layer = Layer.Shrub; s.Spacing = 4.2f; s.Tilt = 10f; s.ScaleMin = 1.15f; s.ScaleMax = 1.6f; break;
                case Archetype.Mushroom: s.Layer = Layer.Shrub; s.Spacing = 4.4f; s.Tilt = 10f; s.ScaleMin = 1.15f; s.ScaleMax = 1.6f; break;
                case Archetype.OrbPlant: s.Layer = Layer.Shrub; s.Spacing = 4.6f; s.Tilt = 8f; s.ScaleMin = 1.1f; s.ScaleMax = 1.5f; break;
                case Archetype.Fern: s.Layer = Layer.Shrub; s.Spacing = 4f; s.Tilt = 8f; s.ScaleMin = 1.15f; s.ScaleMax = 1.6f; break;
                case Archetype.Flowers: s.Layer = Layer.Flower; s.Spacing = 3.2f; s.Tilt = 6f; s.ScaleMin = 1.6f; s.ScaleMax = 2.1f; break;
                case Archetype.Grass: s.Layer = Layer.Grass; s.Spacing = 2f; s.Tilt = 6f; s.GroundTint = true; s.ScaleMin = 1.5f; s.ScaleMax = 1.9f; break;
                case Archetype.Reeds: s.Layer = Layer.Reed; s.Spacing = 2.4f; s.Tilt = 5f; s.ScaleMin = 1.05f; s.ScaleMax = 1.35f; break;
                case Archetype.SpiralBloom: s.Layer = Layer.Tree; s.Spacing = 15f; s.TrunkR = 0.95f; s.TrunkH = 8f; s.Tilt = 2f; s.ScaleMin = 0.85f; s.ScaleMax = 1.15f; break;
                case Archetype.JellyCap: s.Layer = Layer.Tree; s.Spacing = 10f; s.TrunkR = 0.4f; s.TrunkH = 5f; s.Tilt = 3f; break;
                case Archetype.ShelfSpire: s.Layer = Layer.Tree; s.Spacing = 10f; s.TrunkR = 0.55f; s.TrunkH = 6f; s.Tilt = 2f; break;
                case Archetype.CrystalTree: s.Layer = Layer.Tree; s.Spacing = 8f; s.TrunkR = 0.4f; s.TrunkH = 4f; s.Tilt = 3f; break;
                case Archetype.BulbPod: s.Layer = Layer.Shrub; s.Spacing = 4.4f; s.Tilt = 8f; s.ScaleMin = 1.1f; s.ScaleMax = 1.5f; break;
                case Archetype.GlowCoral: s.Layer = Layer.Shrub; s.Spacing = 4.2f; s.Tilt = 6f; s.ScaleMin = 1.1f; s.ScaleMax = 1.5f; break;
                case Archetype.Anemone: s.Layer = Layer.Shrub; s.Spacing = 4f; s.Tilt = 6f; s.ScaleMin = 1.1f; s.ScaleMax = 1.5f; break;
            }
            for (int v = 0; v < variants; v++)
            {
                var mb = new MeshBuilder();
                var c = rng.Jitter(main, 0.02f, 0.1f, 0.08f);
                switch (type)
                {
                    case Archetype.Lollipop: Lollipop(mb, ref rng, c, second, bark); break;
                    case Archetype.ConeTree: ConeTree(mb, ref rng, c, bark); break;
                    case Archetype.Palm: Palm(mb, ref rng, c, bark, accent); break;
                    case Archetype.CloudTree: CloudTree(mb, ref rng, c, bark); break;
                    case Archetype.Willow: Willow(mb, ref rng, c, accent, bark); break;
                    case Archetype.GiantMushroom: Mushroom(mb, ref rng, c, accent, true); break;
                    case Archetype.Bamboo: Bamboo(mb, ref rng, c, second); break;
                    case Archetype.Cactus: Cactus(mb, ref rng, c, accent); break;
                    case Archetype.Puffball: Puffball(mb, ref rng, c, second, accent); break;
                    case Archetype.Succulent: Succulent(mb, ref rng, c, accent); break;
                    case Archetype.Mushroom: Mushroom(mb, ref rng, c, accent, false); break;
                    case Archetype.OrbPlant: OrbPlant(mb, ref rng, c, accent); break;
                    case Archetype.Fern: Fern(mb, ref rng, c, second); break;
                    case Archetype.Flowers: FlowerPatch(mb, ref rng, main, pal.Flowers[rng.Range(0, pal.Flowers.Length)], pal.Grass[0]); break;
                    case Archetype.Grass: GrassTuft(mb, ref rng); break;
                    case Archetype.Reeds: Reeds(mb, ref rng, c, accent); break;
                    case Archetype.SpiralBloom: SpiralBloom(mb, ref rng, c, second, accent, bark); break;
                    case Archetype.JellyCap: JellyCap(mb, ref rng, c, accent, bark); break;
                    case Archetype.ShelfSpire: ShelfSpire(mb, ref rng, c, second, accent, bark); break;
                    case Archetype.BulbPod: BulbPod(mb, ref rng, c, accent); break;
                    case Archetype.CrystalTree: CrystalTree(mb, ref rng, c, accent, bark); break;
                    case Archetype.GlowCoral: GlowCoral(mb, ref rng, c, accent); break;
                    case Archetype.Anemone: Anemone(mb, ref rng, c, accent); break;
                }
                // Glowdeep's night: every plant glows a little (the crystal and lantern parts already glow a lot).
                if (pal.Biome == PrismBiome.Glowdeep)
                {
                    float cap = s.Layer == Layer.Grass ? 0.93f : 0.8f;
                    for (int i = 0; i < mb.Colors.Count; i++)
                    {
                        var col = mb.Colors[i];
                        if (col.a > cap) mb.Colors[i] = WithAlpha(col, cap);
                    }
                }
                s.Variants.Add(mb);
            }
            return s;
        }

        // ------------------------------------------------------------------ templates

        private static Color Lite(Color c, float t = 0.25f) => Color.Lerp(c, Color.white, t);

        /// <summary>A single-sided blade (the foliage material renders both sides).</summary>
        public static void Blade1(MeshBuilder mb, Vector3 basePos, Vector3 tip, float width, Color baseColor, Color tipColor)
        {
            var dir = (tip - basePos).normalized;
            var side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.forward).normalized * (width * 0.5f);
            var n = Vector3.Cross(side, dir).normalized;
            if (n.y < 0f) n = -n;
            int i = mb.Vertex(basePos - side, baseColor, n);
            mb.Vertex(basePos + side, baseColor, n);
            mb.Vertex(tip, tipColor, n);
            mb.Triangle(i, i + 1, i + 2);
        }

        /// <summary>A single small triangle facing along <paramref name="normal"/> (berries, spots, dots).</summary>
        public static void Dot(MeshBuilder mb, Vector3 p, Vector3 normal, float size, Color col)
        {
            var side = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized * size;
            var up = Vector3.Cross(side, normal).normalized * size;
            int i = mb.Vertex(p + up, col, normal);
            mb.Vertex(p - up * 0.5f + side * 0.87f, col, normal);
            mb.Vertex(p - up * 0.5f - side * 0.87f, col, normal);
            mb.Triangle(i, i + 1, i + 2);
        }

        private static void Trunk(MeshBuilder mb, Vector3 a, Vector3 ctrl, Vector3 b, float r0, float r1, Color c0, Color c1, int segs = 3, int sides = 6)
        {
            Vector3 prev = a;
            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs;
                var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b;
                float ra = Mathf.Lerp(r0, r1, (i - 1) / (float)segs), rb = Mathf.Lerp(r0, r1, t);
                mb.Tube(prev, p, ra, rb, sides, Color.Lerp(c0, c1, (i - 1) / (float)segs), Color.Lerp(c0, c1, t));
                prev = p;
            }
        }

        public static void Lollipop(MeshBuilder mb, ref ZoneRandom rng, Color c, Color c2, Color bark)
        {
            float th = rng.Range(2.6f, 6f), tr = rng.Range(0.14f, 0.28f);
            var top = new Vector3(rng.Range(-0.6f, 0.6f), th, rng.Range(-0.6f, 0.6f));
            Trunk(mb, Vector3.zero, new Vector3(rng.Range(-0.4f, 0.4f), th * 0.5f, rng.Range(-0.4f, 0.4f)), top, tr, tr * 0.6f, Shade(bark, 0.8f), bark);
            int mode = rng.Range(0, 3);
            bool smooth = rng.Chance(0.35f);
            if (mode == 0)
            {
                float r = rng.Range(1.5f, 2.6f);
                Ball(mb, top + Vector3.up * r * 0.7f, new Vector3(r, r * rng.Range(0.85f, 1.15f), r), Shade(c, 0.7f), Lite(c), ref rng, 0.08f, smooth ? 2 : 1, !smooth);
            }
            else if (mode == 1)
            {
                float r = rng.Range(1.4f, 2.1f);
                var p = top;
                int n = rng.Range(2, 4);
                for (int i = 0; i < n; i++)
                {
                    var cc = i == n - 1 && rng.Chance(0.4f) ? c2 : c;
                    Ball(mb, p + Vector3.up * r * 0.6f, new Vector3(r, r * 0.8f, r), Shade(cc, 0.7f), Lite(cc), ref rng, 0.08f, 1, !smooth);
                    p += Vector3.up * r * 1.2f;
                    r *= 0.72f;
                }
                mb.Tube(top, p, tr * 0.6f, tr * 0.3f, 5, bark, bark);
            }
            else
            {
                int n = rng.Range(3, 6);
                float r0 = rng.Range(1.1f, 1.6f);
                Ball(mb, top + Vector3.up * r0, new Vector3(r0, r0, r0) * 1.1f, Shade(c, 0.7f), Lite(c), ref rng, 0.1f, 1, true);
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f + rng.Range(0f, 0.5f);
                    var off = new Vector3(Mathf.Cos(a), rng.Range(-0.2f, 0.5f), Mathf.Sin(a)) * r0 * 1.05f;
                    float r = r0 * rng.Range(0.6f, 0.85f);
                    var cc = rng.Chance(0.2f) ? c2 : c;
                    Ball(mb, top + Vector3.up * r0 + off, Vector3.one * r, Shade(cc, 0.7f), Lite(cc), ref rng, 0.1f, 1, true);
                }
            }
        }

        public static void ConeTree(MeshBuilder mb, ref ZoneRandom rng, Color c, Color bark)
        {
            if (rng.Chance(0.3f))
            {
                float h = rng.Range(6f, 10f), r = rng.Range(0.8f, 1.3f);
                mb.Tube(Vector3.zero, Vector3.up * 1f, 0.2f, 0.16f, 6, bark, bark);
                Cone(mb, Vector3.up * 0.6f, r, h, rng.Range(6, 9), Shade(c, 0.7f), Lite(c, 0.3f), rng.Range(0f, 6f), 0.2f);
                return;
            }
            float trunk = rng.Range(0.6f, 1.4f);
            int tiers = rng.Range(2, 6);
            float baseR = rng.Range(1.4f, 2.4f);
            float height = rng.Range(4.5f, 8.5f);
            int sides = rng.Range(5, 9);
            mb.Tube(Vector3.zero, Vector3.up * (trunk + 0.5f), 0.22f, 0.16f, 6, Shade(bark, 0.8f), bark);
            float span = height - trunk;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)tiers;
                float y = trunk + f * span * 0.78f;
                float r = baseR * (1f - f * 0.7f);
                float h = span / tiers * 1.7f;
                var tc = (t & 1) == 0 ? c : Color.Lerp(c, Color.white, 0.12f);
                Cone(mb, Vector3.up * y, r, h, sides, Shade(tc, 0.72f), Lite(tc, 0.3f), rng.Range(0f, 6f), 0.15f);
            }
        }

        public static void Palm(MeshBuilder mb, ref ZoneRandom rng, Color c, Color bark, Color accent)
        {
            float h = rng.Range(4.5f, 8.5f);
            float lean = rng.Range(0.6f, 2.4f);
            float la = rng.Range(0f, Mathf.PI * 2f);
            var leanDir = new Vector3(Mathf.Cos(la), 0, Mathf.Sin(la));
            int segs = 7;
            Vector3 prev = Vector3.zero;
            var barkB = Color.Lerp(bark, Color.white, 0.25f);
            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs;
                var p = Vector3.up * h * t + leanDir * lean * t * t;
                mb.Tube(prev, p, Mathf.Lerp(0.26f, 0.14f, (i - 1f) / segs), Mathf.Lerp(0.26f, 0.14f, t), 6, (i & 1) == 0 ? bark : barkB, (i & 1) == 0 ? barkB : bark);
                prev = p;
            }
            var top = prev;
            int fronds = rng.Range(7, 12);
            float L = rng.Range(2.6f, 3.8f);
            float wmax = rng.Range(0.35f, 0.6f);
            for (int f = 0; f < fronds; f++)
            {
                float a = f / (float)fronds * Mathf.PI * 2f + rng.Range(-0.15f, 0.15f);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                float droop = rng.Range(0.3f, 0.6f);
                const int n = 6;
                var pts = new Vector3[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    pts[i] = top + dir * (L * t) + Vector3.up * (Mathf.Sin(t * Mathf.PI * 0.8f) * L * 0.3f - t * t * L * droop);
                }
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    float w0 = wmax * Mathf.Sin(Mathf.PI * Mathf.Pow(t0, 0.7f)) + 0.03f, w1 = wmax * Mathf.Sin(Mathf.PI * Mathf.Pow(t1, 0.7f)) + 0.03f;
                    var c0 = Color.Lerp(Shade(c, 0.7f), Lite(c, 0.2f), t0);
                    var c1 = Color.Lerp(Shade(c, 0.7f), Lite(c, 0.2f), t1);
                    var drop0 = Vector3.down * w0 * 0.35f;
                    var drop1 = Vector3.down * w1 * 0.35f;
                    QuadGrad(mb, pts[i], pts[i + 1], pts[i + 1] + side * w1 + drop1, pts[i] + side * w0 + drop0, c0, c1);
                    QuadGrad(mb, pts[i] - side * w0 + drop0, pts[i + 1] - side * w1 + drop1, pts[i + 1], pts[i], c0, c1);
                }
            }
            if (rng.Chance(0.6f))
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f;
                    Ball(mb, top + new Vector3(Mathf.Cos(a), -0.35f, Mathf.Sin(a)) * 0.3f, Vector3.one * 0.18f, accent, Lite(accent), ref rng, 0.05f, 0, true, 0.6f);
                }
            }
        }

        public static void CloudTree(MeshBuilder mb, ref ZoneRandom rng, Color c, Color bark)
        {
            float h = rng.Range(3.5f, 6.5f);
            var top = new Vector3(rng.Range(-0.8f, 0.8f), h, rng.Range(-0.8f, 0.8f));
            var ctrl = new Vector3(rng.Range(-1.2f, 1.2f), h * 0.5f, rng.Range(-1.2f, 1.2f));
            Trunk(mb, Vector3.zero, ctrl, top, 0.32f, 0.16f, Shade(bark, 0.8f), bark, 4, 6);
            int branches = rng.Range(3, 7);
            for (int i = 0; i < branches; i++)
            {
                float t = rng.Range(0.45f, 0.9f);
                var basePt = (1 - t) * (1 - t) * Vector3.zero + 2 * (1 - t) * t * ctrl + t * t * top;
                float a = rng.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), rng.Range(0.3f, 0.9f), Mathf.Sin(a)).normalized;
                float len = rng.Range(1.2f, 2.6f);
                var end = basePt + dir * len;
                mb.Tube(basePt, end, 0.12f, 0.07f, 5, bark, bark);
                float r = rng.Range(0.9f, 1.5f);
                Ball(mb, end + Vector3.up * r * 0.25f, new Vector3(r, r * rng.Range(0.45f, 0.62f), r), Shade(c, 0.72f), Lite(c, 0.3f), ref rng, 0.1f, 1, true);
            }
            float rt = rng.Range(1.2f, 1.8f);
            Ball(mb, top + Vector3.up * rt * 0.3f, new Vector3(rt, rt * 0.6f, rt), Shade(c, 0.72f), Lite(c, 0.3f), ref rng, 0.1f, 1, true);
        }

        public static void Willow(MeshBuilder mb, ref ZoneRandom rng, Color c, Color accent, Color bark)
        {
            float h = rng.Range(3f, 5f);
            var top = new Vector3(rng.Range(-0.4f, 0.4f), h, rng.Range(-0.4f, 0.4f));
            Trunk(mb, Vector3.zero, new Vector3(rng.Range(-0.6f, 0.6f), h * 0.5f, rng.Range(-0.6f, 0.6f)), top, 0.35f, 0.22f, Shade(bark, 0.8f), bark, 3, 7);
            float R = rng.Range(2.4f, 3.4f), Ry = R * rng.Range(0.55f, 0.75f);
            var cc = top + Vector3.up * Ry * 0.6f;
            Ball(mb, cc, new Vector3(R, Ry, R), Shade(c, 0.72f), Lite(c, 0.25f), ref rng, 0.08f, 2, true);
            int strands = rng.Range(22, 38);
            var tipC = Color.Lerp(c, accent, rng.Range(0.3f, 0.8f));
            for (int i = 0; i < strands; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float rr = R * rng.Range(0.7f, 0.98f);
                var p = cc + new Vector3(Mathf.Cos(a) * rr, -Ry * rng.Range(0.1f, 0.5f), Mathf.Sin(a) * rr);
                float len = rng.Range(1.2f, Mathf.Min(3.2f, p.y - 0.4f));
                var outv = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.25f;
                Blade1(mb, p, p + Vector3.down * len + outv, rng.Range(0.12f, 0.22f), Shade(c, 0.85f), tipC);
            }
        }

        public static void Mushroom(MeshBuilder mb, ref ZoneRandom rng, Color c, Color glow, bool giant)
        {
            int count = giant ? 1 : rng.Range(1, 5);
            var stalkC = Color.Lerp(Color.white, c, 0.15f);
            for (int i = 0; i < count; i++)
            {
                var basePt = i == 0 ? Vector3.zero : new Vector3(rng.Range(-0.7f, 0.7f), 0, rng.Range(-0.7f, 0.7f));
                float s = i == 0 ? 1f : rng.Range(0.45f, 0.8f);
                float h = (giant ? rng.Range(4f, 7.5f) : rng.Range(0.35f, 1.3f)) * s;
                float r = (giant ? rng.Range(0.35f, 0.65f) : rng.Range(0.05f, 0.16f)) * s;
                float rc = (giant ? rng.Range(2.4f, 4.2f) : rng.Range(0.3f, 0.9f)) * s;
                var top = basePt + new Vector3(rng.Range(-0.2f, 0.2f) * h, h, rng.Range(-0.2f, 0.2f) * h);
                Trunk(mb, basePt, (basePt + top) * 0.5f + new Vector3(rng.Range(-0.15f, 0.15f) * h, 0, 0), top, r * 1.2f, r, Shade(stalkC, 0.85f), stalkC, giant ? 3 : 2, 6);
                bool cone = rng.Chance(0.3f);
                if (cone) Cone(mb, top - Vector3.up * rc * 0.15f, rc, rc * rng.Range(0.6f, 1.1f), 9, Shade(c, 0.8f), Lite(c, 0.3f), 0f, 0.1f);
                else Ball(mb, top, new Vector3(rc, rc * rng.Range(0.4f, 0.6f), rc), Shade(c, 0.8f), Lite(c, 0.25f), ref rng, 0.05f, giant ? 2 : 1, true);
                // Glowing gills.
                var g = WithAlpha(glow, 0.35f);
                int k = 10;
                for (int j = 0; j < k; j++)
                {
                    float a0 = j / (float)k * Mathf.PI * 2f, a1 = (j + 1) / (float)k * Mathf.PI * 2f;
                    var p0 = top + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * rc * 0.9f - Vector3.up * 0.02f;
                    var p1 = top + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * rc * 0.9f - Vector3.up * 0.02f;
                    mb.FlatTriangle(top - Vector3.up * rc * 0.05f, p0, p1, g);
                }
                if (!cone && rng.Chance(0.6f))
                {
                    int spots = rng.Range(3, 8);
                    for (int j = 0; j < spots; j++)
                    {
                        float a = rng.Range(0f, Mathf.PI * 2f), e = rng.Range(0.3f, 1.2f);
                        var d = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e) * 0.5f, Mathf.Sin(a) * Mathf.Cos(e));
                        Dot(mb, top + Vector3.Scale(d, new Vector3(rc, rc * 0.55f, rc)) * 1.02f, d.normalized, rc * 0.14f, Color.white);
                    }
                }
            }
        }

        /// <summary>A double-sided petal or leaf: base, two side points and a tip, coloured base to tip.</summary>
        private static void Petal(MeshBuilder mb, Vector3 basePos, Vector3 left, Vector3 tip, Vector3 right, Color baseC, Color midC, Color tipC)
        {
            var n = Vector3.Cross(left - basePos, right - basePos).normalized;
            if (n.y < 0f) n = -n;
            int i = mb.Vertex(basePos, baseC, n);
            mb.Vertex(left, midC, n);
            mb.Vertex(tip, tipC, n);
            mb.Vertex(right, midC, n);
            mb.Triangle(i, i + 1, i + 2);
            mb.Triangle(i, i + 2, i + 3);
        }

        /// <summary>
        /// Wonderwood's giant: three or four strands twisting round each other into a tall trunk, with glowing veins,
        /// crowned by huge petals and hung with lantern pods.
        /// </summary>
        public static void SpiralBloom(MeshBuilder mb, ref ZoneRandom rng, Color c, Color c2, Color glow, Color bark)
        {
            float h = rng.Range(9f, 14f);
            int strands = rng.Range(3, 5);
            float twist = rng.Range(0.6f, 1.1f) * Mathf.PI * 2f * rng.Sign();
            float spread = rng.Range(0.55f, 0.75f);
            var lean = new Vector3(rng.Range(-0.8f, 0.8f), 0, rng.Range(-0.8f, 0.8f));
            var vein = WithAlpha(glow, 0.3f);
            const int segs = 9;
            Vector3 At(int strand, float t)
            {
                float a = strand / (float)strands * Mathf.PI * 2f + twist * t;
                float r = spread * (1f - 0.75f * t) + 0.08f;
                return new Vector3(Mathf.Cos(a) * r, t * h, Mathf.Sin(a) * r) + lean * (t * t);
            }
            for (int s = 0; s < strands; s++)
            {
                var barkS = Color.Lerp(bark, Shade(bark, 1.25f), s / (float)strands);
                for (int i = 0; i < segs; i++)
                {
                    float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                    var p0 = At(s, t0);
                    var p1 = At(s, t1);
                    mb.Tube(p0, p1, Mathf.Lerp(0.42f, 0.2f, t0), Mathf.Lerp(0.42f, 0.2f, t1), 6, Shade(barkS, 0.8f + 0.2f * t0), Shade(barkS, 0.8f + 0.2f * t1));
                    // A thin glowing vein on the outside of each strand.
                    var o0 = new Vector3(p0.x - lean.x * t0 * t0, 0, p0.z - lean.z * t0 * t0).normalized * Mathf.Lerp(0.4f, 0.19f, t0);
                    var o1 = new Vector3(p1.x - lean.x * t1 * t1, 0, p1.z - lean.z * t1 * t1).normalized * Mathf.Lerp(0.4f, 0.19f, t1);
                    mb.Tube(p0 + o0, p1 + o1, 0.06f, 0.05f, 3, vein, vein);
                }
                // Root flare.
                var root = At(s, 0f);
                var outD = new Vector3(root.x, 0, root.z).normalized;
                mb.Tube(root + Vector3.up * 1.2f, root + outD * 1.3f + Vector3.down * 0.3f, 0.3f, 0.12f, 5, bark, Shade(bark, 0.7f));
            }
            var top = At(0, 1f);
            top.x = lean.x;
            top.z = lean.z;
            // Two rings of petals: big outer ones drooping at the tips, smaller inner ones cupped upward.
            int petals = rng.Range(5, 8);
            float P = rng.Range(4f, 6f);
            float rot = rng.Range(0f, Mathf.PI * 2f);
            for (int ring = 0; ring < 2; ring++)
            {
                float len = ring == 0 ? P : P * 0.55f;
                float lift = ring == 0 ? 0.6f : 1.4f;
                float droop = ring == 0 ? -1.2f : 0.9f;
                var col = ring == 0 ? c : c2;
                for (int i = 0; i < petals; i++)
                {
                    float a = rot + (i + ring * 0.5f) / petals * Mathf.PI * 2f + rng.Range(-0.1f, 0.1f);
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var side = new Vector3(-d.z, 0, d.x);
                    float w = len * rng.Range(0.32f, 0.42f);
                    var b0 = top + Vector3.up * (ring * 0.3f);
                    var mid = b0 + d * len * 0.5f + Vector3.up * lift;
                    var tip = b0 + d * len + Vector3.up * (lift + droop);
                    Petal(mb, b0, mid - side * w, tip, mid + side * w, Shade(col, 0.75f), col, Lite(Color.Lerp(col, glow, 0.35f), 0.2f));
                }
            }
            Ball(mb, top + Vector3.up * 0.9f, Vector3.one * rng.Range(0.6f, 0.9f), glow, Lite(glow, 0.4f), ref rng, 0.05f, 1, false, 0.3f);
            int pods = rng.Range(3, 7);
            for (int i = 0; i < pods; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var hang = top + d * P * rng.Range(0.45f, 0.75f) + Vector3.up * 0.2f;
                float drop = rng.Range(0.8f, 2f);
                mb.Tube(hang, hang + Vector3.down * drop, 0.03f, 0.03f, 3, bark, bark);
                Ball(mb, hang + Vector3.down * (drop + 0.25f), new Vector3(0.22f, 0.32f, 0.22f), glow, Lite(glow, 0.5f), ref rng, 0.04f, 1, false, 0.25f);
            }
        }

        /// <summary>A jellyfish mushroom: a translucent bell on a bending stalk, trailing glowing tendrils.</summary>
        public static void JellyCap(MeshBuilder mb, ref ZoneRandom rng, Color c, Color glow, Color bark)
        {
            float h = rng.Range(5f, 8.5f);
            var top = new Vector3(rng.Range(-0.8f, 0.8f), h, rng.Range(-0.8f, 0.8f));
            var stalk = Color.Lerp(bark, c, 0.35f);
            Trunk(mb, Vector3.zero, new Vector3(rng.Range(-0.8f, 0.8f), h * 0.5f, rng.Range(-0.8f, 0.8f)), top, 0.38f, 0.22f, Shade(stalk, 0.8f), stalk, 4, 6);
            float rc = rng.Range(2.1f, 3.1f), ry = rc * rng.Range(0.6f, 0.8f);
            var bell = top + Vector3.up * ry * 0.35f;
            Ball(mb, bell, new Vector3(rc, ry, rc), Shade(c, 0.85f), Lite(c, 0.35f), ref rng, 0.04f, 2, false, 0.7f);
            Ball(mb, bell, new Vector3(rc, ry, rc) * 0.55f, glow, Lite(glow, 0.4f), ref rng, 0.04f, 1, false, 0.25f);
            int tendrils = rng.Range(14, 24);
            var t0 = WithAlpha(Color.Lerp(c, glow, 0.5f), 0.35f);
            var t1 = WithAlpha(Lite(glow, 0.3f), 0.2f);
            for (int i = 0; i < tendrils; i++)
            {
                float a = i / (float)tendrils * Mathf.PI * 2f + rng.Range(-0.1f, 0.1f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var p = bell + d * rc * rng.Range(0.75f, 0.92f) - Vector3.up * ry * 0.3f;
                float len = rng.Range(1.6f, Mathf.Max(1.8f, Mathf.Min(4.5f, p.y - 1.8f)));
                var mid = p + Vector3.down * len * 0.5f + d * rng.Range(-0.3f, 0.3f);
                Blade1(mb, p, mid, 0.16f, t0, t1);
                Blade1(mb, mid, mid + Vector3.down * len * 0.5f - d * rng.Range(0f, 0.3f), 0.12f, t1, t1);
            }
        }

        /// <summary>A column stacked with shelf fungus, alternating sides and colours, the shelf rims glowing.</summary>
        public static void ShelfSpire(MeshBuilder mb, ref ZoneRandom rng, Color c, Color c2, Color glow, Color bark)
        {
            float h = rng.Range(6f, 10f);
            var top = new Vector3(rng.Range(-0.4f, 0.4f), h, rng.Range(-0.4f, 0.4f));
            Trunk(mb, Vector3.zero, new Vector3(rng.Range(-0.5f, 0.5f), h * 0.5f, rng.Range(-0.5f, 0.5f)), top, 0.55f, 0.28f, Shade(bark, 0.8f), bark, 4, 7);
            int shelves = rng.Range(4, 8);
            float a = rng.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < shelves; i++)
            {
                float t = Mathf.Lerp(0.35f, 1f, i / (float)Mathf.Max(1, shelves - 1));
                var at = new Vector3(top.x * t, h * t, top.z * t);
                a += rng.Range(1.8f, 2.8f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float r = Mathf.Lerp(2.2f, 1.1f, t) * rng.Range(0.85f, 1.15f);
                var col = i % 2 == 0 ? c : c2;
                var centre = at + d * r * 0.55f;
                Ball(mb, centre, new Vector3(r, r * 0.16f, r), Shade(col, 0.7f), Lite(col, 0.25f), ref rng, 0.05f, 1, true);
                // Glowing rim along the shelf's outer edge.
                for (int k = -3; k <= 3; k++)
                {
                    float ak = a + k * 0.32f;
                    var e = centre + new Vector3(Mathf.Cos(ak), 0, Mathf.Sin(ak)) * r * 0.97f - Vector3.up * r * 0.06f;
                    Dot(mb, e, new Vector3(Mathf.Cos(ak), 0.3f, Mathf.Sin(ak)).normalized, r * 0.12f, WithAlpha(glow, 0.3f));
                }
            }
            Mushroom(mb, ref rng, c2, glow, false);
            Ball(mb, top + Vector3.up * 0.4f, new Vector3(0.9f, 0.5f, 0.9f), Shade(c, 0.8f), Lite(c, 0.3f), ref rng, 0.05f, 1, true);
        }

        /// <summary>Arching stems that hang glowing lantern bulbs.</summary>
        public static void BulbPod(MeshBuilder mb, ref ZoneRandom rng, Color c, Color glow)
        {
            int stems = rng.Range(3, 7);
            for (int i = 0; i < stems; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float h = rng.Range(0.9f, 1.7f);
                var tip = d * rng.Range(0.5f, 0.9f) + Vector3.up * h * 0.85f;
                Trunk(mb, d * 0.05f, d * 0.1f + Vector3.up * h * 1.15f, tip, 0.04f, 0.025f, Shade(c, 0.7f), c, 3, 4);
                Ball(mb, tip + Vector3.down * 0.22f, new Vector3(0.14f, 0.22f, 0.14f) * rng.Range(0.8f, 1.3f), glow, Lite(glow, 0.5f), ref rng, 0.04f, 1, false, 0.25f);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f + rng.Range(0f, 0.3f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Petal(mb, Vector3.zero, d * 0.3f + new Vector3(-d.z, 0, d.x) * 0.18f + Vector3.up * 0.2f, d * 0.7f + Vector3.up * 0.15f,
                    d * 0.3f - new Vector3(-d.z, 0, d.x) * 0.18f + Vector3.up * 0.2f, Shade(c, 0.6f), c, Lite(c, 0.2f));
            }
        }

        /// <summary>Glowdeep's tree: a faceted crystal trunk branching into prisms with glowing tips.</summary>
        public static void CrystalTree(MeshBuilder mb, ref ZoneRandom rng, Color c, Color glow, Color bark)
        {
            float h = rng.Range(3.5f, 6.5f);
            var top = new Vector3(rng.Range(-0.4f, 0.4f), h, rng.Range(-0.4f, 0.4f));
            var stone = Color.Lerp(bark, c, 0.25f);
            Trunk(mb, Vector3.zero, new Vector3(rng.Range(-0.4f, 0.4f), h * 0.5f, rng.Range(-0.4f, 0.4f)), top, 0.42f, 0.24f, Shade(stone, 0.8f), stone, 3, 5);
            var crystal = WithAlpha(c, 0.6f);
            var crystalTip = WithAlpha(Lite(Color.Lerp(c, glow, 0.4f), 0.3f), 0.35f);
            int branches = rng.Range(4, 8);
            float a0 = rng.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < branches; i++)
            {
                float a = a0 + i / (float)branches * Mathf.PI * 2f + rng.Range(-0.3f, 0.3f);
                float t = rng.Range(0.5f, 1f);
                var from = top * t;
                var d = new Vector3(Mathf.Cos(a), rng.Range(0.5f, 1.3f), Mathf.Sin(a)).normalized;
                float len = rng.Range(1.8f, 3.4f) * (1.2f - 0.4f * t);
                var to = from + d * len;
                mb.Tube(from, to, rng.Range(0.18f, 0.28f), 0.02f, 4, crystal, crystalTip);
                Ball(mb, to, Vector3.one * rng.Range(0.12f, 0.2f), glow, Lite(glow, 0.5f), ref rng, 0.04f, 0, true, 0.2f);
            }
            int spikes = rng.Range(3, 6);
            for (int i = 0; i < spikes; i++)
            {
                var off = new Vector3(rng.Range(-0.3f, 0.3f), 0, rng.Range(-0.3f, 0.3f));
                Cone(mb, top + off, rng.Range(0.25f, 0.45f), rng.Range(1.2f, 2.6f), 5, crystal, crystalTip, rng.Range(0f, 6f));
            }
            Ball(mb, top + Vector3.up * 0.3f, Vector3.one * 0.45f, glow, Lite(glow, 0.5f), ref rng, 0.05f, 1, false, 0.25f);
        }

        /// <summary>A cluster of glowing tubes, open at the top.</summary>
        public static void GlowCoral(MeshBuilder mb, ref ZoneRandom rng, Color c, Color glow)
        {
            int tubes = rng.Range(5, 12);
            for (int i = 0; i < tubes; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float h = rng.Range(0.5f, 1.7f);
                float r = rng.Range(0.07f, 0.14f);
                var b = d * rng.Range(0f, 0.35f);
                var tip = b + d * rng.Range(0.1f, 0.4f) * h + Vector3.up * h;
                mb.Tube(b, tip, r * 0.8f, r, 5, Shade(c, 0.6f), WithAlpha(c, 0.6f));
                Ball(mb, tip, new Vector3(r * 1.3f, r * 0.5f, r * 1.3f), glow, Lite(glow, 0.5f), ref rng, 0.03f, 0, true, 0.2f);
            }
        }

        /// <summary>A low dome with a crown of glowing tentacles.</summary>
        public static void Anemone(MeshBuilder mb, ref ZoneRandom rng, Color c, Color glow)
        {
            float R = rng.Range(0.4f, 0.65f);
            Ball(mb, Vector3.up * R * 0.25f, new Vector3(R, R * 0.6f, R), Shade(c, 0.6f), c, ref rng, 0.06f, 1, true);
            int n = rng.Range(18, 30);
            var tipC = WithAlpha(Lite(glow, 0.3f), 0.2f);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float rr = R * rng.Range(0.1f, 0.75f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var p = d * rr + Vector3.up * R * 0.7f;
                float len = rng.Range(0.5f, 1.1f);
                var tip = p + (d * rng.Range(0.3f, 0.8f) + Vector3.up).normalized * len;
                Blade1(mb, p, tip, 0.09f, WithAlpha(c, 0.7f), tipC);
            }
        }

        public static void Bamboo(MeshBuilder mb, ref ZoneRandom rng, Color c, Color c2)
        {
            int stalks = rng.Range(5, 12);
            var node = Color.Lerp(c, Color.white, 0.4f);
            for (int s = 0; s < stalks; s++)
            {
                var b = new Vector3(rng.Range(-0.7f, 0.7f), 0, rng.Range(-0.7f, 0.7f));
                float h = rng.Range(3f, 7f), r = rng.Range(0.05f, 0.09f);
                var lean = new Vector3(rng.Range(-0.08f, 0.08f), 1, rng.Range(-0.08f, 0.08f)).normalized;
                float segL = rng.Range(0.6f, 0.95f);
                float y = 0;
                while (y < h)
                {
                    float y1 = Mathf.Min(h, y + segL);
                    mb.Tube(b + lean * y, b + lean * (y1 - 0.06f), r, r * 0.95f, 5, Shade(c, 0.8f), c);
                    mb.Tube(b + lean * (y1 - 0.06f), b + lean * y1, r * 1.3f, r * 1.3f, 5, node, node);
                    y = y1;
                }
                var tip = b + lean * h;
                int leaves = rng.Range(3, 6);
                for (int l = 0; l < leaves; l++)
                {
                    float a = rng.Range(0f, Mathf.PI * 2f);
                    var d = new Vector3(Mathf.Cos(a), rng.Range(-0.3f, 0.3f), Mathf.Sin(a));
                    var p = b + lean * (h - rng.Range(0f, 1.2f));
                    Blade1(mb, p, p + d * rng.Range(0.6f, 1.1f), 0.16f, c2, Lite(c2, 0.2f));
                }
            }
        }

        public static void Cactus(MeshBuilder mb, ref ZoneRandom rng, Color c, Color flower)
        {
            float h = rng.Range(1.6f, 4f), r = rng.Range(0.25f, 0.45f);
            mb.Tube(Vector3.zero, Vector3.up * h, r, r * 0.95f, 8, Shade(c, 0.75f), c);
            Ball(mb, Vector3.up * h, new Vector3(r * 0.95f, r * 0.7f, r * 0.95f), c, Lite(c, 0.2f), ref rng, 0.02f, 1, true);
            int arms = rng.Range(0, 4);
            for (int i = 0; i < arms; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float y = h * rng.Range(0.3f, 0.65f);
                float out_ = rng.Range(0.45f, 0.8f), up = rng.Range(0.6f, 1.4f);
                float ar = r * 0.65f;
                var p0 = Vector3.up * y;
                var p1 = p0 + d * out_;
                mb.Tube(p0, p1 + d * ar * 0.5f, ar, ar, 7, c, c);
                mb.Tube(p1, p1 + Vector3.up * up, ar, ar * 0.95f, 7, c, Lite(c, 0.1f));
                Ball(mb, p1 + Vector3.up * up, new Vector3(ar, ar * 0.7f, ar), c, Lite(c, 0.2f), ref rng, 0.02f, 0, true);
            }
            if (rng.Chance(0.7f)) Ball(mb, Vector3.up * (h + r * 0.6f), Vector3.one * r * 0.45f, flower, Lite(flower), ref rng, 0.2f, 0, true, 0.7f);
        }

        public static void Puffball(MeshBuilder mb, ref ZoneRandom rng, Color c, Color c2, Color flower)
        {
            int n = rng.Range(3, 8);
            float scale = rng.Range(0.7f, 1.3f);
            bool dots = rng.Chance(0.45f);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), d = i == 0 ? 0f : rng.Range(0.3f, 0.8f) * scale;
                float r = rng.Range(0.35f, 0.85f) * scale * (i == 0 ? 1.2f : 1f);
                var p = new Vector3(Mathf.Cos(a) * d, r * 0.75f + (i == 0 ? 0.2f : 0f), Mathf.Sin(a) * d);
                var cc = rng.Chance(0.15f) ? c2 : c;
                Ball(mb, p, new Vector3(r, r * 0.9f, r), Shade(cc, 0.72f), Lite(cc, 0.25f), ref rng, 0.12f, r > 0.6f ? 1 : 0, true);
                if (dots)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        var dir = new Vector3(rng.Range(-1f, 1f), rng.Range(0.3f, 1f), rng.Range(-1f, 1f)).normalized;
                        Dot(mb, p + Vector3.Scale(dir, new Vector3(r, r * 0.9f, r)) * 1.04f, dir, 0.09f, WithAlpha(flower, 0.75f));
                    }
                }
            }
        }

        public static void Succulent(MeshBuilder mb, ref ZoneRandom rng, Color c, Color tip)
        {
            int rings = rng.Range(2, 4);
            float L0 = rng.Range(0.6f, 1.5f);
            float wid = rng.Range(0.12f, 0.24f);
            for (int ring = 0; ring < rings; ring++)
            {
                int leaves = rng.Range(7, 14);
                float elev = Mathf.Lerp(70f, 25f, ring / (float)Mathf.Max(1, rings - 1)) * Mathf.Deg2Rad;
                float L = L0 * (1f - ring * 0.2f) * (ring == 0 ? 0.8f : 1f);
                for (int i = 0; i < leaves; i++)
                {
                    float a = i / (float)leaves * Mathf.PI * 2f + ring * 0.4f;
                    var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(elev), Mathf.Sin(elev), Mathf.Sin(a) * Mathf.Cos(elev));
                    var side = Vector3.Cross(Vector3.up, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))).normalized * wid;
                    var b = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.08f + Vector3.up * 0.05f;
                    var t = b + dir * L;
                    var ridge = b + dir * L * 0.4f + Vector3.up * wid * 0.5f;
                    var bc = Shade(c, 0.75f);
                    var tc = Color.Lerp(c, tip, 0.65f);
                    var n1 = Vector3.Cross(ridge - (b - side), t - (b - side)).normalized;
                    int k = mb.Vertex(b - side, bc, n1);
                    mb.Vertex(ridge, c, n1);
                    mb.Vertex(t, tc, n1);
                    mb.Triangle(k, k + 1, k + 2);
                    var n2 = Vector3.Cross(t - (b + side), ridge - (b + side)).normalized;
                    int k2 = mb.Vertex(b + side, bc, n2);
                    mb.Vertex(t, tc, n2);
                    mb.Vertex(ridge, c, n2);
                    mb.Triangle(k2, k2 + 1, k2 + 2);
                }
            }
            if (rng.Chance(0.35f))
            {
                float h = rng.Range(1.4f, 3f);
                mb.Tube(Vector3.zero, Vector3.up * h, 0.05f, 0.03f, 5, c, Lite(c));
                Ball(mb, Vector3.up * h, Vector3.one * rng.Range(0.12f, 0.22f), tip, Lite(tip), ref rng, 0.1f, 1, true, 0.5f);
            }
        }

        public static void OrbPlant(MeshBuilder mb, ref ZoneRandom rng, Color c, Color glow)
        {
            int stalks = rng.Range(3, 8);
            for (int i = 0; i < stalks; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float h = rng.Range(0.8f, 2.3f);
                var top = d * rng.Range(0.2f, 0.7f) * h + Vector3.up * h;
                var ctrl = d * 0.05f + Vector3.up * h * 0.6f;
                Trunk(mb, d * 0.05f, ctrl, top, 0.035f, 0.02f, Shade(c, 0.8f), c, 3, 4);
                Ball(mb, top, Vector3.one * rng.Range(0.09f, 0.2f), glow, Lite(glow, 0.4f), ref rng, 0.05f, 1, false, 0.3f);
            }
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.26f;
                Blade1(mb, Vector3.zero, new Vector3(Mathf.Cos(a) * 0.5f, 0.25f, Mathf.Sin(a) * 0.5f), 0.18f, Shade(c, 0.7f), c);
            }
        }

        public static void Fern(MeshBuilder mb, ref ZoneRandom rng, Color c, Color c2)
        {
            int fronds = rng.Range(6, 12);
            float L = rng.Range(0.8f, 1.6f);
            float w = rng.Range(0.12f, 0.22f);
            for (int f = 0; f < fronds; f++)
            {
                float a = f / (float)fronds * Mathf.PI * 2f + rng.Range(-0.2f, 0.2f);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                const int n = 7;
                float curl = rng.Range(0.5f, 1.4f);
                var pts = new Vector3[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    float ang = t * curl * Mathf.PI * 0.9f;
                    pts[i] = dir * (Mathf.Sin(ang) / Mathf.Max(curl, 0.1f) * L * 0.9f) + Vector3.up * ((1f - Mathf.Cos(ang)) / Mathf.Max(curl, 0.1f) * L * 0.5f + t * L * 0.5f);
                }
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    float w0 = w * (1f - t0 * 0.8f), w1 = w * (1f - t1 * 0.8f);
                    var c0 = Color.Lerp(Shade(c, 0.7f), c, t0);
                    var c1 = Color.Lerp(Shade(c, 0.7f), Color.Lerp(c, c2, 0.4f), t1);
                    QuadGrad(mb, pts[i] - side * w0, pts[i + 1] - side * w1, pts[i + 1] + side * w1, pts[i] + side * w0, c0, c1);
                }
            }
        }

        public static void FlowerPatch(MeshBuilder mb, ref ZoneRandom rng, Color c, Color c2, Color stem)
        {
            int count = rng.Range(2, 5);
            int head = rng.Range(0, 3);
            var stemC = Color.Lerp(stem, new Color(0.2f, 0.5f, 0.25f), 0.4f);
            for (int i = 0; i < count; i++)
            {
                var b = new Vector3(rng.Range(-0.45f, 0.45f), 0, rng.Range(-0.45f, 0.45f));
                float h = rng.Range(0.22f, 0.5f);
                var top = b + new Vector3(rng.Range(-0.08f, 0.08f), h, rng.Range(-0.08f, 0.08f));
                Blade1(mb, b, top, 0.035f, Shade(stemC, 0.7f), stemC);
                var fc = rng.Chance(0.25f) ? c2 : c;
                fc.a = 0.9f;
                switch (head)
                {
                    case 0:
                    {
                        int petals = rng.Range(5, 7);
                        float pl = rng.Range(0.12f, 0.18f);
                        for (int p = 0; p < petals; p++)
                        {
                            float a0 = p / (float)petals * Mathf.PI * 2f;
                            var d0 = new Vector3(Mathf.Cos(a0 - 0.3f), 0.25f, Mathf.Sin(a0 - 0.3f)) * pl;
                            var d1 = new Vector3(Mathf.Cos(a0), 0.35f, Mathf.Sin(a0)) * pl * 1.3f;
                            var d2 = new Vector3(Mathf.Cos(a0 + 0.3f), 0.25f, Mathf.Sin(a0 + 0.3f)) * pl;
                            int k = mb.Vertex(top, fc, Vector3.up);
                            mb.Vertex(top + d0 * 1.2f, fc, Vector3.up);
                            mb.Vertex(top + d2 * 1.2f, Lite(fc, 0.2f), Vector3.up);
                            mb.Triangle(k, k + 1, k + 2);
                        }
                        var cc = new Color(1f, 0.95f, 0.5f, 0.8f);
                        mb.FlatTriangle(top + new Vector3(-0.03f, 0.03f, -0.02f), top + new Vector3(0f, 0.04f, 0.035f), top + new Vector3(0.03f, 0.03f, -0.02f), cc);
                        break;
                    }
                    case 1:
                        Ball(mb, top, Vector3.one * rng.Range(0.09f, 0.14f), fc, Lite(fc, 0.3f), ref rng, 0.1f, 0, true, 0.9f);
                        break;
                    default:
                    {
                        float r = rng.Range(0.06f, 0.1f), ch = rng.Range(0.1f, 0.16f);
                        for (int p = 0; p < 5; p++)
                        {
                            float a0 = p / 5f * Mathf.PI * 2f, a1 = (p + 1) / 5f * Mathf.PI * 2f;
                            var q0 = top + new Vector3(Mathf.Cos(a0) * r, ch, Mathf.Sin(a0) * r);
                            var q1 = top + new Vector3(Mathf.Cos(a1) * r, ch, Mathf.Sin(a1) * r);
                            var qm = top + new Vector3(Mathf.Cos((a0 + a1) * 0.5f) * r * 1.3f, ch * 1.25f, Mathf.Sin((a0 + a1) * 0.5f) * r * 1.3f);
                            mb.FlatTriangle(top, q1, q0, Shade(fc, 0.85f));
                            mb.FlatTriangle(q0, q1, qm, fc);
                        }
                        break;
                    }
                }
            }
            for (int i = 0; i < 3; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                Blade1(mb, Vector3.zero, new Vector3(Mathf.Cos(a) * 0.35f, 0.18f, Mathf.Sin(a) * 0.35f), 0.12f, Shade(stemC, 0.7f), stemC);
            }
        }

        public static void GrassTuft(MeshBuilder mb, ref ZoneRandom rng)
        {
            int blades = rng.Range(4, 7);
            float hmax = rng.Range(0.3f, 0.48f);
            for (int i = 0; i < blades; i++)
            {
                var b = new Vector3(rng.Range(-0.18f, 0.18f), -0.05f, rng.Range(-0.18f, 0.18f));
                float h = rng.Range(0.6f, 1f) * hmax;
                var lean = new Vector3(b.x, 0, b.z).normalized * rng.Range(0.15f, 0.4f) * h;
                Blade1(mb, b, b + Vector3.up * h + lean, rng.Range(0.1f, 0.15f), new Color(0.62f, 0.62f, 0.62f), new Color(1f, 1f, 1f));
            }
        }

        public static void Reeds(MeshBuilder mb, ref ZoneRandom rng, Color c, Color accent)
        {
            int n = rng.Range(7, 15);
            for (int i = 0; i < n; i++)
            {
                var b = new Vector3(rng.Range(-0.35f, 0.35f), -0.1f, rng.Range(-0.35f, 0.35f));
                float h = rng.Range(1.1f, 2.6f);
                var tip = b + new Vector3(rng.Range(-0.25f, 0.25f), h, rng.Range(-0.25f, 0.25f));
                Blade1(mb, b, tip, rng.Range(0.05f, 0.1f), Shade(c, 0.7f), Lite(c, 0.2f));
                if (rng.Chance(0.4f))
                {
                    var d = (tip - b).normalized;
                    mb.Tube(tip - d * 0.45f, tip - d * 0.12f, 0.055f, 0.05f, 5, accent, Lite(accent, 0.1f));
                }
            }
        }

        // ------------------------------------------------------------------ scattering

        public sealed class Context
        {
            public ZoneRequest Zone;
            public PrismTerrain Terrain;
            public PrismPalette Palette;
            /// <summary>Every collider of the zone so far; plants grow only where the top surface is tagged ground.</summary>
            public PrismCollision World;
            public System.Func<Vector3, Color> GroundColor;
            public float LowY, HighY;
            public bool[] Occupied; // 1 m grid, trunks
            public bool[] NoTrees;  // 1 m grid where no trunk may stand (optional)
            public Vector2 HeroEye, HeroTarget; // keep the hero sight line clear of trees
            public float[] Budget = { 480000f, 330000f, 300000f, 600000f, 60000f };
            /// <summary>Noise offset for the clearings (open glades with no trees and few shrubs).</summary>
            public Vector2 ClearOff;
            /// <summary>False on a headless build: placements and trunk colliders only, no plant meshes.</summary>
            public bool Render = true;
        }

        public sealed class Planted
        {
            /// <summary>Plant meshes per 62.5 m chunk (null entries when empty or headless).</summary>
            public PrismMesh[] Chunks;
            /// <summary>Trunk collider geometry (identical in full and headless builds).</summary>
            public MeshBuilder Trunks = new MeshBuilder();
            /// <summary>
            /// Boxes around tree canopies and big shrubs (identical in full and headless builds). They block sight
            /// (enemy aim and enemy bolts), not movement or players' shots.
            /// </summary>
            public readonly List<Bounds> Foliage = new List<Bounds>();
            public string Summary;
        }

        /// <summary>Most shrubs (this share) are scaled down to at most <see cref="ShrubCap"/> tall, below eye height.</summary>
        private const float ShortShrubShare = 0.8f;
        private const float ShrubCap = 1.5f;

        /// <summary>
        /// How much of the full tree and shrub cover a tile kind gets. Forests stay thick; meadows are mostly open,
        /// so zones feel different and most of them let you see a fair way.
        /// </summary>
        private static void Cover(TileKind tile, out float trees, out float shrubs)
        {
            switch (tile)
            {
                case TileKind.Forest: trees = 0.85f; shrubs = 0.8f; break;
                case TileKind.Meadow: trees = 0.25f; shrubs = 0.5f; break;
                case TileKind.Lake:
                case TileKind.River: trees = 0.45f; shrubs = 0.6f; break;
                default: trees = 0.4f; shrubs = 0.55f; break;
            }
        }

        /// <summary>Plants shorter than this don't block sight.</summary>
        private const float FoliageMinHeight = 1.1f;

        /// <summary>
        /// A sight-blocking box for a tree canopy (its upper part) or a big shrub (most of it), a little inside the
        /// plant's outline so shots past its edge aren't blocked by thin air.
        /// </summary>
        private static void AddFoliage(List<Bounds> list, Species sp, Candidate c)
        {
            if (sp.Layer != Layer.Tree && sp.Layer != Layer.Shrub) return;
            var verts = sp.Variants[c.Variant].Vertices;
            if (verts.Count == 0) return;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var v in verts)
            {
                var w = c.M.MultiplyPoint3x4(v);
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }
            float height = max.y - min.y;
            if (height < FoliageMinHeight) return;
            var b = new Bounds((min + max) * 0.5f, max - min);
            float bottom = sp.Layer == Layer.Tree ? min.y + Mathf.Max(sp.TrunkH * c.Scale * 0.8f, height * 0.35f) : min.y + height * 0.1f;
            if (bottom >= max.y - 0.3f) return;
            var size = new Vector3(b.size.x * 0.7f, max.y - bottom, b.size.z * 0.7f);
            list.Add(new Bounds(new Vector3(b.center.x, (bottom + max.y) * 0.5f, b.center.z), size));
        }

        private struct Candidate
        {
            public int Species, Variant, Chunk;
            public Matrix4x4 M;
            public Color Tint;
            public Vector3 Pos;
            public float Scale;
        }

        /// <summary>
        /// Scatters every species (any thread). Trees go first, one species after another, so the understory can avoid
        /// their trunks; every other species is placed row by row in parallel (each row has its own random stream, so
        /// the result never depends on scheduling).
        /// </summary>
        public static Planted Plant(Context ctx, List<Species> species)
        {
            var zone = ctx.Zone;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const int chunks = 4;
            int size = ctx.Terrain.C;
            float chunkSize = size / (float)chunks;
            var oMoist = ZoneNoise.Offset(zone.Seed, 31);
            ctx.ClearOff = ZoneNoise.Offset(zone.Seed, 61);

            // Trees first (stable order by layer, then as picked).
            var order = new List<int>();
            for (int layer = 0; layer <= (int)Layer.Reed; layer++)
                for (int i = 0; i < species.Count; i++)
                    if ((int)species[i].Layer == layer) order.Add(i);

            var perSpecies = new List<Candidate>[species.Count];
            foreach (int si in order)
            {
                if (species[si].Layer != Layer.Tree) continue;
                var list = new List<Candidate>();
                var rng = ZoneRandom.For(ZoneRandom.Hash(zone.Seed, 0x5CA7, si, (int)species[si].Type), "trees");
                int n = Mathf.CeilToInt(size / species[si].Spacing);
                for (int gz = 0; gz < n; gz++) Row(ctx, species, si, gz, ref rng, oMoist, chunkSize, chunks, list);
                perSpecies[si] = list;
            }
            var rest = order.FindAll(si => species[si].Layer != Layer.Tree);
            var rowJobs = new List<(int si, int gz)>();
            foreach (int si in rest)
            {
                int n = Mathf.CeilToInt(size / species[si].Spacing);
                for (int gz = 0; gz < n; gz++) rowJobs.Add((si, gz));
            }
            var rowResults = new List<Candidate>[rowJobs.Count];
            System.Threading.Tasks.Parallel.For(0, rowJobs.Count, j =>
            {
                var (si, gz) = rowJobs[j];
                var rng = ZoneRandom.For(ZoneRandom.Hash(zone.Seed, 0x5CA8, si, gz), (int)species[si].Type);
                var list = new List<Candidate>();
                Row(ctx, species, si, gz, ref rng, oMoist, chunkSize, chunks, list);
                rowResults[j] = list;
            });
            for (int j = 0; j < rowJobs.Count; j++)
            {
                int si = rowJobs[j].si;
                if (perSpecies[si] == null) perSpecies[si] = new List<Candidate>();
                perSpecies[si].AddRange(rowResults[j]);
            }

            double tPlace = clock.Elapsed.TotalMilliseconds;
            var cands = new List<Candidate>();
            var layerTris = new long[5];
            foreach (int si in order)
            {
                foreach (var c in perSpecies[si])
                {
                    cands.Add(c);
                    layerTris[(int)species[si].Layer] += species[si].Variants[c.Variant].TriangleCount;
                }
            }

            // Thin each layer evenly to its triangle budget.
            var keep = new float[5];
            for (int i = 0; i < 5; i++) keep[i] = layerTris[i] > ctx.Budget[i] ? ctx.Budget[i] / layerTris[i] : 1f;
            var thin = ZoneRandom.For(zone.Seed, "thin");
            System.Array.Clear(layerTris, 0, 5);
            var kept = new List<Candidate>(cands.Count);
            var res = new Planted();
            foreach (var c in cands)
            {
                var sp = species[c.Species];
                if (thin.Value() > keep[(int)sp.Layer]) continue;
                kept.Add(c);
                int tris = sp.Variants[c.Variant].TriangleCount;
                layerTris[(int)sp.Layer] += tris;
                sp.Placed++;
                sp.Tris += tris;
                if (sp.TrunkR > 0f) res.Trunks.Tube(c.Pos + Vector3.down * 0.5f, c.Pos + Vector3.up * sp.TrunkH * c.Scale, sp.TrunkR * c.Scale, sp.TrunkR * c.Scale * 0.7f, 6, Color.white, Color.white);
                AddFoliage(res.Foliage, sp, c);
            }

            double tThin = clock.Elapsed.TotalMilliseconds;
            res.Chunks = new PrismMesh[chunks * chunks];
            if (ctx.Render)
            {
                System.Threading.Tasks.Parallel.For(0, chunks * chunks, ci =>
                {
                    int verts = 0;
                    foreach (var c in kept) if (c.Chunk == ci) verts += species[c.Species].Variants[c.Variant].VertexCount;
                    if (verts == 0) return;
                    var pm = new PrismMesh($"Plants {ci}", verts);
                    foreach (var c in kept) if (c.Chunk == ci) pm.Append(species[c.Species].Variants[c.Variant], c.M, c.Tint);
                    pm.FixMissingNormals();
                    res.Chunks[ci] = pm;
                });
            }

            var sb = new System.Text.StringBuilder();
            foreach (var sp in species) sb.Append(sp).Append(' ');
            sb.Append($"(tris T{layerTris[0] / 1000}k S{layerTris[1] / 1000}k F{layerTris[2] / 1000}k G{layerTris[3] / 1000}k; ms place {tPlace:0} thin {tThin - tPlace:0} mesh {clock.Elapsed.TotalMilliseconds - tThin:0})");
            res.Summary = sb.ToString();
            return res;
        }

        /// <summary>One row of one species' jittered grid.</summary>
        private static void Row(Context ctx, List<Species> species, int si, int gz, ref ZoneRandom rng, Vector2 oMoist, float chunkSize, int chunks, List<Candidate> output)
        {
            var zone = ctx.Zone;
            var t = ctx.Terrain;
            var sp = species[si];
            float step = sp.Spacing;
            int size = t.C;
            int n = Mathf.CeilToInt(size / step);
            for (int gx = 0; gx < n; gx++)
            {
                float lx = (gx + rng.Value()) * step, lz = (gz + rng.Value()) * step;
                float roll = rng.Value();
                float wx = zone.Origin.x + lx, wz = zone.Origin.z + lz;
                // Inside the zone, trees far enough in that their crowns stay out of the neighbour.
                if (!zone.Contains(wx, wz, sp.Layer == Layer.Tree ? 3f : 0.5f)) continue;
                float density = Density(ctx, sp, lx, lz, wx, wz, oMoist);
                if (roll > density) continue;
                if (!ctx.World.Down(wx, wz, 600f, out float y, out var normal, out int tag)) continue;
                if (tag != PrismCollision.TagGround) continue;
                if (!Allowed(ctx, sp, lx, lz, y > t.HeightAt(lx, lz) + 2.5f)) continue;
                float minUp = sp.Layer == Layer.Tree ? 0.85f : sp.Layer == Layer.Grass ? 0.6f : 0.72f;
                if (normal.y < minUp) continue;
                if (t.HasWater)
                {
                    if (sp.Layer == Layer.Reed) { if (y < t.WaterLevel - 0.7f || y > t.WaterLevel + 0.5f) continue; }
                    else if (y < t.WaterLevel + 0.05f) continue;
                }
                else if (sp.Layer == Layer.Reed) continue;
                int ox = Mathf.Clamp((int)lx, 0, size - 1), oz = Mathf.Clamp((int)lz, 0, size - 1);
                if (sp.Layer == Layer.Tree)
                {
                    if (ctx.NoTrees != null && ctx.NoTrees[oz * size + ox]) continue;
                    if (NearOccupied(ctx, ox, oz, 2)) continue;
                    ctx.Occupied[oz * size + ox] = true;
                }
                else if (sp.Layer == Layer.Shrub && ctx.Occupied[oz * size + ox]) continue;

                float scale = rng.Range(sp.ScaleMin, sp.ScaleMax);
                if (sp.Layer == Layer.Grass) scale *= Mathf.Lerp(0.8f, 1.3f, density);
                var rot = Quaternion.Euler(rng.Range(-sp.Tilt, sp.Tilt), rng.Range(0f, 360f), rng.Range(-sp.Tilt, sp.Tilt));
                var pos = new Vector3(wx, y, wz);
                Color tint;
                // The same random draws happen with or without rendering, so placements match headless builds.
                if (sp.GroundTint)
                {
                    var g = ctx.Render ? ctx.GroundColor(pos) : Color.white;
                    tint = Color.Lerp(g, Color.white, 0.12f) * 1.05f;
                    tint = rng.Jitter(tint, 0.015f, 0.08f, 0.08f);
                }
                else tint = rng.Jitter(Color.white, 0f, 0f, 0.1f);
                tint.a = 1f;
                int variant = rng.Range(0, sp.Variants.Count);
                float capRoll = rng.Value();
                if (sp.Layer == Layer.Shrub && capRoll < ShortShrubShare && sp.VariantHeight[variant] > 0.1f)
                    scale = Mathf.Min(scale, ShrubCap / sp.VariantHeight[variant]);
                int ci = Mathf.Clamp((int)(lz / chunkSize), 0, chunks - 1) * chunks + Mathf.Clamp((int)(lx / chunkSize), 0, chunks - 1);
                output.Add(new Candidate { Species = si, Variant = variant, Chunk = ci, M = Matrix4x4.TRS(pos, rot, Vector3.one * scale), Tint = tint, Pos = pos, Scale = scale });
            }
        }

        private static bool NearOccupied(Context ctx, int x, int z, int r)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = x + dx, zz = z + dz;
                    int size = ctx.Terrain.C;
                    if (xx < 0 || zz < 0 || xx >= size || zz >= size) continue;
                    if (ctx.Occupied[zz * size + xx]) return true;
                }
            }
            return false;
        }

        private static float Density(Context ctx, Species sp, float lx, float lz, float wx, float wz, Vector2 oMoist)
        {
            var t = ctx.Terrain;
            float h = t.SurfaceAt(lx, lz);
            float hn = Mathf.InverseLerp(ctx.LowY, ctx.HighY, h);
            float moist = 0.5f + 0.6f * ZoneNoise.Fbm(wx + oMoist.x, wz + oMoist.y, 1f / 70f, 3);
            if (t.HasWater) moist += PrismTerrain.Smooth(4f, 0f, h - t.WaterLevel) * 0.5f;
            float patch = ZoneNoise.Fbm(wx + sp.PatchOff.x, wz + sp.PatchOff.y, sp.PatchFreq, 3);
            float pm = PrismTerrain.Smooth(sp.PatchThr - 0.05f, sp.PatchThr + 0.18f, patch);
            float hNiche = Mathf.Exp(-PrismTerrain.Sq((hn - sp.HeightPref) / sp.HeightWidth));
            float mNiche = Mathf.Exp(-PrismTerrain.Sq((moist - sp.MoistPref) / sp.MoistWidth));
            // Clearings: open glades (about a third of the ground) with no trees and few shrubs.
            float clearing = PrismTerrain.Smooth(0.0f, 0.12f, ZoneNoise.Fbm(wx + ctx.ClearOff.x, wz + ctx.ClearOff.y, 1f / 80f, 2));
            Cover(ctx.Zone.Tile, out float treeCover, out float shrubCover);
            PrismBiomes.Cover(PrismBiomes.For(ctx.Zone), ref treeCover, ref shrubCover);
            switch (sp.Layer)
            {
                case Layer.Grass: return Mathf.Clamp01(0.55f + 0.5f * moist) * (0.7f + 0.3f * pm);
                case Layer.Reed: return 0.85f;
                case Layer.Flower: return pm * (0.5f + 0.5f * mNiche) * 0.9f;
                case Layer.Shrub: return pm * hNiche * (0.4f + 0.6f * mNiche) * 0.85f * shrubCover * (1f - 0.8f * clearing);
                default: return pm * hNiche * (0.45f + 0.55f * mNiche) * 1.25f * treeCover * (1f - clearing);
            }
        }

        private static bool Allowed(Context ctx, Species sp, float lx, float lz, bool elevated)
        {
            var t = ctx.Terrain;
            var zone = ctx.Zone;
            var p = new Vector2(zone.Origin.x + lx, zone.Origin.z + lz);
            bool tall = sp.Layer == Layer.Tree || sp.Layer == Layer.Shrub;
            foreach (var a in zone.Anchors)
            {
                float r = a.Kind == ZoneAnchorKind.Shrine ? ZoneRequest.ShrineClearRadius + 0.5f : a.Kind == ZoneAnchorKind.Gate ? ZoneRequest.GateClearRadius + 0.5f : 2.5f;
                // Named places sit in a wide open space so they can be seen (and recognised) from a way off.
                if (tall && a.Kind == ZoneAnchorKind.Landmark) r = 14f;
                else if (tall && a.Kind == ZoneAnchorKind.Shrine) r = 10f;
                else if (tall && a.Kind == ZoneAnchorKind.Gate) r = 12f;
                if (sp.Layer == Layer.Tree) r += 3f;
                if ((new Vector2(a.World.x, a.World.z) - p).sqrMagnitude < r * r) return false;
            }
            // Trails stay open: no trees within 5 m, no shrubs within 3 m, and only a little grass on the trail's edge.
            float trail = t.TrailDistAt(lx, lz);
            float keep = sp.Layer == Layer.Tree ? 5f : sp.Layer == Layer.Shrub ? 3f : sp.Layer == Layer.Flower ? 1.8f : 1.3f;
            if (trail < keep) return false;
            if (sp.Layer != Layer.Grass && sp.Layer != Layer.Flower)
            {
                var ab = ctx.HeroTarget - ctx.HeroEye;
                float len = ab.magnitude;
                float u = Mathf.Clamp(Vector2.Dot(p - ctx.HeroEye, ab) / Mathf.Max(len * len, 1e-3f), 0f, 1f);
                float along = u * len;
                float dist = (ctx.HeroEye + ab * u - p).magnitude;
                float clear = sp.Layer == Layer.Tree ? (along < len - 12f ? 4f + along * 0.12f : 0f) : (along < 12f ? 2.8f : 0f);
                if (dist < clear) return false;
            }
            float ramp = t.RampAt(lx, lz);
            float pad = elevated ? 0f : t.PadAt(lx, lz);
            switch (sp.Layer)
            {
                case Layer.Tree: return ramp < 0.2f && pad < 0.2f;
                case Layer.Shrub: return ramp < 0.3f && pad < 0.6f;
                case Layer.Flower: return ramp < 0.5f;
                default: return ramp < 0.8f;
            }
        }
    }
}
