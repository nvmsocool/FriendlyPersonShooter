using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for the open-world spawn schedule. Seconds.</summary>
    public sealed class SpawnSettings
    {
        /// <summary>Time is cut into waves this long; each wave re-rolls which spawn points are active.</summary>
        public double WaveLength { get; set; } = 60.0;
        /// <summary>
        /// Share of a zone's spawn points active in a wave. With 6–10 points and 1–2 Wisps each this is
        /// about 6 Wisps a minute per zone, comfortably above the ~3 kills a minute a solo player manages.
        /// </summary>
        public float ActiveShare { get; set; } = 0.5f;
        public int MinWisps { get; set; } = 1;
        public int MaxWisps { get; set; } = 2;
        public int MinSkitters { get; set; } = 1;
        public int MaxSkitters { get; set; } = 1;
        /// <summary>A Mite group is one swarm of this many Mites.</summary>
        public int MinMites { get; set; } = 8;
        public int MaxMites { get; set; } = 14;

        public int MinCount(EnemyFamily f) => f == EnemyFamily.Wisp ? MinWisps : f == EnemyFamily.Skitter ? MinSkitters : MinMites;
        public int MaxCount(EnemyFamily f) => f == EnemyFamily.Wisp ? MaxWisps : f == EnemyFamily.Skitter ? MaxSkitters : MaxMites;
    }

    /// <summary>A group of enemies that should be alive at one spot for a stretch of time.</summary>
    public readonly struct SpawnEvent
    {
        public readonly MapPoint Point;
        /// <summary>The spawn point's id within its zone, or -1 for a bloom group.</summary>
        public readonly int SpawnPointId;
        public readonly EnemyFamily Family;
        public readonly int Count;
        public readonly double Start;
        public readonly double End;
        public readonly long Wave;
        public readonly bool IsBloom;

        public SpawnEvent(MapPoint point, int spawnPointId, EnemyFamily family, int count, double start, double end, long wave, bool isBloom)
        {
            Point = point;
            SpawnPointId = spawnPointId;
            Family = family;
            Count = count;
            Start = start;
            End = end;
            Wave = wave;
            IsBloom = isBloom;
        }

        public ZoneCoord Zone => Point.Zone;

        public override string ToString() => $"{Count} {Family} at {Point} {Start:0}-{End:0}s{(IsBloom ? " (bloom)" : "")}";
    }

    /// <summary>
    /// Which spawn points are active, with what and how many, worked out from the seed and the clock.
    /// A lobby asks for the zones it has loaded and keeps only the live enemies themselves; any
    /// lobby asking about the same zone and time gets the same answer. Running blooms add extra
    /// groups of their color around the bloom.
    /// </summary>
    public sealed class SpawnSchedule
    {
        private readonly LayerMap _map;
        private readonly BloomSchedule _blooms;
        private readonly SpawnSettings _settings;

        public SpawnSchedule(LayerMap map, BloomSchedule blooms = null, SpawnSettings settings = null)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _blooms = blooms;
            _settings = settings ?? new SpawnSettings();
            if (_settings.WaveLength <= 0) throw new ArgumentException("Wave length must be positive.");
        }

        public SpawnSettings Settings => _settings;

        public long WaveAt(double time) => (long)Math.Floor(time / _settings.WaveLength);

        /// <summary>Groups that should be alive in a zone at this moment.</summary>
        public IReadOnlyList<SpawnEvent> ActiveAt(ZoneCoord zone, double time) => Between(zone, time, time);

        /// <summary>Every group in a zone whose time overlaps [<paramref name="from"/>, <paramref name="to"/>].</summary>
        public IReadOnlyList<SpawnEvent> Between(ZoneCoord zone, double from, double to)
        {
            if (to < from) throw new ArgumentException("The window ends before it starts.");
            var info = _map.Zone(zone);
            var result = new List<SpawnEvent>();
            for (long wave = WaveAt(from); wave <= WaveAt(to); wave++)
            {
                AddWave(info, wave, from, to, result);
            }
            return result;
        }

        private void AddWave(ZoneInfo info, long wave, double from, double to, List<SpawnEvent> result)
        {
            double waveStart = wave * _settings.WaveLength;
            double waveEnd = waveStart + _settings.WaveLength;
            var coord = info.Coord;

            foreach (var point in info.SpawnPoints)
            {
                var random = new CombatRandom(SeedHash.Of(_map.Seed, SeedHash.SpawnSalt, coord.Key, point.Id,
                    SeedHash.Low(wave), SeedHash.High(wave)));
                if (!random.Chance(_settings.ActiveShare)) continue;
                int count = Count(point.Family, ref random);
                result.Add(new SpawnEvent(point.Point, point.Id, point.Family, count, waveStart, waveEnd, wave, false));
            }

            if (_blooms == null) return;
            long firstSlot = _blooms.SlotAt(waveStart);
            long lastSlot = _blooms.SlotAt(waveEnd - 1e-6);
            for (long slot = firstSlot; slot <= lastSlot; slot++)
            {
                var bloom = _blooms.BloomIn(coord, slot);
                if (bloom == null) continue;
                double start = Math.Max(waveStart, bloom.Start);
                double end = Math.Min(waveEnd, bloom.End);
                if (end <= start || start > to || end <= from) continue;

                var random = new CombatRandom(SeedHash.Of(_map.Seed, SeedHash.BloomSalt + 100, coord.Key,
                    SeedHash.Low(wave), SeedHash.High(wave)));
                var settings = _blooms.Settings;
                int count = _settings.MaxCount(bloom.Family) * Math.Max(1, settings.CountMultiplier);
                for (int g = 0; g < settings.SpawnGroups; g++)
                {
                    float angle = random.Angle();
                    float radius = settings.Radius * MathF.Sqrt(random.NextFloat());
                    var world = bloom.Point.World + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    // Groups that would land outside the zone are pulled back to the bloom centre.
                    if (_map.Tiling.EdgeDistance(coord, world) < 0f) world = bloom.Point.World;
                    result.Add(new SpawnEvent(new MapPoint(coord, world), -1, bloom.Family, count, start, end, wave, true));
                }
            }
        }

        private int Count(EnemyFamily family, ref CombatRandom random)
        {
            int min = _settings.MinCount(family);
            int max = Math.Max(min, _settings.MaxCount(family));
            return min + random.NextInt(max - min + 1);
        }
    }
}
