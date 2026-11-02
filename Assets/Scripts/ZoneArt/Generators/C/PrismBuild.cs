using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// One Prismatica zone build. <see cref="Compute"/> does all the shaping, walk checks, placement and mesh data on
    /// worker threads (pure computation, the same on every machine and for any frame timing); <see cref="Run"/> then
    /// turns the data into meshes, colliders and objects in short main-thread steps. Colliders are cooked on worker
    /// threads with <see cref="Physics.BakeMesh"/> before they are assigned. Headless builds skip every render-only
    /// mesh, material and light but compute exactly the same collider data.
    /// </summary>
    internal sealed class PrismBuild
    {
        private const double SliceMs = 4.0;

        private readonly ZoneRequest _zone;
        private readonly Transform _parent;
        private readonly ZoneResult _result;
        private readonly bool _headless;
        private readonly Vector3 _o;

        private PrismPalette _pal;
        private PrismTerrain.Shape _shape;
        private PrismTerrain _t;
        private float _thr;
        private readonly List<LandmarkPlan> _landmarks = new List<LandmarkPlan>();
        private readonly List<TowerPlan> _towers = new List<TowerPlan>();
        private PrismStructures _st;
        private MeshBuilder[] _hexChunks;
        private readonly MeshBuilder _links = new MeshBuilder();
        private int _linksBuilt;
        private readonly List<PrismCollider> _colliders = new List<PrismCollider>();
        private PrismCollision _world;
        private PrismWalk _walk;

        private float _sunElev, _sunAz, _fogDensity;
        private ZoneRandom _look;
        private ZoneView _hero;
        private List<Species> _species;
        private PrismFlora.Planted _flora;

        // Render data (full builds only).
        private readonly List<(PrismMesh mesh, string material, bool shadows, string group)> _render = new List<(PrismMesh, string, bool, string)>();

        private Vector2 _oPatch, _oPatch2, _oPatch3;
        private readonly System.Text.StringBuilder _times = new System.Text.StringBuilder();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastMark;

        public PrismBuild(ZoneRequest zone, Transform parent, ZoneResult result)
        {
            _zone = zone;
            _parent = parent;
            _result = result;
            _headless = zone.Headless;
            _o = zone.Origin;
        }

        private void Mark(string what)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            _times.Append($"{what} {now - _lastMark:0} ");
            _lastMark = now;
        }

        private Vector2 L(Vector3 w) => new Vector2(w.x - _o.x, w.z - _o.z);

        // ================================================================== main thread

        // Main-thread step bookkeeping: the longest stretch between two yields, and what it was doing.
        private readonly Stopwatch _step = new Stopwatch();
        private double _longestStep, _mainMs;
        private string _longestLabel = "";

        private void Lap(string label)
        {
            double ms = _step.Elapsed.TotalMilliseconds;
            _mainMs += ms;
            if (ms > _longestStep)
            {
                _longestStep = ms;
                _longestLabel = label;
            }
        }

        public IEnumerator Run()
        {
            _step.Restart();
            var root = new GameObject($"Prismatica {_zone.Coord}");
            root.transform.SetParent(_parent, false);
            _result.Root = root;
            _pal = PrismPalette.Create(_zone);
            // Warm the shared icosphere cache here so worker threads only ever read it.
            MeshBuilder.Icosphere(0);
            MeshBuilder.Icosphere(1);
            MeshBuilder.Icosphere(2);
            Mark("start");
            Lap("start");
            yield return null;

            _step.Restart();
            var compute = Task.Run(Compute);
            while (!compute.IsCompleted) yield return null;
            _step.Restart();
            compute.GetAwaiter().GetResult();
            Mark("compute");

            // Collision meshes first, then cook them on worker threads while the render objects are made.
            foreach (var c in _colliders)
            {
                c.CreateMesh();
                if (_step.Elapsed.TotalMilliseconds > SliceMs)
                {
                    Lap("collider meshes");
                    yield return null;
                    _step.Restart();
                }
            }
            var bake = Task.Run(() => Parallel.ForEach(_colliders, c => c.Bake()));

            if (!_headless)
            {
                Lap("collider meshes");
                yield return null;
                _step.Restart();
                var mats = CreateMaterials();
                Lap("materials");
                yield return null;
                _step.Restart();
                var groups = new Dictionary<string, Transform>();
                foreach (var (mesh, material, shadows, group) in _render)
                {
                    if (!groups.TryGetValue(group, out var g))
                    {
                        g = new GameObject(group).transform;
                        g.SetParent(root.transform, false);
                        groups[group] = g;
                    }
                    mesh.ToRenderObject(g, mats[material], shadows);
                    if (_step.Elapsed.TotalMilliseconds > SliceMs)
                    {
                        Lap("render " + mesh.Name);
                        yield return null;
                        _step.Restart();
                    }
                }
            }
            _render.Clear();
            Lap("render");

            while (!bake.IsCompleted) yield return null;
            _step.Restart();
            bake.GetAwaiter().GetResult();
            Mark("objects");

            var colRoot = new GameObject("Colliders").transform;
            colRoot.SetParent(root.transform, false);
            foreach (var c in _colliders)
            {
                c.ToColliderObject(colRoot);
                c.Positions = null;
                c.Indices = null;
                if (_step.Elapsed.TotalMilliseconds > SliceMs)
                {
                    Lap("colliders " + c.Name);
                    yield return null;
                    _step.Restart();
                }
            }
            Mark("colliders");
            Lap("colliders");

            if (!_headless)
            {
                yield return null;
                _step.Restart();
                ZoneLook.Sun(root.transform, _sunElev, _sunAz, _pal.Dusk ? Color.Lerp(_pal.Sun, Color.white, 0.35f) : _pal.Sun, _pal.Dusk ? 1.15f : 1.35f);
                ZoneLook.Ambient(_pal.AmbientSky, _pal.AmbientEquator, _pal.AmbientGround, 1.05f);
                ZoneLook.Fog(_pal.Fog, _fogDensity);
                ZoneLook.PostProcessing(root.transform, 0.75f, 0.95f, 14f, 14f, 0.22f, _pal.Dusk ? 0.15f : 0f);
                Lap("sun and post");
                yield return null;
                _step.Restart();
                var heroDir = _hero.LookAt - _hero.Position;
                heroDir.y = 0;
                heroDir.Normalize();
                PrismaticaGenerator.SetupSky(_pal, heroDir, ref _look);
                Lap("sky");
            }
            _result.HeroViews.Add(_hero);
            Mark("look");
            _result.Notes = $"{_pal.Mood}; ground repairs {_groundRepairs} (ground walk {_groundWalk}); props removed {_propsRemoved}; links {_linksBuilt}; final walk {_finalWalk}; tree retries {_treeRetries}; stranded {_t.StrandedShare * 100f:0.0}%; walk {_walk}; ms[{_times}]; main {_mainMs:0} ms, longest step {_longestStep:0.0} ms ({_longestLabel}); towers {_st.Towers}, bridges {_st.Bridges}, ramps {_t.RampsCarved}, landmarks {string.Join("/", _landmarks.ConvertAll(l => l.Kind.ToString()))}; flora {_flora.Summary}";
        }

        private Dictionary<string, Material> CreateMaterials()
        {
            var pal = _pal;
            var lit = new Dictionary<string, Material>();
            var ground = PrismaticaGenerator.Lit("Ground", pal, 0.4f, 0.3f, 0.12f, 0.12f);
            var hexMat = PrismaticaGenerator.Lit("Hex", pal, 0.35f, 0.3f, 0.08f, 0.3f);
            var solid = PrismaticaGenerator.Lit("Solid", pal, 0.35f, 0.3f, 0.06f, 0.3f);
            var crystal = PrismaticaGenerator.Lit("Crystal", pal, 0.5f, 0.1f, 0.03f, 0.3f);
            crystal.SetFloat("_Rim", 1.2f);
            crystal.SetFloat("_Glow", 3f);
            var foliage = PrismaticaGenerator.Lit("Foliage", pal, 0.6f, 0.25f, 0.06f, 0.5f);
            foliage.SetFloat("_Cull", 0f);
            foliage.SetFloat("_Translucency", 0.45f);
            var farMat = PrismaticaGenerator.Lit("Far", pal, 0.45f, 0.2f, 0.05f, 0.05f);
            var stBands = ZoneRandom.For(_zone.Seed, "bands");
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
            // Distant lands are calmer than the zone so the playable area pops from above.
            for (int i = 0; i < 6; i++) farMat.SetColor("_S" + i, Color.Lerp(pal.Strata[i % pal.Strata.Length], pal.Fog, 0.3f));
            var water = ZoneLook.Material("ZoneArtC/PrismWater", "Water");
            water.SetColor("_Shallow", new Color(pal.Water.r, pal.Water.g, pal.Water.b, 0.62f));
            water.SetColor("_Deep", new Color(pal.WaterDeep.r, pal.WaterDeep.g, pal.WaterDeep.b, 0.9f));
            water.SetColor("_Sky", Color.Lerp(pal.Water, Color.Lerp(pal.SkyHorizon, pal.SkyMid, 0.4f), 0.45f));
            water.SetColor("_Glint", pal.Sun);
            water.SetColor("_SkyH", Color.Lerp(pal.SkyHorizon, pal.Water, 0.3f));
            water.SetColor("_SkyZ", Color.Lerp(pal.SkyMid, pal.SkyZenith, 0.5f));
            var cloudMat = PrismaticaGenerator.Lit("Cloud", pal, 0.95f, 0.45f, 0.03f, 0.6f);
            cloudMat.SetColor("_ShadowColor", Color.Lerp(pal.CloudShade, pal.SkyMid, 0.3f) * 0.85f);
            cloudMat.SetFloat("_Glow", 1.2f);
            lit["ground"] = ground;
            lit["hex"] = hexMat;
            lit["solid"] = solid;
            lit["crystal"] = crystal;
            lit["foliage"] = foliage;
            lit["far"] = farMat;
            lit["water"] = water;
            lit["cloud"] = cloudMat;
            return lit;
        }

        // ================================================================== worker threads

        private void Compute()
        {
            Plan();
            Mark("plan");
            Ground();
            Mark("ground");
            Structures();
            Mark("structures");
            BuildWorld();
            Mark("world");
            Repair();
            Mark("walk");
            LookAndHero();
            Mark("hero");
            Flora();
            Mark("flora");
            if (!_headless)
            {
                RenderData();
                Mark("render");
            }
        }

        private float SafeBase(Vector2 p)
        {
            float h = _t.HeightAt(p.x, p.y);
            if (_t.HasWater) h = Mathf.Max(h, _t.WaterLevel + 0.3f);
            return h;
        }

        /// <summary>Heights, anchor pads, landmark and tower plans, hex fields and the ramp network.</summary>
        private void Plan()
        {
            var zone = _zone;
            _shape = PrismTerrain.ShapeFor(zone);
            var t = _t = new PrismTerrain(zone);
            t.BuildHeights(_shape);
            Mark("heights");
            if (t.HasWater)
            {
                for (int i = 0; i < t.H.Length; i++) if (t.H[i] < t.WaterLevel + 0.4f) t.NoHex[i] = 1f;
            }

            foreach (var a in zone.Anchors)
            {
                var p = L(a.World);
                switch (a.Kind)
                {
                    case ZoneAnchorKind.Shrine:
                        t.Flatten(p, ZoneRequest.ShrineClearRadius + 2f, 7f, SafeBase(p), true);
                        t.Protect(p, ZoneRequest.ShrineClearRadius + 2f);
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
            var usedKinds = new HashSet<LandmarkKind>();
            foreach (var a in zone.Anchors)
            {
                if (a.Kind != ZoneAnchorKind.Landmark) continue;
                var lp = new LandmarkPlan { Local = L(a.World), Name = a.Name, Dir = prng.Range(0f, Mathf.PI * 2f) };
                lp.Kind = PrismaticaGenerator.PickLandmark(zone.Tile, usedKinds, ref prng);
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
                _landmarks.Add(lp);
            }

            // Towers with spiral stairs.
            int towerCount = zone.Tile switch
            {
                TileKind.Tower => prng.Range(3, 5),
                TileKind.Canyon => prng.Range(1, 3),
                TileKind.Ruins => 1,
                _ => prng.Chance(0.45f) ? 0 : 1,
            };
            for (int attempt = 0; attempt < 200 && _towers.Count < towerCount; attempt++)
            {
                float R = zone.Tile == TileKind.Tower ? prng.Range(7f, 11f) : prng.Range(6f, 9.5f);
                var c = new Vector2(prng.Range(38f, 212f), prng.Range(38f, 212f));
                bool ok = true;
                foreach (var a in zone.Anchors) if ((L(a.World) - c).magnitude < R + 18f) ok = false;
                foreach (var lp in _landmarks) if ((lp.Feature - c).magnitude < R + lp.PadR + 10f) ok = false;
                foreach (var tw in _towers) if ((tw.Local - c).magnitude < R + tw.R + 22f) ok = false;
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
                _towers.Add(plan);
            }

            _thr = PrismaticaGenerator.HexThreshold(zone, _shape.HexFrac);
            Mark("plans");
            t.Connect(_shape, _thr, 12);
        }

        /// <summary>Terrain and hex field collision data.</summary>
        private void Ground()
        {
            _st = new PrismStructures(_zone, _t, _pal);
            // Check the built ground (terrain and hex columns, exactly as they will collide) the way players walk it,
            // and carve extra ramps wherever it disagrees with the ramp model. Cheap on the CPU copy, so a few passes fit.
            for (int pass = 0; pass < GroundRepairPasses; pass++)
            {
                var cols = GroundColliders();
                var world = new PrismCollision(_o);
                foreach (var c in cols) c.AddTo(world);
                world.Build();
                var walk = PrismWalk.Analyze(_zone, world);
                _groundWalk = walk;
                if (walk.TwoWayShare > 0.985f && walk.UnreachableAnchors.Count == 0) break;
                int carved = _t.RepairFromWalk(walk.Map, _shape, _thr, RepairMinCells, walk.UnreachableAnchors.ConvertAll(ai => L(_zone.Anchors[ai].World)));
                _groundRepairs += carved;
                if (carved == 0) break;
            }
            _colliders.AddRange(GroundColliders());
        }

        private const int GroundRepairPasses = 3;
        private const int RepairMinCells = 25;
        private int _groundRepairs;
        private int _propsRemoved, _treeRetries;
        private PrismWalk _finalWalk;

        /// <summary>A removable prop: where it stands and which Solid/Crystal triangles are its own.</summary>
        private struct Prop
        {
            public Vector2 Local;
            public float Radius;
            public int Solid0, Solid1, Crystal0, Crystal1;
        }
        private readonly List<Prop> _props = new List<Prop>();
        private int _structStart = -1, _structCount;

        private void AddProp(Vector2 local, float radius, int solid0, int crystal0)
        {
            _props.Add(new Prop { Local = local, Radius = radius, Solid0 = solid0, Solid1 = _st.Solid.Triangles.Count, Crystal0 = crystal0, Crystal1 = _st.Crystal.Triangles.Count });
        }
        private PrismWalk _groundWalk;

        private List<PrismCollider> GroundColliders()
        {
            var list = new List<PrismCollider>();
            var terrain = new PrismCollider[25];
            Parallel.For(0, 25, i => terrain[i] = _t.ChunkCollider(i % 5 * PrismTerrain.Chunk, i / 5 * PrismTerrain.Chunk));
            list.AddRange(terrain);
            _hexChunks = _st.HexField();
            var hex = new PrismCollider[_hexChunks.Length];
            Parallel.For(0, hex.Length, i => hex[i] = _hexChunks[i].VertexCount > 0 ? PrismCollider.FromBuilder($"Hex {i}", _hexChunks[i], PrismCollision.TagGround) : null);
            foreach (var h in hex) if (h != null) list.Add(h);
            return list;
        }

        /// <summary>Towers, bridges, landmarks and props (one random stream, in a fixed order).</summary>
        private void Structures()
        {
            var zone = _zone;
            var t = _t;
            var st = _st;
            var pal = _pal;
            var o = _o;
            var srng = ZoneRandom.For(zone.Seed, "structures");
            foreach (var tw in _towers) st.Tower(tw, ref srng);
            for (int i = 0; i < _towers.Count; i++)
            {
                for (int j = i + 1; j < _towers.Count; j++)
                {
                    if (st.Bridges >= 2) break;
                    if ((_towers[i].Local - _towers[j].Local).magnitude < 80f) st.Bridge(_towers[i], _towers[j], ref srng);
                }
            }
            var trng = ZoneRandom.For(zone.Seed, "giant");
            foreach (var lp in _landmarks)
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
                foreach (var tw in _towers) if ((tw.Local - p).magnitude < tw.R + clear) return false;
                foreach (var lp in _landmarks) if ((lp.Feature - p).magnitude < lp.PadR + clear) return false;
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
                int s0 = st.Solid.Triangles.Count, c0 = st.Crystal.Triangles.Count;
                st.Slab(p, h, ref srng);
                AddProp(p, 7f, s0, c0);
                i++;
            }
            int clusters = zone.Tile switch { TileKind.Crater => srng.Range(14, 20), TileKind.Canyon => srng.Range(6, 10), _ => srng.Range(4, 9) };
            for (int i = 0, tries = 0; i < clusters && tries < 300; tries++)
            {
                Vector2 p = zone.Tile == TileKind.Crater && i < clusters - 4
                    ? _shape.Feature + srng.InsideCircle() * _shape.FeatureR * 0.8f
                    : new Vector2(srng.Range(20f, 230f), srng.Range(20f, 230f));
                if (p.x < 20f || p.y < 20f || p.x > 230f || p.y > 230f) continue;
                if (t.RampAt(p.x, p.y) > 0.05f || t.PadAt(p.x, p.y) > 0.05f) continue;
                bool near = false;
                foreach (var a in zone.Anchors) if ((L(a.World) - p).magnitude < 10f) near = true;
                foreach (var tw in _towers) if ((tw.Local - p).magnitude < tw.R + 6f) near = true;
                if (near) continue;
                float y = t.SurfaceAt(p.x, p.y);
                float size = zone.Tile == TileKind.Crater ? srng.Range(1.2f, 3.6f) : srng.Range(0.8f, 2.4f);
                int k0 = st.Crystal.Triangles.Count;
                st.CrystalCluster(new Vector3(o.x + p.x, y, o.z + p.y), size, srng.Range(3, 8), ref srng);
                AddProp(p, size * 1.2f, st.Solid.Triangles.Count, k0);
                i++;
            }
            int boulders = srng.Range(15, 35);
            for (int i = 0, tries = 0; i < boulders && tries < 300; tries++)
            {
                var p = new Vector2(srng.Range(20f, 230f), srng.Range(20f, 230f));
                if (!OpenGround(p, 6f)) continue;
                int b0 = st.Solid.Triangles.Count;
                float bs = srng.Range(0.7f, 2.4f);
                st.Boulder(new Vector3(o.x + p.x, t.HeightAt(p.x, p.y), o.z + p.y), bs, ref srng);
                AddProp(p, bs * 1.3f, b0, st.Crystal.Triangles.Count);
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
                foreach (var tw in _towers) if ((tw.Local - p).magnitude < tw.R + 25f) hi = Mathf.Max(hi, tw.TopY + 6f);
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

            StructureColliders();
        }

        /// <summary>(Re)makes the Solid, Tops and Crystals colliders, always in the same place in the collider list.</summary>
        private void StructureColliders()
        {
            var st = _st;
            var parts = new (MeshBuilder mb, string name, int tag)[]
            {
                (st.Solid, "Solid", PrismCollision.TagSolid),
                (st.Plantable, "Tops", PrismCollision.TagGround),
                (st.Crystal, "Crystals", PrismCollision.TagCrystal),
            };
            var cols = new PrismCollider[parts.Length];
            Parallel.For(0, parts.Length, i => cols[i] = parts[i].mb.Triangles.Count > 0 ? PrismCollider.FromBuilder(parts[i].name, parts[i].mb, parts[i].tag) : null);
            var list = new List<PrismCollider>();
            foreach (var c in cols) if (c != null) list.Add(c);
            if (_structStart < 0) _structStart = _colliders.Count;
            else _colliders.RemoveRange(_structStart, _structCount);
            _colliders.InsertRange(_structStart, list);
            _structCount = list.Count;
        }

        private void BuildWorld()
        {
            _world = new PrismCollision(_o);
            foreach (var c in _colliders) c.AddTo(_world);
            _world.Build();
        }

        /// <summary>Checks the zone the way players walk it and links every anchor that can't be reached.</summary>
        private void Repair()
        {
            _walk = PrismWalk.Analyze(_zone, _world);
            // Props must never cut off ground the bare terrain connects: take out the ones standing where it got worse.
            for (int pass = 0; pass < 2 && Regressed(_groundWalk, _walk); pass++)
            {
                if (!PruneProps(_groundWalk, _walk)) break;
                StructureColliders();
                BuildWorld();
                _walk = PrismWalk.Analyze(_zone, _world);
            }
            if (_walk.UnreachableAnchors.Count > 0)
            {
                var lrng = ZoneRandom.For(_zone.Seed, "links");
                foreach (int ai in _walk.UnreachableAnchors)
                {
                    var a = _zone.Anchors[ai];
                    float startR = a.Kind == ZoneAnchorKind.Shrine ? ZoneRequest.ShrineClearRadius + 1.6f : a.Kind == ZoneAnchorKind.Gate ? ZoneRequest.GateClearRadius + 1.6f : 2.2f;
                    if (BuildLink(L(a.World), startR, _walk.Map, _links, ref lrng)) _linksBuilt++;
                }
                if (_links.VertexCount > 0)
                {
                    var c = PrismCollider.FromBuilder("Causeways", _links, PrismCollision.TagGround);
                    _colliders.Add(c);
                    BuildWorld();
                    _walk = PrismWalk.Analyze(_zone, _world);
                }
            }
        }

        /// <summary>True when <paramref name="after"/> lost ground or anchors that <paramref name="before"/> could reach.</summary>
        private static bool Regressed(PrismWalk before, PrismWalk after)
        {
            if (after.TwoWayShare < before.TwoWayShare - 0.01f) return true;
            foreach (int ai in after.UnreachableAnchors) if (!before.UnreachableAnchors.Contains(ai)) return true;
            return false;
        }

        /// <summary>
        /// Where something new cut the way off: cells two-way in <paramref name="before"/> but only one-way in
        /// <paramref name="after"/>, in patches big enough to matter, next to ground that is still two-way (plus newly
        /// lost anchors). Returns the 1 m grid within <paramref name="pad"/> cells of those places.
        /// </summary>
        private bool[] LostFrontier(PrismWalk before, PrismWalk after, int pad)
        {
            const int N = 250, MinPatch = 30;
            var lost = new sbyte[N * N];
            for (int k = 0; k < N * N; k++) lost[k] = (sbyte)(before.Map[k] == 2 && after.Map[k] == 1 ? -1 : 0);
            var front = new bool[N * N];
            void Mark(int x, int z)
            {
                for (int dz = -pad; dz <= pad; dz++)
                {
                    for (int dx = -pad; dx <= pad; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx >= 0 && zz >= 0 && xx < N && zz < N && dx * dx + dz * dz <= pad * pad) front[zz * N + xx] = true;
                    }
                }
            }
            bool NearTwoWay(int x, int z)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx >= 0 && zz >= 0 && xx < N && zz < N && after.Map[zz * N + xx] == 2) return true;
                    }
                }
                return false;
            }
            var stack = new Stack<int>();
            var cells = new List<int>();
            for (int k = 0; k < N * N; k++)
            {
                if (lost[k] != -1) continue;
                cells.Clear();
                lost[k] = 1;
                stack.Push(k);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    cells.Add(cur);
                    int x = cur % N, z = cur / N;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= N || nz >= N || lost[nz * N + nx] != -1) continue;
                        lost[nz * N + nx] = 1;
                        stack.Push(nz * N + nx);
                    }
                }
                if (cells.Count < MinPatch) continue;
                foreach (int c in cells) if (NearTwoWay(c % N, c / N)) Mark(c % N, c / N);
            }
            foreach (int ai in after.UnreachableAnchors)
            {
                if (before.UnreachableAnchors.Contains(ai)) continue;
                var p = L(_zone.Anchors[ai].World);
                int ax = Mathf.Clamp((int)p.x, 0, N - 1), az = Mathf.Clamp((int)p.y, 0, N - 1);
                for (int dz = -3; dz <= 3; dz++)
                {
                    for (int dx = -3; dx <= 3; dx++) Mark(Mathf.Clamp(ax + dx, 0, N - 1), Mathf.Clamp(az + dz, 0, N - 1));
                }
            }
            return front;
        }

        /// <summary>Takes out the props standing where the walk regressed. Returns false when there were none.</summary>
        private bool PruneProps(PrismWalk before, PrismWalk after)
        {
            var front = LostFrontier(before, after, 3);
            var doomed = new List<int>();
            for (int i = 0; i < _props.Count; i++)
            {
                var pr = _props[i];
                int r = Mathf.CeilToInt(pr.Radius);
                bool hit = false;
                for (int dz = -r; dz <= r && !hit; dz++)
                {
                    for (int dx = -r; dx <= r && !hit; dx++)
                    {
                        int x = (int)pr.Local.x + dx, z = (int)pr.Local.y + dz;
                        if (x >= 0 && z >= 0 && x < 250 && z < 250 && dx * dx + dz * dz <= r * r) hit = front[z * 250 + x];
                    }
                }
                if (hit) doomed.Add(i);
            }
            if (doomed.Count == 0) return false;
            // Props were added in order, so their triangle ranges ascend: cut from the back and shift the rest.
            var keep = new List<Prop>();
            int ds = 0, dc = 0;
            for (int i = 0, d = 0; i < _props.Count; i++)
            {
                var pr = _props[i];
                if (d < doomed.Count && doomed[d] == i)
                {
                    d++;
                    ds += pr.Solid1 - pr.Solid0;
                    dc += pr.Crystal1 - pr.Crystal0;
                    continue;
                }
                pr.Solid0 -= ds; pr.Solid1 -= ds; pr.Crystal0 -= dc; pr.Crystal1 -= dc;
                keep.Add(pr);
            }
            for (int d = doomed.Count - 1; d >= 0; d--)
            {
                var pr = _props[doomed[d]];
                _st.Solid.Triangles.RemoveRange(pr.Solid0, pr.Solid1 - pr.Solid0);
                _st.Crystal.Triangles.RemoveRange(pr.Crystal0, pr.Crystal1 - pr.Crystal0);
            }
            _props.Clear();
            _props.AddRange(keep);
            _propsRemoved += doomed.Count;
            return true;
        }

        private bool BuildLink(Vector2 anchor, float startR, byte[] map, MeshBuilder mb, ref ZoneRandom lr)
        {
            var t = _t;
            var o = _o;
            float Ground(Vector2 q) => _world.Down(o.x + q.x, o.z + q.y, 800f, out float y, out _, out _) ? y : t.SurfaceAt(q.x, q.y);
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
            if (bestCost == float.MaxValue) return false;
            var ab = bestB - bestA;
            int n = Mathf.CeilToInt(ab.magnitude / spacing);
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                var q = bestA + ab * u;
                float top = Mathf.Lerp(bestYa, bestYb, u) + 0.05f;
                float bottom = Mathf.Min(t.HeightAt(Mathf.Clamp(q.x, 0f, 250f), Mathf.Clamp(q.y, 0f, 250f)), top) - 1.5f;
                var col = Color.Lerp(_pal.HexTop[i % _pal.HexTop.Length], _pal.Strata[(i / 3) % _pal.Strata.Length], 0.35f);
                _st.HexColumn(mb, new Vector3(o.x + q.x, 0f, o.z + q.y), colR, top, bottom, col);
                t.MarkRamp(q, 2.2f);
            }
            return true;
        }

        /// <summary>Sun angles, fog density and the hero view (its random draws happen in headless builds too).</summary>
        private void LookAndHero()
        {
            _look = ZoneRandom.For(_zone.Seed, "look");
            _sunElev = _pal.Dusk ? _look.Range(16f, 30f) : _look.Range(30f, 52f);
            _sunAz = 45f + _look.Sign() * _look.Range(70f, 115f);
            _fogDensity = _look.Range(0.0009f, 0.0012f);
            var sunForward = Quaternion.Euler(_sunElev, _sunAz, 0f) * Vector3.forward;
            _hero = PickHero(sunForward, ref _look);
        }

        private ZoneView PickHero(Vector3 sunForward, ref ZoneRandom rng)
        {
            var zone = _zone;
            var t = _t;
            var st = _st;
            var o = zone.Origin;
            Vector3 target;
            float height;
            if (st.Heroes.Count > 0)
            {
                var w = new float[st.Heroes.Count];
                for (int i = 0; i < w.Length; i++) w[i] = Mathf.Sqrt(Mathf.Max(st.Heroes[i].height, 1f)) * (i < st.Towers ? (zone.Tile == TileKind.Tower ? 1.6f : 0.6f) : 1.4f);
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
                float d = Mathf.Max(26f, height * 1.5f + 14f) * rng.Range(0.8f, 1.8f);
                var lp = new Vector2(target.x - o.x + Mathf.Cos(a) * d, target.z - o.z + Mathf.Sin(a) * d);
                if (lp.x < 3f || lp.y < 3f || lp.x > 247f || lp.y > 247f) continue;
                int cell = Mathf.Clamp((int)lp.y, 0, 249) * 250 + Mathf.Clamp((int)lp.x, 0, 249);
                if (t.Comp[cell] != t.MainComp) continue;
                float y = t.SurfaceAt(lp.x, lp.y);
                if (t.HasWater) y = Mathf.Max(y, t.WaterLevel);
                var eye = new Vector3(o.x + lp.x, y + 1.7f, o.z + lp.y);
                var look = target + Vector3.up * height * 0.45f;
                float score = (y - target.y) * 0.4f;
                if (_world.Linecast(eye, look, out var hitPoint) && (hitPoint - look).magnitude > height * 0.6f + 4f) score -= 100f;
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
                        if (_world.Raycast(eye + Vector3.up * (hgt * 1.5f - 0.6f), d3, 14f, out float dist))
                            score -= (2f + 6f * facing) * (14f / Mathf.Max(dist, 2f)) * 0.25f;
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
                {
                    var lv = look - eye;
                    float pitchDeg = Mathf.Atan2(lv.y, new Vector2(lv.x, lv.z).magnitude) * Mathf.Rad2Deg;
                    score -= Mathf.Max(0f, Mathf.Abs(pitchDeg) - 10f) * 0.6f;
                }
                score -= Mathf.Abs(Vector3.Dot(dir, sunForward)) * 6f;
                score += rng.Range(0f, 3f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestView = new ZoneView(eye, look + Vector3.up * height * 0.15f, 62f);
                }
            }
            return bestView;
        }

        private void Flora()
        {
            _species = PrismFlora.PickSpecies(_zone, _pal, _t.HasWater);
            var heights = new List<float>(_t.S);
            heights.Sort();
            _oPatch = ZoneNoise.Offset(_zone.Seed, 51);
            _oPatch2 = ZoneNoise.Offset(_zone.Seed, 52);
            _oPatch3 = ZoneNoise.Offset(_zone.Seed, 53);
            var bare = _world;
            bool[] noTrees = null;
            _finalWalk = _walk;
            for (int attempt = 0; ; attempt++)
            {
                var ctx = new PrismFlora.Context
                {
                    Zone = _zone, Terrain = _t, Palette = _pal, World = bare,
                    GroundColor = GrassAt, LowY = heights[heights.Count / 20], HighY = heights[heights.Count * 19 / 20] + 0.01f,
                    Occupied = new bool[250 * 250], NoTrees = noTrees,
                    HeroEye = new Vector2(_hero.Position.x, _hero.Position.z), HeroTarget = new Vector2(_hero.LookAt.x, _hero.LookAt.z),
                    Render = !_headless,
                };
                _flora = PrismFlora.Plant(ctx, _species);
                if (_flora.Trunks.VertexCount == 0) break;
                var trunks = PrismCollider.FromBuilder("Trunks", _flora.Trunks, PrismCollision.TagTrunk);
                var world = new PrismCollision(_o);
                foreach (var c in _colliders) c.AddTo(world);
                trunks.AddTo(world);
                world.Build();
                var walk = PrismWalk.Analyze(_zone, world);
                // Trunks must not close a path the ground and structures leave open: plant again around the places they did.
                if (attempt < TreeRetries && Regressed(_walk, walk))
                {
                    var front = LostFrontier(_walk, walk, 4);
                    if (noTrees == null) noTrees = front;
                    else for (int k = 0; k < front.Length; k++) noTrees[k] |= front[k];
                    _treeRetries++;
                    continue;
                }
                _colliders.Add(trunks);
                _world = world;
                _finalWalk = walk;
                break;
            }
        }

        private const int TreeRetries = 2;

        // ------------------------------------------------------------------ render data (full builds only)

        private Color GrassAt(Vector3 w)
        {
            var pal = _pal;
            float n1 = ZoneNoise.Warped(w.x + _oPatch.x, w.z + _oPatch.y, 1f / 45f, 14f, 3);
            float n2 = ZoneNoise.Fbm(w.x + _oPatch2.x, w.z + _oPatch2.y, 1f / 15f, 2);
            float n3 = ZoneNoise.Fbm(w.x + _oPatch3.x, w.z + _oPatch3.y, 1f / 28f, 2);
            var g = Color.Lerp(pal.Grass[0], pal.Grass[1], PrismTerrain.Smooth(-0.05f, 0.2f, n1));
            g = Color.Lerp(g, pal.Grass[2], PrismTerrain.Smooth(0.38f, 0.5f, n3) * 0.85f);
            g = PrismGeo.Shade(g, 1f + n2 * 0.1f);
            return g;
        }

        private Color ColorAt(Vector3 w, Vector3 n, bool inZone)
        {
            var pal = _pal;
            var t = _t;
            var o = _o;
            var g = GrassAt(w);
            var rock = _st.StrataAt(w.y + ZoneNoise.Simplex(w.x * 0.05f, w.z * 0.05f) * 0.8f);
            float cliff = PrismTerrain.Smooth(0.8f, 0.6f, n.y);
            var c = Color.Lerp(g, rock, cliff);
            float h = Mathf.Abs(Mathf.Sin(w.x * 12.9898f + w.z * 78.233f) * 43758.55f) % 1f;
            c = PrismGeo.Shade(c, 0.94f + 0.1f * h);
            if (!inZone)
            {
                float fx = Mathf.Max(0f, Mathf.Max(o.x - w.x, w.x - o.x - 250f)), fz = Mathf.Max(0f, Mathf.Max(o.z - w.z, w.z - o.z - 250f));
                float fd = Mathf.Sqrt(fx * fx + fz * fz);
                c = Color.Lerp(c, pal.Fog, 0.12f + PrismTerrain.Smooth(10f, 500f, fd) * 0.5f);
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

        private static PrismMesh Pack(string name, MeshBuilder mb)
        {
            var pm = new PrismMesh(name, mb.VertexCount);
            pm.Append(mb);
            pm.FixMissingNormals();
            return pm;
        }

        private void RenderData()
        {
            // Terrain chunks and hex chunks in parallel; everything else alongside.
            var terrain = new PrismMesh[25];
            var hex = new PrismMesh[_hexChunks.Length];
            PrismMesh solid = null, tops = null, crystals = null, decor = null, decorRock = null, links = null, water = null, clouds = null;
            PrismFar.Lands far = null;
            Parallel.Invoke(
                () => Parallel.For(0, 25, i => terrain[i] = _t.ChunkRender(i % 5 * PrismTerrain.Chunk, i / 5 * PrismTerrain.Chunk, (w, n) => ColorAt(w, n, true))),
                () => Parallel.For(0, hex.Length, i => hex[i] = _hexChunks[i].VertexCount > 0 ? Pack($"Hex {i}", _hexChunks[i]) : null),
                () =>
                {
                    solid = Pack("Solid", _st.Solid);
                    tops = Pack("Tops", _st.Plantable);
                    crystals = Pack("Crystals", _st.Crystal);
                    decor = Pack("Floating", _st.Decor);
                    decorRock = Pack("Floating rock", _st.DecorRock);
                    links = Pack("Causeways", _links);
                    clouds = PrismFar.Clouds(_zone, _pal);
                    if (_t.HasWater) water = WaterMesh();
                },
                () =>
                {
                    // In the game world the real neighbouring zones surround this one, so no backdrop.
                    if (!_zone.InWorld) far = PrismFar.Build(_zone, _t, _pal, (w, n) => ColorAt(w, n, false), _t.HasWater ? _t.WaterLevel : -999f, _st, _species);
                });

            foreach (var m in terrain) _render.Add((m, "ground", true, "Terrain"));
            foreach (var m in hex) if (m != null) _render.Add((m, "hex", true, "Hex columns"));
            void Add(PrismMesh m, string mat, bool shadows, string group)
            {
                if (m != null && !m.Empty) _render.Add((m, mat, shadows, group));
            }
            Add(solid, "solid", true, "Structures");
            Add(tops, "hex", true, "Structures");
            Add(crystals, "crystal", true, "Structures");
            Add(decor, "foliage", true, "Structures");
            Add(decorRock, "hex", true, "Structures");
            Add(links, "hex", true, "Structures");
            Add(water, "water", false, "Water");
            foreach (var m in _flora.Chunks) Add(m, "foliage", true, "Flora");
            if (far != null)
            {
                foreach (var m in far.Ground) Add(m, "far", true, "Far lands");
                Add(far.Trees, "foliage", true, "Far lands");
                Add(far.Skyline, "far", true, "Far lands");
                Add(far.SkylineCrystals, "crystal", true, "Far lands");
                Add(far.Water, "water", false, "Far lands");
            }
            Add(clouds, "cloud", false, "Clouds");
        }

        private PrismMesh WaterMesh()
        {
            var t = _t;
            var o = _o;
            const int cells = 50;
            float cs = 250f / cells;
            var w = new PrismMesh("Water", (cells + 1) * (cells + 1));
            for (int z = 0; z <= cells; z++)
            {
                for (int x = 0; x <= cells; x++)
                {
                    float lx = x * cs, lz = z * cs;
                    float depth = Mathf.Clamp01((t.WaterLevel - t.HeightAt(lx, lz)) / 1.1f);
                    float shore = PrismTerrain.Smooth(0.35f, 0f, depth);
                    w.Vertex(new Vector3(o.x + lx, t.WaterLevel, o.z + lz), Vector3.up, new Color(depth, shore, 0f, 1f));
                }
            }
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int i = z * (cells + 1) + x;
                    w.Triangle(i, i + cells + 1, i + cells + 2);
                    w.Triangle(i, i + cells + 2, i + 1);
                }
            }
            return w;
        }
    }
}
