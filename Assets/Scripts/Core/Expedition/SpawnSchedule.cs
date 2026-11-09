using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for open-world spawning. Seconds and metres.</summary>
    public sealed class SpawnSettings
    {
        /// <summary>Share of a zone's spawn points in use on a layer, picked once from the seed. The rest stay empty.</summary>
        public float ActiveShare { get; set; } = 0.5f;
        /// <summary>After a spawn point's whole group is defeated, it waits this long before it can refill.</summary>
        public double RespawnSeconds { get; set; } = 120.0;
        /// <summary>A spawn point only refills while no player is this close (metres), so nothing pops in on top of anyone.</summary>
        public float ClearRadius { get; set; } = 100f;
        /// <summary>A cleared bloom group comes back this fast while its bloom runs (players are expected right there).</summary>
        public double BloomRefillSeconds { get; set; } = 20.0;
        public int MinWisps { get; set; } = 1;
        public int MaxWisps { get; set; } = 2;
        public int MinSkitters { get; set; } = 1;
        public int MaxSkitters { get; set; } = 1;
        /// <summary>A Mite group is one swarm of this many Mites.</summary>
        public int MinMites { get; set; } = 8;
        public int MaxMites { get; set; } = 14;
        public int MinSentinels { get; set; } = 1;
        public int MaxSentinels { get; set; } = 1;
        public int MinDivers { get; set; } = 1;
        public int MaxDivers { get; set; } = 2;
        /// <summary>Bulwarks come alone: one is a long fight.</summary>
        public int MinBulwarks { get; set; } = 1;
        public int MaxBulwarks { get; set; } = 1;
        /// <summary>
        /// Chance a group of a family that has an elite kind (see <see cref="Elites.For"/>) is led by an elite.
        /// Rises with the layer (see <see cref="LayerSettings.EliteChance"/>).
        /// </summary>
        public float EliteChance { get; set; } = 0.06f;

        public int MinCount(EnemyFamily f)
        {
            switch (f)
            {
                case EnemyFamily.Wisp: return MinWisps;
                case EnemyFamily.Skitter: return MinSkitters;
                case EnemyFamily.Sentinel: return MinSentinels;
                case EnemyFamily.Diver: return MinDivers;
                case EnemyFamily.Bulwark: return MinBulwarks;
                default: return MinMites;
            }
        }

        public int MaxCount(EnemyFamily f)
        {
            switch (f)
            {
                case EnemyFamily.Wisp: return MaxWisps;
                case EnemyFamily.Skitter: return MaxSkitters;
                case EnemyFamily.Sentinel: return MaxSentinels;
                case EnemyFamily.Diver: return MaxDivers;
                case EnemyFamily.Bulwark: return MaxBulwarks;
                default: return MaxMites;
            }
        }
    }

    /// <summary>Where a group of enemies came from.</summary>
    public enum SpawnSource : byte
    {
        /// <summary>Not from a group (debug spawns, raid enemies, Glimmers).</summary>
        None = 0,
        /// <summary>A zone's spawn point. It refills after it's cleared (see <see cref="SpawnSchedule"/>).</summary>
        Point = 1,
        /// <summary>One of a running bloom's groups.</summary>
        Bloom = 2,
        /// <summary>Adds called in by a boss; they leave after a while.</summary>
        Adds = 3,
    }

    /// <summary>Identifies one group of enemies: a spawn point, a bloom's group, or a boss's adds.</summary>
    public readonly struct SpawnGroupId : IEquatable<SpawnGroupId>
    {
        public readonly SpawnSource Source;
        /// <summary>The zone's <see cref="ZoneCoord.Key"/> (0 for adds).</summary>
        public readonly int Zone;
        /// <summary>The spawn point's id, or the bloom group's index.</summary>
        public readonly int Slot;
        /// <summary>The bloom's slot, or the adds' serial number (0 for spawn points).</summary>
        public readonly long Serial;

        private SpawnGroupId(SpawnSource source, int zone, int slot, long serial)
        {
            Source = source;
            Zone = zone;
            Slot = slot;
            Serial = serial;
        }

        public static SpawnGroupId Point(int zoneKey, int pointId) => new SpawnGroupId(SpawnSource.Point, zoneKey, pointId, 0);
        public static SpawnGroupId Bloom(int zoneKey, int group, long bloomSlot) => new SpawnGroupId(SpawnSource.Bloom, zoneKey, group, bloomSlot);
        public static SpawnGroupId Adds(long serial) => new SpawnGroupId(SpawnSource.Adds, 0, 0, serial);

        /// <summary>False for the default id, which enemies not made by a group carry.</summary>
        public bool IsSet => Source != SpawnSource.None;

        public bool Equals(SpawnGroupId o) => Source == o.Source && Zone == o.Zone && Slot == o.Slot && Serial == o.Serial;
        public override bool Equals(object obj) => obj is SpawnGroupId o && Equals(o);
        public override int GetHashCode() => ((Zone * 397) ^ (Slot * 7919) ^ Serial.GetHashCode()) * 4 + (int)Source;
        public override string ToString() => $"{Source} {ZoneCoord.FromKey(Zone)}/{Slot}/{Serial}";
    }

    /// <summary>A group of enemies to spawn now at one spot.</summary>
    public readonly struct SpawnEvent
    {
        public readonly SpawnGroupId Group;
        public readonly MapPoint Point;
        /// <summary>The spawn point's id within its zone, or -1 for a bloom group.</summary>
        public readonly int SpawnPointId;
        public readonly EnemyFamily Family;
        public readonly int Count;
        /// <summary>When the group's time is up (its bloom's end), or infinity for a spawn point's group, which stays until killed.</summary>
        public readonly double End;
        /// <summary>How many groups this spot has had before (0 = the first).</summary>
        public readonly int Generation;
        /// <summary>The group is led by an elite of this kind (one of its <see cref="Count"/>), or None.</summary>
        public readonly EliteKind Elite;

        public SpawnEvent(SpawnGroupId group, MapPoint point, int spawnPointId, EnemyFamily family, int count, double end, int generation,
            EliteKind elite = EliteKind.None)
        {
            Group = group;
            Point = point;
            SpawnPointId = spawnPointId;
            Family = family;
            Count = count;
            End = end;
            Generation = generation;
            Elite = elite;
        }

        public bool IsBloom => Group.Source == SpawnSource.Bloom;
        public ZoneCoord Zone => Point.Zone;

        public override string ToString() =>
            $"{Count} {Family}{(Elite != EliteKind.None ? $" ({Elite} elite)" : "")} at {Point} #{Generation}{(IsBloom ? $" (bloom until {End:0}s)" : "")}";
    }

    /// <summary>
    /// Which groups of enemies to spawn, and when. There are no waves: enemies stay until they're killed.
    /// - Each zone uses a seeded share of its spawn points (<see cref="SpawnSettings.ActiveShare"/>). The first time
    ///   a zone is near players, each fills with its own family.
    /// - Once a point's whole group is dead, it waits <see cref="SpawnSettings.RespawnSeconds"/>. After that, as soon as
    ///   no player is within <see cref="SpawnSettings.ClearRadius"/>, it refills with a new random enemy from the zone's mix.
    /// - A running bloom adds groups of its color around it. A cleared bloom group comes back quickly while the bloom
    ///   runs; when the bloom ends, the host sends any survivors away once nobody is near them.
    /// The host reports what it really spawned (<see cref="Spawned"/>) and every death (<see cref="Lost"/>).
    /// </summary>
    public sealed class SpawnSchedule
    {
        private sealed class GroupState
        {
            public int Alive;
            public int Generation;
            public double DefeatedAt;
        }

        private static readonly EnemyFamily[] Families = (EnemyFamily[])Enum.GetValues(typeof(EnemyFamily));

        private readonly LayerMap _map;
        private readonly BloomSchedule _blooms;
        private readonly SpawnSettings _settings;
        private readonly Dictionary<SpawnGroupId, GroupState> _groups = new Dictionary<SpawnGroupId, GroupState>();

        public SpawnSchedule(LayerMap map, BloomSchedule blooms = null, SpawnSettings settings = null)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _blooms = blooms;
            _settings = settings ?? new SpawnSettings();
            if (_settings.RespawnSeconds < 0) throw new ArgumentException("Respawn time can't be negative.");
        }

        public SpawnSettings Settings => _settings;

        /// <summary>True if this spawn point is in use on the layer (seeded, fixed for the layer).</summary>
        public bool IsActive(ZoneCoord zone, int pointId)
        {
            var random = new CombatRandom(SeedHash.Of(_map.Seed, SeedHash.SpawnSalt, zone.Key, pointId, -1));
            return random.Chance(_settings.ActiveShare);
        }

        /// <summary>Living enemies of a group, as far as the schedule knows.</summary>
        public int AliveIn(SpawnGroupId group) => _groups.TryGetValue(group, out var g) ? g.Alive : 0;

        /// <summary>
        /// Groups to spawn in a zone now, given where the players stand. Each returned group counts as alive until the
        /// host reports its deaths; the host calls <see cref="Spawned"/> with how many enemies it really made.
        /// </summary>
        public IReadOnlyList<SpawnEvent> Due(ZoneCoord zone, double time, IReadOnlyList<Vector2> players)
        {
            var info = _map.Zone(zone);
            var coord = info.Coord;
            var result = new List<SpawnEvent>();

            foreach (var point in info.SpawnPoints)
            {
                if (!IsActive(coord, point.Id)) continue;
                var id = SpawnGroupId.Point(coord.Key, point.Id);
                var g = State(id);
                if (g.Alive > 0) continue;
                if (g.Generation > 0
                    && (time - g.DefeatedAt < _settings.RespawnSeconds || AnyPlayerWithin(point.Point.World, players, _settings.ClearRadius))) continue;

                var random = new CombatRandom(SeedHash.Of(_map.Seed, SeedHash.SpawnSalt, coord.Key, point.Id, g.Generation));
                // The first group is the point's own family; later ones are a new roll from the zone's mix.
                var family = g.Generation == 0 ? point.Family : PickFamily(info, ref random);
                int count = Count(family, ref random);
                var elite = Elites.For(family);
                if (elite != EliteKind.None && !random.Chance(_settings.EliteChance)) elite = EliteKind.None;
                result.Add(new SpawnEvent(id, point.Point, point.Id, family, count, double.PositiveInfinity, g.Generation, elite));
                g.Alive = count;
                g.Generation++;
            }

            if (_blooms != null) AddBloom(info, time, result);
            return result;
        }

        private void AddBloom(ZoneInfo info, double time, List<SpawnEvent> result)
        {
            var coord = info.Coord;
            long slot = _blooms.SlotAt(time);
            var bloom = _blooms.BloomIn(coord, slot);
            if (bloom == null || time < bloom.Start || time >= bloom.End) return;

            var settings = _blooms.Settings;
            int count = _settings.MaxCount(bloom.Family) * Math.Max(1, settings.CountMultiplier);
            for (int i = 0; i < settings.SpawnGroups; i++)
            {
                var id = SpawnGroupId.Bloom(coord.Key, i, slot);
                var g = State(id);
                if (g.Alive > 0) continue;
                if (g.Generation > 0 && time - g.DefeatedAt < _settings.BloomRefillSeconds) continue;

                var random = new CombatRandom(SeedHash.Of(_map.Seed, SeedHash.BloomSalt + 100, coord.Key,
                    SeedHash.Low(slot), SeedHash.High(slot), i * 1000 + g.Generation));
                float angle = random.Angle();
                float radius = settings.Radius * MathF.Sqrt(random.NextFloat());
                var world = bloom.Point.World + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                // Groups that would land outside the zone are pulled back to the bloom centre.
                if (_map.Tiling.EdgeDistance(coord, world) < 0f) world = bloom.Point.World;
                result.Add(new SpawnEvent(id, new MapPoint(coord, world), -1, bloom.Family, count, bloom.End, g.Generation));
                g.Alive = count;
                g.Generation++;
            }
        }

        /// <summary>The host made this many enemies for a group it was told to spawn (0 counts as cleared at once).</summary>
        public void Spawned(SpawnGroupId group, int count, double time)
        {
            if (!_groups.TryGetValue(group, out var g)) return;
            g.Alive = Math.Max(0, count);
            if (g.Alive == 0) g.DefeatedAt = time;
        }

        /// <summary>One enemy of a group died. The last one starts the group's wait to come back.</summary>
        public void Lost(SpawnGroupId group, double time)
        {
            if (!_groups.TryGetValue(group, out var g) || g.Alive <= 0) return;
            if (--g.Alive == 0) g.DefeatedAt = time;
        }

        /// <summary>True if any player is within <paramref name="radius"/> of a spot, measured the short way around the wrap.</summary>
        public bool AnyPlayerWithin(Vector2 at, IReadOnlyList<Vector2> players, float radius)
        {
            if (players == null) return false;
            float r2 = radius * radius;
            foreach (var p in players)
            {
                if (_map.Tiling.Delta(at, p).LengthSquared() <= r2) return true;
            }
            return false;
        }

        private GroupState State(SpawnGroupId id)
        {
            if (!_groups.TryGetValue(id, out var g)) _groups[id] = g = new GroupState();
            return g;
        }

        /// <summary>A family from the zone's mix, by its weights (Glimmers never come from spawn points).</summary>
        private static EnemyFamily PickFamily(ZoneInfo info, ref CombatRandom random)
        {
            float total = 0f;
            foreach (var f in Families) total += Weight(info, f);
            float roll = random.NextFloat() * total;
            var last = info.SpawnPoints.Count > 0 ? info.SpawnPoints[0].Family : EnemyFamily.Wisp;
            foreach (var f in Families)
            {
                float w = Weight(info, f);
                if (w <= 0f) continue;
                last = f;
                if (roll < w) return f;
                roll -= w;
            }
            return last;
        }

        private static float Weight(ZoneInfo info, EnemyFamily f) => f == EnemyFamily.Glimmer ? 0f : info.FamilyWeight(f);

        private int Count(EnemyFamily family, ref CombatRandom random)
        {
            int min = _settings.MinCount(family);
            int max = Math.Max(min, _settings.MaxCount(family));
            return min + random.NextInt(max - min + 1);
        }
    }
}
