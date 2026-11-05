using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for blooms. Seconds and metres.</summary>
    public sealed class BloomSettings
    {
        /// <summary>
        /// Time is cut into slots this long; each zone hosts at most one bloom per slot, so a zone's
        /// blooms never overlap.
        /// </summary>
        public double SlotLength { get; set; } = 360.0;
        /// <summary>
        /// Chance a zone hosts a bloom in a slot. 0.1 on a 4x4 grid is about 1.6 blooms per
        /// 6 minutes, so usually one is running somewhere.
        /// </summary>
        public float ChancePerZonePerSlot { get; set; } = 0.1f;
        /// <summary>"A few minutes": 3–5. Must fit inside a slot.</summary>
        public double MinDuration { get; set; } = 180.0;
        public double MaxDuration { get; set; } = 300.0;
        /// <summary>Bloom spawn groups appear within this distance of the bloom's centre.</summary>
        public float Radius { get; set; } = 15f;
        /// <summary>Spawn groups a bloom adds to each spawn wave.</summary>
        public int SpawnGroups { get; set; } = 3;
        /// <summary>Each bloom group is this many times a normal group's largest size.</summary>
        public int CountMultiplier { get; set; } = 2;
        /// <summary>How many slots ahead <see cref="BloomSchedule.TryGetNext"/> looks before giving up.</summary>
        public int MaxSearchSlots { get; set; } = 240;
    }

    /// <summary>A temporary event where one color spawns heavily in one spot.</summary>
    public sealed class Bloom
    {
        public Bloom(MapPoint point, EssenceColor color, EnemyFamily family, double start, double end, long slot)
        {
            Point = point;
            Color = color;
            Family = family;
            Start = start;
            End = end;
            Slot = slot;
        }

        public MapPoint Point { get; }
        public ZoneCoord Zone => Point.Zone;
        public EssenceColor Color { get; }
        public EnemyFamily Family { get; }
        /// <summary>Seconds on the world clock.</summary>
        public double Start { get; }
        public double End { get; }
        public double Duration => End - Start;
        public long Slot { get; }

        /// <summary>Running at this time (start inclusive, end exclusive).</summary>
        public bool IsActive(double time) => time >= Start && time < End;

        public override string ToString() => $"{Color} bloom in zone {Zone} {Start:0}-{End:0}s";
    }

    /// <summary>
    /// Where and when blooms happen, worked out from the layer seed and the clock alone, so every
    /// lobby agrees without sending anything. Nothing is stored; ask for any time, past or future.
    /// </summary>
    public sealed class BloomSchedule
    {
        private readonly LayerMap _map;
        private readonly BloomSettings _settings;

        public BloomSchedule(LayerMap map, BloomSettings settings = null)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _settings = settings ?? new BloomSettings();
            if (_settings.SlotLength <= 0) throw new ArgumentException("Slot length must be positive.");
            if (_settings.MinDuration <= 0 || _settings.MaxDuration < _settings.MinDuration || _settings.MaxDuration > _settings.SlotLength)
                throw new ArgumentException("Bloom durations must be positive and fit inside a slot.");
        }

        public BloomSettings Settings => _settings;

        public long SlotAt(double time) => (long)Math.Floor(time / _settings.SlotLength);

        /// <summary>The bloom a zone hosts in a slot, or null.</summary>
        public Bloom BloomIn(ZoneCoord zone, long slot)
        {
            var info = _map.Zone(zone);
            var random = new CombatRandom(SeedHash.Of(_map.Seed, SeedHash.BloomSalt, info.Coord.Key,
                SeedHash.Low(slot), SeedHash.High(slot)));
            if (!random.Chance(_settings.ChancePerZonePerSlot)) return null;

            var bias = info.Bias;
            var color = bias.HasSecondary && random.Chance(bias.SecondaryShare) ? bias.Secondary : bias.Primary;
            if (!EssenceColors.TryGetFamily(color, out var family)) return null;

            double duration = _settings.MinDuration + (_settings.MaxDuration - _settings.MinDuration) * random.NextFloat();
            double slotStart = slot * _settings.SlotLength;
            double start = slotStart + (_settings.SlotLength - duration) * random.NextFloat();

            // Centre on one of the zone's spawn points when it has any, so blooms sit in fightable spots.
            MapPoint point;
            if (info.SpawnPoints.Count > 0) point = info.SpawnPoints[random.NextInt(info.SpawnPoints.Count)].Point;
            else point = new MapPoint(info.Coord, _map.ZoneCentre(info.Coord));

            return new Bloom(point, color, family, start, start + duration, slot);
        }

        /// <summary>Every bloom running anywhere on the layer at this time, in zone order.</summary>
        public IReadOnlyList<Bloom> ActiveAt(double time)
        {
            var result = new List<Bloom>();
            long slot = SlotAt(time);
            foreach (var zone in _map.Zones)
            {
                var bloom = BloomIn(zone.Coord, slot);
                if (bloom != null && bloom.IsActive(time)) result.Add(bloom);
            }
            return result;
        }

        /// <summary>The bloom running in one zone at this time, or null.</summary>
        public Bloom ActiveIn(ZoneCoord zone, double time)
        {
            var bloom = BloomIn(zone, SlotAt(time));
            return bloom != null && bloom.IsActive(time) ? bloom : null;
        }

        /// <summary>
        /// The next bloom to start at or after <paramref name="time"/> (optionally of one color).
        /// Ties go to the first zone in row order. False if none starts within
        /// <see cref="BloomSettings.MaxSearchSlots"/> slots.
        /// </summary>
        public bool TryGetNext(double time, out Bloom next, EssenceColor? color = null)
        {
            next = null;
            long first = SlotAt(time);
            for (long slot = first; slot < first + _settings.MaxSearchSlots; slot++)
            {
                foreach (var zone in _map.Zones)
                {
                    var bloom = BloomIn(zone.Coord, slot);
                    if (bloom == null || bloom.Start < time) continue;
                    if (color.HasValue && bloom.Color != color.Value) continue;
                    if (next == null || bloom.Start < next.Start) next = bloom;
                }
                // Later slots start later still, so the first slot with a match holds the answer.
                if (next != null) return true;
            }
            return false;
        }

        /// <summary>The closest running bloom of a color within <paramref name="maxZones"/> zone steps, or null.</summary>
        public Bloom NearestActive(Vector2 world, EssenceColor color, double time, int maxZones = int.MaxValue)
        {
            var here = _map.Locate(world).Zone;
            Bloom best = null;
            float bestDistance = float.MaxValue;
            foreach (var bloom in ActiveAt(time))
            {
                if (bloom.Color != color || _map.Tiling.Distance(here, bloom.Zone) > maxZones) continue;
                float d = _map.Distance(world, bloom.Point.World);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = bloom;
                }
            }
            return best;
        }
    }
}
