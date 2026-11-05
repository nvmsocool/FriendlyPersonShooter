using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Social
{
    public enum SightingKind : byte
    {
        /// <summary>A cluster of enemies of one color.</summary>
        Enemies = 0,
        /// <summary>A bloom.</summary>
        Bloom = 1,
    }

    /// <summary>Tunables for sightings. Seconds.</summary>
    public sealed class SightingSettings
    {
        /// <summary>
        /// How long an enemy sighting stays on the map. 5 minutes: spawns move on with the
        /// schedule, so older pins mislead more than they help.
        /// </summary>
        public double EnemyLifetime { get; set; } = 300.0;

        /// <summary>How long a bloom pin lasts when the bloom's end isn't known.</summary>
        public double BloomLifetime { get; set; } = 240.0;

        /// <summary>Pins a book keeps; the oldest go first.</summary>
        public int MaxSightings { get; set; } = 24;

        /// <summary>
        /// When false (the default) sharing passes on only the zone biases the other player saw
        /// for themselves, as the doc says ("once you or someone you shared sightings with has
        /// visited it"), so knowledge spreads one hop per share and stays worth asking for. True
        /// passes on everything they know.
        /// </summary>
        public bool ShareLearnedBiases { get; set; } = false;
    }

    /// <summary>A private map pin: what was seen, where and when.</summary>
    public sealed class Sighting
    {
        public Sighting(SightingKind kind, EssenceColor color, ZoneCoord zone, int landmarkId, Vector2 position,
            double seenAt, double expiresAt, int seenBy)
        {
            Kind = kind;
            Color = color;
            Zone = zone;
            LandmarkId = landmarkId;
            Position = position;
            SeenAt = seenAt;
            ExpiresAt = expiresAt;
            SeenBy = seenBy;
        }

        public SightingKind Kind { get; }
        public EssenceColor Color { get; }
        public ZoneCoord Zone { get; }
        /// <summary>The nearest landmark when it was seen, or -1 if the layer has none.</summary>
        public int LandmarkId { get; }
        /// <summary>World ground position (x, z).</summary>
        public Vector2 Position { get; }
        public double SeenAt { get; }
        public double ExpiresAt { get; }
        /// <summary>The player who saw it first-hand.</summary>
        public int SeenBy { get; }

        public bool IsExpired(double now) => now >= ExpiresAt;

        /// <summary>Same thing in the same place: same kind, color, zone and landmark.</summary>
        internal bool SameAs(Sighting other) =>
            Kind == other.Kind && Color == other.Color && Zone == other.Zone && LandmarkId == other.LandmarkId;

        /// <summary>The quick-chat line that passes this pin on: "{color} past {landmark}" or "{color} bloom at {landmark}!".</summary>
        public QuickChatMessage ToQuickChat() => Kind == SightingKind.Bloom
            ? QuickChatMessage.BloomAt(Color, LandmarkId)
            : QuickChatMessage.ColorPast(Color, LandmarkId);

        public override string ToString() => $"{Color} {Kind} in {Zone} at {SeenAt:0}s";
    }

    /// <summary>What one share added to a book.</summary>
    public readonly struct SightingShareResult
    {
        public readonly int NewZones;
        public readonly int NewSightings;

        public SightingShareResult(int newZones, int newSightings)
        {
            NewZones = newZones;
            NewSightings = newSightings;
        }

        /// <summary>True when the share taught the receiver something (counts as an info exchange).</summary>
        public bool Useful => NewZones > 0 || NewSightings > 0;
    }

    /// <summary>
    /// One player's map knowledge for the current world seed:
    /// <list type="bullet">
    /// <item>Which zones' color bias they know. A zone's bias shows once they have visited it, or
    /// once someone who visited it shared sightings with them.</item>
    /// <item>Recent private pins (enemy clusters, blooms), which expire.</item>
    /// </list>
    /// Clear it on reseed. Swapping books ("share" while looking at a player) is two
    /// <see cref="MergeFrom"/> calls.
    /// </summary>
    public sealed class SightingBook
    {
        private readonly SightingSettings _settings;
        private readonly HashSet<ZoneCoord> _visited = new HashSet<ZoneCoord>();
        private readonly HashSet<ZoneCoord> _learned = new HashSet<ZoneCoord>();
        private readonly List<Sighting> _sightings = new List<Sighting>();

        public SightingBook(int ownerId, SightingSettings settings = null)
        {
            OwnerId = ownerId;
            _settings = settings ?? new SightingSettings();
        }

        public int OwnerId { get; }
        public SightingSettings Settings => _settings;

        /// <summary>Zones the owner has been in this seed.</summary>
        public IReadOnlyCollection<ZoneCoord> VisitedZones => _visited;

        /// <summary>Every zone whose bias the owner knows (visited or learned).</summary>
        public IReadOnlyCollection<ZoneCoord> KnownBiasZones
        {
            get
            {
                var all = new HashSet<ZoneCoord>(_visited);
                all.UnionWith(_learned);
                return all;
            }
        }

        /// <summary>The owner entered a zone: its bias is now known.</summary>
        public void Visit(ZoneCoord zone) => _visited.Add(zone);

        public bool KnowsBias(ZoneCoord zone) => _visited.Contains(zone) || _learned.Contains(zone);

        /// <summary>The zone's color lean for the map, if the owner knows it.</summary>
        public bool TryGetBias(ZoneCoord zone, LayerMap map, out ZoneColorBias bias)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var wrapped = map.Tiling.Wrap(zone);
            if (!KnowsBias(wrapped))
            {
                bias = default;
                return false;
            }
            bias = map.Zone(wrapped).Bias;
            return true;
        }

        /// <summary>
        /// Records a pin, replacing an older pin of the same thing in the same place. Seeing
        /// something in a zone also means you were there, so the zone counts as visited.
        /// </summary>
        public void Record(Sighting sighting)
        {
            if (sighting == null) throw new ArgumentNullException(nameof(sighting));
            if (sighting.SeenBy == OwnerId) _visited.Add(sighting.Zone);
            AddOrRefresh(sighting);
        }

        /// <summary>The owner saw a cluster of enemies of a color at a position.</summary>
        public Sighting RecordEnemies(EssenceColor color, Vector2 position, LayerMap map, double now)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var landmark = map.NearestLandmark(position);
            var s = new Sighting(SightingKind.Enemies, color, map.Locate(position).Zone, landmark?.Id ?? -1, position,
                now, now + _settings.EnemyLifetime, OwnerId);
            Record(s);
            return s;
        }

        /// <summary>The owner saw a bloom. The pin lasts until the bloom ends.</summary>
        public Sighting RecordBloom(Bloom bloom, LayerMap map, double now)
        {
            if (bloom == null) throw new ArgumentNullException(nameof(bloom));
            if (map == null) throw new ArgumentNullException(nameof(map));
            var position = bloom.Point.World;
            var landmark = map.NearestLandmark(position);
            double expires = bloom.End > now ? bloom.End : now + _settings.BloomLifetime;
            var s = new Sighting(SightingKind.Bloom, bloom.Color, bloom.Zone, landmark?.Id ?? -1, position, now, expires, OwnerId);
            Record(s);
            return s;
        }

        /// <summary>Unexpired pins, newest first.</summary>
        public IReadOnlyList<Sighting> Recent(double now)
        {
            var list = new List<Sighting>();
            foreach (var s in _sightings)
            {
                if (!s.IsExpired(now)) list.Add(s);
            }
            list.Sort((a, b) => b.SeenAt.CompareTo(a.SeenAt));
            return list;
        }

        /// <summary>
        /// The newest unexpired pin of a color (blooms first, since they're worth more), or null.
        /// Used to answer "Seen any {color}?".
        /// </summary>
        public Sighting Latest(EssenceColor color, double now)
        {
            Sighting best = null;
            foreach (var s in _sightings)
            {
                if (s.Color != color || s.IsExpired(now)) continue;
                if (best == null || Better(s, best)) best = s;
            }
            return best;
        }

        /// <summary>Drops expired pins. Returns how many went.</summary>
        public int ExpireOld(double now) => _sightings.RemoveAll(s => s.IsExpired(now));

        /// <summary>
        /// Takes in what another player shares: the biases of zones they visited (see
        /// <see cref="SightingSettings.ShareLearnedBiases"/>) and their unexpired pins. Pins keep
        /// their original finder and expiry.
        /// </summary>
        public SightingShareResult MergeFrom(SightingBook other, double now)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other == this) return new SightingShareResult(0, 0);

            int newZones = 0;
            foreach (var zone in other._visited)
            {
                if (!KnowsBias(zone) && _learned.Add(zone)) newZones++;
            }
            if (_settings.ShareLearnedBiases)
            {
                foreach (var zone in other._learned)
                {
                    if (!KnowsBias(zone) && _learned.Add(zone)) newZones++;
                }
            }

            int newSightings = 0;
            foreach (var s in other._sightings)
            {
                if (s.IsExpired(now)) continue;
                if (AddOrRefresh(s)) newSightings++;
            }
            return new SightingShareResult(newZones, newSightings);
        }

        /// <summary>Forgets everything (the world reseeded).</summary>
        public void Clear()
        {
            _visited.Clear();
            _learned.Clear();
            _sightings.Clear();
        }

        /// <summary>Adds a pin or refreshes an older copy. True if the book gained something newer.</summary>
        private bool AddOrRefresh(Sighting sighting)
        {
            for (int i = 0; i < _sightings.Count; i++)
            {
                if (!_sightings[i].SameAs(sighting)) continue;
                if (sighting.SeenAt <= _sightings[i].SeenAt) return false;
                _sightings[i] = sighting;
                return true;
            }
            _sightings.Add(sighting);
            if (_settings.MaxSightings > 0 && _sightings.Count > _settings.MaxSightings)
            {
                int oldest = 0;
                for (int i = 1; i < _sightings.Count; i++)
                {
                    if (_sightings[i].SeenAt < _sightings[oldest].SeenAt) oldest = i;
                }
                _sightings.RemoveAt(oldest);
            }
            return true;
        }

        private static bool Better(Sighting candidate, Sighting best)
        {
            if (candidate.Kind != best.Kind) return candidate.Kind == SightingKind.Bloom;
            return candidate.SeenAt > best.SeenAt;
        }
    }
}
