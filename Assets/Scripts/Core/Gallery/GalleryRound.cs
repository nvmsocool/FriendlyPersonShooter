using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Gallery
{
    /// <summary>What a shooting gallery is for. Each kind has its own rule, time and target count (see <see cref="GalleryDifficulty"/>).</summary>
    public enum GalleryKind : byte
    {
        None = 0,
        /// <summary>A new player's starting gun: three gun cards, no time limit.</summary>
        Starter = 1,
        /// <summary>A loot box: three item cards, one shot.</summary>
        Box = 2,
        /// <summary>A boss reward chest: three rare-or-better item cards, one shot.</summary>
        Reward = 3,
        /// <summary>Claiming a dropped essence cache.</summary>
        Cache = 4,
        /// <summary>Starting the group's move up a layer (the up-portal).</summary>
        Travel = 7,
    }

    /// <summary>How a gallery is won.</summary>
    public enum GalleryRule : byte
    {
        /// <summary>Hit any one target; which one is the choice (cards).</summary>
        Choose = 0,
        /// <summary>Hit every target in order (they're numbered); a wrong one does nothing.</summary>
        Ordered = 1,
        /// <summary>Targets come one at a time, each with its own short window; every hit and miss counts.</summary>
        Sequence = 2,
        /// <summary>Hit every target, in any order.</summary>
        All = 3,
    }

    public enum GalleryOutcome : byte
    {
        Running = 0,
        Won = 1,
        Lost = 2,
    }

    /// <summary>The numbers for one gallery: rule, targets, time and how hard they are to hit.</summary>
    public readonly struct GallerySpec
    {
        public readonly GalleryKind Kind;
        public readonly GalleryRule Rule;
        public readonly int Targets;
        /// <summary>Seconds for the whole gallery (Choose and Ordered); 0 = no limit.</summary>
        public readonly float Seconds;
        /// <summary>Seconds each target of a <see cref="GalleryRule.Sequence"/> stays up.</summary>
        public readonly float PerTarget;
        /// <summary>A missed shot ends the gallery (lost).</summary>
        public readonly bool OneShot;
        /// <summary>Target radius in degrees (angular, as seen from the eye).</summary>
        public readonly float Radius;
        /// <summary>How fast targets move, degrees per second (at their fastest).</summary>
        public readonly float Speed;

        public GallerySpec(GalleryKind kind, GalleryRule rule, int targets, float seconds, float perTarget, bool oneShot, float radius, float speed)
        {
            Kind = kind;
            Rule = rule;
            Targets = targets;
            Seconds = seconds;
            PerTarget = perTarget;
            OneShot = oneShot;
            Radius = radius;
            Speed = speed;
        }

        /// <summary>Total time the gallery can run (0 = no limit).</summary>
        public float Duration => Rule == GalleryRule.Sequence ? PerTarget * Targets : Seconds;
    }

    /// <summary>What one gallery shot did.</summary>
    public readonly struct GalleryShot
    {
        /// <summary>The target hit, or -1.</summary>
        public readonly int Target;
        /// <summary>The hit counted (a wrong target of an Ordered gallery doesn't).</summary>
        public readonly bool Counted;

        public GalleryShot(int target, bool counted)
        {
            Target = target;
            Counted = counted;
        }

        public bool Hit => Target >= 0;
    }

    /// <summary>
    /// One shooting gallery: targets that drift around the direction the player faced when it opened, the shots at
    /// them and the result. Everything follows from the spec, the seed and the time since it opened, so the host
    /// (which judges the shots) and the player's screen (which draws the targets) agree without sending positions.
    /// Angles are degrees; target positions are offsets (yaw, pitch) from the opening aim.
    /// </summary>
    public sealed class GalleryRound
    {
        /// <summary>Targets stay within this many degrees of the opening aim, sideways and up/down.</summary>
        public const float MaxYaw = 34f, MaxPitch = 19f;

        private readonly Vector2[] _base, _amp, _freq, _phase;

        public GallerySpec Spec { get; }
        public uint Seed { get; }
        public GalleryOutcome Outcome { get; private set; }
        /// <summary>Choose: the target that won. Otherwise -1.</summary>
        public int Chosen { get; private set; } = -1;
        /// <summary>Targets hit so far (bit per target, first 31 targets).</summary>
        public int Hits { get; private set; }
        /// <summary>How many targets have been hit.</summary>
        public int HitCount { get; private set; }
        /// <summary>Number of targets.</summary>
        public int Count => _base.Length;
        /// <summary>Ordered: the next target to hit. Sequence: the target that's up.</summary>
        public int Next { get; private set; }
        /// <summary>Sequence: when the current target came up (seconds since the start).</summary>
        public float NextSince { get; private set; }
        /// <summary>When the gallery ended (seconds since the start), once it has.</summary>
        public float EndedAt { get; private set; }

        public GalleryRound(GallerySpec spec, uint seed)
        {
            Spec = spec;
            Seed = seed;
            int n = Math.Max(1, spec.Targets);
            _base = new Vector2[n];
            _amp = new Vector2[n];
            _freq = new Vector2[n];
            _phase = new Vector2[n];
            var random = new CombatRandom(seed | 1u);
            for (int i = 0; i < n; i++)
            {
                _base[i] = BasePosition(spec, i, n, ref random);
                float ax = random.Range(5f, 9f), ay = random.Range(3f, 6f);
                _amp[i] = new Vector2(ax, ay);
                // Lissajous: each axis swings at its own rate; the faster axis sets the top speed.
                float wx = spec.Speed / ax * random.Range(0.75f, 1f);
                float wy = spec.Speed / ay * random.Range(0.45f, 0.7f);
                _freq[i] = new Vector2(wx, wy);
                _phase[i] = new Vector2(random.Angle(), random.Angle());
            }
        }

        private static Vector2 BasePosition(GallerySpec spec, int i, int n, ref CombatRandom random)
        {
            switch (spec.Rule)
            {
                case GalleryRule.Choose when n > 1:
                    // Cards in a row, spread across the view.
                    return new Vector2((i - (n - 1) * 0.5f) * 22f, random.Range(-5f, 5f));
                case GalleryRule.Ordered:
                case GalleryRule.All:
                {
                    // Around a ring, so the order has to be found.
                    float a = random.Angle();
                    float r = random.Range(9f, 15f);
                    return new Vector2(MathF.Cos(a) * r * 1.4f, MathF.Sin(a) * r * 0.8f);
                }
                default:
                    return new Vector2(random.Range(-16f, 16f), random.Range(-8f, 8f));
            }
        }

        public bool Over => Outcome != GalleryOutcome.Running;

        /// <summary>Seconds left (no limit = infinity), at <paramref name="t"/> seconds since the start.</summary>
        public float TimeLeft(float t)
        {
            float d = Spec.Duration;
            return d <= 0f ? float.PositiveInfinity : Math.Max(0f, d - t);
        }

        /// <summary>Sequence: seconds the current target has left.</summary>
        public float TargetTimeLeft(float t) => Math.Max(0f, Spec.PerTarget - (t - NextSince));

        /// <summary>Where target <paramref name="i"/> is at <paramref name="t"/> (offset from the opening aim, degrees).</summary>
        public Vector2 Offset(int i, float t)
        {
            var b = _base[i];
            var p = new Vector2(
                b.X + _amp[i].X * MathF.Sin(_freq[i].X * t + _phase[i].X),
                b.Y + _amp[i].Y * MathF.Sin(_freq[i].Y * t + _phase[i].Y));
            return new Vector2(Math.Clamp(p.X, -MaxYaw, MaxYaw), Math.Clamp(p.Y, -MaxPitch, MaxPitch));
        }

        /// <summary>Is target <paramref name="i"/> up (drawn and shootable) at <paramref name="t"/>?</summary>
        public bool Visible(int i, float t)
        {
            if (Over || i < 0 || i >= _base.Length) return false;
            switch (Spec.Rule)
            {
                case GalleryRule.Ordered:
                case GalleryRule.All: return (Hits & (1 << i)) == 0;
                case GalleryRule.Sequence: return i == Next && t >= NextSince;
                default: return true;
            }
        }

        /// <summary>
        /// Runs the clock to <paramref name="t"/>: the time limit, and Sequence targets running out (each one missed is
        /// added to <paramref name="missed"/>).
        /// </summary>
        public void Step(float t, List<int> missed = null)
        {
            if (Over) return;
            if (Spec.Rule == GalleryRule.Sequence)
            {
                while (!Over && t - NextSince >= Spec.PerTarget)
                {
                    missed?.Add(Next);
                    Advance(NextSince + Spec.PerTarget);
                }
                return;
            }
            if (Spec.Seconds > 0f && t >= Spec.Seconds) End(GalleryOutcome.Lost, Spec.Seconds);
        }

        /// <summary>
        /// A shot with aim offset (<paramref name="yaw"/>, <paramref name="pitch"/>) from the opening aim, at time
        /// <paramref name="t"/> (call <see cref="Step"/> first). <paramref name="aimPitch"/> is the shot's real pitch,
        /// which narrows sideways distances near the poles.
        /// </summary>
        public GalleryShot Shoot(float yaw, float pitch, float aimPitch, float t)
        {
            if (Over) return new GalleryShot(-1, false);
            int best = -1;
            float bestD = float.MaxValue;
            float squeeze = MathF.Cos(aimPitch * MathF.PI / 180f);
            for (int i = 0; i < _base.Length; i++)
            {
                if (!Visible(i, t)) continue;
                var o = Offset(i, t);
                float dx = (yaw - o.X) * squeeze, dy = pitch - o.Y;
                float d = dx * dx + dy * dy;
                if (d > Spec.Radius * Spec.Radius || d >= bestD) continue;
                bestD = d;
                best = i;
            }
            if (best < 0)
            {
                if (Spec.OneShot) End(GalleryOutcome.Lost, t);
                return new GalleryShot(-1, false);
            }
            switch (Spec.Rule)
            {
                case GalleryRule.Ordered:
                    if (best != Next) return new GalleryShot(best, false);
                    Mark(best);
                    Next++;
                    if (Next >= _base.Length) End(GalleryOutcome.Won, t);
                    return new GalleryShot(best, true);
                case GalleryRule.All:
                    Mark(best);
                    if (Hits == (1 << _base.Length) - 1) End(GalleryOutcome.Won, t);
                    return new GalleryShot(best, true);
                case GalleryRule.Sequence:
                    Mark(best);
                    Advance(t);
                    return new GalleryShot(best, true);
                default:
                    Mark(best);
                    Chosen = best;
                    End(GalleryOutcome.Won, t);
                    return new GalleryShot(best, true);
            }
        }

        /// <summary>Ends it early (walked away, a raid started): lost.</summary>
        public void Cancel(float t)
        {
            if (!Over) End(GalleryOutcome.Lost, t);
        }

        private void Mark(int i)
        {
            if (i < 31) Hits |= 1 << i;
            HitCount++;
        }

        /// <summary>
        /// Sets the progress the host reported, so a player's screen draws the right targets (it only works out
        /// where they are).
        /// </summary>
        public void Restore(int hits, int hitCount, int next, float nextSince, GalleryOutcome outcome, int chosen, float endedAt)
        {
            Hits = hits;
            HitCount = hitCount;
            Next = next;
            NextSince = nextSince;
            Outcome = outcome;
            Chosen = chosen;
            EndedAt = endedAt;
        }

        private void Advance(float since)
        {
            Next++;
            NextSince = since;
            // A sequence is won by getting through it (the hits and misses are what count).
            if (Next >= _base.Length) End(GalleryOutcome.Won, since);
        }

        private void End(GalleryOutcome outcome, float t)
        {
            Outcome = outcome;
            EndedAt = t;
        }

        /// <summary>
        /// The colour slot (0..2) each of three item cards shows, shuffled by the seed. The loot preview uses the same
        /// seed, so a card's colour before the gallery matches its target in it.
        /// </summary>
        public static int[] ColorOrder(uint seed)
        {
            var order = new[] { 0, 1, 2 };
            var random = new CombatRandom(seed ^ 0xC0105EEDu | 1u);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = random.NextInt(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }
    }
}
