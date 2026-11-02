using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// "Prismatica": a geometric dreamscape. Terraced plateaus with candy-striped strata, basalt hex-column
    /// fields that step up in walkable increments, towers wrapped in floating spiral stairs, bridges,
    /// landmark set pieces (crystal spires, monolith rings, rainbow arches, floating spirals, giant trees,
    /// hex ziggurats), a dense varied flora of invented species, and a painterly sky with planets and moons.
    /// </summary>
    public sealed class PrismaticaGenerator : IZoneGenerator
    {
        public string Name => "prismatica";

        public ZoneResult Build(ZoneRequest zone, Transform parent)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder();
            int LinksBuilt = 0;
            PrismTerrain.MaxGrad = float.TryParse(System.Environment.GetEnvironmentVariable("PRISM_GRAD"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gEnv) ? gEnv : 0.85f;
            PrismTerrain.Debugging = System.Environment.GetEnvironmentVariable("PRISM_DEBUG") == "1";
            void Mark(string what) { times.Append($"{what} {sw.Elapsed.TotalSeconds:0.0} "); }
            var root = new GameObject($"Prismatica {zone.Coord}");
            root.transform.SetParent(parent, false);
            var result = new ZoneResult { Root = root };
            var pal = PrismPalette.Create(zone);
            var shape = PrismTerrain.ShapeFor(zone);
            var t = new PrismTerrain(zone);
            t.BuildHeights(shape);
            var o = zone.Origin;
            Vector2 L(Vector3 w) => new Vector2(w.x - o.x, w.z - o.z);

            if (t.HasWater)
            {
                for (int i = 0; i < t.H.Length; i++) if (t.H[i] < t.WaterLevel + 0.4f) t.NoHex[i] = 1f;
            }

            // ---------------------------------------------------------------- anchors and plans
            float SafeBase(Vector2 p)
            {
                float h = t.HeightAt(p.x, p.y);
                if (t.HasWater) h = Mathf.Max(h, t.WaterLevel + 0.3f);
                return h;
            }

            foreach (var a in zone.Anchors)
            {
                var p = L(a.World);
                switch (a.Kind)
                {
                    case ZoneAnchorKind.Shrine:
                        t.Flatten(p, ZoneRequest.ShrineClearRadius + 2f, 7f, SafeBase(p), true);
                        t.Protect(p, ZoneRequest.ShrineClearRadius + 2f);
                        if (PrismTerrain.Debugging) Debug.Log($"[Prismatica] {a.Name} at {p.x:0},{p.y:0} base {SafeBase(p):0.0}");
                        break;
                    case ZoneAnchorKind.Gate:
                        t.Flatten(p, ZoneRequest.GateClearRadius + 2.5f, 7f, SafeBase(p), true);
                        t.Protect(p, ZoneRequest.GateClearRadius + 2.5f);
                        break;
                    case ZoneAnchorKind.Spawn:
                        t.Flatten(p, 2f, 4f, SafeBase(p), true, 0.4f);
                        break;
                }
            }

            var prng = ZoneRandom.For(zone.Seed, "plans");
            var landmarks = new List<LandmarkPlan>();
            var usedKinds = new HashSet<LandmarkKind>();
            foreach (var a in zone.Anchors)
            {
                if (a.Kind != ZoneAnchorKind.Landmark) continue;
                var lp = new LandmarkPlan { Local = L(a.World), Name = a.Name, Dir = prng.Range(0f, Mathf.PI * 2f) };
                lp.Kind = PickLandmark(zone.Tile, usedKinds, ref prng);
                usedKinds.Add(lp.Kind);
                var dir = new Vector2(Mathf.Cos(lp.Dir), Mathf.Sin(lp.Dir));
                switch (lp.Kind)
                {
                    case LandmarkKind.CrystalSpire: lp.Feature = lp.Local + dir * 8f; lp.PadR = 9f; break;
                    case LandmarkKind.MonolithRing: lp.Feature = lp.Local; lp.PadR = 15f; break;
                    case LandmarkKind.RainbowArch: lp.Feature = lp.Local; lp.PadR = 14f; break;
                    case LandmarkKind.FloatingSpiral: lp.Feature = lp.Local + dir * 3f; lp.PadR = 10.5f; break;
                    case LandmarkKind.GiantTree: lp.Feature = lp.Local + dir * 7f; lp.PadR = 7f; break;
                    default: lp.Feature = lp.Local; lp.PadR = 21f; break;
                }
                lp.Feature = new Vector2(Mathf.Clamp(lp.Feature.x, 24f, 226f), Mathf.Clamp(lp.Feature.y, 24f, 226f));
                lp.BaseY = SafeBase(lp.Feature);
                t.Flatten(lp.Feature, lp.PadR, 6f, lp.BaseY, true);
                if ((lp.Feature - lp.Local).sqrMagnitude > 1f) t.Flatten(lp.Local, 3f, 4f, t.HeightAt(lp.Local.x, lp.Local.y), true, 0.5f);
                landmarks.Add(lp);
            }

            // Towers with spiral stairs.
            var towers = new List<TowerPlan>();
            int towerCount = zone.Tile switch
            {
                TileKind.Tower => prng.Range(3, 5),
                TileKind.Canyon => prng.Range(1, 3),
                TileKind.Ruins => 1,
                _ => prng.Chance(0.45f) ? 0 : 1,
            };
            for (int attempt = 0; attempt < 200 && towers.Count < towerCount; attempt++)
            {
                float R = zone.Tile == TileKind.Tower ? prng.Range(7f, 11f) : prng.Range(6f, 9.5f);
                var c = new Vector2(prng.Range(38f, 212f), prng.Range(38f, 212f));
                bool ok = true;
                foreach (var a in zone.Anchors) if ((L(a.World) - c).magnitude < R + 18f) ok = false;
                foreach (var lp in landmarks) if ((lp.Feature - c).magnitude < R + lp.PadR + 10f) ok = false;
                foreach (var tw in towers) if ((tw.Local - c).magnitude < R + tw.R + 22f) ok = false;
                if (!ok) continue;
                float baseY = t.HeightAt(c.x, c.y);
                if (t.HasWater && baseY < t.WaterLevel + 0.6f) continue;
                float hgt = zone.Tile == TileKind.Tower ? prng.Range(24f, 42f) : prng.Range(16f, 30f);
                hgt = Mathf.Round(hgt / 0.6f) * 0.6f;
                var toCenter = new Vector2(125f, 125f) - c;
                var plan = new TowerPlan
                {
                    Local = c, R = R, BaseY = baseY, TopY = baseY + hgt,
                    StairAngle = Mathf.Atan2(toCenter.y, toCenter.x), StairDir = prng.Chance(0.5f) ? 1f : -1f,
                    Cantilever = prng.Chance(0.65f),
                };
                plan.CantileverAngle = plan.StairAngle + Mathf.PI + prng.Range(-0.6f, 0.6f);
                t.Flatten(c, R + 4.8f, 5f, baseY, true);
                towers.Add(plan);
            }

            // ---------------------------------------------------------------- hexes and connectivity
            float thr = HexThreshold(zone, shape.HexFrac);
            t.Connect(shape, thr, 12);
            Mark("ground");
            if (System.Environment.GetEnvironmentVariable("PRISM_DEBUG") == "1") t.DebugPng($"D:/Projects/ZoneArtRenders/c/debug/comp_{zone.Seed}.png");

            // ---------------------------------------------------------------- materials
            var ground = Lit("Ground", pal, 0.4f, 0.3f, 0.12f, 0.12f);
            var hexMat = Lit("Hex", pal, 0.35f, 0.3f, 0.08f, 0.3f);
            var solid = Lit("Solid", pal, 0.35f, 0.3f, 0.06f, 0.3f);
            var crystal = Lit("Crystal", pal, 0.5f, 0.1f, 0.03f, 0.3f);
            crystal.SetFloat("_Rim", 1.2f);
            crystal.SetFloat("_Glow", 3f);
            var foliage = Lit("Foliage", pal, 0.6f, 0.25f, 0.06f, 0.5f);
            foliage.SetFloat("_Cull", 0f);
            foliage.SetFloat("_Translucency", 0.45f);
            var farMat = Lit("Far", pal, 0.45f, 0.2f, 0.05f, 0.05f);
            var stBands = ZoneRandom.For(zone.Seed, "bands");
            float bandH = stBands.Range(1.6f, 3.2f), bandOff = stBands.Range(0f, 10f);
            foreach (var m in new[] { ground, hexMat, farMat })
            {
                m.SetFloat("_StrataOn", 1f);
                m.SetFloat("_BandH", bandH);
                m.SetFloat("_BandOffset", bandOff);
                m.SetFloat("_StrataCount", pal.Strata.Length);
                for (int i = 0; i < 6; i++) m.SetColor("_S" + i, pal.Strata[i % pal.Strata.Length]);
            }
            farMat.SetFloat("_StrataWarp", 4f);
            var water = ZoneLook.Material("ZoneArtC/PrismWater", "Water");
            water.SetColor("_Shallow", new Color(pal.Water.r, pal.Water.g, pal.Water.b, 0.62f));
            water.SetColor("_Deep", new Color(pal.WaterDeep.r, pal.WaterDeep.g, pal.WaterDeep.b, 0.9f));
            water.SetColor("_Sky", Color.Lerp(pal.Water, Color.Lerp(pal.SkyHorizon, pal.SkyMid, 0.4f), 0.45f));
            water.SetColor("_Glint", pal.Sun);
            water.SetColor("_SkyH", Color.Lerp(pal.SkyHorizon, pal.Water, 0.3f));
            water.SetColor("_SkyZ", Color.Lerp(pal.SkyMid, pal.SkyZenith, 0.5f));

            // ---------------------------------------------------------------- ground and structures
            var plantable = new List<Collider>();
            var st = new PrismStructures(zone, t, pal, root.transform);
            var oPatch = ZoneNoise.Offset(zone.Seed, 51);
            var oPatch2 = ZoneNoise.Offset(zone.Seed, 52);
            var oPatch3 = ZoneNoise.Offset(zone.Seed, 53);
            Color GrassAt(Vector3 w)
            {
                float n1 = ZoneNoise.Warped(w.x + oPatch.x, w.z + oPatch.y, 1f / 45f, 14f, 3);
                float n2 = ZoneNoise.Fbm(w.x + oPatch2.x, w.z + oPatch2.y, 1f / 15f, 2);
                float n3 = ZoneNoise.Fbm(w.x + oPatch3.x, w.z + oPatch3.y, 1f / 28f, 2);
                var g = Color.Lerp(pal.Grass[0], pal.Grass[1], PrismTerrain.Smooth(-0.05f, 0.2f, n1));
                g = Color.Lerp(g, pal.Grass[2], PrismTerrain.Smooth(0.38f, 0.5f, n3) * 0.85f);
                g = PrismGeo.Shade(g, 1f + n2 * 0.1f);
                return g;
            }
            Color ColorAt(Vector3 w, Vector3 n, bool inZone)
            {
                var g = GrassAt(w);
                var rock = st.StrataAt(w.y + ZoneNoise.Simplex(w.x * 0.05f, w.z * 0.05f) * 0.8f);
                float cliff = PrismTerrain.Smooth(0.8f, 0.6f, n.y);
                var c = Color.Lerp(g, rock, cliff);
                float h = Mathf.Abs(Mathf.Sin(w.x * 12.9898f + w.z * 78.233f) * 43758.55f) % 1f;
                c = PrismGeo.Shade(c, 0.94f + 0.1f * h);
                if (!inZone)
                {
                    float fx = Mathf.Max(0f, Mathf.Max(o.x - w.x, w.x - o.x - 250f)), fz = Mathf.Max(0f, Mathf.Max(o.z - w.z, w.z - o.z - 250f));
                    float fd = Mathf.Sqrt(fx * fx + fz * fz);
                    c = Color.Lerp(c, pal.Fog, PrismTerrain.Smooth(10f, 600f, fd) * 0.55f);
                }
                if (inZone)
                {
                    float lx = w.x - o.x, lz = w.z - o.z;
                    foreach (var (pc, pr) in t.Pads)
                    {
                        float pd = (new Vector2(lx, lz) - pc).magnitude;
                        if (pd > pr + 0.5f || n.y < 0.9f) continue;
                        float ang = Mathf.Atan2(lz - pc.y, lx - pc.x);
                        int ring = Mathf.FloorToInt(pd / 1.6f);
                        int spoke = Mathf.FloorToInt((ang / (Mathf.PI * 2f) + 1f) * (6 + ring * 3));
                        var tile = ((ring + spoke) & 1) == 0 ? pal.HexTop[ring % pal.HexTop.Length] : Color.Lerp(pal.Strata[ring % pal.Strata.Length], pal.Plaza, 0.35f);
                        if (ring == Mathf.FloorToInt(pr / 1.6f)) tile = Color.Lerp(pal.Glow, Color.white, 0.3f);
                        return PrismGeo.Shade(tile, 0.95f + 0.08f * h);
                    }
                    float ramp = t.RampAt(lx, lz);
                    if (ramp > 0.01f) c = Color.Lerp(c, Color.Lerp(pal.Path, rock, 0.15f * (1f - n.y)), PrismTerrain.Smooth(0.3f, 0.8f, ramp) * PrismTerrain.Smooth(0.5f, 0.75f, n.y));
                    float pad = t.PadAt(lx, lz);
                    if (pad > 0.01f) c = Color.Lerp(c, pal.Plaza, PrismTerrain.Smooth(0.4f, 0.9f, pad) * 0.85f * PrismTerrain.Smooth(0.6f, 0.85f, n.y));
                }
                if (t.HasWater)
                {
                    c = Color.Lerp(c, pal.Path, PrismTerrain.Smooth(t.WaterLevel + 0.9f, t.WaterLevel + 0.2f, w.y) * 0.7f);
                    c = Color.Lerp(c, pal.LakeBed, PrismTerrain.Smooth(t.WaterLevel, t.WaterLevel - 0.6f, w.y));
                }
                return c;
            }
            var terrainGo = t.BuildMesh(root.transform, ground, (w, n) => ColorAt(w, n, true), plantable);
            var hexGo = st.HexField(hexMat, plantable);
            Mark("mesh");
            // Check the built ground with the real walk analysis and carve extra ramps where it disagrees with the model.
            for (int pass = 0; pass < (System.Environment.GetEnvironmentVariable("PRISM_REPAIR") == "1" ? 2 : 0); pass++)
            {
                var walk = Walkability.Analyze(zone);
                if (walk.TwoWayShare > 0.99f) break;
                if (t.RepairFromWalk(walk.Map, shape, thr) == 0) break;
                Object.DestroyImmediate(terrainGo);
                Object.DestroyImmediate(hexGo);
                plantable.RemoveAll(c => c == null);
                terrainGo = t.BuildMesh(root.transform, ground, (w, n) => ColorAt(w, n, true), plantable);
                hexGo = st.HexField(hexMat, plantable);
            }
            Mark("repair");
            bool debug = System.Environment.GetEnvironmentVariable("PRISM_DEBUG") == "1";
            if (debug) DebugWalk(zone, t, "D:/Projects/ZoneArtRenders/c/debug/walk_terrain.png");

            var srng = ZoneRandom.For(zone.Seed, "structures");
            foreach (var tw in towers) st.Tower(tw, ref srng);
            for (int i = 0; i < towers.Count; i++)
            {
                for (int j = i + 1; j < towers.Count; j++)
                {
                    if (st.Bridges >= 2) break;
                    if ((towers[i].Local - towers[j].Local).magnitude < 80f) st.Bridge(towers[i], towers[j], ref srng);
                }
            }
            var trng = ZoneRandom.For(zone.Seed, "giant");
            foreach (var lp in landmarks)
            {
                st.Landmark(lp, ref srng, (mb, pos, size) =>
                {
                    var tpl = new MeshBuilder();
                    var c = pal.Foliage[trng.Range(0, pal.Foliage.Length)];
                    var bark = pal.Bark[trng.Range(0, pal.Bark.Length)];
                    if (trng.Chance(0.5f)) PrismFlora.CloudTree(tpl, ref trng, c, bark);
                    else PrismFlora.Lollipop(tpl, ref trng, c, pal.Foliage[trng.Range(0, pal.Foliage.Length)], bark);
                    float s = trng.Range(3.2f, 4.2f);
                    st.Decor.Append(tpl, Matrix4x4.TRS(pos + Vector3.down * 0.3f, trng.YawOnly(), Vector3.one * s), Color.white);
                    PrismGeo.Prism(st.Solid, pos + Vector3.down, Vector3.up, 0.9f, 9f, 8, 0f, bark, bark);
                    for (int k = 0; k < 7; k++)
                    {
                        float a = k / 7f * Mathf.PI * 2f;
                        var mp = new Vector3(pos.x + Mathf.Cos(a) * 4.5f, pos.y, pos.z + Mathf.Sin(a) * 4.5f);
                        var m = new MeshBuilder();
                        PrismFlora.Mushroom(m, ref trng, pal.Flowers[k % pal.Flowers.Length], pal.Crystal[0], false);
                        st.Decor.Append(m, Matrix4x4.TRS(mp, trng.YawOnly(), Vector3.one * 1.4f), Color.white);
                    }
                });
            }

            // Props on open main ground.
            bool OpenGround(Vector2 p, float clear)
            {
                if (p.x < 18f || p.y < 18f || p.x > 232f || p.y > 232f) return false;
                int k = Mathf.Clamp((int)p.y, 0, 249) * 250 + Mathf.Clamp((int)p.x, 0, 249);
                if (t.CellHex[k] >= 0 || t.Comp[k] != t.MainComp) return false;
                if (t.RampAt(p.x, p.y) > 0.05f || t.PadAt(p.x, p.y) > 0.05f) return false;
                if (t.HasWater && t.HeightAt(p.x, p.y) < t.WaterLevel + 0.3f) return false;
                foreach (var a in zone.Anchors) if ((L(a.World) - p).magnitude < clear) return false;
                foreach (var tw in towers) if ((tw.Local - p).magnitude < tw.R + clear) return false;
                foreach (var lp in landmarks) if ((lp.Feature - p).magnitude < lp.PadR + clear) return false;
                return true;
            }
            int slabs = zone.Tile switch { TileKind.Ruins => srng.Range(9, 14), TileKind.Canyon => srng.Range(4, 7), TileKind.Tower => srng.Range(3, 6), _ => srng.Range(2, 5) };
            for (int i = 0, tries = 0; i < slabs && tries < 300; tries++)
            {
                var p = new Vector2(srng.Range(20f, 230f), srng.Range(20f, 230f));
                if (!OpenGround(p, 14f)) continue;
                float h = t.HeightAt(p.x, p.y);
                bool flat = true;
                for (int k = 0; k < 8 && flat; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6f;
                    if (Mathf.Abs(t.SurfaceAt(q.x, q.y) - h) > 1.2f || t.RampAt(q.x, q.y) > 0.05f) flat = false;
                }
                if (!flat) continue;
                st.Slab(p, h, ref srng);
                i++;
            }
            int clusters = zone.Tile switch { TileKind.Crater => srng.Range(14, 20), TileKind.Canyon => srng.Range(6, 10), _ => srng.Range(4, 9) };
            for (int i = 0, tries = 0; i < clusters && tries < 300; tries++)
            {
                Vector2 p = zone.Tile == TileKind.Crater && i < clusters - 4
                    ? shape.Feature + srng.InsideCircle() * shape.FeatureR * 0.8f
                    : new Vector2(srng.Range(20f, 230f), srng.Range(20f, 230f));
                if (p.x < 20f || p.y < 20f || p.x > 230f || p.y > 230f) continue;
                if (t.RampAt(p.x, p.y) > 0.05f || t.PadAt(p.x, p.y) > 0.05f) continue;
                bool near = false;
                foreach (var a in zone.Anchors) if ((L(a.World) - p).magnitude < 10f) near = true;
                foreach (var tw in towers) if ((tw.Local - p).magnitude < tw.R + 6f) near = true;
                if (near) continue;
                float y = t.SurfaceAt(p.x, p.y);
                float size = zone.Tile == TileKind.Crater ? srng.Range(1.2f, 3.6f) : srng.Range(0.8f, 2.4f);
                st.CrystalCluster(new Vector3(o.x + p.x, y, o.z + p.y), size, srng.Range(3, 8), ref srng);
                i++;
            }
            int boulders = srng.Range(15, 35);
            for (int i = 0, tries = 0; i < boulders && tries < 300; tries++)
            {
                var p = new Vector2(srng.Range(20f, 230f), srng.Range(20f, 230f));
                if (!OpenGround(p, 6f)) continue;
                st.Boulder(new Vector3(o.x + p.x, t.HeightAt(p.x, p.y), o.z + p.y), srng.Range(0.7f, 2.4f), ref srng);
                i++;
            }
            int isles = zone.Tile == TileKind.Tower || zone.Tile == TileKind.Crater ? srng.Range(4, 7) : srng.Range(2, 5);
            for (int i = 0; i < isles; i++)
            {
                var p = new Vector2(srng.Range(30f, 220f), srng.Range(30f, 220f));
                float hi = t.SurfaceAt(p.x, p.y);
                for (int k = 0; k < 16; k++)
                {
                    var q = p + srng.InsideCircle() * 22f;
                    hi = Mathf.Max(hi, t.SurfaceAt(Mathf.Clamp(q.x, 0f, 250f), Mathf.Clamp(q.y, 0f, 250f)));
                }
                foreach (var tw in towers) if ((tw.Local - p).magnitude < tw.R + 25f) hi = Mathf.Max(hi, tw.TopY + 6f);
                float y = Mathf.Max(hi + 20f, 30f + srng.Range(5f, 30f));
                st.FloatingIsland(new Vector3(o.x + p.x, y, o.z + p.y), srng.Range(4f, 9f), ref srng, (mb, pos, size) =>
                {
                    var tpl = new MeshBuilder();
                    var c = pal.Foliage[trng.Range(0, pal.Foliage.Length)];
                    PrismFlora.Lollipop(tpl, ref trng, c, c, pal.Bark[0]);
                    mb.Append(tpl, Matrix4x4.TRS(pos, trng.YawOnly(), Vector3.one * trng.Range(0.8f, 1.3f)), Color.white);
                    var tpl2 = new MeshBuilder();
                    PrismFlora.Puffball(tpl2, ref trng, pal.Foliage[trng.Range(0, pal.Foliage.Length)], c, pal.Flowers[0]);
                    mb.Append(tpl2, Matrix4x4.TRS(pos + new Vector3(size * 0.4f, 0, size * 0.2f), trng.YawOnly(), Vector3.one), Color.white);
                });
            }
            st.Finish(solid, hexMat, crystal, foliage, plantable);
            Mark("structures");

            // Any anchor the real walk analysis cannot reach gets a stepped hex causeway to two-way ground.
            {
                var walk = Walkability.Analyze(zone);
                var links = new MeshBuilder();
                var lrng = ZoneRandom.For(zone.Seed, "links");
                foreach (var a in zone.Anchors)
                {
                    if (!walk.UnreachableAnchors.Contains(a.Name)) continue;
                    float startR = a.Kind == ZoneAnchorKind.Shrine ? ZoneRequest.ShrineClearRadius + 1.6f : a.Kind == ZoneAnchorKind.Gate ? ZoneRequest.GateClearRadius + 1.6f : 2.2f;
                    if (BuildLink(L(a.World), startR, walk.Map, links, ref lrng)) LinksBuilt++;
                }
                if (links.VertexCount > 0)
                {
                    var go = links.ToGameObject("Causeways", root.transform, hexMat, collider: true);
                    plantable.Add(go.GetComponent<Collider>());
                    Physics.SyncTransforms();
                }
            }
            Mark("links");
            if (debug) DebugWalk(zone, t, "D:/Projects/ZoneArtRenders/c/debug/walk_structures.png");

            // ---------------------------------------------------------------- water
            if (t.HasWater)
            {
                var w = new MeshBuilder();
                const int cells = 50;
                float cs = 250f / cells;
                for (int z = 0; z <= cells; z++)
                {
                    for (int x = 0; x <= cells; x++)
                    {
                        float lx = x * cs, lz = z * cs;
                        float depth = Mathf.Clamp01((t.WaterLevel - t.HeightAt(lx, lz)) / 1.1f);
                        float shore = PrismTerrain.Smooth(0.35f, 0f, depth);
                        w.Vertex(new Vector3(o.x + lx, t.WaterLevel, o.z + lz), new Color(depth, shore, 0f, 1f), Vector3.up);
                    }
                }
                for (int z = 0; z < cells; z++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        int i = z * (cells + 1) + x;
                        w.Quad(i, i + cells + 1, i + cells + 2, i + 1);
                    }
                }
                w.ToGameObject("Water", root.transform, water, collider: false, castShadows: false);
            }

            Physics.SyncTransforms();

            // ---------------------------------------------------------------- sky, light, post
            var look = ZoneRandom.For(zone.Seed, "look");
            float sunElev = pal.Dusk ? look.Range(16f, 30f) : look.Range(30f, 52f);
            float sunAz = 45f + look.Sign() * look.Range(70f, 115f);
            ZoneLook.Sun(root.transform, sunElev, sunAz, pal.Sun, pal.Dusk ? 1.15f : 1.35f);
            ZoneLook.Ambient(pal.AmbientSky, pal.AmbientEquator, pal.AmbientGround, 1.05f);
            ZoneLook.Fog(pal.Fog, look.Range(0.0011f, 0.0015f));
            ZoneLook.PostProcessing(root.transform, 0.75f, 0.95f, 14f, 14f, 0.22f, pal.Dusk ? 0.15f : 0f);

            // Hero: look at the tallest set piece from open ground, with the planet behind it.
            var hero = PickHero(zone, t, st, ref look);
            result.HeroViews.Add(hero);
            var heroDir = (hero.LookAt - hero.Position);
            heroDir.y = 0;
            heroDir.Normalize();
            SetupSky(zone, pal, heroDir, ref look);

            // ---------------------------------------------------------------- flora
            var species = PrismFlora.PickSpecies(zone, pal, t.HasWater);
            var heights = new List<float>(t.S);
            heights.Sort();
            var ctx = new PrismFlora.Context
            {
                Zone = zone, Terrain = t, Palette = pal, Plantable = new HashSet<Collider>(plantable),
                GroundColor = w => GrassAt(w), LowY = heights[heights.Count / 20], HighY = heights[heights.Count * 19 / 20] + 0.01f,
                Occupied = new bool[250 * 250],
                HeroEye = new Vector2(hero.Position.x, hero.Position.z), HeroTarget = new Vector2(hero.LookAt.x, hero.LookAt.z),
            };
            PrismFlora.Plant(ctx, species, root.transform, foliage, out string florals);
            Mark("flora");

            // ---------------------------------------------------------------- far lands
            PrismFar.Build(zone, t, pal, root.transform, farMat, foliage, water, crystal, (w, n) => ColorAt(w, n, false), t.HasWater ? t.WaterLevel : -999f, st, species);
            var cloudMat = Lit("Cloud", pal, 0.95f, 0.45f, 0.03f, 0.6f);
            cloudMat.SetColor("_ShadowColor", Color.Lerp(pal.CloudShade, pal.SkyMid, 0.3f) * 0.85f);
            cloudMat.SetFloat("_Glow", 1.2f);
            PrismFar.Clouds(zone, pal, root.transform, cloudMat);
            Mark("far");

            bool BuildLink(Vector2 anchor, float startR, byte[] map, MeshBuilder mb, ref ZoneRandom lr)
            {
                float Ground(Vector2 q)
                {
                    var origin = new Vector3(o.x + q.x, 800f, o.z + q.y);
                    return Physics.Raycast(origin, Vector3.down, out var hit, 2000f) ? hit.point.y : t.SurfaceAt(q.x, q.y);
                }
                const float riseMax = 0.45f, spacing = 1.35f, colR = 1.25f;
                float bestCost = float.MaxValue;
                Vector2 bestA = default, bestB = default;
                float bestYa = 0f, bestYb = 0f;
                for (int k = 0; k < 24; k++)
                {
                    float ang = k / 24f * Mathf.PI * 2f;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var pa = anchor + dir * startR;
                    if (pa.x < 3f || pa.y < 3f || pa.x > 247f || pa.y > 247f) continue;
                    float ya = Ground(pa);
                    for (float d = 4f; d < 110f; d += 1.5f)
                    {
                        var pb = pa + dir * d;
                        if (pb.x < 2f || pb.y < 2f || pb.x > 248f || pb.y > 248f) break;
                        int cell = Mathf.Clamp((int)pb.y, 0, 249) * 250 + Mathf.Clamp((int)pb.x, 0, 249);
                        if (map[cell] != 2) continue;
                        float yb = Ground(pb);
                        int steps = Mathf.CeilToInt(d / spacing);
                        if (Mathf.Abs(yb - ya) / steps > riseMax) continue;
                        if (d >= bestCost) break;
                        // Nothing may poke above the walkway along the way.
                        bool clear = true;
                        for (float u = 1.5f; u < d - 1f && clear; u += 1f)
                        {
                            var q = pa + dir * u;
                            float line = Mathf.Lerp(ya, yb, u / d);
                            for (int sd = -1; sd <= 1 && clear; sd++)
                            {
                                var qq = q + new Vector2(-dir.y, dir.x) * (sd * 1.2f);
                                if (Ground(qq) > line + 0.35f) clear = false;
                            }
                        }
                        if (!clear) continue;
                        bestCost = d;
                        bestA = pa;
                        bestB = pb;
                        bestYa = ya;
                        bestYb = yb;
                        break;
                    }
                }
                if (PrismTerrain.Debugging) Debug.Log($"[Prismatica] link from {anchor} r {startR}: {bestA} y {bestYa:0.0} -> {bestB} y {bestYb:0.0} cost {bestCost}");
                if (bestCost == float.MaxValue) return false;
                var ab = bestB - bestA;
                int n = Mathf.CeilToInt(ab.magnitude / spacing);
                for (int i = 0; i <= n; i++)
                {
                    float u = i / (float)n;
                    var q = bestA + ab * u;
                    float top = Mathf.Lerp(bestYa, bestYb, u) + 0.05f;
                    float bottom = Mathf.Min(t.HeightAt(Mathf.Clamp(q.x, 0f, 250f), Mathf.Clamp(q.y, 0f, 250f)), top) - 1.5f;
                    var col = Color.Lerp(pal.HexTop[i % pal.HexTop.Length], pal.Strata[(i / 3) % pal.Strata.Length], 0.35f);
                    st.HexColumn(mb, new Vector3(o.x + q.x, 0f, o.z + q.y), colR, top, bottom, col);
                    t.MarkRamp(q, 2.2f);
                }
                return true;
            }

            result.Notes = $"{pal.Mood}; links {LinksBuilt}; stranded {t.StrandedShare * 100f:0.0}%; t[{times}]; towers {st.Towers}, bridges {st.Bridges}, ramps {t.RampsCarved}, landmarks {string.Join("/", landmarks.ConvertAll(l => l.Kind.ToString()))}; flora {florals}";
            return result;
        }

        private static void DebugWalk(ZoneRequest zone, PrismTerrain t, string path)
        {
            Physics.SyncTransforms();
            var rep = Walkability.Analyze(zone);
            int n0 = rep.MapSize, sc = 3, n = n0 * sc;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++)
            {
                int m = rep.Map[(i / n / sc) * n0 + (i % n) / sc];
                px[i] = m == 2 ? new Color32(60, 200, 90, 255) : m == 1 ? new Color32(230, 170, 40, 255) : new Color32(30, 30, 40, 255);
            }
            var probe = System.Environment.GetEnvironmentVariable("PRISM_PROBE");
            if (!string.IsNullOrEmpty(probe))
            {
                var pp = probe.Split(',');
                var pc = new Vector2(float.Parse(pp[0]), float.Parse(pp[1]));
                foreach (var (a, b) in t.RampSegments)
                {
                    if ((a - pc).magnitude > 16f && (b - pc).magnitude > 16f) continue;
                    var sb = new System.Text.StringBuilder($"[Prismatica] ramp {a} -> {b}: ");
                    float len = (b - a).magnitude;
                    for (float u = 0; u <= len; u += 1f)
                    {
                        var q = Vector2.Lerp(a, b, u / len);
                        var origin = new Vector3(zone.Origin.x + q.x, 800f, zone.Origin.z + q.y);
                        if (Physics.Raycast(origin, Vector3.down, out var hit, 2000f))
                        {
                            int m = rep.Map[Mathf.Clamp((int)q.y, 0, 249) * 250 + Mathf.Clamp((int)q.x, 0, 249)];
                            sb.Append($"{hit.point.y:0.0}/{hit.normal.y:0.00}/{m} ");
                        }
                    }
                    Debug.Log(sb.ToString());
                }
            }
            foreach (var (a, b) in t.RampSegments)
            {
                float len = (b - a).magnitude;
                for (float u = 0; u <= len; u += 0.3f)
                {
                    var p = Vector2.Lerp(a, b, u / Mathf.Max(len, 0.01f)) * sc;
                    int x = (int)p.x, y = (int)p.y;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    px[y * n + x] = u < 3f ? new Color32(30, 60, 255, 255) : new Color32(255, 40, 200, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[Prismatica] {path}: {rep}");
        }

        private static Material Lit(string name, PrismPalette pal, float wrap, float toon, float breakup, float rim)
        {
            var m = ZoneLook.Material("ZoneArtC/PrismLit", name);
            m.SetFloat("_Wrap", wrap);
            m.SetFloat("_Toon", toon);
            m.SetFloat("_Breakup", breakup);
            m.SetFloat("_Rim", rim);
            m.SetColor("_RimColor", pal.Rim);
            m.SetColor("_ShadowColor", pal.ShadowTint);
            m.SetFloat("_Glow", 4f);
            return m;
        }

        private static LandmarkKind PickLandmark(TileKind tile, HashSet<LandmarkKind> used, ref ZoneRandom rng)
        {
            // CrystalSpire, MonolithRing, RainbowArch, FloatingSpiral, GiantTree, Ziggurat
            float[] w = tile switch
            {
                TileKind.Ruins => new float[] { 1, 4, 2, 1, 0.5f, 3 },
                TileKind.Crater => new float[] { 5, 1, 1, 1.5f, 0.5f, 1.5f },
                TileKind.Forest => new float[] { 1, 1.5f, 1, 1, 5, 1 },
                TileKind.Tower => new float[] { 1.5f, 1, 1.5f, 4, 0.5f, 2 },
                TileKind.Canyon => new float[] { 2, 1, 4, 1.5f, 0.5f, 1.5f },
                TileKind.Lake => new float[] { 2.5f, 1, 2, 3, 1.5f, 1 },
                TileKind.River => new float[] { 1.5f, 1, 4, 1.5f, 2, 1 },
                _ => new float[] { 1.5f, 2.5f, 2, 1.5f, 3, 2 },
            };
            if (used.Count < w.Length) foreach (var k in used) w[(int)k] = 0f;
            return (LandmarkKind)rng.Weighted(w);
        }

        private static float HexThreshold(ZoneRequest zone, float frac)
        {
            var oMask = ZoneNoise.Offset(zone.Seed, 21);
            var vals = new List<float>();
            for (int z = 0; z < 50; z++)
            {
                for (int x = 0; x < 50; x++)
                {
                    float wx = zone.Origin.x + 20f + x * 4.2f, wz = zone.Origin.z + 20f + z * 4.2f;
                    vals.Add(ZoneNoise.Fbm(wx + oMask.x, wz + oMask.y, 1f / 65f, 3));
                }
            }
            vals.Sort();
            return vals[Mathf.Clamp((int)((1f - frac) * vals.Count), 0, vals.Count - 1)];
        }

        private static ZoneView PickHero(ZoneRequest zone, PrismTerrain t, PrismStructures st, ref ZoneRandom rng)
        {
            var o = zone.Origin;
            Vector3 target;
            float height;
            if (st.Heroes.Count > 0)
            {
                var w = new float[st.Heroes.Count];
                for (int i = 0; i < w.Length; i++) w[i] = Mathf.Sqrt(Mathf.Max(st.Heroes[i].height, 1f)) * (i < st.Towers ? 0.6f : 1.4f);
                int pick = rng.Weighted(w);
                target = st.Heroes[pick].pos;
                height = st.Heroes[pick].height;
            }
            else
            {
                target = zone.Center + Vector3.up * t.SurfaceAt(125f, 125f);
                height = 10f;
            }
            ZoneView bestView = new ZoneView(target + new Vector3(-50f, 25f, -50f), target + Vector3.up * height * 0.5f, 60f);
            float bestScore = float.MinValue;
            for (int k = 0; k < 64; k++)
            {
                float a = k / 64f * Mathf.PI * 2f;
                float d = Mathf.Max(26f, height * 1.5f + 14f) * rng.Range(0.8f, 1.35f);
                var lp = new Vector2(target.x - o.x + Mathf.Cos(a) * d, target.z - o.z + Mathf.Sin(a) * d);
                if (lp.x < 3f || lp.y < 3f || lp.x > 247f || lp.y > 247f) continue;
                int cell = Mathf.Clamp((int)lp.y, 0, 249) * 250 + Mathf.Clamp((int)lp.x, 0, 249);
                if (t.Comp[cell] != t.MainComp) continue;
                float y = t.SurfaceAt(lp.x, lp.y);
                if (t.HasWater) y = Mathf.Max(y, t.WaterLevel);
                var eye = new Vector3(o.x + lp.x, y + 1.7f, o.z + lp.y);
                var look = target + Vector3.up * height * 0.45f;
                float score = (y - target.y) * 0.4f;
                if (Physics.Linecast(eye, look, out var hit) && (hit.point - look).magnitude > height * 0.6f + 4f) score -= 100f;
                // Openness: penalise walls close around the camera, especially toward the target.
                var dir = (look - eye);
                dir.y = 0f;
                dir.Normalize();
                for (int j = 0; j < 12; j++)
                {
                    float b = j / 12f * Mathf.PI * 2f;
                    var dd = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                    float facing = Mathf.Max(0f, Vector2.Dot(dd, new Vector2(dir.x, dir.z)));
                    var d3 = new Vector3(dd.x, 0f, dd.y);
                    for (int hgt = 0; hgt < 2; hgt++)
                    {
                        if (Physics.Raycast(eye + Vector3.up * (hgt * 1.5f - 0.6f), d3, out var wh, 14f))
                            score -= (2f + 6f * facing) * (14f / Mathf.Max(wh.distance, 2f)) * 0.25f;
                    }
                }
                // Foreground crest check: the ground must stay below a ray 14 degrees under the view centre.
                {
                    var fwd = (look - eye);
                    float fl = fwd.magnitude;
                    float pitch = Mathf.Asin(Mathf.Clamp(fwd.y / Mathf.Max(fl, 0.01f), -1f, 1f)) - 14f * Mathf.Deg2Rad;
                    for (float dd2 = 3f; dd2 < Mathf.Min(fl * 0.7f, 40f); dd2 += 2f)
                    {
                        var q = lp + new Vector2(dir.x, dir.z) * dd2;
                        if (q.x < 0f || q.y < 0f || q.x > 250f || q.y > 250f) break;
                        float lineY = eye.y + Mathf.Tan(pitch) * dd2;
                        if (t.SurfaceAt(q.x, q.y) > lineY) { score -= 6f; }
                    }
                }
                if (RenderSettings.sun != null) score -= Mathf.Abs(Vector3.Dot(dir, RenderSettings.sun.transform.forward)) * 6f;
                score += rng.Range(0f, 3f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestView = new ZoneView(eye, look + Vector3.up * height * 0.15f, 62f);
                }
            }
            return bestView;
        }

        private static void SetupSky(ZoneRequest zone, PrismPalette pal, Vector3 heroDir, ref ZoneRandom rng)
        {
            var sky = ZoneLook.Material("ZoneArtC/PrismSky", "Prismatica sky");
            sky.SetColor("_Zenith", pal.SkyZenith);
            sky.SetColor("_Mid", pal.SkyMid);
            sky.SetColor("_Horizon", pal.SkyHorizon);
            sky.SetColor("_Below", pal.Fog);
            sky.SetFloat("_MidHeight", rng.Range(0.18f, 0.4f));
            sky.SetColor("_SunColor", pal.Sun);
            sky.SetFloat("_SunSize", rng.Range(0.002f, 0.006f));
            sky.SetFloat("_SunHalo", rng.Range(0.6f, 1.3f));
            sky.SetFloat("_SunRays", rng.Range(0.1f, 0.5f));
            sky.SetColor("_CloudLit", pal.CloudLit);
            sky.SetColor("_CloudShade", pal.CloudShade);
            sky.SetColor("_CloudRim", pal.CloudRim);
            sky.SetFloat("_CloudCover", rng.Range(0.5f, 0.62f));
            sky.SetFloat("_CloudScale", rng.Range(0.7f, 1.2f));
            sky.SetVector("_CloudSeed", new Vector4(rng.Range(-50f, 50f), rng.Range(-50f, 50f), rng.Range(0f, 6f), 0));
            sky.SetVector("_Swirl1", new Vector4(rng.Range(-1.5f, 1.5f), rng.Range(-1.5f, 1.5f), rng.Range(0.6f, 1.2f), rng.Sign() * rng.Range(0.9f, 1.8f)));
            sky.SetVector("_Swirl2", new Vector4(rng.Range(-2f, 2f), rng.Range(-2f, 2f), rng.Range(0.5f, 1.0f), rng.Sign() * rng.Range(0.7f, 1.5f)));
            sky.SetFloat("_Streak", rng.Range(0.2f, 0.7f));
            sky.SetColor("_BankBack", pal.BankBack);
            sky.SetColor("_BankMid", pal.BankMid);
            sky.SetColor("_BankFront", pal.BankFront);
            sky.SetFloat("_BankHeight", rng.Range(0.06f, 0.15f));
            sky.SetFloat("_BankSeed", rng.Range(0f, 100f));
            sky.SetFloat("_Stars", pal.Dusk ? rng.Range(0.8f, 1.4f) : rng.Range(0.25f, 0.7f));
            sky.SetFloat("_Aurora", pal.Dusk ? (rng.Chance(0.7f) ? rng.Range(0.5f, 1.2f) : 0f) : (rng.Chance(0.15f) ? rng.Range(0.3f, 0.6f) : 0f));
            sky.SetColor("_AuroraA", pal.AuroraA);
            sky.SetColor("_AuroraB", pal.AuroraB);

            Vector4 Dir(float azDeg, float elevDeg, float radius)
            {
                float a = azDeg * Mathf.Deg2Rad, e = elevDeg * Mathf.Deg2Rad;
                return new Vector4(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a), radius);
            }
            float heroAz = Mathf.Atan2(heroDir.z, heroDir.x) * Mathf.Rad2Deg;
            int skyStyle = rng.Weighted(new[] { 0.5f, 0.25f, 0.25f }); // planet, sky ring, big moons
            if (skyStyle == 1)
            {
                // Ring plane normal tilted 45-65 degrees from vertical, leaning away from the hero view, so the band arcs over the target.
                float tilt = rng.Range(45f, 65f) * Mathf.Deg2Rad;
                float naz = (heroAz + 180f + rng.Range(-30f, 30f)) * Mathf.Deg2Rad;
                var rn = new Vector3(Mathf.Cos(naz) * Mathf.Sin(tilt), Mathf.Cos(tilt), Mathf.Sin(naz) * Mathf.Sin(tilt));
                sky.SetVector("_SkyRing", new Vector4(rn.x, rn.y, rn.z, rng.Range(0.1f, 0.17f)));
                sky.SetColor("_SkyRingCol", Color.Lerp(pal.Ring, Color.white, 0.25f));
            }
            else sky.SetVector("_SkyRing", Vector4.zero);
            if (skyStyle == 0 || (skyStyle == 1 && rng.Chance(0.35f)))
            {
                bool arc = rng.Chance(0.35f);
                float pAz = heroAz + rng.Range(-25f, 25f);
                if (arc) sky.SetVector("_Planet", Dir(pAz, rng.Range(-4f, 6f), rng.Range(0.35f, 0.55f)));
                else sky.SetVector("_Planet", Dir(pAz, rng.Range(14f, 32f), rng.Range(0.1f, 0.22f)));
                sky.SetVector("_PlanetParams", new Vector4(rng.Range(5f, 13f), rng.Range(-0.5f, 0.5f), rng.Range(0.2f, 0.35f), rng.Chance(0.6f) ? 1f : 0f));
            }
            else sky.SetVector("_Planet", Vector4.zero);
            sky.SetColor("_PlanetA", pal.PlanetA);
            sky.SetColor("_PlanetB", pal.PlanetB);
            sky.SetColor("_PlanetC", pal.PlanetC);
            sky.SetColor("_RingColor", pal.Ring);
            int moons = skyStyle == 2 ? 3 : rng.Range(1, 3);
            var names = new[] { "_Moon1", "_Moon2", "_Moon3" };
            for (int i = 0; i < 3; i++)
            {
                if (i < moons)
                {
                    float az = heroAz + 60f + i * rng.Range(70f, 120f);
                    if (skyStyle == 2 && i == 0) az = heroAz + rng.Range(-20f, 20f);
                    float mr = skyStyle == 2 ? (i == 0 ? rng.Range(0.09f, 0.15f) : rng.Range(0.03f, 0.07f)) : rng.Range(0.015f, 0.05f);
                    sky.SetVector(names[i], Dir(az, rng.Range(14f, 45f), mr));
                    sky.SetColor(names[i] + "Col", pal.Moons[i]);
                }
                else sky.SetVector(names[i], Vector4.zero);
            }
            RenderSettings.skybox = sky;
        }
    }
}
