using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the world boss. Metres, seconds, hit points.</summary>
    public sealed class ColossusSettings
    {
        /// <summary>The shell (a big sphere) and the crystal core on top of it.</summary>
        public float BodyRadius { get; set; } = 6f;
        public float CoreRadius { get; set; } = 2f;
        public float HoverHeight { get; set; } = 9f;
        public float BobHeight { get; set; } = 0.4f;
        /// <summary>Damage share of every hit while it isn't stunned.</summary>
        public float ArmorMultiplier { get; set; } = 0.2f;
        public float TurnSpeed { get; set; } = 30f;

        /// <summary>Hovering: a volley of bolts every so often at the nearest player it can see.</summary>
        public float HoverTime { get; set; } = 9f;
        public float VolleyEvery { get; set; } = 2.4f;
        public int VolleyBolts { get; set; } = 3;
        /// <summary>Side-to-side gap between a volley's bolts at the target (m), so only a real sidestep clears all of them.</summary>
        public float VolleySpread { get; set; } = 2.2f;
        public float VolleyRange { get; set; } = 70f;
        public float BoltSpeed { get; set; } = 17f;
        public float BoltRadius { get; set; } = 0.7f;
        public float BoltDamage { get; set; } = 12f;

        /// <summary>The slam: it rises (the warning), drops, and sends out a ring along the ground; jump it.</summary>
        public float RiseTime { get; set; } = 1.6f;
        public float RiseHeight { get; set; } = 5f;
        public float SlamTime { get; set; } = 0.3f;
        public float WaveSpeed { get; set; } = 13f;
        public float WaveRange { get; set; } = 36f;
        public float WaveWidth { get; set; } = 1.4f;
        public float WaveDamage { get; set; } = 22f;

        /// <summary>After a slam it sits stunned with its armor open: hits do full damage, the core counts as a weak point.</summary>
        public float StunTime { get; set; } = 5f;

        /// <summary>Health fractions at which it calls in adds.</summary>
        public float[] AddWaveAt { get; set; } = { 0.75f, 0.5f, 0.25f };
    }

    public enum ColossusPhase : byte
    {
        Hover = 0,
        /// <summary>Rising before a slam (the warning).</summary>
        Rise = 1,
        Slam = 2,
        /// <summary>On the ground after a slam, armor open.</summary>
        Stunned = 3,
    }

    [Flags]
    public enum ColossusEvent : byte
    {
        None = 0,
        /// <summary>Fire a volley at the nearest player in sight.</summary>
        Volley = 1,
        /// <summary>It hit the ground: start a shockwave at <see cref="ColossusBehavior.Home"/>.</summary>
        Slammed = 2,
    }

    /// <summary>
    /// The world boss (the Colossus). Unlike the raid's Bulwark (a turning plate you circle to hit), it
    /// hovers firing volleys you sidestep, then rises, slams down and sends a shockwave along the ground
    /// that you jump over. After each slam it sits stunned with its armor open: that's when hits count.
    /// Hits at any other time do a fifth of their damage. A plain struct for ECS.
    /// </summary>
    public struct ColossusBehavior
    {
        /// <summary>The ground under it.</summary>
        public Vector3 Home;
        /// <summary>Centre of the shell.</summary>
        public Vector3 Position;
        public float Yaw;
        public ColossusPhase Phase;
        public float PhaseTime;
        public float NextVolley;
        public float Age;
        public int WavesCalled;

        public static ColossusBehavior Create(Vector3 home, float yaw, ColossusSettings s) => new ColossusBehavior
        {
            Home = home,
            Position = home + new Vector3(0f, s.HoverHeight, 0f),
            Yaw = yaw,
            Phase = ColossusPhase.Hover,
            NextVolley = s.VolleyEvery,
        };

        /// <summary>Armor open: hits do full damage.</summary>
        public bool Exposed => Phase == ColossusPhase.Stunned;
        /// <summary>Rising for a slam: get ready to jump.</summary>
        public bool Warning => Phase == ColossusPhase.Rise || Phase == ColossusPhase.Slam;

        public float DamageMultiplier(ColossusSettings s) => Exposed ? 1f : s.ArmorMultiplier;

        public Vector3 CoreCenter(ColossusSettings s) => CoreCenterOf(Position, s);

        public static Vector3 CoreCenterOf(Vector3 position, ColossusSettings s) => position + new Vector3(0f, s.BodyRadius * 0.85f, 0f);

        /// <summary>
        /// One tick. <paramref name="face"/> is a point to turn toward (the nearest player), if any; Slow
        /// (<paramref name="moveMultiplier"/>) slows its turning and stretches its hover.
        /// </summary>
        public ColossusEvent Step(float dt, float moveMultiplier, Vector3? face, ColossusSettings s)
        {
            Age += dt;
            PhaseTime += dt * Math.Max(0.1f, moveMultiplier);
            var events = ColossusEvent.None;
            if (face.HasValue && Phase != ColossusPhase.Stunned)
            {
                var d = face.Value - Position;
                float want = MathF.Atan2(d.X, d.Z) * 180f / MathF.PI;
                float delta = ((want - Yaw) % 360f + 540f) % 360f - 180f;
                float turn = s.TurnSpeed * moveMultiplier * dt;
                Yaw += Math.Clamp(delta, -turn, turn);
            }

            float bob = s.BobHeight * MathF.Sin(Age * 1.7f);
            float hover = s.HoverHeight + bob;
            float ground = s.BodyRadius * 0.6f;
            switch (Phase)
            {
                case ColossusPhase.Hover:
                {
                    // Coming back up from a slam over the first second.
                    float lift = Math.Min(1f, PhaseTime);
                    SetHeight(ground + (hover - ground) * lift);
                    NextVolley -= dt;
                    if (NextVolley <= 0f && PhaseTime < s.HoverTime - 0.5f)
                    {
                        NextVolley = s.VolleyEvery;
                        events |= ColossusEvent.Volley;
                    }
                    if (PhaseTime >= s.HoverTime) Enter(ColossusPhase.Rise);
                    break;
                }
                case ColossusPhase.Rise:
                    SetHeight(hover + s.RiseHeight * Math.Min(1f, PhaseTime / s.RiseTime));
                    if (PhaseTime >= s.RiseTime) Enter(ColossusPhase.Slam);
                    break;
                case ColossusPhase.Slam:
                {
                    float top = s.HoverHeight + s.RiseHeight;
                    SetHeight(top + (ground - top) * Math.Min(1f, PhaseTime / s.SlamTime));
                    if (PhaseTime >= s.SlamTime)
                    {
                        events |= ColossusEvent.Slammed;
                        Enter(ColossusPhase.Stunned);
                    }
                    break;
                }
                default:
                    SetHeight(ground);
                    if (PhaseTime >= s.StunTime)
                    {
                        Enter(ColossusPhase.Hover);
                        NextVolley = s.VolleyEvery;
                    }
                    break;
            }
            return events;
        }

        /// <summary>The add wave due now (1, 2 or 3) if health just fell past the next threshold, else 0.</summary>
        public int TakeWaveDue(float healthFraction, ColossusSettings s)
        {
            if (WavesCalled >= s.AddWaveAt.Length || healthFraction > s.AddWaveAt[WavesCalled]) return 0;
            return ++WavesCalled;
        }

        /// <summary>
        /// Where a volley's bolts aim: the target and points beside it, across the line from the boss.
        /// </summary>
        public static List<Vector3> VolleyAims(Vector3 from, Vector3 target, ColossusSettings s)
        {
            var aims = new List<Vector3>();
            var d = target - from;
            var side = Vector3.Normalize(new Vector3(d.Z, 0f, -d.X));
            if (float.IsNaN(side.X)) side = Vector3.UnitX;
            int n = Math.Max(1, s.VolleyBolts);
            for (int i = 0; i < n; i++)
            {
                float k = i - (n - 1) * 0.5f;
                aims.Add(target + side * (k * s.VolleySpread));
            }
            return aims;
        }

        private void Enter(ColossusPhase phase)
        {
            Phase = phase;
            PhaseTime = 0f;
        }

        private void SetHeight(float h) => Position = new Vector3(Home.X, Home.Y + h, Home.Z);
    }

    /// <summary>A player as a shockwave sees them: where they stand and whether their feet are on the ground.</summary>
    public readonly struct WaveTarget
    {
        public readonly int Id;
        /// <summary>Feet position.</summary>
        public readonly Vector3 Feet;
        public readonly bool Grounded;
        public readonly bool CanBeHit;

        public WaveTarget(int id, Vector3 feet, bool grounded, bool canBeHit = true)
        {
            Id = id;
            Feet = feet;
            Grounded = grounded;
            CanBeHit = canBeHit;
        }
    }

    /// <summary>
    /// A ring that runs out along the ground from a slam. It hits each player once as its edge passes
    /// their feet, unless they're in the air (jump it) or well above or below the slam (on a ledge).
    /// </summary>
    public sealed class Shockwave
    {
        /// <summary>Players more than this far above or below the slam point are out of its reach.</summary>
        public const float HeightReach = 4f;

        private readonly HashSet<int> _hit = new HashSet<int>();

        public Shockwave(int id, Vector3 center, float speed, float range, float width, float damage)
        {
            Id = id;
            Center = center;
            Speed = speed;
            Range = range;
            Width = width;
            Damage = damage;
        }

        public int Id { get; }
        public Vector3 Center { get; }
        public float Speed { get; }
        public float Range { get; }
        public float Width { get; }
        public float Damage { get; }
        public float Age { get; private set; }
        public float Radius => Age * Speed;
        public bool Done => Radius > Range + Width;

        /// <summary>Advances the ring and adds the ids of players its edge caught this step.</summary>
        public void Step(float dt, IReadOnlyList<WaveTarget> players, List<int> caught)
        {
            float before = Radius;
            Age += dt;
            float after = Math.Min(Radius, Range);
            foreach (var p in players)
            {
                if (!p.CanBeHit || _hit.Contains(p.Id)) continue;
                var flat = new Vector2(p.Feet.X - Center.X, p.Feet.Z - Center.Z);
                float d = flat.Length();
                // Its edge (a band Width wide) swept over them this step.
                if (d < before - Width * 0.5f || d > after + Width * 0.5f) continue;
                if (Math.Abs(p.Feet.Y - Center.Y) > HeightReach) continue;
                // Jumped it: they're in the air while the edge passes (don't count them as dodged twice).
                if (!p.Grounded) continue;
                _hit.Add(p.Id);
                caught.Add(p.Id);
            }
        }
    }
}
