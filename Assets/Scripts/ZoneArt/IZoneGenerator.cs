using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// Builds the look of one zone: ground, rocks, structures, vegetation, sky, sun and post-processing.
    /// The same request must always give the same zone (use only <see cref="ZoneRequest.Seed"/> for randomness).
    /// Implementations need a public parameterless constructor; <see cref="ZoneGenerators"/> finds them by reflection.
    /// </summary>
    public interface IZoneGenerator
    {
        /// <summary>Short id used on the command line, e.g. "verdant".</summary>
        string Name { get; }

        /// <summary>
        /// Builds the zone under <paramref name="parent"/> (which sits at the world origin) and sets up
        /// the scene's sky, sun and post-processing. Everything players can stand on or bump into needs a collider.
        /// The scene's <see cref="RenderSettings"/> are reset to defaults before every call.
        /// </summary>
        ZoneResult Build(ZoneRequest request, Transform parent);
    }

    public enum ZoneAnchorKind
    {
        /// <summary>A named place players give directions by; build something memorable here.</summary>
        Landmark,
        /// <summary>Respawns players (and Reset position lands there); needs flat open ground within <see cref="ZoneRequest.ShrineClearRadius"/>.</summary>
        Shrine,
        /// <summary>The gate to the next layer, or the raid door; needs flat open ground within <see cref="ZoneRequest.GateClearRadius"/>.</summary>
        Gate,
        /// <summary>Where enemies appear; needs to be reachable on foot.</summary>
        Spawn,
    }

    /// <summary>A gameplay spot the layer generator placed in this zone. Positions are on the ground plane (y = 0).</summary>
    public readonly struct ZoneAnchor
    {
        public readonly ZoneAnchorKind Kind;
        public readonly string Name;
        /// <summary>World position (x, 0, z).</summary>
        public readonly Vector3 World;

        public ZoneAnchor(ZoneAnchorKind kind, string name, Vector3 world)
        {
            Kind = kind;
            Name = name;
            World = world;
        }

        public override string ToString() => $"{Kind} {Name} at {World}";
    }

    /// <summary>Everything a generator gets to build one zone.</summary>
    public sealed class ZoneRequest
    {
        public const float ShrineClearRadius = 6f;
        public const float GateClearRadius = 7f;

        /// <summary>The only source of randomness a generator should use.</summary>
        public uint Seed { get; }
        /// <summary>The layer's seed (shared by every zone of the layer: use it for anything that must match across zones).</summary>
        public uint LayerSeed { get; }
        public ZoneCoord Coord { get; }
        /// <summary>
        /// Side of the zone's square build box in metres. Zones are hexagons, squares and triangles (see
        /// <see cref="Outline"/>); the box holds the whole outline with a metre or more to spare. Build ground
        /// over the box if that is easier, but only what lies inside the outline belongs to this zone.
        /// </summary>
        public float Size { get; }
        /// <summary>The build box's minimum corner in world space (y = 0, whole metres). The box covers Origin .. Origin + (Size, *, Size).</summary>
        public Vector3 Origin { get; }
        /// <summary>The zone's corners in world space (x, z), counter-clockwise. Convex.</summary>
        public IReadOnlyList<Vector2> Outline => _outline;
        public ZoneShape Shape => Coord.Shape;
        /// <summary>The hand-tile theme the layer picked for this zone (a hint, not a rule).</summary>
        public TileKind Tile { get; }
        /// <summary>The essence color the zone leans toward (a hint for accent colors).</summary>
        public EssenceColor Lean { get; }
        public IReadOnlyList<ZoneAnchor> Anchors { get; }

        /// <summary>
        /// The wrapping world's two periods (world x, z; both zero when the world doesn't wrap). Anything that must
        /// match across the world's wrap seam repeats on this lattice; see <see cref="ZoneEdges.BorderHeight(ZoneRequest, float, float)"/>.
        /// </summary>
        public Vector2 WrapA { get; set; }
        public Vector2 WrapB { get; set; }

        /// <summary>
        /// True when the zone is built inside the game world with its real neighbours around it: skip anything
        /// drawn outside the zone (backdrops, distant lands). Sky, sun, ambient, fog and post-processing are still
        /// set up as usual; the game captures them per zone.
        /// </summary>
        public bool InWorld { get; set; }

        /// <summary>
        /// True on a server without graphics: only colliders matter. A generator may skip purely visual work, but every
        /// collider must come out exactly as in a full build (clients and the server must agree on the ground).
        /// </summary>
        public bool Headless { get; set; }

        /// <summary>
        /// Which layer of the world the zone is on (1 = the top). Generators may build a different biome per layer;
        /// like everything else, it must give the same zone on the server and every client.
        /// </summary>
        public int Layer { get; set; } = 1;

        /// <summary>The middle of the zone (y = 0): the outline's centroid, always inside it.</summary>
        public Vector3 Center { get; }
        /// <summary>Metres from <see cref="Center"/> to the nearest edge.</summary>
        public float InnerRadius => EdgeDistance(Center.x, Center.z);

        private readonly Vector2[] _outline;
        private readonly Vector2[] _normals;

        /// <summary>A zone with this outline (world x, z, counter-clockwise, convex).</summary>
        public ZoneRequest(uint seed, uint layerSeed, ZoneCoord coord, IReadOnlyList<Vector2> outline, TileKind tile, EssenceColor lean, IReadOnlyList<ZoneAnchor> anchors)
        {
            Seed = seed;
            LayerSeed = layerSeed;
            Coord = coord;
            _outline = new Vector2[outline.Count];
            for (int i = 0; i < _outline.Length; i++) _outline[i] = outline[i];
            _normals = new Vector2[_outline.Length];
            var centroid = Vector2.zero;
            for (int i = 0; i < _outline.Length; i++)
            {
                var e = _outline[(i + 1) % _outline.Length] - _outline[i];
                _normals[i] = new Vector2(-e.y, e.x).normalized; // inward for a counter-clockwise outline
                centroid += _outline[i];
            }
            centroid /= _outline.Length;
            Center = new Vector3(centroid.x, 0f, centroid.y);
            BoxAround(_outline, out var origin, out float size);
            Origin = origin;
            Size = size;
            Tile = tile;
            Lean = lean;
            Anchors = anchors;
        }

        /// <summary>A plain square zone of side <paramref name="size"/> at grid spot (coord.X, coord.Y), for tools and tests.</summary>
        public ZoneRequest(uint seed, uint layerSeed, ZoneCoord coord, float size, TileKind tile, EssenceColor lean, IReadOnlyList<ZoneAnchor> anchors)
            : this(seed, layerSeed, coord, Square(coord.X * size, coord.Y * size, size), tile, lean, anchors)
        {
        }

        private static Vector2[] Square(float x, float z, float size) =>
            new[] { new Vector2(x, z), new Vector2(x + size, z), new Vector2(x + size, z + size), new Vector2(x, z + size) };

        /// <summary>The square build box around an outline: whole-metre corner, at least a metre spare on every side.</summary>
        public static void BoxAround(IReadOnlyList<Vector2> outline, out Vector3 origin, out float size)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in outline)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            origin = new Vector3(Mathf.Floor(min.x) - 1f, 0f, Mathf.Floor(min.y) - 1f);
            size = Mathf.Ceil(Mathf.Max(max.x - min.x, max.y - min.y)) + 3f;
        }

        /// <summary>The build box for one zone of a tiling (same as a request's <see cref="Origin"/> and <see cref="Size"/>).</summary>
        public static void BoxFor(ZoneTiling tiling, ZoneCoord coord, out Vector3 origin, out float size)
        {
            var outline = new List<Vector2>();
            foreach (var p in tiling.Outline(coord)) outline.Add(new Vector2(p.X, p.Y));
            BoxAround(outline, out origin, out size);
        }

        /// <summary>Metres from (x, z) to the nearest edge: positive inside the zone, negative outside.</summary>
        public float EdgeDistance(float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _outline.Length; i++)
            {
                var a = _outline[i];
                float d = (x - a.x) * _normals[i].x + (z - a.y) * _normals[i].y;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>True if (x, z) is inside the zone, at least <paramref name="margin"/> metres from every edge.</summary>
        public bool Contains(float x, float z, float margin = 0f) => EdgeDistance(x, z) >= margin;

        /// <summary>The request for one zone of a generated layer.</summary>
        public static ZoneRequest FromLayer(LayerMap map, ZoneCoord coord)
        {
            var zone = map.Zone(coord);
            var tiling = map.Tiling;
            var anchors = new List<ZoneAnchor>();
            foreach (var l in zone.Landmarks) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Landmark, l.Name, ToWorld(l.Point.World)));
            foreach (var s in zone.Shrines) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Shrine, $"Shrine {s.Id}", ToWorld(s.Point.World)));
            if (zone.HasGate) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Gate, "Gate", ToWorld(zone.Gate.Point.World)));
            // The raid door needs the same flat, open ground as a gate (its posts and the ring players stand in).
            var door = RaidSite.DoorFor(map);
            if (tiling.ZoneAt(door).Equals(coord)) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Gate, "Raid door", ToWorld(door)));
            foreach (var p in zone.SpawnPoints) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Spawn, $"{p.Family} spawn {p.Id}", ToWorld(p.Point.World)));
            uint seed = ZoneRandom.Hash(map.Seed, 0x2A7E, coord.Key, 0);
            var outline = new List<Vector2>();
            foreach (var p in tiling.Outline(coord)) outline.Add(new Vector2(p.X, p.Y));
            return new ZoneRequest(seed, map.Seed, coord, outline, zone.Tile, zone.Bias.Primary, anchors)
            {
                WrapA = new Vector2(tiling.PeriodA.X, tiling.PeriodA.Y),
                WrapB = new Vector2(tiling.PeriodB.X, tiling.PeriodB.Y),
                Layer = map.Layer,
            };
        }

        private static Vector3 ToWorld(System.Numerics.Vector2 p) => new Vector3(p.X, 0f, p.Y);

        public override string ToString() => $"zone {Coord} ({Tile}, {Lean}) seed {Seed}";
    }

    /// <summary>What a generator hands back.</summary>
    public sealed class ZoneResult
    {
        /// <summary>The zone's root object (must be a child of the parent passed to Build).</summary>
        public GameObject Root;

        /// <summary>
        /// The generator's own favourite shots, best first (eye height or anywhere). The gallery shoots the first.
        /// Leave empty to let the gallery pick.
        /// </summary>
        public readonly List<ZoneView> HeroViews = new List<ZoneView>();

        /// <summary>Optional notes printed in the gallery report (biome picked, palette, counts...).</summary>
        public string Notes = "";
    }

    /// <summary>A camera pose for screenshots.</summary>
    public readonly struct ZoneView
    {
        public readonly Vector3 Position;
        public readonly Vector3 LookAt;
        public readonly float FieldOfView;

        public ZoneView(Vector3 position, Vector3 lookAt, float fieldOfView = 70f)
        {
            Position = position;
            LookAt = lookAt;
            FieldOfView = fieldOfView;
        }
    }
}
