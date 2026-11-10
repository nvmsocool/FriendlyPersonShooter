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
        /// <summary>Gate unlock targets (hit in order) on layers 1, 2 and 3.</summary>
        public static readonly int[] GateTargets = { 3, 4, 5 };
        /// <summary>Dropped cache targets (hit them all) on layers 1, 2 and 3.</summary>
        public static readonly int[] CacheTargets = { 3, 5, 7 };

        /// <summary>Base target speed, degrees per second, on layer 1.</summary>
        public const float BaseSpeed = 9f;
        /// <summary>Seconds each banking target stays up on layer 1.</summary>
        public const float BankSeconds = 3f;
        /// <summary>Essence each banking target holds at most.</summary>
        public const int BankChunk = 5;
        /// <summary>Seconds between gallery shots (one shot per click).</summary>
        public const float ShotGap = 0.25f;

        /// <summary>Layer index 0..2 for a layer number (1 = surface).</summary>
        public static int Tier(int layer) => Math.Clamp(layer - 1, 0, 2);

        /// <summary>
        /// The spec for a gallery of <paramref name="kind"/> on <paramref name="layer"/> (1 = surface).
        /// <paramref name="targets"/> sets the target count where it varies (banking: one per chunk).
        /// </summary>
        public static GallerySpec For(GalleryKind kind, int layer, int targets = 0)
        {
            int k = Tier(layer);
            float radius = Radius[k], speed = BaseSpeed * SpeedScale[k], time = TimeScale[k];
            switch (kind)
            {
                case GalleryKind.Starter:
                    // Slow, big and no limit: a new player's first look at a gallery.
                    return new GallerySpec(kind, GalleryRule.Choose, 3, 0f, 0f, false, 5f, 4f);
                case GalleryKind.Box:
                case GalleryKind.Reward:
                    return new GallerySpec(kind, GalleryRule.Choose, 3, 10f * time, 0f, true, radius, speed);
                case GalleryKind.Cache:
                    return new GallerySpec(kind, GalleryRule.All, CacheTargets[k], 10f * time, 0f, false, radius, speed);
                case GalleryKind.Bank:
                    return new GallerySpec(kind, GalleryRule.Sequence, Math.Max(1, targets), 0f, BankSeconds * time, false, radius, speed);
                case GalleryKind.Gate:
                    return new GallerySpec(kind, GalleryRule.Ordered, GateTargets[k], 10f * time, 0f, false, radius, speed);
                case GalleryKind.Travel:
                    return new GallerySpec(kind, GalleryRule.Choose, 1, 8f * time, 0f, false, radius, speed * 1.6f);
                case GalleryKind.RaidStart:
                    return new GallerySpec(kind, GalleryRule.Choose, 1, 10f * time, 0f, false, radius, speed);
                default:
                    return new GallerySpec(GalleryKind.None, GalleryRule.Choose, 1, 1f, 0f, true, radius, speed);
            }
        }

        /// <summary>The chance a bot gets a gallery right (the server decides for them).</summary>
        public static float BotSuccess(int layer) => new[] { 0.95f, 0.85f, 0.75f }[Tier(layer)];

        /// <summary>Banking chunks: <paramref name="amount"/> essence dealt in targets of up to <see cref="BankChunk"/>.</summary>
        public static int BankTargets(int amount) => amount <= 0 ? 0 : (amount + BankChunk - 1) / BankChunk;

        /// <summary>Essence held by banking target <paramref name="i"/> when <paramref name="amount"/> is dealt out.</summary>
        public static int BankChunkAmount(int amount, int i) => Math.Clamp(amount - i * BankChunk, 0, BankChunk);
    }
}
