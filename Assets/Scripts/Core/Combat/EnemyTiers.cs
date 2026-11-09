using System;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// Difficulty tiers (1 to 3, one per layer). A higher tier makes a family's targets smaller and faster, its
    /// patterns jumpier, and its weak points open for less time. Health and damage stay the same: deeper layers are
    /// harder to hit, not harder to survive.
    /// </summary>
    public static class EnemyTiers
    {
        public const int Min = 1;
        public const int Max = 3;

        public static int Clamp(int tier) => Math.Max(Min, Math.Min(Max, tier));

        /// <summary>Hitbox (and drawn) size: 100%, 88%, 76%.</summary>
        public static float Size(int tier) => Pick(tier, 1f, 0.88f, 0.76f);

        /// <summary>Movement speed: 100%, 115%, 130%.</summary>
        public static float Speed(int tier) => Pick(tier, 1f, 1.15f, 1.3f);

        /// <summary>How long weak points and hit windows stay open: 100%, 85%, 70%.</summary>
        public static float Window(int tier) => Pick(tier, 1f, 0.85f, 0.7f);

        /// <summary>How long between changes of direction or blinks (shorter is jumpier): 100%, 85%, 70%.</summary>
        public static float Rhythm(int tier) => Pick(tier, 1f, 0.85f, 0.7f);

        private static float Pick(int tier, float t1, float t2, float t3)
        {
            switch (Clamp(tier))
            {
                case 2: return t2;
                case 3: return t3;
                default: return t1;
            }
        }

        public static WispSettings Wisp(int tier)
        {
            var s = new WispSettings();
            s.BodyRadius *= Size(tier);
            s.HittableWindow *= Window(tier);
            s.MinBlinkInterval *= Rhythm(tier);
            s.MaxBlinkInterval *= Rhythm(tier);
            return s;
        }

        public static SkitterSettings Skitter(int tier)
        {
            var s = new SkitterSettings();
            s.BodyRadius *= Size(tier);
            s.CoreRadius *= Size(tier);
            s.Speed *= Speed(tier);
            s.MinTurnInterval *= Rhythm(tier);
            s.MaxTurnInterval *= Rhythm(tier);
            return s;
        }

        public static SentinelSettings Sentinel(int tier)
        {
            var s = new SentinelSettings();
            s.BodyRadius *= Size(tier);
            s.EyeRadius *= Size(tier);
            s.EyeOpenTime *= Window(tier);
            return s;
        }

        public static DiverSettings Diver(int tier)
        {
            var s = new DiverSettings();
            s.BodyRadius *= Size(tier);
            s.WingRadius *= Size(tier);
            s.WingSpan *= Size(tier);
            s.CircleSpeed *= Speed(tier);
            s.DiveSpeed *= Speed(tier);
            s.MinCircleTime *= Rhythm(tier);
            s.MaxCircleTime *= Rhythm(tier);
            return s;
        }

        /// <summary>The Bulwark that roams the world (smaller than the raid boss, no add waves).</summary>
        public static BulwarkSettings Bulwark(int tier)
        {
            var s = BulwarkSettings.ForWorld();
            s.BodyRadius *= Size(tier);
            s.PlateRadius *= Size(tier);
            s.OpenTime *= Window(tier);
            s.TurnSpeed *= Speed(tier);
            s.EnragedTurnSpeed *= Speed(tier);
            return s;
        }

        public static MiteSwarmSettings Mites(int tier)
        {
            var s = new MiteSwarmSettings();
            s.MiteRadius *= Size(tier);
            s.DriftSpeed *= Speed(tier);
            s.OrbitSpeed *= Speed(tier);
            s.MinTurnInterval *= Rhythm(tier);
            s.MaxTurnInterval *= Rhythm(tier);
            return s;
        }
    }

    /// <summary>
    /// An elite mixes two families: it moves and fights like its main family with a trick from the second, has more
    /// health, and drops more of its main color plus a little of the second's.
    /// </summary>
    public enum EliteKind : byte
    {
        None = 0,
        /// <summary>A Skitter that blinks like a Wisp: tracking and flicking.</summary>
        Blinker = 1,
        /// <summary>A Bulwark guarded by a Mite swarm: timing and target switching.</summary>
        Guarded = 2,
        /// <summary>A Sentinel that circles overhead like a Diver: precision and vertical tracking.</summary>
        Soaring = 3,
        /// <summary>A Diver that blinks between circling points like a Wisp: vertical tracking and flicking.</summary>
        Phasing = 4,
    }

    public static class Elites
    {
        /// <summary>Elites have this many times their family's health.</summary>
        public const float HealthMultiplier = 2.5f;
        /// <summary>Elites are this much bigger (drawn and hit).</summary>
        public const float SizeMultiplier = 1.2f;
        /// <summary>An elite drops this many times its family's normal drop of the main color...</summary>
        public const float DropMultiplier = 3f;
        /// <summary>...plus this much of the second family's color.</summary>
        public const int SecondDrop = 1;

        /// <summary>The elite kind a family can have, or None (Wisps and Mites have none).</summary>
        public static EliteKind For(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Skitter: return EliteKind.Blinker;
                case EnemyFamily.Bulwark: return EliteKind.Guarded;
                case EnemyFamily.Sentinel: return EliteKind.Soaring;
                case EnemyFamily.Diver: return EliteKind.Phasing;
                default: return EliteKind.None;
            }
        }

        public static EnemyFamily Primary(EliteKind kind)
        {
            switch (kind)
            {
                case EliteKind.Blinker: return EnemyFamily.Skitter;
                case EliteKind.Guarded: return EnemyFamily.Bulwark;
                case EliteKind.Soaring: return EnemyFamily.Sentinel;
                default: return EnemyFamily.Diver;
            }
        }

        public static EnemyFamily Second(EliteKind kind)
        {
            switch (kind)
            {
                case EliteKind.Blinker: return EnemyFamily.Wisp;
                case EliteKind.Guarded: return EnemyFamily.Mite;
                case EliteKind.Soaring: return EnemyFamily.Diver;
                default: return EnemyFamily.Wisp;
            }
        }

        public static string Name(EliteKind kind)
        {
            switch (kind)
            {
                case EliteKind.Blinker: return "Blinking Skitter";
                case EliteKind.Guarded: return "Guarded Bulwark";
                case EliteKind.Soaring: return "Soaring Sentinel";
                case EliteKind.Phasing: return "Phasing Diver";
                default: return "";
            }
        }
    }
}
