using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the Wisp (flicking drill). Metres and seconds.</summary>
    public sealed class WispSettings
    {
        /// <summary>Blink points are within this horizontal distance of the anchor.</summary>
        public float BlinkRadius { get; set; } = 6f;
        /// <summary>Each blink lands at least this far from the last point, so every blink needs a flick.</summary>
        public float MinBlinkDistance { get; set; } = 2.5f;
        public float MinHeight { get; set; } = 1.5f;
        public float MaxHeight { get; set; } = 3f;
        public float MinBlinkInterval { get; set; } = 1.2f;
        public float MaxBlinkInterval { get; set; } = 2.0f;
        /// <summary>Seconds after each blink during which the Wisp can be hit.</summary>
        public float HittableWindow { get; set; } = 0.9f;
        /// <summary>Whole-body hitbox; Wisps have no separate weak point.</summary>
        public float BodyRadius { get; set; } = 0.5f;
    }

    /// <summary>
    /// A Wisp hovers near its anchor and blinks (teleports) to a new point every 1.2-2 s. It can
    /// only be hit for a short window right after each blink. A plain struct so it can live in an
    /// ECS component; call <see cref="Step"/> once per server tick.
    /// </summary>
    public struct WispBehavior
    {
        /// <summary>Ground point the Wisp hovers around.</summary>
        public Vector3 Anchor;
        /// <summary>Body centre.</summary>
        public Vector3 Position;
        public float TimeToBlink;
        public float HittableTimeLeft;
        public CombatRandom Random;

        public bool IsHittable => HittableTimeLeft > 0f;

        /// <summary>Spawns at a random blink point, hittable as if it had just blinked in.</summary>
        public static WispBehavior Create(Vector3 anchor, uint seed, WispSettings s)
        {
            var w = new WispBehavior { Anchor = anchor, Random = new CombatRandom(seed) };
            w.Position = w.PickPoint(s, false);
            w.HittableTimeLeft = s.HittableWindow;
            w.TimeToBlink = w.Random.Range(s.MinBlinkInterval, s.MaxBlinkInterval);
            return w;
        }

        /// <summary>Advances time. Returns true when the Wisp blinked during this step.</summary>
        public bool Step(float dt, WispSettings s)
        {
            HittableTimeLeft = Math.Max(0f, HittableTimeLeft - dt);
            TimeToBlink -= dt;
            if (TimeToBlink > 0f) return false;

            float overshoot = -TimeToBlink;
            Position = PickPoint(s, true);
            HittableTimeLeft = Math.Max(0f, s.HittableWindow - overshoot);
            TimeToBlink = Random.Range(s.MinBlinkInterval, s.MaxBlinkInterval) - overshoot;
            return true;
        }

        public HitTarget ToHitTarget(int id, WispSettings s)
        {
            var t = HitTarget.Body(id, Position, s.BodyRadius);
            t.Hittable = IsHittable;
            return t;
        }

        private Vector3 PickPoint(WispSettings s, bool awayFromCurrent)
        {
            Vector3 p = Position;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = Random.Angle();
                // Square root keeps points spread evenly over the disc.
                float r = s.BlinkRadius * MathF.Sqrt(Random.NextFloat());
                p = Anchor + new Vector3(MathF.Cos(angle) * r, Random.Range(s.MinHeight, s.MaxHeight), MathF.Sin(angle) * r);
                if (!awayFromCurrent || Vector3.Distance(p, Position) >= s.MinBlinkDistance) return p;
            }
            return p;
        }
    }
}
