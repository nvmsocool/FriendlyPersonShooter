using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>A thread the Weaver ties between two players (or a player and itself).</summary>
    public struct WeaverTether
    {
        /// <summary>The Weaver's end of a thread tied to only one player.</summary>
        public const int WeaverEnd = -1;

        public int A;
        public int B;
        public int HitsLeft;
        public double Snaps;

        public bool Holds(int playerId) => A == playerId || B == playerId;
    }

    public enum TetherHit : byte
    {
        /// <summary>Someone tied by this thread shot it: they can't cut their own (unless alone).</summary>
        Refused,
        Hit,
        Cut,
    }

    /// <summary>
    /// The Weaver: a Bulwark that fights over a floor that falls away. The arena floor is a
    /// <see cref="Grid"/> x <see cref="Grid"/> grid of tiles; every <see cref="FallEvery"/> seconds one cracks
    /// (<see cref="CrackSeconds"/> of warning) and drops. Once <see cref="MaxFallen"/> are gone, each new fall
    /// brings the oldest one back. The four tiles under the pillars never fall.
    /// Every <see cref="TetherEvery"/> seconds it ties players together in pairs (an odd one out to itself).
    /// While any thread holds, the Weaver takes only <see cref="ShieldedDamage"/> of damage; a thread needs
    /// <see cref="TetherHits"/> hits from someone it doesn't hold, and one left for <see cref="TetherLife"/>
    /// seconds snaps for <see cref="TetherDamage"/> to both ends.
    /// </summary>
    public sealed class RaidWeaver
    {
        public const int Grid = 8;
        public const float TileSize = 6f;
        public const float Half = Grid * TileSize * 0.5f;
        public const double FirstFall = 8.0;
        public const double FallEvery = 2.5;
        public const double CrackSeconds = 3.0;
        public const int MaxFallen = 30;
        /// <summary>Falling through a hole: back onto a standing tile, stung.</summary>
        public const float FallDamage = 12f;

        public const double FirstTether = 10.0;
        public const double TetherEvery = 15.0;
        public const double TetherLife = 10.0;
        public const int TetherHits = 4;
        public const int MaxTethers = 4;
        public const float TetherDamage = 25f;
        public const float ShieldedDamage = 0.25f;
        /// <summary>A thread's hit radius around the line between its ends.</summary>
        public const float TetherRadius = 0.45f;

        private readonly List<int> _order = new List<int>();
        private readonly Queue<int> _fallenQueue = new Queue<int>();
        private readonly List<WeaverTether> _tethers = new List<WeaverTether>();
        private readonly List<int> _scratch = new List<int>();
        private CombatRandom _random;
        private int _next;
        private double _nextFall;
        private double _nextTether;
        private readonly Dictionary<int, double> _cracks = new Dictionary<int, double>();

        public RaidWeaver(uint seed, int players, double now)
        {
            Players = Math.Max(1, players);
            _random = new CombatRandom(seed * 1103515245u + 12345u | 1u);
            for (int i = 0; i < Grid * Grid; i++) if (!Safe(i)) _order.Add(i);
            for (int i = _order.Count - 1; i > 0; i--)
            {
                int j = _random.NextInt(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            _nextFall = now + FirstFall;
            _nextTether = now + FirstTether;
        }

        public int Players { get; }
        /// <summary>Bit i set while tile i is gone.</summary>
        public ulong Fallen { get; private set; }
        /// <summary>Bit i set while tile i is cracking (it falls soon).</summary>
        public ulong Cracking { get; private set; }
        public int TilesFallen { get; private set; }
        public IReadOnlyList<WeaverTether> Tethers => _tethers;
        public int TethersTied { get; private set; }
        public int TethersCut { get; private set; }
        public int TethersSnapped { get; private set; }
        /// <summary>The damage the Weaver takes: cut by its threads while any hold.</summary>
        public float DamageMultiplier => _tethers.Count > 0 ? ShieldedDamage : 1f;

        // ---------- the floor ----------

        /// <summary>The tiles under the four pillars (at +-10 m), which never fall.</summary>
        public static bool Safe(int tile)
        {
            int gx = tile % Grid, gz = tile / Grid;
            return (gx == 2 || gx == 5) && (gz == 2 || gz == 5);
        }

        /// <summary>The tile under a point (relative to the arena's centre), or -1 off the floor.</summary>
        public static int TileAt(float x, float z)
        {
            int gx = (int)Math.Floor((x + Half) / TileSize), gz = (int)Math.Floor((z + Half) / TileSize);
            if (gx < 0 || gx >= Grid || gz < 0 || gz >= Grid) return -1;
            return gx + gz * Grid;
        }

        /// <summary>A tile's centre on the floor (relative to the arena's centre).</summary>
        public static Vector3 TileCenter(int tile) =>
            new Vector3(-Half + (tile % Grid + 0.5f) * TileSize, 0f, -Half + (tile / Grid + 0.5f) * TileSize);

        public static bool IsSet(ulong mask, int tile) => tile >= 0 && (mask & (1ul << tile)) != 0;

        public bool Standing(int tile) => tile >= 0 && !IsSet(Fallen, tile);

        /// <summary>
        /// Cracks the next tile when it's time and drops tiles whose warning ran out. True when
        /// <see cref="Fallen"/> changed (the caller rebuilds the floor's colliders).
        /// </summary>
        public bool StepFloor(double now)
        {
            bool changed = false;
            if (now >= _nextFall)
            {
                _nextFall = now + FallEvery;
                for (int tries = 0; tries < _order.Count; tries++)
                {
                    int tile = _order[_next];
                    _next = (_next + 1) % _order.Count;
                    if (IsSet(Fallen, tile) || IsSet(Cracking, tile)) continue;
                    Cracking |= 1ul << tile;
                    _cracks[tile] = now + CrackSeconds;
                    break;
                }
            }
            _scratch.Clear();
            foreach (var kv in _cracks) if (now >= kv.Value) _scratch.Add(kv.Key);
            foreach (int tile in _scratch)
            {
                _cracks.Remove(tile);
                Cracking &= ~(1ul << tile);
                if (_fallenQueue.Count >= MaxFallen)
                {
                    int back = _fallenQueue.Dequeue();
                    Fallen &= ~(1ul << back);
                }
                Fallen |= 1ul << tile;
                _fallenQueue.Enqueue(tile);
                TilesFallen++;
                changed = true;
            }
            return changed;
        }

        /// <summary>The standing, uncracked tile nearest a point (for someone who fell through).</summary>
        public int NearestSafeTile(Vector3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < Grid * Grid; i++)
            {
                if (IsSet(Fallen, i) || IsSet(Cracking, i)) continue;
                var c = TileCenter(i);
                float d = (c.X - p.X) * (c.X - p.X) + (c.Z - p.Z) * (c.Z - p.Z);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        // ---------- the threads ----------

        /// <summary>
        /// Drops threads whose players are no longer up, snaps the ones left too long (into
        /// <paramref name="snapped"/>), and ties new ones when it's time. Returns how many were tied.
        /// </summary>
        public int StepTethers(double now, IReadOnlyList<int> up, List<WeaverTether> snapped)
        {
            for (int i = _tethers.Count - 1; i >= 0; i--)
            {
                var t = _tethers[i];
                bool aUp = Contains(up, t.A), bUp = t.B == WeaverTether.WeaverEnd || Contains(up, t.B);
                if (!aUp || !bUp)
                {
                    _tethers.RemoveAt(i);
                    continue;
                }
                if (now < t.Snaps) continue;
                snapped.Add(t);
                TethersSnapped++;
                _tethers.RemoveAt(i);
            }
            if (now < _nextTether || _tethers.Count > 0 || up.Count == 0) return 0;
            _nextTether = now + TetherEvery;
            _scratch.Clear();
            for (int i = 0; i < up.Count; i++) _scratch.Add(up[i]);
            for (int i = _scratch.Count - 1; i > 0; i--)
            {
                int j = _random.NextInt(i + 1);
                (_scratch[i], _scratch[j]) = (_scratch[j], _scratch[i]);
            }
            int tied = 0;
            for (int i = 0; i < _scratch.Count && _tethers.Count < MaxTethers; i += 2)
            {
                int b = i + 1 < _scratch.Count ? _scratch[i + 1] : WeaverTether.WeaverEnd;
                _tethers.Add(new WeaverTether { A = _scratch[i], B = b, HitsLeft = TetherHits, Snaps = now + TetherLife });
                tied++;
            }
            TethersTied += tied;
            return tied;
        }

        private static bool Contains(IReadOnlyList<int> list, int id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return true;
            return false;
        }

        /// <summary>A shot landed on thread <paramref name="index"/>.</summary>
        public TetherHit HitTether(int index, int shooterId)
        {
            var t = _tethers[index];
            if (Players > 1 && t.Holds(shooterId)) return TetherHit.Refused;
            t.HitsLeft--;
            if (t.HitsLeft > 0)
            {
                _tethers[index] = t;
                return TetherHit.Hit;
            }
            _tethers.RemoveAt(index);
            TethersCut++;
            return TetherHit.Cut;
        }

        /// <summary>
        /// For clients: up to <see cref="MaxTethers"/> threads, 16 bits each (bits 0-5 end A's id, 6-11 end B's
        /// id or 63 for the Weaver, 12-14 hits left, 15 set).
        /// </summary>
        public ulong PackTethers()
        {
            ulong packed = 0;
            for (int i = 0; i < _tethers.Count && i < MaxTethers; i++)
            {
                var t = _tethers[i];
                ulong a = (ulong)(t.A & 63);
                ulong b = t.B == WeaverTether.WeaverEnd ? 63ul : (ulong)(t.B & 63);
                ulong one = a | (b << 6) | ((ulong)Math.Min(7, t.HitsLeft) << 12) | (1ul << 15);
                packed |= one << (16 * i);
            }
            return packed;
        }

        /// <summary>Thread <paramref name="i"/> from <see cref="PackTethers"/>; false if there is none.</summary>
        public static bool UnpackTether(ulong packed, int i, out int a, out int b, out int hitsLeft)
        {
            ulong one = (packed >> (16 * i)) & 0xFFFF;
            a = (int)(one & 63);
            int rawB = (int)((one >> 6) & 63);
            b = rawB == 63 ? WeaverTether.WeaverEnd : rawB;
            hitsLeft = (int)((one >> 12) & 7);
            return (one & (1ul << 15)) != 0;
        }
    }
}
