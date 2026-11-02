using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the Skitter (tracking drill). Metres and seconds.</summary>
    public sealed class SkitterSettings
    {
        public float Speed { get; set; } = 7f;
        public float MinTurnInterval { get; set; } = 0.2f;
        public float MaxTurnInterval { get; set; } = 0.6f;
        /// <summary>Never strays farther than this (horizontally) from home.</summary>
        public float LeashRadius { get; set; } = 8f;
        /// <summary>Past this share of the leash, new directions point back toward home.</summary>
        public float ReturnShare { get; set; } = 0.7f;
        /// <summary>Chance a turn reverses direction (left-right-left), versus a fresh random heading.</summary>
        public float ReverseChance { get; set; } = 0.6f;
        public float BodyRadius { get; set; } = 0.6f;
        public float CoreRadius { get; set; } = 0.2f;
        /// <summary>Core position relative to the body centre.</summary>
        public Vector3 CoreOffset { get; set; } = Vector3.Zero;
    }

    /// <summary>
    /// A Skitter strafes erratically along the ground at high speed, changing direction every
    /// 0.2-0.6 s and staying on a leash around home. Its glowing core is the weak point.
    /// Position is the body centre and keeps home's height. A plain struct for ECS.
    /// </summary>
    public struct SkitterBehavior
    {
        public Vector3 Home;
        public Vector3 Position;
        /// <summary>Unit direction on the ground plane (Y = 0).</summary>
        public Vector3 Heading;
        public float TimeToTurn;
        public CombatRandom Random;

        public Vector3 Velocity(SkitterSettings s) => Heading * s.Speed;

        public static SkitterBehavior Create(Vector3 home, uint seed, SkitterSettings s)
        {
            var k = new SkitterBehavior { Home = home, Position = home, Random = new CombatRandom(seed) };
            float a = k.Random.Angle();
            k.Heading = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            k.TimeToTurn = k.Random.Range(s.MinTurnInterval, s.MaxTurnInterval);
            return k;
        }

        /// <summary>Advances time. Returns true when the Skitter changed direction this step.</summary>
        public bool Step(float dt, SkitterSettings s)
        {
            bool turned = false;
            TimeToTurn -= dt;
            if (TimeToTurn <= 0f)
            {
                Turn(s);
                TimeToTurn += Random.Range(s.MinTurnInterval, s.MaxTurnInterval);
                turned = true;
            }

            Vector3 next = Position + Heading * (s.Speed * dt);
            Vector3 offset = new Vector3(next.X - Home.X, 0f, next.Z - Home.Z);
            float dist = offset.Length();
            if (dist > s.LeashRadius)
            {
                // Hit the leash: stop at its edge and bounce back inward.
                offset *= s.LeashRadius / dist;
                Position = new Vector3(Home.X + offset.X, Home.Y, Home.Z + offset.Z);
                Turn(s);
                return true;
            }
            Position = new Vector3(next.X, Home.Y, next.Z);
            return turned;
        }

        public HitTarget ToHitTarget(int id, SkitterSettings s)
        {
            return HitTarget.WithWeakPoint(id, Position, s.BodyRadius, Position + s.CoreOffset, s.CoreRadius);
        }

        private void Turn(SkitterSettings s)
        {
            Vector3 toHome = new Vector3(Home.X - Position.X, 0f, Home.Z - Position.Z);
            float dist = toHome.Length();
            float angle;
            if (dist > s.LeashRadius * s.ReturnShare)
            {
                // Head back toward home, within +-60 degrees so it still looks erratic.
                angle = MathF.Atan2(toHome.Z, toHome.X) + Random.Range(-1.05f, 1.05f);
            }
            else if (Random.Chance(s.ReverseChance))
            {
                // Reverse with some spread: the classic left-right juke.
                angle = MathF.Atan2(-Heading.Z, -Heading.X) + Random.Range(-0.6f, 0.6f);
            }
            else
            {
                angle = Random.Angle();
            }
            Heading = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
        }
    }
}
