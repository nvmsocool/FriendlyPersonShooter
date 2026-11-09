using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for generating one layer. Distances in metres.</summary>
    public sealed class LayerSettings
    {
        /// <summary>Colors zones can lean toward (each spawns its family). All six by default.</summary>
        public IReadOnlyList<EssenceColor> Palette { get; set; } = new[]
        {
            EssenceColor.Yellow, EssenceColor.Red, EssenceColor.Green, EssenceColor.Blue, EssenceColor.Purple, EssenceColor.Orange,
        };

        /// <summary>Which layer this is (1 = the top; deeper is harder).</summary>
        public int Layer { get; set; } = 1;
        /// <summary>Enemy difficulty tier (see <see cref="Combat.EnemyTiers"/>): the layer number.</summary>
        public int Tier { get; set; } = 1;
        /// <summary>Chance a spawn group of a family with an elite kind is led by an elite (see <see cref="SpawnSettings.EliteChance"/>).</summary>
        public float EliteChance { get; set; } = 0.06f;
        /// <summary>Share of each zone's spawn points in use on the layer (see <see cref="SpawnSettings.ActiveShare"/>).</summary>
        public float ActiveShare { get; set; } = 0.5f;
        /// <summary>Chance a zone hosts a bloom in a slot (see <see cref="BloomSettings.ChancePerZonePerSlot"/>).</summary>
        public float BloomChance { get; set; } = 0.1f;
        /// <summary>The gate down: the color it costs and its first price.</summary>
        public EssenceColor GateColor { get; set; } = EssenceColor.Yellow;
        public int GateCost { get; set; } = 40;
        /// <summary>The deepest layer: its gate ends the expedition instead of leading further down.</summary>
        public bool Deepest { get; set; }

        /// <summary>How many layers the world has.</summary>
        public const int LayerCount = 3;

        /// <summary>Layer 1 uses the world seed as is; deeper layers get their own seed from it, so each layer is a new map.</summary>
        public static uint SeedFor(uint worldSeed, int layer) =>
            layer <= 1 ? worldSeed : SeedHash.Of(worldSeed, SeedHash.DepthSalt, layer);

        public SpawnSettings Spawns() => new SpawnSettings { EliteChance = EliteChance, ActiveShare = ActiveShare };

        public BloomSettings Blooms() => new BloomSettings { ChancePerZonePerSlot = BloomChance };

        public GateSettings Gate() => new GateSettings { Color = GateColor, Cost = GateCost };

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

        /// <summary>The playtest's first layer (see <see cref="ForLayer"/>).</summary>
        public static LayerSettings ForPlaytest() => ForLayer(1);

        /// <summary>
        /// The three layers:
        /// 1. Tier 1, every color, a few elites; the gate costs 40 Yellow.
        /// 2. Tier 2, denser spawns, more zones mixing two colors, and many more elites; the gate costs 40 Blue.
        /// 3. Tier 3, only the hardest families (Skitters, Sentinels, Bulwarks, Divers), elites everywhere and blooms half
        ///    as often; its gate (40 Purple) ends the expedition.
        /// </summary>
        public static LayerSettings ForLayer(int layer)
        {
            layer = System.Math.Max(1, System.Math.Min(LayerCount, layer));
            var s = new LayerSettings { Layer = layer, Tier = layer, GateCount = 1, Deepest = layer == LayerCount };
            if (layer == 2)
            {
                s.EliteChance = 0.2f;
                s.ActiveShare = 0.6f;
                s.SecondColorChance = 0.7f;
                s.GateColor = EssenceColor.Blue;
            }
            else if (layer == 3)
            {
                s.Palette = new[] { EssenceColor.Red, EssenceColor.Blue, EssenceColor.Purple, EssenceColor.Orange };
                s.EliteChance = 0.3f;
                s.ActiveShare = 0.6f;
                s.SecondColorChance = 0.6f;
                s.BloomChance = 0.05f;
                s.GateColor = EssenceColor.Purple;
            }
            return s;
        }

        /// <summary>
        /// The MVP world: 3x3 repeating cells of a tiling with 125 m edges (54 zones: 9 hexagons about 250 m
        /// across, 27 squares, 18 triangles), about 1 km on a side. The farthest point is about 590 m from the
        /// start, so the gate is 2–3 minutes of walking at 5 m/s, leaving most of 20 minutes for hunting.
        /// </summary>
        public static ZoneTiling MvpTiling() => new ZoneTiling(3, 3, 125f);
    }
}
