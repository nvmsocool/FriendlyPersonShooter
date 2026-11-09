using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the Diver (vertical tracking drill). Metres, seconds, m/s.</summary>
    public sealed class DiverSettings
    {
        /// <summary>The body; the two wings either side of it are the weak points.</summary>
        public float BodyRadius { get; set; } = 0.45f;
        public float WingRadius { get; set; } = 0.38f;
        /// <summary>How far each wing's centre sits from the body's centre.</summary>
        public float WingSpan { get; set; } = 0.85f;
        /// <summary>It circles this high above its home, on a ring this wide, at this speed.</summary>
        public float CircleHeight { get; set; } = 10f;
        public float CircleRadius { get; set; } = 7f;
        public float CircleSpeed { get; set; } = 7f;
        /// <summary>Seconds of circling between dives.</summary>
        public float MinCircleTime { get; set; } = 3.5f;
        public float MaxCircleTime { get; set; } = 5.5f;
        /// <summary>It hangs still and screeches this long before diving (the warning).</summary>
        public float WarnTime { get; set; } = 0.7f;
        public float DiveSpeed { get; set; } = 20f;
        public float ClimbSpeed { get; set; } = 9f;
        /// <summary>It dives at players within this distance of where it is.</summary>
        public float DiveRange { get; set; } = 26f;
        /// <summary>A dive that passes this close to its target's body hits them.</summary>
        public float StrikeRadius { get; set; } = 1.4f;
        public float Damage { get; set; } = 10f;
        /// <summary>The Phasing elite blinks to a new spot on its ring this often instead of flying round it.</summary>
        public float BlinkInterval { get; set; } = 1.3f;
    }

    public enum DiverPhase : byte
    {
        Circle = 0,
        /// <summary>Hanging still before a dive (drawn as a glow).</summary>
        Warn = 1,
        Dive = 2,
        Climb = 3,
    }

    public enum DiverEventKind : byte
    {
        None = 0,
        /// <summary>It picked a target and is about to dive.</summary>
        Warned,
        /// <summary>The dive hit its target: apply <see cref="DiverSettings.Damage"/>.</summary>
        Struck,
        /// <summary>The dive missed.</summary>
        Missed,
    }

    public struct DiverEvent
    {
        public DiverEventKind Kind;
        public int TargetId;
    }

    /// <summary>
    /// A Diver circles high over its home, then picks the nearest player it can see, hangs still for a moment
    /// (the warning) and dives at where they stood. A dive that passes close enough hits them; either way it climbs
    /// back up and circles again. Its wings are the weak points. The Phasing elite blinks round its ring instead of
    /// flying. Position is the body centre. A plain struct for ECS.
    /// </summary>
    public struct DiverBehavior
    {
        public Vector3 Home;
        public Vector3 Position;
        /// <summary>Unit direction it is flying (for drawing and its wings).</summary>
        public Vector3 Heading;
        public DiverPhase Phase;
        public float PhaseTime;
        public float Angle;
        public int TargetId;
        public Vector3 DiveAt;
        public bool Phasing;
        public float BlinkTime;
        public CombatRandom Random;

        public static DiverBehavior Create(Vector3 home, uint seed, DiverSettings s, bool phasing = false)
        {
            var d = new DiverBehavior { Home = home, Phasing = phasing, Random = new CombatRandom(seed == 0 ? 1u : seed), Heading = Vector3.UnitZ };
            d.Angle = d.Random.Angle();
            d.Position = d.RingPoint(s);
            d.PhaseTime = d.Random.Range(s.MinCircleTime, s.MaxCircleTime);
            d.BlinkTime = s.BlinkInterval;
            return d;
        }

        public bool Telegraph => Phase == DiverPhase.Warn;

        /// <summary>The wing on the right (+1) or left (-1) of its heading.</summary>
        public Vector3 Wing(int side, DiverSettings s)
        {
            var flat = new Vector3(Heading.X, 0f, Heading.Z);
            if (flat.LengthSquared() < 1e-6f) flat = Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Vector3.Normalize(flat)));
            return Position - right * (side * s.WingSpan);
        }

        public HitTarget ToHitTarget(int id, DiverSettings s)
        {
            var t = HitTarget.WithWeakPoint(id, Position, s.BodyRadius, Wing(1, s), s.WingRadius);
            t.HasSecondWeakPoint = true;
            t.SecondWeakPointCenter = Wing(-1, s);
            return t;
        }

        /// <summary>
        /// One tick. <paramref name="players"/> are the players it may dive at (positions are body centres);
        /// <paramref name="canSee"/> tests a line of sight (null = always). <paramref name="blinked"/> is true when the
        /// Phasing elite jumped this tick.
        /// </summary>
        public DiverEvent Step(float dt, IReadOnlyList<AttackCandidate> players, DiverSettings s, out bool blinked,
            Func<Vector3, Vector3, bool> canSee = null, float moveMultiplier = 1f)
        {
            blinked = false;
            float move = dt * moveMultiplier;
            switch (Phase)
            {
                case DiverPhase.Circle:
                {
                    if (Phasing)
                    {
                        BlinkTime -= move;
                        if (BlinkTime <= 0f)
                        {
                            BlinkTime += s.BlinkInterval;
                            Angle += Random.Range(1.2f, 2.4f);
                            blinked = true;
                        }
                    }
                    else Angle += move * s.CircleSpeed / Math.Max(0.1f, s.CircleRadius);
                    var next = RingPoint(s);
                    if (!blinked && Vector3.DistanceSquared(next, Position) > 1e-6f) Heading = Vector3.Normalize(next - Position);
                    Position = next;
                    PhaseTime -= dt;
                    if (PhaseTime > 0f) return default;
                    int target = EnemyAttack.Nearest(Position, players, s.DiveRange, canSee);
                    if (target < 0)
                    {
                        PhaseTime = 0.5f;
                        return default;
                    }
                    Phase = DiverPhase.Warn;
                    PhaseTime = s.WarnTime;
                    TargetId = players[target].Id;
                    return new DiverEvent { Kind = DiverEventKind.Warned, TargetId = TargetId };
                }
                case DiverPhase.Warn:
                {
                    PhaseTime -= dt;
                    if (PhaseTime > 0f) return default;
                    if (!TryFind(players, TargetId, out var p))
                    {
                        Phase = DiverPhase.Climb;
                        return new DiverEvent { Kind = DiverEventKind.Missed, TargetId = TargetId };
                    }
                    Phase = DiverPhase.Dive;
                    DiveAt = p.Position;
                    if (Vector3.DistanceSquared(DiveAt, Position) > 1e-6f) Heading = Vector3.Normalize(DiveAt - Position);
                    return default;
                }
                case DiverPhase.Dive:
                {
                    Position += Heading * (s.DiveSpeed * move);
                    if (TryFind(players, TargetId, out var p) && Vector3.DistanceSquared(p.Position, Position) <= s.StrikeRadius * s.StrikeRadius)
                    {
                        Phase = DiverPhase.Climb;
                        return new DiverEvent { Kind = DiverEventKind.Struck, TargetId = TargetId };
                    }
                    // Past where they stood (or near the ground under it): pull up.
                    if (Vector3.Dot(DiveAt - Position, Heading) < -0.5f || Position.Y < DiveAt.Y - 0.8f)
                    {
                        Phase = DiverPhase.Climb;
                        return new DiverEvent { Kind = DiverEventKind.Missed, TargetId = TargetId };
                    }
                    return default;
                }
                default:
                {
                    var ring = RingPoint(s);
                    var to = ring - Position;
                    float d = to.Length();
                    float step = s.ClimbSpeed * move;
                    if (d <= Math.Max(step, 0.5f))
                    {
                        Position = ring;
                        Phase = DiverPhase.Circle;
                        PhaseTime = Random.Range(s.MinCircleTime, s.MaxCircleTime);
                        return default;
                    }
                    Heading = to / d;
                    Position += Heading * step;
                    return default;
                }
            }
        }

        /// <summary>Stops a dive early (it hit the ground or a wall): it climbs back up.</summary>
        public void PullUp()
        {
            if (Phase == DiverPhase.Dive) Phase = DiverPhase.Climb;
        }

        private static bool TryFind(IReadOnlyList<AttackCandidate> players, int id, out AttackCandidate found)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Id != id || !players[i].CanBeAttacked) continue;
                found = players[i];
                return true;
            }
            found = default;
            return false;
        }

        private Vector3 RingPoint(DiverSettings s) =>
            Home + new Vector3((float)Math.Cos(Angle) * s.CircleRadius, s.CircleHeight, (float)Math.Sin(Angle) * s.CircleRadius);
    }
}
