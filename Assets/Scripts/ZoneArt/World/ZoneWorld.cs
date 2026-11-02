using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// The generated zones of a layer, live in the game. Zones stream in around points of interest (players,
    /// the camera), a few milliseconds of building per frame, and unload once nobody has been near for a while.
    ///
    /// Every finished zone is split in two:
    /// - its colliders move to a collision hierarchy that never moves, at the zone's true position in the
    ///   wrapping world (positions in [0, WorldSize)). Movement, shots and spawns query it.
    /// - its visuals move to the copy of the zone nearest the camera, so the world wraps seamlessly on screen.
    /// Each zone's sky, sun, ambient, fog and post-processing are captured, and the zone the camera is in sets
    /// the scene's look, blending when the camera crosses into another zone.
    ///
    /// One instance per process: the server and client worlds of a play session share it.
    /// </summary>
    public sealed class ZoneWorld
    {
        /// <summary>Main-thread milliseconds per frame spent building while nobody is waiting on a zone.</summary>
        public static float BuildBudgetMs = 6f;
        /// <summary>The same while someone stands in a zone that isn't built yet.</summary>
        public static float UrgentBudgetMs = 40f;
        /// <summary>Neighbouring zones start building once a point of interest is this close to them (metres).</summary>
        public static float PrefetchDistance = 150f;
        /// <summary>Zones nobody has been near for this long unload (seconds).</summary>
        public static float UnloadAfter = 60f;
        /// <summary>Main-thread milliseconds per frame spent on bot nav grids.</summary>
        public static float NavBudgetMs = 3f;
        /// <summary>Seconds the look takes to blend from one zone to the next.</summary>
        public static float LookBlendTime = 2.5f;

        public static ZoneWorld Current { get; private set; }

        /// <summary>The zone world for a layer, created on first use (and replaced when the layer or generator changes).</summary>
        public static ZoneWorld Open(LayerMap map, string generatorName, bool headless)
        {
            // Leaving play mode destroys the zones (but not this static, with domain reload off): start over.
            if (Current != null && Current._root == null) Current = null;
            if (Current != null && Current.Map.Seed == map.Seed && Current.GeneratorName == generatorName && Current.Headless == headless) return Current;
            Close();
            Current = new ZoneWorld(map, generatorName, headless);
            return Current;
        }

        public static void Close()
        {
            if (Current == null) return;
            Current.Dispose();
            Current = null;
        }

        public LayerMap Map { get; }
        public string GeneratorName { get; }
        public bool Headless { get; }
        public float ZoneSize { get; }
        public float WorldSize { get; }

        /// <summary>Where the camera is (true world position), or null when nothing renders. Set every frame by the view.</summary>
        public Vector3? ViewPoint { get; set; }

        /// <summary>Layers the zone colliders live on (for physics queries).</summary>
        public const int CollisionMask = 1 << 0;

        private readonly IZoneGenerator _generator;
        private readonly Slot[] _slots;
        private readonly GameObject _root;
        private readonly Transform _visualRoot, _collisionRoot;
        private readonly List<(Vector3 point, bool urgent)> _interest = new List<(Vector3, bool)>();
        private Slot _building;
        private readonly Stack<IEnumerator> _steps = new Stack<IEnumerator>();
        private int _tickedFrame = -1, _lateFrame = -1;
        private Light _sun;
        private Material _skyBlend;
        private Slot _lookFrom, _lookTo;
        private float _lookT = 1f;
        private readonly Stopwatch _clock = new Stopwatch();

        /// <summary>Totals for the HUD and logs.</summary>
        public int ZonesBuilt { get; private set; }
        public double BuildSeconds { get; private set; }
        public double LongestStepMs { get; private set; }

        private enum SlotState { Empty, Building, Ready }

        private sealed class Slot
        {
            public ZoneCoord Coord;
            public SlotState State;
            public GameObject Visual, Collision;
            public ZoneResult Result;
            public Look Look;
            public RenderState Pending;
            public ZoneNavGrid Nav;
            public ZoneNavGrid.Builder NavBuilder;
            public float LastWanted = float.NegativeInfinity;
            public float Priority;
            public double Seconds;
        }

        private ZoneWorld(LayerMap map, string generatorName, bool headless)
        {
            Map = map;
            GeneratorName = generatorName;
            Headless = headless;
            ZoneSize = map.Grid.ZoneSize;
            WorldSize = map.Grid.Width * ZoneSize;
            _generator = ZoneGenerators.Find(generatorName);
            _slots = new Slot[map.Grid.Width * map.Grid.Height];
            for (int y = 0; y < map.Grid.Height; y++)
            {
                for (int x = 0; x < map.Grid.Width; x++) _slots[y * map.Grid.Width + x] = new Slot { Coord = new ZoneCoord(x, y) };
            }
            _root = new GameObject("Zone World");
            Object.DontDestroyOnLoad(_root);
            _visualRoot = new GameObject("Visual").transform;
            _visualRoot.SetParent(_root.transform, false);
            _collisionRoot = new GameObject("Collision").transform;
            _collisionRoot.SetParent(_root.transform, false);
            _root.AddComponent<Runner>().World = this;
        }

        private void Dispose()
        {
            foreach (var s in _slots) Unload(s);
            if (_root != null) Object.Destroy(_root);
            if (_skyBlend != null) Object.Destroy(_skyBlend);
        }

        // ------------------------------------------------------------------ queries

        public ZoneCoord ZoneAt(float x, float z) => Map.Grid.ZoneAt(x, z);

        public bool IsReady(ZoneCoord c) => SlotOf(c).State == SlotState.Ready;

        /// <summary>True when the zone under this (true, wrapped) position is built and has its colliders.</summary>
        public bool IsReady(float x, float z) => IsReady(ZoneAt(x, z));

        /// <summary>Builds the zone under this position right now if it isn't built (a hitch of a few seconds).</summary>
        public void EnsureReady(float x, float z)
        {
            var slot = SlotOf(ZoneAt(x, z));
            slot.LastWanted = Time.unscaledTime;
            if (slot.State == SlotState.Ready) return;
            if (_building != null && _building != slot) FinishCurrent();
            if (slot.State == SlotState.Empty) Start(slot);
            FinishCurrent();
        }

        /// <summary>Marks a point players care about this frame: its zone and nearby neighbours stream in.</summary>
        public void AddInterest(Vector3 point, bool urgent = true) => _interest.Add((point, urgent));

        /// <summary>The highest surface under (x, z) below <paramref name="fromY"/>, or false if there is none (or the zone isn't built).</summary>
        public bool GroundBelow(float x, float z, float fromY, out RaycastHit hit, float maxDrop = 2000f) =>
            Physics.Raycast(new Vector3(x, fromY, z), Vector3.down, out hit, maxDrop, CollisionMask, QueryTriggerInteraction.Ignore);

        /// <summary>The top surface at (x, z), or <paramref name="fallback"/> if there is none.</summary>
        public float TopAt(float x, float z, float fallback = 0f) => GroundBelow(x, z, 600f, out var hit) ? hit.point.y : fallback;

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit) =>
            Physics.Raycast(origin, direction, out hit, maxDistance, CollisionMask, QueryTriggerInteraction.Ignore);

        public bool CheckCapsule(Vector3 a, Vector3 b, float radius) =>
            Physics.CheckCapsule(a, b, radius, CollisionMask, QueryTriggerInteraction.Ignore);

        public bool SphereCast(Vector3 origin, float radius, Vector3 direction, float maxDistance, out RaycastHit hit) =>
            Physics.SphereCast(origin, radius, direction, out hit, maxDistance, CollisionMask, QueryTriggerInteraction.Ignore);

        /// <summary>Where players can stand in a built zone, for bot paths (built on first use, null if the zone isn't built).</summary>
        public ZoneNavGrid NavGrid(ZoneCoord c)
        {
            var slot = SlotOf(c);
            if (slot.State != SlotState.Ready || slot.Collision == null) return null;
            // Built a few rows per frame by Tick once someone asks; null until then.
            if (slot.Nav == null && slot.NavBuilder == null)
            {
                slot.NavBuilder = new ZoneNavGrid.Builder(new Vector3(slot.Coord.X * ZoneSize, 0f, slot.Coord.Y * ZoneSize), ZoneSize);
            }
            slot.LastWanted = Mathf.Max(slot.LastWanted, Time.unscaledTime - UnloadAfter * 0.5f);
            return slot.Nav;
        }

        private ZoneNavigator _navigator;

        /// <summary>Pathfinding over the built zones (shared; use from the main thread).</summary>
        public ZoneNavigator Navigator => _navigator ??= new ZoneNavigator(this);

        /// <summary>Notes from the generator for a built zone (palette, counts...), or null.</summary>
        public string NotesFor(ZoneCoord c) => SlotOf(c).Result?.Notes;

        public int ReadyCount
        {
            get
            {
                int n = 0;
                foreach (var s in _slots) if (s.State == SlotState.Ready) n++;
                return n;
            }
        }

        public bool IsBuilding => _building != null;

        // ------------------------------------------------------------------ streaming

        private Slot SlotOf(ZoneCoord c)
        {
            c = Map.Grid.Wrap(c.X, c.Y);
            return _slots[c.Y * Map.Grid.Width + c.X];
        }

        private void Tick()
        {
            if (_tickedFrame == Time.frameCount) return;
            _tickedFrame = Time.frameCount;
            float now = Time.unscaledTime;
            if (ViewPoint.HasValue) _interest.Add((ViewPoint.Value, true));

            bool urgent = false;
            foreach (var s in _slots) s.Priority = float.MaxValue;
            foreach (var (p, isUrgent) in _interest)
            {
                var c = ZoneAt(Wrap(p.x), Wrap(p.z));
                var own = SlotOf(c);
                Want(own, 0f, now);
                if (isUrgent && own.State != SlotState.Ready) urgent = true;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var n = Map.Grid.Wrap(c.X + dx, c.Y + dy);
                        float d = DistanceToZone(p, n);
                        if (d < PrefetchDistance) Want(SlotOf(n), d, now);
                    }
                }
            }
            _interest.Clear();

            if (_building == null)
            {
                Slot best = null;
                foreach (var s in _slots)
                {
                    if (s.State != SlotState.Empty || s.Priority == float.MaxValue) continue;
                    if (best == null || s.Priority < best.Priority) best = s;
                }
                if (best != null) Start(best);
            }
            if (_building != null) Step(urgent && _building.Priority == 0f ? UrgentBudgetMs : BuildBudgetMs);

            foreach (var s in _slots)
            {
                if (s.State == SlotState.Ready && now - s.LastWanted > UnloadAfter) Unload(s);
            }

            // Nav grids someone asked for, a few milliseconds per frame.
            foreach (var s in _slots)
            {
                if (s.NavBuilder == null) continue;
                if (s.NavBuilder.Step(NavBudgetMs))
                {
                    s.Nav = s.NavBuilder.Result;
                    s.NavBuilder = null;
                    Debug.Log($"[ZoneWorld] nav grid for zone {s.Coord}: {s.Nav.Count} standing spots");
                }
                break;
            }
        }

        private static void Want(Slot s, float priority, float now)
        {
            s.LastWanted = now;
            if (priority < s.Priority) s.Priority = priority;
        }

        private float Wrap(float v) => v - WorldSize * Mathf.Floor(v / WorldSize);

        /// <summary>Distance from a point to a zone's square, the short way round the wrapping world.</summary>
        private float DistanceToZone(Vector3 p, ZoneCoord c)
        {
            float cx = (c.X + 0.5f) * ZoneSize, cz = (c.Y + 0.5f) * ZoneSize;
            float dx = Mathf.Abs(Near(p.x - cx)) - ZoneSize * 0.5f;
            float dz = Mathf.Abs(Near(p.z - cz)) - ZoneSize * 0.5f;
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dz, 0f)).magnitude;
        }

        private float Near(float delta) => delta - WorldSize * Mathf.Round(delta / WorldSize);

        private void Start(Slot slot)
        {
            var request = ZoneRequest.FromLayer(Map, slot.Coord);
            request.InWorld = true;
            request.Headless = Headless;
            slot.State = SlotState.Building;
            slot.Visual = new GameObject($"Zone {slot.Coord}");
            slot.Visual.transform.SetParent(_visualRoot, false);
            slot.Result = new ZoneResult();
            slot.Seconds = 0;
            var saved = RenderState.Capture();
            ZoneLook.Reset();
            slot.Pending = RenderState.Capture();
            saved.Apply();
            _steps.Clear();
            _steps.Push(ZoneBuild.Steps(_generator, request, slot.Visual.transform, slot.Result));
            _building = slot;
        }

        /// <summary>Runs the current build for up to <paramref name="budgetMs"/>, keeping its scene settings apart from the live ones.</summary>
        private void Step(float budgetMs)
        {
            var slot = _building;
            var saved = RenderState.Capture();
            slot.Pending.Apply();
            _clock.Restart();
            bool done = false;
            try
            {
                while (true)
                {
                    double before = _clock.Elapsed.TotalMilliseconds;
                    if (!StepOnce())
                    {
                        done = true;
                        break;
                    }
                    LongestStepMs = System.Math.Max(LongestStepMs, _clock.Elapsed.TotalMilliseconds - before);
                    if (_clock.Elapsed.TotalMilliseconds >= budgetMs) break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ZoneWorld] Building zone {slot.Coord} failed: {e}");
                done = true;
            }
            slot.Seconds += _clock.Elapsed.TotalSeconds;
            slot.Pending = RenderState.Capture();
            saved.Apply();
            if (done) Finish(slot);
        }

        /// <summary>Advances the nested build enumerators by one step; false once the build is complete.</summary>
        private bool StepOnce()
        {
            while (_steps.Count > 0)
            {
                var top = _steps.Peek();
                if (!top.MoveNext())
                {
                    _steps.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested) _steps.Push(nested);
                return true;
            }
            return false;
        }

        private void FinishCurrent()
        {
            while (_building != null) Step(float.MaxValue);
        }

        private void Finish(Slot slot)
        {
            _building = null;
            _steps.Clear();
            if (slot.Result.Root == null)
            {
                // The build failed: leave the zone empty and don't retry every frame.
                Object.Destroy(slot.Visual);
                slot.Visual = null;
                slot.State = SlotState.Ready;
                slot.Collision = new GameObject($"Zone {slot.Coord} (failed)");
                slot.Collision.transform.SetParent(_collisionRoot, false);
                return;
            }

            // Colliders go to the fixed collision hierarchy at their true position.
            slot.Collision = new GameObject($"Zone {slot.Coord}");
            slot.Collision.transform.SetParent(_collisionRoot, false);
            foreach (var c in slot.Visual.GetComponentsInChildren<Collider>(true)) MoveCollider(c, slot.Collision.transform);
            Physics.SyncTransforms();

            if (Headless)
            {
                Object.Destroy(slot.Visual);
                slot.Visual = null;
            }
            else
            {
                slot.Look = Look.From(slot.Pending, slot.Visual);
            }
            slot.State = SlotState.Ready;
            ZonesBuilt++;
            BuildSeconds += slot.Seconds;
            Debug.Log($"[ZoneWorld] zone {slot.Coord} ready: {slot.Seconds:0.0} s of main-thread building. {slot.Result.Notes}");
        }

        private static void MoveCollider(Collider c, Transform parent)
        {
            var t = c.transform;
            var go = new GameObject(t.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(t.position, t.rotation);
            go.transform.localScale = t.lossyScale;
            switch (c)
            {
                case MeshCollider m:
                    var copy = go.AddComponent<MeshCollider>();
                    copy.cookingOptions = m.cookingOptions;
                    copy.convex = m.convex;
                    copy.sharedMesh = m.sharedMesh;
                    break;
                case BoxCollider b:
                    var box = go.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = b.size;
                    break;
                case SphereCollider s:
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.center = s.center;
                    sphere.radius = s.radius;
                    break;
                case CapsuleCollider k:
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.center = k.center;
                    capsule.radius = k.radius;
                    capsule.height = k.height;
                    capsule.direction = k.direction;
                    break;
                default:
                    Debug.LogWarning($"[ZoneWorld] Collider type {c.GetType().Name} on {t.name} isn't supported; it stays with the visuals.");
                    return;
            }
            Object.DestroyImmediate(c);
        }

        private void Unload(Slot slot)
        {
            if (slot == _building)
            {
                _building = null;
                _steps.Clear();
            }
            if (_lookFrom == slot) _lookFrom = null;
            if (_lookTo == slot) _lookTo = null;
            var assets = new HashSet<Object>();
            foreach (var go in new[] { slot.Visual, slot.Collision })
            {
                if (go == null) continue;
                foreach (var f in go.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) assets.Add(f.sharedMesh);
                foreach (var m in go.GetComponentsInChildren<MeshCollider>(true)) if (m.sharedMesh != null) assets.Add(m.sharedMesh);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var mat in r.sharedMaterials) if (mat != null) assets.Add(mat);
                }
                foreach (var v in go.GetComponentsInChildren<Volume>(true)) if (v.sharedProfile != null) assets.Add(v.sharedProfile);
                Object.Destroy(go);
            }
            if (slot.Look?.Skybox != null) assets.Add(slot.Look.Skybox);
            foreach (var a in assets) Object.Destroy(a);
            slot.Visual = null;
            slot.Collision = null;
            slot.Result = null;
            slot.Look = null;
            slot.Nav = null;
            slot.NavBuilder = null;
            slot.State = SlotState.Empty;
        }

        // ------------------------------------------------------------------ view

        private void LateTick()
        {
            if (_lateFrame == Time.frameCount) return;
            _lateFrame = Time.frameCount;
            if (Headless || !ViewPoint.HasValue) return;
            var view = ViewPoint.Value;

            // Each zone's visuals sit at the copy nearest the camera.
            foreach (var s in _slots)
            {
                if (s.State != SlotState.Ready || s.Visual == null) continue;
                float cx = (s.Coord.X + 0.5f) * ZoneSize, cz = (s.Coord.Y + 0.5f) * ZoneSize;
                float ox = view.x + Near(cx - view.x) - cx, oz = view.z + Near(cz - view.z) - cz;
                s.Visual.transform.position = new Vector3(ox, 0f, oz);
            }

            var here = SlotOf(ZoneAt(Wrap(view.x), Wrap(view.z)));
            if (here.State == SlotState.Ready && here.Look != null && here != _lookTo)
            {
                _lookFrom = _lookTo != null && _lookTo.Look != null ? _lookTo : null;
                _lookTo = here;
                _lookT = _lookFrom == null ? 1f : 0f;
            }
            if (_lookTo == null || _lookTo.Look == null) return;
            _lookT = Mathf.Min(1f, _lookT + Time.unscaledDeltaTime / Mathf.Max(0.01f, LookBlendTime));
            ApplyLook(_lookFrom?.Look, _lookTo.Look, Mathf.SmoothStep(0f, 1f, _lookT));
        }

        private void ApplyLook(Look from, Look to, float t)
        {
            if (from == null) t = 1f;
            if (_sun == null)
            {
                var go = new GameObject("Zone Sun");
                go.transform.SetParent(_root.transform, false);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
            }
            var a = from ?? to;
            _sun.transform.rotation = Quaternion.Slerp(a.SunRotation, to.SunRotation, t);
            _sun.color = Color.Lerp(a.SunColor, to.SunColor, t);
            _sun.intensity = Mathf.Lerp(a.SunIntensity, to.SunIntensity, t);
            _sun.shadows = to.SunShadows;
            _sun.shadowStrength = Mathf.Lerp(a.ShadowStrength, to.ShadowStrength, t);
            RenderSettings.sun = _sun;

            RenderSettings.ambientMode = to.State.AmbientMode;
            RenderSettings.ambientSkyColor = Color.Lerp(a.State.AmbientSky, to.State.AmbientSky, t);
            RenderSettings.ambientEquatorColor = Color.Lerp(a.State.AmbientEquator, to.State.AmbientEquator, t);
            RenderSettings.ambientGroundColor = Color.Lerp(a.State.AmbientGround, to.State.AmbientGround, t);
            RenderSettings.ambientIntensity = Mathf.Lerp(a.State.AmbientIntensity, to.State.AmbientIntensity, t);
            RenderSettings.fog = to.State.Fog;
            RenderSettings.fogMode = to.State.FogMode;
            RenderSettings.fogColor = Color.Lerp(a.State.FogColor, to.State.FogColor, t);
            RenderSettings.fogDensity = Mathf.Lerp(a.State.FogDensity, to.State.FogDensity, t);
            RenderSettings.fogStartDistance = Mathf.Lerp(a.State.FogStart, to.State.FogStart, t);
            RenderSettings.fogEndDistance = Mathf.Lerp(a.State.FogEnd, to.State.FogEnd, t);

            if (t >= 1f || a.Skybox == null || to.Skybox == null || a.Skybox.shader != to.Skybox.shader)
            {
                RenderSettings.skybox = t >= 0.5f ? to.Skybox : a.Skybox;
            }
            else
            {
                if (_skyBlend == null || _skyBlend.shader != to.Skybox.shader) _skyBlend = new Material(to.Skybox) { name = "Sky blend" };
                _skyBlend.Lerp(a.Skybox, to.Skybox, t);
                RenderSettings.skybox = _skyBlend;
            }

            foreach (var s in _slots)
            {
                if (s.Look?.Volume == null) continue;
                float w = s.Look == to ? t : s.Look == from ? 1f - t : 0f;
                s.Look.Volume.weight = w;
                s.Look.Volume.enabled = w > 0.001f;
            }
        }

        /// <summary>The scene settings a zone build writes: captured between build steps and per finished zone.</summary>
        private struct RenderState
        {
            public Material Skybox;
            public Light Sun;
            public AmbientMode AmbientMode;
            public Color AmbientSky, AmbientEquator, AmbientGround;
            public float AmbientIntensity;
            public bool Fog;
            public FogMode FogMode;
            public Color FogColor;
            public float FogDensity, FogStart, FogEnd;
            public float ReflectionIntensity;
            public DefaultReflectionMode ReflectionMode;

            public static RenderState Capture() => new RenderState
            {
                Skybox = RenderSettings.skybox, Sun = RenderSettings.sun, AmbientMode = RenderSettings.ambientMode,
                AmbientSky = RenderSettings.ambientSkyColor, AmbientEquator = RenderSettings.ambientEquatorColor,
                AmbientGround = RenderSettings.ambientGroundColor, AmbientIntensity = RenderSettings.ambientIntensity,
                Fog = RenderSettings.fog, FogMode = RenderSettings.fogMode, FogColor = RenderSettings.fogColor,
                FogDensity = RenderSettings.fogDensity, FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance,
                ReflectionIntensity = RenderSettings.reflectionIntensity, ReflectionMode = RenderSettings.defaultReflectionMode,
            };

            public void Apply()
            {
                RenderSettings.skybox = Skybox;
                RenderSettings.sun = Sun;
                RenderSettings.ambientMode = AmbientMode;
                RenderSettings.ambientSkyColor = AmbientSky;
                RenderSettings.ambientEquatorColor = AmbientEquator;
                RenderSettings.ambientGroundColor = AmbientGround;
                RenderSettings.ambientIntensity = AmbientIntensity;
                RenderSettings.fog = Fog;
                RenderSettings.fogMode = FogMode;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogDensity = FogDensity;
                RenderSettings.fogStartDistance = FogStart;
                RenderSettings.fogEndDistance = FogEnd;
                RenderSettings.reflectionIntensity = ReflectionIntensity;
                RenderSettings.defaultReflectionMode = ReflectionMode;
            }
        }

        /// <summary>A finished zone's look: its scene settings, sun and post-processing volume.</summary>
        private sealed class Look
        {
            public RenderState State;
            public Material Skybox;
            public Quaternion SunRotation = Quaternion.Euler(45f, 30f, 0f);
            public Color SunColor = Color.white;
            public float SunIntensity = 1f, ShadowStrength = 0.85f;
            public LightShadows SunShadows = LightShadows.Soft;
            public Volume Volume;

            public static Look From(RenderState state, GameObject zone)
            {
                var look = new Look { State = state, Skybox = state.Skybox };
                bool found = false;
                foreach (var light in zone.GetComponentsInChildren<Light>(true))
                {
                    if (light.type != LightType.Directional) continue;
                    if (light == state.Sun || !found)
                    {
                        found = true;
                        look.SunRotation = light.transform.rotation;
                        look.SunColor = light.color;
                        look.SunIntensity = light.intensity;
                        look.SunShadows = light.shadows;
                        look.ShadowStrength = light.shadowStrength;
                    }
                    light.enabled = false;
                }
                look.Volume = zone.GetComponentInChildren<Volume>(true);
                if (look.Volume != null)
                {
                    look.Volume.weight = 0f;
                    look.Volume.enabled = false;
                }
                return look;
            }
        }

        /// <summary>Drives streaming and the view once per frame.</summary>
        private sealed class Runner : MonoBehaviour
        {
            public ZoneWorld World;

            private void Update()
            {
                // A script reload during play leaves the runner without its world: retire it.
                if (World == null || World != Current)
                {
                    Destroy(gameObject);
                    return;
                }
                World.Tick();
            }

            private void LateUpdate()
            {
                if (World != null && World == Current) World.LateTick();
            }
        }
    }
}
