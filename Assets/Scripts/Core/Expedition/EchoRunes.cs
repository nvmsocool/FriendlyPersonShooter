using System;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    public enum RuneHit : byte
    {
        /// <summary>Not counted and not a mistake (the set is showing, done, or the rune was just hit).</summary>
        Ignored,
        Progress,
        /// <summary>This set is done for the round; the other set still has to finish.</summary>
        SetDone,
        RoundDone,
        Solved,
        /// <summary>The wrong rune: this set shows its sequence again.</summary>
        Wrong,
    }

    /// <summary>
    /// Echo Runes: two sets of <see cref="RunesPerSet"/> runes, one on each side of a tall wall that
    /// splits the room. Each set flashes its own sequence, then the group shoots it back. A wrong rune
    /// (or too long a pause) makes that set show its sequence again. When both sets are done the next
    /// round starts, one rune longer, with new sequences. Three rounds. Hitting the rune that was just
    /// hit again is ignored (sequences never repeat a rune back to back), so two players shooting the
    /// same rune don't make a mistake.
    /// </summary>
    public sealed class EchoRunes
    {
        public const int Sets = 2;
        public const int RunesPerSet = 4;
        public const int Rounds = 3;
        public const int MaxLength = 7;
        /// <summary>Each rune of a show is lit this long, then dark for <see cref="ShowGap"/>.</summary>
        public const double ShowLit = 0.6;
        public const double ShowGap = 0.3;
        /// <summary>A short pause before a show, so players can look over.</summary>
        public const double ShowLead = 1.2;
        /// <summary>After the show (or the last hit), this long to hit the next rune.</summary>
        public const double HitWindow = 9.0;

        private readonly int[,] _sequence = new int[Sets, MaxLength];
        private readonly int[] _index = new int[Sets];
        private readonly bool[] _done = new bool[Sets];
        private readonly double[] _showAt = new double[Sets];
        private readonly double[] _lastHitAt = new double[Sets];
        private CombatRandom _random;

        public EchoRunes(uint seed, int players, double now)
        {
            _random = new CombatRandom(seed | 1u);
            BaseLength = players >= 6 ? 4 : 3;
            NewRound(now);
        }

        public int BaseLength { get; }
        public int Round { get; private set; }
        public int Length { get; private set; }
        public bool Solved { get; private set; }
        public int Mistakes { get; private set; }

        public int SequenceAt(int set, int i) => _sequence[set, i];
        public int IndexOf(int set) => _index[set];
        public bool Done(int set) => _done[set];

        public float Progress
        {
            get
            {
                if (Solved) return 1f;
                float within = (float)(_index[0] + _index[1]) / (2 * Length);
                return (Round + within) / Rounds;
            }
        }

        private double ShowLength => ShowLead + Length * (ShowLit + ShowGap);

        /// <summary>True while a set is flashing its sequence (hits on it are ignored).</summary>
        public bool Showing(int set, double now) => !_done[set] && now < _showAt[set] + ShowLength;

        /// <summary>The rune a set has lit right now during its show, or -1.</summary>
        public int Lit(int set, double now)
        {
            if (!Showing(set, now)) return -1;
            double t = now - _showAt[set] - ShowLead;
            if (t < 0) return -1;
            int i = (int)(t / (ShowLit + ShowGap));
            if (i >= Length || t - i * (ShowLit + ShowGap) >= ShowLit) return -1;
            return _sequence[set, i];
        }

        private void NewRound(double now)
        {
            Length = Math.Min(MaxLength, BaseLength + Round);
            for (int s = 0; s < Sets; s++)
            {
                int last = -1;
                for (int i = 0; i < Length; i++)
                {
                    int r = last < 0 ? _random.NextInt(RunesPerSet) : _random.NextInt(RunesPerSet - 1);
                    if (last >= 0 && r >= last) r++;
                    _sequence[s, i] = r;
                    last = r;
                }
                Replay(s, now);
                _done[s] = false;
            }
        }

        private void Replay(int set, double now)
        {
            _index[set] = 0;
            _showAt[set] = now;
            _lastHitAt[set] = now + ShowLength;
        }

        public RuneHit Hit(int set, int rune, double now)
        {
            if (Solved || set < 0 || set >= Sets || rune < 0 || rune >= RunesPerSet) return RuneHit.Ignored;
            if (_done[set] || Showing(set, now)) return RuneHit.Ignored;
            int i = _index[set];
            if (i > 0 && _sequence[set, i - 1] == rune) return RuneHit.Ignored;
            if (_sequence[set, i] != rune)
            {
                Mistakes++;
                Replay(set, now);
                return RuneHit.Wrong;
            }
            _index[set] = i + 1;
            _lastHitAt[set] = now;
            if (_index[set] < Length) return RuneHit.Progress;
            _done[set] = true;
            if (!_done[1 - set]) return RuneHit.SetDone;
            Round++;
            if (Round >= Rounds)
            {
                Solved = true;
                return RuneHit.Solved;
            }
            NewRound(now);
            return RuneHit.RoundDone;
        }

        /// <summary>True when a set waited too long for its next rune and shows its sequence again.</summary>
        public bool Step(double now)
        {
            if (Solved) return false;
            bool any = false;
            for (int s = 0; s < Sets; s++)
            {
                if (_done[s] || Showing(s, now) || now - _lastHitAt[s] <= HitWindow) continue;
                Mistakes++;
                Replay(s, now);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// For the HUD and clients: bits 0-2 length, 3-4 round, then 8 bits per set (3 index, 3 lit rune + 1,
        /// 1 showing, 1 done).
        /// </summary>
        public int Pack(double now)
        {
            int v = Length | (Math.Min(Round, 3) << 3);
            for (int s = 0; s < Sets; s++)
            {
                int b = _index[s] | ((Lit(s, now) + 1) << 3) | ((Showing(s, now) ? 1 : 0) << 6) | ((_done[s] ? 1 : 0) << 7);
                v |= b << (5 + 8 * s);
            }
            return v;
        }

        public static int LengthOf(int packed) => packed & 7;
        public static int RoundOf(int packed) => (packed >> 3) & 3;
        public static int IndexOf(int packed, int set) => (packed >> (5 + 8 * set)) & 7;
        public static int LitOf(int packed, int set) => ((packed >> (8 + 8 * set)) & 7) - 1;
        public static bool ShowingOf(int packed, int set) => ((packed >> (11 + 8 * set)) & 1) != 0;
        public static bool DoneOf(int packed, int set) => ((packed >> (12 + 8 * set)) & 1) != 0;
    }
}
