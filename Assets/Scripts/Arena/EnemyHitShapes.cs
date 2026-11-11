using FriendlyPersonShooter.Core.Combat;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Every enemy's hit spheres (body and weak points) from what every machine knows about it: family, tier, elite,
    /// whether it's the Colossus or a raid boss, where it is and which way it faces. The host and each player's game
    /// use the same shapes, so a player's own hit test (see <see cref="LocalShots"/>) matches what the host would find.
    /// </summary>
    public static class EnemyHitShapes
    {
        public const float GlimmerRadius = 0.45f;

        private static readonly WispSettings Wisp1 = new WispSettings();
        private static readonly SkitterSettings Skitter1 = new SkitterSettings();
        private static readonly WispSettings[] WispTier = { null, EnemyTiers.Wisp(1), EnemyTiers.Wisp(2), EnemyTiers.Wisp(3) };
        private static readonly SkitterSettings[] SkitterTier = { null, EnemyTiers.Skitter(1), EnemyTiers.Skitter(2), EnemyTiers.Skitter(3) };
        private static readonly SentinelSettings[] SentinelTier = { null, EnemyTiers.Sentinel(1), EnemyTiers.Sentinel(2), EnemyTiers.Sentinel(3) };
        private static readonly DiverSettings[] DiverTier = { null, EnemyTiers.Diver(1), EnemyTiers.Diver(2), EnemyTiers.Diver(3) };
        private static readonly BulwarkSettings[] BulwarkWorldTier = { null, EnemyTiers.Bulwark(1), EnemyTiers.Bulwark(2), EnemyTiers.Bulwark(3) };
        private static readonly MiteSwarmSettings[] MiteTier = { null, EnemyTiers.Mites(1), EnemyTiers.Mites(2), EnemyTiers.Mites(3) };
        /// <summary>The raid bosses' tuning.</summary>
        private static readonly BulwarkSettings RaidBulwark = new BulwarkSettings();
        public static readonly ColossusSettings Colossus = new ColossusSettings();

        public static WispSettings Wisp(int tier) => tier <= 1 ? Wisp1 : WispTier[EnemyTiers.Clamp(tier)];
        public static SkitterSettings Skitter(int tier) => tier <= 1 ? Skitter1 : SkitterTier[EnemyTiers.Clamp(tier)];
        public static SentinelSettings Sentinel(int tier) => SentinelTier[EnemyTiers.Clamp(tier)];
        public static DiverSettings Diver(int tier) => DiverTier[EnemyTiers.Clamp(tier)];
        public static MiteSwarmSettings Mites(int tier) => MiteTier[EnemyTiers.Clamp(tier)];
        /// <summary>A raid boss (or twin) uses the raid tuning; a Bulwark roaming the world its tier of the world tuning.</summary>
        public static BulwarkSettings Bulwark(int tier, bool roaming) => roaming ? BulwarkWorldTier[EnemyTiers.Clamp(tier)] : RaidBulwark;

        /// <summary>
        /// The enemy's hit target at <paramref name="position"/>, facing <paramref name="yaw"/> degrees.
        /// <paramref name="roaming"/>: a Bulwark roaming the world (else the raid tuning); <paramref name="worldBoss"/>: the Colossus.
        /// </summary>
        public static HitTarget For(int id, EnemyFamily family, int tier, EliteKind elite, bool worldBoss, bool roaming,
            Vector3 position, float yaw, bool hittable)
        {
            float grow = elite != EliteKind.None ? Elites.SizeMultiplier : 1f;
            HitTarget t;
            switch (family)
            {
                case EnemyFamily.Wisp:
                    t = HitTarget.Body(id, position, Wisp(tier).BodyRadius * grow);
                    break;
                case EnemyFamily.Skitter:
                {
                    var k = Skitter(tier);
                    t = HitTarget.WithWeakPoint(id, position, k.BodyRadius * grow, position + k.CoreOffset, k.CoreRadius * grow);
                    break;
                }
                case EnemyFamily.Sentinel:
                {
                    var k = Sentinel(tier);
                    t = HitTarget.WithWeakPoint(id, position, k.BodyRadius * grow, SentinelBehavior.EyeCenterOf(position, yaw, k), k.EyeRadius * grow);
                    t.WeakPointOnSurface = true;
                    break;
                }
                case EnemyFamily.Diver:
                {
                    var d = Diver(tier);
                    // Wings stick out sideways from its heading (yaw is the heading's direction on the ground).
                    var forward = BulwarkBehavior.FacingOf(yaw);
                    var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
                    t = HitTarget.WithWeakPoint(id, position, d.BodyRadius * grow, position - right * d.WingSpan, d.WingRadius * grow);
                    t.HasSecondWeakPoint = true;
                    t.SecondWeakPointCenter = position + right * d.WingSpan;
                    break;
                }
                case EnemyFamily.Glimmer:
                    t = HitTarget.Body(id, position, GlimmerRadius);
                    break;
                case EnemyFamily.Bulwark:
                {
                    if (worldBoss)
                    {
                        t = HitTarget.WithWeakPoint(id, position, Colossus.BodyRadius, ColossusBehavior.CoreCenterOf(position, Colossus), Colossus.CoreRadius);
                    }
                    else
                    {
                        var b = Bulwark(tier, roaming);
                        t = HitTarget.WithWeakPoint(id, position, b.BodyRadius * grow, BulwarkBehavior.PlateCenterOf(position, yaw, b), b.PlateRadius * grow);
                    }
                    t.WeakPointOnSurface = true;
                    break;
                }
                default:
                    t = HitTarget.Body(id, position, Mites(tier).MiteRadius);
                    break;
            }
            t.Hittable = hittable;
            return t;
        }
    }
}
