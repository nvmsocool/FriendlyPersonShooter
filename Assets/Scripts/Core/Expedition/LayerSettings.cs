using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for generating one layer. Distances in metres.</summary>
    public sealed class LayerSettings
    {
        /// <summary>Colors zones can lean toward. Each must have an enemy family (Yellow, Red, Green for now).</summary>
        public IReadOnlyList<EssenceColor> Palette { get; set; } =
            new[] { EssenceColor.Yellow, EssenceColor.Red, EssenceColor.Green };

        /// <summary>
        /// When set, every zone leans only toward this color and spawns only its family, whatever the
        /// palette says (the MVP's "one essence color").
        /// </summary>
        public EssenceColor? ForcedColor { get; set; }

        /// <summary>Chance a zone leans toward a second color as well as its main one.</summary>
        public float SecondColorChance { get; set; } = 0.4f;
        /// <summary>Range for the second color's share of the zone's spawns.</summary>
        public float MinSecondaryShare { get; set; } = 0.25f;
        public float MaxSecondaryShare { get; set; } = 0.45f;
        /// <summary>
        /// Spawn weight for palette colors a zone doesn't lean toward (the main color has weight 1
        /// before normalising), so a zone "leans" rather than being pure.
        /// </summary>
        public float OffBiasWeight { get; set; } = 0.05f;

        /// <summary>Tile kinds zones are picked from.</summary>
        public IReadOnlyList<TileKind> Tiles { get; set; } = new[]
        {
            TileKind.Meadow, TileKind.Ruins, TileKind.River, TileKind.Tower,
            TileKind.Canyon, TileKind.Forest, TileKind.Lake, TileKind.Crater,
        };

        // Feature counts are for a hexagon; squares and triangles get their share by area (at least one each).
        public int MinLandmarks { get; set; } = 1;
        public int MaxLandmarks { get; set; } = 3;
        public int MinShrines { get; set; } = 2;
        public int MaxShrines { get; set; } = 3;
        public int MinSpawnPoints { get; set; } = 6;
        public int MaxSpawnPoints { get; set; } = 10;

        /// <summary>Gate zones in the layer. The MVP has exactly one.</summary>
        public int GateCount { get; set; } = 1;
        /// <summary>
        /// Gates sit in zones whose centre is at least this share of the farthest zone centre's distance
        /// (in metres, the short way around) from the start point.
        /// </summary>
        public float GateDistanceShare { get; set; } = 0.85f;

        /// <summary>Features stay this far inside a zone's edges (capped at half the zone's inner radius).</summary>
        public float EdgeMargin { get; set; } = 20f;
        /// <summary>Features try to stay at least this far apart within a zone.</summary>
        public float FeatureSpacing { get; set; } = 30f;
        /// <summary>Placement attempts per feature before taking the best one found.</summary>
        public int PlacementTries { get; set; } = 12;

        /// <summary>MVP step 4: Yellow only, one gate.</summary>
        public static LayerSettings ForMvp()
        {
            return new LayerSettings
            {
                Palette = new[] { EssenceColor.Yellow },
                ForcedColor = EssenceColor.Yellow,
                GateCount = 1,
            };
        }

        /// <summary>
        /// The playtest layer: zones lean toward Yellow, Red or Green (Wisps, Skitters, Mites), so the
        /// raid key's Red and Green can be hunted; one gate (still Yellow).
        /// </summary>
        public static LayerSettings ForPlaytest()
        {
            return new LayerSettings { GateCount = 1 };
        }

        /// <summary>
        /// The MVP world: 3x3 repeating cells of a tiling with 125 m edges (54 zones: 9 hexagons about 250 m
        /// across, 27 squares, 18 triangles), about 1 km on a side. The farthest point is about 590 m from the
        /// start, so the gate is 2–3 minutes of walking at 5 m/s, leaving most of 20 minutes for hunting.
        /// </summary>
        public static ZoneTiling MvpTiling() => new ZoneTiling(3, 3, 125f);
    }
}
