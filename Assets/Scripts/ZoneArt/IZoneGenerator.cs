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
        /// <summary>The gate to the next layer; needs flat open ground within <see cref="ZoneRequest.GateClearRadius"/>.</summary>
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
        /// <summary>Side of the square zone in metres.</summary>
        public float Size { get; }
        /// <summary>The zone's minimum corner in world space (y = 0). The zone covers Origin .. Origin + (Size, *, Size).</summary>
        public Vector3 Origin { get; }
        /// <summary>The hand-tile theme the layer picked for this zone (a hint, not a rule).</summary>
        public TileKind Tile { get; }
        /// <summary>The essence color the zone leans toward (a hint for accent colors).</summary>
        public EssenceColor Lean { get; }
        public IReadOnlyList<ZoneAnchor> Anchors { get; }

        /// <summary>Side of the square wrapping world in metres (0 when unknown). Anything that must match across the world's edge repeats at this period.</summary>
        public float WorldSize { get; set; }

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

        public Vector3 Center => Origin + new Vector3(Size * 0.5f, 0f, Size * 0.5f);

        public ZoneRequest(uint seed, uint layerSeed, ZoneCoord coord, float size, TileKind tile, EssenceColor lean, IReadOnlyList<ZoneAnchor> anchors)
        {
            Seed = seed;
            LayerSeed = layerSeed;
            Coord = coord;
            Size = size;
            Origin = new Vector3(coord.X * size, 0f, coord.Y * size);
            Tile = tile;
            Lean = lean;
            Anchors = anchors;
        }

        /// <summary>The request for one zone of a generated layer.</summary>
        public static ZoneRequest FromLayer(LayerMap map, ZoneCoord coord)
        {
            var zone = map.Zone(coord);
            float size = map.Grid.ZoneSize;
            var anchors = new List<ZoneAnchor>();
            foreach (var l in zone.Landmarks) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Landmark, l.Name, ToWorld(l.Point.World)));
            foreach (var s in zone.Shrines) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Shrine, $"Shrine {s.Id}", ToWorld(s.Point.World)));
            if (zone.HasGate) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Gate, "Gate", ToWorld(zone.Gate.Point.World)));
            foreach (var p in zone.SpawnPoints) anchors.Add(new ZoneAnchor(ZoneAnchorKind.Spawn, $"{p.Family} spawn {p.Id}", ToWorld(p.Point.World)));
            uint seed = ZoneRandom.Hash(map.Seed, 0x2A7E, coord.X, coord.Y);
            return new ZoneRequest(seed, map.Seed, coord, size, zone.Tile, zone.Bias.Primary, anchors) { WorldSize = map.Grid.Width * size };
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
