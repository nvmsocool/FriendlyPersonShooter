using System;

namespace FriendlyPersonShooter.Core.Gallery
{
    /// <summary>
    /// The numbers for each kind of shooting gallery, by layer. Deeper layers have smaller, faster targets and less
    /// time. The starting gun pick never gets harder and has no time limit.
    /// </summary>
    public static class GalleryDifficulty
    {
        /// <summary>Target radius in degrees on layers 1, 2 and 3.</summary>
        public static readonly float[] Radius = { 4f, 3f, 2.2f };
        /// <summary>Target speed multiplier on layers 1, 2 and 3.</summary>
        public static readonly float[] SpeedScale = { 1f, 1.5f, 2f };
        /// <summary>Time multiplier on layers 1, 2 and 3.</summary>
        public static readonly float[] TimeScale = { 1f, 0.85f, 0.7f };
        /// <summary>Dropped cache targets (hit them all) on layers 1, 2 and 3.</summary>
        public static readonly int[] CacheTargets = { 3, 5, 7 };

        /// <summary>Base target speed, degrees per second, on layer 1.</summary>
        public const float BaseSpeed = 9f;
        /// <summary>Seconds between gallery shots (one shot per click).</summary>
        public const float ShotGap = 0.25f;

        /// <summary>Layer index 0..2 for a layer number (1 = surface).</summary>
        public static int Tier(int layer) => Math.Clamp(layer - 1, 0, 2);

        /// <summary>The spec for a gallery of <paramref name="kind"/> on <paramref name="layer"/> (1 = surface).</summary>
        public static GallerySpec For(GalleryKind kind, int layer)
        {
            int k = Tier(layer);
            float radius = Radius[k], speed = BaseSpeed * SpeedScale[k], time = TimeScale[k];
            switch (kind)
            {
                case GalleryKind.Starter:
                    // Slow, big and no limit: a new player's first look at a gallery.
                    return new GallerySpec(kind, GalleryRule.Choose, 3, 0f, false, 5f, 4f);
                case GalleryKind.Box:
                case GalleryKind.Reward:
                    return new GallerySpec(kind, GalleryRule.Choose, 3, 10f * time, true, radius, speed);
                case GalleryKind.Cache:
                    return new GallerySpec(kind, GalleryRule.All, CacheTargets[k], 10f * time, false, radius, speed);
                case GalleryKind.Travel:
                    return new GallerySpec(kind, GalleryRule.Choose, 1, 8f * time, false, radius, speed * 1.6f);
                default:
                    return new GallerySpec(GalleryKind.None, GalleryRule.Choose, 1, 1f, true, radius, speed);
            }
        }

        /// <summary>The chance a bot gets a gallery right (the server decides for them).</summary>
        public static float BotSuccess(int layer) => new[] { 0.95f, 0.85f, 0.75f }[Tier(layer)];
    }
}
