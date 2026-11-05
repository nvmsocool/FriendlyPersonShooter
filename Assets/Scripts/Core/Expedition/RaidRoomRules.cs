using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    public enum LockHit : byte
    {
        /// <summary>Not counted and not a mistake (the crystal just lit, or the room is solved).</summary>
        Ignored,
        /// <summary>The right crystal, but the same player as the last hit: someone else has to.</summary>
        SamePlayer,
        Progress,
        RoundDone,
        Solved,
        /// <summary>The wrong crystal: the sequence starts over.</summary>
        Wrong,
    }

    /// <summary>
    /// Color Locks: the altar shows an order of crystals (0 Yellow, 1 Red, 2 Green). Shoot them in that
    /// order, each hit by a different player than the one before (unless only one player is up), with
    /// at most <see cref="HitWindow"/> seconds between hits. A wrong crystal or a pause starts the round
    /// over (same order). Three rounds, each one crystal longer. Hitting the crystal that just lit again
    /// is ignored, so spray from a second player doesn't count as a mistake.
    /// </summary>
    public sealed class ColorLocks
    {
        public const int Crystals = 3;
        public const int Rounds = 3;
        public const double HitWindow = 6.0;
        public const int MaxLength = 7;

        private readonly int[] _sequence = new int[MaxLength];
        private CombatRandom _random;

        public ColorLocks(uint seed, int players)
        {
            _random = new CombatRandom(seed | 1u);
            BaseLength = players >= 6 ? 4 : 3;
            NewSequence();
        }

        public int BaseLength { get; }
        public int Round { get; private set; }
        public int Length { get; private set; }
        /// <summary>How many crystals of this round's order are lit.</summary>
        public int Index { get; private set; }
        public int LastHitter { get; private set; } = -1;
        public double LastHitAt { get; private set; }
        public bool Solved { get; private set; }
        public int Mistakes { get; private set; }

        public int SequenceAt(int i) => _sequence[i];
        public int Next => Solved ? -1 : _sequence[Index];

        public float Progress => Solved ? 1f : (Round + (float)Index / Length) / Rounds;

        private void NewSequence()
        {
            Length = Math.Min(MaxLength, BaseLength + Round);
            int last = -1;
            for (int i = 0; i < Length; i++)
            {
                // Never the same crystal twice in a row.
                int c = last < 0 ? _random.NextInt(Crystals) : _random.NextInt(Crystals - 1);
                if (last >= 0 && c >= last) c++;
                _sequence[i] = c;
                last = c;
            }
            Index = 0;
            LastHitter = -1;
        }

        public LockHit Hit(int crystal, int player, int playersUp, double now)
        {
            if (Solved || crystal < 0 || crystal >= Crystals) return LockHit.Ignored;
            if (Index > 0 && _sequence[Index - 1] == crystal) return LockHit.Ignored;
            if (crystal != _sequence[Index])
            {
                Index = 0;
                LastHitter = -1;
                Mistakes++;
                return LockHit.Wrong;
            }
            if (playersUp > 1 && player == LastHitter) return LockHit.SamePlayer;
            Index++;
            LastHitter = player;
            LastHitAt = now;
            if (Index < Length) return LockHit.Progress;
            Round++;
            if (Round >= Rounds)
            {
                Solved = true;
                return LockHit.Solved;
            }
            NewSequence();
            return LockHit.RoundDone;
        }

        /// <summary>True when a pause too long between hits just started the round over.</summary>
        public bool Step(double now)
        {
            if (Solved || Index == 0 || now - LastHitAt <= HitWindow) return false;
            Index = 0;
            LastHitter = -1;
            Mistakes++;
            return true;
        }

        /// <summary>
        /// The round for the HUD and clients: bits 0-2 length, 3-5 index, 6-7 round, then 2 bits per crystal.
        /// </summary>
        public int Pack()
        {
            int v = Length | (Index << 3) | (Math.Min(Round, 3) << 6);
            for (int i = 0; i < Length; i++) v |= _sequence[i] << (8 + 2 * i);
            return v;
        }

        public static int LengthOf(int packed) => packed & 7;
        public static int IndexOf(int packed) => (packed >> 3) & 7;
        public static int RoundOf(int packed) => (packed >> 6) & 3;
        public static int CrystalOf(int packed, int i) => (packed >> (8 + 2 * i)) & 3;
    }

    /// <summary>
    /// Hold the Plates: about half the group (1-4 plates) must stand on every active plate at once. The
    /// bar fills while all are held and drains slowly while any is empty.
    /// </summary>
    public sealed class HoldPlates
    {
        public const float HoldSeconds = 25f;
        public const float DrainRate = 0.4f;

        public HoldPlates(int players)
        {
            Plates = Math.Max(1, Math.Min(4, (players + 1) / 2));
        }

        public int Plates { get; }
        /// <summary>Seconds held so far.</summary>
        public float Held { get; private set; }
        public bool Solved { get; private set; }
        public float Progress => Solved ? 1f : Held / HoldSeconds;
        /// <summary>Bit i set: plate i has someone on it (from the last step).</summary>
        public int Occupied { get; private set; }

        public bool AllHeld => Occupied == (1 << Plates) - 1;

        /// <summary>Which active plates have someone (up) standing on them; positions relative to the room.</summary>
        public static int OccupancyOf(RaidRoomLayout layout, int plates, IEnumerable<Vector3> standing)
        {
            int mask = 0;
            foreach (var p in standing)
            {
                for (int i = 0; i < plates; i++)
                {
                    var c = layout.Targets[i];
                    float dx = p.X - c.X, dz = p.Z - c.Z;
                    if (dx * dx + dz * dz <= RaidRoomLayout.PlateRadius * RaidRoomLayout.PlateRadius && Math.Abs(p.Y - c.Y) < 2.5f) mask |= 1 << i;
                }
            }
            return mask;
        }

        /// <summary>Returns true the step the bar fills.</summary>
        public bool Step(float dt, int occupied)
        {
            if (Solved) return false;
            Occupied = occupied & ((1 << Plates) - 1);
            Held = AllHeld ? Held + dt : Math.Max(0f, Held - dt * DrainRate);
            if (Held < HoldSeconds) return false;
            Held = HoldSeconds;
            Solved = true;
            return true;
        }
    }

    /// <summary>
    /// Sweeper Gauntlet: low arms turn on some stones (jump them), falling sends you back to the start.
    /// Solved when everyone up stands on the end pad at once.
    /// </summary>
    public sealed class GauntletRun
    {
        public const float SweeperDamage = 6f;
        public const float FallDamage = 5f;
        /// <summary>Gauntlet hits never take a player below this: a mistake costs time, not the run.</summary>
        public const float HealthFloor = 10f;
        public const double HitCooldown = 1.0;
        public const float PlayerRadius = 0.4f;

        private readonly Dictionary<(int Player, int Sweeper), double> _lastHit = new Dictionary<(int, int), double>();

        public GauntletRun(RaidRoomLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public RaidRoomLayout Layout { get; }
        public bool Solved { get; private set; }
        /// <summary>How far the furthest player got (stones of the path), for the HUD.</summary>
        public int Furthest { get; private set; }
        public float Progress => Solved ? 1f : (float)Furthest / (Layout.Path.Count - 1);

        /// <summary>
        /// True if sweeper <paramref name="index"/> swept over a player standing at <paramref name="feet"/>
        /// (relative to the room) between room times <paramref name="t0"/> and <paramref name="t1"/>.
        /// Feet higher than the arm (mid-jump) are safe.
        /// </summary>
        public static bool Swept(RaidSweeper s, Vector3 feet, double t0, double t1)
        {
            if (feet.Y > s.Pivot.Y + RaidRoomLayout.SweeperHeight || feet.Y < s.Pivot.Y - 1.5f) return false;
            float dx = feet.X - s.Pivot.X, dz = feet.Z - s.Pivot.Z;
            float r = (float)Math.Sqrt(dx * dx + dz * dz);
            float reach = RaidRoomLayout.SweeperWidth * 0.5f + PlayerRadius;
            if (r > s.Length + reach) return false;
            // Near the pivot the arm always clips you.
            if (r <= reach) return true;
            float half = (float)Math.Asin(Math.Min(1f, reach / r));
            float theta = (float)Math.Atan2(dx, dz);
            float a0 = s.AngleAt(t0), a1 = s.AngleAt(t1);
            if (a1 < a0) { var tmp = a0; a0 = a1; a1 = tmp; }
            if (a1 - a0 >= 2 * Math.PI) return true;
            // Bring theta into [a0 - half, a0 - half + 2π) and test the swept interval.
            double lo = a0 - half;
            double th = theta - lo;
            th -= 2 * Math.PI * Math.Floor(th / (2 * Math.PI));
            return th <= (a1 - a0) + 2 * half;
        }

        /// <summary>
        /// Advances one check. <paramref name="players"/> are (id, feet relative to the room, up). Adds to
        /// <paramref name="swept"/> the players a sweeper hit and to <paramref name="fell"/> those who fell.
        /// Returns true the step the room is solved.
        /// </summary>
        public bool Step(double t0, double t1, IReadOnlyList<(int Id, Vector3 Feet, bool Up)> players, List<int> swept, List<int> fell)
        {
            if (Solved) return false;
            int up = 0, home = 0;
            var end = Layout.Path[Layout.Path.Count - 1];
            float floor = float.MaxValue;
            foreach (var b in Layout.Path) floor = Math.Min(floor, b.Top);
            foreach (var (id, feet, isUp) in players)
            {
                if (!isUp) continue;
                up++;
                if (feet.Y < floor - RaidRoomLayout.FallDepth)
                {
                    fell.Add(id);
                    continue;
                }
                int stone = Layout.StoneUnder(feet);
                if (stone > Furthest) Furthest = stone;
                if (end.Over(feet.X, feet.Z) && feet.Y > end.Top - 1f) home++;
                for (int i = 0; i < Layout.Sweepers.Count; i++)
                {
                    if (!Swept(Layout.Sweepers[i], feet, t0, t1)) continue;
                    if (_lastHit.TryGetValue((id, i), out var last) && t1 - last < HitCooldown) continue;
                    _lastHit[(id, i)] = t1;
                    swept.Add(id);
                }
            }
            if (up == 0 || home < up) return false;
            Solved = true;
            return true;
        }
    }

    /// <summary>
    /// Wisp Storm: kill <see cref="Target"/> Wisps within <see cref="Duration"/> seconds. Running out of
    /// time starts the count over. The storm's Wisps don't attack: it's an aim check against the clock.
    /// </summary>
    public sealed class WispStorm
    {
        public const double Duration = 90.0;

        public WispStorm(int players, bool swarming, double now)
        {
            Target = 8 + 2 * players + (swarming ? 4 : 0);
            MaxAlive = 3 + players + (swarming ? 3 : 0);
            AttemptStarted = now;
        }

        public int Target { get; }
        public int MaxAlive { get; }
        public int Kills { get; private set; }
        public int Attempts { get; private set; } = 1;
        public double AttemptStarted { get; private set; }
        public bool Solved { get; private set; }
        public float Progress => Solved ? 1f : (float)Kills / Target;

        public double TimeLeft(double now) => Math.Max(0, AttemptStarted + Duration - now);

        /// <summary>Returns true the kill that solves the room.</summary>
        public bool Kill()
        {
            if (Solved) return false;
            Kills++;
            if (Kills < Target) return false;
            Solved = true;
            return true;
        }

        /// <summary>True when time just ran out and the count started over.</summary>
        public bool Step(double now)
        {
            if (Solved || now < AttemptStarted + Duration) return false;
            Kills = 0;
            Attempts++;
            AttemptStarted = now;
            return true;
        }
    }
}
