using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// A visible shot from an enemy: it flies in a straight line from where the enemy was toward where the
    /// target stood when it let go, and keeps going past them. It hits the first player whose body it passes
    /// within reach of, and stops at cover (the caller works out how far the line is clear when it fires).
    /// Moving after the windup dodges it. A plain struct for ECS.
    /// </summary>
    public struct EnemyBolt
    {
        /// <summary>How close to a player's body centre a bolt must pass (plus its own radius) to hit them.</summary>
        public const float PlayerRadius = 0.55f;
        /// <summary>A bolt flies this many times its attack range before it fades (so a dodged bolt flies past).</summary>
        public const float OvershootRange = 1.5f;

        public int Id;
        public EnemyFamily Family;
        /// <summary>Where it was fired from (for "what hit you" feedback).</summary>
        public Vector3 Origin;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Damage;
        public float Radius;
        public float Age;
        /// <summary>Seconds until it reaches cover or the end of its flight.</summary>
        public float Life;

        public bool Done => Age >= Life;

        /// <summary>
        /// A bolt from <paramref name="from"/> toward <paramref name="aim"/>. It flies at most
        /// <see cref="OvershootRange"/> times <paramref name="range"/>, and no further than
        /// <paramref name="clearDistance"/> (where the line meets cover).
        /// </summary>
        public static EnemyBolt Fire(int id, EnemyFamily family, Vector3 from, Vector3 aim, float speed, float radius,
            float damage, float range, float clearDistance)
        {
            if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed));
            var d = aim - from;
            float len = d.Length();
            var dir = len > 1e-4f ? d / len : Vector3.UnitZ;
            float travel = Math.Min(range * OvershootRange, Math.Max(0f, clearDistance));
            return new EnemyBolt
            {
                Id = id, Family = family, Origin = from, Position = from, Velocity = dir * speed,
                Damage = damage, Radius = radius, Life = travel / speed,
            };
        }

        /// <summary>
        /// Moves the bolt one step (not past the end of its life) and returns the index of the first
        /// attackable player it passed through, or -1.
        /// </summary>
        public int Step(float dt, IReadOnlyList<AttackCandidate> players)
        {
            float step = Math.Min(dt, Math.Max(0f, Life - Age));
            var from = Position;
            var to = Position + Velocity * step;
            Age += dt;
            Position = to;
            int best = -1;
            float bestT = float.MaxValue;
            float reach = Radius + PlayerRadius;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].CanBeAttacked) continue;
                float t = ClosestT(from, to, players[i].Position);
                var p = from + (to - from) * t;
                if (Vector3.DistanceSquared(p, players[i].Position) > reach * reach || t >= bestT) continue;
                best = i;
                bestT = t;
            }
            if (best >= 0) Age = Life;
            return best;
        }

        private static float ClosestT(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-8f) return 0f;
            return Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f);
        }
    }
}
