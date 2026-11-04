namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>How a gun fires. None = an empty gun slot.</summary>
    public enum FrameKind : byte
    {
        None = 0,
        Pistol = 1,
        Rifle = 2,
        Smg = 3,
        Marksman = 4,
        Beam = 5,
        Launcher = 6,
    }

    /// <summary>The kind of shot a frame fires.</summary>
    public enum FireMode : byte
    {
        /// <summary>Instant hit along the aim, one bullet per shot.</summary>
        Hitscan = 0,
        /// <summary>Holding the trigger fires a burst of <see cref="FrameStats.BurstCount"/> hitscan bullets.</summary>
        Burst = 1,
        /// <summary>Short-range hitscan pulses; damage builds the longer it stays on one target.</summary>
        Beam = 2,
        /// <summary>A slow projectile that bursts on impact, hitting everything near it.</summary>
        Projectile = 3,
    }

    /// <summary>
    /// A frame's base numbers. Damage per second is close to 80 for every frame (the old single gun's
    /// 83), so enemy health stays valid; the frames differ in how they deliver it.
    /// Times in seconds, distances in metres, angles in degrees.
    /// </summary>
    public sealed class FrameStats
    {
        public FrameKind Kind { get; set; }
        public string Name { get; set; }
        public FireMode Mode { get; set; }
        /// <summary>Damage of one bullet, pulse or direct projectile hit.</summary>
        public float Damage { get; set; }
        /// <summary>Seconds between shots (between bursts for Burst, between pulses for Beam).</summary>
        public float Interval { get; set; }
        public int BurstCount { get; set; } = 1;
        /// <summary>Seconds between the bullets inside one burst.</summary>
        public float BurstGap { get; set; }
        /// <summary>Half-angle of the random cone a bullet leaves in (0 = perfectly accurate).</summary>
        public float Spread { get; set; }
        public float Range { get; set; }
        /// <summary>Damage starts falling off at this distance and bottoms out at <see cref="FalloffEnd"/> (0 = no falloff).</summary>
        public float FalloffStart { get; set; }
        public float FalloffEnd { get; set; }
        /// <summary>Damage multiplier at and past <see cref="FalloffEnd"/>.</summary>
        public float FalloffMin { get; set; } = 1f;
        /// <summary>Shots (or pulses) per magazine.</summary>
        public int Magazine { get; set; }
        public float Reload { get; set; }
        /// <summary>Zoom while aiming (right mouse): 1 = none, 2 = half the field of view.</summary>
        public float Zoom { get; set; } = 1.2f;
        /// <summary>Spread multiplier while zoomed.</summary>
        public float ZoomedSpread { get; set; } = 0.6f;
        /// <summary>
        /// Multiplies how much a hit heals (Vigor, and the tiny heal without a payload), so healing per
        /// second evens out between fast and slow frames.
        /// </summary>
        public float HealScale { get; set; } = 1f;
        /// <summary>Multiplies the strength of the buffs and debuffs a hit gives (the Marksman's big buff per hit).</summary>
        public float EffectScale { get; set; } = 1f;
        // Beam
        /// <summary>Damage multiplier when the beam first lands on a target; it grows to <see cref="BeamMax"/>.</summary>
        public float BeamMin { get; set; } = 1f;
        public float BeamMax { get; set; } = 1f;
        /// <summary>Seconds on one target to reach <see cref="BeamMax"/>.</summary>
        public float BeamRamp { get; set; }
        // Projectile
        public float ProjectileSpeed { get; set; }
        public float SplashRadius { get; set; }
        /// <summary>Splash damage at the centre, as a share of <see cref="Damage"/>; it falls to half at the edge.</summary>
        public float SplashShare { get; set; }

        /// <summary>Burst damage per second while the trigger is held (ignores reloads, falloff and beam build-up).</summary>
        public float Dps => Damage * BurstCount / Interval;
    }

    public static class GunFrames
    {
        public static readonly FrameKind[] All =
        {
            FrameKind.Pistol, FrameKind.Rifle, FrameKind.Smg, FrameKind.Marksman, FrameKind.Beam, FrameKind.Launcher,
        };

        private static readonly FrameStats Pistol = new FrameStats
        {
            Kind = FrameKind.Pistol, Name = "Pistol", Mode = FireMode.Hitscan,
            Damage = 16f, Interval = 0.2f, Spread = 0.3f, Range = 70f,
            Magazine = 12, Reload = 1.1f, Zoom = 1.3f, HealScale = 1.6f,
        };

        private static readonly FrameStats Rifle = new FrameStats
        {
            Kind = FrameKind.Rifle, Name = "Rifle", Mode = FireMode.Burst,
            Damage = 12f, Interval = 0.45f, BurstCount = 3, BurstGap = 0.07f, Spread = 0.4f, Range = 90f,
            Magazine = 24, Reload = 1.5f, Zoom = 1.5f, HealScale = 1.1f,
        };

        private static readonly FrameStats Smg = new FrameStats
        {
            Kind = FrameKind.Smg, Name = "SMG", Mode = FireMode.Hitscan,
            Damage = 7f, Interval = 0.08f, Spread = 2.2f, Range = 60f,
            FalloffStart = 18f, FalloffEnd = 40f, FalloffMin = 0.45f,
            Magazine = 36, Reload = 1.6f, Zoom = 1.15f, HealScale = 0.65f,
        };

        private static readonly FrameStats Marksman = new FrameStats
        {
            Kind = FrameKind.Marksman, Name = "Marksman", Mode = FireMode.Hitscan,
            Damage = 48f, Interval = 0.6f, Spread = 1.2f, ZoomedSpread = 0f, Range = 140f,
            Magazine = 6, Reload = 2f, Zoom = 2.5f, HealScale = 4f, EffectScale = 1.3f,
        };

        private static readonly FrameStats Beam = new FrameStats
        {
            Kind = FrameKind.Beam, Name = "Beam", Mode = FireMode.Beam,
            Damage = 7f, Interval = 0.1f, Spread = 0f, Range = 32f,
            Magazine = 60, Reload = 1.8f, Zoom = 1.1f, HealScale = 0.45f,
            BeamMin = 0.6f, BeamMax = 1.4f, BeamRamp = 1.5f,
        };

        private static readonly FrameStats Launcher = new FrameStats
        {
            Kind = FrameKind.Launcher, Name = "Launcher", Mode = FireMode.Projectile,
            Damage = 30f, Interval = 0.75f, Spread = 0.5f, Range = 80f,
            Magazine = 5, Reload = 2.2f, Zoom = 1.2f, HealScale = 2.5f,
            ProjectileSpeed = 38f, SplashRadius = 3.5f, SplashShare = 0.9f,
        };

        public static FrameStats For(FrameKind kind)
        {
            switch (kind)
            {
                case FrameKind.Pistol: return Pistol;
                case FrameKind.Rifle: return Rifle;
                case FrameKind.Smg: return Smg;
                case FrameKind.Marksman: return Marksman;
                case FrameKind.Beam: return Beam;
                case FrameKind.Launcher: return Launcher;
                default: return null;
            }
        }

        public static string Name(FrameKind kind) => For(kind)?.Name ?? "Empty";
    }
}
