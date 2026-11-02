using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for how a bot ally dodges around. Metres and seconds.</summary>
    public sealed class BotMoveSettings
    {
        /// <summary>Each strafe (A or D) lasts between these.</summary>
        public float MinStrafeTime { get; set; } = 0.2f;
        public float MaxStrafeTime { get; set; } = 0.7f;
        /// <summary>Chance a new strafe goes the other way (ADAD) rather than a random pick.</summary>
        public float ReverseChance { get; set; } = 0.75f;
        /// <summary>Chance a new strafe is a short pause instead.</summary>
        public float PauseChance { get; set; } = 0.12f;
        /// <summary>Chance a new strafe also walks forward or back a little.</summary>
        public float DiagonalChance { get; set; } = 0.2f;
        public float JumpsPerSecond { get; set; } = 0.3f;
        public float JumpCooldown { get; set; } = 0.9f;
        /// <summary>Keeps between these distances from the anchor (arena centre, nearest enemy...).</summary>
        public float MinDistance { get; set; } = 4f;
        public float MaxDistance { get; set; } = 12f;
    }

    /// <summary>
    /// Movement keys relative to where the player faces, like a keyboard:
    /// Horizontal -1 = A (left), +1 = D (right); Vertical -1 = S (back), +1 = W (forward).
    /// </summary>
    public struct MoveInput
    {
        public int Horizontal;
        public int Vertical;
        public bool Jump;

        /// <summary>
        /// The flat world direction these keys move toward for a player facing <paramref name="yawDeg"/>
        /// (see <see cref="AimMath"/>). Normalized, or zero when no key is held.
        /// </summary>
        public Vector3 ToWorld(float yawDeg)
        {
            float y = yawDeg * AimMath.Deg2Rad;
            var forward = new Vector3(MathF.Sin(y), 0f, MathF.Cos(y));
            var right = new Vector3(MathF.Cos(y), 0f, -MathF.Sin(y));
            Vector3 d = right * Horizontal + forward * Vertical;
            return d.LengthSquared() > 0f ? Vector3.Normalize(d) : Vector3.Zero;
        }
    }

    /// <summary>
    /// Makes a bot ally move like a player in a fight, so healing it is a tracking drill:
    /// ADAD strafing with random timings, short pauses, the odd jump, and staying within a
    /// distance band of an anchor point. Outputs keys relative to the bot's facing, matching
    /// what a human client sends. A plain struct for ECS.
    /// </summary>
    public struct BotMover
    {
        public int Strafe;
        public int Walk;
        public float TimeToChange;
        public float JumpCooldownLeft;
        public CombatRandom Random;

        public static BotMover Create(uint seed)
        {
            var m = new BotMover { Random = new CombatRandom(seed) };
            m.Strafe = m.Random.Chance(0.5f) ? 1 : -1;
            return m;
        }

        /// <param name="position">Bot's feet.</param>
        /// <param name="yawDeg">Where the bot faces (it may be aiming at an enemy, not the anchor).</param>
        /// <param name="anchor">Point to keep within the distance band of.</param>
        public MoveInput Step(float dt, Vector3 position, float yawDeg, Vector3 anchor, BotMoveSettings s)
        {
            TimeToChange -= dt;
            if (TimeToChange <= 0f)
            {
                TimeToChange = Random.Range(s.MinStrafeTime, s.MaxStrafeTime);
                if (Random.Chance(s.PauseChance))
                {
                    Strafe = 0;
                    TimeToChange *= 0.5f;
                }
                else if (Strafe != 0 && Random.Chance(s.ReverseChance))
                {
                    Strafe = -Strafe;
                }
                else
                {
                    Strafe = Random.Chance(0.5f) ? 1 : -1;
                }
                Walk = Random.Chance(s.DiagonalChance) ? (Random.Chance(0.5f) ? 1 : -1) : 0;
            }

            var input = new MoveInput { Horizontal = Strafe, Vertical = Walk };

            // Out of the band: walk back into it, keeping the strafe on any axis the fix doesn't need.
            var to = new Vector3(anchor.X - position.X, 0f, anchor.Z - position.Z);
            float dist = to.Length();
            Vector3 want = Vector3.Zero;
            if (dist > s.MaxDistance) want = to / dist;
            else if (dist < s.MinDistance && dist > 1e-4f) want = -to / dist;
            if (want != Vector3.Zero)
            {
                float y = yawDeg * AimMath.Deg2Rad;
                float localX = want.X * MathF.Cos(y) - want.Z * MathF.Sin(y);
                float localZ = want.X * MathF.Sin(y) + want.Z * MathF.Cos(y);
                const float axisThreshold = 0.38f; // about 67 degrees off an axis
                input.Vertical = Math.Abs(localZ) > axisThreshold ? Math.Sign(localZ) : 0;
                if (Math.Abs(localX) > axisThreshold) input.Horizontal = Math.Sign(localX);
            }

            JumpCooldownLeft = Math.Max(0f, JumpCooldownLeft - dt);
            if (JumpCooldownLeft <= 0f && Random.Chance(s.JumpsPerSecond * dt))
            {
                input.Jump = true;
                JumpCooldownLeft = s.JumpCooldown;
            }
            return input;
        }
    }
}
