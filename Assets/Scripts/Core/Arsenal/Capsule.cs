using System;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>
    /// The flight of a launched payload capsule: it's flung up and out from the gun, somewhere within
    /// <see cref="SpreadDegrees"/> either side of the aim, and falls under gravity. Shoot it before
    /// <see cref="Life"/> runs out for a full capsule. It's smaller and faster on deeper layers.
    /// </summary>
    public readonly struct CapsuleFlight
    {
        /// <summary>Seconds a capsule flies before it's lost.</summary>
        public const float Life = 2.2f;
        /// <summary>How far either side of the aim it can go (degrees).</summary>
        public const float SpreadDegrees = 35f;
        public const float Gravity = 9.8f;
        /// <summary>No hits in the first moment, so the shot that launched it can't also catch it.</summary>
        public const float CatchDelay = 0.12f;

        /// <summary>Hit radius (metres) and launch speed (m/s) on layers 1, 2 and 3.</summary>
        public static readonly float[] Radius = { 0.42f, 0.34f, 0.28f };
        public static readonly float[] Speed = { 9.5f, 11f, 12.5f };

        public readonly Vector3 From;
        public readonly Vector3 Velocity;
        public readonly float HitRadius;

        public CapsuleFlight(Vector3 from, Vector3 velocity, float hitRadius)
        {
            From = from;
            Velocity = velocity;
            HitRadius = hitRadius;
        }

        /// <summary>
        /// A launch from <paramref name="from"/> for a player aiming at (<paramref name="yawDeg"/>,
        /// <paramref name="pitchDeg"/>) on <paramref name="layer"/> (1 = surface).
        /// </summary>
        public static CapsuleFlight Launch(Vector3 from, float yawDeg, float pitchDeg, int layer, ref CombatRandom rng)
        {
            int k = Math.Clamp(layer - 1, 0, 2);
            float yaw = (yawDeg + rng.Range(-SpreadDegrees, SpreadDegrees)) * (float)(Math.PI / 180.0);
            // Rising: 30-50 degrees up from level, nudged by where the player looks (looking down still throws it up).
            float rise = Math.Clamp(rng.Range(30f, 50f) + pitchDeg * 0.3f, 20f, 70f) * (float)(Math.PI / 180.0);
            float speed = Speed[k];
            var v = new Vector3(MathF.Sin(yaw) * MathF.Cos(rise), MathF.Sin(rise), MathF.Cos(yaw) * MathF.Cos(rise)) * speed;
            return new CapsuleFlight(from, v, Radius[k]);
        }

        /// <summary>Where it is <paramref name="t"/> seconds after launch.</summary>
        public Vector3 At(float t) => From + Velocity * t + new Vector3(0f, -0.5f * Gravity * t * t, 0f);

        /// <summary>Can a shot <paramref name="t"/> seconds after launch hit it?</summary>
        public static bool Catchable(float t) => t >= CatchDelay && t < Life;
    }
}
