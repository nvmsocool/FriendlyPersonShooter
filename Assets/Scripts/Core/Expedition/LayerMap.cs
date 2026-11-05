using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// One generated layer: every zone's theme, color lean, landmarks, shrines and spawn points, plus
    /// the gates and the start point. Built by <see cref="LayerGenerator"/>; identical on every
    /// machine for the same seed and tiling. All lookups measure the short way around the wrap.
    /// World positions are <see cref="Vector2"/> with X = world x and Y = world z, in metres.
    /// </summary>
    public sealed class LayerMap
    {
        private readonly ZoneInfo[] _zones;

        internal LayerMap(uint seed, ZoneTiling tiling, ZoneInfo[] zones, IReadOnlyList<Landmark> landmarks,
            IReadOnlyList<Shrine> shrines, IReadOnlyList<GateSite> gates, ZoneCoord startZone, MapPoint startPoint)
        {
            Seed = seed;
            Tiling = tiling;
            _zones = zones;
            Landmarks = landmarks;
            Shrines = shrines;
            Gates = gates;
            StartZone = startZone;
            StartPoint = startPoint;
        }

        public uint Seed { get; }
        public ZoneTiling Tiling { get; }
        /// <summary>All zones, in the tiling's index order.</summary>
        public IReadOnlyList<ZoneInfo> Zones => _zones;
        public IReadOnlyList<Landmark> Landmarks { get; }
        public IReadOnlyList<Shrine> Shrines { get; }
        public IReadOnlyList<GateSite> Gates { get; }
        /// <summary>The zone new players enter the layer in.</summary>
        public ZoneCoord StartZone { get; }
        /// <summary>Where new players appear: the start zone's first shrine.</summary>
        public MapPoint StartPoint { get; }

        public ZoneInfo Zone(ZoneCoord coord) => _zones[Tiling.IndexOf(coord)];

        /// <summary>The middle of a zone, in world metres.</summary>
        public Vector2 ZoneCentre(ZoneCoord coord) => Tiling.Centre(coord);

        /// <summary>Which zone a world position is in, and its canonical position (wrapping positions outside the world).</summary>
        public MapPoint Locate(Vector2 world)
        {
            var p = Tiling.WrapPoint(world);
            return new MapPoint(Tiling.ZoneAt(p), p);
        }

        /// <summary>The shortest step from one world position to another, going around the wrap if shorter.</summary>
        public Vector2 Delta(Vector2 from, Vector2 to) => Tiling.Delta(from, to);

        /// <summary>Metres between two world positions, the short way around.</summary>
        public float Distance(Vector2 a, Vector2 b) => Tiling.DistanceBetween(a, b);

        /// <summary>The landmark closest to a world position (any zone). Null only if the layer has none.</summary>
        public Landmark NearestLandmark(Vector2 world) => Nearest(Landmarks, world, l => l.Point.World);

        /// <summary>The shrine closest to a world position.</summary>
        public Shrine NearestShrine(Vector2 world) => Nearest(Shrines, world, s => s.Point.World);

        /// <summary>The gate closest to a world position, or null if the layer has none.</summary>
        public GateSite NearestGate(Vector2 world) => Nearest(Gates, world, g => g.Point.World);

        /// <summary>The closest spawn point of a family, or null if no zone spawns it.</summary>
        public SpawnPoint NearestSpawnPoint(Vector2 world, EnemyFamily family)
        {
            SpawnPoint best = null;
            float bestDistance = float.MaxValue;
            foreach (var zone in _zones)
            {
                foreach (var point in zone.SpawnPoints)
                {
                    if (point.Family != family) continue;
                    float d = Distance(world, point.Point.World);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = point;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The closest zone (in zone steps, then metres to its centre) that leans toward a color,
        /// or null if none does.
        /// </summary>
        public ZoneInfo NearestZoneLeaningToward(EssenceColor color, Vector2 world)
        {
            var here = Locate(world).Zone;
            ZoneInfo best = null;
            int bestSteps = int.MaxValue;
            float bestDistance = float.MaxValue;
            foreach (var zone in _zones)
            {
                if (!zone.Bias.LeansToward(color)) continue;
                int steps = Tiling.Distance(here, zone.Coord);
                float d = Distance(world, ZoneCentre(zone.Coord));
                if (steps < bestSteps || (steps == bestSteps && d < bestDistance))
                {
                    best = zone;
                    bestSteps = steps;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>
        /// Directions for the compass and quick-chat: "near the Broken Tower in zone 3.4", using
        /// the nearest landmark and the zone it stands in.
        /// </summary>
        public string Describe(Vector2 world)
        {
            var landmark = NearestLandmark(world);
            if (landmark == null) return $"in zone {Locate(world).Zone}";
            return $"near {landmark.Name} in zone {landmark.Point.Zone}";
        }

        private T Nearest<T>(IReadOnlyList<T> items, Vector2 world, Func<T, Vector2> positionOf) where T : class
        {
            T best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                float d = Distance(world, positionOf(items[i]));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = items[i];
                }
            }
            return best;
        }
    }
}
