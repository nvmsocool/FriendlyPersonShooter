using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the Sentinel (long-range precision drill). Metres, seconds, degrees.</summary>
    public sealed class SentinelSettings
    {
        /// <summary>The armored pillar's hit sphere, centred this high above the ground.</summary>
        public float BodyRadius { get; set; } = 0.85f;
        public float BodyHeight { get; set; } = 1.6f;
        /// <summary>The eye: head-sized, on the side facing its target, a little above the body's centre.</summary>
        public float EyeRadius { get; set; } = 0.24f;
        public float EyeLift { get; set; } = 0.45f;
        /// <summary>Damage share of a hit that doesn't land on an open eye.</summary>
        public float ArmorMultiplier { get; set; } = 0.3f;
        /// <summary>The eye opens this long before each shot (the warning)...</summary>
        public float EyeOpenTime { get; set; } = 1.2f;
        /// <summary>...and stays open this long after it.</summary>
        public float AfterFireTime { get; set; } = 0.4f;
        /// <summary>How fast it turns to face its target (degrees per second).</summary>
        public float TurnSpeed { get; set; } = 140f;
        /// <summary>The Soaring elite circles this high above its home, on a ring this wide, at this speed (m/s).</summary>
        public float SoarHeight { get; set; } = 7f;
        public float SoarRadius { get; set; } = 6f;
        public float SoarSpeed { get; set; } = 3.5f;
    }

    /// <summary>
    /// A Sentinel stands far off, turned toward the player it watches. Its body is armored; before each shot it opens
    /// a head-sized eye for about a second (the warning), fires a fast bolt, and the eye shuts again soon after. Hits
    /// on the open eye are weak-point hits; everything else does a fraction of the damage. The Soaring elite circles
    /// overhead instead of standing still. Position is the body centre. A plain struct for ECS.
    /// </summary>
    public struct SentinelBehavior
    {
        /// <summary>Ground point it stands on (or circles above).</summary>
        public Vector3 Home;
        public Vector3 Position;
        /// <summary>Which way it faces, in degrees (0 = +z, 90 = +x).</summary>
        public float Yaw;
        public bool Telegraph;
        public float AfterFire;
        public bool Soaring;
        public float Angle;

        public static SentinelBehavior Create(Vector3 home, uint seed, SentinelSettings s, bool soaring = false)
        {
            var random = new CombatRandom(seed == 0 ? 1u : seed);
            var b = new SentinelBehavior { Home = home, Soaring = soaring, Yaw = random.Range(0f, 360f), Angle = random.Angle() };
            b.Position = b.RestPoint(s);
            return b;
        }

        public bool EyeOpen => Telegraph || AfterFire > 0f;

        public Vector3 Facing => BulwarkBehavior.FacingOf(Yaw);

        public Vector3 EyeCenter(SentinelSettings s) => EyeCenterOf(Position, Yaw, s);

        public static Vector3 EyeCenterOf(Vector3 position, float yaw, SentinelSettings s) =>
            position + BulwarkBehavior.FacingOf(yaw) * (s.BodyRadius * 0.9f) + new Vector3(0f, s.EyeLift, 0f);

        /// <summary>
        /// One tick. <paramref name="telegraph"/> is whether its attack is winding up (the eye opens), <paramref name="fired"/>
        /// whether it fired this tick, and <paramref name="target"/> the player it watches (null = keep its heading).
        /// </summary>
        public void Step(float dt, bool telegraph, bool fired, Vector3? target, SentinelSettings s, float moveMultiplier = 1f)
        {
            Telegraph = telegraph;
            AfterFire = fired ? s.AfterFireTime : Math.Max(0f, AfterFire - dt);
            if (Soaring) Angle += dt * moveMultiplier * s.SoarSpeed / Math.Max(0.1f, s.SoarRadius);
            Position = RestPoint(s);
            if (!target.HasValue) return;
            var to = target.Value - Position;
            if (to.X * to.X + to.Z * to.Z < 1e-4f) return;
            float want = (float)(Math.Atan2(to.X, to.Z) * 180.0 / Math.PI);
            float delta = ((want - Yaw) % 360f + 540f) % 360f - 180f;
            float step = s.TurnSpeed * moveMultiplier * dt;
            Yaw = (Yaw + Math.Max(-step, Math.Min(step, delta)) + 360f) % 360f;
        }

        /// <summary>Full damage and a weak-point hit only on the open eye; a fraction anywhere else.</summary>
        public void HitOutcome(bool hitEye, SentinelSettings s, out float damageMultiplier, out bool weakPoint)
        {
            weakPoint = hitEye && EyeOpen;
            damageMultiplier = weakPoint ? 1f : s.ArmorMultiplier;
        }

        public HitTarget ToHitTarget(int id, SentinelSettings s)
        {
            var t = HitTarget.WithWeakPoint(id, Position, s.BodyRadius, EyeCenter(s), s.EyeRadius);
            t.WeakPointOnSurface = true;
            return t;
        }

        private Vector3 RestPoint(SentinelSettings s)
        {
            if (!Soaring) return Home + new Vector3(0f, s.BodyHeight, 0f);
            return Home + new Vector3((float)Math.Cos(Angle) * s.SoarRadius, s.SoarHeight, (float)Math.Sin(Angle) * s.SoarRadius);
        }
    }
}
