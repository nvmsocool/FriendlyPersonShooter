using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for the world boss. Seconds and metres.</summary>
    public sealed class WorldBossSettings
    {
        /// <summary>The first one lands this long after the server starts.</summary>
        public double FirstSpawn { get; set; } = 240.0;
        /// <summary>The next one lands this long after the last one fell.</summary>
        public double RespawnDelay { get; set; } = 480.0;
        /// <summary>Health is the raid Bulwark's for this many players at least, times <see cref="HealthMultiplier"/>.</summary>
        public int MinPlayers { get; set; } = 3;
        public float HealthMultiplier { get; set; } = 1.5f;
        /// <summary>Each player who hit it gets this much of each of Yellow, Red and Green.</summary>
        public int EssenceEach { get; set; } = 12;
        /// <summary>XP for each player who hit it.</summary>
        public int Xp { get; set; } = 300;
        /// <summary>It lands at a landmark this far from the nearest player, if one is in range.</summary>
        public float MinDistance { get; set; } = 90f;
        public float MaxDistance { get; set; } = 320f;
    }

    /// <summary>
    /// The world boss: the Colossus (see <see cref="ColossusBehavior"/>), which lands near a landmark close to the
    /// players on a timer. Anyone can join the fight, no key needed; everyone who hit it is paid in all three colors.
    /// One at a time; the next lands a while after the last one falls.
    /// </summary>
    public sealed class WorldBoss
    {
        private static readonly EssenceColor[] RewardColors = { EssenceColor.Yellow, EssenceColor.Red, EssenceColor.Green };

        public WorldBoss(WorldBossSettings settings = null, double startTime = 0.0)
        {
            Settings = settings ?? new WorldBossSettings();
            NextSpawnAt = startTime + Settings.FirstSpawn;
        }

        public WorldBossSettings Settings { get; }
        public bool Alive { get; private set; }
        /// <summary>When the next one lands (meaningless while one is alive).</summary>
        public double NextSpawnAt { get; private set; }
        /// <summary>Where the live one landed.</summary>
        public Landmark Site { get; private set; }
        public int Spawns { get; private set; }
        public int Kills { get; private set; }

        public bool Due(double now) => !Alive && now >= NextSpawnAt;

        public void Spawned(Landmark site)
        {
            Alive = true;
            Site = site;
            Spawns++;
        }

        public void Killed(double now)
        {
            if (!Alive) return;
            Alive = false;
            Site = null;
            Kills++;
            NextSpawnAt = now + Settings.RespawnDelay;
        }

        /// <summary>Health for <paramref name="players"/> players online.</summary>
        public float Health(int players, CombatSettings combat) =>
            BulwarkSettings.HealthFor(Math.Max(Settings.MinPlayers, players), combat) * Settings.HealthMultiplier;

        /// <summary>What everyone who hit it gets: <see cref="WorldBossSettings.EssenceEach"/> of each color.</summary>
        public List<EssenceAward> Rewards(IEnumerable<int> hitters)
        {
            var awards = new List<EssenceAward>();
            if (hitters == null) return awards;
            foreach (int id in hitters)
            {
                foreach (var c in RewardColors) awards.Add(new EssenceAward(id, c, Settings.EssenceEach));
            }
            return awards;
        }

        /// <summary>
        /// A landmark to land at: one between <see cref="WorldBossSettings.MinDistance"/> and
        /// <see cref="WorldBossSettings.MaxDistance"/> from the nearest player if any (random among them),
        /// else the nearest landmark beyond the minimum, else any. Null if the map has none.
        /// </summary>
        public Landmark PickSite(LayerMap map, IReadOnlyList<Vector2> players, uint seed)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Landmarks.Count == 0) return null;
            var random = new CombatRandom(seed == 0 ? 1u : seed);
            if (players == null || players.Count == 0) return map.Landmarks[random.NextInt(map.Landmarks.Count)];

            var inRange = new List<Landmark>();
            Landmark fallback = null;
            float fallbackDistance = float.MaxValue;
            foreach (var l in map.Landmarks)
            {
                float nearest = float.MaxValue;
                foreach (var p in players) nearest = Math.Min(nearest, map.Distance(l.Point.World, p));
                if (nearest >= Settings.MinDistance && nearest <= Settings.MaxDistance) inRange.Add(l);
                else if (nearest >= Settings.MinDistance && nearest < fallbackDistance)
                {
                    fallback = l;
                    fallbackDistance = nearest;
                }
            }
            if (inRange.Count > 0) return inRange[random.NextInt(inRange.Count)];
            return fallback ?? map.Landmarks[random.NextInt(map.Landmarks.Count)];
        }
    }
}
