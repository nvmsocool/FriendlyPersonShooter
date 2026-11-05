using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>The terrain theme a zone is built with. Decides which landmark words fit it.</summary>
    public enum TileKind : byte
    {
        Meadow = 0,
        Ruins = 1,
        River = 2,
        Tower = 3,
        Canyon = 4,
        Forest = 5,
        Lake = 6,
        Crater = 7,
    }

    /// <summary>
    /// A spot on the layer: its zone and its world position. Ground positions are <see cref="Vector2"/>
    /// with X = world x and Y = world z, in metres.
    /// </summary>
    public readonly struct MapPoint
    {
        public readonly ZoneCoord Zone;
        /// <summary>World ground position (x, z), in the canonical copy of the zone (see <see cref="ZoneTiling.WrapPoint"/>).</summary>
        public readonly Vector2 World;

        public MapPoint(ZoneCoord zone, Vector2 world)
        {
            Zone = zone;
            World = world;
        }

        public override string ToString() => $"{Zone} ({World.X:0.#}, {World.Y:0.#})";
    }

    /// <summary>A named place players give directions by ("the Broken Tower", "Twin Falls").</summary>
    public sealed class Landmark
    {
        public Landmark(int id, string name, MapPoint point)
        {
            Id = id;
            Name = name;
            Point = point;
        }

        /// <summary>Unique within the layer.</summary>
        public int Id { get; }
        /// <summary>Unique within the layer, ready to drop into a sentence ("near the Broken Tower").</summary>
        public string Name { get; }
        public MapPoint Point { get; }

        public override string ToString() => Name;
    }

    /// <summary>A shrine: banks carried essence and is where downed players respawn.</summary>
    public sealed class Shrine
    {
        public Shrine(int id, MapPoint point)
        {
            Id = id;
            Point = point;
        }

        /// <summary>Unique within the layer.</summary>
        public int Id { get; }
        public MapPoint Point { get; }
    }

    /// <summary>A place enemies of one family appear. Whether it is active comes from the spawn schedule.</summary>
    public sealed class SpawnPoint
    {
        public SpawnPoint(int id, EnemyFamily family, MapPoint point)
        {
            Id = id;
            Family = family;
            Point = point;
        }

        /// <summary>Index within its zone.</summary>
        public int Id { get; }
        public EnemyFamily Family { get; }
        public MapPoint Point { get; }
    }

    /// <summary>Where a gate down to the next layer stands.</summary>
    public sealed class GateSite
    {
        public GateSite(int id, MapPoint point)
        {
            Id = id;
            Point = point;
        }

        /// <summary>Unique within the layer.</summary>
        public int Id { get; }
        public MapPoint Point { get; }
    }

    /// <summary>The one or two essence colors a zone leans toward.</summary>
    public readonly struct ZoneColorBias
    {
        public readonly EssenceColor Primary;
        public readonly bool HasSecondary;
        /// <summary>Only meaningful when <see cref="HasSecondary"/>.</summary>
        public readonly EssenceColor Secondary;
        /// <summary>The secondary color's share of the lean (0 when there is none).</summary>
        public readonly float SecondaryShare;

        public ZoneColorBias(EssenceColor primary)
        {
            Primary = primary;
            HasSecondary = false;
            Secondary = primary;
            SecondaryShare = 0f;
        }

        public ZoneColorBias(EssenceColor primary, EssenceColor secondary, float secondaryShare)
        {
            if (primary == secondary) throw new ArgumentException("The two colors must differ.");
            Primary = primary;
            HasSecondary = true;
            Secondary = secondary;
            SecondaryShare = secondaryShare;
        }

        /// <summary>True if the zone leans toward this color at all.</summary>
        public bool LeansToward(EssenceColor color) => color == Primary || (HasSecondary && color == Secondary);

        /// <summary>This color's share of the lean: 1 - share for the main color, share for the second, else 0.</summary>
        public float Weight(EssenceColor color)
        {
            if (color == Primary) return 1f - SecondaryShare;
            if (HasSecondary && color == Secondary) return SecondaryShare;
            return 0f;
        }

        public override string ToString() => HasSecondary ? $"{Primary}+{Secondary}" : Primary.ToString();
    }

    /// <summary>Everything generated for one zone.</summary>
    public sealed class ZoneInfo
    {
        private readonly float[] _familyWeights;

        internal ZoneInfo(ZoneCoord coord, TileKind tile, ZoneColorBias bias, float[] familyWeights,
            IReadOnlyList<Landmark> landmarks, IReadOnlyList<Shrine> shrines, IReadOnlyList<SpawnPoint> spawnPoints)
        {
            Coord = coord;
            Tile = tile;
            Bias = bias;
            _familyWeights = familyWeights;
            Landmarks = landmarks;
            Shrines = shrines;
            SpawnPoints = spawnPoints;
        }

        public ZoneCoord Coord { get; }
        public TileKind Tile { get; }
        public ZoneColorBias Bias { get; }
        public IReadOnlyList<Landmark> Landmarks { get; }
        public IReadOnlyList<Shrine> Shrines { get; }
        public IReadOnlyList<SpawnPoint> SpawnPoints { get; }
        /// <summary>The gate in this zone, or null.</summary>
        public GateSite Gate { get; internal set; }
        public bool HasGate => Gate != null;

        /// <summary>Share of this zone's spawns that should be this family (all families add to 1).</summary>
        public float FamilyWeight(EnemyFamily family) => _familyWeights[(int)family];
    }
}
